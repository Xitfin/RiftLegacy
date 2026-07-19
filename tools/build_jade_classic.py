import ctypes
import json
import re
import struct
import sys
import zipfile
from pathlib import Path

MASK64 = (1 << 64) - 1
P1, P2, P3, P4, P5 = (11400714785074694791, 14029467366897019727,
                      1609587929392839161, 9650029242287828579,
                      2870177450012600261)


def rol(value, bits):
    return ((value << bits) | (value >> (64 - bits))) & MASK64


def xx_round(acc, value):
    return (rol((acc + value * P2) & MASK64, 31) * P1) & MASK64


def xxh64(data):
    if isinstance(data, str):
        data = data.lower().encode("utf-8")
    size, cursor = len(data), 0
    if size >= 32:
        values = [(P1 + P2) & MASK64, P2, 0, (-P1) & MASK64]
        while cursor <= size - 32:
            for index in range(4):
                values[index] = xx_round(values[index], struct.unpack_from("<Q", data, cursor + index * 8)[0])
            cursor += 32
        result = sum(rol(values[index], (1, 7, 12, 18)[index]) for index in range(4)) & MASK64
        for value in values:
            result = ((result ^ xx_round(0, value)) * P1 + P4) & MASK64
    else:
        result = P5
    result = (result + size) & MASK64
    while cursor <= size - 8:
        result = (rol(result ^ xx_round(0, struct.unpack_from("<Q", data, cursor)[0]), 27) * P1 + P4) & MASK64
        cursor += 8
    if cursor <= size - 4:
        result = (rol(result ^ ((struct.unpack_from("<I", data, cursor)[0] * P1) & MASK64), 23) * P2 + P3) & MASK64
        cursor += 4
    while cursor < size:
        result = (rol(result ^ ((data[cursor] * P5) & MASK64), 11) * P1) & MASK64
        cursor += 1
    result ^= result >> 33
    result = (result * P2) & MASK64
    result ^= result >> 29
    result = (result * P3) & MASK64
    return (result ^ (result >> 32)) & MASK64


def fnv1a32(text):
    result = 2166136261
    for value in text.lower().encode("utf-8"):
        result = ((result ^ value) * 16777619) & 0xFFFFFFFF
    return result


class Zstd:
    def __init__(self, library):
        self.lib = ctypes.CDLL(str(library))
        self.lib.ZSTD_decompress.restype = ctypes.c_size_t
        self.lib.ZSTD_compress.restype = ctypes.c_size_t
        self.lib.ZSTD_compressBound.restype = ctypes.c_size_t

    def decompress(self, compressed, size):
        output = ctypes.create_string_buffer(size)
        source = ctypes.create_string_buffer(compressed)
        result = self.lib.ZSTD_decompress(output, size, source, len(compressed))
        if result != size:
            raise RuntimeError("Zstd decompression failed")
        return output.raw[:result]

    def compress(self, raw):
        capacity = self.lib.ZSTD_compressBound(len(raw))
        output = ctypes.create_string_buffer(capacity)
        source = ctypes.create_string_buffer(raw)
        result = self.lib.ZSTD_compress(output, capacity, source, len(raw), 9)
        if result > capacity:
            raise RuntimeError("Zstd compression failed")
        return output.raw[:result]


def read_wad(path, zstd):
    data = Path(path).read_bytes()
    if data[:2] != b"RW" or data[2] != 3:
        raise RuntimeError("Unsupported WAD")
    table = 272
    count = struct.unpack_from("<I", data, 268)[0]
    entries = {}
    for index in range(count):
        entry = struct.unpack_from("<QIII BBH 8s", data, table + index * 32)
        entries[entry[0]] = entry

    def raw(entry):
        payload = data[entry[1]:entry[1] + entry[2]]
        if entry[4] == 0:
            return payload
        if entry[4] == 3:
            return zstd.decompress(payload, entry[3])
        raise RuntimeError("Unsupported compression type: %s" % entry[4])
    return entries, raw


