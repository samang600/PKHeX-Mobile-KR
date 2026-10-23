"""Build the embedded PokeLDN CPython runtime and native iOS frameworks (Generated/).

Run on macOS with Xcode and Python 3.13 (GitHub Actions ios.yml does this automatically):
    python3 PKHeXKR/Platforms/iOS/PokeLDN/build_ios_runtime.py

Single source of truth: the PokeLDN Python code (pokeldn, ldn, bin, kr_*.py) and the pure-Python
packages are taken from the Android LDN library (Platforms/Android/ldn/pokeldn-trade-kr.aar),
so iOS and Android always run the same code. Only the iOS host files (RuntimeSource/ios_*.py)
live here. Native modules (pycryptodome, zstandard) are compiled for iphoneos from verified sdists.

Size: device-only Python.xcframework (no simulator slice / duplicate stdlib), stdlib without
tests/IDLE/Tk, packages without tests, stripped extension modules, and no work files left behind.
Generated/ and Support/ are build output - never commit them or put them in source zips.

    --stage-only   extract and trim the Python files only (works on any OS; for checking sizes)
"""

from __future__ import annotations

import ast
import hashlib
import io
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import zipfile


HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[2]                       # PKHeXKR/
AAR = PROJECT / "Platforms" / "Android" / "ldn" / "pokeldn-trade-kr.aar"
PKG = HERE / "Generated"
RES = PKG / "Resources"
FRAMEWORKS = PKG / "Frameworks"
SUPPORT = HERE / "Support"                      # download cache (CI caches this folder)
WORK = SUPPORT / "work"
PYTHON_RELEASE = "3.13-b16"
PYTHON_ARCHIVE = "Python-3.13-iOS-support.b16.tar.gz"
PYTHON_URL = f"https://github.com/beeware/Python-Apple-support/releases/download/{PYTHON_RELEASE}/{PYTHON_ARCHIVE}"
PYTHON_SHA256 = "22b96f3d0c3663225f078556b036d2ee6ce3ff98c885706585240681335c71ea"
MIN_IOS = "15.0"
NATIVE_PACKAGES = {
    "pycryptodome==3.21.0": "f7787e0d469bdae763b876174cf2e6c0f7be79808af26b1da96f1a64bcf47297",
    "zstandard==0.23.0": "b2d8c62d08e7255f68f7a740bae85b3c9b8e5466baa9cbf7f57f1cde0ac6bc09",
}

# Standard library parts never used by PokeLDN (tests alone are ~36 MB).
STDLIB_DROP = {"test", "idlelib", "tkinter", "turtledemo", "turtle.py", "ensurepip", "pydoc_data",
               "lib2to3", "venv", "__phello__", "__hello__.py", "antigravity.py", "this.py"}
DYNLOAD_DROP = re.compile(r"^(_test|_xxtest|_ctypes_test|xxlimited|xxsubtype)")
# Package folders that are only tests.
TEST_DIRS = {"tests", "_tests", "test", "SelfTest"}  # not "testing": trio imports trio.testing
# Android-only Python files in the AAR runtime (android_host_loop is the shared trio loop: iOS uses it too).
APP_DROP = {"android_compat.py"}


def run(*args: object, cwd: Path | None = None) -> None:
    subprocess.run([str(x) for x in args], cwd=cwd, check=True)


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def support_framework() -> Path:
    archive = SUPPORT / PYTHON_ARCHIVE
    framework = SUPPORT / "Python.xcframework"
    if archive.exists() and sha256(archive) != PYTHON_SHA256:
        archive.unlink()
        shutil.rmtree(framework, ignore_errors=True)
    if not archive.exists():
        SUPPORT.mkdir(parents=True, exist_ok=True)
        print(f"Downloading {PYTHON_URL}")
        urllib.request.urlretrieve(PYTHON_URL, archive)
        if sha256(archive) != PYTHON_SHA256:
            archive.unlink()
            raise SystemExit("Python-Apple-support archive checksum mismatch")
    if not framework.is_dir():
        with tarfile.open(archive, "r:gz") as tar:
            members = [m for m in tar.getmembers() if m.name.startswith("Python.xcframework/")]
            tar.extractall(SUPPORT, members=members, filter="data")
    if not framework.is_dir():
        raise RuntimeError(f"Python support archive did not contain {framework}")
    return framework


