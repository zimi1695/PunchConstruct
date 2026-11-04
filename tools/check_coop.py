"""Two native ENet game processes, isolated saves, direct targeted rooms only."""
import argparse
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile
import time
from check_current import qa_issues, window_position
from qa_logs import archive_diagnostics
from package import write_pack
from network_proxy import NetworkProxy

ROOT = Path(__file__).resolve().parent.parent
RUNTIME = ROOT / 'tools/runtime'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--guest', choices=('construct', 'ironclad', 'necrobinder'), default='construct')
    parser.add_argument('--players', choices=(2, 3, 4), type=int, default=2)
    parser.add_argument('--package-dir', type=Path, help='Test an existing exact three-file package without exporting anything')
    parser.add_argument('--rendered', action='store_true')
    parser.add_argument('--headless', action='store_true', help='Use the dummy renderer for diagnostics; real rendering is the default')
    parser.add_argument('--treasure-only', action='store_true', help='Only the real shared chest UI, cursor gestures and native mouse selection')
    parser.add_argument('--latency-ms', type=float, default=0, help='One-way loopback UDP delay per packet')
    parser.add_argument('--jitter-ms', type=float, default=0)
    parser.add_argument('--loss-percent', type=float, default=0)
    parser.add_argument('--blackout-seconds', type=float, default=0, help='Drop both directions once at the first combat hand-choice action')
    args = parser.parse_args()
    if not (0 <= args.jitter_ms <= args.latency_ms <= 500 and 0 <= args.loss_percent <= 30 and 0 <= args.blackout_seconds <= 10):
        parser.error('Require 0 <= jitter <= latency <= 500ms, loss 0..30%, blackout 0..10s')
    impaired = any((args.latency_ms, args.loss_percent, args.blackout_seconds))
    roles = ['host', 'client'] + ['client' + str(i) for i in range(2, args.players)]
    label = args.guest + (('-' + str(args.players) + 'p') if args.players > 2 else '')
    if args.treasure_only:
        label += '-treasure'
    if impaired:
        label += '-network'
    stamp = time.strftime('%Y%m%d-%H%M%S') + '-' + str(time.time_ns() % 1000000000)
    evidence = RUNTIME / 'qa' / ('coop-' + label + '-' + stamp)
    evidence.mkdir(parents=True)
    summary = dict(schema=1, scenario='coop', guest=args.guest, players=args.players, passed=False, peers={}, snapshots={})
    if args.treasure_only:
        summary['scenario'] = 'coop-treasure'
    parent = Path(tempfile.gettempdir()).resolve()
    stage = Path(tempfile.mkdtemp(prefix='PunchConstruct-coop-', dir=parent)).resolve()
    assert stage.parent == parent and stage.name.startswith('PunchConstruct-coop-')
    processes = []
    outputs = []
    proxy = None
    try:
        def link(source, dest):
            dest.parent.mkdir(parents=True, exist_ok=True)
            os.link(source, dest)

        for source in RUNTIME.iterdir():
            if source.is_file() and (source.suffix.lower() in ('.dll', '.exe', '.pck') or source.name in ('override.cfg', 'release_info.json')):
                link(source, stage / source.name)
        for tree in ('data_sts2_windows_x86_64', 'mods/BaseLib'):
            for source in (RUNTIME / tree).rglob('*'):
                if source.is_file():
                    link(source, stage / tree / source.relative_to(RUNTIME / tree))
        mod = stage / 'mods/PunchConstruct'
        mod.mkdir(parents=True)
        if args.package_dir:
            for name in ('PunchConstruct.dll', 'PunchConstruct.json', 'PunchConstruct.pck'):
                shutil.copy2(args.package_dir / name, mod / name)
        else:
            shutil.copy2(ROOT / 'PunchConstruct/bin/Release/net9.0/PunchConstruct.dll', mod / 'PunchConstruct.dll')
            shutil.copy2(ROOT / 'PunchConstruct/PunchConstruct.json', mod / 'PunchConstruct.json')
            assets = ROOT / 'PunchConstruct/assets'
            write_pack(mod / 'PunchConstruct.pck', {'PunchConstruct/' + p.relative_to(assets).as_posix(): p.read_bytes()
                                                  for p in assets.rglob('*') if p.is_file()})
        summary['build'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in mod.iterdir()}
        tested_version = tuple(map(int, json.loads((mod / 'PunchConstruct.json').read_text(encoding='utf-8'))['version'].split('.')[:3]))
        coord = stage / 'coord'
        coord.mkdir()
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reservation:
            reservation.bind(('127.0.0.1', 0))
            port = reservation.getsockname()[1]
        if impaired:
            proxy = NetworkProxy(port, args.players - 1, args.latency_ms, args.jitter_ms,
                                 args.loss_percent, args.blackout_seconds, coord / 'fervor-0-prepared-host',
                                 shutdown_ready=lambda: all((coord / ('checked-' + r)).exists() for r in roles))
            proxy.start()
        for role in roles:
            appdata = stage / 'userdata' / role
            settings = appdata / 'PunchConstructTest/default/1/settings.save'
            settings.parent.mkdir(parents=True)
            settings.write_text(json.dumps(dict(schema_version=8, volume_master=0, volume_bgm=0, volume_sfx=0, volume_ambience=0, language='zhs', fps_limit=60,
                mod_settings=dict(mods_enabled=True, mod_list=[]), seen_ea_disclaimer=True, skip_intro_logo=True,
                # Four real windows cannot all stay focused, and background FPS
                # throttling would starve the peers that are not in front.
                limit_fps_in_background=False if args.treasure_only or not args.headless else True)), encoding='utf-8')
            env = os.environ.copy()
            env['APPDATA'] = str(appdata)
            connect_port = port if role == 'host' or proxy is None else proxy.client_ports[roles.index(role) - 1]
            command = [str(stage / 'SlayTheSpire2.exe'), '--force-steam=off', '--max-fps', '60', '--quit-after', '36000' if impaired else '18000',
                '--punchconstruct-coopcheck', '--punchconstruct-coop-role=' + role,
                '--punchconstruct-coop-dir=' + str(coord), '--punchconstruct-coop-port=' + str(connect_port),
                '--punchconstruct-coop-guest=' + args.guest, '--punchconstruct-coop-players=' + str(args.players), '--log-file', str(stage / (role + '.log'))]
            if not args.headless:
                slot = roles.index(role)
                command += ['--rendering-method', 'gl_compatibility', '--display-driver', 'windows', '--resolution', '960x540',
                            '--position', window_position(960 + slot * 40, 540 + slot * 30)]
            else:
                command += ['--headless']
            if args.treasure_only:
                command += ['--punchconstruct-coop-treasure', '--punchconstruct-visual']
            output = (stage / (role + '-console.log')).open('wb')
            outputs.append(output)
            processes.append(subprocess.Popen(command, cwd=stage, env=env, stdout=output, stderr=subprocess.STDOUT,
                creationflags=subprocess.CREATE_NO_WINDOW))
        deadline = time.monotonic() + (300 if impaired else 210 if tested_version >= (0, 4, 20) and args.players > 2 else 150)
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            # Fail fast on either peer; never leave its counterpart running a fixture.
            if any((stage / (role + '.log')).exists() and '[Construct QA FAIL]' in (stage / (role + '.log')).read_text(encoding='utf-8-sig', errors='replace')
                   for role in roles):
                break
            time.sleep(.1)
        passed = True
        for process in processes:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=10)
                passed = False
        for output in outputs:
            output.close()
        for role, process in zip(roles, processes):
            log = stage / (role + '.log')
            report = log.read_text(encoding='utf-8-sig', errors='replace') if log.exists() else (stage / (role + '-console.log')).read_text(encoding='utf-8-sig', errors='replace')
            archive = RUNTIME / ('coop-' + label + '-' + role + '.log')
            archive.write_text(report, encoding='utf-8')
            for line in report.splitlines():
                if '[Construct ' in line:
                    print(role + ': ' + line)
            (evidence / (role + '.log')).write_text(report, encoding='utf-8')
            diagnostic = archive_diagnostics(stage / 'userdata' / role, evidence / role, require_actions=not args.treasure_only)
            issues = qa_issues(report) + diagnostic['errors']
            summary['peers'][role] = dict(exit_code=process.returncode, diagnostics=diagnostic, assertions=len(re.findall(r'\[Construct QA \d+\]', report)), errors=issues)
            if process.returncode != 0 or '[Construct COOP PASS]' not in report or '[Construct QA FAIL]' in report or issues:
                passed = False
                print(role + ' errors:', issues)
                print(report[-6000:])
        phases = ('treasure',) if args.treasure_only else ('combat', 'marks', 'divinity', 'turn', 'temporary-strength-expired', 'choices', 'rewards', 'concurrent', 'survivor')
        if not args.treasure_only and tested_version >= (0, 4, 20):
            phases = phases[:7] + ('boxing-generated', 'boxing-resolved') + phases[7:]
        if not args.treasure_only and tested_version >= (0, 4, 22):
            phases += ('pandora',)
        for phase in phases:
            snapshots = [coord / (phase + '-' + role + '.json') for role in roles]
            if all(p.exists() for p in snapshots):
                matching = all(json.loads(snapshots[0].read_text()) == json.loads(p.read_text()) for p in snapshots[1:])
                summary['snapshots'][phase] = matching
                passed &= matching
                for p in snapshots:
                    shutil.copy2(p, RUNTIME / ('coop-' + label + '-' + p.name))
                    shutil.copy2(p, evidence / p.name)
                print('All actual peer snapshots match:', phase, matching)
            else:
                passed = False
        summary['passed'] = passed
        if proxy:
            proxy.stop()
            summary['network'] = proxy.report()
            summary['network']['validation_errors'] = proxy.validation_errors()
            summary['passed'] &= not summary['network']['validation_errors']
            passed = summary['passed']
            print('Network impairment:', json.dumps(summary['network']))
        if not passed:
            raise SystemExit('Coop check failed; inspect runtime/coop-' + args.guest + '-*.log')
        print('COOP PASS:', args.guest, 'DLL', hashlib.sha256((mod / 'PunchConstruct.dll').read_bytes()).hexdigest())
    finally:
        if args.treasure_only:
            for screenshot in stage.glob('qa-treasure-*.png'):
                shutil.copy2(screenshot, evidence / screenshot.name)
        for process in processes:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=10)
        for output in outputs:
            output.close()
        if proxy and 'network' not in summary:
            proxy.stop()
            summary['network'] = proxy.report()
            summary['network']['validation_errors'] = proxy.validation_errors()
        for role in roles:
            if role not in summary['peers']:
                archive_diagnostics(stage / 'userdata' / role, evidence / role)
                for suffix in ('.log', '-console.log'):
                    source = stage / (role + suffix)
                    if source.exists():
                        shutil.copy2(source, evidence / source.name)
        (evidence / 'result.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
        print('Evidence:', evidence)
        target = stage.resolve()
        assert target.parent == parent and target.name.startswith('PunchConstruct-coop-')
        shutil.rmtree(target)


if __name__ == '__main__':
    main()
