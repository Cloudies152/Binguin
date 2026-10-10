// ============================================================================
// 钓竿装配台 —— 自定义合成/制饵入口（gizmo 打开对话框）+ 装配工作流
//
// ★ 为什么用自定义对话框而不是原版配方（用户点名的问题）：
//   原版 RecipeDef 的 ingredient filter 只能按 thingDef/类别筛选，
//   无法按物品材质区分（如"玻璃钢长竿身" vs "铁长竿身"）。
//   配件是 stuff 物品（制作时选材质），合成时必须精确选择材质 →
//   装配台 gizmo 打开 Dialog_BinguinRodAssembly（4 配件按物品逐一点选）。
//   鱼饵同理（Dialog_BinguinBait，效果由消耗食物决定）。
//
// ★ 装配工作流（2026-08-19 用户反馈）：
//   对话框确认后 → 选择一名殖民者 → 派自定义 Job（Binguin_AssembleRod，
//   driverClass 静态构造挂载）→ 殖民者走到装配台交互格工作 → 完成时
//   FinishAssembly：扣除配件 + 4 高级零部件，按制作技能生成品质，产出鱼竿。
//   待装配数据暂存在 comp（单槽），job 完成时读取（取消 job 不损失配件）。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

using Binguin.Helper;

namespace Binguin.Feature.Rods
{
    public class CompProperties_BinguinRodAssembly : CompProperties
    {
        public CompProperties_BinguinRodAssembly()
        {
            compClass = typeof(CompBinguinRodAssembly);
        }
    }

    public class CompBinguinRodAssembly : ThingComp
    {
        // ---- 待装配数据（对话框设置，job 完成时读取） ----
        public Thing pendingShaft;
        public Thing pendingTip;
        public Thing pendingHook;
        public Thing pendingBait;
        public Pawn pendingPawn;

        public bool HasPendingAssembly
        {
            // ★ 2026-10-08 修正：原来只看 shaft/tip/hook，**漏了鱼饵** ⇒
            //   鱼饵缺失时会误判"没有待装配"。四件一起判。
            get
            {
                return pendingShaft != null && pendingTip != null
                    && pendingHook != null && pendingBait != null;
            }
        }

        // ★ 装配中断检测（用户 2026-08-19 反馈：征召打断殖民者 → 装配台永久卡
        //   "正在装配中"）：pendingPawn 的当前 job 不再是装配 job → 复位
        //   （配件未扣，可重新合成；job 完成时 FinishAssembly 已清，幂等）
        //
        // ★★★ 2026-10-08 **重要修正**（用户反馈：「制作时被征召中断」，
        //      但玩家其实没有征召，配件也没被消耗）：
        //   旧实现是"只要某一 tick 里 `curJob.def != 装配job` 就立刻复位"——
        //   这是**瞬时判断**，而现在的搬运流程要搬 **4 件配件 + 4 个高级零部件**，
        //   中间有 `Toils_General.Do` 切换 target、有落物 toil、有"换下一个目标"的间隙，
        //   任何一个 tick 的 `curJob.def` 不如预期就会**误判为被中断** ⇒ 清空 pending。
        //   实测后果（Player.log）：
        //     4 条「配件已放下」之后紧跟一条「搬运被中断（征召等），装配台已复位」
        //     ⇒ `pendingShaft/Tip/Hook/Bait` 全被清空
        //     ⇒ job driver 走到 `SpawnUnfinishedRod()` 时 pending 已空
        //     ⇒ **半成品生成不出来**，4 件配件散在台边（好在没被销毁，仍可重来）。
        //   ⇒ 改成**看门狗**：必须同时满足三个条件、且**连续** `StallResetTicks`
        //     （默认 600 tick = 10 秒）都成立才复位。一次瞬时不等绝不动手。
        private static JobDef cachedAssembleJobDef;
        private static bool assembleJobLookedUp;

        /// <summary>连续"卡住"多少 tick 才复位（默认 600 = 10 秒）。</summary>
        private const int StallResetTicks = 600;

        /// <summary>连续卡住的 tick 计数（进存档）。</summary>
        private int stallTicks;

