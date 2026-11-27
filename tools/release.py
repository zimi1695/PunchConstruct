"""Build or check the deterministic three-file release ZIP. Never uploads."""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent.parent
NAMES = ('PunchConstruct.dll', 'PunchConstruct.pck', 'PunchConstruct.json')
FIXED_TIME = (1980, 1, 1, 0, 0, 0)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def build_zip(source):
    """Return deterministic ZIP bytes for the exact three-file candidate."""
    if {p.name for p in source.iterdir()} != set(NAMES):
        raise SystemExit(f'Release source must contain exactly {NAMES}: {source}')
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in NAMES:
            data = (source / name).read_bytes()
            info = zipfile.ZipInfo(name, date_time=FIXED_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 0
            info.external_attr = 0
            archive.writestr(info, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    return stream.getvalue()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-dir', type=Path, default=ROOT / 'dist/SteamWorkshop/content',
                        help='Exact three-file candidate to package (default: dist/SteamWorkshop/content)')
    parser.add_argument('--output', type=Path,
                        help='ZIP path (default: dist/releases/PunchConstruct-<version>.zip)')
    parser.add_argument('--check', action='store_true', help='Only verify that the existing ZIP is byte-reproducible')
    parser.add_argument('--force', action='store_true', help='Rewrite an existing ZIP that differs')
    args = parser.parse_args()
    source = args.source_dir.resolve()
    manifest = json.loads((source / 'PunchConstruct.json').read_text(encoding='utf-8-sig'))
    output = (args.output or ROOT / 'dist/releases' / ('PunchConstruct-' + manifest['version'] + '.zip')).resolve()
    payload = build_zip(source)
    digest = sha(payload)
    files = {name: sha((source / name).read_bytes()) for name in NAMES}
    if args.check:
        if not output.exists():
            raise SystemExit(f'Missing release ZIP: {output}')
        if output.read_bytes() != payload:
            raise SystemExit(f'Release ZIP is not reproducible from {source}: {output}')
        print(json.dumps(dict(status='PASS', output=output.as_posix(), bytes=len(payload),
                              sha256=digest, files=files), indent=2))
        return
    if output.exists() and not args.force:
        if output.read_bytes() != payload:
            raise SystemExit(f'Release ZIP differs; rerun with --force to replace: {output}')
        print(json.dumps(dict(status='UNCHANGED', output=output.as_posix(), bytes=len(payload),
                              sha256=digest, files=files), indent=2))
        return
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=output.parent, prefix=output.name + '.', suffix='.tmp', delete=False) as stream:
        stream.write(payload)
        temporary = Path(stream.name)
    try:
        os.replace(temporary, output)
    finally:
        if temporary.exists():
            temporary.unlink()
    print(json.dumps(dict(status='WRITTEN', output=output.as_posix(), bytes=len(payload),
                          sha256=digest, files=files), indent=2))


if __name__ == '__main__':
    main()
