"""Convert adopted art to game textures; never modify the user's source images."""
import hashlib
import io
import json
import re
import struct
from pathlib import Path
from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / '资料/美术素材'
DEST = ROOT / 'PunchConstruct/assets/art'

# Judge each silhouette separately; alpha-1 noise is not visible artwork.
RELIC_FIT = {
    'guard_stance': .94, 'guard_stance_big': .94,
    'ancient_guard_stance': .88, 'ancient_guard_stance_big': .88,
    'spinning_screw': .90, 'spinning_screw_big': .90,
    'open_end_wrench': .90, 'open_end_wrench_big': .90,
    'spare_knuckles': .92, 'spare_knuckles_big': .92,
    'emergency_oil_can': .90, 'emergency_oil_can_big': .90,
}


def export(image, name):
    image.save(DEST / (name + '.png'))
    encoded = io.BytesIO()
    image.save(encoded, format='WEBP', lossless=True, exact=True)
    payload = encoded.getvalue()
    width, height = image.size
    # Godot 4 GST2, one lossless WebP level. No editor/import cache required.
    header = b'GST2' + struct.pack('<8I', 1, width, height, 0, 0xffffffff, 0, 0, 0)
    header += struct.pack('<IHHIII', 2, width, height, 0, 5 if image.mode == 'RGBA' else 4, len(payload))
    (DEST / (name + '.ctex')).write_bytes(header + payload)


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    records = []
    roster = (ROOT / '资料/卡牌清单.txt').read_text(encoding='utf-8')
    card_numbers = {m[2]: int(m[1]) for m in re.finditer(r'^(\d{2})\s+([^\s：]+)：', roster, re.M)}
    for source, name, size, nearest, padded in [
        ('角色/头像.png', 'portrait', (32, 32), True, False),
        ('角色/选人图标.png', 'character_select_icon', (132, 195), False, False),
        ('角色/选人海报.png', 'character_select', (1920, 1080), False, False),
        ('角色/选人背景.png', 'character_select_background', (1920, 1080), False, False),
        ('角色/选人角色.png', 'character_select_actor', (1920, 1080), False, False),
        ('卡牌/小斩波.png', 'small_wave', (1000, 760), False, False),
        ('卡牌/铁斩波形态.png', 'iron_wave_form', (1000, 760), False, False),
        ('卡牌/与我一斗.png', 'fight_me', (1000, 760), False, False),
        ('卡牌/愈战愈勇.png', 'battle_fervor', (1000, 760), False, False),
        ('卡牌/东尼斩波.jpg', 'tony_wave', (1000, 760), False, False),
        ('卡牌/铁斩波.png', 'iron_wave', (1000, 760), False, False),
        ('卡牌/灼热铁斩波.png', 'scorching_iron_wave', (1000, 760), False, False),
        ('卡牌/锻造车间.jpg', 'forge_workshop', (1000, 760), False, False),
        ('卡牌/王者风范.jpg', 'royal_bearing', (1000, 760), False, False),
        ('卡牌/趁热打铁.jpg', 'strike_while_hot', (1000, 760), False, False),
        ('卡牌/终局裁定.jpg', 'final_judgment', (1000, 760), False, False),
        ('卡牌/卸甲成军.jpg', 'unarm_to_arms', (1000, 760), False, False),
        ('卡牌/厄运共鸣.jpg', 'doom_resonance', (1000, 760), False, False),
        ('卡牌/不倒不休.jpg', 'never_back_down', (1000, 760), False, False),
        ('卡牌/拳王形态.jpg', 'boxing_champion_form', (606, 852), False, False),
        ('卡牌/披荆斩棘.png', 'bramble_field', (1000, 760), False, False),
        ('卡牌/左勾拳.png', 'left_hook', (1000, 760), False, False),
        ('卡牌/右勾拳.png', 'right_hook', (1000, 760), False, False),
        ('卡牌/格斗流程.png', 'combat_routine', (1000, 760), False, False),
        ('卡牌/快速拳.png', 'quick_punch', (1000, 760), False, False),
        ('卡牌/连环拳.png', 'combination_punch', (1000, 760), False, False),
        ('卡牌/格挡.png', 'boxing_guard', (1000, 760), False, False),
        ('卡牌/过载.png', 'furnace_overload', (1000, 760), False, False),
        ('遗物/架势.png', 'guard_stance', (128, 128), False, True),
        ('遗物/架势.png', 'guard_stance_big', (256, 256), False, True),
        ('遗物/先古架势.png', 'ancient_guard_stance', (128, 128), False, True),
        ('遗物/先古架势.png', 'ancient_guard_stance_big', (256, 256), False, True),
        ('遗物/自旋螺丝.png', 'spinning_screw', (128, 128), True, True),
        ('遗物/自旋螺丝.png', 'spinning_screw_big', (256, 256), True, True),
        ('遗物/开口扳手.png', 'open_end_wrench', (128, 128), True, True),
        ('遗物/开口扳手.png', 'open_end_wrench_big', (256, 256), True, True),
        ('遗物/备用指节.png', 'spare_knuckles', (128, 128), True, True),
        ('遗物/备用指节.png', 'spare_knuckles_big', (256, 256), True, True),
        ('遗物/应急油壶.png', 'emergency_oil_can', (128, 128), True, True),
        ('遗物/应急油壶.png', 'emergency_oil_can_big', (256, 256), True, True),
    ]:
        path = SOURCE / source
        original = Image.open(path)
        image = original.convert('RGBA' if 'A' in original.getbands() else 'RGB')
        operation = 'nearest resize' if nearest else 'resize without crop'
        if padded:
            operation = f'visible alpha>8 crop, per-asset silhouette fit {RELIC_FIT[name]}, transparent padding'
            alpha = image.getchannel('A')
            image.putalpha(alpha.point(lambda a: a if a > 8 else 0))
            image = image.crop(image.getchannel('A').getbbox())
            extent = round(size[0] * RELIC_FIT[name])
            image = ImageOps.contain(image, (extent, extent), Image.Resampling.NEAREST if nearest else Image.Resampling.LANCZOS)
            canvas = Image.new('RGBA', size)
            canvas.paste(image, ((size[0] - image.width) // 2, (size[1] - image.height) // 2))
            image = canvas
        else:
            if abs(image.width / image.height / (size[0] / size[1]) - 1) < .01:
                image = image.resize(size, Image.Resampling.NEAREST if nearest else Image.Resampling.LANCZOS)
            else:
                # The Tony meme is almost square. Preserve it with black side margins.
                image = ImageOps.pad(image, size, Image.Resampling.LANCZOS, color=(0, 0, 0))
                operation = 'contain without crop, black side padding'
        export(image, name)
        record = dict(source=path.relative_to(ROOT).as_posix(), source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                      source_size=list(original.size), output=f'PunchConstruct/assets/art/{name}.ctex', size=list(size), conversion=operation)
        if source.startswith('卡牌/'):
            record['card_number'] = card_numbers[path.stem]
        if padded:
            bounds = image.getchannel('A').point(lambda a: 255 if a > 16 else 0).getbbox()
            record['visible_bounds'] = list(bounds)
            record['visible_size'] = [bounds[2] - bounds[0], bounds[3] - bounds[1]]
        records.append(record)
        if name in ('guard_stance', 'ancient_guard_stance', 'spinning_screw', 'open_end_wrench', 'spare_knuckles', 'emergency_oil_can'):
            outline = Image.new('RGBA', image.size, (255, 255, 255, 0))
            outline.putalpha(image.getchannel('A'))
            export(outline, name + '_outline')
            records.append({**record, 'output': f'PunchConstruct/assets/art/{name}_outline.ctex',
                            'conversion': 'white silhouette using the matching icon alpha'})
    for title, name in {
        '东尼斩波': 'delayed_wave', '铁斩波形态': 'iron_wave_form',
        '锻造车间': 'forge_workshop', '王者风范': 'royal_bearing',
        '趁热打铁': 'strike_while_hot', '终局裁定': 'final_judgment',
        '卸甲成军': 'unarm_to_arms', '不倒不休': 'never_back_down',
        '拳王形态': 'boxing_champion_form',
        '黄铜螺帽': 'brass_nut',
    }.items():
        path = SOURCE / '状态图标' / (title + '.png')
        original = Image.open(path).convert('RGBA')
        if original.size != (64, 64):
            raise ValueError(f'Status icon must be 64x64: {path}')
        for suffix, size in [('', (64, 64)), ('_big', (256, 256))]:
            export(original.resize(size, Image.Resampling.NEAREST), 'power_' + name + suffix)
            record = dict(source=path.relative_to(ROOT).as_posix(), source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                                source_size=[64, 64], output=f'PunchConstruct/assets/art/power_{name}{suffix}.ctex', size=list(size),
                                conversion='nearest resize')
            if title in card_numbers:
                record['card_number'] = card_numbers[title]
            records.append(record)
    record = SOURCE / '素材清单.json'
    record.write_text(json.dumps(records, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    used = {r['source'] for r in records}
    actual = {p.relative_to(ROOT).as_posix() for category in ('角色', '卡牌', '遗物', '状态图标')
              for p in (SOURCE/category).iterdir() if p.suffix.lower() in ('.png', '.jpg', '.jpeg', '.webp')}
    if used != actual:
        raise ValueError(f'Adopted artwork mismatch: {actual ^ used}')
    print(f'Exported {len(records)} texture variants from {len(used)} adopted sources.')


if __name__ == '__main__':
    main()
