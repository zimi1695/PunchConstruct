"""Convergence lint: keep soft reflection and pool construction on the safe entry
points so the next contributor cannot reintroduce a type-poisoning static init or
a throw-out-of-pool-construction card lookup.

Flags (line/statement based; extend as new patterns appear):
  1. Bare AccessTools.FieldRefAccess outside SoftRef.cs / CharacterScope.cs
     (CharacterScope wraps it in try/catch -> null; SoftRef resolves it lazily).
     A new bare static FieldRefAccess poisons the whole type on a native rename.
  2. Inline AccessTools.Method(...).MakeGenericMethod(...) outside SoftRef.cs.
     The receiver may span lines and its arguments may nest parentheses; the old
     single-line regex missed both, so this uses a balanced scan.
  3. A static field initializer that calls AccessTools directly outside
     SoftRef.cs / CharacterScope.cs. A missing native member then throws
     TypeInitializationException before any Run/Guard can fall back.
  4. Bare AccessTools.*(...).Invoke/GetValue/SetValue in production code. Click
     paths must resolve through SoftRef (null -> native fallback) instead of
     invoking a possibly-null MethodInfo/FieldInfo. QA-only sources are exempt.
  5. Bare ModelDb.Card<...> in ConstructPools.cs (GenerateAllCards must use TryCard
     so a removed native card degrades instead of making the character disappear).

Harmony patch-target resolution (TargetMethods / AccessTools.Method for a patch
target) is legitimate and NOT flagged: only the inline poison patterns above are.
tools/NativeProbe/PoolShutdown.cs is compiled into the public DLL, so it is
scanned for patterns 1-3 even though it is QA-only at runtime.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / 'PunchConstruct'
PROBE = ROOT / 'tools' / 'NativeProbe'
FIELDREF_ALLOW = {'SoftRef.cs', 'CharacterScope.cs'}
STATIC_ALLOW = {'SoftRef.cs', 'CharacterScope.cs'}
INVOKE_ALLOW = {'SoftRef.cs', 'IlBridge.cs'}
QA_NAMES = {'PlaytestRunner.cs', 'SmokeCheck.cs'}

# AccessTools.X(...) where the argument list may contain one level of nested
# parentheses (typeof(...), nameof(...), attribute lookups) and newlines.
ACCESS_CALL = r'AccessTools\.(?:Method|Field|PropertyGetter|PropertySetter)\((?:[^()]|\([^()]*\))*\)'
MAKEGENERIC = re.compile(ACCESS_CALL + r'\s*\.\s*MakeGenericMethod', re.S)
INLINE_INVOKE = re.compile(ACCESS_CALL + r'\s*\.\s*(?:Invoke|GetValue|SetValue)\b', re.S)


def line_of(text, index):
    return text.count('\n', 0, index) + 1


def is_qa(path):
    return path.name.endswith('Checks.cs') or path.name in QA_NAMES or 'NativeProbe' in path.parts


def scan_static_initializers(path, text, violations):
    if path.name in STATIC_ALLOW:
        return
    lines = text.splitlines()
    index = 0
    while index < len(lines):
        line = lines[index]
        if 'static readonly' not in line:
            index += 1
            continue
        statement = line
        cursor = index
        # A field initializer is one statement; stop at the first semicolon or
        # after a bounded window so an unrelated block cannot be swallowed.
        while ';' not in statement and cursor + 1 < len(lines) and cursor - index < 8:
            cursor += 1
            statement += '\n' + lines[cursor]
        if 'AccessTools.' in statement and 'SoftRef.' not in statement:
            violations.append('%s:%d static field initializer calls AccessTools directly '
                              '(route through SoftRef.Register)' % (path.relative_to(ROOT), index + 1))
        index = cursor + 1


def main():
    violations = []
    files = sorted(SRC.rglob('*.cs'))
    if PROBE.exists():
        files += sorted(PROBE.rglob('*.cs'))
    for path in files:
        if 'bin' in path.parts or 'obj' in path.parts:
            continue
        name = path.name
        text = path.read_text(encoding='utf-8-sig')
        for lineno, line in enumerate(text.splitlines(), 1):
            if 'AccessTools.FieldRefAccess' in line and name not in FIELDREF_ALLOW:
                violations.append('%s:%d bare FieldRefAccess (route through SoftRef.FieldRef)' % (path.relative_to(ROOT), lineno))
            if name == 'ConstructPools.cs' and re.search(r'ModelDb\.Card<(?!T>)', line):
                violations.append('%s:%d bare ModelDb.Card< in pool construction (use TryCard<>)' % (path.relative_to(ROOT), lineno))
        if name not in {'SoftRef.cs'}:
            for match in MAKEGENERIC.finditer(text):
                violations.append('%s:%d inline AccessTools.Method(...).MakeGenericMethod on possibly-null '
                                  '(resolve through SoftRef and guard)' % (path.relative_to(ROOT), line_of(text, match.start())))
        if not is_qa(path) and name not in INVOKE_ALLOW:
            for match in INLINE_INVOKE.finditer(text):
                violations.append('%s:%d inline AccessTools reflection invoke in production '
                                  '(resolve through SoftRef, fall back to native)' % (path.relative_to(ROOT), line_of(text, match.start())))
        scan_static_initializers(path, text, violations)
    if violations:
        print('COMPAT LINT FAIL:')
        for violation in violations:
            print('  ' + violation)
        sys.exit(1)
    print('COMPAT LINT PASS: soft reflection and pool construction stay on SoftRef/IlBridge/TryCard')


if __name__ == '__main__':
    main()
