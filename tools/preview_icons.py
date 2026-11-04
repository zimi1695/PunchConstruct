"""Generate the overview of all adopted status icons."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
root = Path(__file__).resolve().parent.parent
folder = root / '资料/美术素材/状态图标'
icons = json.loads((folder / '图标清单.json').read_text(encoding='utf-8'))
canvas = Image.new('RGB', (1080, 60 + ((len(icons) + 2) // 3) * 290), '#181c25')
draw = ImageDraw.Draw(canvas)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 14)
draw.text((22, 10), f'状态图标｜左：64像素实际尺寸；右：3倍放大｜{len(icons)}张均已采用', font=small, fill='#e3e6ee')
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
canvas.save(folder.parent/'图标总览.png')
print('Adopted icon overview updated.')
