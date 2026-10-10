// ============================================================================
// 尚方宝剑 —— 武器 comp（2026-10-06 用户需求）
//
// 用户规格原文：
//   「尚方宝剑，用所有鱼类25条制作。需要研究终极钓鱼学，前置是高级钓鱼学，
//     需要高级研究台和多远分析仪。宝剑外形是一条鱼，会类似泰拉瑞亚的天顶剑
//     一样远程进行14条鱼的轮流斩击，对椭圆形范围造成范围伤害，每次斩击
//     10钝器伤害（附带冰爆），100%钝器穿透，每秒斩击14次。武器面板是
//     30钝器伤害/冷却0.7s。（注意，远程斩击是测试玩法，要在mod选项打开，
//     常态只能用于近战）」
//
// 本文件负责：**14 条鱼轮流的循环指针** + **装备时按 mod 选项授予远程技能**。
//   · 武器面板 30 钝击 / 0.7s 冷却 → XML `Binguin_VoidSwordFish.tools`
//   · 常态纯近战                     → 远程技能默认不给（BinguinSettings.enableVoidStrike）
//   · 14 条鱼轮流                    → `nextFishIndex`（存在 comp 上 ⇒ 跨施放/跨存档连续）
//   · 椭圆范围 14 连斩 + 10 钝击 + 冰爆 → `Ability_BinguinVoidStrike.cs`
//
// ★ 为什么用 Ability 而不是武器 verb（与高级钓竿同一个理由，见
//   `Ability_BinguinRodSkills.cs` 头部注释）：给 ThingDef 挂 verbs 会把武器判定成
//   **远程武器** —— 那就做不到用户要求的"常态只能用于近战"了。走 Ability 系统时，
//   剑本身永远是纯近战武器，远程斩击只是一个额外的命令按钮。
//
// ★ 继承 `CompEquippable`（不是 `ThingComp`）：装备武器的 gizmo 收集链是
//   `Pawn_EquipmentTracker.GetGizmos` → 主武器 `TryGetComp&lt;CompEquippable&gt;()`
//   → `CompGetEquippedGizmosExtra()`（1.6 反编译确认）。
//
// ★ 自定义类型由对应功能的 XML 声明，使用完整命名空间和程序集名。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;

using Binguin.Core;

namespace Binguin.Feature.VoidSword
{
    /// <summary>
    /// 尚方宝剑的数值调参（**数据容器**）。
    ///
    /// ★ 为什么单独抽一个纯数据类：尚方宝剑需要**两份** CompProperties
    ///   —— 一份挂在**武器**上（`ThingDef.comps`，类型必须是 `CompProperties`），
    ///   一份挂在**技能**上（`AbilityDef.comps`，类型必须是
    ///   `CompProperties_AbilityEffect`，否则 `CompAbilityEffect.Props` 转型失败）。
    ///   两处要的是同一套数值 ⇒ 抽成接口，两边各自实现（见下面两个类）。
    /// </summary>
    public interface IBinguinVoidSwordCfg
    {
        int Strikes { get; }
        float DamagePerStrike { get; }
        float IceBurstPerStrike { get; }
        float ArmorPenetration { get; }
        float OvalRadiusLong { get; }
        float OvalRadiusShort { get; }
        float MaxStrikeRange { get; }
    }

    /// <summary>武器上的那份 comp 属性（`ThingDef.comps` 用）。</summary>
    public class CompProperties_BinguinVoidSword : CompProperties, IBinguinVoidSwordCfg
    {
        public int strikes = 14;
        public float damagePerStrike = 10f;
        public float iceBurstPerStrike = 10f;
        public float armorPenetration = 1f;
        public float ovalRadiusLong = 3.5f;
        public float ovalRadiusShort = 2f;
        public float maxStrikeRange = 26f;

        public int Strikes { get { return strikes; } }
        public float DamagePerStrike { get { return damagePerStrike; } }
        public float IceBurstPerStrike { get { return iceBurstPerStrike; } }
        public float ArmorPenetration { get { return armorPenetration; } }
        public float OvalRadiusLong { get { return ovalRadiusLong; } }
        public float OvalRadiusShort { get { return ovalRadiusShort; } }
        public float MaxStrikeRange { get { return maxStrikeRange; } }

        public CompProperties_BinguinVoidSword()
        {
            compClass = typeof(CompBinguinVoidSword);
        }
    }

    /// <summary>
    /// 技能上的那份 comp 属性（`AbilityDef.comps` 用）。
    /// ★ 必须继承 `CompProperties_AbilityEffect` —— `CompAbilityEffect.Props`
    ///   的类型就是它，继承错了会在效果里转型失败（CS0039）。
    /// </summary>
    public class CompProperties_BinguinVoidStrike : CompProperties_AbilityEffect, IBinguinVoidSwordCfg
    {
        public int strikes = 14;
        public float damagePerStrike = 10f;
        public float iceBurstPerStrike = 10f;
        public float armorPenetration = 1f;
        public float ovalRadiusLong = 3.5f;
        public float ovalRadiusShort = 2f;
        public float maxStrikeRange = 26f;

        public int Strikes { get { return strikes; } }
        public float DamagePerStrike { get { return damagePerStrike; } }
        public float IceBurstPerStrike { get { return iceBurstPerStrike; } }
        public float ArmorPenetration { get { return armorPenetration; } }
        public float OvalRadiusLong { get { return ovalRadiusLong; } }
        public float OvalRadiusShort { get { return ovalRadiusShort; } }
        public float MaxStrikeRange { get { return maxStrikeRange; } }

