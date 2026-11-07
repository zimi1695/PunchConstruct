"""Prepare or verify the official Workshop layout. Never uploads or exports a ZIP."""
import argparse
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path

from package import write_pack
from pck import index
from check_current import qa_issues
from release_evidence import collect as multiplayer_evidence

ROOT = Path(__file__).resolve().parent.parent
WORKSHOP = ROOT / 'dist/SteamWorkshop'
CONTENT = WORKSHOP / 'content'
NAMES = ('PunchConstruct.dll', 'PunchConstruct.pck', 'PunchConstruct.json')
CHECKS = {
    'startup': '[Construct STARTUP PASS]',
    'smoke': '[PunchConstruct SMOKE PASS]',
    'pool': '[PunchConstruct POOL PASS]',
    'focus': '[Construct FOCUS PASS]',
    'vigor': '[Construct VIGOR PASS]',
    'update': '[Construct UPDATE PASS]',
    'edge': '[Construct EDGE PASS]',
    'boundary': '[Construct BOUNDARY PASS]',
    'doll': '[Construct DOLL PASS]',
    'icons': '[PunchConstruct ICON PASS]',
    'observer': '[Construct OBSERVER PASS]',
    'observerpersist': '[Construct OBSERVER PERSIST PASS]',
    'architect': '[Construct ARCHITECT PASS]',
    'end': '[Construct END FLOW PASS]',
    'relicvisual': '[Construct RELIC VISUAL PASS]',
    'relics': '[Construct RELIC PASS]',
    'workshop': '[Construct WORKSHOP RELIC PASS]',
    'additions': '[Construct ADDITIONS PASS]',
    'adapt': '[Construct ADAPT PASS]',
    'modes': '[Construct MODES PASS]',
    'events': '[Construct EVENT PASS]',
    'shadernative': '[Construct SHADER PASS]',
    'facing': '[Construct FACING PASS]',
    'regentvisual': '[Construct REGENT VISUAL PASS]',
    'soup': '[Construct SOUP PASS]',
    'persist': '[Construct PERSIST PASS]',
}


def checks_for_version(version):
    checks = dict(CHECKS)
    # Historical static-poster candidates keep their original 26-suite contract.
    if tuple(map(int, version.split('.')[:3])) >= (0, 4, 16):
        checks['poster'] = '[Construct POSTER PASS]'
    if tuple(map(int, version.split('.')[:3])) >= (0, 4, 17):
        checks['draft'] = '[Construct DRAFT PASS]'
    if tuple(map(int, version.split('.')[:3])) >= (0, 4, 19):
        checks['animation'] = '[Construct ANIMATION PASS]'
    if tuple(map(int, version.split('.')[:3])) >= (0, 4, 20):
        checks['boxing'] = '[Construct BOXING PASS]'
    if tuple(map(int, version.split('.')[:3])) >= (0, 4, 21):
        checks['selection'] = '[Construct SELECTION PASS]'
    if tuple(map(int, version.split('.')[:3])) >= (0, 4, 26):
        checks['compat'] = '[Construct COMPAT PASS]'
        checks['english'] = '[Construct ENGLISH PASS]'
        checks['locale'] = '[PunchConstruct SMOKE PASS]'
        checks['localezh'] = '[PunchConstruct SMOKE PASS]'
    return checks


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def profile_hashes():
    profile = ROOT / 'tools/runtime/play-userdata'
    return {p.relative_to(ROOT).as_posix(): sha(p) for p in profile.rglob('*') if p.is_file()}