        private static JobDef AssembleJobDef
        {
            get
            {
                if (!assembleJobLookedUp)
                {
                    assembleJobLookedUp = true;
                    cachedAssembleJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_AssembleRod");
                }
                return cachedAssembleJobDef;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!HasPendingAssembly || pendingPawn == null)
            {
                stallTicks = 0;
                return;
            }

            // ★★ 2026-10-08 重写（旧版见上面注释：瞬时判断会误判"被中断"）。
            //   看门狗三条同时成立、且连续 10 秒才复位：
            //     ① 装配者既没在做装配 job、队列里也没有
            //     ② 装配台附近找不到待装的配件（说明它们已经不在运输途中）
            //     ③ 连续卡住 ≥ StallResetTicks
            if (IsActivelyAssembling(pendingPawn))
            {
                stallTicks = 0;
                return;
            }
            if (HasPendingPartNearTable())
            {
                // 配件已经落在台边 —— 这也算"进行中"，不要复位
                stallTicks = 0;
                return;
            }

            stallTicks++;
            if (stallTicks < StallResetTicks)
            {
                return;
            }

            // 到这里才是真正卡住（装配者彻底不管了、配件也不在台边）
            // 再确认一次，避免中途又被派活
            if (IsActivelyAssembling(pendingPawn) || HasPendingPartNearTable())
            {
                stallTicks = 0;
                return;
            }
            BinguinLogUtility.Log("装配台连续 " + StallResetTicks
                + " tick 无人处理，判定为放弃装配：把配件放回台边、清空待装配状态。");
            ResetStalledAssembly();
        }

