"""Archive structured game diagnostics before disposable test profiles disappear."""
import hashlib
import json
from pathlib import Path
import shutil


def archive_diagnostics(userdata, destination, *, require_actions=False, expected_dll=None):
    destination.mkdir(parents=True, exist_ok=True)
    files = sorted(p for p in userdata.rglob('pc-*.jsonl') if p.parent.name == 'logs')
    records = []
    errors = []
    for source in files:
        shutil.copy2(source, destination / source.name)
        try:
            records.extend(json.loads(line) for line in source.read_text(encoding='utf-8').splitlines())
        except (ValueError, OSError) as error:
            errors.append('Invalid diagnostic JSONL: ' + str(error))
    if not records:
        errors.append('Missing structured diagnostics')
    sessions = {}
    for row in records:
        if not {'schema', 'utc', 'seq', 'session', 'run', 'level', 'event', 'context', 'data'} <= row.keys():
            errors.append('Missing diagnostic schema fields')
        sessions.setdefault(row.get('session'), []).append(row)
        if row.get('event') in ('diagnostics.observer_failed', 'runtime.unhandled', 'action.failed', 'room.failed', 'network.divergence', 'qa.failed'):
            errors.append('Unexpected diagnostic failure: ' + row['event'])
    for session in sessions.values():
        ids = [r['seq'] for r in session]
        if ids != sorted(set(ids)):
            errors.append('Non-monotonic or duplicated diagnostic sequence')
        starts = [r for r in session if r['event'] == 'session.start']
        if not starts or any(len(r['data'].get('dllSha256', '')) != 64 for r in starts):
            errors.append('Missing build fingerprint')
        elif expected_dll is not None:
            # Closed loop: every session must attest the exact candidate DLL the
            # game actually loaded (assembly.Location), not merely a well-formed
            # fingerprint. A stale or wrong-path DLL fails the gate here.
            bad = [r['data']['dllSha256'] for r in starts if r['data']['dllSha256'] != expected_dll]
            if bad:
                errors.append('Build fingerprint mismatch: loaded %s expected %s' % (bad[0][:12], expected_dll[:12]))
    if require_actions:
        events = {r['event'] for r in records}
        for name in ('run.setup', 'room.enter', 'action.begin', 'action.end', 'choice.sent', 'choice.received', 'run.cleanup'):
            if name not in events:
                errors.append('Missing exercised lifecycle log: ' + name)
    return dict(files=[dict(name=p.name, sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in files],
                records=len(records), events=sorted({r['event'] for r in records}), errors=errors)