def ignore_common(directory: str, names: list[str]) -> set[str]:
    return {n for n in names if n == "__pycache__" or n.endswith((".pyc", ".pyo")) or n.endswith(" 2")
            or re.search(r" \d+(\.\w+)?$", n)}  # macOS Finder copies ("x 3.py")


def copytree(source: Path, target: Path, drop=()) -> None:
    def ignore(directory: str, names: list[str]) -> set[str]:
        return ignore_common(directory, names) | {n for n in names if n in drop}
    shutil.copytree(source, target, dirs_exist_ok=True, ignore=ignore, symlinks=True)


def extract_imy(aar: zipfile.ZipFile, name: str, target: Path, skip_top=()) -> None:
    """Chaquopy .imy files are zips inside the AAR's assets/chaquopy."""
    data = aar.read(f"assets/chaquopy/{name}")
    with zipfile.ZipFile(io.BytesIO(data)) as imy:
        for info in imy.infolist():
            parts = Path(info.filename).parts
            if info.is_dir() or not parts or parts[0] in skip_top:
                continue
            if "__pycache__" in parts or info.filename.endswith((".pyc", ".so")):
                continue  # Android bytecode / ELF modules are useless on iOS
            if any(p in TEST_DIRS for p in parts[:-1]):
                continue
            out = target.joinpath(*parts)
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_bytes(imy.read(info))


def stage_python(framework: Path | None) -> None:
    if PKG.exists():
        shutil.rmtree(PKG)
    RES.mkdir(parents=True)
    app = RES / "pokeldn" / "app"
    packages = RES / "pokeldn" / "app_packages"
    if not AAR.is_file():
        raise SystemExit(f"Missing {AAR} (the Android LDN library is the PokeLDN source for iOS too)")
    with zipfile.ZipFile(AAR) as aar:
        extract_imy(aar, "app.imy", app, skip_top=APP_DROP)
        # Crypto and zstandard are rebuilt for iphoneos from their sdists below.
        extract_imy(aar, "requirements-common.imy", packages,
                    skip_top={"Crypto", "zstandard", "pycryptodome-3.21.0.dist-info", "zstandard-0.23.0.dist-info"})
    for f in (HERE / "RuntimeSource").glob("ios_*.py"):
        shutil.copy2(f, app / f.name)
    shutil.copy2(HERE / "RuntimeSource" / "LICENSE", RES / "pokeldn" / "LICENSE")   # AGPL-3.0 (PokeLDN)
    for top in ("bin", "pokeldn", "ldn"):
        if not (app / top).is_dir():
            raise SystemExit(f"AAR runtime has no {top}/ - rebuild ldnapp/embed first")
    copytree(HERE / "Assets", RES / "pokeldn")

    if framework is None:
        return
    lib = RES / "python" / "lib"
    lib.mkdir(parents=True)
    copytree(framework / "lib", lib, drop=STDLIB_DROP | {"libpython3.13.dylib"})
    copytree(framework / "ios-arm64" / "lib-arm64", lib)
    for so in (lib / "python3.13" / "lib-dynload").glob("*.so"):
        if DYNLOAD_DROP.match(so.name):
            so.unlink()


def device_xcframework(framework: Path) -> None:
    """Only the iphoneos Python.framework (stdlib is already in Resources/python)."""
    out = FRAMEWORKS / "Python.xcframework"
    out.mkdir(parents=True)
    shutil.copytree(framework / "ios-arm64" / "Python.framework", out / "ios-arm64" / "Python.framework", symlinks=True)
    info = plistlib.loads((framework / "Info.plist").read_bytes())
    info["AvailableLibraries"] = [x for x in info["AvailableLibraries"] if x.get("LibraryIdentifier") == "ios-arm64"]
    if not info["AvailableLibraries"]:
        raise RuntimeError("Python.xcframework has no ios-arm64 slice")
    (out / "Info.plist").write_bytes(plistlib.dumps(info))


