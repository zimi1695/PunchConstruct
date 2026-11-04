"""Verify selected textures against the installed source pack; never changes images."""
import argparse
import hashlib
import json
from pathlib import Path
import re
from pck import index

ROOT = Path(__file__).resolve().parent.parent
SOURCE = Path('D:/game/steam/steamapps/workshop/content/2868840/3747526116')
MANIFEST = ROOT / '资料/美术素材/观者搬运素材.json'


def read_entry(stream, entries, name):
    offset, length, digest, flags = entries[name]
    assert flags == 0, name
    stream.seek(offset)
    raw = stream.read(length)
    assert hashlib.md5(raw).hexdigest() == digest, name
    return raw


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--record', action='store_true', help='Record exact import and texture paths in the existing provenance list')
    args = parser.parse_args()
    rows = json.loads(MANIFEST.read_text(encoding='utf-8'))
    source_manifest = json.loads((SOURCE / 'Watcher.json').read_text(encoding='utf-8-sig'))
    assert source_manifest['version'] == '0.10.3'
    pack = SOURCE / 'Watcher.pck'
    entries = index(pack)
    native = index(ROOT / 'tools/runtime/SlayTheSpire2.pck')
    native_textures = {}
    for name, (_, length, digest, flags) in native.items():
        if name.endswith('.ctex') and flags == 0:
            native_textures.setdefault((length, digest), []).append(name)
    with pack.open('rb') as stream:
        for row in rows:
            import_path = row['source_path'] + '.import'
            settings = read_entry(stream, entries, import_path).decode('utf-8')
            texture_path = re.search(r'^path="res://([^\"]+)"', settings, re.M)[1]
            raw = read_entry(stream, entries, texture_path)
            digest = hashlib.sha256(raw).hexdigest()
            assert digest == row['sha256'] == hashlib.sha256((ROOT / row['output']).read_bytes()).hexdigest(), row['class']
            matches = native_textures.get((len(raw), hashlib.md5(raw).hexdigest()), [])
            row.update(source_import_path=import_path, source_texture_path=texture_path,
                       source_pack_match=True, identical_sts2_texture_paths=matches)
            print('PASS:', row['class'], 'exact Watcher texture; identical STS2 textures:', len(matches))
    if args.record:
        MANIFEST.write_text(json.dumps(rows, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('PASS:', len(rows), 'selected textures; upstream authorship is not established by byte matching.')


if __name__ == '__main__':
    main()
