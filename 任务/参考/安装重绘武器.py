# -*- coding: utf-8 -*-
"""把 redraw_*.png 处理成 512x256 并安装到武器贴图目录。

- 去白底、去右下角水印
- 裁切 → 等比缩放 → 512x256（2 的幂）
- 不做量化
"""
import os
import sys
import numpy as np
from collections import deque
import cv2
from PIL import Image

sys.path.insert(0, r"W:\dsh\zengbing\Tools")

SRC_DIR = r"W:\dsh\zengbing\Tools\ai_in"
DST_DIR = r"W:\dsh\zengbing\Binguin_开发版\Textures\Things\Item\Equipment\WeaponRanged\Binguin"
W, H = 512, 256

NAMES = ["Shotgun", "SMG", "M870", "AssaultRifle", "LeverRifle", "PrecisionRifle"]


def cut_white(path):
    im = Image.open(path).convert("RGBA")
    a = np.array(im)
    rgb = a[..., :3].astype(np.int16)
    h, w = rgb.shape[:2]
    tol = 26
    nw = (rgb.min(axis=2) > 255 - tol * 2) & ((rgb.max(axis=2) - rgb.min(axis=2)) < tol)
    seen = np.zeros((h, w), bool)
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            if nw[y, x] and not seen[y, x]:
                seen[y, x] = True
                q.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if nw[y, x] and not seen[y, x]:
                seen[y, x] = True
                q.append((y, x))
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and not seen[ny, nx] and nw[ny, nx]:
                seen[ny, nx] = True
                q.append((ny, nx))
    alpha = np.where(seen, 0, 255).astype(np.uint8)
    mask = (alpha > 0).astype(np.uint8)
    n, labels, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
    keep = np.zeros_like(mask)
    for i in range(1, n):
        x, y, ww, hh, area = stats[i]
        if x > w * 0.82 and y > h * 0.90 and area < 30000:
            continue
        keep[labels == i] = 255
    out = np.dstack([a[..., :3], keep])
    ys, xs = np.where(keep > 0)
    return Image.fromarray(out[ys.min():ys.max() + 1, xs.min():xs.max() + 1], "RGBA")


def export(src, dst):
    crop = cut_white(src)
    cw, ch = crop.size
    pad = 6
    sc = min((W - pad) / cw, (H - pad) / ch)
    im = crop.resize((max(1, int(round(cw * sc))), max(1, int(round(ch * sc)))), Image.LANCZOS)
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    canvas.alpha_composite(im, ((W - im.width) // 2, (H - im.height) // 2))
    canvas.save(dst, optimize=True)
    return canvas, crop.size


if __name__ == "__main__":
    for name in NAMES:
        src = os.path.join(SRC_DIR, "redraw_%s.png" % name)
        if not os.path.exists(src):
            print(name, "缺少重绘图，跳过")
            continue
        dst = os.path.join(DST_DIR, name + ".png")
        canvas, content = export(src, dst)
        print(name, "->", dst, canvas.size, "content", content)
    print("DONE")
