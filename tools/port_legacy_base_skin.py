import json
import struct
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

import build_jade_classic as jade


def wad_from_bytes(data, zstd):
    count = struct.unpack_from("<I", data, 268)[0]
    chunks = {}
    for index in range(count):
        entry = struct.unpack_from("<QIII BBH 8s", data, 272 + index * 32)
        payload = data[entry[1]:entry[1] + entry[2]]
        if entry[4] == 0:
            raw = payload
        elif entry[4] == 3:
            raw = zstd.decompress(payload, entry[3])
        else:
            raise RuntimeError("Unsupported package compression: %d" % entry[4])
        chunks[entry[0]] = raw
    return chunks


def legacy_skeleton(path):
    data = Path(path).read_bytes()
    if data[:8] != b"r3d2sklt":
        raise RuntimeError("Expected a legacy SKL")
    version = struct.unpack_from("<I", data, 8)[0]
    joint_count = struct.unpack_from("<I", data, 16)[0]
    cursor, names = 20, []
    for _ in range(joint_count):
        names.append(data[cursor:cursor + 32].split(b"\0", 1)[0].decode("ascii"))
        cursor += 88
    if version == 2:
        influence_count = struct.unpack_from("<I", data, cursor)[0]
        influences = list(struct.unpack_from("<" + "I" * influence_count, data, cursor + 4))
    else:
        influences = list(range(joint_count))
    return names, influences


def modern_skeleton(data):
    if struct.unpack_from("<I", data, 4)[0] != 0x22FD4FC3:
        raise RuntimeError("Expected a modern SKL")
    joint_count = struct.unpack_from("<H", data, 14)[0]
    influence_count = struct.unpack_from("<I", data, 16)[0]
    joints_offset, _, influences_offset = struct.unpack_from("<iii", data, 20)
    names = []
    for index in range(joint_count):
        joint = joints_offset + index * 100
        relative_name = struct.unpack_from("<i", data, joint + 96)[0]
        start = joint + 96 + relative_name
        names.append(data[start:data.index(b"\0", start)].decode("ascii"))
    influences = list(struct.unpack_from("<" + "h" * influence_count, data, influences_offset))
    return names, influences


def remap_skn(path, old_skl, modern_skl, material_name):
    data = bytearray(Path(path).read_bytes())
    if struct.unpack_from("<I", data, 0)[0] != 0x00112233:
        raise RuntimeError("Expected an SKN model")
    major, minor, submesh_count = struct.unpack_from("<HHI", data, 4)
    if major != 2 or minor != 1:
        raise RuntimeError("Legacy port currently supports SKN 2.1")
    old_names, old_influences = legacy_skeleton(old_skl)
    modern_names, modern_influences = modern_skeleton(modern_skl)
    modern_lookup = {modern_names[joint].lower(): index for index, joint in enumerate(modern_influences)}
    # Semantic aliases used by champions whose skeleton was renamed during a
    # visual update. Values target the closest modern animation influence.
    aliases = {
        "pelvis": "hip", "bag": "quiverful2",
        "l_calf": "l_knee", "r_calf": "r_knee",
        "l_arm": "l_uparm", "r_arm": "r_uparm",
        "l_forarm": "l_forearm", "r_forarm": "r_forearm",
        "cape": "m_cape_jnt", "cape_b": "m_cape_b_jnt", "cape_c": "m_cape_c_jnt",
        "hair": "head", "hair_b": "head", "missle": "main_arrow",
        "l_thumb": "l_hand_finger_thumb_jnt", "r_thumb": "r_hand_finger_thumb_jnt",
        "l_finger": "l_hand_finger_middle_jnt", "r_finger": "r_hand_finger_middle_jnt",
        "r_weapon": "bow_01", "r_weapon_b": "bow_02", "r_weapon_c": "bow_04",
        "r_weapon_d": "bow_04", "r_weapon_e": "bow_04", "r_weapon_f": "bow_pull",
        "r_tip": "r_toe", "l_tip": "l_toe",
        "root": "hip",
        # Miss Fortune's ASU renamed most of the humanoid rig.
        "r_forearm": "r_elbow", "l_forearm": "l_elbow",
        "r_sleeves": "r_cuff1", "l_sleeves": "l_cuff1",
        "r_thumb_index": "r_thumb1", "r_thumb_index_b": "r_thumb2",
        "l_thumb_index": "l_thumb1", "l_thumb_index_b": "l_thumb2",
        "r_finger_index": "r_ring1", "r_finger_index_b": "r_ring2",
        "l_finger_index": "l_ring1", "l_finger_index_b": "l_ring2",
        "r_uparm": "r_shoulder", "l_uparm": "l_shoulder",
        "c_hip": "pelvis", "l_thigh": "l_hip", "r_thigh": "r_hip",
        "l_knee": "l_kneelower", "r_knee": "r_kneelower",
        "l_trousers": "r_skirt1", "r_trousers": "r_skirt2",
        "c_waist": "spine1", "chest": "spine2", "c_neck": "neck",
        "c_cap": "hat", "hail_top": "hat",
        "b_hair": "c_hair1", "b_hair_b": "c_hair2", "b_hair_c": "c_hair3",
        "b_hair_d": "c_hair3", "b_hair_e": "c_hair3",
        "f_hair": "f_hair1", "f_hair_b": "f_hair2",
        "f_hair_c": "f_hair2", "f_hair_d": "f_hair2",
        "l_hair": "c_hair1", "l_hair_b": "c_hair2", "l_hair_c": "c_hair3",
        "l_hair_d": "c_hair3", "r_hair": "c_hair1", "r_hair_b": "c_hair2",
        "r_hair_c": "c_hair3", "r_hair_d": "c_hair3",
    }
    influence_map = {}
    for old_index, joint in enumerate(old_influences):
        original_name = old_names[joint].lower()
        name = original_name
        # Prefer an exact match. Aliases are only fallbacks for champions such
        # as Ashe whose bones were renamed; applying them unconditionally can
        # break champions like Brand that still expose the original names.
        if name not in modern_lookup:
            name = aliases.get(name, name)
        if name not in modern_lookup and original_name == "root" and "pelvis" in modern_lookup:
            name = "pelvis"
        if name not in modern_lookup:
            raise RuntimeError("Modern skeleton is missing bone: " + old_names[joint])
        influence_map[old_index] = modern_lookup[name]

    cursor = 12
    for submesh in range(submesh_count):
        if submesh == 0:
            encoded = material_name.encode("ascii")[:63]
            data[cursor:cursor + 64] = encoded + b"\0" * (64 - len(encoded))
        cursor += 80
    index_count, vertex_count = struct.unpack_from("<II", data, cursor)
    cursor += 8 + index_count * 2
    for vertex in range(vertex_count):
        bone_offset = cursor + vertex * 52 + 12
        for slot in range(4):
            old_index = data[bone_offset + slot]
            data[bone_offset + slot] = influence_map.get(old_index, 0)
    return bytes(data)


