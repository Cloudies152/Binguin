// ============================================================================
// 高级钓竿 —— 配件数据存档 + 动态属性计算 + 装备技能（穿刺/钩鱼）
//
// 属性规则（用户设计文档）：
//   攻击冷却倍率 ← 竿身（短制 ×80% × 竿身材质冷却乘数）
//   钝器/锐器伤害倍率 & 穿甲 ← 竿头（长制 ×120% × 竿头材质 Sharp/Blunt 乘数）
//   耐久度乘数 ← 鱼钩（材质 MaxHitPoints 乘数）
//   额外 Buff ← 鱼饵（素/肉/虫肉/扭曲肉/人肉/虫胶/无忌口）
// 动态化：StatPart_BinguinRod 注册到 pawn stat（伤害/冷却/移速/毒性/免疫/自愈）
//   与 MaxHitPoints（耐久）；伤害与穿甲共用 MeleeWeapon_DamageMultiplier（原版结算）。
//
// 技能（装备时 gizmo，10s 冷却）：
//   直钩「穿刺」：向目标方向 10 格内所有敌人造成 200% 面板伤害
//   弯钩「钩鱼」：31 格内选中单位，PawnFlyer 拉到身前
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Binguin.Feature.Rods
{
    public class CompProperties_BinguinRod : CompProperties
    {
        public CompProperties_BinguinRod()
        {
            compClass = typeof(CompBinguinRod);
        }
    }

    // ★ 必须继承 CompEquippable（不是 ThingComp！）：装备武器的 gizmo 收集链 =
    //   Pawn_EquipmentTracker.GetGizmos → 主武器 TryGetComp<CompEquippable>() →
    //   CompGetEquippedGizmosExtra()（1.6 反编译确认）。CompGetWornGizmosExtra
    //   只被 Apparel.GetWornGizmos 调用（衣服专用）→ 之前技能不显示的原因。
    public class CompBinguinRod : CompEquippable
    {
        // ---- 配件数据（合成时写入，Scribe 存档） ----
        public bool longShaft = true;        // true=长制 false=短制
        public bool bladeTip = true;         // true=刀制 false=锤制
        public bool straightHook = true;     // true=直钩 false=弯钩
        public ThingDef shaftStuff;          // 竿身材质
        public ThingDef tipStuff;            // 竿头材质
        public ThingDef hookStuff;           // 鱼钩材质
        public List<BinguinBaitEffect> baitEffects;   // 鱼饵效果（0~2）

        private int nextSkillTick = -1;      // 技能冷却

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<bool>(ref longShaft, "longShaft", true, false);
            Scribe_Values.Look<bool>(ref bladeTip, "bladeTip", true, false);
            Scribe_Values.Look<bool>(ref straightHook, "straightHook", true, false);
            Scribe_Defs.Look<ThingDef>(ref shaftStuff, "shaftStuff");
            Scribe_Defs.Look<ThingDef>(ref tipStuff, "tipStuff");
            Scribe_Defs.Look<ThingDef>(ref hookStuff, "hookStuff");
            Scribe_Collections.Look<BinguinBaitEffect>(ref baitEffects, "baitEffects", LookMode.Value);
            Scribe_Values.Look<int>(ref nextSkillTick, "nextSkillTick", -1, false);
        }

        public bool HasEffect(BinguinBaitEffect e)
        {
            return baitEffects != null && baitEffects.Contains(e);
        }

        // ---- 动态属性（StatPart 读取） ----
        // 伤害/穿甲倍率：长制 ×1.2 × 竿头材质乘数 × 肉饵 1.05
        public float DamageMultiplier
        {
            get
            {
                float m = 1f;
                if (longShaft) m *= 1.2f;
                m *= BinguinRodUtility.DamageMultOf(tipStuff, bladeTip);
                if (HasEffect(BinguinBaitEffect.Meat)) m *= 1.05f;
                return m;
            }
        }

        // 攻击冷却倍率：短制 ×0.8 × 竿身材质乘数
        public float CooldownMultiplier
        {
            get
            {
                float m = 1f;
                if (!longShaft) m *= 0.8f;
                m *= BinguinRodUtility.CooldownMultOf(shaftStuff);
                return m;
            }
        }

        // 耐久倍率：鱼钩材质 MaxHitPoints 乘数
        public float DurabilityMultiplier
        {
            get { return BinguinRodUtility.DurabilityMultOf(hookStuff); }
        }

        public override string CompInspectStringExtra()
        {
            List<string> parts = new List<string>();
            parts.Add((longShaft ? "Binguin_CompRod_01".Translate() : "Binguin_CompRod_02".Translate()) + "（" + StuffLabel(shaftStuff) + "）");
            parts.Add((bladeTip ? "Binguin_CompRod_03".Translate() : "Binguin_CompRod_04".Translate()) + "（" + StuffLabel(tipStuff) + "）");
            parts.Add((straightHook ? "Binguin_CompRod_05".Translate() : "Binguin_CompRod_06".Translate()) + "（" + StuffLabel(hookStuff) + "）");
            parts.Add("Binguin_CompRod_07".Translate() + DamageMultiplier.ToString("0.00") + "Binguin_CompRod_08".Translate() + CooldownMultiplier.ToString("0.00"));
            if (baitEffects != null && baitEffects.Count > 0)
            {
                List<string> bs = new List<string>();
                for (int i = 0; i < baitEffects.Count; i++)
                {
                    bs.Add(BinguinRodUtility.EffectLabel(baitEffects[i]));
                }
                // ★ 2026-09：去掉 .ToArray()——string.Join(string, IEnumerable<string>)
                //   在 .NET 4 就有，原来每次检视都要多分配两个数组。
                parts.Add("Binguin_CompRod_09".Translate() + string.Join("+", bs));
            }
            return string.Join("\n", parts);
        }

        private static string StuffLabel(ThingDef stuff)
        {
            return stuff != null ? stuff.label : "?";
        }

        // ==================== 装备技能（Ability 系统，Bladelink 式） ====================
        // 装备 → pawn.abilities.GainAbility（命令栏出现技能按钮，鱼竿保持近战）；
        // 卸下 → RemoveAbility。★ 不能用武器 verb（会把鱼竿变成远程武器！）
        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            TryChangeAbility(pawn, true);
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            TryChangeAbility(pawn, false);
            base.Notify_Unequipped(pawn);
        }

        private void TryChangeAbility(Pawn pawn, bool gain)
        {
            if (pawn == null || pawn.abilities == null)
            {
                return;
            }
            string defName = straightHook ? "Binguin_AbilityPierce" : "Binguin_AbilityHook";
            AbilityDef abilityDef = DefDatabase<AbilityDef>.GetNamedSilentFail(defName);
            if (abilityDef == null)
            {
                return;
            }
            if (gain)
            {
                if (pawn.abilities.GetAbility(abilityDef) == null)
                {
                    pawn.abilities.GainAbility(abilityDef);
                }
            }
            else
            {
                pawn.abilities.RemoveAbility(abilityDef);
            }
        }
    }
}
