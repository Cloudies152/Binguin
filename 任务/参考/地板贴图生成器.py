# -*- coding: utf-8 -*-
"""冰鹅族地板贴图生成器（1024×1024，无缝平铺，图案细密）。

三张地板：
  IceFloor        冰面地板：淡蓝底 + 细霜纹 + 细裂纹
  SnowflakeFloor  雪花地板：蓝底 + 大量小雪花（重复网格）
  FancyIceFloor   精致冰地板：深蓝底 + 菱形格 + 雪花团花

要点（参考其他 mod 的地形贴图）：
- 1024×1024（2 的幂，引擎可生成 mipmap）
- 图案在一个格子里重复多次，游戏里 1 格 = 1 张图，缩到 64px 时看起来是"细纹理"而不是"一个大花纹"
- 所有元素按 9 宫格偏移绘制，保证四边无缝
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 1024          # 输出尺寸
SS = 2            # 超采样倍数（内部按 2048 画，最后缩小，边缘更细腻）
OUT_DIR = r"W:\dsh\zengbing\Binguin_开发版\Textures\Things\Floor\Binguin"

random.seed(20261117)


# ---------------------------------------------------------------- 工具
def canvas(bg):
    im = Image.new("RGBA", (S * SS, S * SS), bg + (255,))
    return im, ImageDraw.Draw(im)


def wrap(draw_fn, x, y):
    """在 9 个偏移位置各画一次，保证无缝。"""
    for dx in (-S * SS, 0, S * SS):
        for dy in (-S * SS, 0, S * SS):
            draw_fn(x + dx, y + dy)


def add_noise(im, amp=6):
    a = np.array(im).astype(np.int16)
    noise = np.random.normal(0, amp, (a.shape[0], a.shape[1], 1)).astype(np.int16)
    a[..., :3] = np.clip(a[..., :3] + noise, 0, 255)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def finish(im, name):
    out = im.resize((S, S), Image.LANCZOS)
    path = os.path.join(OUT_DIR, name + ".png")
    out.save(path, optimize=True)
    print("saved", path)
    return out


# ---------------------------------------------------------------- 1) 冰面地板
def make_ice():
    base = (168, 207, 228)          # #A8CFE4 淡蓝
    im, d = canvas(base)

    # 细霜纹：大量极细的短斜线
    for _ in range(900):
        x = random.uniform(0, S * SS)
        y = random.uniform(0, S * SS)
        ln = random.uniform(6, 22) * SS / 2
        ang = random.uniform(-0.9, -0.5)
        x2, y2 = x + ln * math.cos(ang), y + ln * math.sin(ang)
        c = (232, 244, 250, random.randint(40, 90))

        def fn(x1, y1, x2=x2, y2=y2, c=c):
            d.line([(x1, y1), (x2, y2)], fill=c, width=max(1, SS))
        wrap(fn, x, y)

    # 细裂纹：几条长折线
    for _ in range(7):
        pts = [(random.uniform(0, S * SS), random.uniform(0, S * SS))]
        ang = random.uniform(0, math.tau)
        for _ in range(random.randint(6, 12)):
            ang += random.uniform(-0.5, 0.5)
            ln = random.uniform(30, 70) * SS
            pts.append((pts[-1][0] + ln * math.cos(ang), pts[-1][1] + ln * math.sin(ang)))
        # 画整条（跨边界时靠重复保证连续，简单处理：直接画 + 3x3 偏移）
        for dx in (-S * SS, 0, S * SS):
            for dy in (-S * SS, 0, S * SS):
                d.line([(p[0] + dx, p[1] + dy) for p in pts],
                       fill=(150, 190, 214, 170), width=max(1, SS), joint="curve")

    # 白色小霜点
    for _ in range(500):
        x, y = random.uniform(0, S * SS), random.uniform(0, S * SS)
        r = random.uniform(1, 3) * SS
        c = (245, 250, 253, random.randint(50, 110))

        def fn(x1, y1, r=r, c=c):
            d.ellipse([x1 - r, y1 - r, x1 + r, y1 + r], fill=c)
        wrap(fn, x, y)

    im = add_noise(im, 4)
    return finish(im, "IceFloor")


# ---------------------------------------------------------------- 2) 雪花地板
def draw_snowflake(d, cx, cy, r, color, width):
    for i in range(6):
        a = i * math.pi / 3
        x2, y2 = cx + r * math.cos(a), cy + r * math.sin(a)
        d.line([(cx, cy), (x2, y2)], fill=color, width=width)
        # 两侧分叉
        for s in (-1, 1):
            for t in (0.45, 0.72):
                bx, by = cx + r * t * math.cos(a), cy + r * t * math.sin(a)
                ln = r * 0.30 * (1 - t * 0.5)
                ba = a + s * math.pi / 4
                d.line([(bx, by), (bx + ln * math.cos(ba), by + ln * math.sin(ba))],
                       fill=color, width=max(1, width - 1))


def make_snowflake():
    base = (92, 134, 168)           # #5C86A8
    im, d = canvas(base)
    step = S * SS / 4                # 4×4 网格
    r = step * 0.26
    col = (238, 246, 252, 235)
    for gy in range(4):
        for gx in range(4):
            cx = gx * step + step / 2 + random.uniform(-6, 6) * SS
            cy = gy * step + step / 2 + random.uniform(-6, 6) * SS
            draw_snowflake(d, cx, cy, r, col, max(2, int(2.2 * SS)))
            draw_snowflake(d, cx, cy, r * 0.45, (255, 255, 255, 120), max(1, int(1.2 * SS)))
    # 细颗粒
    for _ in range(400):
        x, y = random.uniform(0, S * SS), random.uniform(0, S * SS)
        rr = random.uniform(1, 2.5) * SS
        c = (210, 228, 242, random.randint(30, 70))

        def fn(x1, y1, rr=rr, c=c):
            d.ellipse([x1 - rr, y1 - rr, x1 + rr, y1 + rr], fill=c)
        wrap(fn, x, y)
    im = add_noise(im, 4)
    return finish(im, "SnowflakeFloor")


# ---------------------------------------------------------------- 3) 精致冰地板
def make_fancy():
    base = (58, 90, 120)            # #3A5A78
    im, d = canvas(base)
    step = S * SS / 2                # 2×2 菱形格
    line_c = (34, 56, 78, 255)
    hi_c = (120, 160, 190, 200)

    # 菱形格（45° 斜线，跨边界靠 3x3 偏移）
    for k in range(-4, 8):
        off = k * step
        for dx in (-S * SS, 0, S * SS):
            for dy in (-S * SS, 0, S * SS):
                d.line([(off + dx, dy - S * SS), (off + S * SS * 2 + dx, dy + S * SS * 3)],
                       fill=line_c, width=max(2, int(2.4 * SS)))
                d.line([(off + dx, dy + S * SS * 3), (off + S * SS * 2 + dx, dy - S * SS)],
                       fill=line_c, width=max(2, int(2.4 * SS)))

    # 菱形中心雪花团花
    for gy in range(2):
        for gx in range(2):
            cx = gx * step + step / 2
            cy = gy * step + step / 2
            draw_snowflake(d, cx, cy, step * 0.22, (200, 224, 240, 220), max(2, int(2.0 * SS)))
            d.ellipse([cx - 6 * SS, cy - 6 * SS, cx + 6 * SS, cy + 6 * SS],
                      fill=(226, 242, 250, 210), outline=(30, 50, 70, 255), width=max(1, int(1.4 * SS)))

    # 少量细线纹理
    for _ in range(220):
        x, y = random.uniform(0, S * SS), random.uniform(0, S * SS)
        ln = random.uniform(8, 20) * SS
        ang = random.uniform(0, math.pi)
        c = (150, 180, 205, random.randint(25, 55))

        def fn(x1, y1, ln=ln, ang=ang, c=c):
            d.line([(x1, y1), (x1 + ln * math.cos(ang), y1 + ln * math.sin(ang))],
                   fill=c, width=max(1, SS))
        wrap(fn, x, y)

    im = add_noise(im, 4)
    return finish(im, "FancyIceFloor")


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    make_ice()
    make_snowflake()
    make_fancy()
