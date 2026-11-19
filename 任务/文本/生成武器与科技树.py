# -*- coding: utf-8 -*-
"""生成 任务/文本/武器与科技树.md（武器数值 + 科技树 + 解锁关系）"""
import glob
import os
import xml.etree.ElementTree as ET

ROOT = r"W:\dsh\zengbing\Binguin_开发版"
GAME = r"G:\SteamLibrary\steamapps\common\RimWorld\Data"
OUT = os.path.join(ROOT, "任务", "文本", "武器与科技树.md")


def collect(paths):
    d = {}
    for f in paths:
        try:
            tree = ET.parse(f)
        except Exception:
            continue
        for node in list(tree.getroot()):
            dn = node.find("defName")
            if dn is None or not dn.text:
                continue
            d[dn.text.strip()] = node
    return d


def t(n, tag, default=None):
    if n is None:
        return default
    e = n.find(tag)
    return e.text.strip() if (e is not None and e.text) else default


def main():
    our = collect(glob.glob(os.path.join(ROOT, "Defs", "**", "*.xml"), recursive=True))
    van = collect(glob.glob(os.path.join(GAME, "**", "*.xml"), recursive=True))
    all_defs = dict(van)
    all_defs.update(our)

    weapons = []
    for dn, node in our.items():
        if not dn.startswith("Binguin_"):
            continue
        verbs, tools = node.find("verbs"), node.find("tools")
        if verbs is None and tools is None:
            continue
        if dn.startswith("Binguin_Bullet") or dn.startswith("Binguin_Gun_"):
            continue

        sb = node.find("statBases")
        g = lambda tag: t(sb, tag, "-") or "-"
        w = dict(defn=dn, label=t(node, "label", dn), melee=(verbs is None),
                 mass=g("Mass"), work=g("WorkToMake"), cd=g("RangedWeapon_Cooldown"),
                 acc=[g("AccuracyTouch"), g("AccuracyShort"), g("AccuracyMedium"), g("AccuracyLong")])

        if verbs is not None:
            v = verbs.find("li")
            proj = t(v, "defaultProjectile")
            w.update(range=t(v, "range", "-"), warmup=t(v, "warmupTime", "-"),
                     burst=t(v, "burstShotCount", "1"), ticks=t(v, "ticksBetweenBurstShots", "-"))
            pj = all_defs.get(proj).find("projectile") if proj in all_defs else None
            w.update(dmg=t(pj, "damageAmountBase", "-"), ap=t(pj, "armorPenetrationBase", "-"))
            try:
                d = float(w["dmg"])
                n = min(int(w["burst"]), 20)
                tick = float(w["ticks"]) if w["ticks"] not in ("-", "") else 0.0
                dur = float(w["warmup"]) + float(w["cd"]) + max(0, n - 1) * tick / 60.0
                w["dps"] = "%.1f" % (d * n / dur) if dur > 0 and d else "-"
            except Exception:
                w["dps"] = "-"
        else:
            w.update(range="-", warmup="-", burst="-", ticks="-",
                     dmg=t(tools.find("li"), "power", "-"), ap="-", dps="-")

        cost = node.find("costList")
        w["cost"] = ", ".join("%s×%s" % (c.tag, c.text) for c in cost) if cost is not None else "—"
        r = node.find("researchPrerequisites")
        w["research"] = ", ".join(li.text for li in r) if r is not None else "—"
        weapons.append(w)

    weapons.sort(key=lambda x: (x["melee"], x["label"]))

    res = {}
    for dn, node in our.items():
        pre = node.find("prerequisites")
        if pre is None and node.find("baseCost") is None:
            continue
        res[dn] = dict(label=t(node, "label", dn), cost=t(node, "baseCost", "-"),
                       pre=[li.text for li in pre] if pre is not None else [])

    L = ["# 冰鹅族 武器数值与科技树", "", "## 一、武器数值", "",
         "| 名称 | defName | 伤害 | 穿甲 | 射程 | 瞄准 | 冷却 | 连发 | 精度 贴/短/中/远 | 质量 | 工作 | 估算DPS | 成本 | 研究 |",
         "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for w in weapons:
        burst = "—" if w["melee"] else "%s×%s" % (w["burst"], w["ticks"])
        L.append("| %s | `%s` | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (
            w["label"], w["defn"], w["dmg"], w["ap"], w["range"], w["warmup"], w["cd"], burst,
            "/".join(w["acc"]), w["mass"], w["work"], w["dps"], w["cost"], w["research"]))

    L += ["", "## 二、科技树", "", "| 研究 | defName | 花费 | 前置 |", "| --- | --- | --- | --- |"]
    for dn in sorted(res, key=lambda k: (len(res[k]["pre"]), res[k]["label"])):
        r = res[dn]
        L.append("| %s | `%s` | %s | %s |" % (r["label"], dn, r["cost"], " / ".join(r["pre"]) or "—"))

    L += ["", "## 三、解锁关系（研究 → 武器）", ""]
    by = {}
    for w in weapons:
        for r in [x.strip() for x in w["research"].split(",") if x.strip() and x.strip() != "—"]:
            by.setdefault(r, []).append(w["label"])
    for r in sorted(by):
        L.append("- **%s**（`%s`）：%s" % (res.get(r, {}).get("label", r), r, "、".join(by[r])))

    open(OUT, "w", encoding="utf-8").write("\n".join(L) + "\n")
    print("生成:", OUT)


if __name__ == "__main__":
    main()
