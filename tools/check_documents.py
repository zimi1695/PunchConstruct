"""Read-only checks for the current roster, documentation links and art manifest."""
from collections import Counter
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent.parent


def main():
    roster = (ROOT / '资料/卡牌清单.txt').read_text(encoding='utf-8')
    entries = re.findall(r'^(\d{2})\s+([^\s：]+)：(攻击|技能|能力)／(白卡|蓝卡|金卡|基础|先古)。', roster, re.M)
    assert [int(e[0]) for e in entries] == list(range(1, 83)), 'Missing, repeated or unordered card numbers'
    assert len({e[1] for e in entries}) == 82, 'Repeated card title'
    by_name = {e[1]: int(e[0]) for e in entries}
    assert Counter(e[3] for e in entries) == {'白卡': 26, '蓝卡': 32, '金卡': 15, '基础': 4, '先古': 5}
    assert Counter(e[2] for e in entries) == {'攻击': 35, '技能': 31, '能力': 16}
    assert {n: by_name[n] for n in ['灼热铁斩波', '铁斩波', '护卫', '与我一斗', '愈战愈勇', '小斩波', '东尼斩波', '震荡']} == {
        '灼热铁斩波': 2, '铁斩波': 82, '护卫': 29, '与我一斗': 69, '愈战愈勇': 70, '小斩波': 79, '东尼斩波': 80, '震荡': 81}
    print('PASS: 82 unique sequential cards, fixed references, rarity and type counts.')

    log = (ROOT / 'tools/runtime/pool-current-check.log').read_text(encoding='utf-8-sig')
    actual = re.findall(r'\[PunchConstruct AUDIT (\d+)\] ([^|]+) \| [^|]+ \| (\w+)', log)
    assert [(int(n), title.strip()) for n, title, _ in actual] == [(int(e[0]), e[1]) for e in entries], 'Roster differs from actual game audit'
    assert '[PunchConstruct POOL PASS]' in log and '[Construct QA FAIL]' not in log
    actual_rarities = {}
    grade = {'Common': '白卡', 'Uncommon': '蓝卡', 'Rare': '金卡', 'Basic': '基础', 'Ancient': '先古'}
    for match in re.finditer(r'\[PunchConstruct RARITY\] (\w+) \| \d+ \| (.*?)(?=\[PunchConstruct|FMOD|$)', log, re.S):
        for title in re.sub(r'\s+', '', match[2]).split('、'):
            actual_rarities[title] = grade[match[1]]
    assert all(actual_rarities[e[1]] == e[3] for e in entries), 'A card rarity differs from the actual game audit'
    pool_source = (ROOT / 'PunchConstruct/ConstructPools.cs').read_text(encoding='utf-8')
    imported = set(re.findall(r'ModelDb.Card<(\w+)>\(\)', pool_source))
    designed = set()
    declared = {}
    for path in (ROOT / 'PunchConstruct').glob('*.cs'):
        source = path.read_text(encoding='utf-8')
        designed.update(re.findall(r'class (\w+)\(\) : (?:CustomCardModel|ObserverCard)\(', source))
        for cls, kind, rarity in re.findall(r'class (\w+)\(\) : (?:CustomCardModel|ObserverCard)\([^,]+, CardType\.(\w+), CardRarity\.(\w+)', source):
            declared[cls] = ({'Attack': '攻击', 'Skill': '技能', 'Power': '能力'}[kind], grade[rarity])
    assert imported | designed == {cls for _, _, cls in actual}, 'Code card membership differs from the roster/audit'
    for entry, (_, _, cls) in zip(entries, actual):
        if cls in declared:
            assert tuple(entry[2:]) == declared[cls], cls
    print('PASS: current source membership and actual game card roster agree.')

    manifest = json.loads((ROOT / '资料/美术素材/素材清单.json').read_text(encoding='utf-8'))
    assert len(manifest) == 66 and len({r['source'] for r in manifest}) == 44
    outputs = {r['output'] for r in manifest}
    actual_outputs = {p.relative_to(ROOT).as_posix() for p in (ROOT / 'PunchConstruct/assets/art').glob('*.ctex')}
    assert outputs == actual_outputs and len(outputs) == len(manifest), 'Unlisted or duplicate exported texture'
    sources = set()
    for row in manifest:
        source = ROOT / row['source']
        assert source.exists() and (ROOT / row['output']).exists()
        assert hashlib.sha256(source.read_bytes()).hexdigest() == row['source_sha256'], source
        sources.add(row['source'])
        if 'card_number' in row:
            assert by_name[source.stem] == row['card_number'], source
        for shared in row.get('also_used_by', []):
            assert by_name[shared['name']] == shared['card_number']
    assert Counter(Path(s).parent.name for s in sources) == {'角色': 5, '卡牌': 23, '遗物': 6, '状态图标': 10}
    icons = json.loads((ROOT / '资料/美术素材/状态图标/图标清单.json').read_text(encoding='utf-8'))
    assert len(icons) == 10
    for row in icons:
        assert row['game_linked'] and row['status'] == '已采用'
        for key in ['original', 'size64', 'game_icon', 'game_big']:
            assert (ROOT / row[key]).exists(), row[key]
    print('PASS: all 44 adopted sources and 66 textures, outlines, card numbers and icon provenance.')

    observer = json.loads((ROOT / '资料/美术素材/观者搬运素材.json').read_text(encoding='utf-8'))
    assert len(observer) == 13 and len({row['class'] for row in observer}) == 13
    assert {row['output'] for row in observer} == {p.relative_to(ROOT).as_posix() for p in (ROOT / 'PunchConstruct/assets/observer').glob('*.ctex')}
    for row in observer:
        assert hashlib.sha256((ROOT / row['output']).read_bytes()).hexdigest() == row['sha256']
    dependencies = json.loads((ROOT / 'PunchConstruct/PunchConstruct.json').read_text(encoding='utf-8'))['dependencies']
    assert [d['id'] for d in dependencies] == ['BaseLib']
    assert '<Reference Include="Watcher">' not in (ROOT / 'PunchConstruct/PunchConstruct.csproj').read_text(encoding='utf-8')
    print('PASS: 13 retained observer textures and BaseLib-only mod dependencies.')
    effects = json.loads((ROOT / '资料/美术素材/观者特效来源.json').read_text(encoding='utf-8'))
    assert len(effects) == 13
    assert {r['output'] for r in effects} == {p.relative_to(ROOT).as_posix() for p in (ROOT / 'PunchConstruct/assets/observer_vfx').iterdir() if p.is_file()}
    for row in effects:
        assert hashlib.sha256((ROOT / row['output']).read_bytes()).hexdigest() == row['sha256']
    print('PASS: scoped Divinity textures, audio and shader provenance; no external runtime resource path.')

    docs = [ROOT / p for p in ['README.md', 'tools/README.md', 'dist/README.md', '资料/角色设计.md', '资料/美术素材/README.md']]
    for path in docs:
        text = path.read_text(encoding='utf-8')
        for target in re.findall(r'\[[^\]]+\]\(([^)]+)\)', text):
            if '://' in target or target.startswith('#'):
                continue
            assert (path.parent / target.split('#')[0]).exists(), (path, target)
    current = '\n'.join(p.read_text(encoding='utf-8') for p in docs) + roster
    for stale in ['十三张卡面', '共73种', '共 73 种', '动量等级1', '营养汤目前只给予永恒', '没有自行新增0费']:
        assert stale not in current, stale
    assert '每场从1' in current and '5动量' in roster and '0费' in roster and '自旋螺丝' in current
    print('PASS: current document links and latest growth, Momentum, Eternal and Screw rules.')

    dialogue = (ROOT / '资料/角色对白.txt').read_text(encoding='utf-8')
    normalize = lambda value: re.sub(r'\s+', '', re.sub(r'\[/?[^\]]+\]', '', value))
    for table, prefixes in [('events', ('PUNCHCONSTRUCT_SPIRALING_WHIRLPOOL.', 'PUNCHCONSTRUCT_AMALGAMATOR.', 'PUNCHCONSTRUCT_BUGSLAYER.')),
                            ('relics', ('PUNCHCONSTRUCT_ADAPTED_ARCHAIC_TOOTH.', 'PUNCHCONSTRUCT_ADAPTED_NEOWS_TALISMAN.', 'PUNCHCONSTRUCT_ADAPTED_NUTRITIOUS_SOUP.'))]:
        values = json.loads((ROOT / f'PunchConstruct/assets/localization/zhs/{table}.json').read_text(encoding='utf-8'))
        for key, value in values.items():
            if key.startswith(prefixes):
                assert normalize(value) in normalize(dialogue), ('Dialogue differs from actual game text', key)
    assert '四、漩涡与融合者事件' in dialogue and '至少两张可移除的小斩波' in dialogue
    print('PASS: event/relic game text matches dialogue document and active event rules.')

    version = json.loads((ROOT / 'PunchConstruct/PunchConstruct.json').read_text(encoding='utf-8'))['version']
    for name in ['README.md', 'tools/README.md', '资料/角色设计.md', '资料/问题记录.txt', '资料/发布前清单.txt',
                 'dist/README.md', 'dist/SteamWorkshop/安装说明.txt', 'dist/SteamWorkshop/发布审查.txt']:
        assert version in (ROOT / name).read_text(encoding='utf-8'), ('Current version missing', name)
    role = (ROOT / '资料/角色设计.md').read_text(encoding='utf-8')
    assert '无色牌共15种' in role and '普通随机无色来源为11种' in role
    assert '全难度' in role and '共7张' not in role
    config = json.loads((ROOT / 'dist/SteamWorkshop/workshop.json').read_text(encoding='utf-8'))
    squeezed = re.sub(r'\s+', '', config['description'])
    assert '82种卡牌' in squeezed and '22张原创设计' in squeezed
    assert '[url=https://steamcommunity.com/sharedfiles/filedetails/?id=3747526116]原模组页面[/url]' in config['description']
    assert (ROOT / 'dist/SteamWorkshop/发布说明.txt').read_text(encoding='utf-8') == config['title'] + '\n\n' + config['description'] + '\n'
    sync = json.loads((ROOT / 'tools/runtime/content-current.json').read_text(encoding='utf-8'))
    assert sync['version'] == version
    for name, digest in sync['build'].items():
        assert hashlib.sha256((ROOT / 'dist/SteamWorkshop/content' / name).read_bytes()).hexdigest() == digest, name
    print('PASS: current versions, manual-install content hashes, colorless counts and release copy agree.')


if __name__ == '__main__':
    main()
