// ============================================================================
// 采切石一体机 —— 深钻井式自动采石 + 切石
//
// 机制（2026-08-18 用户要求）：
//   通电后每隔 workIntervalTicks（默认 5000 = 2 小时）凭空采出一块石料：
//     - 建筑建在冰面/冰鹅地板上 → 产出「冰岩」（Binguin_IceRock）
//     - 否则 → 产出随机岩石块（ChunkGranite/Sandstone/Limestone/Slate/Marble）
//   切石模式开启时，采出的石料直接切成砖：
//     - 冰岩 → 冰岩砖（Binguin_IceRockBrick，bricksPerStone 块）
//     - 岩石块 → 石砖（Blocks，bricksPerStone 块）
//   产物就近掉落（GenPlace.TryPlaceThing，自动堆叠），由搬运工搬走。
//
// ★ 用 nextWorkTick（精确 tick 计时）而非 IsHashIntervalTick（哈希对齐可能漂移），
//   并在建筑检查面板显示「下一块产出倒计时」，dev 模式提供「立即产出」按钮。
//   每次产出/跳过原因写日志（[冰鹅族] 前缀），便于排查。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

using Binguin.Helper;

namespace Binguin.Feature.Production
{
    public class CompProperties_BinguinAutoStone : CompProperties
    {
        public int workIntervalTicks = 5000;   // 2 小时一块（1 小时 = 2500 tick）
        public int bricksPerStone = 20;

        public CompProperties_BinguinAutoStone()
        {
            compClass = typeof(CompBinguinAutoStone);
        }
    }

    public class CompBinguinAutoStone : ThingComp
    {
        public bool cuttingEnabled = true;
        private int nextWorkTick = -1;
        private int stoneTypeIndex = -1;   // 普通地面的石头类型（-1 = 首次工作时随机定，之后固定）

        // 普通地面固定石头类型（板岩/石灰岩/大理石/花岗岩/砂岩，用户 2026-08-18 指定）
        private static readonly string[] StoneTypes = new string[]
        {
            "Slate", "Limestone", "Marble", "Granite", "Sandstone"
        };

        public CompProperties_BinguinAutoStone Props
        {
            get { return (CompProperties_BinguinAutoStone)props; }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<bool>(ref cuttingEnabled, "cuttingEnabled", true, false);
            Scribe_Values.Look<int>(ref nextWorkTick, "nextWorkTick", -1, false);
            Scribe_Values.Look<int>(ref stoneTypeIndex, "stoneTypeIndex", -1, false);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (nextWorkTick < 0)
            {
                nextWorkTick = GenTicks.TicksGame + Props.workIntervalTicks;
            }
            if (GenTicks.TicksGame >= nextWorkTick)
            {
                nextWorkTick = GenTicks.TicksGame + Props.workIntervalTicks;
                Work();
            }
        }

        // 建筑检查面板：显示下一块产出倒计时
        // ★ 2026-09 性能：本节每帧被调用（选中时 60 次/秒），原来每次都做
        //   TryGetComp（遍历 comps 列表）+ 两次 DefDatabase 查询（石头/石砖）。
        //   power comp 在 PostSpawnSetup 时取一次，def 查询走懒加载缓存。
        private CompPowerTrader powerCompCache;

        public override string CompInspectStringExtra()
        {
            if (powerCompCache == null)
            {
                powerCompCache = parent.TryGetComp<CompPowerTrader>();
            }
            CompPowerTrader power = powerCompCache;
            if (power != null && !power.PowerOn)
            {
                return "Binguin_CompAutoStone_01".Translate();
            }
            int ticksLeft = nextWorkTick - GenTicks.TicksGame;
            if (ticksLeft < 0)
            {
                ticksLeft = 0;
            }
            float hours = (float)ticksLeft / 2500f;
            string mode = cuttingEnabled ? "Binguin_CompAutoStone_02".Translate() : "Binguin_CompAutoStone_03".Translate();
            // ★ 2026-08-19 用户要求：标注一体机会产出什么石头
            //   （冰面/冰鹅地板 → 冰岩；普通地面 → 首次工作时随机固定的石头类型）
            string stoneInfo;
            bool onIceFloor = IsOnIce(parent.Map);
            if (onIceFloor)
            {
                stoneInfo = cuttingEnabled ? "Binguin_CompAutoStone_04".Translate() : "Binguin_CompAutoStone_05".Translate();
            }
            else if (stoneTypeIndex >= 0)
            {
                string type = StoneTypes[stoneTypeIndex];
                ThingDef stoneDef = CachedDef(type);
                stoneInfo = (stoneDef != null ? stoneDef.label : type)
                    + (cuttingEnabled ? " → " + BlocksLabel(type) : "");
            }
            else
            {
                stoneInfo = "Binguin_CompAutoStone_06".Translate();
            }
            return "Binguin_CompAutoStone_07".Translate() + hours.ToString("0.0") + "Binguin_CompAutoStone_08".Translate() + mode + "）\n"
                + "Binguin_CompAutoStone_09".Translate() + stoneInfo;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            // 切石模式开关
            Command_Toggle toggle = new Command_Toggle();
            toggle.defaultLabel = "Binguin_CompAutoStone_10".Translate();
            toggle.defaultDesc = "Binguin_CompAutoStone_11".Translate();
            toggle.isActive = delegate { return cuttingEnabled; };
            toggle.toggleAction = delegate { cuttingEnabled = !cuttingEnabled; };
            toggle.hotKey = null;
            yield return toggle;

            // dev：立即产出一块
            if (Prefs.DevMode)
            {
                Command_Action produce = new Command_Action();
                produce.defaultLabel = "Binguin_CompAutoStone_12".Translate();
                produce.defaultDesc = "Binguin_CompAutoStone_13".Translate();
                produce.action = delegate { Work(); };
                yield return produce;
            }
        }