def native_sources() -> dict[str, Path]:
    sources = WORK / "sources"
    sources.mkdir(parents=True, exist_ok=True)
    req = WORK / "native-requirements.txt"
    req.write_text("".join(f"{spec} --hash=sha256:{digest}\n" for spec, digest in NATIVE_PACKAGES.items()))
    run(sys.executable, "-m", "pip", "download", "--quiet", "--no-deps", "--no-binary", ":all:",
        "--require-hashes", "-r", req, "-d", sources)
    result = {}
    for spec in NATIVE_PACKAGES:
        name, version = spec.split("==", 1)
        folder = sources / f"{name}-{version}"
        if not folder.is_dir():
            with tarfile.open(sources / f"{name}-{version}.tar.gz", "r:gz") as tar:
                tar.extractall(sources, filter="data")
        result[name] = folder
    return result


def target_clang(*args: object) -> None:
    run("xcrun", "--sdk", "iphoneos", "clang", "-target", f"arm64-apple-ios{MIN_IOS}", *args)


def extension_specs(setup_py: Path):
    for node in ast.walk(ast.parse(setup_py.read_text())):
        if not isinstance(node, ast.Call) or getattr(node.func, "id", "") != "Extension" or not node.args:
            continue
        try:
            name = ast.literal_eval(node.args[0])
            keywords = {kw.arg: ast.literal_eval(kw.value) for kw in node.keywords
                        if kw.arg in ("sources", "include_dirs", "define_macros")}
        except (ValueError, TypeError):
            continue
        yield name, keywords.get("sources", []), keywords.get("include_dirs", []), keywords.get("define_macros", [])


def compile_native(packages: Path, framework: Path, sources: dict[str, Path]) -> None:
    include = framework / "ios-arm64" / "include" / "python3.13"
    flags = ["-O2", "-fPIC", "-dynamiclib", "-undefined", "dynamic_lookup"]

    crypto = sources["pycryptodome"]
    copytree(crypto / "lib" / "Crypto", packages / "Crypto", drop={"SelfTest"})
    for f in (packages / "Crypto").rglob("*"):
        if f.suffix in (".c", ".h"):
            f.unlink()
    for name, files, include_dirs, macros in extension_specs(crypto / "setup.py"):
        if any(part in name for part in ("aesni", "clmul")):
            continue
        out = packages / (name.replace(".", "/") + ".cpython-313-iphoneos.so")
        out.parent.mkdir(parents=True, exist_ok=True)
        defines = [f"-D{key}" + ("" if value is None else f"={value}") for key, value in macros]
        includes = [f"-I{crypto / path}" for path in include_dirs]
        target_clang(*flags, *defines, *includes, "-DHAVE_STDINT_H", "-DPYCRYPTO_LITTLE_ENDIAN",
                     "-DSYS_BITS=64", "-DHAVE_UINT128", "-DHAVE_POSIX_MEMALIGN", "-I", include,
                     "-o", out, *[crypto / path for path in files])

    zstd = sources["zstandard"]
    copytree(zstd / "zstandard", packages / "zstandard")
    for f in (packages / "zstandard").rglob("*"):
        if f.suffix in (".c", ".h"):
            f.unlink()
    target_clang(*flags, "-DZSTD_SINGLE_FILE", "-DZSTDLIB_VISIBLE=", "-DZDICTLIB_VISIBLE=",
                 "-DZSTDERRORLIB_VISIBLE=", "-fvisibility=hidden", "-I", include,
                 "-I", zstd / "zstd", "-I", zstd / "c-ext", "-o",
                 packages / "zstandard" / "backend_c.cpython-313-iphoneos.so", zstd / "c-ext" / "backend_c.c")


