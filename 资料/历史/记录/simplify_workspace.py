"""Flatten active materials and keep the old process in one verified history archive."""
import hashlib
import json
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parent.parent
art = root/'资料/美术素材'
data = root/'资料'

def checked(path):
    path = path.resolve()
    assert path.is_relative_to(root), path
    return path

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

icons = json.loads((art/'状态图标/图标清单.json').read_text(encoding='utf-8'))
for item in icons:
    source = checked(root/item['size64'])
    target = checked(art/'状态图标'/(item['name']+'.png'))
    assert not target.exists()
    target.write_bytes(source.read_bytes())
    assert sha(target)==sha(source)
    item.update(original=target.relative_to(root).as_posix(),size64=target.relative_to(root).as_posix(),status='已通过',game_linked=True)

archive_dirs = [data/'历史资料', art/'历史稿', art/'制作记录', art/'状态图标/已选', art/'状态图标/待审', art/'状态图标/64像素']
archive_files = [p for folder in archive_dirs for p in folder.rglob('*') if p.is_file()]
archive_files += [data/'设定/README.md',root/'tools/organize_workspace.py',Path(__file__)]
archive = checked(data/'历史存档.zip')
assert not archive.exists()
hashes = {checked(p):sha(p) for p in archive_files}
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED) as z:
    for path in hashes:
        z.write(path,path.relative_to(root).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for path,digest in hashes.items():
        assert hashlib.sha256(z.read(path.relative_to(root).as_posix())).hexdigest()==digest
for path in hashes:
    checked(path).unlink()
for folder in archive_dirs:
    for path in sorted(folder.rglob('*'),key=lambda p:len(p.parts),reverse=True):
        if path.is_dir():
            checked(path).rmdir()
    checked(folder).rmdir()

for path in list((data/'设定').iterdir()):
    if not path.is_file(): continue
    dest=checked(data/path.name)
    assert not dest.exists()
    checked(path).rename(dest)
checked(data/'设定').rmdir()
(art/'状态图标/图标清单.json').write_text(json.dumps(icons,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Active documents flattened, all 9 icons approved, full-size originals/process safely archived. No game or release files changed yet.')
