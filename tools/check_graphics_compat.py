"""Verify scoped scene derivatives differ from native data only as documented."""
import hashlib
import json
from pathlib import Path
import re
from pck import index

ROOT = Path(__file__).resolve().parent.parent

def main():
    info = json.loads((ROOT / '资料/美术素材/兼容特效来源.json').read_text(encoding='utf-8'))
    pack = ROOT / 'tools/runtime/SlayTheSpire2.pck'
    entries = index(pack)
    assert len(info['resources']) == 4
    with pack.open('rb') as handle:
        for row in info['resources']:
            offset, size, *_ = entries[row['source']]
            handle.seek(offset)
            native = handle.read(size)
            output = (ROOT / row['output']).read_bytes()
            assert hashlib.sha256(native).hexdigest() == row['source_sha256']
            assert hashlib.sha256(output).hexdigest() == row['sha256']
            if row['source'].endswith('.tscn'):
                lines = native.decode().splitlines(keepends=True)
                for i, line in enumerate(lines):
                    if i == 0: lines[i] = re.sub(r' uid="[^"]+"', '', line)
                    elif 'path="res://shaders/vfx/monsters/decimillipede_suck_shader.tres"' in line:
                        lines[i] = re.sub(r' uid="[^"]+"', '', line).replace('res://shaders/vfx/monsters/decimillipede_suck_shader.tres',
                            'res://PunchConstruct/native_compat/decimillipede_suck.gdshader')
                assert ''.join(lines).replace('\r\n', '\n') == output.decode().replace('\r\n', '\n'), row['output']
            else:
                code = re.search(r'\ncode = "((?:\\.|[^"\\])*)"', native.decode(), re.S)[1]
                code = code.replace('\\t', '\t').replace('\\n', '\n').replace('\\"', '"').replace('\\\\', '\\')
                assert code.count('fma(') == 2
                expected = code.replace('fma(n_out45p0, float(n_out37p0), n_in40p2)', '(n_out45p0 * float(n_out37p0) + n_in40p2)')
                expected = expected.replace('fma(float(n_out37p0), n_out46p0, n_in41p2)', '(float(n_out37p0) * n_out46p0 + n_in41p2)')
                assert output.decode().replace('\r\n', '\n') == expected.replace('\r\n', '\n') and 'fma(' not in expected
    print('PASS: three native scene derivatives retain every texture, node and parameter; only two scalar shader expressions change.')

if __name__ == '__main__': main()
