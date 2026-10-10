// ============================================================================
// 高级钓竿 StatPart —— 注册到原版 pawn stat / MaxHitPoints，动态应用钓竿乘数
//
// 机制（1.6 反编译确认）：
//   StatWorker.FinalizeValue 末尾遍历 stat.parts 调 TransformValue
//   （作用于 baseValue+offsets×factors 之后、postProcess 之前）→ 乘算/加算都安全。
//   近战伤害与穿甲共用 pawn.MeleeWeapon_DamageMultiplier（VerbProperties.
//   AdjustedMeleeDamageAmount / AdjustedArmorPenetration 确认），
//   冷却用 pawn.MeleeWeapon_CooldownMultiplier。
// 注册：静态构造对 7 个 StatDef.parts Add（每 stat 一个实例，stat 字段区分分支）。
// ============================================================================

using System;
using RimWorld;
using Verse;

namespace Binguin.Feature.Rods
{
    public class StatPart_BinguinRod : StatPart
    {
        public StatDef stat;

        // ★ 2026-09 性能：本 StatPart 被挂到 7 个 stat 上（含 MoveSpeed /
        //   MaxHitPoints），TransformValue 属于"每次 stat 查询都会跑"的热路径。
        //   原来每次都做字符串比较 + 字符串 switch → 现在在构造时把 stat 解析成
        //   一个整数种类，运行期只比 int。
        private const int KindNone = 0;
        private const int KindMeleeDamage = 1;
        private const int KindMeleeCooldown = 2;
        private const int KindMoveSpeed = 3;
        private const int KindToxicResistance = 4;
        private const int KindImmunityGainSpeed = 5;
        private const int KindInjuryHealing = 6;
        private const int KindMaxHitPoints = 7;

        private int statKind;

        public StatPart_BinguinRod()
        {
        }

        public StatPart_BinguinRod(StatDef statDef)
        {
            stat = statDef;
            statKind = KindOf(statDef);
        }

        private static int KindOf(StatDef statDef)
        {
            if (statDef == null)
            {
                return KindNone;
            }
            switch (statDef.defName)
            {
                case "MaxHitPoints": return KindMaxHitPoints;
                case "MeleeWeapon_DamageMultiplier": return KindMeleeDamage;
                case "MeleeWeapon_CooldownMultiplier": return KindMeleeCooldown;
                case "MoveSpeed": return KindMoveSpeed;
                case "ToxicResistance": return KindToxicResistance;
                case "ImmunityGainSpeed": return KindImmunityGainSpeed;
                case "InjuryHealingFactor": return KindInjuryHealing;
            }
            return KindNone;
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            // ---- 鱼竿物品自身的 stat（MaxHitPoints 耐久乘数） ----
            // ★ 2026-09：StartsWith 改序数比较（默认的区域敏感比较会走
            //   CultureInfo，比序数比较慢很多），且用缓存的 statKind 判断。
            if (req.Thing != null && req.Thing.def != null
                && req.Thing.def.defName.StartsWith("Binguin_AdvancedRod", StringComparison.Ordinal))
            {
                if (statKind == KindMaxHitPoints)
                {
                    CompBinguinRod comp = req.Thing.TryGetComp<CompBinguinRod>();
                    if (comp != null)
                    {
                        val *= comp.DurabilityMultiplier;
                    }
                }
                return;
            }

            // ---- 装备者的 pawn stat ----
            // ★ StatRequest.For(Thing) 只填 thingInt（pawn 字段可能是 null），
            //   必须用 req.Thing as Pawn 兜底（之前鱼饵 buff 不生效的根因）
            Pawn pawn = req.Pawn;
            if (pawn == null)
            {
                pawn = req.Thing as Pawn;
            }
            if (pawn == null || pawn.equipment == null || pawn.equipment.Primary == null)
            {
                return;
            }
            if (statKind == KindNone)
            {
                return;
            }
            CompBinguinRod rod = pawn.equipment.Primary.TryGetComp<CompBinguinRod>();
            if (rod == null)
            {
                return;
            }
            switch (statKind)
            {
                case KindMeleeDamage:
                    val *= rod.DamageMultiplier;
                    break;
                case KindMeleeCooldown:
                    val *= rod.CooldownMultiplier;
                    break;
                case KindMoveSpeed:
                    if (rod.HasEffect(BinguinBaitEffect.Vegetable))
                    {
                        val += 0.2f;
                    }
                    break;
                case KindToxicResistance:
                    if (rod.HasEffect(BinguinBaitEffect.InsectMeat))
                    {
                        val = 1f;   // 免疫中毒
                    }
                    break;
                case KindImmunityGainSpeed:
                    if (rod.HasEffect(BinguinBaitEffect.Omnivorous))
                    {
                        val *= 2f;   // 免疫速度 ×200%（★ 1.6 免疫速度 stat = ImmunityGainSpeed）
                    }
                    break;
                case KindInjuryHealing:
                    if (rod.HasEffect(BinguinBaitEffect.Omnivorous))
                    {
                        val *= 2f;   // 自愈速度 ×200%
                    }
                    break;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            Pawn explainPawn = req.Pawn;
            if (explainPawn == null)
            {
                explainPawn = req.Thing as Pawn;
            }
            if (explainPawn != null && explainPawn.equipment != null && explainPawn.equipment.Primary != null)
            {
                CompBinguinRod rod = explainPawn.equipment.Primary.TryGetComp<CompBinguinRod>();
                // ★ 2026-09：与 TransformValue 统一改用构造时解析好的 statKind
                //   （信息卡每次开合都会调用本方法，不必再做字符串 switch）
                if (rod != null && statKind != KindNone)
                {
                    switch (statKind)
                    {
                        case KindMeleeDamage:
                            return "Binguin_StatPartRod_01".Translate() + rod.DamageMultiplier.ToString("0.00") + "）";
                        case KindMeleeCooldown:
                            return "Binguin_StatPartRod_02".Translate() + rod.CooldownMultiplier.ToString("0.00") + "）";
                        case KindMoveSpeed:
                            if (rod.HasEffect(BinguinBaitEffect.Vegetable)) return "Binguin_StatPartRod_03".Translate();
                            break;
                        case KindToxicResistance:
                            if (rod.HasEffect(BinguinBaitEffect.InsectMeat)) return "Binguin_StatPartRod_04".Translate();
                            break;
                        case KindImmunityGainSpeed:
                            if (rod.HasEffect(BinguinBaitEffect.Omnivorous)) return "Binguin_StatPartRod_05".Translate();
                            break;
                        case KindInjuryHealing:
                            if (rod.HasEffect(BinguinBaitEffect.Omnivorous)) return "Binguin_StatPartRod_06".Translate();
                            break;
                    }
                }
            }
            return "";
        }
    }
}
