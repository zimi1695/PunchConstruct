"""Summarize exact-build local evidence; never export or modify release files."""
import hashlib
import json
from pathlib import Path
import re
from check_current import qa_issues
from workshop import checks_for_version
from pck import index

ROOT = Path(__file__).resolve().parent.parent
RUNTIME = ROOT / 'tools/runtime'
INVARIANTS = ROOT / 'tools/invariants.json'

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def assertion_count(contents):
    # SmokeCheck historically printed [PunchConstruct CHECK]; count it so a
    # suite is never reported as zero assertions merely because of its prefix.
    return len(re.findall(r'^(?:\[Construct QA \d+\]|\[PunchConstruct CHECK\])', contents, re.M))

def load_invariants():
    ledger = json.loads(INVARIANTS.read_text(encoding='utf-8'))
    if ledger.get('schema') != 1:
        raise SystemExit('Unsupported invariants schema: ' + str(ledger.get('schema')))
    return ledger

def evidence_assertions(evidence, suite):
    if suite == 'multiplayer':
        return sum((peer.get('assertions') or 0) for peer in evidence.get('peers', {}).values())
    return sum((log.get('assertions') or 0) for log in evidence.get('logs', {}).values())

def evaluate_invariants(ledger, checks, coop, recovery, required):
    buckets = dict(local=checks, multiplayer=coop, recovery=recovery)
    rows, referenced = {}, {}
    for invariant in ledger['invariants']:
        suite = invariant['suite']
        row = dict(title=invariant['title'], severity=invariant['severity'], kind=invariant['kind'],
            suite=suite, modes=list(invariant.get('modes', [])), status='PASS', assertions=0,
            evidence=[], missing=[], failed=[])
        if suite == 'external':
            row['status'] = 'EXTERNAL'
            row['evidence'] = [invariant.get('evidence')]
            rows[invariant['id']] = row
            continue
        pool = buckets[suite]
        for mode in row['modes']:
            referenced[mode] = suite
            if mode not in pool:
                row['missing'].append(mode)
                continue
            if not pool[mode]['passed']:
                row['failed'].append(mode)
            row['assertions'] += evidence_assertions(pool[mode], suite)
            row['evidence'].append(pool[mode]['evidence'])
        if row['missing']:
            row['status'] = 'MISSING'
        elif row['failed']:
            row['status'] = 'FAIL'
        rows[invariant['id']] = row
    known = {mode for values in required.values() for mode in values}
    return dict(invariants=rows, unmapped_modes=sorted(known - set(referenced)),
        unknown_modes=sorted(set(referenced) - known))

def evaluate_families(ledger, checks):
    rows = {}
    for family in ledger.get('families', []):
        modes = family['modes']
        missing = [mode for mode in modes if mode not in checks]
        failed = [mode for mode in modes if mode in checks and not checks[mode]['passed']]
        rows[family['id']] = dict(title=family['title'], note=family['note'], modes=modes,
            status='MISSING' if missing else 'FAIL' if failed else 'PASS',
            missing=missing, failed=failed)
    return rows

def classification_issues(classification, required_local):
    listed = []
    for kind in ('gate', 'content', 'presentation', 'behavior', 'data_audit'):
        listed += classification.get(kind, [])
    duplicated = sorted({mode for mode in listed if listed.count(mode) > 1})
    return duplicated, sorted(set(required_local) - set(listed))