def make_module_frameworks(base: Path, template: Path) -> None:
    for module_file in sorted(base.rglob("*.so")):
        module = ".".join(module_file.relative_to(base).with_suffix("").parts).split(".cpython")[0]
        module = module.split(".abi3")[0]
        folder = FRAMEWORKS / f"{module}.framework"
        folder.mkdir(parents=True, exist_ok=True)
        info = plistlib.loads(template.read_bytes())
        bundle_component = "mod-" + module.lower().replace(".", "-").replace("_", "-")
        info.update(CFBundleExecutable=module, CFBundleIdentifier=f"com.pkhexkr.pokeldn.{bundle_component}")
        (folder / "Info.plist").write_bytes(plistlib.dumps(info))
        shutil.move(module_file, folder / module)
        # Local symbols are not needed at run time (smaller IPA).
        run("strip", "-x", folder / module)
        # iOS resolves embedded dynamic frameworks by their install name. Leaving
        # clang's default (for example, Modules/array.cpython-313-iphoneos.so)
        # makes dyld search for a loose .so at launch instead of this framework.
        run("install_name_tool", "-id", f"@rpath/{folder.name}/{module}", folder / module)
        relative = (Path("Frameworks") / folder.name / module).as_posix()
        module_file.with_suffix(".fwork").write_text(relative)
        (folder / f"{module}.origin").write_text(module_file.with_suffix(".fwork").relative_to(PKG).as_posix())


def build_native_bridge(framework: Path) -> None:
    device_slice = framework / "ios-arm64"
    include = device_slice / "include" / "python3.13"
    out = FRAMEWORKS / "PokeLdnRuntime.framework"
    out.mkdir(parents=True, exist_ok=True)
    executable = out / "PokeLdnRuntime"
    target_clang("-fobjc-arc", "-O2", "-dynamiclib", "-I", include, "-F", device_slice,
                 "-framework", "Python", "-framework", "Foundation", "-framework", "CoreBluetooth",
                 "-Wl,-install_name,@rpath/PokeLdnRuntime.framework/PokeLdnRuntime",
                 "-Wl,-rpath,@executable_path/Frameworks", "-o", executable, HERE / "native" / "PokeLdnRuntime.m")
    run("strip", "-x", executable)
    (out / "Info.plist").write_bytes(plistlib.dumps({
        "CFBundleExecutable": "PokeLdnRuntime", "CFBundleIdentifier": "com.pkhexkr.pokeldn.runtime",
        "CFBundlePackageType": "FMWK", "CFBundleSupportedPlatforms": ["iPhoneOS"],
        "MinimumOSVersion": MIN_IOS, "UIDeviceFamily": [1, 2],
    }))


def size(path: Path) -> str:
    total = sum(f.stat().st_size for f in path.rglob("*") if f.is_file() and not f.is_symlink())
    return f"{total / 1e6:.1f} MB"


def main() -> None:
    if "--stage-only" in sys.argv:
        stage_python(None)
        print(f"Staged PokeLDN Python files: {size(RES)}")
        return
    if sys.version_info[:2] != (3, 13):
        raise SystemExit("Use Python 3.13 to match the Android PokeLDN runtime.")
    if sys.platform != "darwin":
        raise SystemExit("The iOS runtime must be built on macOS with Xcode installed.")
    framework = support_framework()
    stage_python(framework)
    FRAMEWORKS.mkdir(parents=True)
    device_xcframework(framework)
    packages = RES / "pokeldn" / "app_packages"
    compile_native(packages, framework, native_sources())
    template = framework / "build" / "iOS-dylib-Info-template.plist"
    make_module_frameworks(RES / "python" / "lib" / "python3.13" / "lib-dynload", template)
    make_module_frameworks(packages, template)
    build_native_bridge(framework)
    shutil.rmtree(WORK, ignore_errors=True)
    print(f"Generated iOS PokeLDN runtime: {PKG} ({size(PKG)}: Resources {size(RES)}, Frameworks {size(FRAMEWORKS)})")


if __name__ == "__main__":
    main()
