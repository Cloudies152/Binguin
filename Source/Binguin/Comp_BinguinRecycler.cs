// ============================================================================
// 冰鹅族垃圾回收器 —— Comp（子类化原版 CompAtomizer）
//
// 2026-08-19 用户需求（v2 修订）：占地 2 格、100铁20玻璃钢4零部件、200W，
//   放置有毒垃圾（Wastepack），最多 25 个；每 8 小时（20000 tick）回收 1 个，
//   每次产出 5-10 个随机矿物：铁40% / 玻璃钢20% / 铀20% / 金10% / 银10%。
//
// ★ 方案：继承 RimWorld.CompAtomizer（Biotech 原子化器官方 Comp）——
//   - 容器/搬运/电力/动画/存档继承原版（CompThingContainer 基类：
//     innerContainer public、ContainedThing/Empty/TotalStackCount 属性）
//   - 殖民者自动搬运 Wastepack 进来（原版 WorkGiver_HaulToAtomizer 按
//     CompProperties_Atomizer.thingDef 匹配，子类自动被识别）
//   - DoAtomize 不是 virtual（1.6 反射确认），故自己 override CompTick
//     （virtual）实现计时+回收+产出，不再调用 base 的定时销毁
// ★ 参数：stackLimit=25、ticksPerAtomize=20000（8 小时处理 1 个垃圾）
// ★ XML 零自定义类型：compClass 由 BinguinDefPatches 代码挂载
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin
{
    public class CompBinguinRecycler : CompAtomizer
    {
        // 回收计时（覆盖原版 ticksAtomized 的使用：不依赖其 private 字段）
        private int recycleTicks = 0;

        // 每次回收产出的矿物表：defName -> 权重（总权重 100）
        private static readonly List<KeyValuePair<string, int>> MineralTable =
            new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>("Steel", 40),
                new KeyValuePair<string, int>("Plasteel", 20),
                new KeyValuePair<string, int>("Uranium", 20),
                new KeyValuePair<string, int>("Gold", 10),
                new KeyValuePair<string, int>("Silver", 10)
            };

        private static readonly Dictionary<string, ThingDef> MineralDefCache =
            new Dictionary<string, ThingDef>();

        // 每次回收产出数量范围 5-10（2026-08-19 用户定稿）
        private const int MinOrePerRecycle = 5;
        private const int MaxOrePerRecycle = 10;
        // 「高效垃圾回收 ProPlusMax」完成后：产出提高 50%（8-15，2026-08-20 用户需求）
        private const int MinOreProMax = 8;
        private const int MaxOreProMax = 15;

        private static ResearchProjectDef proMaxResearchCache;

        private CompPowerTrader powerComp;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.TryGetComp<CompPowerTrader>();
        }

        public override void CompTick()
        {
            // 不调用 base.CompTick()（那是原版原子化器的定时销毁逻辑）
            // 容器自身的维护（如 ThingOwner tick）由基类容器机制处理；
            // 这里实现自己的计时回收。
            if (Empty || powerComp == null || !powerComp.PowerOn)
            {
                recycleTicks = 0;
                return;
            }
            recycleTicks++;
            if (recycleTicks >= Props.ticksPerAtomize)
            {
                recycleTicks = 0;
                DoRecycleOne();
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<int>(ref recycleTicks, "recycleTicks", 0, false);
        }

        // 回收 1 个垃圾：从容器取出 1 个 Wastepack 销毁 + 产出随机矿物
        private void DoRecycleOne()
        {
            try
            {
                Thing waste = ContainedThing;
                if (waste == null || waste.stackCount <= 0)
                {
                    return;
                }
                // 从容器扣 1 个
                if (waste.stackCount == 1)
                {
                    innerContainer.Remove(waste);
                    waste.Destroy();
                }
                else
                {
                    waste.stackCount--;
                }

                Map map = parent.MapHeld;
                if (map == null)
                {
                    return;
                }
                int count = ProMaxActive
                    ? Rand.RangeInclusive(MinOreProMax, MaxOreProMax)
                    : Rand.RangeInclusive(MinOrePerRecycle, MaxOrePerRecycle);
                string defName = RollMineral();
                ThingDef def = GetMineralDef(defName);
                if (def == null)
                {
                    Log.Warning("[冰鹅族] 垃圾回收器找不到矿物 def: " + defName);
                    return;
                }
                Thing ore = ThingMaker.MakeThing(def);
                ore.stackCount = count;
                GenPlace.TryPlaceThing(ore, parent.Position, map, ThingPlaceMode.Near);
                Log.Message("[冰鹅族] 垃圾回收器产出 " + count + "x" + def.label + "（" + defName
                    + (ProMaxActive ? "，ProPlusMax 已生效 +50%" : "") + "）");
            }
            catch (System.Exception e)
            {
                Log.Warning("[冰鹅族] 垃圾回收器产出矿物异常: " + e.Message);
            }
        }

        // 「高效垃圾回收 ProPlusMax」研究是否已完成（2000 点，前置垃圾回收；
        // 完成后回收矿物产出 +50%。defName 与 XML 对应，缺省视为未完成）
        private static bool ProMaxActive
        {
            get
            {
                if (proMaxResearchCache == null)
                {
                    proMaxResearchCache = DefDatabase<ResearchProjectDef>
                        .GetNamedSilentFail("Binguin_ResearchRecyclingProMax");
                }
                return proMaxResearchCache != null && proMaxResearchCache.IsFinished;
            }
        }

        // 按权重掷骰：铁40/玻璃钢20/铀20/金10/银10
        private static string RollMineral()
        {
            int roll = Rand.RangeInclusive(1, 100);
            int acc = 0;
            for (int i = 0; i < MineralTable.Count; i++)
            {
                acc += MineralTable[i].Value;
                if (roll <= acc)
                {
                    return MineralTable[i].Key;
                }
            }
            return "Steel";
        }

        private static ThingDef GetMineralDef(string defName)
        {
            ThingDef def;
            if (!MineralDefCache.TryGetValue(defName, out def))
            {
                def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                MineralDefCache[defName] = def;
            }
            return def;
        }

        // 开发者按钮：立即回收（跳过一次处理周期，产出矿物）
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            if (DebugSettings.godMode || Prefs.DevMode)
            {
                Command_Action recycleNow = new Command_Action
                {
                    defaultLabel = "Binguin_CompRecycler_01".Translate(),
                    defaultDesc = "Binguin_CompRecycler_02".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Things/Building/Binguin/Recycler", false),
                    action = delegate
                    {
                        if (!Empty)
                        {
                            DoRecycleOne();
                        }
                        else
                        {
                            Messages.Message("Binguin_CompRecycler_03".Translate(), MessageTypeDefOf.NeutralEvent, false);
                        }
                    }
                };
                yield return recycleNow;
            }
        }
    }
}