        // 深钻井式：凭空采出一块石料（冰面上出冰岩，否则出岩石块）
        private void Work()
        {
            Map map = parent.Map;
            if (map == null)
            {
                BinguinLogUtility.Log("一体机 Work：无地图，跳过。");
                return;
            }
            CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn)
            {
                BinguinLogUtility.Log("一体机 Work：未通电，跳过。");
                return;
            }

            bool onIce = IsOnIce(map);
            if (onIce)
            {
                ThingDef iceRock = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_IceRock");
                if (iceRock == null)
                {
                    BinguinLogUtility.Log("一体机 Work：找不到 Binguin_IceRock！", severity: 1, isDebug: false);
                    return;
                }
                if (cuttingEnabled)
                {
                    ThingDef brick = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_IceRockBrick");
                    if (brick == null)
                    {
                        BinguinLogUtility.Log("一体机 Work：找不到 Binguin_IceRockBrick！", severity: 1, isDebug: false);
                        return;
                    }
                    SpawnProduct(brick, Props.bricksPerStone, map);
                    BinguinLogUtility.Log("一体机产出：" + Props.bricksPerStone + " 冰岩砖（冰面，切石模式）。");
                }
                else
                {
                    SpawnProduct(iceRock, 1, map);
                    BinguinLogUtility.Log("一体机产出：1 冰岩（冰面，采石模式）。");
                }
            }
            else
            {
                // 普通地面：固定一种石头（首次随机，之后一直产这种）
                if (stoneTypeIndex < 0)
                {
                    stoneTypeIndex = Rand.RangeInclusive(0, StoneTypes.Length - 1);
                    BinguinLogUtility.Log("一体机确定石头类型：" + StoneTypes[stoneTypeIndex] + "（此后固定产出）。");
                }
                string type = StoneTypes[stoneTypeIndex];
                if (cuttingEnabled)
                {
                    // 对应石砖：BlocksSlate / BlocksLimestone / BlocksMarble / BlocksGranite / BlocksSandstone
                    ThingDef bricks = DefDatabase<ThingDef>.GetNamedSilentFail("Blocks" + type);
                    if (bricks == null)
                    {
                        BinguinLogUtility.Log("一体机 Work：找不到 Blocks" + type + "！", severity: 1, isDebug: false);
                        return;
                    }
                    SpawnProduct(bricks, Props.bricksPerStone, map);
                    BinguinLogUtility.Log("一体机产出：" + Props.bricksPerStone + " " + bricks.label + "（普通地面，切石模式）。");
                }
                else
                {
                    ThingDef chunk = DefDatabase<ThingDef>.GetNamedSilentFail("Chunk" + type);
                    if (chunk == null)
                    {
                        BinguinLogUtility.Log("一体机 Work：找不到 Chunk" + type + "！", severity: 1, isDebug: false);
                        return;
                    }
                    SpawnProduct(chunk, 1, map);
                    BinguinLogUtility.Log("一体机产出：1 " + chunk.label + "（普通地面，采石模式）。");
                }
            }
        }

        private bool IsOnIce(Map map)
        {
            TerrainDef terrain = parent.Position.GetTerrain(map);
            if (terrain == null)
            {
                return false;
            }
            string name = terrain.defName;
            return name.StartsWith("Ice", System.StringComparison.Ordinal)
                || name.StartsWith("Binguin_", System.StringComparison.Ordinal);
        }


        // ★ 2026-09 性能：检视面板每帧都要取"石头/石砖"的 ThingDef，原来每次都
        //   查一次 DefDatabase（石砖那条还要先拼字符串）。def 名是常量 → 共用一份
        //   懒加载缓存（空结果也缓存：没这个 def 就不必反复查找）。
        private static readonly Dictionary<string, ThingDef> defCache = new Dictionary<string, ThingDef>();

        private static ThingDef CachedDef(string defName)
        {
            ThingDef def;
            if (defCache.TryGetValue(defName, out def))
            {
                return def;
            }
            def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            defCache[defName] = def;
            return def;
        }

        // 石头类型 → 石砖 label（BlocksSlate/BlocksLimestone/BlocksMarble/BlocksGranite/BlocksSandstone）
        private static string BlocksLabel(string type)
        {
            ThingDef def = CachedDef("Blocks" + type);
            return def != null ? def.label : ("Blocks" + type);
        }

        private void SpawnProduct(ThingDef def, int count, Map map)
        {
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            GenPlace.TryPlaceThing(thing, parent.Position, map, ThingPlaceMode.Near, null);
        }
    }
}
