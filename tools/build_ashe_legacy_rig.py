import glob
import hashlib
import json
import struct
import sys
import tempfile
import zipfile
from pathlib import Path

import build_jade_classic as jade
from port_legacy_base_skin import encode_texture, wad_from_bytes


SCALE = 1.25

ANIMATIONS = {
    "ashe_attack1.anm": "ashe_attack1.anm",
    "ashe_attack2.anm": "ashe_attack2.anm",
    "ashe_crit1.anm": "ashe_attack3.anm",
    "ashe_channel.anm": "ashe_channel.anm",
    "ashe_channel_windup.anm": "ashe_channel_windup.anm",
    "ashe_dance1.anm": "ashe_dance.anm",
    "ashe_death.anm": "ashe_death.anm",
    "ashe_idle1.anm": "ashe_idle1.anm",
    "ashe_idle2.anm": "ashe_idle2.anm",
    "ashe_idle3.anm": "ashe_idle3.anm",
    "ashe_idle4.anm": "ashe_idle4.anm",
    "ashe_idle5.anm": "ashe_idle1.anm",
    "ashe_joke.anm": "ashe_taunt.anm",
    "ashe_laugh.anm": "ashe_laugh.anm",
    "ashe_run.anm": "ashe_run.anm",
    "ashe_run_jog.anm": "ashe_run.anm",
    "ashe_run_walk.anm": "ashe_run.anm",
    "ashe_spell1.anm": "ashe_spell1.anm",
    "ashe_spell1_in.anm": "ashe_spell1.anm",
    "ashe_spell2.anm": "ashe_spell2.anm",
    "ashe_spell3.anm": "ashe_spell4.anm",
    "ashe_taunt.anm": "ashe_taunt.anm",
}


def scale_model(path, material):
    data = bytearray(Path(path).read_bytes())
    meshes = struct.unpack_from("<I", data, 8)[0]
    encoded = material.encode("ascii")[:63]
    data[12:76] = encoded + b"\0" * (64 - len(encoded))
    cursor = 12 + meshes * 80
    index_count, vertex_count = struct.unpack_from("<II", data, cursor)
    cursor += 8 + index_count * 2
    for vertex in range(vertex_count):
        position = cursor + vertex * 52
        xyz = struct.unpack_from("<3f", data, position)
        struct.pack_into("<3f", data, position, *(value * SCALE for value in xyz))
    return bytes(data)


def scale_skeleton(path):
    data = bytearray(Path(path).read_bytes())
    if data[:8] != b"r3d2sklt":
        raise RuntimeError("Expected legacy SKL")
    count = struct.unpack_from("<I", data, 16)[0]
    cursor = 20
    for _ in range(count):
        # Legacy joints store a row-major 3x4 inverse-bind matrix after the
        # name, parent and scale. Scale only its translation column.
        matrix = cursor + 40
        for offset in (12, 28, 44):
            value = struct.unpack_from("<f", data, matrix + offset)[0]
            struct.pack_into("<f", data, matrix + offset, value * SCALE)
        cursor += 88
    return bytes(data)


def main():
    if len(sys.argv) != 9:
        raise SystemExit("usage: build_ashe_legacy_rig.py PACKAGE PBE_WAD OLD_ROOT HASH_ROOT ZSTD TEX MODEL_PATH SKL_PATH")
    package, pbe_wad, old_root, hash_root, zstd_dll, tex_tool, model_path, skl_path = sys.argv[1:]
    zstd = jade.Zstd(zstd_dll)
    entries, game_raw = jade.read_wad(pbe_wad, zstd)
    modern_model = game_raw(entries[jade.xxh64(model_path)])
    material = modern_model[12:76].split(b"\0", 1)[0].decode("ascii")

    with zipfile.ZipFile(package) as archive:
        wad_name = next(name for name in archive.namelist() if name.lower().endswith(".wad.client"))
        chunks = wad_from_bytes(archive.read(wad_name), zstd)

    old_root = Path(old_root)
    chunks[jade.xxh64(model_path)] = scale_model(old_root / "Ashe.skn", material)
    chunks[jade.xxh64(skl_path)] = scale_skeleton(old_root / "Ashe.skl")
    texture_path = "assets/characters/jade_ashe/skins/base/jade_ashe_base_2011_tx_cm.project_jade.tex"
    with tempfile.TemporaryDirectory(prefix="csm-ashe-rig-") as temp:
        texture = encode_texture(tex_tool, old_root / "Bowmaster_Clr.dds", Path(temp) / "ashe.tex")
        splash = encode_texture(tex_tool, old_root / "AsheLoadScreen.dds", Path(temp) / "splash.tex")
    chunks[jade.xxh64(texture_path)] = texture

    loadscreen_count = 0
    for filename in glob.glob(str(Path(hash_root) / "hashes.game.txt.*")):
        for line in Path(filename).read_text(encoding="utf8", errors="ignore").splitlines():
            if "ashe" not in line.lower() or "loadscreen" not in line.lower() or not line.lower().endswith(".tex"):
                continue
            digest, target = line.split(" ", 1)
            target_hash = int(digest, 16)
            if target_hash in entries and "/pet" not in target:
                chunks[target_hash] = splash
                loadscreen_count += 1

    animation_root = old_root / "Animations"
    for modern_name, old_name in ANIMATIONS.items():
        target = "assets/characters/ashe/skins/base/animations/" + modern_name
        target_hash = jade.xxh64(target)
        if target_hash not in entries:
            raise RuntimeError("Missing PBE animation target: " + target)
        chunks[target_hash] = (animation_root / old_name).read_bytes()

    missing = ["%016x" % key for key in chunks if key not in entries]
    if missing:
        raise RuntimeError("Package contains missing targets: " + ", ".join(missing))
    metadata = {
        "Name": "Classic Ashe Season 1 - Full Rig",
        "Author": "Xitfin",
        "Version": "0.6-experimental",
        "Description": "Season 1 model, skeleton, animations, textures and loading screens at 125% scale",
    }
    with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr("META/info.json", json.dumps(metadata, indent=2))
        archive.writestr(wad_name, jade.create_wad(chunks, zstd))
    print(json.dumps({"package": package, "chunks": len(chunks), "animations": len(ANIMATIONS),
                      "loadscreens": loadscreen_count, "scale": SCALE,
                      "sha256": hashlib.sha256(Path(package).read_bytes()).hexdigest()}, indent=2))


if __name__ == "__main__":
    main()
