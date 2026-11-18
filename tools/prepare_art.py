"""Convert adopted art to game textures; never modify the user's source images."""
import hashlib
import io
import json
import re
import struct
from pathlib import Path
from PIL import Image, ImageChops, ImageOps

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
    'clockwork_souvenir': .90, 'clockwork_souvenir_big': .90,
}
POWER_FIT = .9375


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


def edge_color(image):
    """Median colour of the outermost opaque pixels, used to flatten P-mode art."""
    rgba = image.convert('RGBA')
    width, height = rgba.size
    pixels = rgba.load()
    samples = []
    for x in range(width):
        for y in (0, height - 1):
            if pixels[x, y][3] > 8:
                samples.append(pixels[x, y][:3])
    for y in range(height):
        for x in (0, width - 1):
            if pixels[x, y][3] > 8:
                samples.append(pixels[x, y][:3])
    if not samples:
        return (0, 0, 0)
    channels = list(zip(*samples))
    return tuple(sorted(channel)[len(channel) // 2] for channel in channels)


def flatten(image):
    """Replace transparent corners with the local edge colour, never black."""
    rgba = image.convert('RGBA')
    canvas = Image.new('RGBA', rgba.size, edge_color(rgba) + (255,))
    canvas.alpha_composite(rgba)
    return canvas.convert('RGB')


def crop_cover(image, size, anchor):
    """Centre-crop to the target aspect at a normalised anchor, then resize."""
    target = size[0] / size[1]
    width, height = image.size
    if width / height > target:
        crop_width, crop_height = round(height * target), height
    else:
        crop_width, crop_height = width, round(width / target)
    left = round((width - crop_width) * anchor[0])
    top = round((height - crop_height) * anchor[1])
    return image.crop((left, top, left + crop_width, top + crop_height)).resize(size, Image.Resampling.LANCZOS)


def card_image(path, crop=None, flatten_source=False, size=(1000, 760)):
    original = Image.open(path)
    image = original.convert('RGBA')
    if flatten_source:
        image = flatten(image)
    else:
        image = image.convert('RGB')
    if crop is not None:
        return crop_cover(image, size, crop), f'centre crop at ({crop[0]:.2f}, {crop[1]:.2f}), Lanczos resize'
    if abs(image.width / image.height / (size[0] / size[1]) - 1) < .01:
        return image.resize(size, Image.Resampling.LANCZOS), 'resize without crop'
    # The Tony meme is almost square. Preserve it with black side margins.
    return ImageOps.pad(image, size, Image.Resampling.LANCZOS, color=(0, 0, 0)), \
        'contain without crop, black side padding'


def padded_icon(path, size, fit, nearest):
    image = Image.open(path).convert('RGBA')
    alpha = image.getchannel('A')
    image.putalpha(alpha.point(lambda a: a if a > 8 else 0))
    image = image.crop(image.getchannel('A').getbbox())
    extent = round(size[0] * fit)
    image = ImageOps.contain(image, (extent, extent),
                             Image.Resampling.NEAREST if nearest else Image.Resampling.LANCZOS)
    canvas = Image.new('RGBA', size)
    canvas.paste(image, ((size[0] - image.width) // 2, (size[1] - image.height) // 2))
    return canvas


def hsv_tint(image, mode):
    """Apply the potion colour treatment while keeping the bottle highlights."""
    rgba = image.convert('RGBA')
    alpha = rgba.getchannel('A')
    hue, saturation, value = rgba.convert('HSV').split()
    if mode == 'gold':
        # Block potion liquid is blue; turn only the saturated liquid gold.
        hue_mask = hue.point(lambda x: 255 if 120 <= x <= 175 else 0)
        saturation_mask = saturation.point(lambda x: 255 if x > 80 else 0)
        alpha_mask = alpha.point(lambda x: 255 if x > 50 else 0)
        mask = ImageChops.multiply(ImageChops.multiply(hue_mask, saturation_mask), alpha_mask)
        hue = Image.composite(Image.new('L', rgba.size, 27), hue, mask)
    elif mode == 'gray_liquid':
        hue_mask = hue.point(lambda x: 255 if 120 <= x <= 175 else 0)
        saturation_mask = saturation.point(lambda x: 255 if x > 80 else 0)
        alpha_mask = alpha.point(lambda x: 255 if x > 50 else 0)
        mask = ImageChops.multiply(ImageChops.multiply(hue_mask, saturation_mask), alpha_mask)
        saturation = Image.composite(Image.new('L', rgba.size, 18), saturation, mask)
        value = Image.composite(value.point(lambda x: int(x * .70)), value, mask)
    elif mode == 'gray_all':
        saturation = Image.new('L', rgba.size, 0)
    else:
        raise ValueError(mode)
    result = Image.merge('HSV', (hue, saturation, value)).convert('RGBA')
    result.putalpha(alpha)
    return result


def hue_rotate(image):
    """Native large potion art is hue-rotated against the icon palette."""
    rgba = image.convert('RGBA')
    alpha = rgba.getchannel('A')
    hue, saturation, value = rgba.convert('HSV').split()
    hue = hue.point(lambda x: (x + 128) % 256)
    result = Image.merge('HSV', (hue, saturation, value)).convert('RGBA')
    result.putalpha(alpha)
    return result


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
        ('卡牌/卸甲成军.png', 'unarm_to_arms', (1000, 760), False, False),
        ('卡牌/厄运共鸣.png', 'doom_resonance', (1000, 760), False, False),
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
            record['card_title'] = path.stem
            record['card_number'] = card_numbers[path.stem]
        if padded:
            bounds = image.getchannel('A').point(lambda a: 255 if a > 16 else 0).getbbox()
            record['visible_bounds'] = list(bounds)
            record['visible_size'] = [bounds[2] - bounds[0], bounds[3] - bounds[1]]
        records.append(record)
        if name in ('guard_stance', 'ancient_guard_stance', 'spinning_screw', 'open_end_wrench',
                    'spare_knuckles', 'emergency_oil_can', 'clockwork_souvenir'):
            outline = Image.new('RGBA', image.size, (255, 255, 255, 0))
            outline.putalpha(image.getchannel('A'))
            export(outline, name + '_outline')
            records.append({**record, 'output': f'PunchConstruct/assets/art/{name}_outline.ctex',
                            'conversion': 'white silhouette using the matching icon alpha'})
    for source, name, crop, flatten_source, title, size in [
        ('卡牌/发条机关.jpg', 'clockwork_mechanism', (.5, .5), False, '发条机关', (1000, 760)),
        ('卡牌/机械飞升.png', 'mechanical_ascension', None, False, '机械飞升', (1000, 760)),
        ('卡牌/燃尽.png', 'burn_out', (.5, .5), False, '燃尽', (1000, 760)),
        # Ancient cards use the tall 606x852 portrait frame (see BoxingChampionForm);
        # a landscape texture would be stretched by the ancient frame.
        ('卡牌/霸无敌.png', 'domineering', (.5, .5), False, '霸无敌', (606, 852)),
        ('一代素材/核心电涌_CoreSurge_1024.png', 'core_surge', None, True, '核心电涌', (1000, 760)),
        ('一代素材/万能药_Panacea_1024.png', 'panacea', None, True, '万能药', (1000, 760)),
    ]:
        path = SOURCE / source
        image, operation = card_image(path, crop, flatten_source, size)
        if flatten_source:
            operation = 'transparent corners flattened to sampled edge colour, ' + operation
        export(image, name)
        records.append(dict(source=path.relative_to(ROOT).as_posix(),
                            source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                            source_size=list(Image.open(path).size),
                            output=f'PunchConstruct/assets/art/{name}.ctex', size=list(size),
                            conversion=operation, card_title=title, card_number=card_numbers[title]))
    path = SOURCE / '一代素材/齿轮工艺品_ClockworkSouvenir_large.png'
    original = Image.open(path)
    for name, size in [('clockwork_souvenir', (128, 128)), ('clockwork_souvenir_big', (256, 256))]:
        image = padded_icon(path, size, RELIC_FIT[name], False)
        export(image, name)
        bounds = image.getchannel('A').point(lambda a: 255 if a > 16 else 0).getbbox()
        records.append(dict(source=path.relative_to(ROOT).as_posix(),
                            source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                            source_size=list(original.size), output=f'PunchConstruct/assets/art/{name}.ctex',
                            size=list(size),
                            conversion=f'visible alpha>8 crop, per-asset silhouette fit {RELIC_FIT[name]}, transparent padding',
                            visible_bounds=list(bounds), visible_size=[bounds[2] - bounds[0], bounds[3] - bounds[1]]))
        if name == 'clockwork_souvenir':
            outline = Image.new('RGBA', image.size, (255, 255, 255, 0))
            outline.putalpha(image.getchannel('A'))
            export(outline, name + '_outline')
            records.append({**records[-1], 'output': f'PunchConstruct/assets/art/{name}_outline.ctex',
                            'conversion': 'white silhouette using the matching icon alpha'})
    path = ROOT / '资料/历史/旧稿/复位弹簧.png'
    original = Image.open(path)
    for suffix, size in [('', (64, 64)), ('_big', (256, 256))]:
        image = padded_icon(path, size, POWER_FIT, True)
        export(image, 'power_clockwork_mechanism' + suffix)
        records.append(dict(source=path.relative_to(ROOT).as_posix(),
                            source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                            source_size=list(original.size),
                            output=f'PunchConstruct/assets/art/power_clockwork_mechanism{suffix}.ctex',
                            size=list(size),
                            conversion=f'visible alpha>8 crop, silhouette fit {POWER_FIT}, nearest resize',
                            card_title='发条机关', card_number=card_numbers['发条机关']))
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
                record['card_title'] = title
                record['card_number'] = card_numbers[title]
            records.append(record)
    ancient_sheet = SOURCE / '药水/先古药水.png'
    for title, name, icon_path, large_path, tint, rotate in [
        ('先古药水', 'ancient', ancient_sheet, ancient_sheet, None, False),
        ('浑浊机油', 'murky_oil', SOURCE / '药水/痊愈药水_图标.png', SOURCE / '药水/痊愈药水_大图.png', 'gray_liquid', False),
        ('拳手炖汤', 'fighters_stew', SOURCE / '药水/士兵炖汤_图标.png', SOURCE / '药水/士兵炖汤_大图.png', 'gray_all', True),
    ]:
        icon_original = Image.open(icon_path).convert('RGBA')
        # The ancient sheet is a single 256x256 image; the icon is a smooth downscale.
        icon = ImageOps.contain(icon_original, (70, 70), Image.Resampling.LANCZOS) if title == '先古药水' else icon_original
        if tint:
            icon = hsv_tint(icon, tint)
        export(icon, f'potion_{name}')
        records.append(dict(source=icon_path.relative_to(ROOT).as_posix(),
                            source_sha256=hashlib.sha256(icon_path.read_bytes()).hexdigest(),
                            source_size=list(icon_original.size), output=f'PunchConstruct/assets/art/potion_{name}.ctex',
                            size=list(icon.size),
                            conversion='256x256 sheet downscaled to 70x70, no tint' if not tint else f'native-size RGBA icon, tint {tint}',
                            potion=title, tint=tint))
        outline = Image.new('RGBA', icon.size, (255, 255, 255, 0))
        outline.putalpha(icon.getchannel('A'))
        export(outline, f'potion_{name}_outline')
        records.append({**records[-1], 'output': f'PunchConstruct/assets/art/potion_{name}_outline.ctex',
                        'conversion': 'white silhouette using the matching potion alpha'})
        large_original = Image.open(large_path).convert('RGBA')
        large = hsv_tint(hue_rotate(large_original) if rotate else large_original, tint) if tint else large_original
        export(large, f'potion_{name}_large')
        records.append(dict(source=large_path.relative_to(ROOT).as_posix(),
                            source_sha256=hashlib.sha256(large_path.read_bytes()).hexdigest(),
                            source_size=list(large_original.size), output=f'PunchConstruct/assets/art/potion_{name}_large.ctex',
                            size=list(large.size),
                            conversion=('180-degree hue correction, ' if rotate else '') + (f'tint {tint}' if tint else 'sheet as-is') + ', 256x256',
                            potion=title, tint=tint))
    record = SOURCE / '素材清单.json'
    record.write_text(json.dumps(records, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    used = {r['source'] for r in records}
    categories = ('角色', '卡牌', '遗物', '状态图标', '药水')
    actual = {p.relative_to(ROOT).as_posix() for category in categories
              for p in (SOURCE/category).iterdir() if p.suffix.lower() in ('.png', '.jpg', '.jpeg', '.webp')}
    used_adopted = {source for source in used if Path(source).parent.name in categories}
    if used_adopted != actual:
        raise ValueError(f'Adopted artwork mismatch: {actual ^ used_adopted}')
    print(f'Exported {len(records)} texture variants from {len(used)} sources '
          f'({len(used_adopted)} adopted in {", ".join(categories)}).')


if __name__ == '__main__':
    main()
