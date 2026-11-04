"""Build an unencrypted Godot 4 pack without an editor or exported game assets."""
import argparse, hashlib, json, struct
from pathlib import Path

def write_pack(destination, files):
    # Godot pack format 2: fixed header, directory, aligned payloads.
    records = []
    for name, data in sorted(files.items()):
        raw = name.encode(); raw += b'\0' * (-len(raw) % 4)
        records.append((raw, data))
    directory_size = sum(4 + len(name) + 8 + 8 + 16 + 4 for name, _ in records)
    position = (100 + directory_size + 15) // 16 * 16
    offsets = []
    for name, data in records:
        offsets.append(position); position = (position + len(data) + 15) // 16 * 16
    destination = Path(destination); destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open('wb') as f:
        f.write(struct.pack('<6IQ', 0x43504447, 2, 4, 5, 1, 0, 0))
        f.write(b'\0' * 64); f.write(struct.pack('<I', len(records)))
        for (name, data), offset in zip(records, offsets):
            f.write(struct.pack('<I', len(name))); f.write(name)
            f.write(struct.pack('<QQ', offset, len(data)))
            f.write(hashlib.md5(data).digest()); f.write(struct.pack('<I', 0))
        for (_, data), offset in zip(records, offsets):
            f.write(b'\0' * (offset - f.tell())); f.write(data)
    return len(records)

if __name__ == '__main__':
    p = argparse.ArgumentParser(); p.add_argument('--configuration', default='Debug'); a = p.parse_args()
    root = Path(__file__).resolve().parent.parent
    src = root / 'PunchConstruct'; dest = root / 'dist/PunchConstruct'; dest.mkdir(parents=True, exist_ok=True)
    dll = src / f'bin/{a.configuration}/net9.0/PunchConstruct.dll'
    if not dll.exists(): raise SystemExit('Build the project first.')
    files = {'PunchConstruct/'+f.relative_to(src/'assets').as_posix():f.read_bytes() for f in (src/'assets').rglob('*') if f.is_file()}
    count = write_pack(dest/'PunchConstruct.pck', files)
    (dest/'PunchConstruct.dll').write_bytes(dll.read_bytes())
    (dest/'PunchConstruct.json').write_bytes((src/'PunchConstruct.json').read_bytes())
    print(f'Packaged {count} resources in {dest}')
