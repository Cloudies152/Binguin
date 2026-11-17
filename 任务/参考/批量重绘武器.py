# -*- coding: utf-8 -*-
"""批量让豆包按统一画风重绘 6 把旧武器。

对每把武器：
  1. 上传 [画风基准 LaserGatling, 旧贴图放大图]
  2. 发送提示词
  3. 等待出现新图（哈希不在发送前的集合里）
  4. 下载到 Tools/ai_in/redraw_<名字>.png
"""
import hashlib
import os
import time

from playwright.sync_api import sync_playwright

STYLE = r"W:\dsh\zengbing\Binguin_开发版\Textures\Things\Item\Equipment\WeaponRanged\Binguin\LaserGatling.png"
REF_DIR = r"W:\dsh\zengbing\Tools\redraw_ref"
OUT = r"W:\dsh\zengbing\Tools\ai_in"

STYLE_DESC = (
    "画风必须完全照图1（冰鹅族激光机枪）：扁平矢量、纯色平涂、粗黑描边（外轮廓粗、内部细）、"
    "白色顶面板 + 中灰蓝壳体 + 结构蓝 + 冰蓝小点缀、右上硬边高光、右下硬阴影，无渐变、无模糊、无透视"
)

WEAPONS = [
    ("Shotgun", "双管霰弹枪（DP-12 风格：两管上下并排、粗壮机匣、方块枪托、前握把）"),
    ("SMG", "小型冲锋枪（短枪身、折叠托、顶部小红点镜、直弹匣）"),
    ("M870", "泵动霰弹枪（单管 + 下方管状弹仓、木质感枪托换成白/灰配色、泵动护木）"),
    ("AssaultRifle", "突击步枪（长机匣、提把式瞄具、弯曲弹匣、护木带散热孔、枪口制退器）"),
    ("LeverRifle", "杠杆步枪（细长枪管 + 下方管仓、杠杆护圈、木质枪托换成白/灰配色）"),
    ("PrecisionRifle", "精确射手步枪（长枪管 + 顶部大瞄准镜 + 两脚架、可调枪托、枪口制退器）"),
]


def big_images(pg):
    return pg.evaluate("""() => [...document.querySelectorAll('img')]
        .filter(i => i.src.startsWith('http') && i.getBoundingClientRect().width > 300)
        .map(i => i.src)""")


def hashes(ctx, urls):
    out = {}
    for u in urls:
        try:
            r = ctx.request.get(u, headers={"Referer": "https://www.doubao.com/"})
            b = r.body()
            if len(b) > 150000:
                out[hashlib.md5(b).hexdigest()] = (u, b)
        except Exception:
            pass
    return out


def main():
    with sync_playwright() as p:
        b = p.chromium.connect_over_cdp("http://127.0.0.1:9222")
        ctx = b.contexts[0]
        pg = [x for x in ctx.pages if "doubao.com/chat" in x.url][0]
        for name, desc in WEAPONS:
            ref = os.path.join(REF_DIR, name + ".png")
            before = set(hashes(ctx, big_images(pg)).keys())
            try:
                inputs = pg.query_selector_all("input[type=file]")
                inputs[0].set_input_files([STYLE, ref])
                time.sleep(5)
                pg.mouse.click(700, 650)
                time.sleep(1)
                prompt = (
                    "直接出图。图1 = 画风基准；图2 = 要重绘的旧武器（**只参考结构和造型，不要参考它的画风**）。\n"
                    "请把图2 重绘成冰鹅族风格的「%s」。\n"
                    "要求：\n"
                    "1）结构照图2：外形轮廓、比例、主要部件位置保持一致；\n"
                    "2）%s；\n"
                    "3）枪口朝右、正侧视、纯白背景、无文字、长宽比约 2:1。"
                ) % (desc, STYLE_DESC)
                pg.keyboard.insert_text(prompt)
                time.sleep(1.5)
                pg.keyboard.press("Enter")
                print(name, "已发送，等待生成…", flush=True)

                found = None
                for _ in range(30):
                    time.sleep(5)
                    cur = hashes(ctx, big_images(pg))
                    new = [(h, v) for h, v in cur.items() if h not in before]
                    # 取最大的一张（新生成图通常 2MB 级别）
                    new.sort(key=lambda kv: -len(kv[1][1]))
                    if new:
                        found = new[0]
                        break
                if not found:
                    print(name, "未取到新图，跳过", flush=True)
                    continue
                dst = os.path.join(OUT, "redraw_%s.png" % name)
                with open(dst, "wb") as f:
                    f.write(found[1][1])
                print(name, "已保存", dst, len(found[1][1]), flush=True)
            except Exception as e:
                print(name, "失败:", str(e)[:80], flush=True)
        print("ALL DONE", flush=True)


if __name__ == "__main__":
    main()
