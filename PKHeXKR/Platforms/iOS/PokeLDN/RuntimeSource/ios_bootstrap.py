"""Initialize iOS-specific loaders before the Android-derived PokeLDN runner imports crypto."""

import os
from pathlib import Path


def install():
    """Resolve BeeWare's .fwork placeholders to embedded iOS module frameworks for PyCryptodome."""
    from Crypto.Util import _file_system, _raw_api

    original = _raw_api.load_pycryptodome_raw_lib

    def load(name, cdecl):
        *package, base = name.split(".")
        for suffix in _raw_api.extension_suffixes:
            placeholder = Path(_file_system.pycryptodome_filename(
                package, base + suffix.removesuffix(".so") + ".fwork"))
            if placeholder.is_file():
                library = Path(os.environ["POKELDN_IOS_BUNDLE"]) / placeholder.read_text().strip()
                return _raw_api.load_lib(str(library), cdecl)
        return original(name, cdecl)

    _raw_api.load_pycryptodome_raw_lib = load
