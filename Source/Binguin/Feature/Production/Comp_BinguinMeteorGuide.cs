// ============================================================================
// 冰鹅族陨石引导器 —— Comp（星空眺望科技）v2
//
// 2026-08-19 用户需求（星空眺望 2000 点，前置环境保护）：
//   - 建筑：陨石引导器，3x3，露天放置（盖屋顶后无法使用）
//   - ★ v2 修正：需要殖民者进行研究工作才能涨进度（同地质扫描仪，
//     吃研究速度 ResearchSpeed 加成）——继承 CompScannerMineralsDeep
//     复用原版工作管线（WorkGiver_OperateScanner + JobDef OperateScanner）
//   - 进度每前进 10% 判定一次发现陨石，发现概率 = 当前进度百分比
//   - 进度到 100% 保底发现（100% 需要 180,000 tick 基础工作量）
//   - 发现的陨石存入仪器（最多 20 颗），10 天后过期
//   - 陨石类型概率：铁60% / 铀15% / 玻璃钢10% / 零部件10% / 银2% / 金2% / 翡翠1%
//   - ★ v2：发现陨石时弹出信件（Letter）
//   - 点击「引导陨石」→ 地图选点 → 1-2 小时内（3600~7200 tick）坠落
//   - ★ v2：dev 按钮「立即发现陨石」
//
// ★ 1.6 机制（反射确认）：
//   - CompScanner.Used(pawn)：daysWorkingSinceLastFinding += 1/lastUserSpeed
//     （lastUserSpeed = pawn 的 scanSpeedStat = ResearchSpeed）
//     → 调用 TickDoesFind（virtual，可 override）→ true 时 DoFind(pawn)（virtual）
//   - CanUseNow：有电 + 露天 + 非禁止 + 玩家派系（继承即可）
//   - WorkGiver_OperateScanner：TryGetComp<CompScanner>()（子类匹配）+ scannerDef
//   - 进度实现：override TickDoesFind —— 以 "workingDays" 累积进度
//     （1/speed 天/次），10% 判定制 + 100% 保底
// ★ 自定义类型由对应功能的 XML 声明，使用完整命名空间和程序集名。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

using Binguin.Feature.Rods;

using Binguin.Helper;

namespace Binguin.Feature.Production
{
    // 注：运行时 props 实例是 XML 里的 CompProperties_ScannerMineralsDeep
    // （PatchMeteorGuide 只改 compClass），自定义数值全部用常量（见 Comp 内），
    // 不再需要自定义属性类。

    // 一颗已发现的陨石（存档数据）
    public class BinguinMeteorRecord : IExposable
    {
        public string mineralDefName;
        public int expireTick = -1;

        public void ExposeData()
        {
            Scribe_Values.Look<string>(ref mineralDefName, "mineralDefName", null, false);
            Scribe_Values.Look<int>(ref expireTick, "expireTick", -1, false);
        }

        public bool Expired
        {
            get { return expireTick >= 0 && GenTicks.TicksGame >= expireTick; }
        }
    }

    // ★ v5 修复：继承 CompScanner（基类）而非 CompDeepScanner！
    //   CompDeepScanner.get_CanUseNow 会检查 map.Biome.hasBedrock——
    //   冰盖（IceSheet）无基岩 → 报 "CannotUseScannerNoBedrock"
    //   （「无法在该群落发掘资源」），建造/使用时弹提示。
    //   v4 曾 override CanUseNow 跳过 bedrock 但建造时仍有提示；
    //   v5 干脆继承 CompScanner 基类（根本没有 bedrock 检查，
    //   CanUseNow 只查 已生成+有电+露天+非禁止+玩家派系），彻底消除提示。
    public class CompBinguinMeteorGuide : CompScanner
    {
        private float progressDays = 0f;           // 累计有效工作天数（吃研究速度）
        private int lastStep = 0;                  // 已判定到的 10% 步数
        private bool guaranteedDone = false;       // 本轮 100% 保底是否已发
        private List<BinguinMeteorRecord> storedMeteors =
            new List<BinguinMeteorRecord>();

        // ★ 自定义数值（常量，避免 props cast 问题——运行时 props 是原版类型）
        private const float FullProgressDays = 3f;       // 满进度标准工作天数（speed=1）
        private const int CheckSteps = 10;               // 每 10% 判定一次
        private const int MaxStoredMeteors = 20;         // 存储上限
        private const int MeteorExpireTicks = 600000;    // 10 天过期

        public float ProgressPercent
        {
            get { return Mathf.Clamp01(progressDays / FullProgressDays); }
        }

