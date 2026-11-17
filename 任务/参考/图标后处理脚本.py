# -*- coding: utf-8 -*-
"""豆包出图 -> RimWorld 图标：去白底、去水印、裁切、缩放、量化去糊。

用法：
    python process_sprite.py <输入图> <输出png> [宽] [高] [--keep-all]

--keep-all  保留所有非白内容（钓线这类细长孤立元素需要），默认只保留最大连通域
"""
import sys, os
import numpy as np
from collections import deque
import cv2
from PIL import Image


def load_and_cut(path, keep_all=False):
    im = Image.open(path).convert("RGBA")
    a = np.array(im)
    rgb = a[..., :3].astype(np.int16)
    h, w = rgb.shape[:2]
    tol = 24
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
    if keep_all:
        for i in range(1, n):
            x, y, ww, hh, area = stats[i]
            if x > w * 0.85 and y > h * 0.85 and area < 20000:   # 右下角水印
                continue
            keep[labels == i] = 255
    else:
        best = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
        keep[labels == best] = 255

    out = np.dstack([a[..., :3], keep])
    ys, xs = np.where(keep > 0)
    return Image.fromarray(out[ys.min():ys.max() + 1, xs.min():xs.max() + 1], "RGBA")


def fit(crop, W, H, pad=4, palette=10):
    cw, ch = crop.size
    sc = min((W - pad) / cw, (H - pad) / ch)
    crop = crop.resize((max(1, int(cw * sc)), max(1, int(ch * sc))), Image.LANCZOS)
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    canvas.alpha_composite(crop, ((W - crop.width) // 2, (H - crop.height) // 2))

    arr = np.array(canvas)
    m = arr[..., 3] > 128
    if m.any():
        px = arr[..., :3][m].reshape(-1, 3).astype(np.float32)
        crit = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 25, 1.0)
        k = min(palette, len(np.unique(px, axis=0)))
        _, lab, centers = cv2.kmeans(px, k, None, crit, 4, cv2.KMEANS_PP_CENTERS)
        q = arr.copy()
        q[..., :3][m] = centers[lab.ravel()].astype(np.uint8)
        canvas = Image.fromarray(q, "RGBA")
    return canvas


def main():
    args = [x for x in sys.argv[1:] if not x.startswith("--")]
    keep_all = "--keep-all" in sys.argv
    src, dst = args[0], args[1]
    W = int(args[2]) if len(args) > 2 else 128
    H = int(args[3]) if len(args) > 3 else 64
    crop = load_and_cut(src, keep_all=keep_all)
    out = fit(crop, W, H)
    out.save(dst)
    preview = out.resize((W * 4, H * 4), Image.LANCZOS)
    preview.save(os.path.splitext(dst)[0] + "_4x.png")
    print("saved", dst, out.size, "content", crop.size)


if __name__ == "__main__":
    main()
