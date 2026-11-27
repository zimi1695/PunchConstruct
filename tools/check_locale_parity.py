"""Gate: every shipped locale must carry the same key set as eng for all 4 tables.

prepare_text.py already enforces zhs/eng parity at build time and mirrors the
other locales, so this normally passes. It is an independent standalone gate that
catches a locale table edited outside the pipeline (a new key added only to
zhs/eng). DoD: add a key to zhs only -> this exits non-zero.
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BASE = ROOT / 'PunchConstruct/assets/localization'
TABLES = ['ancients', 'events', 'relics', 'cards']


def keys(lang, table):
    path = BASE / lang / (table + '.json')
    return set(json.loads(path.read_text(encoding='utf-8-sig'))) if path.is_file() else None


def main():
    langs = sorted(d.name for d in BASE.iterdir() if d.is_dir())
    problems = []
    for table in TABLES:
        eng = keys('eng', table)
        if eng is None:
            problems.append('eng/%s missing' % table)
            continue
        for lang in langs:
            if lang == 'eng':
                continue
            current = keys(lang, table)
            if current is None:
                problems.append('%s/%s missing file' % (lang, table))
                continue
            missing = eng - current
            extra = current - eng
            if missing:
                problems.append('%s/%s missing keys: %s' % (lang, table, sorted(missing)))
            if extra:
                problems.append('%s/%s extra keys: %s' % (lang, table, sorted(extra)))
    if problems:
        print('LOCALE PARITY FAIL:')
        for problem in problems:
            print('  ' + problem)
        sys.exit(1)
    print('LOCALE PARITY PASS: %d locales x %d tables aligned to eng' % (len(langs), len(TABLES)))


if __name__ == '__main__':
    main()