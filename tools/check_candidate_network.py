"""Finish missing exact-candidate ENet checks only. Never uploads or runs campaigns."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
from release_evidence import collect

ROOT = Path(__file__).resolve().parent.parent
CASES = {
    'coop-construct-2p': ['check_coop.py', '--guest', 'construct'],
    'coop-necrobinder-3p': ['check_coop.py', '--guest', 'necrobinder', '--players', '3'],
    'coop-construct-4p': ['check_coop.py', '--guest', 'construct', '--players', '4'],
    'coop-construct-4p-network': ['check_coop.py', '--guest', 'construct', '--players', '4', '--latency-ms', '100', '--jitter-ms', '40', '--loss-percent', '5', '--blackout-seconds', '5'],
    'recovery-menu-2p': ['check_recovery.py'],
    'recovery-abrupt-2p': ['check_recovery.py', '--abrupt'],
    'recovery-process-loss-4p': ['check_recovery.py', '--kill-client', '--players', '4', '--resume-order', 'reverse'],
    'recovery-host-loss-3p': ['check_recovery.py', '--kill-host', '--players', '3', '--party', 'mixed', '--resume-order', 'reverse'],
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package-dir', type=Path, default=Path('dist/SteamWorkshop/content'))
    parser.add_argument('--headless', action='store_true', help='Use the dummy renderer for the network/recovery diagnostics')
    args = parser.parse_args()
    candidate = args.package_dir.resolve()
    files = {n: hashlib.sha256((candidate / n).read_bytes()).hexdigest() for n in ('PunchConstruct.dll', 'PunchConstruct.pck', 'PunchConstruct.json')}
    version = json.loads((candidate / 'PunchConstruct.json').read_text(encoding='utf-8'))['version']
    output = ROOT / 'tools/runtime' / ('candidate-network-' + version + '.json')
    state = dict(version=version, package_files=files, passed=False, checks={})
    for key, command in CASES.items():
        evidence, missing = collect(files)
        if key not in missing:
            state['checks'][key] = dict(passed=True, reused_exact_evidence=True,
                evidence=next(row['directory'] for row in evidence if row['mode'] == key))
        else:
            log = ROOT / 'tools/runtime' / ('candidate-' + version + '-' + key + '.txt')
            with log.open('wb') as stream:
                result = subprocess.run([sys.executable, str(ROOT / 'tools' / command[0]), *command[1:], '--package-dir', str(candidate)]
                    + (['--headless'] if args.headless else []),
                    cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
            evidence, missing = collect(files)
            state['checks'][key] = dict(passed=result.returncode == 0 and key not in missing, exit_code=result.returncode,
                output=log.relative_to(ROOT).as_posix())
        output.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
        print(key + ': ' + ('PASS' if state['checks'][key]['passed'] else 'FAIL'), flush=True)
    assert files == {n: hashlib.sha256((candidate / n).read_bytes()).hexdigest() for n in files}, 'Candidate changed during QA'
    state['passed'] = all(row['passed'] for row in state['checks'].values())
    output.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    raise SystemExit(0 if state['passed'] else 1)


if __name__ == '__main__': main()