        /// <summary>
        /// 这个装配者是不是还在做这台装配台的事（当前 job 或队列里都算）。
        /// ★ 比旧版的"只看 curJob"稳得多。
        /// </summary>
        private bool IsActivelyAssembling(Pawn p)
        {
            if (p == null || p.jobs == null || parent == null)
            {
                return false;
            }
            JobDef def = AssembleJobDef;
            if (def == null)
            {
                return false;
            }
            Job cur = p.jobs.curJob;
            if (cur != null && cur.def == def && cur.targetA.Thing == parent)
            {
                return true;
            }
            JobQueue q = p.jobs.jobQueue;
            if (q != null)
            {
                for (int i = 0; i < q.Count; i++)
                {
                    Job queued = q[i].job;
                    if (queued != null && queued.def == def && queued.targetA.Thing == parent)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 装配台附近（半径 6 格）是否已经有待装的配件在地上 —— 有就说明搬运有进展。
        /// </summary>
        private bool HasPendingPartNearTable()
        {
            Map map = parent != null ? parent.Map : null;
            if (map == null)
            {
                return false;
            }
            Thing[] parts = { pendingShaft, pendingTip, pendingHook, pendingBait };
            for (int i = 0; i < parts.Length; i++)
            {
                Thing part = parts[i];
                if (part == null || part.Destroyed)
                {
                    continue;
                }
                if (!part.Spawned)
                {
                    // 还在某人手上（被搬运中）也算是进行中
                    return true;
                }
                if ((part.Position - parent.Position).LengthHorizontalSquared <= 36f)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 真正卡住时的复位：把配件**放回地图**（绝不销毁），再清空 pending。
        /// ★ 配件不销毁是有意为之 —— 玩家还能重新装配，不会白做 4 件配件。
        /// </summary>
        private void ResetStalledAssembly()
        {
            Map map = parent != null ? parent.Map : null;
            Thing[] parts = { pendingShaft, pendingTip, pendingHook, pendingBait };
            for (int i = 0; i < parts.Length; i++)
            {
                Thing part = parts[i];
                if (part == null || part.Destroyed)
                {
                    continue;
                }
                if (part.Spawned)
                {
                    continue;   // 已经在地上，别重复放
                }
                // 在谁手上就让他放下；否则直接放到台边
                Pawn carrier = part.ParentHolder as Pawn;
                if (carrier != null && carrier.carryTracker != null
                    && carrier.carryTracker.CarriedThing == part)
                {
                    Thing dropped;
                    carrier.carryTracker.TryDropCarriedThing(
                        parent.Position, ThingPlaceMode.Near, out dropped);
                }
                else if (map != null)
                {
                    GenPlace.TryPlaceThing(part, parent.Position, map, ThingPlaceMode.Near, null);
                }
            }
            stallTicks = 0;
            ClearPending();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look<Thing>(ref pendingShaft, "pendingShaft");
            Scribe_References.Look<Thing>(ref pendingTip, "pendingTip");
            Scribe_References.Look<Thing>(ref pendingHook, "pendingHook");
            Scribe_References.Look<Thing>(ref pendingBait, "pendingBait");
            Scribe_References.Look<Pawn>(ref pendingPawn, "pendingPawn");
            // ★ 2026-10-08：看门狗的"连续卡住"计数（1.6 新字段名，读旧档自动补 0）
            Scribe_Values.Look<int>(ref stallTicks, "stallTicks", 0, false);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            // ★ 2026-10-08 由程序员修订版并入：
            //   只对玩家的装配台显示业务按钮（NPC/敌对派系的装配台不显示）。
            //   位置必须在 makeBait 之前 —— 否则第一个按钮已经 yield 出去了。
            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            Command_Action makeBait = new Command_Action();
            makeBait.defaultLabel = "Binguin_CompRodAssembly_01".Translate();
            makeBait.defaultDesc = "Binguin_CompRodAssembly_02".Translate();
            makeBait.icon = ContentFinder<Texture2D>.Get("UI/Commands/BinguinMakeBait", false);
            makeBait.action = delegate
            {
                Find.WindowStack.Add(new Dialog_BinguinBait(parent));
            };
            // ★ 无电禁用（用户 2026-08-19 反馈）
            CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn)
            {
                makeBait.Disable("Binguin_CompRodAssembly_03".Translate());
            }
            yield return makeBait;

            Command_Action assemble = new Command_Action();
            assemble.defaultLabel = "Binguin_CompRodAssembly_04".Translate();
            assemble.defaultDesc = "Binguin_CompRodAssembly_05".Translate();
            assemble.icon = ContentFinder<Texture2D>.Get("UI/Commands/BinguinAssembleRod", false);
            if (power != null && !power.PowerOn)
            {
                assemble.Disable("Binguin_CompRodAssembly_06".Translate());
            }
            if (HasPendingAssembly)
            {
                assemble.Disable("Binguin_CompRodAssembly_07".Translate());
            }
            assemble.action = delegate
            {
                // ★ 2026-09-26 用户点名改成"地图连续点选 4 次"（原来打开 Dialog_BinguinRodAssembly）。
                //   旧对话框保留在文件里没删：想改回去只要把下面这行换回
                //   Find.WindowStack.Add(new Dialog_BinguinRodAssembly(parent));
                //   （它按材质精确列出 4 类配件 + 可选制作人 + 属性预览，仍有参考价值。）
                BinguinRodPartPicker.RequestStart(parent);
            };
            yield return assemble;

            Command_Action openStockPick = new Command_Action();
            openStockPick.defaultLabel = "Binguin_CompRodAssembly_12".Translate();
            openStockPick.defaultDesc = "Binguin_CompRodAssembly_13".Translate();
            openStockPick.icon = ContentFinder<Texture2D>.Get("UI/Commands/BinguinAssembleRod", false);
            if (power != null && !power.PowerOn)
            {
                openStockPick.Disable("Binguin_CompRodAssembly_06".Translate());
            }
            if (HasPendingAssembly)
            {
                openStockPick.Disable("Binguin_CompRodAssembly_07".Translate());
            }
            openStockPick.action = delegate
            {
                Find.WindowStack.Add(new Dialog_BinguinRodStockPick(parent));
            };
            yield return openStockPick;

            // ---- 调试按钮（★ 只在开发者模式显示，正常游玩看不到）----
            // 2026-10-06 用户需求：「做个 debug 用的快速生成四种配件各一个和
            //   4 个高级零部件，这样测试快一点」。就地生成在装配台旁边，省去手工筹备。
            if (Prefs.DevMode)
            {
                Command_Action devKit = new Command_Action();
                devKit.defaultLabel = "（调试）生成装配测试套件";
                devKit.defaultDesc = "在装配台旁生成：4 种配件各 1 个（钢铁材质）+ 4 个高级零部件。\n"
                    + "仅开发者模式可见。";
                devKit.action = delegate
                {
                    DebugActions_RodAssembly.SpawnKitAt(parent.Map, parent.Position,
                        ThingDefOf.Steel, false);
                };
                yield return devKit;
            }
        }

        // 对话框确认：检查 4 高级零部件 + 记录配件 + 派"第一次装配"job
        // ★★ 2026-10-06 起流程变了（用户需求：搬运后变成【钓竿（未完成）】）：
        //    本方法只负责把 4 件配件**登记到 pending** 并派 `Binguin_AssembleRod`。
        //    那个 job 现在做的是：把配件搬到装配台 → `SpawnUnfinishedRod()` 生成半成品
        //    → 然后自动排上 `Binguin_WorkOnUnfinishedRod` 开始干活。
        //    也就是说本方法**不再直接产出成品**。
        public bool StartAssembly(Pawn worker, Thing shaft, Thing tip, Thing hook, Thing bait)
        {
            Map map = parent.Map;
            if (map == null || worker == null || shaft == null || tip == null || hook == null)
            {
                return false;
            }
            if (!HasComponents(4))
            {
                Messages.Message("Binguin_CompRodAssembly_08".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            pendingShaft = shaft;
            pendingTip = tip;
            pendingHook = hook;
            pendingBait = bait;
            pendingPawn = worker;

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_AssembleRod");
            if (jobDef == null)
            {
                BinguinLogUtility.Log("找不到 JobDef Binguin_AssembleRod，装配未派发。", severity: 1, isDebug: false);
                return false;
            }
            Job job = JobMaker.MakeJob(jobDef, parent);
            worker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            Messages.Message(worker.NameShortColored + "Binguin_CompRodAssembly_09".Translate(), parent, MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        /// <summary>
        /// 派"对已有半成品继续/开始干活"的 job（续接流程 + 第一次装配的收尾都用它）。
        /// ★ 同时**重新绑定制作者**为 `worker`：玩家手动派活时以玩家选的人为准。
        /// </summary>
        public static bool StartWorkOnRod(Pawn worker, Thing rod)
        {
            if (worker == null || rod == null || rod.Destroyed)
            {
                return false;
            }
            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_WorkOnUnfinishedRod");
            if (jobDef == null)
            {
                BinguinLogUtility.Log("找不到 JobDef Binguin_WorkOnUnfinishedRod，无法继续装配。", severity: 1, isDebug: false);
                return false;
            }
            CompBinguinUnfinishedRod comp = rod.TryGetComp<CompBinguinUnfinishedRod>();
            if (comp != null)
            {
                comp.boundMaker = worker;   // 绑定制作者（玩家选中的人）
            }
            Job job = JobMaker.MakeJob(jobDef, rod);
            worker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            return true;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ⚠️⚠️ 死代码警告（2026-10-09 用 RimSage 复核原版源码后确认）⚠️⚠️
        //
        //   本方法【全文没有任何调用点】。2026-10-06 起流程已改成：
        //     StartAssembly  → 派 Binguin_AssembleRod job
        //                    → 小人把 4 件配件 + 4 个高级零部件搬到装配台
        //                    → SpawnUnfinishedRod() 生成【钓竿（未完成）】
        //                    → Binguin_WorkOnUnfinishedRod 干活
        //                    → 收尾逻辑在 Comp_BinguinUnfinishedRod.cs
        //   ⇒ 这里保留的只是**旧流程的收尾逻辑**，供对照。
        //
        //   ★ 为什么不能直接删：语言键 / 注释 / 未来可能需要对照，
        //     而且它是 public（删了要连带清干净）。
        //   ★★ 但**绝对不能重新接上它**：它会 `ConsumeComponents(4)` 并
        //     `ThingMaker.MakeThing(rodDef)` —— 现在是 SpawnUnfinishedRod 在收料，
        //     两处都跑 = **双重扣料 + 双重产出**。
        //   ⇒ 所以下面加了一句运行时告警：万一有人接上，日志里立刻能看见。
        // ══════════════════════════════════════════════════════════════════════
        public void FinishAssembly(Pawn worker)
        {
            BinguinLogUtility.Log("CompBinguinRodAssembly.FinishAssembly 被调用了 —— 这是【死代码】！"
                + "当前流程的收尾在 Comp_BinguinUnfinishedRod.cs。"
                + "若确实需要走旧路径，请先确认不会与 SpawnUnfinishedRod 双重扣料。", severity: 1, isDebug: false);
            Map map = parent.Map;
            if (map == null) return;
            if (pendingShaft == null || pendingTip == null || pendingHook == null)
            {
                ClearPending();
                return;
            }
            if (!ConsumeComponents(4))
            {
                Messages.Message("Binguin_CompRodAssembly_10".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                ClearPending();
                return;
            }

            bool bladeTip = pendingTip.def.defName == BinguinRodUtility.TipBlade;
            ThingDef rodDef = DefDatabase<ThingDef>.GetNamedSilentFail(bladeTip ? BinguinRodUtility.RodBlade : BinguinRodUtility.RodHammer);
            if (rodDef == null)
            {
                ClearPending();
                return;
            }

            Thing rod = ThingMaker.MakeThing(rodDef);
            CompBinguinRod comp = rod.TryGetComp<CompBinguinRod>();
            if (comp != null)
            {
                comp.longShaft = pendingShaft.def.defName == BinguinRodUtility.ShaftLong;
                comp.bladeTip = bladeTip;
                comp.straightHook = pendingHook.def.defName == BinguinRodUtility.HookStraight;
                comp.shaftStuff = pendingShaft.Stuff;
                comp.tipStuff = pendingTip.Stuff;
                comp.hookStuff = pendingHook.Stuff;
                CompBinguinRodBait baitComp = pendingBait != null ? pendingBait.TryGetComp<CompBinguinRodBait>() : null;
                if (baitComp != null && baitComp.effects != null && baitComp.effects.Count > 0)
                {
                    comp.baitEffects = new List<BinguinBaitEffect>(baitComp.effects);
                }
            }

            // ★ 品质：按制作技能（原版制作品质生成）
            QualityCategory quality = QualityUtility.GenerateQualityCreatedByPawn(worker, SkillDefOf.Crafting, false);
            CompQuality qualityComp = rod.TryGetComp<CompQuality>();
            if (qualityComp != null)
            {
                qualityComp.SetQuality(quality, null);
            }

            // 消耗配件
            // ★ 2026-10-05：现在配件会先被小人**搬到装配台旁边**（见
            //   JobDriver_BinguinAssembleRod 的搬运段），所以它们可能已经在
            //   小人手上 / 掉在装配台附近的地上 —— 不再一定是"原地图格子上那件"。
            //   `Thing.Destroy()` 对这些情况都成立（会先自动从 holder 里摘出来），
            //   所以这里保持不变；但加一道"已销毁就跳过"的保护，避免重复销毁报错。
            DestroyPart(pendingShaft);
            DestroyPart(pendingTip);
            DestroyPart(pendingHook);
            DestroyPart(pendingBait);
            ClearPending();

            GenPlace.TryPlaceThing(rod, parent.Position, map, ThingPlaceMode.Near, null);
            Messages.Message("Binguin_CompRodAssembly_11".Translate() + quality.GetLabel() + "）！", rod, MessageTypeDefOf.PositiveEvent, true);
        }

        /// <summary>安全销毁一件配件（已销毁 / 空引用都跳过）。</summary>
        private static void DestroyPart(Thing t)
        {
            if (t != null && !t.Destroyed)
            {
                t.Destroy(DestroyMode.Vanish);
            }
        }

        public void ClearPending()
        {
            pendingShaft = null;
            pendingTip = null;
            pendingHook = null;
            pendingBait = null;
            pendingPawn = null;
        }

        /// <summary>
        /// 把某个格子上压着的**可搬运物品**挪到旁边，给即将放下的东西腾位置。
        ///
        /// ★ 为什么需要（2026-10-09 用 RimSage 查原版源码确认的机制）：
        ///   `BuildingProperties.maxItemsInCell` 默认 **1**（`BuildingProperties.cs:132`），
        ///   装配台（`BenchBase` 子类）没覆盖 ⇒ 每格只能容 1 个物品。
        ///   而 `GenPlace.TryPlaceDirect` 的容量是
        ///     `GetMaxItemsAllowedInCell(map) - 该格已有物品数`
        ///   （`GridsUtility.cs:267` / `GenPlace.cs:352`）⇒ 格子上有东西时**放不进去**。
        ///
        /// ★ 安全性：
        ///   · 只动 `def.category == ThingCategory.Item` 且 `def.alwaysHaulable` 的东西
        ///     （建筑、蓝图、尸体等一律不碰）；
        ///   · 用原版 `ThingPlaceMode.Near` 挪到附近，失败就放弃（**绝不销毁**）；
        ///   · 留 `TryAbsorbPart` 后面照样能从地图上把它吸进半成品
        ///     （它按 `pendingXxx` 引用找东西，不依赖位置）。
        /// </summary>
        private void MakeRoomOnCell(IntVec3 cell, Map map)
        {
            try
            {
                if (!cell.IsValid || !cell.InBounds(map))
                {
                    return;
                }
                List<Thing> things = cell.GetThingList(map);
                if (things == null || things.Count == 0)
                {
                    return;
                }
                // ★ 必须复制一份再遍历：挪动过程中会改 things 列表
                List<Thing> snapshot = new List<Thing>(things);
                int moved = 0;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    Thing t = snapshot[i];
                    if (t == null || t.Destroyed || t.def == null)
                    {
                        continue;
                    }
                    if (t.def.category != ThingCategory.Item || !t.def.alwaysHaulable)
                    {
                        continue;   // 不是可搬运物品 —— 不碰
                    }
                    if (t.def.EverHaulable && t.def.category == ThingCategory.Item
                        && t.IsForbidden(Faction.OfPlayer))
                    {
                        // 被禁止的物品玩家可能故意摆在那里，不动它
                        continue;
                    }
                    if (GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near, null))
                    {
                        moved++;
                    }
                }
                if (moved > 0)
                {
                    BinguinLogUtility.Log("为放下半成品，把装配台格上的 " + moved
                        + " 件物品挪到了旁边。");
                }
            }
            catch (Exception ex)
            {
                // 腾位置失败不影响主流程 —— Direct 失败还有后面的兜底
                BinguinLogUtility.Log("腾台面位置时出错（已忽略）：" + ex.Message, severity: 1, isDebug: false);
            }
        }

        // ================================================================
        // 【钓竿（未完成）】· 可搬运半成品（2026-10-06 用户需求）
        // ================================================================
        /// <summary>
        /// 生成一根【钓竿（未完成）】并放到装配台旁边，同时把 4 件配件**装进它里面**
        /// （配件从地图上被摘走，由半成品的容器保管 —— 不会出现两份）。
        ///
        /// ★ 这就是用户要的"搬运后会变成一个 钓竿（未完成）"那一步。
        /// ★ 之后由 `JobDriver_BinguinWorkOnUnfinishedRod` 对它干活；
        ///   工作量记在半成品身上 ⇒ 谁都能续接、也能搬走。
        /// </summary>
        public Thing SpawnUnfinishedRod()
        {
            Map map = parent != null ? parent.Map : null;
            if (map == null)
            {
                BinguinLogUtility.Log("生成半成品失败：装配台不在有效地图上。", severity: 1, isDebug: false);
                return null;
            }
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_UnfinishedRod");
            if (def == null)
            {
                BinguinLogUtility.Log("找不到 Binguin_UnfinishedRod（def 未加载？），半成品未生成。", severity: 1, isDebug: false);
                return null;
            }

            // ① 生成
            // ★★★ 2026-10-06 位置（用户要求：「鱼竿未成品应该在装配台中间（即殖民者前方）」）：
            //   **半成品放在装配台自己那一格（建筑中心）**，小人在交互格干活
            //   ⇒ 小人在台前、半成品在台上，正是原版工作台的形态。
            //   历史（别再绕）：
            //     · 放"台子那格"时被占位逻辑挤到另一侧 ⇒ 小人够不着（已用 Direct 解决）
            //     · 放"交互格"时半成品和小人同一格 ⇒ 小人站在半成品上
            //   ⇒ 现在：`ThingPlaceMode.Direct` 精确放在台子中心；失败才退回交互格 → Near。
            Thing rod = ThingMaker.MakeThing(def);
            if (rod == null)
            {
                return null;
            }
            Building table = parent as Building;
            IntVec3 icell = (table != null) ? table.InteractionCell : IntVec3.Invalid;
            IntVec3 tableCell = parent.Position;

            // ★★ 关键前提（2026-10-09 用 RimSage 查原版源码确认）：
            //   `GenPlace.TryPlaceDirect` 的容量算法是
            //     `num3 = loc.GetMaxItemsAllowedInCell(map) - 该格已有物品数`
            //   而 `IntVec3.GetMaxItemsAllowedInCell` = `GetEdifice(map)?.MaxItemsInCell ?? 1`
            //   （`Source/Verse/GridsUtility.cs:267`），
            //   `BuildingProperties.maxItemsInCell` 默认 = **1**（`BuildingProperties.cs:132`），
            //   我们的装配台（`BenchBase` 子类）没覆盖它 ⇒ **每格只能容 1 个物品**。
            //   ⇒ 如果台子格上已经压着一件零件（搬运过程中很正常），
            //     `num3 <= 0` ⇒ **Direct 会失败** ⇒ 半成品掉到交互格/台子旁边（地上），
            //     而不是"在台面上"。这与需求「鱼竿未成品应该在装配台中间」不符。
            //   ⇒ 修法：放之前先把那格上的**可搬运物品**挪到旁边，给半成品腾位置。
            MakeRoomOnCell(tableCell, map);

            // ★ Direct = 精确放在指定格（装配台是 PassThroughOnly，物品可以压在台面上）
            GenPlace.TryPlaceThing(rod, tableCell, map, ThingPlaceMode.Direct, null);
            if (!rod.Spawned && icell.IsValid && icell.InBounds(map))
            {
                // 没能压在台面上 ⇒ 退回交互格
                MakeRoomOnCell(icell, map);
                GenPlace.TryPlaceThing(rod, icell, map, ThingPlaceMode.Direct, null);
            }
            if (!rod.Spawned)
            {
                // ⚠️ 最后兜底：Near 会放到台子【旁边】（地上），不是台面。
                //   正常流程不该走到这里（前面 Direct 应该成功）。
                BinguinLogUtility.Log("半成品无法压到台面上（台子格被占？），已退到台子附近的地上。", severity: 1, isDebug: false);
                GenPlace.TryPlaceThing(rod, tableCell, map, ThingPlaceMode.Near, null);
            }
            if (!rod.Spawned)
            {
                BinguinLogUtility.Log("半成品没能放到地图上，已放弃。", severity: 1, isDebug: false);
                if (!rod.Destroyed) rod.Destroy(DestroyMode.Vanish);
                return null;
            }

            // ② 把 4 件配件装进去
            CompBinguinUnfinishedRod comp = rod.TryGetComp<CompBinguinUnfinishedRod>();
            if (comp == null)
            {
                BinguinLogUtility.Log("半成品上没有 CompBinguinUnfinishedRod"
                    + "（检查 XML 的 comps 声明），已放弃。", severity: 1, isDebug: false);
                if (!rod.Destroyed) rod.Destroy(DestroyMode.Vanish);
                return null;
            }
            int absorbed = 0;
            // ★ 先收"还在小人手上"的那件（`pendingPawn` 的 carryTracker），
            //   再收地图上的其余配件 —— 生成时半成品占的是交互格，
            //   而搬运 toil 刚把配件放在同一个格子里，先腾出手上那件更稳。
            //   `TryAbsorbPart` 对"地图 holder"和"手上 holder"都能处理（DeSpawn + TryAdd）。
            absorbed += AddPartTo(comp, pendingShaft, pendingPawn) ? 1 : 0;
            absorbed += AddPartTo(comp, pendingTip, pendingPawn) ? 1 : 0;
            absorbed += AddPartTo(comp, pendingHook, pendingPawn) ? 1 : 0;
            absorbed += AddPartTo(comp, pendingBait, pendingPawn) ? 1 : 0;

            // ★★ 2026-10-08 用户需求：**高级零部件也跟着一起被吸进半成品**，
            //   和 4 件配件完全同一条路径（`AddPartTo` → `TryAbsorbPart`）。
            //   ★ 前提：`Comp_BinguinUnfinishedRod.Accepts` 的白名单里要有 ComponentSpacer，
            //     否则 `TryAbsorbPart` 第一句就把它拒了（这是原来的根本原因）。
            //   ★ 顺序：① 小人手上 → ② 台边最近的 → ③ 更远的（按距离递增）
            //   ★★ 为什么必须按距离：诊断日志实测，地图扫描顺序会**先挑到仓库里
            //      97 格外的旧货**，小人走过去就卡住。这里同样按距离排序，
            //      优先吸台边那几堆。
            int spacersAbsorbed = 0;
            const int SpacersNeeded2 = 4;
            // ① 小人手上
            Thing inHand = pendingPawn != null && pendingPawn.carryTracker != null
                ? pendingPawn.carryTracker.CarriedThing : null;
            if (inHand != null && inHand.def != null && inHand.def.defName == "ComponentSpacer")
            {
                spacersAbsorbed += AddPartTo(comp, inHand, pendingPawn) ? 1 : 0;
            }
            // ② 其余的，按离台距离从近到远
            if (spacersAbsorbed < SpacersNeeded2)
            {
                List<Thing> rest = FindAllSpacersOnMap(map);
                IntVec3 tp = parent.Position;
                rest.Sort(delegate (Thing a, Thing b)
                {
                    float da = (a.Position - tp).LengthHorizontalSquared;
                    float db = (b.Position - tp).LengthHorizontalSquared;
                    return da.CompareTo(db);
                });
                for (int i = 0; i < rest.Count && spacersAbsorbed < SpacersNeeded2; i++)
                {
                    Thing sp = rest[i];
                    if (sp == null || sp.Destroyed) continue;
                    if (AddPartTo(comp, sp, pendingPawn))
                    {
                        spacersAbsorbed++;
                    }
                }
            }
            BinguinLogUtility.Log("零部件装入半成品 " + spacersAbsorbed + " 堆（共计入 "
                + comp.spacersSpent + " 个）。");

            // ★ 记住是在哪台装配台上做的：半成品在交互格上，干活时用
            //   `GetEdifice` 找不到台子，得靠这个引用（也用于工作速度与站位）。
            comp.workTable = parent;
            // ★★ 2026-10-06 用户需求「记得绑定制作者」：
            //   第 5 步选的那个人（`pendingPawn`）就是这根半成品的制作者。
            //   绑定后：只有他能**自动**回来续做（其它人不会抢），
            //   但玩家仍可手动用「继续装配」换人（换人时会重新绑定）。
            comp.boundMaker = pendingPawn;
            // ★★★ 2026-10-09 修复（用户报「搬高级零部件只搬一个，却消耗四个」）：
            //
            // 【旧代码的 bug】
            //   `comp.spacersSpent = 4;`  —— **硬编码**
            //   而 `CompBinguinUnfinishedRod.TryAbsorbPart` 的 L301 其实**已经正确记账**了：
            //       `spacersSpent += part.stackCount;`
            //   ⇒ 这行硬编码把真实数量**覆盖**掉了
            //   ⇒ 结果：只吸进 1 个实物、却按 4 个记账 ⇒ 玩家看到"消耗四个"。
            //
            // 【修法】不再硬编码，改成按**实际吸进容器的数量**记账：
            //   · `spacersSpent` 由 `TryAbsorbPart` 累加，这里**一个字都不改**
            //   · 少了的部分**在这里立刻从地图补扣**（不是拖到收尾）
            //     —— 因为「已经付过料」的语义是"生成半成品时就付了"，
            //        补扣放在这里，收尾/退款两端就都不用特判了
            //   · 补扣成功 ⇒ `spacersSpent` 记满 4（容器里的实物 + 补扣的合计 4）
            //   · 补扣失败 ⇒ `spacersSpent` 保持实际值，**收尾时会再兜一次**
            //     （见 `Comp_BinguinUnfinishedRod` 收尾里的 `spacersSpent > 0` 分支）
            //
            // ★ 这样保证：**玩家既不亏也不白拿** —— 拿走几个就记几个，缺的当场补。
            if (spacersAbsorbed < SpacersNeeded2)
            {
                int gap = SpacersNeeded2 - comp.spacersSpent;
                if (gap > 0)
                {
                    // ConsumeComponentsFromMap 成功会返回 true，并已扣掉地图上的实物
                    if (ConsumeComponentsFromMap(map, gap))
                    {
                        comp.spacersSpent = SpacersNeeded2;
                        BinguinLogUtility.Log("零部件只吸进 " + spacersAbsorbed
                            + " 堆（" + (SpacersNeeded2 - gap) + " 个实物），"
                            + "差额 " + gap + " 个已从地图补扣。");
                    }
                    else
                    {
                        // 地图上也不够 —— 保持实际数量，让收尾流程再兜；绝不虚报
                        BinguinLogUtility.Log("零部件差额 " + gap
                            + " 个在地图上补扣失败；spacersSpent 保持实际值 "
                            + comp.spacersSpent + "（收尾时会再试一次）。", severity: 1, isDebug: false);
                    }
                }
            }

            // ★ 记下这根半成品【实际】付过几个高级零部件 —— 半成品被销毁时要按这个数退回来
            //   （★ 不再硬编码 4，见上面的修复说明）

            BinguinLogUtility.Log("已生成【钓竿（未完成）】于 " + rod.Position
                + "，装入配件 " + absorbed + " 件：" + comp.PartsSummary());

            ClearPending();
            return rod;
        }

        /// <summary>
        /// 把一件配件装进半成品。
        /// ★ `carrier` 必须传：搬运 toil 结束时配件可能还在小人手上，
        ///   而 `ThingOwner.TryAdd` 不会自动接管被 carryTracker 持有的东西。
        /// </summary>
        private static bool AddPartTo(CompBinguinUnfinishedRod comp, Thing part, Pawn carrier)
        {
            if (comp == null || part == null || part.Destroyed)
            {
                return false;
            }
            return comp.TryAbsorbPart(part, carrier);
        }

        /// <summary>把配件装进**已存在**的半成品（续接流程用）。</summary>
        public static bool AddPartToExisting(Thing rod, Thing part, Pawn carrier)
        {
            if (rod == null || part == null || part.Destroyed)
            {
                return false;
            }
            CompBinguinUnfinishedRod comp = rod.TryGetComp<CompBinguinUnfinishedRod>();
            return comp != null && comp.TryAbsorbPart(part, carrier);
        }

        // 检查地图上高级零部件数量
        private bool HasComponents(int count)
        {
            return HasComponentsOnMap(parent.Map, count);
        }

        /// <summary>
        /// 扫地图上所有高级零部件（ComponentSpacer）。
        /// ★ 2026-10-08：JobDriver 里那份叫 `FindAllSpacers` 是 private，
        ///   装配台这里要用，所以单独放一份（同名会混淆，故加 OnMap 后缀）。
        /// </summary>
        public static List<Thing> FindAllSpacersOnMap(Map map)
        {
            List<Thing> result = new List<Thing>();
            if (map == null)
            {
                return result;
            }
            List<Thing> all = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.def == null || t.Destroyed || !t.Spawned)
                {
                    continue;
                }
                if (t.def.defName != "ComponentSpacer")
                {
                    continue;
                }
                result.Add(t);
            }
            return result;
        }
        /// <summary>地图上高级零部件够不够（静态版：半成品收尾也要用）。</summary>
        public static bool HasComponentsOnMap(Map map, int count)
        {
            if (map == null) return false;
            int found = 0;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (t != null && t.def != null && t.def.defName == "ComponentSpacer")
                {
                    found += t.stackCount;
                    if (found >= count) return true;
                }
            }
            return false;
        }

        // 从地图扣除高级零部件
        private bool ConsumeComponents(int count)
        {
            return ConsumeComponentsFromMap(parent.Map, count);
        }

        /// <summary>
        /// 从地图扣除 count 个高级零部件（静态版：半成品收尾也要用，保证只有一份实现）。
        /// ★ 不能在枚举 listerThings 时 Destroy（Collection was modified 红字！），
        ///   先收集整堆再统一销毁。
        /// </summary>
        public static bool ConsumeComponentsFromMap(Map map, int count)
        {
            if (map == null) return false;
            int need = count;
            List<Thing> fullStacks = new List<Thing>();
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (need <= 0) break;
                if (t == null || t.def == null || t.def.defName != "ComponentSpacer") continue;
                if (t.stackCount <= need)
                {
                    fullStacks.Add(t);
                    need -= t.stackCount;
                }
                else
                {
                    Thing removed = t.SplitOff(need);
                    if (removed != null)
                    {
                        removed.Destroy();
                        need = 0;
                    }
                }
            }
            for (int i = 0; i < fullStacks.Count; i++)
            {
                if (fullStacks[i] != null && !fullStacks[i].Destroyed)
                {
                    fullStacks[i].Destroy();
                }
            }
            return need <= 0;
        }
    }
}
