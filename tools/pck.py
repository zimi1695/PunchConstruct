"""Read a local Godot pack index and extract selected resources into the workspace."""
import argparse, hashlib, json, re, struct
from pathlib import Path

def index(path):
    with open(path, 'rb') as f:
        magic, version, major, minor, patch = struct.unpack('<5I', f.read(20))
        if magic != 0x43504447: raise ValueError('Not a Godot pack')
        flags, base = struct.unpack('<IQ', f.read(12)) if version >= 2 else (0, 0)
        directory = struct.unpack('<Q', f.read(8))[0] if version >= 3 else None
        f.read(64)
        if directory is not None: f.seek(directory)
        count, = struct.unpack('<I', f.read(4))
        entries = {}
        for _ in range(count):
            size, = struct.unpack('<I', f.read(4))
            name = f.read(size).rstrip(b'\0').decode()
            offset, length = struct.unpack('<QQ', f.read(16))
            digest = f.read(16).hex()
            entry_flags, = struct.unpack('<I', f.read(4)) if version >= 2 else (0,)
            entries[name] = (base + offset, length, digest, entry_flags)
        return entries

if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('pack'); p.add_argument('pattern'); p.add_argument('--out')
    a = p.parse_args()
    entries = index(a.pack)
    hits = {k:v for k,v in entries.items() if re.search(a.pattern, k, re.I)}
    if a.out:
        root = Path(a.out).resolve(); root.mkdir(parents=True, exist_ok=True)
        with open(a.pack, 'rb') as f:
            for k, (offset, size, digest, flags) in hits.items():
                dest = (root / k.removeprefix('res://')).resolve()
                if not dest.is_relative_to(root): raise ValueError(k)
                f.seek(offset); data = f.read(size)
                if hashlib.md5(data).hexdigest() != digest: raise ValueError('Checksum: '+k)
                dest.parent.mkdir(parents=True, exist_ok=True); dest.write_bytes(data)
    print(json.dumps(hits, ensure_ascii=False, indent=2))
