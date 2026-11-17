# -*- coding: utf-8 -*-
"""把武器贴图从原始高清图重新导出为 512x256（2 的幂），交给引擎 mipmap 降采样。

与 process_sprite.py 的区别：
- 不做 kmeans 量化（量化会产生色斑）
- 输出边长必须是 2 的幂（512x256），这样 Unity 才会生成 mipmap
"""
import os
import sys
import numpy as np
from collections import deque
import cv2
from PIL import Image

sys.path.insert(0, r"W:\dsh\zengbing\Tools")


def cut_white(path, drop_watermark=True):
    im = Image.open(path).convert("RGBA")
    a = np.array(im)
    rgb = a[..., :3].astype(np.int16)
    h, w = rgb.shape[:2]
    tol = 26
    near_white = (rgb.min(axis=2) > 255 - tol * 2) & ((rgb.max(axis=2) - rgb.min(axis=2)) < tol)

    seen = np.zeros((h, w), bool)
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            if near_white[y, x] and not seen[y, x]:
                seen[y, x] = True
                q.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if near_white[y, x] and not seen[y, x]:
                seen[y, x] = True
                q.append((y, x))
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and not seen[ny, nx] and near_white[ny, nx]:
                seen[ny, nx] = True
                q.append((ny, nx))

    alpha = np.where(seen, 0, 255).astype(np.uint8)
    mask = (alpha > 0).astype(np.uint8)
    n, labels, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
    keep = np.zeros_like(mask)
    for i in range(1, n):
        x, y, ww, hh, area = stats[i]
        if drop_watermark and x > w * 0.82 and y > h * 0.90 and area < 30000:
            continue
        keep[labels == i] = 255
    out = np.dstack([a[..., :3], keep])
    ys, xs = np.where(keep > 0)
    return Image.fromarray(out[ys.min():ys.max() + 1, xs.min():xs.max() + 1], "RGBA")


def export(src, dst, W=512, H=256, pad=4):
    crop = cut_white(src)
    cw, ch = crop.size
    sc = min((W - pad) / cw, (H - pad) / ch)
    im = crop.resize((max(1, int(round(cw * sc))), max(1, int(round(ch * sc)))), Image.LANCZOS)
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    canvas.alpha_composite(im, ((W - im.width) // 2, (H - im.height) // 2))
    canvas.save(dst, optimize=True)
    print(os.path.basename(dst), canvas.size, "content", crop.size)


if __name__ == "__main__":
    T = r"W:\dsh\zengbing\Tools"
    B = r"W:\dsh\zengbing\Binguin_开发版\Textures\Things\Item\Equipment\WeaponRanged\Binguin"
    jobs = [
        (os.path.join(T, "ai_in", "fg_8_9891a568.png"), os.path.join(B, "LaserGatling.png")),
        (os.path.join(T, "ai_in", "fix_2_f09cf10a.png"), os.path.join(B, "PenguinKick.png")),
    ]
    for src, dst in jobs:
        if os.path.exists(src):
            export(src, dst)
        else:
            print("缺少源图", src)
