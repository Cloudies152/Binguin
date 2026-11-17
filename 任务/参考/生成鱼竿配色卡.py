# -*- coding: utf-8 -*-
"""鱼竿专用配色卡：只有鱼竿上实际出现的 8 个颜色。"""
from PIL import Image, ImageDraw, ImageFont

W, H = 1120, 470
BG = (250, 251, 252)

COLORS = [
    ("#040507", "描边黑 / 最暗", 18.3),
    ("#1E212A", "深灰 阴影", 14.4),
    ("#323844", "深灰 主体暗部", 16.4),
    ("#4E5364", "灰蓝 主体", 20.3),
    ("#667588", "灰蓝 亮面", 9.0),
    ("#959FA7", "浅灰", 4.3),
    ("#C7C9D5", "冷白 高光面", 8.2),
    ("#F4F6F8", "近白 最高光", 9.1),
]


def font(sz, bold=False):
    for f in ((r"C:\Windows\Fonts\msyhbd.ttc" if bold else r"C:\Windows\Fonts\msyh.ttc"),
              r"C:\Windows\Fonts\simhei.ttf"):
        try:
            return ImageFont.truetype(f, sz)
        except Exception:
            continue
    return ImageFont.load_default()


def hex2rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def lum(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def main():
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    d.text((40, 24), "冰鹅族 鱼竿 配色（只用这 8 色）", fill=(26, 30, 36), font=font(30, True))
    d.text((40, 64), "从三张鱼竿贴图统计的实际用色，按明度从暗到亮排列；百分比为该色占比", fill=(110, 116, 126), font=font(17))

    n = len(COLORS)
    sw = (W - 80 - (n - 1) * 10) // n
    sh = 150
    y = 110
    for i, (hexv, use, share) in enumerate(COLORS):
        x = 40 + i * (sw + 10)
        rgb = hex2rgb(hexv)
        d.rectangle([x, y, x + sw, y + sh], fill=rgb, outline=(215, 219, 224))
        tc = (255, 255, 255) if lum(rgb) < 130 else (24, 28, 34)
        d.text((x + 8, y + 10), hexv, fill=tc, font=font(17, True))
        d.text((x + 8, y + sh + 8), use, fill=(90, 96, 106), font=font(13))
        d.text((x + 8, y + sh + 28), "%.0f%%" % share, fill=(150, 156, 166), font=font(13))

    d.text((40, H - 96), "用法：轮廓与最暗处 #040507；主体灰蓝 #4E5364 / #323844；亮面 #667588；",
           fill=(90, 96, 106), font=font(16))
    d.text((40, H - 72), "白色高光 #C7C9D5 / #F4F6F8；阴影 #1E212A。鱼竿是低科技工具，**不要蓝色发光**。",
           fill=(90, 96, 106), font=font(16))
    out = r"W:\dsh\zengbing\任务素材\鱼竿交接包\09_鱼竿配色.png"
    img.save(out)
    print("saved", out)


if __name__ == "__main__":
    main()
