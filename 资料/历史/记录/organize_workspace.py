"""One-time workspace cleanup. No game launch, release export or status-icon integration."""
import hashlib
import json
import os
import re
from collections import defaultdict
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parent.parent
ART = '资料/美术素材'
OLD = '资料/美术草稿'
moves = {}
records = []

def checked(path):
    path = (ROOT / path).resolve()
    if not path.is_relative_to(ROOT):
        raise ValueError(f'Outside workspace: {path}')
    return path

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def plan(old, new):
    source, target = checked(old), checked(new)
    assert source.is_file(), old
    assert not target.exists(), new
    assert new not in moves.values(), new
    moves[old] = new

def write(name, text):
    path = checked(name)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')

def remove(path, reason):
    path = checked(path)
    assert path.is_file()
    records.append({'action': 'delete', 'path': path.relative_to(ROOT).as_posix(), 'sha256': digest(path), 'reason': reason})
    path.unlink()

# Protect gameplay code, current engine resources, release files and user play saves.
protected = list((ROOT/'PunchConstruct').glob('*.cs')) + list((ROOT/'PunchConstruct/assets').rglob('*')) + list((ROOT/'dist').rglob('*'))
protected += list((ROOT/'tools/runtime/play-userdata').rglob('current_run*'))
before = {p: digest(p) for p in protected if p.is_file()}
original_design = (ROOT/'sts2_punch_construct_design.md').read_text(encoding='utf-8-sig')
original_readme = (ROOT/'README.md').read_text(encoding='utf-8-sig')

for old, new in {
    '一些基本设定.txt': '资料/设定/原始设定.txt',
    '卡牌池审阅清单_v0.1.txt': '资料/设定/卡牌清单.txt',
    '角色背景与对白草稿_v0.1.txt': '资料/设定/角色对白.txt',
    '待修复问题清单.txt': '资料/设定/问题记录.txt',
    'sts2_punch_construct_design.md': '资料/历史资料/设计整理旧稿.md',
    '原版全遗物_改动审查稿_v0.1.md': '资料/历史资料/已撤回的遗物改动提案.md',
    '试玩反馈与方向待定_2026-10-21.txt': '资料/历史资料/早期试玩反馈.txt',
}.items():
    plan(old, new)

# Name active art by its actual use, including the user's earlier Small Wave/Iron Wave swap.
for old, new in {
    '头像_像素_v0.2.png': '角色/头像.png',
    '选人海报_v0.1.png': '角色/选人海报.png',
    '铁斩波.png': '卡牌/小斩波.png',
    '卡牌_小斩波_v0.2.png': '卡牌/铁斩波.png',
    '卡牌_铁斩波形态_v0.5.png': '卡牌/铁斩波形态.png',
    '东尼斩波.jpg': '卡牌/东尼斩波.jpg',
    '卡牌_锻造车间_v0(3).jpg': '卡牌/锻造车间.jpg',
    '卡牌_王者风范_v0_facefix.jpg': '卡牌/王者风范.jpg',
    '卡牌_趁热打铁_v04.jpg': '卡牌/趁热打铁.jpg',
    '卡牌_终局裁定_v0.jpg': '卡牌/终局裁定.jpg',
    '卡牌_卸甲成军_v0.jpg': '卡牌/卸甲成军.jpg',
    '卡牌_厄运共鸣_v0.jpg': '卡牌/厄运共鸣.jpg',
    '卡牌_不倒不休_v0.jpg': '卡牌/不倒不休.jpg',
    '卡牌_拳王形态_v0.jpg': '卡牌/拳王形态.jpg',
    '架势_v0.2.png': '遗物/架势.png',
    '架势_v0.1.png': '遗物/先古架势.png',
}.items():
    plan(f'{OLD}/可用素材/{old}', f'{ART}/{new}')

icon_folder = ROOT/OLD/'状态图标草稿_v0.1'
icon_specs = [
    ('06_插地小剑_v0.2.png', '铁斩波形态', '已选'),
    ('06_插地小剑_v0.3_王者风范.png', '王者风范', '已选'),
    ('02_小擂台_v0.1.png', '不倒不休', '待审'),
    ('03_烧红铁锭_v0.1.png', '趁热打铁', '待审'),
    ('04_小车间_v0.1.png', '锻造车间', '待审'),
    ('05_拳王腰带_v0.1.png', '拳王形态', '待审'),
    ('07_沙漏_v0.1.png', '东尼斩波', '待审'),
    ('08_断甲骨手_v0.1.png', '卸甲成军', '待审'),
    ('09_裁定印记_v0.1.png', '终局裁定', '待审'),
]
icons = []
for old, name, status in icon_specs:
    target = f'{ART}/状态图标/{status}/{name}.png'
    small = f'{ART}/状态图标/64像素/{status}/{name}.png'
    plan(f'{OLD}/状态图标草稿_v0.1/{old}', target)
    plan(f'{OLD}/状态图标草稿_v0.1/64x64_待审/{old}', small)
    icons.append({'name': name, 'status': status, 'original': target, 'size64': small, 'game_linked': False})
