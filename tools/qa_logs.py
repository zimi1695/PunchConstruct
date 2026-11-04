"""Archive structured game diagnostics before disposable test profiles disappear."""
import hashlib
import json
from pathlib import Path
import shutil


def archive_diagnostics(userdata, destination, *, require_actions=False):
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
        if not any(r['event'] == 'session.start' and len(r['data']['dllSha256']) == 64 for r in session):
            errors.append('Missing build fingerprint')
    if require_actions:
        events = {r['event'] for r in records}
        for name in ('run.setup', 'room.enter', 'action.begin', 'action.end', 'choice.sent', 'choice.received', 'run.cleanup'):
            if name not in events:
                errors.append('Missing exercised lifecycle log: ' + name)
    return dict(files=[dict(name=p.name, sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in files],
                records=len(records), events=sorted({r['event'] for r in records}), errors=errors)