def status():
    """Read-only audit: never prepares files, changes a report or uploads."""
    source = ROOT / 'PunchConstruct'
    files = {name: sha(CONTENT / name) if (CONTENT / name).exists() else None for name in NAMES}
    current = {NAMES[0]: source / 'bin/Release/net9.0' / NAMES[0], NAMES[2]: source / NAMES[2]}
    entries = index(CONTENT / NAMES[1]) if files[NAMES[1]] else {}
    assets = source / 'assets'
    resources = {'PunchConstruct/' + p.relative_to(assets).as_posix(): p for p in assets.rglob('*') if p.is_file()}
    resources_match = set(entries) == set(resources)
    if resources_match:
        with (CONTENT / NAMES[1]).open('rb') as pack:
            for name, (offset, length, digest, flags) in entries.items():
                pack.seek(offset)
                data = pack.read(length)
                if flags != 0 or hashlib.md5(data).hexdigest() != digest or data != resources[name].read_bytes():
                    resources_match = False
                    break
    report_path = WORKSHOP / 'verification.json'
    report = json.loads(report_path.read_text(encoding='utf-8')) if report_path.exists() else {}
    # A historical report must not hide newer exact-file evidence by redirecting
    # checks to its archived logs. Report freshness is shown separately below.
    evidence = {e['mode']: e for e in report.get('evidence', [])} if report.get('package_files') == files else {}
    checks = {}
    changed_logs = []
    candidate_manifest = json.loads((CONTENT / 'PunchConstruct.json').read_text(encoding='utf-8')) if (CONTENT / 'PunchConstruct.json').exists() else {'version': '0.0.0'}
    required_checks = checks_for_version(candidate_manifest['version'])
    for mode, marker in required_checks.items():
        log = ROOT / f'tools/runtime/{mode}-current-check.log'
        recorded = evidence.get(mode)
        if recorded:
            recorded_log = ROOT / recorded['log']
            if recorded_log.exists() and sha(recorded_log) == recorded.get('log_sha256'):
                log = recorded_log
            else:
                changed_logs.append(mode)
            if recorded.get('seed_log'):
                seed_log = ROOT / recorded['seed_log']
                if not seed_log.exists() or sha(seed_log) != recorded.get('seed_log_sha256'):
                    changed_logs.append(mode + ':seed')
        value = log.read_text(encoding='utf-8-sig', errors='replace') if log.exists() else ''
        package = re.search(r'^\[Construct QA PACKAGE\] (.+)$', value, re.M)
        checks[mode] = bool(package and json.loads(package[1]) == files and marker in value
                            and ' QA FAIL]' not in value and ' SMOKE FAIL]' not in value and not qa_issues(value))
    config_path = WORKSHOP / 'workshop.json'
    config = json.loads(config_path.read_text(encoding='utf-8')) if config_path.exists() else {}
    preview = WORKSHOP / '发布说明.txt'
    result = dict(
        read_only=True,
        package_matches_latest_build=all(files[name] and path.exists() and files[name] == sha(path)
                                         for name, path in current.items()),
        resources_match_latest_source=resources_match,
        verification_matches_candidate=bool(report) and report.get('package_files') == files,
        old_report_missing_required_checks=sorted(set(required_checks) - set(evidence)),
        old_report_logs_changed=changed_logs,
        exact_candidate_checks=checks,
        description_synced=bool(config) and preview.exists() and preview.read_text(encoding='utf-8') ==
            config.get('title', '') + '\n\n' + config.get('description', '') + '\n',
        locally_recorded_upload_status=report.get('upload_status', 'UNKNOWN'),
    )
    candidate_multiplayer, missing_multiplayer = multiplayer_evidence(files)
    result['missing_candidate_multiplayer'] = missing_multiplayer
    result['candidate_multiplayer'] = [{k: v for k, v in row.items() if k != 'files'} for row in candidate_multiplayer]
    result['archived_multiplayer_logs_changed'] = [row['mode'] + ':' + name
        for row in report.get('multiplayer_evidence', []) for name, digest in row['files'].items()
        if not (ROOT / row['directory'] / name).exists() or sha(ROOT / row['directory'] / name) != digest]
    print(json.dumps(result, ensure_ascii=False, indent=2))


