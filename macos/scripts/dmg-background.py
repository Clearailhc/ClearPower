#!/usr/bin/env python3
"""Render the DMG window background: 'drag ClearPower to Applications' with an arrow."""
import sys
from PIL import Image, ImageDraw, ImageFont

W, H, S = 660, 400, 2  # points, retina scale
img = Image.new("RGB", (W * S, H * S), (246, 246, 248))
d = ImageDraw.Draw(img)
def font(size):
    for p in ("/System/Library/Fonts/Supplemental/Hiragino Sans GB.ttc", "/System/Library/Fonts/Hiragino Sans GB.ttc", "/System/Library/Fonts/STHeiti Light.ttc", "/System/Library/Fonts/Helvetica.ttc"):
        try:
            return ImageFont.truetype(p, size * S)
        except OSError:
            continue
    return ImageFont.load_default()
# arrow between the two icon slots (icons sit at y≈190; centres x=165 and x=495)
y = 190 * S
x0, x1 = 250 * S, 410 * S
d.line([(x0, y), (x1, y)], fill=(150, 150, 155), width=6 * S)
d.polygon([(x1 + 14 * S, y), (x1 - 8 * S, y - 14 * S), (x1 - 8 * S, y + 14 * S)], fill=(150, 150, 155))
title = "Drag ClearPower to Applications"
sub = "把 ClearPower 拖到 Applications 文件夹 · 首次打开请在「系统设置 > 隐私与安全性」中点「仍要打开」"
f1, f2 = font(20), font(12)
tw = d.textlength(title, font=f1)
d.text(((W * S - tw) / 2, 296 * S), title, fill=(60, 60, 65), font=f1)
tw = d.textlength(sub, font=f2)
d.text(((W * S - tw) / 2, 330 * S), sub, fill=(120, 120, 125), font=f2)
img.save(sys.argv[1], dpi=(72 * S, 72 * S))
