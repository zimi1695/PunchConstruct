"""Prepare scoped localization for both languages and the compiled text table."""
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
draft = (ROOT / '资料/角色对白.txt').read_text(encoding='utf-8-sig')
section = draft.split('二、先古专属对白')[1].split('三、「重拳出击」')[0]
blocks = re.split(r'\n\d+\. ', section)[1:]
ancients = {}
for block in blocks:
    name = re.search(r'（([A-Z_]+)', block).group(1)
    lines = [line.split('：', 1)[1].strip() for line in block.splitlines() if '：' in line and not line.startswith('名称')]
    # Exactly three groups, each ancient / construct / ancient.
    if len(lines) != 9:
        raise ValueError((name, len(lines)))
    for i in range(3):
        for j in range(3):
            key = f'PUNCHCONSTRUCT_{name}.talk.PUNCHCONSTRUCT-CONSTRUCT_CHARACTER.{i}-{j}r'
            ancients[key + ('.char' if j == 1 else '.ancient')] = lines[i * 3 + j]
            if j < 2:
                ancients[key + '.next'] = '继续'
events = {
    'PUNCHCONSTRUCT_PUNCH_OFF.pages.INITIAL.description': '两台与你同型号的[gold]拳击构装体[/gold]正在互殴，脚边散落着财宝。\n\n看见同类到来，它们各自挪开半步，给你腾了个位置。',
    'PUNCHCONSTRUCT_PUNCH_OFF.pages.NAB.description': '你和它们碰了碰拳，顺手拿起一件遗物。\n\n它们用一记右勾拳表示反对。',
    'PUNCHCONSTRUCT_PUNCH_OFF.pages.I_CAN_TAKE_THEM.description': '你示意它们，举起双拳。\n\n两台构装体碰了一下拳，并肩朝你走来。',
}
events.update({
    'PUNCHCONSTRUCT_SPIRALING_WHIRLPOOL.pages.INITIAL.options.OBSERVE.description': '为一张[gold]小斩波[/gold][gold]附魔[/gold]：[purple]涡旋[/purple]。',
    'PUNCHCONSTRUCT_SPIRALING_WHIRLPOOL.pages.OBSERVE.description': '水流在你的拳套间盘旋。\n\n你记下这股劲道，将它带离泉边。',
    'PUNCHCONSTRUCT_AMALGAMATOR.pages.INITIAL.description': '[b][jitter]铿！铿！！锵！！！[/jitter][/b]\n\n工坊的门轰然开启，六臂工匠打量着你的拳套。\n\n“攻守兼备？不错。不过，还能再锻一锻。”',
    'PUNCHCONSTRUCT_AMALGAMATOR.pages.INITIAL.options.COMBINE_STRIKES.title': '融合斩波',
    'PUNCHCONSTRUCT_AMALGAMATOR.pages.INITIAL.options.COMBINE_STRIKES.description': '移除[blue]2[/blue]张[gold]小斩波[/gold]，将一张[gold]铁斩波[/gold]加入你的牌组。',
    'PUNCHCONSTRUCT_AMALGAMATOR.pages.INITIAL.options.COMBINE_DEFENDS.title': '淬炼铁斩波',
    'PUNCHCONSTRUCT_AMALGAMATOR.pages.INITIAL.options.COMBINE_DEFENDS.description': '将一张[gold]铁斩波[/gold][gold]变化[/gold]为[gold]灼热铁斩波[/gold]并[gold]强化[/gold]。',
    'PUNCHCONSTRUCT_AMALGAMATOR.pages.COMBINE_STRIKES.description': '[b][jitter]当！当！！锵！！！[/jitter][/b]\n\n两道斩波在锤下合为一击。\n\n工匠满意地收起铁锤。',
    'PUNCHCONSTRUCT_AMALGAMATOR.pages.COMBINE_DEFENDS.description': '[b][jitter]当！当！！锵！！！[/jitter][/b]\n\n铁剑从炉中抽出，刃上仍流淌着赤红的热意。\n\n“趁热。”',
})

