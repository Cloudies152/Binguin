# -*- coding: utf-8 -*-
"""图标边缘清理：去掉黑描边上的杂色 / 灰边 / JPEG 噪点。

问题来源
--------
1. AI 出的原图是 **JPEG** → 黑边周围带 8×8 块状色噪（chroma subsampling）。
2. 白底抠图 + LANCZOS 缩放 → 边缘产生半透明「黑白混合」像素，看上去是一圈灰边。
3. 游戏内 textureCompression(DXT) 会把这个灰边进一步放大成彩色杂边。

处理流程
--------
1. 硬化 alpha：>= 阈值 → 255，否则 0（扁平矢量图标不需要软边）。
2. 边缘颜色净化：把边界 1~3 px 的颜色从「内部实心区」外扩填充，
   消除白底混色。
3. 深色描边归并：边缘带里接近黑的像素统一压到 #02040A，去掉 JPEG 色噪。

用法
----
    python clean_icon_edges.py <文件或目录> [...]

目录会递归处理其中所有 .png（可用 --dry 只打印不写入）。
"""
import os
import sys

import cv2
import numpy as np
from PIL import Image

ALPHA_THRESHOLD = 110   # >= 阈值视为不透明
EDGE_BAND = 4           # 边缘净化带宽（像素）
DARK_SNAP_LUM = 62      # 亮度低于此值视为描边
DARK_SNAP_CHROMA = 26   # 且彩度低于此值
OUTLINE_RGB = (2, 4, 10)


def binary_alpha(a):
    alpha = a[..., 3]
    return np.where(alpha >= ALPHA_THRESHOLD, 255, 0).astype(np.uint8)


def clean(path, dry=False):
    im = Image.open(path).convert("RGBA")
    a = np.array(im)
    solid = binary_alpha(a) > 0

    if not solid.any():
        return False

    # ---- 0) 去掉孤立小斑点（JPEG 振铃/噪点被硬化后会变成彩色小点） ----
    n, lab, stats, _ = cv2.connectedComponentsWithStats(solid.astype(np.uint8), 8)
    keep = np.zeros_like(solid)
    for i in range(1, n):
        if stats[i, cv2.CC_STAT_AREA] >= 30:
            keep |= (lab == i)
    if keep.any():
        solid = keep

    # ---- 1.5) 中值去噪：压掉 JPEG 色噪（红绿点） ----
    rgb0 = a[..., :3].copy()
    denoised = cv2.medianBlur(rgb0, 3)
    rgb = np.where(solid[..., None], denoised, rgb0).astype(np.float32)
    core = solid.copy()
    # 先腐蚀掉 1px，得到「真正的内部」
    er = np.ones((3, 3), np.uint8)
    core = cv2.erode(core.astype(np.uint8), er, iterations=1) > 0

    for _ in range(EDGE_BAND):
        sums = cv2.boxFilter(rgb * core[..., None], -1, (3, 3), normalize=False)
        cnts = cv2.boxFilter(core.astype(np.float32), -1, (3, 3), normalize=False)
        band = solid & (~core) & (cnts > 0)
        if not band.any():
            break
        rgb[band] = sums[band] / cnts[band][:, None]
        core = core | band

    out = a.copy()
    out[..., :3] = np.clip(rgb, 0, 255).astype(np.uint8)

    # ---- 3) 深色描边归并（只动边缘带） ----
    band_mask = solid & (~cv2.erode(solid.astype(np.uint8), er, iterations=3).astype(bool))
    px = out[..., :3].astype(np.int16)
    lum = (0.299 * px[..., 0] + 0.587 * px[..., 1] + 0.114 * px[..., 2])
    chroma = px.max(axis=2) - px.min(axis=2)
    snap = band_mask & (lum < DARK_SNAP_LUM) & (chroma < DARK_SNAP_CHROMA)
    out[snap, :3] = OUTLINE_RGB

    # ---- 1) 硬化 alpha ----
    out[..., 3] = np.where(solid, 255, 0).astype(np.uint8)

    if dry:
        print("[dry]", path)
        return True

    Image.fromarray(out, "RGBA").save(path, optimize=True)
    return True


def collect(targets):
    files = []
    for t in targets:
        if os.path.isdir(t):
            for root, _, names in os.walk(t):
                for n in names:
                    if n.lower().endswith(".png"):
                        files.append(os.path.join(root, n))
        elif t.lower().endswith(".png"):
            files.append(t)
    return files


def main():
    args = [x for x in sys.argv[1:] if not x.startswith("--")]
    dry = "--dry" in sys.argv
    if not args:
        print(__doc__)
        sys.exit(1)

    files = collect(args)
    if not files:
        print("没找到 PNG")
        return

    done = 0
    for f in files:
        try:
            if clean(f, dry):
                done += 1
                print("清理", os.path.basename(f))
        except Exception as e:
            print("失败", f, e)
    print("完成 %d / %d" % (done, len(files)))


if __name__ == "__main__":
    main()
