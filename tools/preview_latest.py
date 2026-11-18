"""Latest-version isolated pre-check: reuse check_current.py against a disposable
runtime copy (default tools/runtime-0.113.1) without touching the frozen 0.113.0
official QA baseline. Writes <mode>-0.113.1-preview.json/log evidence in tools/runtime."""
import argparse, json, shutil, subprocess, sys, time
from pathlib import Path
from check_current import MODES, qa_issues

ROOT = Path(__file__).resolve().parent.parent
RUNTIME = ROOT / 'tools/runtime'

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('modes', nargs='*', default=['startup', 'smoke', 'additions', 'pool', 'modes', 'selection'])
    p.add_argument('--runtime-dir', type=Path, default=ROOT / 'tools/runtime-0.113.1')
    p.add_argument('--package-dir', type=Path, default=ROOT / 'dist/SteamWorkshop/content')
    p.add_argument('--tag', default='0.113.1-preview')
    a = p.parse_args()
    runtime = a.runtime_dir.resolve()
    version = json.loads((runtime / 'release_info.json').read_text(encoding='utf-8'))['version']
    results = []
    for mode in a.modes:
        marker = MODES[mode][1]
        start = time.time()
        command = [sys.executable, str(ROOT / 'tools/check_current.py'), mode, '--rendered',
                   '--package-dir', str(a.package_dir), '--runtime-dir', str(runtime)]
        proc = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')
        elapsed = round(time.time() - start, 1)
        log = runtime / (mode + '-current-check.log')
        value = log.read_text(encoding='utf-8-sig', errors='replace') if log.exists() else ''
        issues = qa_issues(value)
        criteria = dict(returncode_zero=proc.returncode == 0, marker=marker in value,
                        no_qa_fail=' QA FAIL]' not in value and ' SMOKE FAIL]' not in value,
                        no_issues=not issues)
        passed = all(criteria.values())
        preview_log = RUNTIME / (mode + '-' + a.tag + '.log')
        if log.exists(): shutil.copy2(log, preview_log)
        record = dict(mode=mode, game_version=version, elapsed_seconds=elapsed, returncode=proc.returncode,
                      marker=marker, criteria=criteria, issues=issues, passed=passed,
                      log=str(preview_log))
        (RUNTIME / (mode + '-' + a.tag + '.json')).write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        results.append(record)
        print(f'{mode} ({version}): {"PASS" if passed else "FAIL"} [{elapsed}s]', flush=True)
        if not passed:
            sys.stdout.buffer.write(((proc.stdout + proc.stderr)[-1500:]).encode('utf-8', 'replace') + b'\n')
            sys.stdout.flush()
    failed = [r['mode'] for r in results if not r['passed']]
    print('Latest-version pre-check:', 'all PASS' if not failed else 'FAILED ' + ', '.join(failed))
    raise SystemExit(1 if failed else 0)

if __name__ == '__main__':
    main()