        public CompProperties_BinguinVoidStrike()
        {
            compClass = typeof(CompAbilityEffect_BinguinVoidStrike);
        }
    }

    /// <summary>
    /// 尚方宝剑的武器 comp：14 条鱼轮流的循环指针 + 装备时授予远程技能。
    /// </summary>
    public class CompBinguinVoidSword : CompEquippable
    {
        /// <summary>下一条鱼的下标。存在 comp 上 ⇒ 跨施放、跨存档都连续。</summary>
        public int nextFishIndex;

        /// <summary>从 XML/代码拿到的调参（拿不到就用一份内置默认值，保证不 NRE）。</summary>
        public CompProperties_BinguinVoidSword Cfg
        {
            get
            {
                CompProperties_BinguinVoidSword p = props as CompProperties_BinguinVoidSword;
                if (p == null)
                {
                    p = new CompProperties_BinguinVoidSword();
                }
                return p;
            }
        }

        /// <summary>装备这柄剑的小人（`CompEquippable` 没有专门的 Pawn 字段，
        /// 用 `ThingComp.ParentHolder` 往上找）。</summary>
        public Pawn Wearer
        {
            get
            {
                if (parent == null)
                {
                    return null;
                }
                return parent.ParentHolder as Pawn;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<int>(ref nextFishIndex, "binguinVoidNextFish", 0, false);
        }

        /// <summary>
        /// 让"技能是否在命令栏里"与 mod 选项保持一致。
        /// ★ 由 `CompGetEquippedGizmosExtra` 每次收集 gizmo 时调用 ⇒ 玩家在
        ///   mod 设置里一勾，场上已经装备好的剑立刻就有技能按钮，不需要卸下重装。
        /// </summary>
        public void SyncAbility(Pawn pawn)
        {
            if (pawn == null || pawn.abilities == null)
            {
                return;
            }
            AbilityDef def = BinguinVoidSwordUtility.StrikeAbilityDef;
            if (def == null)
            {
                // XML 没加载（比如奥德赛没开）⇒ 什么都不做，绝不让武器坏掉
                return;
            }
            bool want = BinguinMod.Settings != null && BinguinMod.Settings.enableVoidStrike;
            Ability has = pawn.abilities.GetAbility(def);
            if (want && has == null)
            {
                pawn.abilities.GainAbility(def);
            }
            else if (!want && has != null)
            {
                pawn.abilities.RemoveAbility(def);
            }
        }

        public override IEnumerable<Gizmo> CompGetEquippedGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetEquippedGizmosExtra())
            {
                yield return g;
            }
            // ★ 每帧校正一次技能有无（mod 选项即时生效的唯一入口）
            SyncAbility(Wearer);
        }

        public override string CompInspectStringExtra()
        {
            string s = base.CompInspectStringExtra();
            if (BinguinMod.Settings != null && BinguinMod.Settings.enableVoidStrike)
            {
                string line = "远程斩击：已启用（测试玩法，能力栏 → 天顶鱼斩）";
                s = s.NullOrEmpty() ? line : s + "\n" + line;
            }
            return s;
        }
    }

    /// <summary>尚方宝剑用到的 def 查询（懒加载缓存，沿用本 mod 的 2026-09 性能惯例）。</summary>
    public static class BinguinVoidSwordUtility
    {
        private static bool looked;
        private static AbilityDef strikeAbility;
        private static DamageDef fishDamage;
        private static readonly List<ThingDef> fishDefs = new List<ThingDef>();

        /// <summary>
        /// 14 种鱼的 defName —— 就是**原版 Odyssey 的全部鱼类**（用户在游戏数据里
        /// 一共能找到 14 种鱼，正好对应"14 条鱼轮流斩击"）。
        /// </summary>
        public static readonly string[] FishDefNames = new string[]
        {
            "Fish_Salmon",    // 鲑鱼
            "Fish_Bass",      // 鲈鱼
            "Fish_Tilapia",   // 罗非鱼
            "Fish_Cod",       // 鳕鱼
            "Fish_Bluefish",  // 蓝鱼
            "Fish_Guppy",     // 孔雀鱼
            "Fish_Frostfish", // 霜鱼
            "Fish_Catfish",   // 鲶鱼
            "Fish_Piranha",   // 食人鱼
            "Fish_Dogfish",   // 角鲨
            "Fish_Marlin",    // 枪鱼
            "Fish_Tuna",      // 金枪鱼
            "Fish_Flounder",  // 比目鱼
            "Fish_Toxfish",   // 毒鱼
        };

        private static void Lookup()
        {
            if (looked)
            {
                return;
            }
            looked = true;
            strikeAbility = DefDatabase<AbilityDef>.GetNamedSilentFail("Binguin_AbilityVoidStrike");
            fishDamage = DefDatabase<DamageDef>.GetNamedSilentFail("Binguin_FishBluntDamage");
            for (int i = 0; i < FishDefNames.Length; i++)
            {
                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(FishDefNames[i]);
                if (d != null)
                {
                    fishDefs.Add(d);
                }
            }
        }

        /// <summary>14 种鱼（按 defName 顺序；缺哪个 mod 就跳过哪个）。</summary>
        public static List<ThingDef> FishDefs
        {
            get { Lookup(); return fishDefs; }
        }

        public static AbilityDef StrikeAbilityDef
        {
            get { Lookup(); return strikeAbility; }
        }

        /// <summary>斩击用的 DamageDef（钝击 + 冰爆），没配就退回原版 Blunt。</summary>
        public static DamageDef FishDamageDef
        {
            get { Lookup(); return fishDamage != null ? fishDamage : DamageDefOf.Blunt; }
        }
    }
}
