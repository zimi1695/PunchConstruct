"""Independent native event control, without loading the character mod."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import argparse
from check_current import qa_issues

ROOT = Path(__file__).resolve().parent.parent
RUNTIME = ROOT / 'tools/runtime'

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release-pools', action='store_true')
    parser.add_argument('--verbose', action='store_true')
    args = parser.parse_args()
    parent = Path(tempfile.gettempdir()).resolve()
    stage = Path(tempfile.mkdtemp(prefix='NativeProbe-check-', dir=parent)).resolve()
    evidence = RUNTIME / 'qa' / ('native-events-' + time.strftime('%Y%m%d-%H%M%S') + '-' + str(time.time_ns() % 1000000000))
    evidence.mkdir()
    log = stage / 'native-events.log'
    report = ''
    try:
        def link(a, b):
            b.parent.mkdir(parents=True, exist_ok=True)
            os.link(a, b)
        for p in RUNTIME.iterdir():
            if p.is_file() and (p.suffix.lower() in ('.dll', '.exe', '.pck') or p.name in ('override.cfg', 'release_info.json')):
                link(p, stage / p.name)
        for tree in ('data_sts2_windows_x86_64', 'mods/BaseLib'):
            for p in (RUNTIME / tree).rglob('*'):
                if p.is_file(): link(p, stage / tree / p.relative_to(RUNTIME / tree))
        mod = stage / 'mods/NativeProbe'
        mod.mkdir()
        shutil.copy2(ROOT / 'tools/NativeProbe/bin/Release/net9.0/NativeProbe.dll', mod / 'NativeProbe.dll')
        shutil.copy2(ROOT / 'tools/NativeProbe/NativeProbe.json', mod / 'NativeProbe.json')
        settings = stage / 'userdata/PunchConstructTest/default/1/settings.save'
        settings.parent.mkdir(parents=True)
        settings.write_text(json.dumps(dict(schema_version=8, volume_master=0, volume_bgm=0, volume_sfx=0, volume_ambience=0,
            language='zhs', mod_settings=dict(mods_enabled=True, mod_list=[]), seen_ea_disclaimer=True, skip_intro_logo=True)), encoding='utf-8')
        env = os.environ.copy()
        env['APPDATA'] = str(stage / 'userdata')
        command = [str(stage / 'SlayTheSpire2.exe'), '--force-steam=off', '--rendering-method', 'gl_compatibility',
            '--display-driver', 'windows', '--resolution', '1280x720', '--log-file', str(log), '--quit-after', '10800']
        if args.release_pools: command.append('--nativeprobe-release-pools')
        if args.verbose: command.append('--verbose')
        result = subprocess.run(command, cwd=stage, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=120, creationflags=subprocess.CREATE_NO_WINDOW)
        report = log.read_text(encoding='utf-8-sig', errors='replace')
        for line in report.splitlines():
            if '[NativeProbe' in line or 'Buffer with GL ID' in line: print(line)
        issues = qa_issues(report)
        isolated = '[NativeProbe ISOLATION]' in report and 'Calling initializer method of type PunchConstruct' not in report
        passed = result.returncode == 0 and '[NativeProbe PASS]' in report and isolated and not issues
        (evidence / 'result.json').write_text(json.dumps(dict(passed=passed, isolated=isolated, exit_code=result.returncode,
            probe_sha256=hashlib.sha256((mod / 'NativeProbe.dll').read_bytes()).hexdigest(), issues=issues), indent=2), encoding='utf-8')
        print('Evidence:', evidence)
        if not passed: raise SystemExit('Native control has diagnostics; preserved without suppressing them.')
    finally:
        if log.exists(): shutil.copy2(log, evidence / log.name)
        assert stage.parent == parent and stage.name.startswith('NativeProbe-check-')
        shutil.rmtree(stage)

if __name__ == '__main__': main()
