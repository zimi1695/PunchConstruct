"""Native disconnect/join UI and fresh-process multiplayer save recovery; QA only."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import tempfile
import time
from check_current import qa_issues, window_position
from qa_logs import archive_diagnostics
from package import write_pack

ROOT = Path(__file__).resolve().parent.parent
RUNTIME = ROOT / 'tools/runtime'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--native-control', action='store_true', help='All players use native Ironclad; character mod remains loaded')
    parser.add_argument('--abrupt', action='store_true', help='Force immediate transport close, retaining native disconnect diagnostics')
    parser.add_argument('--kill-client', action='store_true', help='Terminate only this runner\'s guest process without a graceful disconnect')
    parser.add_argument('--kill-host', action='store_true', help='Terminate only this runner\'s host process and check native guest disconnect handling')
    parser.add_argument('--live-join', action='store_true', help='Diagnostic only: use native join UI while the host run remains active')
    parser.add_argument('--independent', action='store_true', help='Exclude PunchConstruct entirely; requires a NativeOnly QA build')
    parser.add_argument('--players', type=int, choices=(2, 3, 4), default=2)
    parser.add_argument('--party', choices=('construct', 'mixed'), default='construct', help='Mixed: construct host, Necrobinder guest, remaining Ironclad guests')
    parser.add_argument('--resume-order', choices=('same', 'reverse'), default='same', help='Reverse guest arrival order when regrouping from the saved run')
    parser.add_argument('--package-dir', type=Path, help='Read-only test of the exact three files in an existing candidate')
    parser.add_argument('--headless', action='store_true', help='Use the dummy renderer for diagnostics; real rendering is the default')
    args = parser.parse_args()
    if sum((args.abrupt, args.kill_client, args.kill_host)) > 1:
        parser.error('Choose one disconnect mode')
    if (args.native_control or args.independent) and args.party != 'construct':
        parser.error('Native controls cannot use a construct mixed party')
    if args.independent and args.package_dir:
        parser.error('Independent control excludes the character mod and cannot verify a candidate')
    if args.independent: args.native_control = True
    roles = ['host', 'client'] + ['client' + str(i) for i in range(2, args.players)]
    character = 'native' if args.native_control else 'construct'
    disconnect = 'host-loss' if args.kill_host else 'process-loss' if args.kill_client else 'abrupt' if args.abrupt else 'menu'
    evidence = RUNTIME / 'qa' / ('recovery-' + character + (('-' + args.party) if args.party != 'construct' else '') + (('-' + str(args.players) + 'p') if args.players > 2 else '') + ('-reverse' if args.resume_order == 'reverse' else '') + ('-independent' if args.independent else '') + (('-' + disconnect) if disconnect != 'menu' else '') + '-' + time.strftime('%Y%m%d-%H%M%S') + '-' + str(time.time_ns() % 1000000000))
    evidence.mkdir()
    parent = Path(tempfile.gettempdir()).resolve()
    stage = Path(tempfile.mkdtemp(prefix='PunchConstruct-recovery-', dir=parent)).resolve()
    assert stage.parent == parent and stage.name.startswith('PunchConstruct-recovery-')
    summary = dict(scenario='recovery', character=character, independent=args.independent,
                   players=args.players, party=args.party, resume_order=args.resume_order,
                   disconnect=disconnect, live_join=args.live_join, passed=False, phases={})
    processes = []
    outputs = []
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
        if not args.independent:
            mod = stage / 'mods/PunchConstruct'
            mod.mkdir(parents=True)
            if args.package_dir:
                for name in ('PunchConstruct.dll', 'PunchConstruct.pck', 'PunchConstruct.json'):
                    shutil.copy2(args.package_dir / name, mod / name)
            else:
                shutil.copy2(ROOT / 'PunchConstruct/bin/Release/net9.0/PunchConstruct.dll', mod / 'PunchConstruct.dll')
                shutil.copy2(ROOT / 'PunchConstruct/PunchConstruct.json', mod / 'PunchConstruct.json')
                assets = ROOT / 'PunchConstruct/assets'
                write_pack(mod / 'PunchConstruct.pck', {'PunchConstruct/' + p.relative_to(assets).as_posix(): p.read_bytes()
                                                      for p in assets.rglob('*') if p.is_file()})
            summary['build'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in mod.iterdir()}
        else:
            summary['build'] = {}
        probe_name = 'RecoveryNativeProbe' if args.independent else 'RecoveryProbe'
        probe = stage / 'mods' / probe_name
        probe.mkdir()
        probe_build = 'tools/RecoveryProbe/bin/Native/Release/net9.0' if args.independent else 'tools/RecoveryProbe/bin/Release/net9.0'
        shutil.copy2(ROOT / probe_build / (probe_name + '.dll'), probe / (probe_name + '.dll'))
        manifest = json.loads((ROOT / 'tools/RecoveryProbe/RecoveryProbe.json').read_text(encoding='utf-8'))
        manifest['id'] = probe_name
        if args.independent: manifest['dependencies'] = [d for d in manifest['dependencies'] if d['id'] != 'PunchConstruct']
        (probe / (probe_name + '.json')).write_text(json.dumps(manifest), encoding='utf-8')
        summary['probe_sha256'] = hashlib.sha256((probe / (probe_name + '.dll')).read_bytes()).hexdigest()
        for role in roles:
            settings = stage / ('userdata/' + role + '/PunchConstructTest/default/1/settings.save')
            settings.parent.mkdir(parents=True)
            settings.write_text(json.dumps(dict(schema_version=8, volume_master=0, volume_bgm=0, volume_sfx=0, volume_ambience=0,
                language='zhs', fps_limit=60, mod_settings=dict(mods_enabled=True, mod_list=[]), seen_ea_disclaimer=True, skip_intro_logo=True)), encoding='utf-8')
        for phase in ('seed', 'resume'):
            coord = stage / ('coord-' + phase)
            coord.mkdir()
            if phase == 'resume':
                for p in (stage / 'coord-seed').glob('expected-*.json'): shutil.copy2(p, coord / p.name)
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reservation:
                reservation.bind(('127.0.0.1', 0))
                port = reservation.getsockname()[1]
            processes = []
            outputs = []
            phase_result = dict(passed=False, peers={})
            summary['phases'][phase] = phase_result
            for role in roles:
                env = os.environ.copy()
                env['APPDATA'] = str(stage / 'userdata' / role)
                log = stage / (phase + '-' + role + '.log')
                out = (stage / (phase + '-' + role + '-console.log')).open('wb')
                outputs.append(out)
                command = [str(stage / 'SlayTheSpire2.exe'), '--force-steam=off', '--max-fps', '60', '--quit-after', '18000']
                if args.headless:
                    command.append('--headless')
                else:
                    slot = roles.index(role)
                    command += ['--rendering-method', 'gl_compatibility', '--display-driver', 'windows', '--resolution', '960x540',
                                '--position', window_position(960 + slot * 40, 540 + slot * 30)]
                command += [
                    '--recovery-role=' + role, '--recovery-stage=' + phase, '--recovery-dir=' + str(coord),
                    '--recovery-port=' + str(port), '--recovery-character=' + character,
                    '--recovery-players=' + str(args.players), '--recovery-party=' + args.party,
                    '--recovery-resume-order=' + args.resume_order,
                    '--recovery-disconnect=' + disconnect,
                    '--recovery-livejoin=' + ('yes' if args.live_join else 'no'), '--log-file', str(log)]
                processes.append(subprocess.Popen(command, cwd=stage, env=env, stdout=out, stderr=subprocess.STDOUT,
                                                  creationflags=subprocess.CREATE_NO_WINDOW))
            deadline = time.monotonic() + 150
            killed_role = None
            while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
                loss_role = 'host' if args.kill_host else 'client'
                if (args.kill_client or args.kill_host) and phase == 'seed' and killed_role is None and (coord / ('kill-ready-' + loss_role)).exists():
                    victim = processes[roles.index(loss_role)]
                    assert victim.poll() is None
                    victim.terminate(); victim.wait(timeout=10)
                    killed_role = loss_role
                    # Only fixture barriers for a process deliberately killed
                    # by this runner. Host must still receive a real transport
                    # disconnect; these files cannot satisfy that condition.
                    markers = ['seed-finished-' + loss_role, 'finished-' + loss_role]
                    if loss_role == 'client': markers += ['attempted-client']
                    for marker in markers:
                        (coord / marker).write_text('expected QA process termination', encoding='utf-8')
                if any('[Recovery FAIL]' in p.read_text(encoding='utf-8-sig', errors='replace') or qa_issues(p.read_text(encoding='utf-8-sig', errors='replace'))
                       for p in stage.glob(phase + '-*.log') if not p.name.endswith('-console.log')):
                    break
                time.sleep(.1)
            for p in processes:
                if p.poll() is None: p.terminate(); p.wait(timeout=10)
            for out in outputs: out.close()
            for role, process in zip(roles, processes):
                log = stage / (phase + '-' + role + '.log')
                report = log.read_text(encoding='utf-8-sig', errors='replace') if log.exists() else ''
                if log.exists(): shutil.copy2(log, evidence / log.name)
                diagnostics = archive_diagnostics(stage / 'userdata' / role, evidence / (phase + '-' + role), require_actions=False) if not args.independent else dict(events=[], errors=[], scope='No character logger in independent control')
                issues = qa_issues(report) + diagnostics['errors']
                if not args.independent and phase == 'resume' and '[Recovery PASS]' in report:
                    issues += ['Missing exercised recovery log: ' + event for event in ('action.begin', 'action.end') if event not in diagnostics['events']]
                expected_termination = phase == 'seed' and role == killed_role
                passed = ((expected_termination and process.returncode != 0) or
                          (not expected_termination and process.returncode == 0 and '[Recovery PASS] ' + phase + ' ' + role in report)) and '[Recovery FAIL]' not in report and not issues
                phase_result['peers'][role] = dict(passed=passed, exit_code=process.returncode, errors=issues,
                    expected_termination=expected_termination, assertions=len(re.findall(r'^\[Recovery CHECK', report, re.M)), diagnostics=diagnostics)
                for line in report.splitlines():
                    if '[Recovery' in line: print(phase, role, line)
                if not passed: print(report[-5000:])
            for p in coord.glob('*.json'): shutil.copy2(p, evidence / (phase + '-' + p.name))
            phase_result['passed'] = all(p['passed'] for p in phase_result['peers'].values())
            if not phase_result['passed']:
                can_resume = phase == 'seed' and all((coord / ('expected-' + role + '.json')).exists() for role in roles) and any((stage / 'userdata/host').rglob('current_run_mp.save'))
                if not can_resume: raise SystemExit('Recovery phase failed: ' + phase)
                print('Disconnect diagnostics retained; still checking the already-written save in fresh processes.')
        expected = json.loads((evidence / 'seed-expected-host.json').read_text(encoding='utf-8'))
        for name in ['seed-expected-' + role + '.json' for role in roles] + ['resume-restored-' + role + '.json' for role in roles]:
            assert expected == json.loads((evidence / name).read_text(encoding='utf-8')), name
        summary['snapshots_match'] = True
        summary['resume_passed'] = summary['phases']['resume']['passed']
        summary['passed'] = all(p['passed'] for p in summary['phases'].values())
        print('RECOVERY RESULT:', character, summary['passed'])
        if not summary['passed']: raise SystemExit('Native disconnect diagnostics remain; successful save recovery is recorded separately.')
    finally:
        for p in processes:
            if p.poll() is None: p.terminate(); p.wait(timeout=10)
        for out in outputs: out.close()
        for p in stage.glob('*.log'):
            if not (evidence / p.name).exists(): shutil.copy2(p, evidence / p.name)
        for p in stage.glob('coord-*/*.json'):
            name = p.parent.name + '-' + p.name
            if not (evidence / name).exists(): shutil.copy2(p, evidence / name)
        for role in roles:
            archive_diagnostics(stage / 'userdata' / role, evidence / ('final-' + role))
            for p in (stage / 'userdata' / role).rglob('*.save'):
                target = evidence / 'saves' / role / p.relative_to(stage / 'userdata' / role)
                target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(p, target)
        (evidence / 'result.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
        print('Evidence:', evidence)
        assert stage.resolve().parent == parent and stage.name.startswith('PunchConstruct-recovery-')
        shutil.rmtree(stage)


if __name__ == '__main__': main()
