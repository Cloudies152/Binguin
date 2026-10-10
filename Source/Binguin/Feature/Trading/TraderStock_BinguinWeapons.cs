// ============================================================================
// 冰鹅族【作战商出售冰鹅族武器】—— 2026-09-26 用户需求
//
// 用户需求原文：
//   「再给作战商加 2-5 把冰鹅族武器出售，只能是冷冻武器及以下的武器
//     （霰雪无垠武器不售卖）」
//
// ★ 为什么必须写自定义生成器（不能只用原版 StockGenerator_*）：
//   需求是「**2-5 把不同**的武器」，而原版三种生成器都做不到：
//     · `StockGenerator_MultiDef`：IL 实证它只 `RandomElement(thingDefs)` 取**1 个** def，
//       再生成 `countRange` 个 —— 一次只有一种武器，且【完全不读 thingDefCountRange】。
//     · `StockGenerator_Category` / `Tag` 能按"几种"抽，但它们只认 category/tag。
//       而冰鹅武器【共享 weaponTags】（实测：6 把枪都有 Gun + IndustrialGunAdvanced，
//       杠杆步枪/精确步枪/企鹅飞踢还共享 LongShots）
//       ⇒ 按 tag 收会把霰雪无垠的【企鹅飞踢！！】一起收进来，无法排除。
//     · 也没有"按研究层级排除"的原版手段。
//   ⇒ 自己写一个 StockGenerator 子类，精确实现"2-5 种不同武器"。
//
// ★★ 武器层级怎么判定的（IL + XML 双向核对过）：
//   冰鹅族武器共 7 把，按解锁研究分三层：
//     · 冰鹅机加工 Binguin_ResearchMachining
//         Binguin_SMG 冲锋枪 / Binguin_Shotgun 霰弹枪 / Binguin_LeverRifle 杠杆步枪
//     · 突袭冷冻武器 Binguin_ResearchFrostAssault   ← 用户说的"冷冻武器"
//         Binguin_AssaultRifle 突击步枪 / Binguin_PrecisionRifle 精确步枪
//     · 霰雪无垠 Binguin_ResearchEndlessSnow        ← 用户要求【不卖】
//         Binguin_PenguinKick 企鹅飞踢！！ / Binguin_LaserGatling 极激急击机枪
//   ⇒ "冷冻武器及以下" = 前两层 = 5 把，排除霰雪无垠那 2 把。
//
// ★ 排除是【数据驱动】的：遍历 DefDatabase<RecipeDef>，凡是
//   `researchPrerequisite` / `researchPrerequisites` 命中黑名单研究的，
//   其产物就整类排除。以后新加的武器只要挂在霰雪无垠（或更高级）下，
//   会自动被排除 —— 不需要回来改这个文件。
//   （黑名单在下面 ExcludedResearchDefNames，加研究名即可。）
//
// ★ 品质：**不设置**，跟原版商人一致。
//   实测 `CompQuality.SetQuality` 的 23 处调用者里【没有任何一处】在
//   TraderStock / StockGenerator 路径上 ⇒ 原版商人的武器/衣服就是默认品质。
//   这里也保持一致，不自己发明"商人卖传奇武器"。
//
// ★ 挂载方式：本 mod 的 XML 里【零自定义类型引用】（见 BinguinDefPatches.cs 顶部说明：
//   GenTypes 缓存会在早期固化导致 "Could not find type"），
//   所以这个生成器由 C# 在 `BinguinDefPatches` 静态构造里加进
//   `Binguin_Caravan_CombatSupplier` 的 stockGenerators。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;   // PlanetTile（GenerateThings 的签名要用）
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Trading
{
    /// <summary>给作战商生成 2-5 把【不同的】冰鹅族武器（排除霰雪无垠层级）。</summary>
    public class StockGenerator_BinguinWeapons : StockGenerator
    {
        /// <summary>一次补货出几种武器（2-5 把，用户指定）。</summary>
        public IntRange weaponCountRange = new IntRange(2, 5);

        /// <summary>每种武器几个。1~2：2 种=2~4 把，5 种=5~10 把，手感合理。</summary>
        public IntRange countPerWeapon = new IntRange(1, 2);

        /// <summary>
        /// 要排除的武器层级研究：产物挂在这些研究（或更高级）下的武器一律不卖。
        /// ★ 霰雪无垠 = 用户明确要求不卖的终极层。
        /// </summary>
        private static readonly string[] ExcludedResearchDefNames =
        {
            "Binguin_ResearchEndlessSnow",     // 霰雪无垠（终极）：企鹅飞踢！！ / 极激急击机枪
        };

        private static List<ThingDef> cachedEligible;
        private static bool lookedUp;
        // 选过的武器（每次 GenerateThings 开头清空）
        private readonly List<ThingDef> tmpChosen = new List<ThingDef>();

        /// <summary>
        /// 可卖武器 = 所有挂在本 mod 研究链下、且【不在排除层级】的武器。
        /// 用 RecipeDef 的研究前置来判定，新增武器自动纳入/排除。
        /// </summary>
        public static List<ThingDef> EligibleWeapons
        {
            get
            {
                if (lookedUp) return cachedEligible;
                lookedUp = true;
                List<ThingDef> list = new List<ThingDef>();
                try
                {
                    List<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
                    for (int i = 0; i < recipes.Count; i++)
                    {
                        RecipeDef r = recipes[i];
                        if (r == null) continue;
                        if (IsExcludedResearch(r.researchPrerequisite)) continue;
                        if (r.researchPrerequisites != null)
                        {
                            bool skip = false;
                            for (int j = 0; j < r.researchPrerequisites.Count; j++)
                            {
                                if (IsExcludedResearch(r.researchPrerequisites[j]))
                                {
                                    skip = true;
                                    break;
                                }
                            }
                            if (skip) continue;
                        }
                        ThingDef product = ProducedDef(r);
                        if (product == null) continue;
                        // ★ 只要【冰鹅族自己的】武器：必须是武器，且走本 mod 的研究
                        if (!product.IsWeapon) continue;
                        // ★★ 只要【枪】——排除近战工具。
                        //   实测分层：7 把枪都是 parent=BaseGunWithQuality 且
                        //   IsRangedWeapon/IsWeaponUsingProjectiles = true；
                        //   而 3 把钓竿（Binguin_FishingRod / AdvancedRod_Blade /
                        //   AdvancedRod_Hammer）都是 parent=BaseWeapon + weaponClasses=Melee。
                        //   作战商卖钓竿显然不合适，所以按"是不是远程投射武器"筛。
                        if (!product.IsRangedWeapon || !product.IsWeaponUsingProjectiles) continue;
                        if (product.defName == null || !product.defName.StartsWith("Binguin_",
                                StringComparison.Ordinal)) continue;
                        if (!IsMadeByBinguinResearch(r)) continue;
                        if (!list.Contains(product)) list.Add(product);
                    }
                }
                catch (Exception e)
                {
                    BinguinLogUtility.Log("枚举可售冰鹅武器失败：" + e.Message, severity: 1, isDebug: false);
                }
                // 只有真的查到了才缓存（defs 没加载完时别把空表缓存住）
                if (list.Count > 0) cachedEligible = list;
                return list;
            }
        }

        /// <summary>这个研究名是否在"不卖"黑名单里。</summary>
        private static bool IsExcludedResearch(ResearchProjectDef r)
        {
            if (r == null || r.defName == null) return false;
            for (int i = 0; i < ExcludedResearchDefNames.Length; i++)
            {
                if (r.defName == ExcludedResearchDefNames[i]) return true;
            }
            return false;
        }

        /// <summary>这个配方是否属于本 mod 的武器科技线（避免把原版武器也收进来）。</summary>
        private static bool IsMadeByBinguinResearch(RecipeDef r)
        {
            if (r.researchPrerequisite != null && r.researchPrerequisite.defName != null
                && r.researchPrerequisite.defName.StartsWith("Binguin_", StringComparison.Ordinal))
            {
                return true;
            }
            if (r.researchPrerequisites != null)
            {
                for (int i = 0; i < r.researchPrerequisites.Count; i++)
                {
                    ResearchProjectDef p = r.researchPrerequisites[i];
                    if (p != null && p.defName != null
                        && p.defName.StartsWith("Binguin_", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>取配方的产物（1.6 有 ProducedThingDef 就直接用，否则读 products 列表）。</summary>
        private static ThingDef ProducedDef(RecipeDef r)
        {
            if (r.ProducedThingDef != null) return r.ProducedThingDef;
            if (r.products != null)
            {
                for (int i = 0; i < r.products.Count; i++)
                {
                    ThingDefCountClass p = r.products[i];
                    if (p != null && p.thingDef != null) return p.thingDef;
                }
            }
            return null;
        }

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction)
        {
            List<ThingDef> pool = EligibleWeapons;
            if (pool == null || pool.Count == 0)
            {
                BinguinLogUtility.Log("作战商卖武器：可用武器池为空，本次不产武器。", severity: 1, isDebug: false);
                yield break;
            }

            // 2-5 把【不同】武器；池子比需求小时按池子大小取
            int want = weaponCountRange.RandomInRange;
            if (want > pool.Count) want = pool.Count;

            tmpChosen.Clear();
            for (int i = 0; i < want; i++)
            {
                ThingDef def;
                if (!TryPickUnchosen(pool, out def)) break;
                tmpChosen.Add(def);

                int n = countPerWeapon.RandomInRange;
                if (n <= 0) n = 1;
                IEnumerable<Thing> made = StockGeneratorUtility.TryMakeForStock(def, n, faction);
                if (made == null) continue;
                foreach (Thing t in made)
                {
                    yield return t;
                }
            }
        }

        /// <summary>从池子里随机取一个"这次还没选过"的武器。</summary>
        private bool TryPickUnchosen(List<ThingDef> pool, out ThingDef picked)
        {
            picked = null;
            // 池子不大（5 把），直接随机试几次；试不到就顺序找一个
            for (int attempt = 0; attempt < 24; attempt++)
            {
                ThingDef c = pool[Rand.Range(0, pool.Count)];
                if (!tmpChosen.Contains(c))
                {
                    picked = c;
                    return true;
                }
            }
            for (int i = 0; i < pool.Count; i++)
            {
                if (!tmpChosen.Contains(pool[i]))
                {
                    picked = pool[i];
                    return true;
                }
            }
            return false;
        }

        public override bool HandlesThingDef(ThingDef thingDef)
        {
            List<ThingDef> pool = EligibleWeapons;
            return pool != null && thingDef != null && pool.Contains(thingDef);
        }

        /// <summary>启动自检用：把当前生效的武器池打出来。</summary>
        public static string DescribePool()
        {
            List<ThingDef> pool = EligibleWeapons;
            if (pool == null || pool.Count == 0) return "(空)";
            List<string> names = new List<string>();
            for (int i = 0; i < pool.Count; i++) names.Add(pool[i].defName);
            return pool.Count + " 把：" + string.Join(", ", names.ToArray());
        }
    }
}
