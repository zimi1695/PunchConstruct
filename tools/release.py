"""Package with scoped evidence; never require or claim a full simulated run."""
import hashlib
import json
import re
import zipfile
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent.parent
dist = root / 'dist'
mod = dist / 'PunchConstruct'
manifest = json.loads((mod / 'PunchConstruct.json').read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
files = {p.name: sha(p) for p in sorted(mod.iterdir()) if p.is_file()}

def log(name):
    return (root / 'tools/runtime' / name).read_text(encoding='utf-8-sig')

focus, smoke = log('focus-check.log'), log('smoke.log')
flow, end, design = log('flow-check.log'), log('end-check.log'), log('design-check.log')
flow_check_count = len(re.findall(r'\[Construct QA \d+\]', flow))
for label, value, marker, digest_marker in [
    ('focused debug check', focus, '[Construct FOCUS PASS]', 'Construct QA DLL'),
    ('model/resource smoke', smoke, '[PunchConstruct SMOKE PASS]', 'PunchConstruct SMOKE DLL'),
]:
    if marker not in value or '[Construct QA FAIL]' in value or '[PunchConstruct SMOKE FAIL]' in value:
        raise SystemExit(f'Missing successful {label}.')
    match = re.search(r'\[' + re.escape(digest_marker) + r'\] ([0-9a-f]{64})', value)
    if not match or match[1] != files['PunchConstruct.dll']:
        raise SystemExit(f'{label} did not check the packaged DLL.')
    if 'No loader found for resource: res://PunchConstruct' in value or 'Parse Error:' in value:
        raise SystemExit(f'Mod resource error in {label}.')

philosopher_checks = re.findall(r'\[Construct QA \d+\] Philosopher offers three unrestricted-rarity candidates from selected imports of (\w+)', flow)
if any(philosopher_checks.count(color) != 3 for color in ('regent', 'necrobinder', 'defect')):
    raise SystemExit('Missing the three rows for each philosopher color.')
if '[Construct END FLOW PASS]' not in end or '[Construct DESIGN PASS]' not in design:
    raise SystemExit('Missing prior scoped end-flow/design evidence.')

profile = json.loads((root / 'tools/runtime/play-profile-before-qa.json').read_text(encoding='utf-8'))
for path, digest in profile.items():
    if sha(root / path) != digest:
        raise SystemExit(f'The user play save was changed: {path}')
for name, digest in files.items():
    if sha(root / 'tools/runtime/mods/PunchConstruct' / name) != digest:
        raise SystemExit(f'The runtime and package differ: {name}')

def evidence(filename, value, scope, status):
    match = re.search(r'\[Construct QA DLL\] ([0-9a-f]{64})', value)
    return {'log': 'tools/runtime/' + filename, 'log_sha256': sha(root / 'tools/runtime' / filename),
            'dll_sha256': match[1] if match else None, 'scope': scope, 'status': status}

report = {
    'version': manifest['version'], 'verified_utc': datetime.now(timezone.utc).isoformat(),
    'game': '0.111.0', 'baselib': '3.4.7', 'watcher': 'NOT_REQUIRED', 'build': 'Release',
    'engine_smoke': 'PASS', 'smoke_checks': smoke.count('[PunchConstruct CHECK]'),
    'focused_debug': 'PASS', 'focused_checks': int(re.search(r'\[Construct FOCUS PASS\] (\d+)', focus)[1]),
    'test_method': 'Direct debug room entry; one controlled turn for queued draw verification; no map progression or full battle.',
    'focused_check_details': re.findall(r'\[Construct QA \d+\] (.+)', focus),
    'full_playtest': 'NOT_RUN_ON_FINAL_BUILD; earlier broad run stopped at user request',
    'philosopher': 'PASS: Regent/Necrobinder/Defect, three rows each, three unrestricted-rarity candidates per row, one chosen per row',
    'end_flow': 'PASS_ON_EARLIER_BUILD',
    'new_designs': 'SCOPED_CHECKS_PASSED_ON_EARLIER_BUILD',
    'evidence': [
        evidence('focus-check.log', focus, 'Shock/Shock+ on multiple enemies, starter two Vigor, native Touch of Orobas replacement, ancient five Vigor and actual next-turn draw, scoped Iron Wave art and separate ancient relic art', 'PASS; final packaged DLL'),
        evidence('focus-v0.3.0.log', log('focus-v0.3.0.log'), 'Transformation pool/gates; Punch Off two options, Nab reward/cost and completion text', '5 checks PASS on v0.3.0; not repeated'),
        evidence('flow-check.log', flow, 'Adopted art; native rewards/shop/rest/events; all three philosopher colors', f'{flow_check_count} checks completed; interrupted later during Nab reward UI; no overall PASS'),
        evidence('end-check.log', end, 'Abandon, lethal damage, game-over, save removal, menu cleanup and fresh combat', '7 checks PASS on earlier build; not repeated'),
        evidence('design-check.log', design, 'New card mechanics, upgrades, forge/doom/summon and acquisition rules', 'DESIGN block passed; later flow harness failed on an incorrect reward-button cast, since corrected; no overall PASS'),
    ],
    'existing_play_saves_unchanged': True,
    'adopted_art_sources': len({x['source'] for x in json.loads((root / '资料/美术素材/素材清单.json').read_text(encoding='utf-8'))}),
    'untested': ['full final-build regression', 'full-run balance', 'high ascension', 'network multiplayer', 'arbitrary mod combinations'],
    'logs_note': 'Engine startup/thread-pool and renderer shutdown diagnostics remain. Earlier partial logs are scoped evidence, not a full-regression PASS.',
    'files': files,
}
(dist / 'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
archive = dist / f"PunchConstruct-v{manifest['version']}.zip"
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as z:
    for p in sorted(mod.iterdir()):
        if p.is_file(): z.write(p, 'PunchConstruct/' + p.name)
    z.write(root / 'README.md', 'README.md')
    z.write(dist / 'verification.json', 'verification.json')
    z.write(root / '资料/美术素材/素材清单.json', 'adopted-art.json')
print(f'Packaged with scoped verification: {archive} ({archive.stat().st_size} bytes)')