for old, name in [('01_螺丝_v0.1.png','螺丝'), ('06_插地小剑_v0.1.png','插地剑')]:
    plan(f'{OLD}/状态图标草稿_v0.1/{old}', f'{ART}/历史稿/状态图标/{name}.png')
    plan(f'{OLD}/状态图标草稿_v0.1/64x64_待审/{old}', f'{ART}/历史稿/状态图标/64像素/{name}.png')
plan(f'{OLD}/状态图标草稿_v0.1/九张图标_实际尺寸与放大预览.png', f'{ART}/历史稿/状态图标/初次图标总览.png')

# Keep earlier image drafts, but put draft numbers in directories, not in filenames.
history_counts = defaultdict(int)
for path in sorted((ROOT/OLD/'搁置素材').iterdir()):
    if path.suffix.lower() not in ('.png', '.jpg', '.jpeg', '.webp'):
        continue
    role = re.split(r'_v', path.stem.removeprefix('卡牌_'))[0]
    category = '角色' if role.startswith('头像') else '卡牌'
    role = '头像' if category == '角色' else role
    history_counts[role] += 1
    plan(path.relative_to(ROOT).as_posix(), f'{ART}/历史稿/{category}/{role}/稿件{history_counts[role]}/{role}{path.suffix.lower()}')

prompt_records = {}
for path in icon_folder.glob('*生成记录.json'):
    item = json.loads(path.read_text(encoding='utf-8-sig'))
    prompt_records[item['file']] = item
for line in (icon_folder/'生成记录.jsonl').read_text(encoding='utf-8-sig').splitlines():
    if line.strip():
        item = json.loads(line)
        prompt_records.setdefault(item['file'], item)
early_notes = []
for path in sorted((ROOT/OLD/'绘图说明').glob('*.txt')):
    early_notes.append(f'## {path.stem}\n\n' + path.read_text(encoding='utf-8-sig'))

plan(f'{OLD}/素材接入说明.txt', f'{ART}/制作记录/早期素材接入记录.txt')
plan(f'{OLD}/采用素材清单.json', f'{ART}/素材清单.json')
for path in sorted((ROOT/'资料/遗物审查_v0.1').iterdir()):
    plan(path.relative_to(ROOT).as_posix(), '资料/历史资料/遗物参考数据/' + path.name.replace('_v0.1',''))

# No recursive delete or move: every resolved file target is checked before the operation.
for old, new in moves.items():
    source, target = checked(old), checked(new)
    source_hash = digest(source)
    target.parent.mkdir(parents=True, exist_ok=True)
    source.rename(target)
    assert digest(target) == source_hash
    records.append({'action':'move','from':old,'to':new,'sha256':source_hash})

def relink(text, old_parent, new_parent):
    def one(match):
        target = match.group(1)
        if re.match(r'^[a-z]+:|^#', target, re.I):
            return match.group(0)
        raw, separator, anchor = unquote(target.strip('<>')).partition('#')
        old_absolute = (old_parent/raw).resolve()
        try:
            old_relative = old_absolute.relative_to(ROOT).as_posix()
        except ValueError:
            return match.group(0)
        replacement = moves.get(old_relative)
        if old_relative.startswith(OLD+'/绘图说明/'):
            replacement = f'{ART}/制作记录/早期绘图记录.md'
        if old_relative == 'sts2_punch_construct_design.md':
            replacement = '资料/设定/角色设计.md'
        new_absolute = ROOT/replacement if replacement else old_absolute
        if not new_absolute.exists() and replacement is None:
            return match.group(0)
        value = Path(os.path.relpath(new_absolute,new_parent)).as_posix()
        if separator:
            value += '#'+anchor
        return ']('+value+')'
    return re.sub(r'\]\(([^)\n]+)\)', one, text)

main = original_design.split('# 第一部分　你的设定',1)[1].split('# 第二部分',1)[0]
main = '# 拳击构装体：当前角色设定\n\n> 原始构想见[原始设定](原始设定.txt)，完整卡池见[卡牌清单](卡牌清单.txt)。以下保留用户确认的主体规则；旧问答、美术审稿和已撤回的方案集中保存在历史资料。\n\n' + relink(main,ROOT,ROOT/'资料/设定')
main += '\n## 当前实现与美术状态\n\n- 铁斩波形态图标已选用插地铁剑，王者风范图标已选用王者重剑，均已按用途命名，尚未接入游戏。其余七张图标待审。\n- 小斩波、铁斩波卡面已按用户要求互换，源文件名现与实际用途一致。\n- 营养汤、幽灵种子、正版及假打击木偶使用原图；飞镖改银色主体，头盔改偏银色，均不旋转；水盆继续倒扣。\n- 风的女儿在本角色玩偶事件固定第一，拿一个必得、看两个必含、看三个排第一；三条路线已专项验证。\n- 组内加权×2、先古商店5%、金卡基础价1.5倍、护卫普通卡属于试玩参数，未当作用户定稿。\n- 营养汤批量替换及永恒已测；保存退出重进后的永恒、已有各类附魔等边界仍待专项验证。\n'
write('资料/设定/角色设计.md', main)
write('资料/历史资料/工作区说明旧稿.md', relink(original_readme,ROOT,ROOT/'资料/历史资料'))
for old in ['sts2_punch_construct_design.md','原版全遗物_改动审查稿_v0.1.md']:
    new = moves[old]
    path = ROOT/new
    text = path.read_text(encoding='utf-8-sig')
    write(new, '> 历史记录：不作为当前设定，当前规则见[角色设计](../设定/角色设计.md)。\n\n' + relink(text,ROOT,path.parent))
