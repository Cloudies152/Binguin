# -*- coding: utf-8 -*-
"""鱼池连接贴图生成器：一张母图 → 16 种连接变体。

原理
----
鱼池那 16 张 `FishPond_l0..l15` 不是生长阶段，而是**四边连接组合**：
每一位代表一侧是否有相邻鱼池，连上的那侧不画岸边石（让水面连起来）。

    bit1 = 顶（贴图上边）
    bit2 = 右
    bit4 = 底
    bit8 = 左

所以美术只需要画 **一张**「四面都有岸边石」的母图，其余 15 张由本脚本生成。

用法
----
    python gen_pond_variants.py <母图.png> <输出目录>

母图要求
--------
- 128×128（或 512×512，脚本会自动缩放）
- 四边都有岸边石 / 池壁
- 水面在中间

输出
----
`<输出目录>/FishPond_l0.png` … `FishPond_l15.png`
"""
import os
import sys

import numpy as np
from PIL import Image


def load_master(path, size=128):
    im = Image.open(path).convert("RGBA")
    if im.size != (size, size):
        im = im.resize((size, size), Image.LANCZOS)
    return np.array(im)


def water_color(a):
    """取中心区域中位数作为水面颜色。"""
    h, w = a.shape[:2]
    cx, cy = w // 2, h // 2
    patch = a[cy - 8:cy + 8, cx - 8:cx + 8, :3].reshape(-1, 3)
    return np.median(patch, axis=0).astype(np.uint8)


def make_variant(base, mask, band, water, corner=True):
    a = base.copy()
    h, w = a.shape[:2]
    top = bool(mask & 1)
    right = bool(mask & 2)
    bottom = bool(mask & 4)
    left = bool(mask & 8)
    if top:
        a[0:band, :, :3] = water
    if bottom:
        a[h - band:h, :, :3] = water
    if right:
        a[:, w - band:w, :3] = water
    if left:
        a[:, 0:band, :3] = water
    # 两邻边都连通时，把角落的石头也清掉
    if corner:
        corners = [
            (top and left, 0, 0),
            (top and right, w - band, 0),
            (bottom and left, 0, h - band),
            (bottom and right, w - band, h - band),
        ]
        for cond, x, y in corners:
            if cond:
                a[y:y + band, x:x + band, :3] = water
    return a


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    master_path, out_dir = sys.argv[1], sys.argv[2]
    size = int(sys.argv[3]) if len(sys.argv) > 3 else 128
    base = load_master(master_path, size)
    water = water_color(base)
    band = int(size * 0.11)
    os.makedirs(out_dir, exist_ok=True)
    for mask in range(16):
        a = make_variant(base, mask, band, water)
        dst = os.path.join(out_dir, "FishPond_l%d.png" % mask)
        Image.fromarray(a, "RGBA").save(dst, optimize=True)
    print("已生成 16 张 ->", out_dir)
    print("水面色 #%02X%02X%02X，岸边带宽 %d px" % (water[0], water[1], water[2], band))


if __name__ == "__main__":
    main()
