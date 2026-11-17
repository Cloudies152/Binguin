# -*- coding: utf-8 -*-
"""生成冰鹅族「标准色板」图表：蓝 / 灰 / 白 / 黄，带 hex + RGB。

色值来源：
- 蓝、灰、白：从现有武器贴图统计（kmeans 主色，见 palette_raw.json）
- 黄：企鹅喙的暖色取样（#F8A524 等），补成一条完整色阶
"""
from PIL import Image, ImageDraw, ImageFont


def font(sz):
    for f in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyhbd.ttc", r"C:\Windows\Fonts\simhei.ttf"):
        try:
            return ImageFont.truetype(f, sz)
        except Exception:
            continue
    return ImageFont.load_default()


W, H = 1080, 880
BG = (246, 247, 249)
CARD = (255, 255, 255)
INK = (32, 36, 42)
SUB = (108, 114, 124)

FAMILIES = [
    ("蓝色 Blue", [
        ("#8ADBF0", "冰蓝高光 / 能量"),
        ("#6695B8", "结构蓝 亮"),
        ("#5C86A8", "结构蓝 中"),
        ("#3E6989", "结构蓝 暗"),
        ("#2E4A63", "结构蓝 最深"),
    ]),
    ("灰色 Gray", [
        ("#A9B0BA", "浅灰 高光"),
        ("#868B97", "浅灰"),
        ("#656D79", "灰蓝 主色"),
        ("#525A65", "灰蓝 暗"),
        ("#343C46", "深灰"),
        ("#1D2126", "近黑"),
        ("#030404", "描边黑"),
    ]),
    ("白色 White", [
        ("#FFFFFF", "纯白 高光"),
        ("#FBFCFC", "面板白"),
        ("#D3E5EB", "冷白 阴影面"),
    ]),
    ("黄色 Yellow", [
        ("#FFD87A", "浅黄 高光"),
        ("#FFC24D", "冰鹅黄 主色"),
        ("#F8A524", "喙 / 暖黄"),
        ("#D9821A", "深黄 阴影"),
    ]),
]


def hex2rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def lum(c):
    r, g, b = c
    return 0.299 * r + 0.587 * g + 0.114 * b


def main():
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    # 标题
    d.text((40, 22), "冰鹅族 标准色板", fill=INK, font=font(26))
    d.text((40, 58), "蓝色 / 灰色 / 白色 / 黄色    上排为 HEX 色值，下排为用途", fill=SUB, font=font(15))

    y = 92
    for name, colors in FAMILIES:
        d.text((40, y), name, fill=INK, font=font(18))
        y += 26
        n = len(colors)
        sw = (W - 80 - (n - 1) * 12) // n
        sh = 96
        for i, (hexv, use) in enumerate(colors):
            x = 40 + i * (sw + 12)
            rgb = hex2rgb(hexv)
            d.rectangle([x, y, x + sw, y + sh], fill=rgb, outline=(210, 214, 220))
            tc = (255, 255, 255) if lum(rgb) < 130 else (25, 28, 33)
            d.text((x + 8, y + 10), hexv, fill=tc, font=font(16))
            d.text((x + 8, y + sh + 7), use, fill=SUB, font=font(13))
        y += sh + 44

    # 底部提示
    d.text((40, H - 66), "用法：主体用灰蓝 #656D79 / #525A65；面板用白 #FBFCFC；", fill=SUB, font=font(14))
    d.text((40, H - 42), "结构件用 #6695B8 / #3E6989；能量用 #8ADBF0；描边统一 #030404。", fill=SUB, font=font(14))

    out = r"W:\dsh\zengbing\Tools\palette_standard.png"
    img.save(out)
    print("saved", out, img.size)


if __name__ == "__main__":
    main()
