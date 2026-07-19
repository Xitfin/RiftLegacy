import json
import sys
import zipfile
from pathlib import Path

import build_jade_classic as jade
from port_legacy_base_skin import wad_from_bytes


ANIMATIONS = (
    "attack1", "attack2", "crit", "channel", "channel_windup", "death",
    "idle1", "idle2", "idle3", "joke", "run", "spell1", "spell2",
    "spell3", "spell4", "taunt", "laugh", "dance", "dance_transition",
)


def read_package(path, zstd):
    with zipfile.ZipFile(path) as archive:
        wad_name = next(name for name in archive.namelist()
                        if name.lower().endswith(".wad.client"))
        return wad_name, wad_from_bytes(archive.read(wad_name), zstd)


def copy(chunks, source, source_path, target_path):
    raw = source.get(jade.xxh64(source_path))
    if raw is None:
        raise RuntimeError("Missing source asset: " + source_path)
    chunks[jade.xxh64(target_path)] = raw
    return len(raw)


def main():
    if len(sys.argv) != 4:
        raise SystemExit("usage: apply_anivia_mod_rig.py SOURCE_FANTOME PACKAGE ZSTD_DLL")
    source_path, package_path, zstd_path = map(Path, sys.argv[1:])
    zstd = jade.Zstd(zstd_path)
    _, source = read_package(source_path, zstd)
    wad_name, chunks = read_package(package_path, zstd)

    copied = {
        "model": copy(
            chunks, source,
            "assets/characters/anivia/skins/base/anivia.skn",
            "assets/characters/jade_anivia/skins/base/jade_anivia.project_jade.skn"),
        "skeleton": copy(
            chunks, source,
            "assets/characters/anivia/skins/base/anivia.skl",
            "assets/characters/jade_anivia/skins/base/jade_anivia.project_jade.skl"),
    }
    for animation in ANIMATIONS:
        copied[animation] = copy(
            chunks, source,
            "assets/characters/anivia/skins/base/animations/anivia_%s.anm" % animation,
            "assets/characters/jade_anivia/skins/base/animations/"
            "jade_anivia_%s.project_jade.anm" % animation)

    metadata = {
        "Name": "Classic Anivia Season 1 - PBE",
        "Author": "AryasDemise / Xitfin",
        "Version": "0.6",
        "Description": "Old Anivia model, skeleton and complete animation set routed to every Jade Anivia skin",
    }
    with zipfile.ZipFile(package_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr("META/info.json", json.dumps(metadata, indent=2))
        archive.writestr(wad_name, jade.create_wad(chunks, zstd))
    print(json.dumps({"copied": copied, "chunks": len(chunks)}, indent=2))


if __name__ == "__main__":
    main()