def create_wad(chunks, zstd):
    compressed = []
    for path_hash, raw in sorted(chunks.items()):
        payload = zstd.compress(raw)
        compressed.append((path_hash, raw, payload))
    data_offset = 272 + len(compressed) * 32
    if data_offset % 8:
        data_offset += 8 - data_offset % 8
    table, payloads, cursor = bytearray(), bytearray(), data_offset
    for path_hash, raw, payload in compressed:
        padding = (-cursor) % 8
        payloads.extend(b"\0" * padding)
        cursor += padding
        table.extend(struct.pack("<QIII BBH 8s", path_hash, cursor, len(payload), len(raw), 3, 0, 0,
                                 struct.pack("<Q", xxh64(raw))))
        payloads.extend(payload)
        cursor += len(payload)
    header = bytearray(b"RW\x03\x04" + b"\0" * 264)
    header.extend(struct.pack("<I", len(compressed)))
    result = header + table
    result.extend(b"\0" * (data_offset - len(result)))
    result.extend(payloads)
    return bytes(result)


def main():
    if len(sys.argv) != 8:
        raise SystemExit("usage: build_jade_classic.py WAD OUTPUT CHAMPION NAMESPACE SOURCE_SKIN NAME ZSTD_DLL")
    wad, output, champion, namespace, source_skin, display_name, zstd_dll = sys.argv[1:]
    source_skin = int(source_skin)
    zstd = Zstd(zstd_dll)
    entries, read_raw = read_wad(wad, zstd)
    source_path = "data/characters/%s/skins/skin%d.bin" % (namespace, source_skin)
    source_entry = entries.get(xxh64(source_path))
    if not source_entry:
        raise RuntimeError("Source skin not found: " + source_path)
    source = read_raw(source_entry)
    source_lower = source.lower()
    if b"classic" not in source_lower or b"project_jade.skn" not in source_lower:
        raise RuntimeError("Source is not the expected PBE Classic model")

    slots = [slot for slot in range(500)
             if xxh64("data/characters/%s/skins/skin%d.bin" % (namespace, slot)) in entries]
    chunks = {}
    source_object = "Characters/%s/Skins/Skin%d" % (namespace, source_skin)
    for slot in slots:
        if slot == source_skin:
            continue
        clone = source.replace(struct.pack("<I", fnv1a32(source_object)),
                               struct.pack("<I", fnv1a32("Characters/%s/Skins/Skin%d" % (namespace, slot))))
        clone = clone.replace(struct.pack("<I", fnv1a32(source_object + "/Resources")),
                              struct.pack("<I", fnv1a32("Characters/%s/Skins/Skin%d/Resources" % (namespace, slot))))
        chunks[xxh64("data/characters/%s/skins/skin%d.bin" % (namespace, slot))] = clone

    dependencies = sorted(set(match.decode("ascii") for match in re.findall(
        rb"DATA/Characters/" + re.escape(namespace.encode()) + rb"/[A-Za-z0-9_./]+?\.bin", source)))
    for dependency in dependencies:
        dependency_hash = xxh64(dependency)
        if dependency_hash not in entries:
            raise RuntimeError("Missing dependency in PBE WAD: " + dependency)
        chunks[dependency_hash] = read_raw(entries[dependency_hash])

    wad_data = create_wad(chunks, zstd)
    metadata = {
        "Name": display_name,
        "Author": "Xitfin",
        "Version": "0.6",
        "Description": "Forces %s Skin%d Classic on every %s skin with linked PBE BIN dependencies" %
                       (namespace, source_skin, champion)
    }
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr("META/info.json", json.dumps(metadata, indent=2))
        archive.writestr("WAD/%s.wad.client" % champion, wad_data)
    print(json.dumps({"slots": slots, "overrides": len(slots) - 1,
                      "dependencies": dependencies, "chunks": len(chunks), "output": str(output)}, indent=2))


if __name__ == "__main__":
    main()
