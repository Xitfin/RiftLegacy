import json
import re
import sys
import zipfile
from pathlib import Path

import build_jade_classic as jade
from port_legacy_base_skin import wad_from_bytes


ALIASES = {
    "crit1": "attack3",
    "idle5": "idle4",
    "run_walk": "run",
    "run_jog": "run",
    "spell3": "spell4",
    "joke": "taunt",
    "dance1": "dance",
    "spell1_in": "spell1",
}


def archive_wad(path):
    with zipfile.ZipFile(path) as archive:
        name = next(item for item in archive.namelist()
                    if item.lower().endswith(".wad.client"))
        return name, archive.read(name)


def main():
    if len(sys.argv) != 4:
        raise SystemExit("usage: restore_ashe_mod_rig.py SOURCE_FANTOME PACKAGE ZSTD_DLL")

    source_path, package_path, zstd_path = map(Path, sys.argv[1:])
    zstd = jade.Zstd(zstd_path)
    _, source_wad = archive_wad(source_path)
    package_wad_name, package_wad = archive_wad(package_path)
    source = wad_from_bytes(source_wad, zstd)
    package = wad_from_bytes(package_wad, zstd)

    copied = {}
    for path_hash, raw in list(package.items()):
        if raw[:4] != b"PROP":
            continue
        for match in re.findall(
                rb"ASSETS/csmoldashe/Characters/Jade_Ashe/Skins/Base/Animations/"
                rb"Jade_Ashe_([A-Za-z0-9_]+)\.project_jade\.anm", raw, re.I):
            target_name = match.decode("ascii").lower()
            source_name = ALIASES.get(target_name, target_name)
            source_asset = "assets/characters/ashe/skins/base/animations/ashe_%s.anm" % source_name
            target_asset = ("assets/csmoldashe/characters/jade_ashe/skins/base/animations/"
                            "jade_ashe_%s.project_jade.anm" % target_name)
            source_hash = jade.xxh64(source_asset)
            if source_hash not in source:
                raise RuntimeError("Missing adapted source animation: " + source_asset)
            package[jade.xxh64(target_asset)] = source[source_hash]
            copied[target_asset] = source_asset

    # The repather may have removed paths which were already redirected to the
    # original namespace. Keep every Base animation from the author's archive too.
    for name in ("attack1", "attack2", "attack3", "channel", "channel_windup",
                 "crystal_dance", "dance", "death", "idle1", "idle2", "idle3",
                 "idle4", "laugh", "queen_run", "run", "spell1", "spell2",
                 "spell4", "taunt"):
        source_asset = "assets/characters/ashe/skins/base/animations/ashe_%s.anm" % name
        source_hash = jade.xxh64(source_asset)
        if source_hash in source:
            target_asset = "assets/csmoldashe/characters/ashe/skins/base/animations/ashe_%s.anm" % name
            package[jade.xxh64(target_asset)] = source[source_hash]

    if not copied:
        raise RuntimeError("No Jade Ashe animation references found")

    metadata = {
        "Name": "Classic Ashe Season 1 - PBE",
        "Author": "AryasDemise / Xitfin",
        "Version": "0.6",
        "Description": "Author's complete Base rig and animations routed to every Jade Ashe skin",
    }
    with zipfile.ZipFile(package_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr("META/info.json", json.dumps(metadata, indent=2))
        archive.writestr(package_wad_name, jade.create_wad(package, zstd))

    print(json.dumps({"jade_animation_payloads": len(copied),
                      "total_chunks": len(package), "mapping": copied}, indent=2))


if __name__ == "__main__":
    main()