        public int StoredCount
        {
            get { return storedMeteors.Count; }
        }

        // ★ 进度判定（被 CompScanner.Used 每 tick 调用）：
        //   daysWorkingSinceLastFinding 由基类维护（1/speed 累积），
        //   我们用它换算进度；按 10% 步进判定，100% 保底。
        protected override bool TickDoesFind(float currentSpeed)
        {
            try
            {
                // 移除过期陨石
                if (storedMeteors.Count > 0 && GenTicks.TicksGame % 600 == 0)
                {
                    storedMeteors.RemoveAll(m => m.Expired);
                }

                progressDays = base.daysWorkingSinceLastFinding;
                if (progressDays < 0f)
                {
                    progressDays = 0f;
                }

                // 满进度 → 保底
                if (progressDays >= FullProgressDays)
                {
                    if (!guaranteedDone)
                    {
                        guaranteedDone = true;
                        return true;
                    }
                    return false;
                }

                // 每 10% 判定一次：概率 = 当前进度百分比
                int step = (int)(ProgressPercent * CheckSteps);
                if (step > lastStep)
                {
                    lastStep = step;
                    int percent = (int)(ProgressPercent * 100f);
                    return Rand.Chance(percent / 100f);
                }
                return false;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("陨石引导器进度判定异常: " + e.Message, severity: 1, isDebug: false);
                return false;
            }
        }

        // dev 按钮用：公开包装（DoFind 是 protected）
        public void ForceDiscover()
        {
            DoFind(null);
        }

        // ★ 发现陨石：掷类型 + 存储 + 信件（由基类 Used 在 TickDoesFind true 时调用）
        protected override void DoFind(Pawn finder)
        {
            try
            {
                if (StoredCount >= MaxStoredMeteors)
                {
                    Messages.Message("Binguin_CompMeteorGuide_01".Translate(),
                        new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                    return;
                }
                string mineral = RollMineral();
                BinguinMeteorRecord rec = new BinguinMeteorRecord
                {
                    mineralDefName = mineral,
                    expireTick = GenTicks.TicksGame + MeteorExpireTicks
                };
                storedMeteors.Add(rec);

                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(mineral);
                string label = def != null ? def.label : mineral;

                // ★ 弹信件（用户要求）
                string letterLabel = "Binguin_CompMeteorGuide_02".Translate();
                string letterText = "Binguin_CompMeteorGuide_03".Translate() + label + "Binguin_CompMeteorGuide_04".Translate()
                    + "Binguin_CompMeteorGuide_05".Translate() + StoredCount + "/" + MaxStoredMeteors + "）。\n\n"
                    + "Binguin_CompMeteorGuide_06".Translate()
                    + "Binguin_CompMeteorGuide_07".Translate();
                Find.LetterStack.ReceiveLetter(letterLabel, letterText,
                    LetterDefOf.NeutralEvent, new LookTargets(parent));

                BinguinLogUtility.Log("陨石引导器发现 " + mineral + " 陨石（存储 " + StoredCount + "/" + MaxStoredMeteors + "）");
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("陨石发现异常: " + e.Message, severity: 1, isDebug: false);
            }
        }

        // 陨石类型权重：铁60/铀15/玻璃钢10/零部件10/银2/金2/翡翠1（总 100）
        // ★ v3：key 改为"资源岩"（MineableXxx）——原版 MeteoriteImpact 的陨石
        //   就是可挖掘的资源岩（ThingSetMaker_Meteorite 生成 8~20 块 Mineable）
        private static readonly List<KeyValuePair<string, int>> MeteorTable =
            new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>("MineableSteel", 60),
                new KeyValuePair<string, int>("MineableUranium", 15),
                new KeyValuePair<string, int>("MineablePlasteel", 10),
                new KeyValuePair<string, int>("MineableComponentsIndustrial", 10),
                new KeyValuePair<string, int>("MineableSilver", 2),
                new KeyValuePair<string, int>("MineableGold", 2),
                new KeyValuePair<string, int>("MineableJade", 1)
            };

        private static string RollMineral()
        {
            int roll = Rand.RangeInclusive(1, 100);
            int acc = 0;
            for (int i = 0; i < MeteorTable.Count; i++)
            {
                acc += MeteorTable[i].Value;
                if (roll <= acc)
                {
                    return MeteorTable[i].Key;
                }
            }
            return "Steel";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<float>(ref progressDays, "progressDays", 0f, false);
            Scribe_Values.Look<int>(ref lastStep, "lastStep", 0, false);
            Scribe_Values.Look<bool>(ref guaranteedDone, "guaranteedDone", false, false);
            Scribe_Collections.Look<BinguinMeteorRecord>(ref storedMeteors, "storedMeteors",
                LookMode.Deep);
        }

