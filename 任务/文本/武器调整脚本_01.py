# -*- coding: utf-8 -*-
"""按用户要求调整：轻型步枪 / 杠杆步枪 / 泵动霰弹枪 / 霰弹枪"""
import re
import os

ROOT = r"W:\dsh\zengbing\Binguin_开发版"
LIGHT = os.path.join(ROOT, r"Defs\Features\IceCombat\Weapons_LightRifle.xml")
BASIC = os.path.join(ROOT, r"Defs\Content\Weapons\Weapons_Basic.xml")
M870 = os.path.join(ROOT, r"Defs\Content\Weapons\Weapons_M870.xml")

MACHINING = "Binguin_ResearchMachining"
ICY = "Binguin_ResearchIcyPeaks"
FROST = "Binguin_ResearchFrostAssault"


def read(p):
    return open(p, encoding="utf-8").read()


def write(p, s):
    with open(p, "w", encoding="utf-8", newline="") as fh:
        fh.write(s)


def block(text, def_name, tag):
    """返回 (start, end) 覆盖该 def 的整个元素。"""
    i = text.find("<defName>%s</defName>" % def_name)
    if i < 0:
        raise RuntimeError("找不到 def: " + def_name)
    start = text.rfind("<" + tag, 0, i)
    end = text.find("</" + tag + ">", i)
    if start < 0 or end < 0:
        raise RuntimeError("找不到 def 边界: " + def_name)
    return start, end + len("</" + tag + ">")


def edit_block(text, def_name, tag, fn):
    s, e = block(text, def_name, tag)
    return text[:s] + fn(text[s:e]) + text[e:]


def sub1(seg, old, new):
    if old not in seg:
        raise RuntimeError("片段未找到: " + old[:70].replace("\n", "\\n"))
    return seg.replace(old, new, 1)


# ============================================================ 1) 轻型步枪
t = read(LIGHT)

t = sub1(t, "<RangedWeapon_Cooldown>1.35</RangedWeapon_Cooldown>",
         "<RangedWeapon_Cooldown>0.8</RangedWeapon_Cooldown>")
t = sub1(t, "<defaultProjectile>Binguin_Bullet_Assault</defaultProjectile>",
         "<defaultProjectile>Binguin_Bullet_LightRifle</defaultProjectile>")
t = sub1(t, "<burstShotCount>3</burstShotCount>", "<burstShotCount>4</burstShotCount>")


def light_gun(seg):
    seg = sub1(seg, "      <ComponentSpacer>1</ComponentSpacer>\n", "")
    seg = sub1(seg, "<li>Binguin_ResearchFrostAssault</li>",
               "<li>%s</li>" % MACHINING)
    return seg


t = edit_block(t, "Binguin_LightRifle", "ThingDef", light_gun)


def light_recipe(seg):
    seg = sub1(seg, "<researchPrerequisite>Binguin_ResearchFrostAssault</researchPrerequisite>",
               "<researchPrerequisite>%s</researchPrerequisite>" % MACHINING)
    spacer = ("      <li>\n        <filter>\n          <thingDefs>\n            <li>ComponentSpacer</li>\n"
              "          </thingDefs>\n        </filter>\n        <count>1</count>\n      </li>\n")
    seg = sub1(seg, spacer, "")
    return seg


t = edit_block(t, "Recipe_Binguin_LightRifle", "RecipeDef", light_recipe)

bullet = """
  <!-- 轻型步枪专用弹道：伤害 8、穿甲 0.14，沿用冰爆伤害类型 -->
  <ThingDef ParentName="BaseBullet">
    <defName>Binguin_Bullet_LightRifle</defName>
    <label>轻型步枪子弹</label>
    <graphicData>
      <texPath>Things/Projectile/Bullet_Small</texPath>
      <graphicClass>Graphic_Single</graphicClass>
    </graphicData>
    <projectile>
      <damageDef>Binguin_IceBulletDamage</damageDef>
      <damageAmountBase>8</damageAmountBase>
      <armorPenetrationBase>0.14</armorPenetrationBase>
      <stoppingPower>0.5</stoppingPower>
      <speed>70</speed>
    </projectile>
  </ThingDef>
"""
t = sub1(t, "\n</Defs>", bullet + "\n</Defs>")
write(LIGHT, t)
print("[1] 轻型步枪：伤害8/穿甲0.14/瞄准0.8/冷却0.8/4连射/无高级件/机加工")

