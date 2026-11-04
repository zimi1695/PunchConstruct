"""Select exact-candidate multiplayer evidence. No upload or game mutation."""
import hashlib
import json
from pathlib import Path
import re
import shutil
from check_current import qa_issues

ROOT = Path(__file__).resolve().parent.parent
REQUIRED = ('coop-construct-2p', 'coop-necrobinder-3p', 'coop-construct-4p',
            'coop-construct-4p-network', 'recovery-menu-2p', 'recovery-abrupt-2p', 'recovery-process-loss-4p', 'recovery-host-loss-3p')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def collect(build, archive=None):
    selected = {}
    for path in sorted((ROOT / 'tools/runtime/qa').glob('*/result.json')):
        row = json.loads(path.read_text(encoding='utf-8'))
        if row.get('build') != build:
            continue
        key = None
        if row.get('scenario') == 'coop':
            key = 'coop-{}-{}p'.format(row['guest'], row['players']) + ('-network' if 'network' in row else '')
        elif row.get('scenario') == 'recovery' and row.get('character') == 'construct' and not row.get('live_join') and not row.get('independent'):
            key = 'recovery-{}-{}p'.format(row['disconnect'], row.get('players', 2))
        if key not in REQUIRED:
            continue
        passed = row.get('passed', False)
        logs = [p for p in path.parent.glob('*.log') if not p.stem.endswith('-console')]
        passed &= bool(logs) and all(not qa_issues(p.read_text(encoding='utf-8-sig', errors='replace')) for p in logs)
        if row['scenario'] == 'coop':
            versions = {match.group(1) for log in logs for match in re.finditer(
                r'\[PunchConstruct\] v(\d+\.\d+\.\d+) loaded', log.read_text(encoding='utf-8-sig', errors='replace'))}
            phases = {'combat', 'marks', 'divinity', 'turn', 'temporary-strength-expired',
                      'choices', 'rewards', 'concurrent', 'survivor'}
            if len(versions) == 1 and tuple(map(int, next(iter(versions)).split('.'))) >= (0, 4, 20):
                phases |= {'boxing-generated', 'boxing-resolved'}
            if len(versions) == 1 and tuple(map(int, next(iter(versions)).split('.'))) >= (0, 4, 22):
                phases.add('pandora')
            passed &= len(versions) == 1 and set(row.get('snapshots', {})) == phases and all(row['snapshots'].values())
            passed &= len(row.get('peers', {})) == row['players'] and all(p.get('exit_code') == 0 and not p.get('errors') for p in row['peers'].values())
        else:
            passed &= row.get('snapshots_match', False) and row.get('resume_passed', False)
            passed &= set(row.get('phases', {})) == {'seed', 'resume'} and all(p['passed'] for p in row['phases'].values())
        selected[key] = dict(source=path.parent, passed=bool(passed))
    evidence = []
    for key, value in selected.items():
        folder = value['source']
        if archive is not None:
            target = archive / key
            # Only QA logs/snapshots/isolated saves, outside uploaded content.
            assert archive.resolve().is_relative_to(ROOT.resolve()) and target.resolve().parent == archive.resolve() and key in REQUIRED
            if target.exists(): shutil.rmtree(target)
            shutil.copytree(folder, target)
            folder = target
        evidence.append(dict(mode=key, passed=value['passed'], directory=folder.relative_to(ROOT).as_posix(),
            files={p.relative_to(folder).as_posix(): sha(p) for p in sorted(folder.rglob('*')) if p.is_file()}))
    missing = [key for key in REQUIRED if key not in selected or not selected[key]['passed']]
    return evidence, missing