write(f'{ART}/制作记录/早期绘图记录.md', '# 早期绘图与审稿记录\n\n以下内容保留原始描述和旧文件名，仅用于追溯，不表示当前采用状态。\n\n'+'\n\n'.join(early_notes))
prompt_list = []
for old, item in sorted(prompt_records.items()):
    item['original_name'] = old
    item['file'] = moves.get(f'{OLD}/状态图标草稿_v0.1/{old}', old)
    item['game_linked'] = False
    prompt_list.append(item)
write(f'{ART}/制作记录/状态图标提示词.json',json.dumps(prompt_list,ensure_ascii=False,indent=2)+'\n')
write(f'{ART}/状态图标/图标清单.json',json.dumps(icons,ensure_ascii=False,indent=2)+'\n')

# Read paths from the manifest so renaming draft files never integrates them automatically.
write('tools/preview_icons.py', '''"""Generate review-only previews from the status-icon manifest."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
root = Path(__file__).resolve().parent.parent
folder = root / '资料/美术素材/状态图标'
icons = json.loads((folder / '图标清单.json').read_text(encoding='utf-8'))
canvas = Image.new('RGB', (1080, 930), '#181c25')
draw = ImageDraw.Draw(canvas)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 14)
draw.text((22, 10), '状态图标｜左：64像素实际尺寸；右：3倍放大｜已选与待审均未接入游戏', font=small, fill='#e3e6ee')
for i, item in enumerate(icons):
    icon = Image.open(root/item['size64']).convert('RGBA')
    assert icon.size == (64,64)
    x,y=(i%3)*360,52+(i//3)*290
    draw.rounded_rectangle((x+10,y,x+350,y+280),8,fill='#242936')
    for xx,yy,size in ((x+30,y+70,64),(x+140,y+20,192)):
        for cy in range(0,size,8):
            for cx in range(0,size,8):
                color='#2d3340' if (cx//8+cy//8)%2 else '#343a47'
                draw.rectangle((xx+cx,yy+cy,xx+cx+7,yy+cy+7),fill=color)
        shown=icon if size==64 else icon.resize((192,192),Image.Resampling.NEAREST)
        canvas.paste(shown,(xx,yy),shown)
    draw.text((x+24,y+231),item['name']+' · '+item['status'],font=font,fill='#f2f1ed')
canvas.save(folder/'图标总览.png')
print('Review-only icon overview updated; no game files changed.')
''')

# Remove redundant generated notes after preserving their useful content centrally.
for path in sorted((ROOT/OLD).rglob('*')):
    if path.is_file():
        remove(path.relative_to(ROOT), 'Superseded generated draft notes; source images and prompts retained in organized folders')
for path in sorted((ROOT/'tools').glob('*.txt')):
    remove(path.relative_to(ROOT), 'Generated API/IL inspection dump; reproducible with tools/Inspect')
for name in ('update_progress.py','update_v031.py'):
    remove('tools/'+name, 'Obsolete one-off progress rewriting script with outdated paths')
for path in sorted((ROOT/OLD).rglob('*'), key=lambda p:len(p.parts), reverse=True):
    if path.is_dir() and not any(path.iterdir()):
        checked(path).rmdir()
if (ROOT/OLD).exists() and not any((ROOT/OLD).iterdir()):
    checked(OLD).rmdir()
old_relics = ROOT/'资料/遗物审查_v0.1'
if old_relics.exists() and not any(old_relics.iterdir()):
    checked(old_relics).rmdir()

# Rebase the archived review generator to its new location/output.
review = ROOT/'资料/历史资料/遗物参考数据/build_relic_review.py'
text = review.read_text(encoding='utf-8')
text = text.replace('ROOT = SNAPSHOT_DIR.parents[1]', 'ROOT = SNAPSHOT_DIR.parent')
text = text.replace('原版全遗物_改动审查稿_v0.1.md', '已撤回的遗物改动提案.md')
review.write_text(text,encoding='utf-8')

for path, previous in before.items():
    assert path.is_file() and digest(path) == previous, f'Protected game/release/save file changed: {path}'
write('资料/历史资料/整理记录.json',json.dumps({'moves_and_deletions':records,'protected_files_unchanged':len(before),'game_status_icons_linked':False},ensure_ascii=False,indent=2)+'\n')
print(f'Organized {len(moves)} files; removed {sum(r["action"]=="delete" for r in records)} redundant generated files; protected {len(before)} game/release/save files unchanged.')
