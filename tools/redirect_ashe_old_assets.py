import json
import sys
import tempfile
import zipfile
from pathlib import Path

import build_jade_classic as jade
from port_legacy_base_skin import wad_from_bytes


def replace_path(value):
    normalized = value.replace("\\", "/")
    prefix = "ASSETS/csmoldashe/Characters/Jade_Ashe/"
    if not normalized.lower().startswith(prefix.lower()):
        return value
    relative = normalized[len(prefix):]
    lower = relative.lower()
    if lower.startswith("skins/base/animations/"):
        name = relative.rsplit("/", 1)[-1].replace(".project_jade", "")
        name = name.replace("Jade_Ashe_", "ashe_")
        aliases = {
            "ashe_crit1.anm": "ashe_attack3.anm", "ashe_idle5.anm": "ashe_idle4.anm",
            "ashe_run_walk.anm": "ashe_run.anm", "ashe_run_jog.anm": "ashe_run.anm",
            "ashe_spell3.anm": "ashe_spell4.anm", "ashe_joke.anm": "ashe_taunt.anm",
            "ashe_dance1.anm": "ashe_dance.anm", "ashe_spell1_in.anm": "ashe_spell1.anm",
        }
        name = aliases.get(name.lower(), name)
        return "ASSETS/csmoldashe/Characters/Ashe/Skins/Base/Animations/" + name
    names = {
        "skins/base/jade_ashe.project_jade.skn": "Skins/Base/Ashe.skn",
        "skins/base/jade_ashe.project_jade.skl": "Skins/Base/Ashe.skl",
        "skins/base/jade_ashe_base_2011_tx_cm.project_jade.tex": "Skins/Base/ashe_base_2011_TX_CM.tex",
        "skins/base/jade_asheloadscreen_0.project_jade.tex": "Skins/Base/AsheLoadScreen.tex",
        "skins/base/jade_asheloadscreen_0_le.project_jade.tex": "Skins/Base/AsheLoadScreen.tex",
        "hud/jade_ashe_circle_0.project_jade.tex": "HUD/Ashe_Circle.tex",
        "hud/jade_ashe_circle.project_jade.tex": "HUD/Ashe_Circle.tex",
        "hud/jade_ashe_square_0.project_jade.tex": "HUD/Ashe_Square.tex",
    }
    target = names.get(lower)
    return "ASSETS/csmoldashe/Characters/Ashe/" + target if target else value


def patch_value(value):
    if isinstance(value, str):
        return replace_path(value)
    if isinstance(value, list):
        for item in value:
            if hasattr(item, "data"):
                item.data = patch_value(item.data)
        return value
    return value


def main():
    if len(sys.argv) != 5:
        raise SystemExit("usage: redirect_ashe_old_assets.py PACKAGE ZSTD PYRITO_ROOT HASH_ROOT")
    package, zstd_dll, pyrito_root, hash_root = sys.argv[1:]
    sys.path.insert(0, pyrito_root)
    import pyRitoFile
    zstd = jade.Zstd(zstd_dll)
    with zipfile.ZipFile(package) as archive:
        wad_name = next(name for name in archive.namelist() if name.lower().endswith(".wad.client"))
        chunks = wad_from_bytes(archive.read(wad_name), zstd)
    patched, replacements = 0, 0
    with tempfile.TemporaryDirectory(prefix="csm-ashe-redirect-") as temp:
        for path_hash, raw in list(chunks.items()):
            source = Path(temp) / ("%016x.bin" % path_hash)
            if raw[:4] != b"PROP":
                continue
            source.write_bytes(raw)
            try:
                parsed = pyRitoFile.bin.BIN().read(str(source))
            except Exception:
                continue
            before = source.read_bytes()
            for entry in parsed.entries:
                for field in entry.data:
                    original = repr(field.data)
                    field.data = patch_value(field.data)
                    if repr(field.data) != original:
                        replacements += 1
            parsed.write(str(source))
            after = source.read_bytes()
            if after != before:
                chunks[path_hash] = after
                patched += 1
    if not replacements:
        raise RuntimeError("No Jade Ashe asset references were redirected")
    with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr("META/info.json", json.dumps({"Name": "Classic Ashe Season 1 - PBE",
            "Author": "AryasDemise / Xitfin", "Version": "0.6",
            "Description": "Old Ashe assets redirected from Jade PBE BINs"}, indent=2))
        archive.writestr(wad_name, jade.create_wad(chunks, zstd))
    print(json.dumps({"patched_bins": patched, "redirected_fields": replacements, "chunks": len(chunks)}, indent=2))


if __name__ == "__main__":
    main()
