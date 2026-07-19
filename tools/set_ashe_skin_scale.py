import json
import sys
import tempfile
import zipfile
from pathlib import Path

import build_jade_classic as jade
from port_legacy_base_skin import wad_from_bytes


FIELD = "a1f805da"  # skinScale
FROM_SCALE = 1.375
TO_SCALE = 1.58125


def patch_fields(value):
    changed = 0
    if isinstance(value, (list, tuple)):
        for item in value:
            changed += patch_fields(item)
    elif isinstance(value, dict) or hasattr(value, "items"):
        try:
            for key, item in value.items():
                changed += patch_fields(key)
                changed += patch_fields(item)
        except (AttributeError, TypeError):
            pass
    else:
        if type(value).__name__ == "BINField":
            if str(value.hash).lower() == FIELD and isinstance(value.data, float):
                if abs(value.data - FROM_SCALE) < 0.001:
                    value.data = TO_SCALE
                    changed += 1
            changed += patch_fields(value.data)
        else:
            for name in getattr(type(value), "__slots__", ()):
                try:
                    changed += patch_fields(getattr(value, name))
                except (AttributeError, TypeError):
                    pass
    return changed


def main():
    if len(sys.argv) != 4:
        raise SystemExit("usage: set_ashe_skin_scale.py PACKAGE ZSTD_DLL PYRITO_ROOT")
    package, zstd_dll, pyrito_root = sys.argv[1:]
    sys.path.insert(0, pyrito_root)
    import pyRitoFile

    zstd = jade.Zstd(zstd_dll)
    with zipfile.ZipFile(package) as archive:
        wad_name = next(name for name in archive.namelist()
                        if name.lower().endswith(".wad.client"))
        chunks = wad_from_bytes(archive.read(wad_name), zstd)

    changed_bins = changed_fields = 0
    with tempfile.TemporaryDirectory(prefix="csm-ashe-scale-") as temp:
        for path_hash, raw in list(chunks.items()):
            if raw[:4] != b"PROP":
                continue
            path = Path(temp) / ("%016x.bin" % path_hash)
            path.write_bytes(raw)
            try:
                parsed = pyRitoFile.bin.BIN().read(str(path))
            except Exception:
                continue
            changes = patch_fields(parsed.entries)
            if changes:
                parsed.write(str(path))
                chunks[path_hash] = path.read_bytes()
                changed_bins += 1
                changed_fields += changes

    if not changed_fields:
        raise RuntimeError("No Ashe skinScale=1.375 field found")

    metadata = {
        "Name": "Classic Ashe Season 1 - PBE",
        "Author": "AryasDemise / Xitfin",
        "Version": "0.6",
        "Description": "Author's complete Base rig with an additional 15% visual scale on every Jade Ashe skin",
    }
    with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr("META/info.json", json.dumps(metadata, indent=2))
        archive.writestr(wad_name, jade.create_wad(chunks, zstd))
    print(json.dumps({"changed_bins": changed_bins, "changed_fields": changed_fields,
                      "old_scale": FROM_SCALE, "new_scale": TO_SCALE}, indent=2))


if __name__ == "__main__":
    main()