def prepare():
    from PIL import Image
    CONTENT.mkdir(parents=True, exist_ok=True)
    extras = {p.name for p in CONTENT.iterdir()} - set(NAMES)
    if extras:
        raise SystemExit(f'Unexpected upload content: {sorted(extras)}')
    source = ROOT / 'PunchConstruct'
    (CONTENT / NAMES[0]).write_bytes((source / 'bin/Release/net9.0' / NAMES[0]).read_bytes())
    (CONTENT / NAMES[2]).write_bytes((source / NAMES[2]).read_bytes())
    assets = source / 'assets'
    write_pack(CONTENT / NAMES[1], {'PunchConstruct/' + p.relative_to(assets).as_posix(): p.read_bytes()
                                 for p in assets.rglob('*') if p.is_file()})
    # Only technical resizing of the adopted source; no repainting or cropping.
    with Image.open(ROOT / '资料/宣传图/模组封面.png') as image:
        image = image.convert('RGB')
        image.thumbnail((1024, 1024), Image.Resampling.LANCZOS)
        image.save(WORKSHOP / 'image.png', optimize=True)
    assert (WORKSHOP / 'image.png').stat().st_size < 1_000_000
    config_path = WORKSHOP / 'workshop.json'
    if not config_path.exists():
        description = (
            '[h1]拳击构装体[/h1]\n'
            '尖塔的本质，是一个巨大的铁斩波。\n\n'
            '一个围绕“边打边防”设计的角色：完全格挡敌人的伤害，积累活力，寻找反击与成长的机会。\n\n'
            '[h2]内容[/h2]\n'
            '74种卡牌，包含原版搬运牌与原创牌；锻造、召唤、灾厄等方向，以及角色专属遗物和事件适配。'
            '卡牌保留各自角色归属，同体系卡牌会随牌组构筑提高出现概率。\n\n'
            '[h2]安装与版本[/h2]\n'
            '需要 BaseLib（已验证3.4.7）；无需安装观者／Watcher。订阅后在游戏模组管理中启用本模组与BaseLib。\n'
            '当前验证环境：Windows、游戏0.111.0、中文。尚未验证其他游戏版本、系统及网络联机。\n\n'
            '[h2]制作与素材来源[/h2]\n'
            '设计与制作：铁斩波。部分原创美术采用AI辅助生成。\n'
            '《杀戮尖塔》系列的原版角色、卡牌与素材归Mega Crit所有。'
            '选入的观者卡面及状态图标取用自Boninall的“观者 Watcher”模组（0.10.3）：'
            '[url=https://steamcommunity.com/sharedfiles/filedetails/?id=3747526116]原模组页面[/url]。'
            '\n'
            '本模组为免费、非官方社区作品，与Mega Crit无隶属关系。'
        )
        config = dict(title='拳击构装体 | Punch Construct', description=description,
                      visibility='private', changeNote='首次创意工坊测试发布；移除Watcher依赖。',
                      tags=['Characters'], dependencies=[3737335127], contentDescriptors=[])
        config_path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (WORKSHOP / 'play-profile-before.json').write_text(json.dumps(profile_hashes(), indent=2), encoding='utf-8')
    print('Prepared:', WORKSHOP)


