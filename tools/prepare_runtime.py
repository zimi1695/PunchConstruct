"""Prepare a disposable test copy. Never writes to the installed game or real saves."""
import hashlib, io, json, shutil, struct
from pathlib import Path
from pck import index

ROOT = Path(__file__).resolve().parent.parent
GAME = Path(r'D:\game\steam\steamapps\common\Slay the Spire 2')
RUNTIME = ROOT / 'tools/runtime'

def patch_settings(data):
    f = io.BytesIO(data)
    if f.read(4) != b'ECFG': raise ValueError('Unexpected settings format')
    n, = struct.unpack('<I', f.read(4)); records = {}
    for _ in range(n):
        name = f.read(struct.unpack('<I', f.read(4))[0]).decode()
        records[name] = f.read(struct.unpack('<I', f.read(4))[0])
    def string(value):
        raw = value.encode(); return struct.pack('<II', 4, len(raw)) + raw + b'\0' * (-len(raw) % 4)
    records['application/config/custom_user_dir_name'] = string('PunchConstructTest')
    # Smoke runs use local files only, without the crash reporter autoload.
    records.pop('autoload/SentryBootstrap', None)
    result = b'ECFG' + struct.pack('<I', len(records))
    for name, value in records.items():
        raw = name.encode(); result += struct.pack('<I', len(raw)) + raw + struct.pack('<I', len(value)) + value
    return result

if __name__ == '__main__':
    RUNTIME.mkdir(parents=True, exist_ok=True)
    for name in ['SlayTheSpire2.exe', 'release_info.json']:
        if not (RUNTIME/name).exists(): shutil.copy2(GAME/name, RUNTIME/name)
    for f in GAME.glob('*.dll'):
        if not (RUNTIME/f.name).exists(): shutil.copy2(f, RUNTIME/f.name)
    shutil.copytree(GAME/'data_sts2_windows_x86_64', RUNTIME/'data_sts2_windows_x86_64', dirs_exist_ok=True)
    pack = GAME/'SlayTheSpire2.pck'; entries = index(pack)
    with pack.open('rb') as f:
        offset, size, *_ = entries['project.binary']; f.seek(offset); settings = patch_settings(f.read(size))
    dest = RUNTIME/'SlayTheSpire2.pck'
    if not dest.exists(): shutil.copyfile(pack, dest)
    with dest.open('r+b') as f:
        f.seek(24); base, = struct.unpack('<Q', f.read(8))
        f.seek(0, 2); offset = f.tell(); f.write(settings)
        entries['project.binary'] = (offset, len(settings), hashlib.md5(settings).hexdigest(), 0)
        directory = f.tell(); f.write(struct.pack('<I', len(entries)))
        for name, (offset, size, digest, flags) in entries.items():
            raw = name.encode(); raw += b'\0' * (-len(raw) % 4)
            f.write(struct.pack('<I', len(raw))); f.write(raw)
            f.write(struct.pack('<QQ', offset-base, size)); f.write(bytes.fromhex(digest)); f.write(struct.pack('<I', flags))
        f.seek(32); f.write(struct.pack('<Q', directory))
    mods = RUNTIME/'mods'; mods.mkdir(exist_ok=True)
    shutil.copytree(ROOT/'dist/PunchConstruct', mods/'PunchConstruct', dirs_exist_ok=True)
    base_lib = Path(r'D:\game\steam\steamapps\workshop\content\2868840\3737335127\BaseLib')
    shutil.copytree(base_lib, mods/'BaseLib', dirs_exist_ok=True)
    # Observer cards and their support are implemented by PunchConstruct itself.
    (RUNTIME/'override.cfg').write_text('[application]\nconfig/use_custom_user_dir=true\nconfig/custom_user_dir_name="PunchConstructTest"\n', encoding='utf-8')
    print('Prepared isolated runtime:', RUNTIME)
