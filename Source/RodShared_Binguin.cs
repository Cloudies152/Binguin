// ============================================================================
// 高级钓鱼学 —— 共享定义（鱼饵效果枚举 + 材质乘数工具 + 食物判定）
//
// ★ 属性计算全部基于原版 stat 通道（2026-08-19 反编译 VerbProperties 确认）：
//   近战伤害 = tool.power × pawn.MeleeWeapon_DamageMultiplier
//   近战穿甲 = tool.armorPenetration × pawn.MeleeWeapon_DamageMultiplier
//   攻击冷却 = tool.cooldownTime × pawn.MeleeWeapon_CooldownMultiplier
//   → 用 StatPart 注册到这两个 stat 即可动态乘算（StatPart 在 FinalizeValue
//     阶段应用，作用于 offsets/factors 之后、postProcess 之前）。
// 材质加成数值直接读原版 stuff 乘数：
//   伤害：stuff 的 SharpDamageMultiplier / BluntDamageMultiplier
//         （铀 Blunt=1.5 → 用户示例「铀锤竿头 ×150%」完全一致）
//   冷却：stuff 的 MeleeWeapon_CooldownMultiplier
//         （玻璃钢=0.8 → 用户示例「玻璃钢 ×80%」完全一致）
//   耐久：stuff 的 MaxHitPoints（铀 2.5 / 玻璃钢 2.8 / 铁 1.0）
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin
{
    // 鱼饵效果（最多两种组合）
    public enum BinguinBaitEffect
    {
        None = 0,
        Vegetable,     // 素：移速 +0.2
        Meat,          // 肉：近战倍率 ×105%
        InsectMeat,    // 虫肉：免疫中毒
        TwistedMeat,   // 扭曲肉：征召时不会崩溃
        HumanMeat,     // 人肉：击杀敌人后心情 +5
        Jelly,         // 虫胶：心情 +3
        Omnivorous     // 无忌口：自愈速度和免疫速度 ×200%
    }

    public static class BinguinRodUtility
    {
        // 配件 defName
        public const string ShaftLong = "Binguin_RodShaft_Long";
        public const string ShaftShort = "Binguin_RodShaft_Short";
        public const string TipBlade = "Binguin_RodTip_Blade";
        public const string TipHammer = "Binguin_RodTip_Hammer";
        public const string HookStraight = "Binguin_RodHook_Straight";
        public const string HookCurved = "Binguin_RodHook_Curved";
        public const string Bait = "Binguin_RodBait";
        public const string RodBlade = "Binguin_AdvancedRod_Blade";
        public const string RodHammer = "Binguin_AdvancedRod_Hammer";

        // ★ 2026-09 性能：这三个方法会被 StatPart_BinguinRod 在【每次 stat 查询】
        //   时调用（挂在含 MoveSpeed 的 7 个 stat 上，属于全局热路径），
        //   而 stat def 是常量 → 改成懒加载静态缓存（null 不缓存，保持可重试）。
        private static StatDef cachedCooldownStat;
        private static StatDef cachedSharpStat;
        private static StatDef cachedBluntStat;
        private static StatDef cachedMaxHpStat;

        private static StatDef CooldownStat
        {
            get
            {
                if (cachedCooldownStat == null)
                {
                    cachedCooldownStat = DefDatabase<StatDef>.GetNamedSilentFail("MeleeWeapon_CooldownMultiplier");
                }
                return cachedCooldownStat;
            }
        }

        private static StatDef SharpStat
        {
            get
            {
                if (cachedSharpStat == null)
                {
                    cachedSharpStat = DefDatabase<StatDef>.GetNamedSilentFail("SharpDamageMultiplier");
                }
                return cachedSharpStat;
            }
        }

        private static StatDef BluntStat
        {
            get
            {
                if (cachedBluntStat == null)
                {
                    cachedBluntStat = DefDatabase<StatDef>.GetNamedSilentFail("BluntDamageMultiplier");
                }
                return cachedBluntStat;
            }
        }

        private static StatDef MaxHpStat
        {
            get
            {
                if (cachedMaxHpStat == null)
                {
                    cachedMaxHpStat = DefDatabase<StatDef>.GetNamedSilentFail("MaxHitPoints");
                }
                return cachedMaxHpStat;
            }
        }

        // 竿身材质 → 冷却倍率（stuff 的 MeleeWeapon_CooldownMultiplier）
        public static float CooldownMultOf(ThingDef stuff)
        {
            if (stuff == null) return 1f;
            StatDef stat = CooldownStat;
            if (stat == null) return 1f;
            return stuff.GetStatValueAbstract(stat, null);
        }

        // 竿头材质 → 伤害/穿甲倍率（刀制用 Sharp，锤制用 Blunt）
        public static float DamageMultOf(ThingDef stuff, bool sharp)
        {
            if (stuff == null) return 1f;
            StatDef stat = sharp ? SharpStat : BluntStat;
            if (stat == null) return 1f;
            return stuff.GetStatValueAbstract(stat, null);
        }

        // 鱼钩材质 → 耐久倍率（stuff 的 MaxHitPoints）
        public static float DurabilityMultOf(ThingDef stuff)
        {
            if (stuff == null) return 1f;
            StatDef stat = MaxHpStat;
            if (stat == null) return 1f;
            return stuff.GetStatValueAbstract(stat, null);
        }

        // 食物 → 鱼饵效果（判定顺序：特殊 def → 肉 → 素 → 兜底无忌口）
        public static BinguinBaitEffect EffectForFood(ThingDef food)
        {
            if (food == null) return BinguinBaitEffect.None;
            if (food.defName == "Meat_Human") return BinguinBaitEffect.HumanMeat;
            if (food.defName == "Meat_Twisted") return BinguinBaitEffect.TwistedMeat;
            if (food.defName == "Meat_Insect" || food.defName == "Meat_Insect_Twisted") return BinguinBaitEffect.InsectMeat;
            if (food.defName == "InsectJelly") return BinguinBaitEffect.Jelly;
            if (food.ingestible != null)
            {
                if (food.ingestible.foodType.HasFlag(FoodTypeFlags.Meat)) return BinguinBaitEffect.Meat;
                if (food.ingestible.foodType.HasFlag(FoodTypeFlags.VegetableOrFruit)) return BinguinBaitEffect.Vegetable;
            }
            return BinguinBaitEffect.Omnivorous;
        }

        public static string EffectLabel(BinguinBaitEffect e)
        {
            switch (e)
            {
                case BinguinBaitEffect.Vegetable: return "Binguin_RodShared_01".Translate();
                case BinguinBaitEffect.Meat: return "Binguin_RodShared_02".Translate();
                case BinguinBaitEffect.InsectMeat: return "Binguin_RodShared_03".Translate();
                case BinguinBaitEffect.TwistedMeat: return "Binguin_RodShared_04".Translate();
                case BinguinBaitEffect.HumanMeat: return "Binguin_RodShared_05".Translate();
                case BinguinBaitEffect.Jelly: return "Binguin_RodShared_06".Translate();
                case BinguinBaitEffect.Omnivorous: return "Binguin_RodShared_07".Translate();
                default: return "Binguin_RodShared_08".Translate();
            }
        }

        public static string EffectDesc(BinguinBaitEffect e)
        {
            switch (e)
            {
                case BinguinBaitEffect.Vegetable: return "Binguin_RodShared_09".Translate();
                case BinguinBaitEffect.Meat: return "Binguin_RodShared_10".Translate();
                case BinguinBaitEffect.InsectMeat: return "Binguin_RodShared_11".Translate();
                case BinguinBaitEffect.TwistedMeat: return "Binguin_RodShared_12".Translate();
                case BinguinBaitEffect.HumanMeat: return "Binguin_RodShared_13".Translate();
                case BinguinBaitEffect.Jelly: return "Binguin_RodShared_14".Translate();
                case BinguinBaitEffect.Omnivorous: return "Binguin_RodShared_15".Translate();
                default: return "";
            }
        }

        // 效果 → 消耗哪类食物（用于制饵对话框检查仓库）
        public static bool IsFoodForEffect(Thing food, BinguinBaitEffect e)
        {
            if (food == null || food.def == null || food.def.ingestible == null) return false;
            if (!food.def.IsNutritionGivingIngestible) return false;
            switch (e)
            {
                case BinguinBaitEffect.Vegetable:
                    return food.def.ingestible.foodType.HasFlag(FoodTypeFlags.VegetableOrFruit);
                case BinguinBaitEffect.Meat:
                    return food.def.defName != "Meat_Human"
                        && food.def.defName != "Meat_Twisted"
                        && food.def.defName != "Meat_Insect"
                        && food.def.ingestible.foodType.HasFlag(FoodTypeFlags.Meat);
                case BinguinBaitEffect.InsectMeat:
                    return food.def.defName == "Meat_Insect" || food.def.defName == "Meat_Insect_Twisted";
                case BinguinBaitEffect.TwistedMeat:
                    return food.def.defName == "Meat_Twisted";
                case BinguinBaitEffect.HumanMeat:
                    return food.def.defName == "Meat_Human";
                case BinguinBaitEffect.Jelly:
                    return food.def.defName == "InsectJelly";
                case BinguinBaitEffect.Omnivorous:
                    return true;   // 无忌口：任意可食用
                default:
                    return false;
            }
        }

        // 地图上某效果的可用食物总营养（★ 2026-08-19 用户定稿：虫胶也按营养值算，
        //   不再按个数特判）
        public static float AvailableNutritionFor(Map map, BinguinBaitEffect e)
        {
            float total = 0f;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (t == null || t.def == null || t.def.ingestible == null) continue;
                if (!IsFoodForEffect(t, e)) continue;
                total += t.def.GetStatValueAbstract(StatDefOf.Nutrition, t.Stuff) * t.stackCount;
            }
            return total;
        }

        // 从地图扣除指定营养的食物（返回实际扣除的营养）
        // ★ 不能在枚举 listerThings 时 Destroy（Collection was modified →
        //   食物消失但不产鱼饵！用户 2026-08-19 反馈）——先收集整堆，循环后统一销毁
        public static float ConsumeNutritionFor(Map map, BinguinBaitEffect e, float need)
        {
            float consumed = 0f;
            List<Thing> fullStacks = new List<Thing>();
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (consumed >= need - 0.001f) break;
                if (t == null || t.def == null || t.def.ingestible == null) continue;
                if (!IsFoodForEffect(t, e)) continue;
                float nut = t.def.GetStatValueAbstract(StatDefOf.Nutrition, t.Stuff);
                if (nut <= 0.001f) continue;
                float stackNut = nut * t.stackCount;
                if (stackNut <= need - consumed + 0.001f)
                {
                    // 整堆全取：收集起来，循环后统一销毁
                    fullStacks.Add(t);
                    consumed += stackNut;
                }
                else
                {
                    // 部分取：SplitOff 拆出新堆（未在地图，直接销毁安全）
                    int takeCount = Mathf.CeilToInt((need - consumed) / nut);
                    takeCount = Mathf.Min(takeCount, t.stackCount);
                    Thing removed = t.SplitOff(takeCount);
                    if (removed != null)
                    {
                        removed.Destroy();
                        consumed += nut * takeCount;
                    }
                }
            }
            for (int i = 0; i < fullStacks.Count; i++)
            {
                if (fullStacks[i] != null && !fullStacks[i].Destroyed)
                {
                    fullStacks[i].Destroy();
                }
            }
            return consumed;
        }
    }
}