# ============================================================ 2) 杠杆步枪 -> 增冰鹅鹅
t = read(BASIC)


def lever_gun(seg):
    return sub1(seg, "<li>Binguin_ResearchMachining</li>",
                "<li>%s</li>" % ICY)


t = edit_block(t, "Binguin_LeverRifle", "ThingDef", lever_gun)


def lever_recipe(seg):
    return sub1(seg, "<researchPrerequisite>Binguin_ResearchMachining</researchPrerequisite>",
                "<researchPrerequisite>%s</researchPrerequisite>" % ICY)


t = edit_block(t, "Recipe_Binguin_LeverRifle", "RecipeDef", lever_recipe)
print("[2] 杠杆步枪：研究 -> 增冰鹅鹅")

# ============================================================ 3) 霰弹枪
SPACER = ("      <li>\n        <filter>\n          <thingDefs>\n            <li>ComponentSpacer</li>\n"
          "          </thingDefs>\n        </filter>\n        <count>1</count>\n      </li>\n")


def shotgun_gun(seg):
    seg = sub1(seg, "<li>Binguin_ResearchMachining</li>",
               "<li>%s</li>" % FROST)
    seg = sub1(seg,
        "      <Steel>120</Steel>\n      <ComponentIndustrial>1</ComponentIndustrial>\n    </costList>",
        "      <Steel>120</Steel>\n      <ComponentIndustrial>1</ComponentIndustrial>\n      <ComponentSpacer>1</ComponentSpacer>\n    </costList>")
    return seg


t = edit_block(t, "Binguin_Shotgun", "ThingDef", shotgun_gun)


def shotgun_recipe(seg):
    seg = sub1(seg, "<researchPrerequisite>Binguin_ResearchMachining</researchPrerequisite>",
               "<researchPrerequisite>%s</researchPrerequisite>" % FROST)
    anchor = ("      <li>\n        <filter>\n          <thingDefs>\n            <li>ComponentIndustrial</li>\n"
              "          </thingDefs>\n        </filter>\n        <count>1</count>\n      </li>\n")
    seg = sub1(seg, anchor, anchor + SPACER)
    return seg


t = edit_block(t, "Recipe_Binguin_Shotgun", "RecipeDef", shotgun_recipe)

# 弹道：Bullet -> 冰爆伤害
t = sub1(t,
    "      <damageAmountBase>24</damageAmountBase>\n      <stoppingPower>3</stoppingPower>\n      <armorPenetrationBase>0.16</armorPenetrationBase>",
    "      <damageAmountBase>24</damageAmountBase>\n      <stoppingPower>3</stoppingPower>\n      <armorPenetrationBase>0.16</armorPenetrationBase>")
i = t.find("<defName>Binguin_Bullet_Shotgun</defName>")
s = t.rfind("<ThingDef", 0, i)
e = t.find("</ThingDef>", i)
seg = t[s:e]
seg = sub1(seg, "<damageDef>Bullet</damageDef>", "<damageDef>Binguin_IceBulletDamage</damageDef>")
t = t[:s] + seg + t[e:]

write(BASIC, t)
print("[3] 霰弹枪：研究 -> 突袭冷冻武器，+1 高级零部件，弹道改冰爆")

# ============================================================ 4) 泵动霰弹枪 -> 增冰鹅鹅
t = read(M870)
n = t.count("Binguin_ResearchMachining")
if n != 2:
    raise RuntimeError("泵动霰弹枪 研究前置数量异常: %d" % n)
t = t.replace("Binguin_ResearchMachining", ICY)
write(M870, t)
print("[4] 泵动霰弹枪：研究 -> 增冰鹅鹅（%d 处）" % n)

print("全部完成")