events.update({
    'PUNCHCONSTRUCT_BUGSLAYER.pages.INITIAL.description': '一名战士正忙着驱赶虫群。杀虫剂似乎不太管用。\n\n你活动了一下双臂，摆出熟悉的架势。',
    'PUNCHCONSTRUCT_BUGSLAYER.pages.INITIAL.options.QUICK.title': '快拳',
    'PUNCHCONSTRUCT_BUGSLAYER.pages.INITIAL.options.QUICK.description': '将一张[gold]快速拳[/gold]加入你的牌组。',
    'PUNCHCONSTRUCT_BUGSLAYER.pages.INITIAL.options.COMBINATION.title': '连打',
    'PUNCHCONSTRUCT_BUGSLAYER.pages.INITIAL.options.COMBINATION.description': '将一张[gold]连环拳[/gold]加入你的牌组。',
    'PUNCHCONSTRUCT_BUGSLAYER.pages.INITIAL.options.GUARD.title': '护住',
    'PUNCHCONSTRUCT_BUGSLAYER.pages.INITIAL.options.GUARD.description': '将一张[gold]格挡[/gold]加入你的牌组。',
    'PUNCHCONSTRUCT_BUGSLAYER.pages.QUICK.description': '一拳。\n\n墙上多了一个拳印，少了几只虫子。',
    'PUNCHCONSTRUCT_BUGSLAYER.pages.COMBINATION.description': '第一拳打散虫群，第二拳紧随其后。\n\n那名战士默默放下了杀虫剂。',
    'PUNCHCONSTRUCT_BUGSLAYER.pages.GUARD.description': '你架起双臂。\n\n虫群撞上拳套，噼里啪啦落了一地。',
})

relic_descriptions = {
    'PUNCHCONSTRUCT_ADAPTED_RUINED_HELMET.description': '每场战斗首次获得[gold]力量[/gold]或[gold]敏捷[/gold]时，获得等量的另一项属性。整场共触发一次。',
    'PUNCHCONSTRUCT_ADAPTED_ARCHAIC_TOOTH.description': '将一张[gold]铁斩波[/gold][gold]变化[/gold]为[gold]灼热铁斩波[/gold]并[gold]强化[/gold]3次。',
    'PUNCHCONSTRUCT_ADAPTED_NEOWS_TALISMAN.description': '[gold]强化[/gold]两张[gold]小斩波[/gold]。',
    'PUNCHCONSTRUCT_ADAPTED_NUTRITIOUS_SOUP.description': '拾起时，将你[gold]牌组[/gold]中的所有[gold]小斩波[/gold][gold]变化[/gold]为[gold]铁斩波[/gold]，并赋予[gold]永恒[/gold]。',
    'PUNCHCONSTRUCT_ADAPTED_PANDORAS_BOX.description': '拾起时，将所有可变化的[gold]小斩波[/gold]、[gold]打击[/gold]和[gold]防御[/gold][gold]变化[/gold]为其他牌。',
    'PUNCHCONSTRUCT_ADAPTED_PAELS_CLAW.description': '拾起时，为所有未附魔的[gold]小斩波[/gold]附魔[purple]黏糊[/purple]。',
}

translations_path = ROOT / 'tools/english_text.json'
if not translations_path.exists():
    raise SystemExit('Missing tools/english_text.json; English text has not been prepared.')
translations = json.loads(translations_path.read_text(encoding='utf-8-sig'))
official = json.loads((ROOT / 'tools/official_terms.json').read_text(encoding='utf-8-sig'))
cjk = re.compile(r'[\u4e00-\u9fff]')
highlight = re.compile(r'\[(gold|purple|blue|red)\]([^\[]+)\[/\1\]')


def normalized(term):
    term = term.lower().replace("'s", "")
    return term[:-1] if term.endswith('s') else term


