# -*- coding: utf-8 -*-
"""把竖构图鱼竿图转正 → 压成 128x64 游戏图标。

流程：去白底 → 裁切 → PCA 求竿身主轴角度 → 旋转到水平 → 再裁切 → 等比缩放进 128x64。
不量化（小图标量化会发糊）。
"""
import math
import os
import sys
import numpy as np
import cv2
from PIL import Image
from collections import deque


def cut_white(path):
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
        # 去掉右下角"豆包AI生成"水印
        if x > w * 0.82 and y > h * 0.90 and area < 30000:
            continue
        keep[labels == i] = 255
    out = np.dstack([a[..., :3], keep])
    ys, xs = np.where(keep > 0)
    return Image.fromarray(out[ys.min():ys.max() + 1, xs.min():xs.max() + 1], "RGBA")


def axis_angle(img):
    """用 PCA 求主轴角度（度），返回使主轴水平的旋转角。"""
    m = np.array(img)[..., 3] > 0
    ys, xs = np.where(m)
    pts = np.column_stack([xs, ys]).astype(np.float32)
    pts -= pts.mean(axis=0)
    cov = np.cov(pts.T)
    evals, evecs = np.linalg.eigh(cov)
    v = evecs[:, np.argmax(evals)]
    ang = math.degrees(math.atan2(v[1], v[0]))
    return ang


def to_icon(src, dst, W=128, H=64, pad=3, rotate=True):
    crop = cut_white(src)
    ang = axis_angle(crop)
    # 归一化到 (-90, 90]
    while ang > 90:
        ang -= 180
    while ang <= -90:
        ang += 180
    best = crop
    best_w = 0
    if rotate:
        for sign in (1, -1):
            try:
                cand = crop.rotate(sign * ang, resample=Image.BICUBIC, expand=True)
                cand = cut_white_from_rgba(cand)
                if cand.size[0] > best_w:
                    best, best_w = cand, cand.size[0]
            except Exception:
                continue
    rot = best
    cw, ch = rot.size
    sc = min((W - pad) / cw, (H - pad) / ch)
    im = rot.resize((max(1, int(cw * sc)), max(1, int(ch * sc))), Image.LANCZOS)
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    canvas.alpha_composite(im, ((W - im.width) // 2, (H - im.height) // 2))
    canvas.save(dst)
    canvas.resize((W * 4, H * 4), Image.LANCZOS).save(dst.replace(".png", "_4x.png"))
    return canvas, ang


def cut_white_from_rgba(im):
    a = np.array(im)
    m = a[..., 3] > 8
    ys, xs = np.where(m)
    return Image.fromarray(a[ys.min():ys.max() + 1, xs.min():xs.max() + 1], "RGBA")


if __name__ == "__main__":
    base = r"W:\dsh\zengbing\Tools\ref_rods"
    out = r"W:\dsh\zengbing\Tools"
    for name in ("rod_plain", "rod_hammer", "rod_blade"):
        src = os.path.join(base, name + ".jpg")
        dst = os.path.join(out, "final_" + name + ".png")
        c, ang = to_icon(src, dst)
        print(name, "主轴角 %.1f°" % ang, "->", dst)
