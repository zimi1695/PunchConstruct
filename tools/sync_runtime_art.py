"""Make local play resources match workspace exactly; never modify dist or ZIPs."""
from pathlib import Path
from pck import index
from package import write_pack

root = Path(__file__).resolve().parent.parent
pack = root / 'tools/runtime/mods/PunchConstruct/PunchConstruct.pck'
assets = root / 'PunchConstruct/assets'
resources = {'PunchConstruct/' + path.relative_to(assets).as_posix(): path.read_bytes()
             for path in assets.rglob('*') if path.is_file()}
matches = False
if pack.exists():
    entries = index(pack)
    matches = set(entries) == set(resources)
    if matches:
        with pack.open('rb') as source:
            for name, (offset, size, digest, flags) in entries.items():
                source.seek(offset)
                if flags != 0 or source.read(size) != resources[name]:
                    matches = False
                    break
if not matches:
    pack.parent.mkdir(parents=True, exist_ok=True)
    write_pack(pack, resources)
print('Local play resources match workspace exactly:', len(resources), 'files.')
