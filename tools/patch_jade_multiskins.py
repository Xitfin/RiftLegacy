import copy
import glob
import json
import sys
import tempfile
import zipfile
from pathlib import Path

import build_jade_classic as jade
from port_legacy_base_skin import wad_from_bytes


def main():
    if len(sys.argv) != 7:
        raise SystemExit("usage: patch_jade_multiskins.py PACKAGE PBE_WAD REP_DIR HASH_DIR ZSTD PYRITO_ROOT")
    package, pbe_wad, rep_dir, hash_dir, zstd_dll, pyrito_root = sys.argv[1:]
    sys.path.insert(0, pyrito_root)
    import pyRitoFile

    zstd = jade.Zstd(zstd_dll)
    entries, game_raw = jade.read_wad(pbe_wad, zstd)
    lookup = {}
    for filename in glob.glob(str(Path(hash_dir) / "hashes.game.txt.*")):
        for line in Path(filename).read_text(encoding="utf8", errors="ignore").splitlines():
            try:
                digest, path = line.split(" ", 1)
                lookup[int(digest, 16)] = path
            except ValueError:
                pass

    with zipfile.ZipFile(package) as archive:
        wad_name = next(name for name in archive.namelist() if name.lower().endswith(".wad.client"))
        chunks = wad_from_bytes(archive.read(wad_name), zstd)

    rep_dir = Path(rep_dir)
    custom_entries = {}
    custom_links = set()
    for skin_bin in (rep_dir / "data/characters/jade_ashe/skins").glob("skin*.bin"):
        parsed = pyRitoFile.bin.BIN().read(str(skin_bin))
        for entry in parsed.entries:
            custom_entries[entry.hash] = copy.deepcopy(entry)
        custom_links.update(parsed.links)

    targets = []
    for path_hash, path in lookup.items():
        lower = path.lower()
        if path_hash not in entries or not lower.endswith(".bin"):
            continue
        if lower == "data/characters/jade_ashe/jade_ashe.bin" or (
                lower.startswith("data/characters/jade_ashe/jade_ashe_multi_skins") and "multi_skins" in lower):
            targets.append((path_hash, path))

    patched_files = 0
    replaced_entries = 0
    with tempfile.TemporaryDirectory(prefix="csm-ashe-multi-") as temp:
        for path_hash, path in targets:
            source = Path(temp) / ("%016x.bin" % path_hash)
            source.write_bytes(game_raw(entries[path_hash]))
            parsed = pyRitoFile.bin.BIN().read(str(source))
            changed = 0
            for index, entry in enumerate(parsed.entries):
                replacement = custom_entries.get(entry.hash)
                if replacement is not None:
                    parsed.entries[index] = copy.deepcopy(replacement)
                    changed += 1
            if not changed:
                continue
            for link in custom_links:
                if link not in parsed.links:
                    parsed.links.append(link)
            parsed.write(str(source))
            chunks[path_hash] = source.read_bytes()
            patched_files += 1
            replaced_entries += changed
            print(path, "entries", changed)

    if not patched_files:
        raise RuntimeError("No Jade Ashe Multi_Skins entries were replaced")
    with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr("META/info.json", json.dumps({
            "Name": "Classic Ashe Season 1 - PBE Multi_Skins",
            "Author": "AryasDemise / Xitfin",
            "Version": "0.6",
            "Description": "Old Ashe repathed to Jade_Ashe with patched PBE Multi_Skins routing"
        }, indent=2))
        archive.writestr(wad_name, jade.create_wad(chunks, zstd))
    print(json.dumps({"targets": len(targets), "patched_files": patched_files,
                      "replaced_entries": replaced_entries, "chunks": len(chunks)}, indent=2))


if __name__ == "__main__":
    main()
