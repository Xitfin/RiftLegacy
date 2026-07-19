import json
import sys
import zipfile
from pathlib import Path

import build_jade_classic as jade
from port_legacy_base_skin import wad_from_bytes


ANIMATION_MAP = {
    "attack1": "attack1", "attack2": "attack2",
    "channel": "idle1", "channel_windup": "idle1", "crit": "attack3",
    "idle1": "idle1", "idle2": "idle2", "idle3": "idle3",
    "joke": "idle1", "dance": "dance", "death": "death",
    "laugh": "laugh", "run": "run",
    "spell1": "spellcast2", "spell2": "spellcast3", "spell3": "spellcast1",
    "spell4_windup": "ult_windup", "spell4_loop": "ult_loop",
    "spell4_winddown": "ult_winddown", "taunt": "taunt",
}


def read_package(path, zstd):
    with zipfile.ZipFile(path) as archive:
        name = next(item for item in archive.namelist()
                    if item.lower().endswith(".wad.client"))
        return name, wad_from_bytes(archive.read(name), zstd)


def copy(chunks, source, source_path, target_path):
    raw = source.get(jade.xxh64(source_path))
    if raw is None:
        raise RuntimeError("Missing source asset: " + source_path)
    chunks[jade.xxh64(target_path)] = raw
    return len(raw)


def main():
    if len(sys.argv) != 4:
        raise SystemExit("usage: apply_janna_mod_rig.py SOURCE_FANTOME PACKAGE ZSTD_DLL")
    source_path, package_path, zstd_path = map(Path, sys.argv[1:])
    zstd = jade.Zstd(zstd_path)
    _, source = read_package(source_path, zstd)
    wad_name, chunks = read_package(package_path, zstd)

    copied = {}
    base = "assets/characters/janna/skins/base/"
    jade_base = "assets/characters/jade_janna/skins/base/"
    copied["model"] = copy(chunks, source, base + "janna.skn",
                           jade_base + "jade_janna.project_jade.skn")
    copied["skeleton"] = copy(chunks, source, base + "janna.skl",
                              jade_base + "jade_janna.project_jade.skl")
    copied["texture"] = copy(chunks, source, base + "janna_base_tx_cm.tex",
                             jade_base + "jade_janna_base_tx_cm.project_jade.tex")
    copied["splash"] = copy(chunks, source, base + "jannaloadscreen.tex",
                            jade_base + "jade_jannaloadscreen.project_jade.tex")

    for target, original in ANIMATION_MAP.items():
        copied["animation_" + target] = copy(
            chunks, source,
            base + "animations/janna_%s.anm" % original,
            jade_base + "animations/jade_janna_%s.project_jade.anm" % target)

    # Janna's Jade character continues to reference the standard Janna VO
    # namespace, so these three original paths can be overridden directly.
    for suffix in ("audio.bnk", "audio.wpk", "events.bnk"):
        path = "assets/sounds/wwise2016/vo/en_us/characters/janna/skins/base/janna_base_vo_" + suffix
        copied["vo_" + suffix] = copy(chunks, source, path, path)

    metadata = {
        "Name": "Classic Janna Oldest - PBE",
        "Author": "AryasDemise / Xitfin",
        "Version": "0.6",
        "Description": "Complete pre-rework Janna model, skeleton, animations, texture, loading screen and VO on every Jade Janna skin",
    }
    with zipfile.ZipFile(package_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr("META/info.json", json.dumps(metadata, indent=2))
        archive.writestr(wad_name, jade.create_wad(chunks, zstd))
    print(json.dumps({"copied": copied, "chunks": len(chunks)}, indent=2))


if __name__ == "__main__":
    main()