def verify(require_checks):
    from PIL import Image
    manifest = json.loads((CONTENT / 'PunchConstruct.json').read_text(encoding='utf-8-sig'))
    assert {p.name for p in CONTENT.iterdir()} == set(NAMES), 'Only three mod files belong in content'
    assert manifest['dependencies'] == [{'id': 'BaseLib', 'min_version': '3.4.7'}]
    assert (CONTENT / 'PunchConstruct.json').read_bytes() == (ROOT / 'PunchConstruct/PunchConstruct.json').read_bytes()
    assert sha(CONTENT / 'PunchConstruct.dll') == sha(ROOT / 'PunchConstruct/bin/Release/net9.0/PunchConstruct.dll')
    entries = index(CONTENT / 'PunchConstruct.pck')
    assets = ROOT / 'PunchConstruct/assets'
    source_assets = {'PunchConstruct/' + p.relative_to(assets).as_posix(): p for p in assets.rglob('*') if p.is_file()}
    assert set(entries) == set(source_assets), 'Pack resources differ from source'
    with (CONTENT / 'PunchConstruct.pck').open('rb') as pack:
        for name, (offset, length, digest, flags) in entries.items():
            pack.seek(offset)
            data = pack.read(length)
            assert flags == 0 and hashlib.md5(data).hexdigest() == digest and data == source_assets[name].read_bytes(), name
    config = json.loads((WORKSHOP / 'workshop.json').read_text(encoding='utf-8'))
    assert config['dependencies'] == [3737335127]
    assert config['visibility'] in ('private', 'public', 'unlisted', 'friends_only')
    assert len(config['description'].encode('utf-8')) < 8000
    preview = WORKSHOP / '发布说明.txt'
    expected_preview = config['title'] + '\n\n' + config['description'] + '\n'
    if not preview.exists():
        preview.write_text(expected_preview, encoding='utf-8')
    assert preview.read_text(encoding='utf-8') == expected_preview, 'Review text and workshop.json differ'
    image_path = WORKSHOP / 'image.png'
    with Image.open(image_path) as image:
        assert image.format == 'PNG' and image_path.stat().st_size < 1_000_000
    digests = {name: sha(CONTENT / name) for name in NAMES}
    evidence = []
    missing = []
    for mode, marker in checks_for_version(manifest['version']).items():
        path = ROOT / f'tools/runtime/{mode}-current-check.log'
        value = path.read_text(encoding='utf-8-sig', errors='replace') if path.exists() else ''
        package = re.search(r'^\[Construct QA PACKAGE\] (.+)$', value, re.M)
        passed = marker in value and ' QA FAIL]' not in value and ' SMOKE FAIL]' not in value and not qa_issues(value)
        matched = package is not None and json.loads(package[1]) == digests
        if not (passed and matched):
            missing.append(mode)
        else:
            # Preserve the exact evidence; later local QA overwrites runtime logs.
            snapshot = WORKSHOP / '验收日志' / (mode + '.log')
            snapshot.parent.mkdir(exist_ok=True)
            snapshot.write_bytes(path.read_bytes())
            path = snapshot
        evidence.append(dict(mode=mode, status='PASS' if passed and matched else 'NOT_VERIFIED_ON_THIS_PACKAGE',
                             log=path.relative_to(ROOT).as_posix(), log_sha256=sha(path) if path.exists() else None))
        helper = re.search(r'^\[Construct QA HELPER\] (.+)$', value, re.M)
        if helper: evidence[-1]['qa_helpers'] = json.loads(helper[1])
        if passed and matched and mode in ('persist', 'observerpersist'):
            seed = ROOT / f'tools/runtime/{mode}-seed-check.log'
            seed_value = seed.read_text(encoding='utf-8-sig')
            assert marker in seed_value and not qa_issues(seed_value) and '[Construct QA FAIL]' not in seed_value
            assert '[Construct QA DLL] ' + digests['PunchConstruct.dll'] in seed_value
            seed_snapshot = WORKSHOP / '验收日志' / (mode + '-seed.log')
            seed_snapshot.write_bytes(seed.read_bytes())
            evidence[-1].update(seed_log=seed_snapshot.relative_to(ROOT).as_posix(), seed_log_sha256=sha(seed_snapshot))
    before = json.loads((WORKSHOP / 'play-profile-before.json').read_text(encoding='utf-8'))
    assert before == profile_hashes(), 'Local play profile changed during release checks'
    network_evidence, missing_network = multiplayer_evidence(digests, WORKSHOP / '验收日志')
    # Upload state is recorded by the sync record, not guessed here: only trust it
    # when the recorded item id still matches mod_id.txt and every recorded file
    # hash equals the candidate actually being verified.
    upload_state = 'NOT_UPLOADED'
    sync_path = ROOT / 'tools/runtime/content-current.json'
    if sync_path.exists():
        sync = json.loads(sync_path.read_text(encoding='utf-8'))
        item_id = (WORKSHOP / 'mod_id.txt').read_text(encoding='utf-8').strip()
        if sync.get('uploaded') and sync.get('workshop_item') == item_id and sync.get('build') == digests:
            upload_state = 'UPLOADED_PRIVATE' if sync.get('workshop_visibility') == 'private' else 'UPLOADED_PUBLIC'
    report = dict(version=manifest['version'], generated_utc=datetime.now(timezone.utc).isoformat(),
                  package_files=digests, resources=len(entries), game='0.111.0', baselib='3.4.7', watcher_required=False,
                  static_package='PASS', targeted_package_checks='PASS' if not missing else 'INCOMPLETE',
                  evidence=evidence, existing_local_play_files_unchanged=True,
                  qa_error_gate='Unknown Godot errors, script/loading exceptions and unclassified game errors reject a check, including after its PASS marker',
                  classified_diagnostics=[
                      'Exact root-certificate, Invalid Task ID and exit-resource diagnostic forms also reproduced in the BaseLib-only startup baseline',
                      'Debug room fixtures may lack current_run.save or its backup; native cleanup reports those two absent temporary files',
                  ],
                  full_playthrough='NOT_RUN', network_multiplayer='PASS_LOCAL_ENET' if not missing_network else 'INCOMPLETE',
                  multiplayer_evidence=network_evidence, missing_multiplayer_checks=missing_network,
                  steam_invites='NOT_VERIFIED', steam_hot_rejoin='NOT_VERIFIED', workshop_subscription='NOT_VERIFIED',
                  artwork_permission='UNRESOLVED: user confirmed no Watcher author permission; attribution is not permission',
                  upload_status=upload_state, cover_source='资料/宣传图/模组封面.png',
                  uploaded_content_only=list(NAMES))
    (WORKSHOP / 'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Package integrity PASS;', len(entries), 'resources; BaseLib only; no game files or saves included.')
    print('Cover:', image_path.stat().st_size, 'bytes; original aspect ratio retained.')
    print('Exact-package checks:', ', '.join(missing) if missing else 'all PASS')
    print('Exact-package multiplayer:', ', '.join(missing_network) if missing_network else 'all PASS')
    if require_checks and (missing or missing_network):
        raise SystemExit('Incomplete targeted package checks')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepare', action='store_true')
    parser.add_argument('--verify', action='store_true', help='Require successful targeted checks of these exact files')
    parser.add_argument('--status', action='store_true', help='Read-only candidate freshness and evidence audit; writes nothing')
    args = parser.parse_args()
    if args.status:
        if args.prepare or args.verify:
            parser.error('--status cannot be combined with --prepare or --verify')
        status()
        raise SystemExit(0)
    if args.prepare:
        prepare()
    verify(args.verify)
