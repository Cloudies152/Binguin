// ============================================================================
// 冰鹅族【饥饱阈值】补丁 —— 2026-10-01
//
// ★ 为什么要写这个补丁（用户需求的连锁反应）：
//   用户把冰鹅的【营养储存上限】抬到 1.2（Defs/01_RaceAndAppearance/AlienRace_Binguin.xml 的 <MaxNutrition>）。
//   但饥饱阈值是按【占上限的百分比】算的 ⇒ 上限一抬，"开始饿"的绝对营养值也跟着抬高：
//       上限 1.0（改版前）：… → 0.36 开始饿
//       上限 1.2（现状）  ：… → 0.432 开始饿      ← 比原来晚饿，用户不要这个
//   用户明确要求：**"开始饿"的绝对位置保持与改版前一致（0.36）**。
//
// ★★ 为什么只能写补丁、不能改 XML（IL 实证，别再去找 XML 字段了）：
//   阈值不是常数，而是从一个【计算属性】推出来的：
//     Need_Food.get_PercentageThreshHungry         = RaceProps.FoodLevelPercentageWantEat × 0.8
//     Need_Food.get_PercentageThreshUrgentlyHungry = RaceProps.FoodLevelPercentageWantEat × 0.4
//   而 `RaceProperties.FoodLevelPercentageWantEat` 的 getter 是
//     `switch (ResolvedDietCategory)` → 直接返回 0.3 / 0.4 / 0.45 这类常数，
//   它【不是 XML 字段】（RaceProperties 里没有对应字段），def 里无法覆盖。
//   ⇒ 唯一办法就是给这个 getter 挂 Harmony 后置补丁，只改冰鹅族。
//
// ★ 数值推导（关键：阈值是"占上限的百分比"，所以要乘上限）：
//   绝对营养值 = PercentageThreshHungry × MaxNutrition
//              = (WantEat × 0.8) × 1.2
//   要求它 = 改版前的 0.36：
//       (WantEat × 0.8) × 1.2 = 0.36   ⇒   WantEat = 0.375
//   ⇒ 把冰鹅的系数 0.45 改成 **0.375**，则两个阈值同时回到改版前的绝对值：
//       Hungry         = 0.375 × 0.8 × 1.2 = 0.36  ✓（改版前 = 0.45×0.8×1.0 = 0.36）
//       UrgentlyHungry = 0.375 × 0.4 × 1.2 = 0.18  ✓（改版前 = 0.45×0.4×1.0 = 0.18）
//   ★ 净效果：**上限更大（能吃得更多），但开始饿/紧急饥饿的绝对时机一点没变。**
//
// ★ 安全性：
//   · 只对【冰鹅族】生效（复用 BinguinRaceUtility，全项目唯一种族判据）；
//     Har 其它种族、原版人类、动物一律不受影响。
//   · 整段 try/catch；反射拿不到 pawn 时原值放行，绝不干扰吃饭逻辑。
//   · Need.pawn 是 protected 字段（Mono.Cecil 实证 IsPublic=False）⇒ 反射需 NonPublic。
// ============================================================================

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Binguin
{
    public static class Patch_BinguinFoodThreshold
    {
        /// <summary>
        /// 冰鹅的 FoodLevelPercentageWantEat 目标值。
        /// 0.375 × 0.8 = 0.30（即"上限的 30% 开始饿"），30% × 1.2 = 0.36 ⇒ 与改版前同绝对位置。
        /// </summary>
        private const float TargetWantEat = 0.375f;

        // Need.pawn 是 protected 字段，反射读一次后缓存（不要每次调用都 GetField）
        private static FieldInfo pawnField;
        private static bool pawnFieldLookedUp;

        private static Pawn PawnOf(Need need)
        {
            try
            {
                if (!pawnFieldLookedUp)
                {
                    pawnFieldLookedUp = true;
                    pawnField = typeof(Need).GetField("pawn",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }
                if (pawnField == null || need == null) return null;
                return pawnField.GetValue(need) as Pawn;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>后置补丁：把冰鹅的可吃系数改成 0.375 ⇒ 两个阈值回到改版前的绝对值。</summary>
        public static void Postfix(Need __instance, ref float __result)
        {
            try
            {
                if (__instance == null) return;
                if (Math.Abs(__result - TargetWantEat) < 0.0001f) return;

                Pawn pawn = PawnOf(__instance);
                if (!BinguinRaceUtility.IsBinguin(pawn)) return;

                __result = TargetWantEat;
                // 说明：本方法【不用】 __runOriginal —— 它只能注入到 Prefix，
                // Postfix 的参数表里没有它（IL 实证：AddPrefixes 引用 2 次 / AddPostfixes 0 次，
                // 用了会 CS0103 编译不过）。若别的前缀把原方法 skip 掉，postfix 仍会执行并覆盖结果，
                // 对本 mod 而言这正是想要的"冰鹅阈值由我说了算"。
            }
            catch (Exception)
            {
                // 任何异常都放行原值 —— 绝不因为这条补丁影响吃饭逻辑
            }
        }
    }
}