def encode_texture(tool, source, output):
    source = Path(source)
    encode_source = source
    converted = None
    if source.suffix.lower() == ".dds":
        from PIL import Image
        converted = Path(output).with_suffix(".png")
        with Image.open(source) as image:
            image.save(converted, "PNG")
        encode_source = converted
    subprocess.run([str(tool), "encode", "--input", str(encode_source), "--output", str(output),
                    "--format", "bc3", "--generate-mipmaps"], check=True)
    if converted is not None:
        converted.unlink(missing_ok=True)
    return Path(output).read_bytes()


def main():
    if len(sys.argv) != 14:
        raise SystemExit("usage: port_legacy_base_skin.py PACKAGE PBE_WAD OLD_SKN OLD_SKL OLD_DDS OLD_SPLASH "
                         "MODEL_PATH SKL_PATH TEXTURE_PATH SPLASH_PATH DISPLAY_NAME ZSTD_DLL TEX_TOOL")
    (package, pbe_wad, old_skn, old_skl, old_dds, old_splash, model_path, skl_path,
     texture_path, splash_path, display_name, zstd_dll, tex_tool) = sys.argv[1:]
    zstd = jade.Zstd(zstd_dll)
    game_entries, game_raw = jade.read_wad(pbe_wad, zstd)
    modern_model = game_raw(game_entries[jade.xxh64(model_path)])
    modern_skl = game_raw(game_entries[jade.xxh64(skl_path)])
    submesh_count = struct.unpack_from("<I", modern_model, 8)[0]
    if submesh_count < 1:
        raise RuntimeError("Modern model has no material")
    material = modern_model[12:76].split(b"\0", 1)[0].decode("ascii")
    converted_model = remap_skn(old_skn, old_skl, modern_skl, material)

    with zipfile.ZipFile(package) as archive:
        wad_name = next(name for name in archive.namelist() if name.lower().startswith("wad/") and name.lower().endswith(".wad.client"))
        chunks = wad_from_bytes(archive.read(wad_name), zstd)
    with tempfile.TemporaryDirectory(prefix="csm-legacy-") as temp:
        texture = encode_texture(tex_tool, old_dds, Path(temp) / "diffuse.tex")
        splash = encode_texture(tex_tool, old_splash, Path(temp) / "splash.tex")
    chunks[jade.xxh64(model_path)] = converted_model
    chunks[jade.xxh64(texture_path)] = texture
    splash_paths = [item for item in splash_path.split("|") if item]
    for target_splash_path in splash_paths:
        chunks[jade.xxh64(target_splash_path)] = splash
    wad_data = jade.create_wad(chunks, zstd)
    metadata = {
        "Name": display_name,
        "Author": "Xitfin",
        "Version": "0.6",
        "Description": "Forces the original Season 1 Classic model, texture, and loading screen on every skin"
    }
    with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr("META/info.json", json.dumps(metadata, indent=2))
        archive.writestr(wad_name, wad_data)
    print(json.dumps({"package": package, "chunks": len(chunks), "material": material,
                      "model_bytes": len(converted_model), "texture_bytes": len(texture),
                      "splash_bytes": len(splash), "splash_targets": len(splash_paths)}, indent=2))


if __name__ == "__main__":
    main()
