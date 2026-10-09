// ============================================================================
// 高级钓竿技能 —— Ability 系统实现（Bladelink 式：近战武器 + 能力按钮）
//
// ★ 为什么不用武器 verb（2026-08-19 用户反馈"鱼竿变远程武器"）：给 ThingDef.verbs
//   挂 verb 会让武器被判定为远程武器！技能必须走 Ability 系统：
//   装备（CompBinguinRod.Notify_Equipped）→ pawn.abilities.GainAbility →
//   AbilityTracker.GetGizmos 显示能力按钮（近战武器完全不受影响）。
//   效果 = CompAbilityEffect 子类（静态构造挂到 AbilityDef.comps）。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin
{
    // 直钩技能：对前方 10 格内所有敌人造成 200% 于武器面板的伤害（Stab）
    public class CompAbilityEffect_BinguinPierce : CompAbilityEffect
    {
        // ★ 基类 Valid/CanApplyOn 内部对特定子类型做硬 cast（InvalidCastException，
        //   日志 Root level exception in OnGUI），自定义效果必须 override 返回 true
        //   （目标合法性由 verbProperties.targetParams 限制）
        public override bool Valid(LocalTargetInfo target, bool throwMessages)
        {
            return true;
        }

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn pawn = parent.pawn;
            if (pawn == null || pawn.Map == null)
            {
                return;
            }
            // ★★ 2026-10-09 用户需求：可以瞄准 pawn 了（不再只能点地板）。
            //   `LocalTargetInfo.Pawn` 是 `Thing as Pawn`（RimSage 查
            //   `Verse/LocalTargetInfo.cs` 实证），瞄准 pawn 时 `target.Cell`
            //   就是那个 pawn 所在的格，所以下面统一取 `.Cell` 即可。
            IntVec3 aimCell = target.Cell;
            // ★★ 方向算法**必须与瞄准预览共用同一份**
            //   （`BinguinPierceAim.DirectionTo`）—— 否则又会变成
            //   "看到一条线、实际打另一个方向"。用户报的正是这个问题的观感版本。
            IntVec3 dir = BinguinPierceAim.DirectionTo(pawn.Position, aimCell);
            if (dir == IntVec3.Zero)
            {
                return;
            }

            Map map = pawn.Map;
            float panelDamage = PanelDamage(pawn);
            // ★ 2026-09 性能：伤害乘数在两层循环外只取一次（原来每个命中目标
            //   都要重新查一次 StatDef + 算一次 stat）。
            float damageMult = pawn.GetStatValue(DamageMultStat);
            ThingDef weaponDef = pawn.equipment != null && pawn.equipment.Primary != null ? pawn.equipment.Primary.def : null;
            DamageDef damageDef = DefDatabase<DamageDef>.GetNamedSilentFail("Stab");
            if (damageDef == null)
            {
                damageDef = DamageDefOf.Cut;
            }

            // ★ 特效：施法者脚下尘土 + 直线路径冰屑拖尾
            FleckMaker.ThrowDustPuff(pawn.Position.ToVector3Shifted(), map, 1.2f);
            FleckDef iceFleck = DefDatabase<FleckDef>.GetNamedSilentFail("Binguin_IceDust");
            int hitCount = 0;
            for (int i = 1; i <= 10; i++)
            {
                IntVec3 cell = pawn.Position + dir * i;
                if (!cell.InBounds(map))
                {
                    break;
                }
                if (i % 2 == 0)
                {
                    if (iceFleck != null)
                    {
                        FleckMaker.Static(cell, map, iceFleck, 0.8f);
                    }
                    else
                    {
                        FleckMaker.ThrowDustPuff(cell.ToVector3Shifted(), map, 0.5f);
                    }
                }
                List<Thing> things = map.thingGrid.ThingsListAt(cell);
                for (int j = 0; j < things.Count; j++)
                {
                    Pawn enemy = things[j] as Pawn;
                    if (enemy == null || enemy == pawn || enemy.Dead)
                    {
                        continue;
                    }
                    // ★ 目标：所有敌人 + 中立生物（含动物），不打友军与盟友
                    if (enemy.Faction == pawn.Faction)
                    {
                        continue;
                    }
                    if (enemy.Faction != null && pawn.Faction != null
                        && enemy.Faction.RelationKindWith(pawn.Faction) == FactionRelationKind.Ally)
                    {
                        continue;
                    }
                    float armorPen = 0.4f * damageMult;
                    DamageInfo dinfo = new DamageInfo(damageDef, panelDamage * 2f, armorPen, 0f, pawn, null, weaponDef,
                        DamageInfo.SourceCategory.ThingOrUnknown, null, false, false, QualityCategory.Normal, false, false);
                    enemy.TakeDamage(dinfo);
                    // 命中尘土
                    FleckMaker.ThrowDustPuff(enemy.Position.ToVector3Shifted(), map, 0.7f);
                    hitCount++;
                }
            }
            MoteMaker.ThrowText(pawn.Position.ToVector3Shifted(), map, "Binguin_AbilityRodSkills_01".Translate() + hitCount + "Binguin_AbilityRodSkills_02".Translate(), 1.2f);
        }

        // ★ 2026-09 性能：MeleeWeapon_DamageMultiplier 是常量 stat → 懒加载缓存，
        //   不再每次命中/每次算面板伤害都查一次 DefDatabase。
        private static StatDef cachedDamageMultStat;
        private static bool damageMultStatLookedUp;

        private static StatDef DamageMultStat
        {
            get
            {
                if (!damageMultStatLookedUp)
                {
                    damageMultStatLookedUp = true;
                    cachedDamageMultStat = DefDatabase<StatDef>.GetNamedSilentFail("MeleeWeapon_DamageMultiplier");
                }
                return cachedDamageMultStat;
            }
        }

        // 面板伤害 = 主工具 power × 装备者 MeleeWeapon_DamageMultiplier（含 StatPart 乘数）
        private float PanelDamage(Pawn pawn)
        {
            float mult = pawn.GetStatValue(DamageMultStat);
            ThingDef rodDef = pawn.equipment != null && pawn.equipment.Primary != null ? pawn.equipment.Primary.def : null;
            if (rodDef != null && rodDef.tools != null && rodDef.tools.Count > 0)
            {
                return rodDef.tools[0].power * mult;
            }
            return 25f * mult;
        }
    }

    // 弯钩技能：瞄准半径 31 格内的一个单位，拉到施法者身前（PawnFlyer 无回弹）
    public class CompAbilityEffect_BinguinHook : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages)
        {
            return true;
        }

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn pawn = parent.pawn;
            Pawn victim = target.Thing as Pawn;
            if (pawn == null || victim == null || victim == pawn)
            {
                return;
            }
            // ★ 钩鱼：任意单位都能钩（敌人/中立/友军/动物都可以，用户定稿 2026-08-19）
            Map map = pawn.Map;
            if (map == null || victim.Map != map || victim.Dead)
            {
                return;
            }
            // 找施法者面前的可用格（先面前，再施法者本格）
            IntVec3 front = pawn.Position + pawn.Rotation.FacingCell;
            if (!front.Walkable(map) || front == victim.Position)
            {
                front = pawn.Position;
            }
            if (front == victim.Position)
            {
                return;
            }
            // ★ 特效：目标被钩起的尘土 + 施法者脚下尘土
            FleckMaker.ThrowDustPuff(victim.Position.ToVector3Shifted(), map, 1f);
            FleckMaker.ThrowDustPuff(pawn.Position.ToVector3Shifted(), map, 0.8f);
            PawnFlyer flyer = PawnFlyer.MakeFlyer(ThingDefOf.PawnFlyer, victim, front, null, null, false, null, null, default(LocalTargetInfo));
            if (flyer != null)
            {
                GenSpawn.Spawn(flyer, front, map, WipeMode.Vanish);
                MoteMaker.ThrowText(pawn.Position.ToVector3Shifted(), map, "Binguin_AbilityRodSkills_03".Translate(), 1.2f);
            }
        }
    }
}
