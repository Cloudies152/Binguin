// ============================================================================
// 蟹笼 —— 自动捕鱼（进阶钓鱼学，奥德赛 DLC 专属）
//
// 机制：
//   - 每 15000 tick（6 小时）捕一次，对齐游戏日内 6:00 / 12:00 / 18:00 / 24:00
//   - ★ 2026-08-20 用户改定：**水域 或 冰面 可放置**（PlaceWorker 校验）；
//     · 放在水域中 → 按水域逻辑捕鱼（可 Gizmo「管理鱼群」手动调控，
//       与原版钓鱼一致的鱼群管理）；
//     · 放在冰面（terrain.IsIce，如冰盖地表）→ 无视 WaterBodyTracker
//       （冻结水体也被登记 Bodies），只出**海洋类（saltwater）鱼群**：
//       取当前 biome fishTypes 的 saltwater 表（20% 稀有/80% 普通加权），
//       回退全部 biome 并集 → 固定海鱼名单。
//   - 捕鱼来源：
//       · 恰好放在水域中（TryGetWaterBodyAt 成功）：从该水域捕鱼——
//         80% 普通鱼 / 20% 稀有鱼，鱼群枯竭则不产出；
//         捕获后 Notify_Fished 扣鱼群（与原版钓鱼一致，防止无限刷）。
//       · 放在水域外（冰面/陆地等任意位置）：从 DefDatabase 的 Fish
//         类目随机一条（不受任何水体状态限制）。
//   - 鱼生成在蟹笼所在格（GenPlace.TryPlaceThing Near 自动找可用格），
//     由搬运工搬走。
//   - PlaceWorker 由 BinguinDefPatches 挂载（XML 零自定义类型）。
//
// ★ 关键 API（1.6 实测，见交接文档）：Verse.WaterBody / WaterBodyTracker /
//   FishingUtility 全在 Assembly-CSharp，Odyssey 未激活时 XML 不加载
//   （loadFolders 门控），本 Comp 也不会挂载。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin
{
    public class CompProperties_BinguinCrabTrap : CompProperties
    {
        public int intervalTicks = 15000;   // 6 小时

        public CompProperties_BinguinCrabTrap()
        {
            compClass = typeof(CompBinguinCrabTrap);
        }
    }

    public class CompBinguinCrabTrap : ThingComp
    {
        private int nextCatchTick = -1;

        public CompProperties_BinguinCrabTrap Props
        {
            get { return (CompProperties_BinguinCrabTrap)props; }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<int>(ref nextCatchTick, "nextCatchTick", -1, false);
        }

        // ★ 性能优化（2026-08-20）：原 CompTick 每 tick 空转 → CompTickRare
        //   （每 250 tick ≈ 4 秒判定一次；6 小时刻度对齐语义不变，触发误差 < 4 秒）
        public override void CompTickRare()
        {
            base.CompTickRare();
            if (nextCatchTick < 0)
            {
                // 对齐到下一个 6 小时刻度（0/15000/30000/45000 = 0:00/6:00/12:00/18:00）
                nextCatchTick = ((GenTicks.TicksGame / Props.intervalTicks) + 1) * Props.intervalTicks;
            }
            if (GenTicks.TicksGame >= nextCatchTick)
            {
                nextCatchTick += Props.intervalTicks;
                CatchOnce();
            }
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            // 与 CatchOnce 同款判定：冰面优先，无视 WaterBodyTracker
            TerrainDef groundHere = parent.Position.GetTerrain(parent.Map);
            bool onIceHere = groundHere != null && groundHere.IsIce;
            WaterBody body;
            bool inWater = !onIceHere && parent.Map.waterBodyTracker.TryGetWaterBodyAt(parent.Position, out body);
            int ticksLeft = nextCatchTick - GenTicks.TicksGame;
            if (ticksLeft < 0)
            {
                ticksLeft = 0;
            }
            float hours = (float)ticksLeft / 2500f;
            string mode = inWater ? "Binguin_CompCrabTrap_01".Translate() : (onIceHere
                ? "Binguin_CompCrabTrap_02".Translate()
                : "Binguin_CompCrabTrap_03".Translate());
            return "Binguin_CompCrabTrap_04".Translate() + hours.ToString("0.0") + "Binguin_CompCrabTrap_05".Translate() + mode + "）";
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            // ★ 管理鱼群（2026-08-20 用户需求）：蟹笼在水域中时，可像原版钓鱼
            //   那样手动查看/调控本水域鱼群数量（+/- 按钮：放苗/捕捞）。
            //   冰面模式不依赖水域，无需管理 → 不显示。
            if (parent.Map != null && parent.Spawned)
            {
                TerrainDef groundG = parent.Position.GetTerrain(parent.Map);
                bool onIceG = groundG != null && groundG.IsIce;
                WaterBody bodyG = null;
                bool inWaterG = !onIceG
                    && parent.Map.waterBodyTracker.TryGetWaterBodyAt(parent.Position, out bodyG);
                if (inWaterG && bodyG != null)
                {
                    Command_Action manage = new Command_Action();
                    manage.defaultLabel = "Binguin_CompCrabTrap_06".Translate();
                    // ★ 2026-10-06：描述改为显示"鱼群 / 上限"与"捕捞下限"
                    //   （原描述写的是"手动调控鱼群数量"，那是被用户否掉的旧行为）
                    manage.defaultDesc = "Binguin_CompCrabTrap_11".Translate()
                        + bodyG.Population.ToString("0") + " / "
                        + bodyG.MaxPopulation.ToString("0")
                        + "Binguin_CompCrabTrap_12".Translate()
                        + MapComponent_BinguinWaterBodyLimits.GetLimit(bodyG).ToString("0")
                        + (MapComponent_BinguinWaterBodyLimits.CanCatchFrom(bodyG)
                            ? "Binguin_CompCrabTrap_13".Translate()
                            : "Binguin_CompCrabTrap_14".Translate());
                    manage.icon = ContentFinder<Texture2D>.Get("Things/Building/Binguin/CrabTrap", false);
                    WaterBody captured = bodyG;
                    manage.action = delegate
                    {
                        Find.WindowStack.Add(new Dialog_BinguinManageFish(captured));
                    };
                    yield return manage;
                }
            }
            if (Prefs.DevMode)
            {
                Command_Action catchNow = new Command_Action();
                catchNow.defaultLabel = "Binguin_CompCrabTrap_09".Translate();
                catchNow.defaultDesc = "Binguin_CompCrabTrap_10".Translate();
                catchNow.action = delegate { CatchOnce(); };
                yield return catchNow;
            }
        }

        private void CatchOnce()
        {
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }

            // ★ 2026-08-20 用户改定（关键）：判定「冰面」优先于「水域」——
            //   只要所在格地形是冰面（terrain.IsIce，如冰盖地表），就完全无视
            //   WaterBodyTracker（Odyssey 会把冻结水体也登记进 Bodies），
            //   直接走全球鱼种随机模式。
            TerrainDef ground = parent.Position.GetTerrain(map);
            bool onIce = ground != null && ground.IsIce;

            WaterBody body = null;
            bool inWater = !onIce && map.waterBodyTracker.TryGetWaterBodyAt(parent.Position, out body);

            ThingDef fishDef = null;
            if (inWater && body != null)
            {
                // 恰好放在水域中：从该水域捕鱼（与原版钓鱼一致，防无限刷）
                if (!body.HasFish || body.Population <= 0.01f)
                {
                    Log.Message("[冰鹅族] 蟹笼捕鱼：水域鱼群枯竭（Population="
                        + body.Population.ToString("0.0") + "），未捕到鱼。");
                    return;
                }
                // ★★ 2026-10-06 用户需求修正：「蟹笼的管理鱼群应该是**限制捕捞数量**
                //   （控制水域内数量）……我指的是限制**整个水域**的鱼类下限，
                //   比如到某个数字就不捕捞」。
                //   ⇒ 鱼群 ≤ 本水域的下限时**停止捕捞**，让鱼群自然恢复。
                //     下限由 `MapComponent_BinguinWaterBodyLimits` 按
                //     (地图, 水域根格) 存储 ⇒ **同一片水域的所有蟹笼共享同一个下限**。
                //   ★ 判定用的是 `CanCatchFrom`（内含 HasFish / Population>0 /
                //     Population > 下限 三项，与鱼池的 keepMinFish 判定同一套容差）。
                if (!MapComponent_BinguinWaterBodyLimits.CanCatchFrom(body))
                {
                    Log.Message("[冰鹅族] 蟹笼捕鱼：鱼群（" + body.Population.ToString("0.0")
                        + "）已降到本水域捕捞下限（"
                        + MapComponent_BinguinWaterBodyLimits.GetLimit(body).ToString("0")
                        + "）以下，本次不捕，等待恢复。");
                    return;
                }
                // 20% 稀有鱼，否则普通鱼
                ThingDef uncommon;
                if (Rand.Chance(0.2f) && body.UncommonFish.TryRandomElement(out uncommon))
                {
                    fishDef = uncommon;
                }
                else
                {
                    ThingDef common;
                    if (!body.CommonFishIncludingExtras.TryRandomElement(out common))
                    {
                        Log.Message("[冰鹅族] 蟹笼捕鱼：该水域没有可捕鱼种，跳过。");
                        return;
                    }
                    fishDef = common;
                }
                map.waterBodyTracker.Notify_Fished(parent.Position, 1f);
            }
            else
            {
                // ★ 水域外（冰面，2026-08-20 用户改定）：不依赖水体，
                //   只出**海洋类（saltwater）鱼群**——优先取当前地图 biome
                //   fishTypes 的 saltwater 表（20% 稀有/80% 普通，按 chance 加权），
                //   找不到时回退全部 biome 的 saltwater 并集，最后回退固定海鱼名单。
                fishDef = RandomSaltwaterFish(map);
                if (fishDef == null)
                {
                    Log.Message("[冰鹅族] 蟹笼捕鱼：找不到任何海洋鱼类定义，跳过。");
                    return;
                }
            }

            Thing fish = ThingMaker.MakeThing(fishDef);
            fish.stackCount = 1;
            GenPlace.TryPlaceThing(fish, parent.Position, map, ThingPlaceMode.Near, null);
            Log.Message("[冰鹅族] 蟹笼捕到鱼：" + fishDef.label
                + (inWater ? "（本水域）" : "（冰面模式：海洋鱼种）"));
        }

        // ---------- 冰面模式：海洋类（saltwater）鱼群随机 ----------

        private static ThingDef RandomSaltwaterFish(Map map)
        {
            // 1) 当前地图 biome 的 fishTypes（IceSheet/Tundra 等在 Odyssey 下都有
            //    saltwater 表）——80% 普通 / 20% 稀有，FishChance 加权。
            if (map != null && map.Biome != null && map.Biome.fishTypes != null)
            {
                BiomeFishTypes ft = map.Biome.fishTypes;
                List<FishChance> pool = Rand.Chance(0.2f)
                    ? ft.saltwater_Uncommon
                    : ft.saltwater_Common;
                if (pool == null || pool.Count == 0)
                {
                    pool = ft.saltwater_Common;
                }
                if (pool == null || pool.Count == 0)
                {
                    pool = ft.saltwater_Uncommon;
                }
                if (pool != null && pool.Count > 0)
                {
                    ThingDef picked = WeightedPickFish(pool);
                    if (picked != null)
                    {
                        return picked;
                    }
                }
            }

            // 2) 回退：扫描所有 biome 的 saltwater 表（并集）
            List<ThingDef> union = new List<ThingDef>();
            List<BiomeDef> biomes = DefDatabase<BiomeDef>.AllDefsListForReading;
            for (int b = 0; b < biomes.Count; b++)
            {
                BiomeDef bd = biomes[b];
                if (bd == null || bd.fishTypes == null)
                {
                    continue;
                }
                AddPoolFish(union, bd.fishTypes.saltwater_Common);
                AddPoolFish(union, bd.fishTypes.saltwater_Uncommon);
            }
            if (union.Count > 0)
            {
                return union.RandomElement();
            }

            // 3) 最后回退：固定海鱼名单（原版 Odyssey 鱼种）
            string[] fallbackNames = new string[]
            {
                "Fish_Salmon", "Fish_Cod", "Fish_Bluefish",
                "Fish_Dogfish", "Fish_Marlin", "Fish_Flounder", "Fish_Tuna"
            };
            List<ThingDef> fallback = new List<ThingDef>();
            for (int i = 0; i < fallbackNames.Length; i++)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(fallbackNames[i]);
                if (def != null)
                {
                    fallback.Add(def);
                }
            }
            if (fallback.Count > 0)
            {
                return fallback.RandomElement();
            }
            return null;
        }

        // FishChance 按 chance 加权随机（原版结构：fishDef + chance）
        private static ThingDef WeightedPickFish(List<FishChance> list)
        {
            float total = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].fishDef != null)
                {
                    total += list[i].chance;
                }
            }
            if (total <= 0f)
            {
                return null;
            }
            float roll = Rand.Value * total;
            float acc = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                FishChance fc = list[i];
                if (fc.fishDef == null)
                {
                    continue;
                }
                acc += fc.chance;
                if (roll <= acc)
                {
                    return fc.fishDef;
                }
            }
            return list[list.Count - 1].fishDef;
        }

        private static void AddPoolFish(List<ThingDef> target, List<FishChance> pool)
        {
            if (pool == null)
            {
                return;
            }
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i].fishDef != null && !target.Contains(pool[i].fishDef))
                {
                    target.Add(pool[i].fishDef);
                }
            }
        }
    }
}
