"""Run local targeted suites, saving partial progress. No full runs or exports."""
from concurrent.futures import ThreadPoolExecutor, as_completed
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
from workshop import checks_for_version

ROOT = Path(__file__).resolve().parent.parent

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package-dir', type=Path, help='Read-only verification of an already prepared exact candidate')
    args = parser.parse_args()
    binary = ROOT / 'PunchConstruct/bin/Release/net9.0/PunchConstruct.dll'
    if args.package_dir: binary = args.package_dir / 'PunchConstruct.dll'
    digest = hashlib.sha256(binary.read_bytes()).hexdigest()
    manifest = args.package_dir / 'PunchConstruct.json' if args.package_dir else ROOT / 'PunchConstruct/PunchConstruct.json'
    version = json.loads(manifest.read_text(encoding='utf-8'))['version']
    required_checks = checks_for_version(version)
    progress = ROOT / 'tools/runtime' / (('candidate-batch-' if args.package_dir else 'local-batch-') + version + '.json')
    state = dict(version=version, dll_sha256=digest, package_dir=str(args.package_dir) if args.package_dir else None, passed=False, checks={})

    def save():
        progress.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')

    def run(mode):
        command = [sys.executable, str(ROOT / 'tools/check_current.py'), mode, '--rendered']
        if args.package_dir: command += ['--package-dir', str(args.package_dir)]
        result = subprocess.run(command, cwd=ROOT,
            capture_output=True, text=True, encoding='utf-8', errors='replace')
        return mode, result.returncode, result.stdout + result.stderr

    def record(result):
        mode, code, output = result
        state['checks'][mode] = dict(passed=code == 0, exit_code=code)
        if code: state['checks'][mode]['failure_tail'] = output[-2500:]
        save()
        print(mode + ': ' + ('PASS' if code == 0 else 'FAIL'), flush=True)

    save()
    # Render- and timing-sensitive suites run alone; concurrent game processes
    # can make native resources load slowly enough to fail their own checks.
    serial = {'vigor', 'shadernative', 'architect'}
    with ThreadPoolExecutor(max_workers=3) as pool:
        for task in as_completed([pool.submit(run, mode) for mode in required_checks if mode not in serial]):
            record(task.result())
    for mode in ('vigor', 'shadernative', 'architect'):
        result = run(mode)
        if mode == 'shadernative' and result[1] != 0:
            # Real-renderer scene parsing has failed transiently after a long
            # batch on this machine; one isolated retry separates that from a
            # reproducible resource defect.
            print('shadernative: retrying after transient resource load failure', flush=True)
            result = run(mode)
        record(result)
    if digest != hashlib.sha256(binary.read_bytes()).hexdigest():
        raise SystemExit('Build changed during verification; partial results retained.')
    state['passed'] = len(state['checks']) == len(required_checks) and all(v['passed'] for v in state['checks'].values())
    save()
    subprocess.run([sys.executable, str(ROOT / 'tools/report_local.py')], cwd=ROOT, check=True)
    raise SystemExit(0 if state['passed'] else 1)

if __name__ == '__main__': main()
