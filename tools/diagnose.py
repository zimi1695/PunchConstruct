"""Read local PunchConstruct JSONL logs; no uploads or changes to game files."""
import argparse
from collections import Counter
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', nargs='?', type=Path,
        default=ROOT / 'tools/runtime/play-userdata/PunchConstructTest/PunchConstruct/logs')
    parser.add_argument('--events', action='store_true', help='Print the complete chronological event stream')
    parser.add_argument('--session', help='Restrict output to one process session')
    args = parser.parse_args()
    files = [args.path] if args.path.is_file() else sorted(args.path.rglob('pc-*.jsonl'))
    rows, malformed = [], []
    for file in files:
        for index, line in enumerate(file.read_text(encoding='utf-8-sig').splitlines(), 1):
            try:
                value = json.loads(line)
                if not args.session or value.get('session') == args.session:
                    rows.append(value)
            except ValueError:
                malformed.append(dict(file=str(file), line=index))
    rows.sort(key=lambda r: (r['utc'], r['session'], r['seq']))
    sessions = sorted({r['session'] for r in rows})
    print(json.dumps(dict(files=len(files), records=len(rows), sessions=sessions,
        builds=[r['data'] for r in rows if r['event'] == 'session.start'],
        event_counts=dict(Counter(r['event'] for r in rows)), malformed_lines=malformed), ensure_ascii=False, indent=2))
    for row in rows:
        slow = row['event'] == 'action.end' and (row.get('data') or {}).get('elapsedMs', 0) is not None and row['data'].get('elapsedMs', 0) >= 5000
        if args.events or row['level'] in ('warn', 'error') or slow:
            print(json.dumps(row, ensure_ascii=False))
    if not rows:
        raise SystemExit('No structured logs at: ' + str(args.path))


if __name__ == '__main__':
    main()
