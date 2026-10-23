"""Normalize embedded CPython extension framework install names in a built .app."""

from __future__ import annotations

import plistlib
import re
import subprocess
import sys
from pathlib import Path


EXTENSION_SUFFIX = re.compile(r"(?:\.cpython-[^/]+|\.abi3)?\.so$")


def run(*args: str) -> str:
    return subprocess.run(args, check=True, text=True, capture_output=True).stdout


def module_name_from_load_path(load_path: str) -> str:
    path = load_path.replace("\\", "/")
    if "/app_packages/" in path:
        relative = path.split("/app_packages/", 1)[1]
        return EXTENSION_SUFFIX.sub("", relative).replace("/", ".")
    if "/lib-dynload/" in path:
        return EXTENSION_SUFFIX.sub("", path.rsplit("/", 1)[1])
    if "/Modules/" in path:
        return EXTENSION_SUFFIX.sub("", path.rsplit("/Modules/", 1)[1])
    if path.startswith("Modules/"):
        return EXTENSION_SUFFIX.sub("", path.removeprefix("Modules/"))
    return EXTENSION_SUFFIX.sub("", path.rsplit("/", 1)[1])


def main(app_path: Path) -> None:
    app_path = app_path.resolve()
    frameworks_dir = app_path / "Frameworks"
    app_info = plistlib.loads((app_path / "Info.plist").read_bytes())
    app_executable = app_path / app_info["CFBundleExecutable"]

    frameworks: dict[str, tuple[Path, str]] = {}
    for framework in sorted(frameworks_dir.glob("*.framework")):
        info_path = framework / "Info.plist"
        if not info_path.is_file():
            continue
        info = plistlib.loads(info_path.read_bytes())
        executable_name = info.get("CFBundleExecutable")
        executable = framework / executable_name if executable_name else None
        if not executable or not executable.is_file():
            continue
        if framework.name in ("Python.framework", "PokeLdnRuntime.framework"):
            continue
        module_key = framework.name.removesuffix(".framework")
        frameworks[module_key] = (framework, executable_name)

    # Set each extension's canonical identity before changing the app's load
    # commands. This also makes the resulting bundle independent of build paths.
    for key, (framework, executable_name) in frameworks.items():
        executable = framework / executable_name
        canonical = f"@rpath/{framework.name}/{executable_name}"
        current_id = run("otool", "-D", str(executable)).splitlines()
        if len(current_id) > 1 and current_id[1].strip() != canonical:
            run("install_name_tool", "-id", canonical, str(executable))

    dependencies = run("otool", "-L", str(app_executable)).splitlines()[1:]
    changes: list[tuple[str, str]] = []
    unresolved: list[str] = []
    for line in dependencies:
        load_path = line.strip().split(" (compatibility version", 1)[0]
        if not load_path.endswith(".so"):
            continue
        key = module_name_from_load_path(load_path)
        framework_entry = frameworks.get(key)
        if framework_entry is None:
            # Some linkers shorten the load command to the module basename.
            basename = key.rsplit(".", 1)[-1]
            matches = [entry for name, entry in frameworks.items()
                       if name == basename or name.endswith("." + basename)]
            if len(matches) == 1:
                framework_entry = matches[0]
        if framework_entry is None:
            unresolved.append(load_path)
            continue
        framework, executable_name = framework_entry
        changes.append((load_path, f"@rpath/{framework.name}/{executable_name}"))

    if unresolved:
        raise RuntimeError("Unmatched CPython extension dependencies:\n" + "\n".join(unresolved))

    for old, new in changes:
        if old != new:
            run("install_name_tool", "-change", old, new, str(app_executable))

    remaining = [line.strip() for line in run("otool", "-L", str(app_executable)).splitlines()[1:]
                 if ".cpython-" in line or ".abi3.so" in line]
    if remaining:
        raise RuntimeError("Non-framework extension load paths remain:\n" + "\n".join(remaining))

    print(f"Normalized {len(changes)} CPython extension framework links in {app_path}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("usage: fix_ios_framework_links.py /path/to/App.app")
    main(Path(sys.argv[1]))