def main():
    dll = ROOT / 'PunchConstruct/bin/Release/net9.0/PunchConstruct.dll'
    digest = sha(dll)
    version = json.loads((ROOT / 'PunchConstruct/PunchConstruct.json').read_text(encoding='utf-8'))['version']
    required_checks = checks_for_version(version)
    assets = {p.relative_to(ROOT / 'PunchConstruct/assets').as_posix(): sha(p)
              for p in sorted((ROOT / 'PunchConstruct/assets').rglob('*')) if p.is_file()}
    checks, coop, controls, recovery = {}, {}, {}, {}
    for result in sorted((RUNTIME / 'qa').glob('*/result.json')):
        row = json.loads(result.read_text(encoding='utf-8'))
        network = row.get('scenario') == 'coop' and row.get('build', {}).get('PunchConstruct.dll') == digest
        if not network and row.get('dll_sha256') != digest:
            continue
        key = 'coop-{}-{}p'.format(row['guest'], row['players']) if network else row.get('mode')
        control = key == 'shaderoriginal'
        if not network and key not in required_checks and not control:
            continue
        evidence = dict(evidence=result.parent.relative_to(ROOT).as_posix(), result_sha256=sha(result),
            dll_sha256=digest, matches_current_dll=True, passed=row['passed'], logs={})
        if row.get('qa_helpers'): evidence['qa_helpers'] = row['qa_helpers']
        for log in sorted(result.parent.glob('*.log')):
            contents = log.read_text(encoding='utf-8-sig', errors='replace')
            issues = qa_issues(contents)
            evidence['logs'][log.name] = dict(sha256=sha(log), assertions=assertion_count(contents),
                unclassified_errors=issues)
            evidence['passed'] &= not issues
        if network:
            evidence['build'] = row['build']
            if 'network' in row:
                evidence['network'] = row['network']
            evidence['snapshots'] = row.get('snapshots')
            evidence['peers'] = {name: dict(assertions=p.get('assertions'), exit_code=p.get('exit_code'),
                errors=p.get('errors')) for name, p in row['peers'].items()}
            coop[key + ('-network' if 'network' in row else '')] = evidence
        elif control:
            controls[key] = evidence
        else:
            checks[key] = evidence
    for result in sorted((RUNTIME / 'qa').glob('recovery-*/result.json')):
        row = json.loads(result.read_text(encoding='utf-8'))
        independent = row.get('independent', False)
        if not row.get('phases') or not row.get('probe_sha256'):
            continue
        if not independent and row.get('build', {}).get('PunchConstruct.dll') != digest:
            continue
        mode = row.get('disconnect', 'initial-harness')
        key = 'recovery-{}-{}{}'.format(row['character'], mode, '-live-join' if row.get('live_join') else '')
        if row.get('players', 2) > 2 or row.get('party', 'construct') != 'construct':
            key += '-{}-{}p'.format(row.get('party', 'construct'), row.get('players', 2))
        if row.get('resume_order') == 'reverse': key += '-reverse'
        diagnostic = independent or row['character'] != 'construct' or mode not in ('menu', 'abrupt', 'process-loss', 'host-loss') or row.get('live_join')
        evidence = dict(evidence=result.parent.relative_to(ROOT).as_posix(), result_sha256=sha(result),
            passed=row['passed'], phases=row['phases'], probe_sha256=row['probe_sha256'],
            snapshots_match=row.get('snapshots_match', False), resume_passed=row.get('resume_passed', False), logs={})
        evidence['players'] = row.get('players', 2)
        evidence['party'] = row.get('party', 'construct')
        evidence['resume_order'] = row.get('resume_order', 'same')
        if independent:
            key += '-independent'
            evidence['character_mod_loaded'] = False
            evidence['scope'] = 'Game + BaseLib + independent QA only; not an exact-character-DLL check'
        else:
            evidence['build'] = row['build']
            evidence['dll_sha256'] = digest
            evidence['matches_current_dll'] = True
        for log in sorted(result.parent.glob('*.log')):
            if log.stem.endswith('-console'):
                continue
            contents = log.read_text(encoding='utf-8-sig', errors='replace')
            issues = qa_issues(contents)
            evidence['logs'][log.name] = dict(sha256=sha(log),
                assertions=len(re.findall(r'^\[Recovery CHECK \d+\]', contents, re.M)), unclassified_errors=issues)
            evidence['passed'] &= not issues
        (controls if diagnostic else recovery)[key] = evidence
    missing = sorted(set(required_checks) - set(checks))
    failed = sorted(k for k, v in checks.items() if not v['passed'])
    required_coop = {'coop-construct-2p', 'coop-necrobinder-3p', 'coop-construct-4p',
                     'coop-construct-4p-network'}
    missing_coop = sorted(required_coop - set(coop))
    failed_coop = sorted(k for k, evidence in coop.items() if not evidence['passed'])
    failed_recovery = sorted(k for k, evidence in recovery.items() if not evidence['passed'])
    required_recovery = {'recovery-construct-menu', 'recovery-construct-abrupt',
                         'recovery-construct-process-loss-construct-4p-reverse',
                         'recovery-construct-host-loss-mixed-3p-reverse'}
    missing_recovery = sorted(required_recovery - set(recovery))
    ledger = load_invariants()
    classification = ledger.get('classification', {})
    duplicate_classified, unclassified = classification_issues(classification, required_checks)
    required = dict(local=required_checks, multiplayer=required_coop, recovery=required_recovery)
    evaluated = evaluate_invariants(ledger, checks, coop, recovery, required)
    families = evaluate_families(ledger, checks)
    unknown_family_modes = sorted({mode for family in ledger.get('families', [])
        for mode in family['modes']} - set(required_checks))
    mode_assertions = {mode: evidence_assertions(evidence, 'local') for mode, evidence in checks.items()}
    family_mode_total = sum(len(family['modes']) for family in ledger.get('families', []))
    suite_entries = len(required_checks) - family_mode_total + len(ledger.get('families', []))
    counts = dict(mode_runs=len(required_checks), suite_entries=suite_entries, executed=len(checks),
        assertions=sum(mode_assertions.values()),
        gate_modes=len(classification.get('gate', [])), content_modes=len(classification.get('content', [])),
        presentation_modes=len(classification.get('presentation', [])), behavior_modes=len(classification.get('behavior', [])),
        data_audits=len(classification.get('data_audit', [])),
        data_audit_assertions=sum(mode_assertions.get(mode, 0) for mode in classification.get('data_audit', [])))
    invariant_rows = evaluated['invariants']
    invariant_total = sum(1 for row in invariant_rows.values() if row['status'] != 'EXTERNAL')
    invariant_passed = sum(1 for row in invariant_rows.values() if row['status'] == 'PASS')
    invariant_failed = sum(1 for row in invariant_rows.values() if row['status'] == 'FAIL')
    invariant_missing = sum(1 for row in invariant_rows.values() if row['status'] == 'MISSING')
    invariant_external = sum(1 for row in invariant_rows.values() if row['status'] == 'EXTERNAL')
    full_invariant = (invariant_passed == invariant_total and not evaluated['unmapped_modes']
        and not evaluated['unknown_modes'] and not unknown_family_modes
        and not duplicate_classified and not unclassified
        and not failed and not failed_coop and not failed_recovery)
    candidate_dir = ROOT / 'dist/SteamWorkshop'
    names = ('PunchConstruct.dll', 'PunchConstruct.pck', 'PunchConstruct.json')
    candidate_files = {name: sha(candidate_dir / 'content' / name) for name in names if (candidate_dir / 'content' / name).is_file()}
    candidate_matches = set(candidate_files) == set(names) and candidate_files.get('PunchConstruct.dll') == digest and candidate_files.get('PunchConstruct.json') == sha(ROOT / 'PunchConstruct/PunchConstruct.json')
    if candidate_matches:
        packed = index(candidate_dir / 'content/PunchConstruct.pck')
        candidate_matches = set(packed) == {'PunchConstruct/' + name for name in assets}
        if candidate_matches:
            with (candidate_dir / 'content/PunchConstruct.pck').open('rb') as stream:
                for name, (offset, length, md5, flags) in packed.items():
                    stream.seek(offset)
                    if flags != 0 or hashlib.sha256(stream.read(length)).hexdigest() != assets[name.removeprefix('PunchConstruct/')]:
                        candidate_matches = False
                        break
    candidate_report = candidate_dir / 'verification.json'
    candidate_record = json.loads(candidate_report.read_text(encoding='utf-8')) if candidate_report.exists() else {}
    candidate_verified = candidate_matches and candidate_record.get('package_files') == candidate_files and candidate_record.get('targeted_package_checks') == 'PASS' and candidate_record.get('network_multiplayer') == 'PASS_LOCAL_ENET'
    report = dict(schema=3, version=version, dll_sha256=digest, asset_count=len(assets),
        resource_tree_sha256=hashlib.sha256(json.dumps(assets, sort_keys=True).encode()).hexdigest(),
        manifest_sha256=sha(ROOT / 'PunchConstruct/PunchConstruct.json'), checks=checks, multiplayer=coop, recovery_checks=recovery, diagnostic_controls=controls,
        missing_checks=missing, failed_checks=failed, missing_multiplayer=missing_coop, failed_multiplayer=failed_coop,
        missing_recovery=missing_recovery, failed_recovery=failed_recovery,
        full_local_verification=not (missing or failed or missing_coop or failed_coop or missing_recovery or failed_recovery),
        full_release_verification=False, exported=bool(candidate_matches), uploaded=False,
        candidate=dict(directory=candidate_dir.relative_to(ROOT).as_posix(), matches_current_files=bool(candidate_matches),
            local_verification=bool(candidate_verified), verification_sha256=sha(candidate_report) if candidate_report.exists() else None),
        mode_classification={kind: classification.get(kind, []) for kind in
            ('gate', 'content', 'presentation', 'behavior', 'data_audit')},
        counts=counts, families=families, invariants=invariant_rows,
        invariant_summary=dict(total=invariant_total, passed=invariant_passed, failed=invariant_failed,
            missing=invariant_missing, external=invariant_external,
            unmapped_modes=evaluated['unmapped_modes'], unknown_modes=evaluated['unknown_modes'],
            unknown_family_modes=unknown_family_modes,
            unclassified_modes=unclassified, duplicate_classified=duplicate_classified),
        full_invariant_verification=full_invariant,
        coverage=ledger['coverage'],
        limits=[
            'All listed suites use the exact DLL hash above; historical evidence remains under qa/verification-*.json.',
            'Resource tree fingerprint describes current local assets; multiplayer results also pin each tested PCK and manifest.',
            'Muted, isolated targeted fixtures only. No full automated runs or changes to author saves.',
            'Steam invites, internet latency/loss, reconnection and real Workshop subscription are not verified by local ENet.',
            'The intermittent native cached fma error was captured during native-role threaded scene preloading. The construct uses a verified isolated non-fma shader; other drivers and platforms are not universally certified.',
            'The unchanged native-role shader control can reproduce the native cached-code fma error. Its strict diagnostic result is retained separately; it does not certify construct functionality or require modifying other characters.',
            'Native card-pool controls are freed only at explicit QA shutdown; normal gameplay pooling and error allowlists are unchanged.',
            'Candidate freshness and local verification are recorded separately. Neither this local report nor private visibility authorizes upload; final author review and Steam checks remain necessary.',
            'Recovery checks cover leaving, restarting processes and regrouping from the native host save. Native ENet closes incoming connections once a run begins; no hot-rejoin implementation is claimed.',
            'Construct forced-close checks are required to pass both disconnect and resume phases. Independent/native-only controls remain separate diagnostics; successful resume alone never turns a failed disconnect phase into an overall pass.',
            'Mode runs are execution granularity, not independent evidence. Families group modes that share one suite entry; invariants are the reporting unit.',
            'Coverage lists what these suites did and did not exercise. full_local_verification=true never implies the unverified axes in coverage.unverified are covered.'
        ])
    current = RUNTIME / 'current-verification.json'
    if current.exists():
        previous = json.loads(current.read_text(encoding='utf-8'))
        archive = RUNTIME / 'qa' / ('verification-' + previous['version'] + '.json')
        if previous['version'] != version and not archive.exists(): archive.write_bytes(current.read_bytes())
    current.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(version=version, passed_checks=sum(v['passed'] for v in checks.values()),
        passed_network_checks=[key for key, evidence in coop.items() if 'network' in evidence and evidence['passed']],
        passed_recovery=[key for key, evidence in recovery.items() if evidence['passed']],
        missing_recovery=missing_recovery, failed_recovery=failed_recovery,
        required_checks=len(required_checks), missing=missing, failed=failed, missing_multiplayer=missing_coop,
        failed_multiplayer=failed_coop, full_local_verification=report['full_local_verification'],
        invariants_passed=invariant_passed, invariants_total=invariant_total,
        unmapped_modes=evaluated['unmapped_modes'], unclassified_modes=unclassified,
        full_invariant_verification=report['full_invariant_verification']), indent=2))

if __name__ == '__main__': main()