        // gizmo：引导陨石 + dev 立即发现
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            // ★ v3：存储管理（列出全部陨石 + 引导/删除）
            yield return new Command_Action
            {
                defaultLabel = "Binguin_CompMeteorGuide_08".Translate() + StoredCount + "/" + MaxStoredMeteors + "）",
                defaultDesc = "Binguin_CompMeteorGuide_09".Translate(),
                icon = ContentFinder<Texture2D>.Get("Things/Building/Binguin/MeteorGuide", false),
                action = delegate
                {
                    OpenStorageDialog();
                }
            };

            // ★ dev：立即发现一颗陨石
            if (DebugSettings.godMode || Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Binguin_CompMeteorGuide_10".Translate(),
                    defaultDesc = "Binguin_CompMeteorGuide_11".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Things/Building/Binguin/MeteorGuide", false),
                    action = delegate
                    {
                        ForceDiscover();
                    }
                };
            }
        }

        public override string CompInspectStringExtra()
        {
            // ★ 2026-09：删掉 base.CompInspectStringExtra() 的调用——返回值从未被使用，
            //   而检视面板每帧调用本方法，等于每帧白造一个字符串（基类无副作用）。
            string progressStr = "Binguin_CompMeteorGuide_12".Translate() + (int)(ProgressPercent * 100f) + "%";
            string storeStr = "Binguin_CompMeteorGuide_13".Translate() + StoredCount + "/" + MaxStoredMeteors;
            if (powerComp == null || !powerComp.PowerOn)
            {
                return progressStr + "Binguin_CompMeteorGuide_14".Translate() + storeStr;
            }
            return progressStr + "\n" + storeStr;
        }

        // ★ v3：存储管理对话框入口
        private void OpenStorageDialog()
        {
            Dialog_BinguinMeteorStorage dialog = new Dialog_BinguinMeteorStorage(this);
            Find.WindowStack.Add(dialog);
        }

        // 供对话框调用：引导指定陨石（选点）
        public void GuideMeteorAt(int index)
        {
            try
            {
                if (index < 0 || index >= storedMeteors.Count)
                {
                    return;
                }
                if (powerComp == null || !powerComp.PowerOn)
                {
                    Messages.Message("Binguin_CompMeteorGuide_15".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                if (RoofedNow())
                {
                    Messages.Message("Binguin_CompMeteorGuide_16".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                TargetingParameters tp = new TargetingParameters
                {
                    canTargetLocations = true,
                    canTargetSelf = false,
                    canTargetPawns = false,
                    canTargetBuildings = false,
                    canTargetItems = false,
                    validator = delegate (TargetInfo t)
                    {
                        return t.Cell.InBounds(parent.Map);
                    }
                };
                int captureIndex = index;
                Find.Targeter.BeginTargeting(tp, delegate (LocalTargetInfo target)
                {
                    GuideToIndex(captureIndex, target.Cell);
                }, null, null, null, true);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("陨石引导异常: " + e.Message, severity: 1, isDebug: false);
            }
        }

        // 按索引引导（供对话框用）
        private void GuideToIndex(int index, IntVec3 cell)
        {
            try
            {
                if (index < 0 || index >= storedMeteors.Count)
                {
                    return;
                }
                BinguinMeteorRecord rec = storedMeteors[index];
                storedMeteors.RemoveAt(index);
                SpawnMeteorFall(rec, cell);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("陨石引导坠落异常: " + e.Message, severity: 1, isDebug: false);
            }
        }

        // 生成陨石坠落（资源岩 8~20 块）
        private void SpawnMeteorFall(BinguinMeteorRecord rec, IntVec3 cell)
        {
            try
            {
                ThingDef mineableDef = DefDatabase<ThingDef>.GetNamedSilentFail(rec.mineralDefName);
                if (mineableDef == null)
                {
                    BinguinLogUtility.Log("陨石资源岩 def 不存在: " + rec.mineralDefName, severity: 1, isDebug: false);
                    return;
                }
                int count = Rand.RangeInclusive(8, 20);
                List<Thing> contents = new List<Thing>();
                for (int i = 0; i < count; i++)
                {
                    Building rock = (Building)ThingMaker.MakeThing(mineableDef);
                    rock.canChangeTerrainOnDestroyed = false;
                    contents.Add(rock);
                }
                ThingDef incoming = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_MeteoriteIncoming");
                if (incoming == null)
                {
                    incoming = ThingDefOf.MeteoriteIncoming;
                }
                SkyfallerMaker.SpawnSkyfaller(incoming, contents, cell, parent.Map);
                Messages.Message("Binguin_CompMeteorGuide_17".Translate(), new LookTargets(cell, parent.Map),
                    MessageTypeDefOf.NeutralEvent, false);
                BinguinLogUtility.Log("陨石引导：向 " + cell + " 坠落（" + rec.mineralDefName + " x" + count + " 块）");
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("陨石引导坠落异常: " + e.Message, severity: 1, isDebug: false);
            }
        }

        // 供对话框调用：删除指定陨石
        public void DeleteMeteorAt(int index)
        {
            try
            {
                if (index >= 0 && index < storedMeteors.Count)
                {
                    string label = GetMeteorLabel(storedMeteors[index].mineralDefName);
                    storedMeteors.RemoveAt(index);
                    Messages.Message("Binguin_CompMeteorGuide_18".Translate() + label + "Binguin_CompMeteorGuide_19".Translate(), MessageTypeDefOf.NeutralEvent, false);
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("删除陨石异常: " + e.Message, severity: 1, isDebug: false);
            }
        }

        // 供对话框读取存储
        public List<BinguinMeteorRecord> StoredMeteorsList
        {
            get { return storedMeteors; }
        }

        public int MaxStored
        {
            get { return MaxStoredMeteors; }
        }

        public string GetMeteorLabel(string mineableDefName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(mineableDefName);
            if (def != null && def.building != null && def.building.mineableThing != null)
            {
                return def.building.mineableThing.label;
            }
            if (def != null)
            {
                return def.label;
            }
            return mineableDefName;
        }

        private bool RoofedNow()
        {
            if (parent.Map == null)
            {
                return true;
            }
            foreach (IntVec3 c in parent.OccupiedRect())
            {
                if (c.Roofed(parent.Map))
                {
                    return true;
                }
            }
            return false;
        }
    }

    // ============ 陨石存储管理对话框 ============
    // 列出全部已捕获陨石：种类 + 过期倒计时 + 引导坠落 / 删除按钮
    public class Dialog_BinguinMeteorStorage : Window
    {
        private readonly CompBinguinMeteorGuide comp;
        private Vector2 scrollPos;

        public Dialog_BinguinMeteorStorage(CompBinguinMeteorGuide comp)
        {
            this.comp = comp;
            doCloseButton = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(560f, 480f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "Binguin_CompMeteorGuide_20".Translate() + comp.StoredCount + "/" + comp.MaxStored + "）");
            Text.Font = GameFont.Small;

            if (comp.StoredCount <= 0)
            {
                Widgets.Label(new Rect(0f, 50f, inRect.width, 30f), "Binguin_CompMeteorGuide_21".Translate());
                return;
            }

            Rect listRect = new Rect(0f, 44f, inRect.width, inRect.height - 100f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 20f, comp.StoredCount * 56f);
            Widgets.BeginScrollView(listRect, ref scrollPos, viewRect);

            List<BinguinMeteorRecord> list = comp.StoredMeteorsList;
            for (int i = 0; i < list.Count; i++)
            {
                BinguinMeteorRecord rec = list[i];
                Rect row = new Rect(0f, i * 56f, viewRect.width, 52f);

                Widgets.DrawBoxSolid(row, rec.Expired ? new Color(0.25f, 0.2f, 0.2f) : new Color(0.12f, 0.16f, 0.22f));

                string label = comp.GetMeteorLabel(rec.mineralDefName);
                string status = rec.Expired ? "Binguin_CompMeteorGuide_22".Translate() : "Binguin_CompMeteorGuide_23".Translate() + ((rec.expireTick - GenTicks.TicksGame) / 60000).ToString("0") + "Binguin_CompMeteorGuide_24".Translate();
                Widgets.Label(new Rect(row.x + 8f, row.y + 6f, 180f, 24f), label);
                Widgets.Label(new Rect(row.x + 8f, row.y + 28f, 180f, 20f), status);

                Rect guideRect = new Rect(row.x + 220f, row.y + 8f, 140f, 34f);
                if (!rec.Expired && Widgets.ButtonText(guideRect, "Binguin_CompMeteorGuide_25".Translate()))
                {
                    comp.GuideMeteorAt(i);
                    Close();
                }

                Rect deleteRect = new Rect(row.x + 372f, row.y + 8f, 100f, 34f);
                if (Widgets.ButtonText(deleteRect, "Binguin_CompMeteorGuide_26".Translate()))
                {
                    comp.DeleteMeteorAt(i);
                }
            }

            Widgets.EndScrollView();
        }
    }
}
