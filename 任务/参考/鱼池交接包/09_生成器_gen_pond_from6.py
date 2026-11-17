# -*- coding: utf-8 -*-
"""鱼池贴图：6 张母版 → 16 张运行时变体（靠旋转生成，不改 C#）。

为什么要 6 张
-------------
鱼池的 16 张 `FishPond_l0..l15` 是"四边连接组合"。按 90° 旋转等价合并后，
只有 6 种**形状**（其余都是它们的旋转）：

    pond_0      四面都有岸边（孤立）
    pond_1      只有一侧连通（开口 1 个，默认朝上 N）
    pond_2adj   相邻两侧连通（转角，默认 N+E）
    pond_2opp   相对两侧连通（直通，默认 N+S）
    pond_3      三侧连通（只有一侧有岸，默认 N+E+S）
    pond_4      四面全连通（纯水面）

本脚本用这 6 张，旋转生成全部 16 张运行时文件。

用法
----
    python gen_pond_from6.py <母版目录> <输出目录>

母版目录里需要：pond_0.png pond_1.png pond_2adj.png pond_2opp.png pond_3.png pond_4.png
（128×128，方形，旋转后仍能对齐）

mask 定义（与 C# 一致）
----------------------
    bit1 = 上(N)   bit2 = 右(E)   bit4 = 下(S)   bit8 = 左(W)
"""
import os
import sys

from PIL import Image

# mask -> (母版名, 顺时针旋转角度)
MAP = {
    0:  ("pond_0", 0),
    1:  ("pond_1", 0),      # 开口 N
    2:  ("pond_1", 90),     # 开口 E
    4:  ("pond_1", 180),    # 开口 S
    8:  ("pond_1", 270),    # 开口 W
    3:  ("pond_2adj", 0),   # N+E
    6:  ("pond_2adj", 90),  # E+S
    12: ("pond_2adj", 180), # S+W
    9:  ("pond_2adj", 270), # W+N
    5:  ("pond_2opp", 0),   # N+S
    10: ("pond_2opp", 90),  # E+W
    7:  ("pond_3", 0),      # N+E+S
    14: ("pond_3", 90),     # E+S+W
    13: ("pond_3", 180),    # S+W+N
    11: ("pond_3", 270),    # W+N+E
    15: ("pond_4", 0),
}


def load(dirpath, name, size):
    p = os.path.join(dirpath, name + ".png")
    im = Image.open(p).convert("RGBA")
    if im.size != (size, size):
        im = im.resize((size, size), Image.LANCZOS)
    return im


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    src_dir, out_dir = sys.argv[1], sys.argv[2]
    size = int(sys.argv[3]) if len(sys.argv) > 3 else 128
    os.makedirs(out_dir, exist_ok=True)

    cache = {}
    for mask in range(16):
        name, deg = MAP[mask]
        if name not in cache:
            cache[name] = load(src_dir, name, size)
        im = cache[name]
        # PIL 的 rotate 是逆时针，这里用负数实现顺时针
        out = im.rotate(-deg, expand=False, resample=Image.NEAREST) if deg else im
        dst = os.path.join(out_dir, "FishPond_l%d.png" % mask)
        out.save(dst, optimize=True)
    print("已从 6 张母版生成 16 张 ->", out_dir)


if __name__ == "__main__":
    main()