def check_official(chinese, text):
    if chinese in official and normalized(text) != normalized(official[chinese]):
        raise ValueError(f'Inconsistent official term: {chinese} -> {official[chinese]} (got {text})')
    zh_tags = [content for _, content in highlight.findall(chinese)]
    en_tags = {normalized(content) for _, content in highlight.findall(text)}
    for zh_tag in zh_tags:
        if zh_tag not in official:
            continue
        expected = normalized(official[zh_tag])
        if expected not in en_tags:
            raise ValueError(f'Inconsistent highlighted term: {zh_tag} -> {official[zh_tag]} (missing from {text})')


def english(value):
    if not cjk.search(value):
        return value
    if value not in translations:
        raise ValueError('Missing English text: ' + value)
    result = translations[value]
    check_official(value, result)
    return result


for locale in ('zhs', 'eng'):
    folder = ROOT / 'PunchConstruct/assets/localization' / locale
    for table, values in [('ancients', ancients), ('events', events)]:
        output = {key: (value if locale == 'zhs' else english(value)) for key, value in values.items()}
        (folder / (table + '.json')).write_text(json.dumps(output, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    source = ROOT / 'PunchConstruct/assets/localization/zhs'
    relics = json.loads((source / 'relics.json').read_text(encoding='utf-8-sig'))
    cards = json.loads((source / 'cards.json').read_text(encoding='utf-8-sig'))
    for key, value in relic_descriptions.items():
        relics[key] = value
    for key, value in relics.items():
        relics[key] = value if locale == 'zhs' else english(value)
    for key, value in cards.items():
        cards[key] = value if locale == 'zhs' else english(value)
    (folder / 'relics.json').write_text(json.dumps(relics, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (folder / 'cards.json').write_text(json.dumps(cards, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

# BaseLib resolves a table per game language; a locale without its own folder
# shows raw keys. Mirror the authored tables so every shipped locale has text:
# traditional Chinese reuses zhs, every other language reuses English.
mirrors = {'zht': 'zhs', 'deu': 'eng', 'esp': 'eng', 'fra': 'eng', 'ind': 'eng',
           'ita': 'eng', 'jpn': 'eng', 'kor': 'eng', 'pol': 'eng', 'ptb': 'eng',
           'rus': 'eng', 'spa': 'eng', 'tha': 'eng', 'tur': 'eng'}
for locale, source in mirrors.items():
    folder = ROOT / 'PunchConstruct/assets/localization' / locale
    folder.mkdir(parents=True, exist_ok=True)
    for table in ('ancients', 'events', 'relics', 'cards'):
        shutil.copyfile(ROOT / 'PunchConstruct/assets/localization' / source / (table + '.json'),
                        folder / (table + '.json'))

for table in ('ancients', 'events', 'relics', 'cards'):
    zhs = json.loads((ROOT / 'PunchConstruct/assets/localization/zhs' / (table + '.json')).read_text(encoding='utf-8-sig'))
    eng = json.loads((ROOT / 'PunchConstruct/assets/localization/eng' / (table + '.json')).read_text(encoding='utf-8-sig'))
    if set(zhs) != set(eng):
        raise ValueError('Key mismatch in ' + table)
    if any(isinstance(value, str) and cjk.search(value) for value in eng.values()):
        raise ValueError('Chinese text remains in English table: ' + table)

lines = ['namespace PunchConstruct;', '', '// Generated by tools/prepare_text.py from tools/english_text.json. Do not edit.',
         'internal static class EnglishText', '{',
         '    internal static readonly Dictionary<string, string> Translations = new()', '    {']
for chinese, text in sorted(translations.items()):
    if cjk.search(text) or not text:
        raise ValueError('Invalid English text for: ' + chinese)
    lines.append('        [' + json.dumps(chinese, ensure_ascii=False) + '] = ' + json.dumps(text, ensure_ascii=False) + ',')
lines += ['    };', '}', '']
(ROOT / 'PunchConstruct/EnglishText.Generated.cs').write_text('\n'.join(lines), encoding='utf-8')
print(f'Prepared {len(ancients)} ancient lines, {len(events)} event lines and {len(translations)} compiled English strings.')
