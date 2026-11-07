"""Run one explicit check in a temporary copy, without touching local play or dist."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
from package import write_pack
from qa_logs import archive_diagnostics
import time

ROOT = Path(__file__).resolve().parent.parent
RUNTIME = ROOT / 'tools/runtime'


def window_position(width, height):
    # Park the real window in the bottom-right corner: it must stay visible and
    # composited (an off-screen window can stop rendering and invalidate pixel
    # checks) but should not cover the desktop while the fixtures run silently.
    try:
        screen = ctypes.windll.user32
        return f'{max(0, screen.GetSystemMetrics(0) - width)},{max(0, screen.GetSystemMetrics(1) - height - 48)}'
    except Exception:
        return '0,0'


MODES = {
    'selection': ('--punchconstruct-selectioncheck', '[Construct SELECTION PASS]'),
    'boxing': ('--punchconstruct-boxingcheck', '[Construct BOXING PASS]'),
    'animation': ('--punchconstruct-animationcheck', '[Construct ANIMATION PASS]'),
    'draft': ('--punchconstruct-draftcheck', '[Construct DRAFT PASS]'),
    'poster': ('--punchconstruct-postercheck', '[Construct POSTER PASS]'),
    'eventscene': ('--punchconstruct-eventcheck', '[Construct EVENT SCENE PASS]'),
    'eventroom': ('--punchconstruct-eventcheck', '[Construct EVENT ROOM PASS]'),
    'eventbrain': ('--punchconstruct-eventcheck', '[Construct EVENT PASS]'),
    'eventphilosopher': ('--punchconstruct-eventcheck', '[Construct EVENT PASS]'),
    'eventscenenoparticles': ('--punchconstruct-eventcheck', '[Construct EVENT SCENE PASS]'),
    'shadernative': ('--punchconstruct-eventcheck', '[Construct SHADER PASS]'),
    'shaderoriginal': ('--punchconstruct-eventcheck', '[Construct SHADER PASS]'),
    'capsulecontrol': ('--punchconstruct-eventcheck', '[Construct CAPSULE CONTROL PASS]'),
    'capsuleconstruct': ('--punchconstruct-eventcheck', '[Construct CAPSULE CONTROL PASS]'),
    'vigor': ('--punchconstruct-vigorcheck', '[Construct VIGOR PASS]'),
    'modes': ('--punchconstruct-modescheck', '[Construct MODES PASS]'),
    'adapt': ('--punchconstruct-adaptcheck', '[Construct ADAPT PASS]'),
    'startup': (None, '[Construct STARTUP PASS]'),
    'startupbase': (None, '[Construct STARTUP BASELINE PASS]'),
    'soup': ('--punchconstruct-soupcheck', '[Construct SOUP PASS]'),
    'facing': ('--punchconstruct-facingcheck', '[Construct FACING PASS]'),
    'regentvisual': ('--punchconstruct-regentvisualcheck', '[Construct REGENT VISUAL PASS]'),
    'observerpersist': ('--punchconstruct-observerseed', '[Construct OBSERVER PERSIST PASS]'),
    'observer': ('--punchconstruct-observercheck', '[Construct OBSERVER PASS]'),
    'architect': ('--punchconstruct-architectcheck', '[Construct ARCHITECT PASS]'),
    'relicvisual': ('--punchconstruct-relicvisualcheck', '[Construct RELIC VISUAL PASS]'),
    'additions': ('--punchconstruct-additionscheck', '[Construct ADDITIONS PASS]'),
    'update': ('--punchconstruct-updatecheck', '[Construct UPDATE PASS]'),
    'pool': ('--punchconstruct-poolcheck', '[PunchConstruct POOL PASS]'),
    'icons': ('--punchconstruct-iconcheck', '[PunchConstruct ICON PASS]'),
    'smoke': ('--punchconstruct-smoke', '[PunchConstruct SMOKE PASS]'),
    'end': ('--punchconstruct-endcheck', '[Construct END FLOW PASS]'),
    'focus': ('--punchconstruct-focuscheck', '[Construct FOCUS PASS]'),
    'doll': ('--punchconstruct-dollcheck', '[Construct DOLL PASS]'),
    'events': ('--punchconstruct-eventcheck', '[Construct EVENT PASS]'),
    'eventscontrol': ('--punchconstruct-eventcheck', '[Construct EVENT PASS]'),
    'relics': ('--punchconstruct-reliccheck', '[Construct RELIC PASS]'),
    'treasure': ('--punchconstruct-reliccheck', '[Construct TREASURE PASS]'),
    'workshop': ('--punchconstruct-reliccheck', '[Construct WORKSHOP RELIC PASS]'),
    'boundary': ('--punchconstruct-boundarycheck', '[Construct BOUNDARY PASS]'),
    'edge': ('--punchconstruct-edgecheck', '[Construct EDGE PASS]'),
    'persist': ('--punchconstruct-persistseed', '[Construct PERSIST PASS]'),
    'compat': ('--punchconstruct-compatcheck', '[Construct COMPAT PASS]'),
    'english': ('--punchconstruct-englishcheck', '[Construct ENGLISH PASS]'),
    'locale': ('--punchconstruct-smoke', '[PunchConstruct SMOKE PASS]'),
    'localezh': ('--punchconstruct-smoke', '[PunchConstruct SMOKE PASS]'),
}


def startup_issues(report):
    # These exact diagnostics also occur in the BaseLib-only rendered baseline.
    known = (
        r'ERROR: Failed to read the root certificate store\.',
        r'ERROR: Invalid Task ID',
        r'ERROR: \d+ RID allocations? of type .+ were leaked at exit\.',
        r'ERROR: \d+ shaders of type \w+ were never freed',
        r'ERROR: Texture with GL ID of \d+: leaked \d+ bytes\.',
        r'ERROR: \d+ resources? still in use at exit \(run with --verbose for details\)\.',
    )
    patterns = list(known)
    # Native screens and VFX never resolve in these targeted fixtures. Mod
    # resources stay strictly checked: every filter excludes res://PunchConstruct/
    # and res://mods/ so a broken mod resource is still reported.
    patterns += (
        r'ERROR: Parse Error: Failed\. \[Resource file res://(?!PunchConstruct/|mods/).+\]',
        r'ERROR: res://(?!PunchConstruct/|mods/).+:\d+ - Parse Error: \.',
        r'ERROR: Failed loading resource: res://(?!PunchConstruct/|mods/).+',
        r"ERROR: Cannot load script for type '.+'\. Path: 'res://src/.+'\.$",
    )
    if 'servers/rendering/dummy/' in report or 'RendererDummy' in report:
        # The dummy renderer cannot allocate real GPU resources, so its RID
        # diagnostics are expected there.
        patterns += (
            r'ERROR: Attempting to initialize the wrong RID',
            r'ERROR: Attempting to use an uninitialized RID',
            r'ERROR: Initializing already initialized RID',
            r'ERROR: Parameter "(?:mem|shader|t|material|texture)" is null\.',
        )
    return [line for line in report.splitlines() if line.startswith('ERROR:') and
            not any(re.fullmatch(pattern, line) for pattern in patterns)]


def qa_issues(report):
    # Debug room fixtures do not autosave; native teardown may try to delete
    # these two nonexistent fixture files. This does not suppress game errors.
    missing_fixture_save = re.compile(r'\[ERROR\] Error deleting path modded/profile1/saves/current_run\.save(?:\.backup)?: Failed')
    return startup_issues(report) + [line for line in report.splitlines()
        if any(token in line for token in ('[ERROR]', 'SCRIPT ERROR:', 'Unhandled exception',
                                          'MissingMethodException', 'TypeLoadException',
                                          'cannot find sfx path: punchconstruct:'))
        and not missing_fixture_save.fullmatch(line)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=MODES)
    parser.add_argument('--rendered', action='store_true', help='Use the real renderer for the selected targeted check')
    parser.add_argument('--package-dir', type=Path, help='Test these exact three packaged files instead of rebuilding the resource pack')
    parser.add_argument('--language', default='zhs',
                        choices=('zhs', 'zht', 'eng', 'deu', 'esp', 'fra', 'ind', 'ita',
                                 'jpn', 'kor', 'pol', 'ptb', 'rus', 'spa', 'tha', 'tur'),
                        help='Initial game language for the disposable fixture profile')
    args = parser.parse_args()
    if args.mode in ('poster', 'selection') and not args.rendered:
        parser.error('poster and selection require --rendered to verify native UI and actual output')
    # A locale mode starts the game in that language: model metadata and table
    # lookup happen before any runtime switch, so this catches missing mirrors.
    language = {'locale': 'deu', 'localezh': 'zht'}.get(args.mode, args.language)
    if args.mode == 'startupbase' and args.package_dir:
        parser.error('startupbase excludes the character mod and cannot verify a release package')
    parent = Path(tempfile.gettempdir()).resolve()
    stage = Path(tempfile.mkdtemp(prefix='PunchConstruct-check-', dir=parent)).resolve()
    assert stage.parent == parent and stage.name.startswith('PunchConstruct-check-')

    def link(source, dest):
        dest.parent.mkdir(parents=True, exist_ok=True)
        os.link(source, dest)

    def link_tree(source, dest):
        for file in source.rglob('*'):
            if file.is_file():
                link(file, dest / file.relative_to(source))

    evidence = RUNTIME / 'qa' / (args.mode + '-' + time.strftime('%Y%m%d-%H%M%S') + '-' + str(time.time_ns() % 1000000000))
    evidence.mkdir(parents=True)
    try:
        for file in RUNTIME.iterdir():
            if file.is_file() and (file.suffix.lower() in ('.dll', '.exe', '.pck') or file.name in ('override.cfg', 'release_info.json')):
                link(file, stage / file.name)
        link_tree(RUNTIME / 'data_sts2_windows_x86_64', stage / 'data_sts2_windows_x86_64')
        for mod in ('BaseLib',):
            link_tree(RUNTIME / 'mods' / mod, stage / 'mods' / mod)
        target = stage / 'mods/PunchConstruct'
        target.mkdir(parents=True)
        qa_helpers = {}
        if args.mode == 'modes':
            helper = stage / 'mods/QaExitProbe'
            helper.mkdir(parents=True)
            for name, source in (
                ('QaExitProbe.dll', ROOT / 'tools/QaExitProbe/bin/Release/net9.0/QaExitProbe.dll'),
                ('QaExitProbe.json', ROOT / 'tools/QaExitProbe/QaExitProbe.json')):
                shutil.copy2(source, helper / name)
                qa_helpers[name] = hashlib.sha256(source.read_bytes()).hexdigest()
        package_hashes = None
        if args.package_dir:
            names = ('PunchConstruct.json', 'PunchConstruct.dll', 'PunchConstruct.pck')
            for name in names:
                shutil.copy2(args.package_dir / name, target / name)
            package_hashes = {name: hashlib.sha256((target / name).read_bytes()).hexdigest() for name in names}
        else:
            shutil.copy2(ROOT / 'PunchConstruct/PunchConstruct.json', target / 'PunchConstruct.json')
            shutil.copy2(ROOT / 'PunchConstruct/bin/Release/net9.0/PunchConstruct.dll', target / 'PunchConstruct.dll')
            assets = ROOT / 'PunchConstruct/assets'
            write_pack(target / 'PunchConstruct.pck', {'PunchConstruct/' + p.relative_to(assets).as_posix(): p.read_bytes()
                                                     for p in assets.rglob('*') if p.is_file()})
        if args.mode == 'startupbase':
            # Remove only these three newly copied files in the temporary fixture.
            for name in ('PunchConstruct.json', 'PunchConstruct.dll', 'PunchConstruct.pck'):
                (target / name).unlink()
            target.rmdir()
        appdata = stage / 'userdata'
        settings = appdata / 'PunchConstructTest/default/1/settings.save'
        settings.parent.mkdir(parents=True)
        settings.write_text(json.dumps(dict(schema_version=8, volume_master=0, volume_bgm=0, volume_sfx=0, volume_ambience=0, language=language, mod_settings=dict(mods_enabled=True, mod_list=[]),
                                            seen_ea_disclaimer=True, skip_intro_logo=True)), encoding='utf-8')
        if args.mode == 'poster':
            # Recording frame-by-frame while unfocused otherwise hits the
            # native background FPS cap. Only this disposable profile changes.
            values = json.loads(settings.read_text(encoding='utf-8'))
            values.update(fps_limit=60, limit_fps_in_background=False)
            settings.write_text(json.dumps(values), encoding='utf-8')
            # The mod raises the construct's maximum ascension, so the native
            # singleplayer ascension tutorial pops up over the real selection
            # screen on a first-run profile. Mark that native tutorial as seen
            # (an earlier fixture already skips the intro logos) so this check
            # measures artwork layering instead of a modal that is not part of
            # the poster feature.
            progress = appdata / 'PunchConstructTest/default/1/modded/profile1/saves/progress.save'
            progress.parent.mkdir(parents=True, exist_ok=True)
            progress.write_text(json.dumps(dict(
                ancient_stats=[], architect_damage=0, card_stats=[], character_stats=[], current_score=0,
                discovered_acts=[], discovered_cards=['CARD.STRIKE_IRONCLAD', 'CARD.DEFEND_IRONCLAD', 'CARD.BASH'],
                discovered_events=[], discovered_potions=[], discovered_relics=['RELIC.BURNING_BLOOD'],
                enable_ftues=True, encounter_stats=[], enemy_stats=[], epochs=[], floors_climbed=0,
                ftue_completed=['ascension_singleplayer_ftue'], max_multiplayer_ascension=0,
                pending_character_unlock='NONE.NONE', preferred_multiplayer_ascension=0, schema_version=24,
                test_subject_kills=0, total_playtime=0, total_unlocks=0, unique_id='PUNCHQA',
                unlocked_achievements=[], wongo_points=0)), encoding='utf-8')
        env = os.environ.copy()
        env['APPDATA'] = str(appdata)
        log = stage / (args.mode + '-current-check.log')
        flag, marker = MODES[args.mode]
        command = [str(stage / 'SlayTheSpire2.exe'), '--headless', '--force-steam=off']
        if flag:
            command.append(flag)
        if args.mode == 'treasure':
            command.append('--punchconstruct-treasure-only')
        if args.mode == 'workshop':
            command.append('--punchconstruct-workshop-only')
        if args.mode in ('capsulecontrol', 'capsuleconstruct'):
            command.append('--punchconstruct-capsule-native-control')
        if args.mode == 'capsuleconstruct':
            command.append('--punchconstruct-capsule-construct-control')
        if args.mode in ('eventscontrol', 'eventbrain', 'eventphilosopher'):
            command.append('--punchconstruct-event-native-control')
        if args.mode == 'eventbrain':
            command.append('--punchconstruct-event-brain-only')
        if args.mode == 'eventphilosopher':
            command.append('--punchconstruct-event-philosopher-only')
        if args.mode in ('eventscene', 'eventscenenoparticles'):
            command.append('--punchconstruct-event-scene-control')
        if args.mode == 'eventscenenoparticles':
            command.append('--punchconstruct-event-scene-no-particles')
        if args.mode in ('shadernative', 'shaderoriginal'):
            command.append('--punchconstruct-shader-control')
        if args.mode == 'shaderoriginal':
            command.append('--punchconstruct-shader-native-role')
        if args.mode == 'eventroom':
            command.append('--punchconstruct-event-room-control')
        command.extend(['--quit-after', '1800' if args.mode in ('startup', 'startupbase') else '24000' if args.mode == 'modes' else '5400', '--log-file', str(log), '--disable-crash-handler'])
        if args.mode in ('relicvisual', 'regentvisual') or args.rendered:
            command.remove('--headless')
            command.extend(['--rendering-method', 'gl_compatibility', '--display-driver', 'windows', '--resolution', '1280x720',
                            '--position', window_position(1280, 720)])
        if args.rendered and args.mode not in ('startup', 'startupbase'):
            command.append('--punchconstruct-visual')
        if args.mode == 'pool':
            command.append('--punchconstruct-audit-list=' + str(ROOT / '资料/卡牌清单.txt'))
        result = subprocess.run(command, cwd=stage, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                timeout=300 if args.mode in ('modes', 'poster') else 180, creationflags=subprocess.CREATE_NO_WINDOW)
        report = log.read_text(encoding='utf-8-sig', errors='replace') if log.exists() else result.stdout.decode('utf-8', errors='replace')
        if args.mode in ('startup', 'startupbase'):
            if ('Time to main menu:' in report and "Preloading 'Common' Complete:" in report and
                ('[PunchConstruct] v' in report) == (args.mode == 'startup') and
                'Calling initializer method of type BaseLib.BaseLibMain' in report and
                '[Construct QA START]' not in report and 'Calling initializer method of type Watcher' not in report and
                not any(error in report for error in ('[ERROR]', 'SCRIPT ERROR:', 'Unhandled exception', 'MissingMethodException', 'TypeLoadException')) and
                not startup_issues(report) and
                result.returncode == 0):
                if args.mode == 'startup':
                    digest = hashlib.sha256((target / 'PunchConstruct.dll').read_bytes()).hexdigest()
                    report += f'\n[Construct QA DLL] {digest}\n'
                mods = 'BaseLib and PunchConstruct' if args.mode == 'startup' else 'BaseLib without PunchConstruct'
                report += f'\n{marker} Native main menu reached with {mods}; no QA arguments or game run.\n'
                log.write_text(report, encoding='utf-8')
        for line in report.splitlines():
            if any(token in line for token in ('[Construct QA ', '[PunchConstruct CHECK]', '[PunchConstruct RARITY', marker)):
                print(line)
        if marker not in report or '[Construct QA FAIL]' in report or result.returncode != 0 or qa_issues(report):
            for issue in qa_issues(report):
                print('Unclassified error:', issue)
            start = report.find('[Construct QA FAIL]')
            print(report[start:start+2400] if start >= 0 else report[-2400:])
            raise SystemExit('Check failed; inspect tools/runtime/' + log.name)
        if args.mode in ('persist', 'observerpersist'):
            shutil.copy2(log, RUNTIME / (args.mode + '-seed-check.log'))
            shutil.copy2(log, evidence / (args.mode + '-seed-check.log'))
            if args.mode == 'observerpersist':
                # Rewrite only the disposable fixture's card IDs to the former
                # Watcher IDs, then exercise native Continue in a new process.
                aliases = dict(OBSERVER_JUST_LUCKY='JUST_LUCKY', OBSERVER_SASH_WHIP='SASH_WHIP',
                               OBSERVER_PROSTRATE='PROSTRATE', OBSERVER_TALK_TO_THE_HAND='TALK_TO_THE_HAND',
                               OBSERVER_WALLOP='WALLOP', OBSERVER_COLD_OBSERVATION='COLD_OBSERVATION',
                               OBSERVER_SWIVEL='SWIVEL', OBSERVER_FASTING='FASTING2')
                changed_ids = set()
                for save in appdata.rglob('*.save'):
                    value = save.read_text(encoding='utf-8-sig')
                    for local_id, old_id in aliases.items():
                        if 'PUNCHCONSTRUCT-' + local_id in value:
                            changed_ids.add(local_id)
                            value = value.replace('PUNCHCONSTRUCT-' + local_id, 'WATCHER_' + old_id)
                    save.write_text(value, encoding='utf-8')
                assert changed_ids == set(aliases), 'Missing legacy card IDs in persistence fixture'
            command[command.index(flag)] = '--punchconstruct-observerresume' if args.mode == 'observerpersist' else '--punchconstruct-persistresume'
            result = subprocess.run(command, cwd=stage, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                    timeout=60, creationflags=subprocess.CREATE_NO_WINDOW)
            report = log.read_text(encoding='utf-8-sig', errors='replace') if log.exists() else result.stdout.decode('utf-8', errors='replace')
            for line in report.splitlines():
                if '[Construct QA ' in line or marker in line:
                    print(line)
            if marker not in report or '[Construct QA FAIL]' in report or result.returncode != 0 or qa_issues(report):
                for issue in qa_issues(report):
                    print('Unclassified error:', issue)
                start = report.find('[Construct QA FAIL]')
                print(report[start:start+2400] if start >= 0 else report[-2400:])
                raise SystemExit('Fresh-process continue failed; inspect tools/runtime/' + log.name)
    finally:
        if args.mode == 'animation':
            for screenshot in stage.glob('qa-animation-*.png'):
                shutil.copy2(screenshot, evidence / screenshot.name)
        if args.mode == 'poster':
            for screenshot in list(stage.glob('qa-poster-*.png')) + list(stage.glob('qa-poster-*.webp')):
                shutil.copy2(screenshot, evidence / screenshot.name)
        if args.mode in ('shadernative', 'shaderoriginal'):
            for screenshot in stage.glob('qa-decimillipede-*.png'):
                shutil.copy2(screenshot, evidence / screenshot.name)
        if args.mode == 'facing':
            for screenshot in stage.glob('qa-crab-*.png'):
                shutil.copy2(screenshot, RUNTIME / screenshot.name)
        if args.mode == 'end':
            for screenshot in stage.glob('qa-deathvisual-*.png'):
                shutil.copy2(screenshot, RUNTIME / screenshot.name)
        if args.mode == 'regentvisual' and (stage / 'qa-regent-costs.png').exists():
            shutil.copy2(stage / 'qa-regent-costs.png', RUNTIME / 'qa-regent-costs.png')
        if args.mode == 'additions' and (stage / 'qa-never-burn-preview.png').exists():
            shutil.copy2(stage / 'qa-never-burn-preview.png', RUNTIME / 'qa-never-burn-preview.png')
        if args.mode == 'observer' and (stage / 'qa-independent-observer.png').exists():
            shutil.copy2(stage / 'qa-independent-observer.png', RUNTIME / 'qa-independent-observer.png')
        if args.mode == 'observer' and (stage / 'qa-observer-divinity.png').exists():
            shutil.copy2(stage / 'qa-observer-divinity.png', evidence / 'qa-observer-divinity.png')
        if args.mode == 'architect' and (stage / 'qa-architect-first-line.png').exists():
            shutil.copy2(stage / 'qa-architect-first-line.png', RUNTIME / 'qa-architect-first-line.png')
        if args.mode == 'relicvisual':
            for filename in ('basin-inventory-fixed.png', 'basin-inventory-native.png'):
                if (stage / filename).exists():
                    shutil.copy2(stage / filename, RUNTIME / filename)
        if 'log' in locals() and log.exists():
            saved_log = RUNTIME / log.name
            shutil.copy2(log, saved_log)
            if qa_helpers:
                saved_log.write_text('[Construct QA HELPER] ' + json.dumps(qa_helpers, sort_keys=True) + '\n'
                                     + saved_log.read_text(encoding='utf-8-sig'), encoding='utf-8')
            if 'package_hashes' in locals() and package_hashes:
                saved_log.write_text('[Construct QA PACKAGE] ' + json.dumps(package_hashes, sort_keys=True) + '\n'
                                     + saved_log.read_text(encoding='utf-8-sig'), encoding='utf-8')
        if args.mode != 'startupbase':
            diagnostic = archive_diagnostics(stage / 'userdata', evidence)
            if 'log' in locals() and log.exists():
                shutil.copy2(saved_log, evidence / log.name)
            (evidence / 'result.json').write_text(json.dumps(dict(schema=1, mode=args.mode,
                dll_sha256=hashlib.sha256((target / 'PunchConstruct.dll').read_bytes()).hexdigest(),
                build={name: hashlib.sha256((target / name).read_bytes()).hexdigest()
                       for name in ('PunchConstruct.dll', 'PunchConstruct.pck', 'PunchConstruct.json')},
                diagnostics=diagnostic, qa_helpers=qa_helpers, passed=('report' in locals() and marker in report and result.returncode == 0 and not qa_issues(report) and not diagnostic['errors'])), indent=2), encoding='utf-8')
            print('Evidence:', evidence)
        assert stage.resolve().parent == parent and stage.name.startswith('PunchConstruct-check-')
        # Windows may still hold handles in the hardlinked game data tree right
        # after the process exits; a cleanup failure must not mask the QA result.
        shutil.rmtree(stage, ignore_errors=True)
        if stage.exists():
            time.sleep(1)
            shutil.rmtree(stage, ignore_errors=True)
        if args.mode != 'startupbase' and diagnostic['errors']:
            raise SystemExit('Structured log checks failed: ' + '; '.join(diagnostic['errors']))


if __name__ == '__main__':
    main()
