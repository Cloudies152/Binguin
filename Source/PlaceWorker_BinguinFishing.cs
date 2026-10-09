// ============================================================================
// 进阶钓鱼学的放置校验（蟹笼 / 打窝用具）
//
// PlaceWorker_BinguinWaterArea（蟹笼）：
//   - 所在格是水域（GridsUtility.GetWaterBodyType != None）→ 允许
//   - 地图完全无水域（waterBodyTracker.Bodies 为空，如冰盖）→ 允许
//     （冰盖蟹笼 = "随机从当前任意水域捕捉"模式）
//   - 其他情况拒绝。
//
// PlaceWorker_BinguinBaitLimit（打窝用具）：
//   - 所在格必须在水域中（TryGetWaterBodyAt 成功）
//   - 该水域内已有打窝用具 < 2（每个水域最多放置 2 个）
//
// ★ placeWorkers 是 List<Type>，由 BinguinDefPatches 静态构造 Add
//   （XML 零自定义类型原则）。
// ============================================================================

using RimWorld;
using Verse;

namespace Binguin
{
    // 蟹笼：水域 或 冰面 可放置（2026-08-20 用户改定）
    //   - 原设计：仅水域 / 地图完全无水域（冰盖）才可放；
    //     实测冰盖地图 waterBodyTracker.Bodies 非空（冻结水体也被登记）
    //     → 非水域格子全部被拒 → 「冰盖上蟹笼无法放置」。
    //   - 用户改定：**放在水域 → 按水域逻辑捕鱼；放在冰面（IsIce）→
    //     无视水域直接运行（全球鱼种随机）**。故允许 = 水域 or 冰面。
    public class PlaceWorker_BinguinWaterArea : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            if (GridsUtility.GetWaterBodyType(loc, map) != WaterBodyType.None)
            {
                return true;
            }
            TerrainDef terrain = loc.GetTerrain(map);
            if (terrain != null && terrain.IsIce)
            {
                return true;
            }
            return new AcceptanceReport("Binguin_PlaceWorkerFishing_01".Translate());
        }
    }

    // 打窝用具：必须在水域中，且每个水域最多 2 个
    public class PlaceWorker_BinguinBaitLimit : PlaceWorker
    {
        private const int MaxPerWaterBody = 2;

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            WaterBody body;
            if (!map.waterBodyTracker.TryGetWaterBodyAt(loc, out body))
            {
                return new AcceptanceReport("Binguin_PlaceWorkerFishing_02".Translate());
            }
            int count = 0;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (t == thingToIgnore || t == thing)
                {
                    continue;
                }
                if (t.def == null || t.def.defName != "Binguin_FishingBait")
                {
                    continue;
                }
                WaterBody other;
                if (map.waterBodyTracker.TryGetWaterBodyAt(t.Position, out other) && other == body)
                {
                    count++;
                    if (count >= MaxPerWaterBody)
                    {
                        return new AcceptanceReport("Binguin_PlaceWorkerFishing_03".Translate() + MaxPerWaterBody + "Binguin_PlaceWorkerFishing_04".Translate());
                    }
                }
            }
            return true;
        }
    }
}
