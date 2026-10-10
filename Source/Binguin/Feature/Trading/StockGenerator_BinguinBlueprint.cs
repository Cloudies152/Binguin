// ============================================================================
// 冰鹅族蓝图（原版科技图纸 Techprint）库存生成器（2026-09 用户需求）
//
//   「霰雪无垠」研究需要 1 张蓝图（科技图纸，市价 3000）。原版会按
//   ResearchProjectDef.techprintCount > 0 自动生成 ThingDef：
//     defName = "Techprint_" + 研究defName
//     → Techprint_Binguin_ResearchEndlessSnow
//   （ThingDefGenerator_Techprints，仅在安装 Royalty 时生成。）
//
//   本生成器把这张蓝图放进商人库存：
//     · 稀有贸易商（Caravan_Outlander_Exotic 稀有品商队 / Orbital_Exotic
//       轨道稀有品商）：chance 概率出现
//     · 冰鹅族据点（Base_Outlander_Standard，仅 faction=Binguin）：70% 概率
//   实例由 Patches/Feature/Trading/BlueprintStocks_Binguin.xml 声明，Royalty 门控。
//
//   ★ 原版 StockGenerator_Techprints 没有任何可配字段（概率写死在内部），
//     所以这里自写一个带 chance / 派系过滤的生成器。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Trading
{
    public class StockGenerator_BinguinBlueprint : StockGenerator
    {
        // 蓝图 ThingDef（留空 = 按研究 defName 自动查找 Techprint_ 前缀 def）
        public ThingDef blueprintDef;
        // 出现概率 0~1
        public float chance = 0.7f;
        // 仅当商人所属派系为该 defName 时才出售（null/空 = 不限派系）
        public string onlyFactionDefName;

        private const string BlueprintResearchDefName = "Binguin_ResearchEndlessSnow";
        private const string BlueprintThingDefName = "Techprint_Binguin_ResearchEndlessSnow";

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            ThingDef def = ResolveBlueprintDef();
            if (def == null)
            {
                yield break; // 未装 Royalty（没有科技图纸系统）→ 不产出
            }
            if (!string.IsNullOrEmpty(onlyFactionDefName))
            {
                if (faction == null || faction.def == null
                    || faction.def.defName != onlyFactionDefName)
                {
                    yield break;
                }
            }
            if (!Rand.Chance(chance))
            {
                yield break;
            }
            int count = countRange.RandomInRange;
            if (count < 1)
            {
                count = 1;
            }
            for (int i = 0; i < count; i++)
            {
                Thing t = null;
                try
                {
                    t = ThingMaker.MakeThing(def, null);
                }
                catch (Exception e)
                {
                    BinguinLogUtility.Log("生成蓝图物品失败：" + e.Message, severity: 1, isDebug: false);
                }
                if (t != null)
                {
                    yield return t;
                }
            }
        }

        public override bool HandlesThingDef(ThingDef td)
        {
            return td != null && td == ResolveBlueprintDef();
        }

        // ★ 2026-09：原实现每次 HandlesThingDef 都查一次 DefDatabase，且在第一次
        //   查询失败时又查了一次 "Techprint_" + 研究名——而该串与 BlueprintThingDefName
        //   完全相同（见上方常量），第一次失败第二次必然也失败（还白拼一个字符串）。
        //   现在只查一次并缓存结果（含 null：没装 Royalty 时不必反复查）。
        private static ThingDef cachedResolvedDef;
        private static bool resolvedLookedUp;

        private ThingDef ResolveBlueprintDef()
        {
            try
            {
                if (blueprintDef != null)
                {
                    return blueprintDef;
                }
                if (!resolvedLookedUp)
                {
                    resolvedLookedUp = true;
                    cachedResolvedDef = DefDatabase<ThingDef>.GetNamedSilentFail(BlueprintThingDefName);
                }
                return cachedResolvedDef;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
