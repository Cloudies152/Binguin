using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Binguin.Helper;

namespace Binguin.Feature.Fishing
{
    /// <summary>
    /// 鱼池水面：把鱼池占据的格子换成原版水地形，由游戏自己渲染水体。
    /// 建筑贴图只画岸边石，中间透明，水面完全来自地形。
    ///
    /// 存档行为：
    ///   · 放置时记录每格「原地形」，写进 comp 一起存盘；
    ///   · 拆除 / 被毁时按记录还原；
    ///   · 读档时若已有记录，重新铺水（幂等）。
    /// </summary>
    public class CompProperties_BinguinPondWater : CompProperties
    {
        /// <summary>水面地形，留空时用原版 WaterShallow。</summary>
        public TerrainDef terrainDef;

        public CompProperties_BinguinPondWater()
        {
            compClass = typeof(CompBinguinPondWater);
        }
    }

    public class CompBinguinPondWater : ThingComp
    {
        /// <summary>被替换格子的索引（IndexedRoofAt 用 CellIndicesUtility）。</summary>
        private List<int> cells = new List<int>();

        /// <summary>与 cells 一一对应的原地形 defName。</summary>
        private List<string> originalTerrains = new List<string>();

        private bool applied;

        private CompProperties_BinguinPondWater Props
        {
            get { return (CompProperties_BinguinPondWater)props; }
        }

        private TerrainDef WaterDef
        {
            get
            {
                if (Props.terrainDef != null)
                {
                    return Props.terrainDef;
                }

                return DefDatabase<TerrainDef>.GetNamedSilentFail("WaterShallow");
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (parent.Map == null)
            {
                return;
            }

            TerrainDef water = WaterDef;
            if (water == null)
            {
                BinguinLogUtility.WarningOnce(
                    "鱼池水面：找不到 WaterShallow 地形，鱼池将保持陆地。", 962301);
                return;
            }

            if (respawningAfterLoad)
            {
                // 读档：记录还在就直接重新铺水（地图本来就存的是水，幂等）
                if (cells.Count > 0)
                {
                    Apply(water);
                }
                return;
            }

            Record();
            Apply(water);
        }

        /// <summary>记录当前每格地形，并铺水。</summary>
        private void Record()
        {
            cells.Clear();
            originalTerrains.Clear();

            Map map = parent.Map;
            foreach (IntVec3 c in parent.OccupiedRect())
            {
                if (!c.InBounds(map))
                {
                    continue;
                }

                TerrainDef t = map.terrainGrid.TerrainAt(c);
                cells.Add(map.cellIndices.CellToIndex(c));
                originalTerrains.Add(t != null ? t.defName : null);
            }
        }

        private void Apply(TerrainDef water)
        {
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 c = map.cellIndices.IndexToCell(cells[i]);
                if (c.InBounds(map))
                {
                    map.terrainGrid.SetTerrain(c, water);
                }
            }

            applied = true;
        }

        /// <summary>供建筑类在拆除/被毁时调用。</summary>
        public void RestoreNow()
        {
            if (parent != null)
            {
                Restore(parent.MapHeld);
            }
        }

        /// <summary>还原原地形。只执行一次。</summary>
        private void Restore(Map map)
        {
            if (!applied || map == null || originalTerrains.Count == 0)
            {
                return;
            }

            for (int i = 0; i < cells.Count && i < originalTerrains.Count; i++)
            {
                string name = originalTerrains[i];
                if (name == null)
                {
                    continue;
                }

                TerrainDef t = DefDatabase<TerrainDef>.GetNamedSilentFail(name);
                if (t == null)
                {
                    continue;
                }

                IntVec3 c = map.cellIndices.IndexToCell(cells[i]);
                if (c.InBounds(map))
                {
                    map.terrainGrid.SetTerrain(c, t);
                }
            }

            applied = false;
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            Restore(previousMap);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref cells, "binguinPondWaterCells", LookMode.Value);
            Scribe_Collections.Look(ref originalTerrains, "binguinPondWaterTerrains", LookMode.Value);

            if (cells == null)
            {
                cells = new List<int>();
            }
            if (originalTerrains == null)
            {
                originalTerrains = new List<string>();
            }
        }
    }
}
