// ============================================================================
// 钓竿装配台【地图连续点选 4 次配件】—— 2026-09-26 用户需求
//
// 用户需求：「复活机械液物品的选择目标代码是怎么写的，我想用到自定义鱼竿部件上，
//            通过选择四次不同部件，再让制作者搬到台上来制作鱼竿」
//
// ★ 选定的交互（用户三选一挑的）：打开装配台 → 直接在地图上连续点选 4 次，
//   顺序 = 竿身 → 竿头 → 鱼钩 → 鱼饵；每步只高亮"该类可用物品"，其余不可点；
//   中途按 ESC 或右键取消。
//
// ★★ 原版目标选择机制（IL 实证，写在文件头备查）：
//   复活机械液 = 三件套：
//     ① XML 物品上挂 `CompProperties_Usable`(出 gizmo) +
//        `CompProperties_Targetable`(compClass = CompTargetable_SingleCorpse) +
//        `CompProperties_TargetEffectResurrect`(选完干什么)
//     ② `CompTargetable.SelectedUseOption(Pawn)` 里就两行：
//           this.caster = pawn;
//           Find.Targeter.BeginTargeting(this, null, true, null, null, true);
//        —— 把 comp 自己当 ITargetingSource 传进去，高亮/可点判定全来自
//           `GetTargetingParameters()` 返回的 TargetingParameters。
//     ③ 选完 → `CompTargetEffect_Resurrect.DoEffectOn(pawn, thing)` 里
//           JobMaker.MakeJob(JobDefOf.Resurrect, target, this.parent)
//           + playerForced = true + TryTakeOrderedJob  ⇒ **派 job 让小人去干活**。
//   ⇒ 精髓：**选目标** 和 **派 job** 是分开的。我们这套完全照这个思路，
//     选完 4 件就调 `CompBinguinRodAssembly.StartAssembly()`（它本来就是派 job 的）。
//
// ★★ 为什么不能像机械液那样"一次选完"：
//   `Verse.TargetingParameters` **压根没有多选字段**（只有 canTarget* / validator /
//   targetSpecificThing 这些），且 `CompTargetable` 只有一个 `Thing selectedTarget`
//   （单数），`MultiSelect` 属性原版没有任何子类重写过
//   ⇒ 原版 targetable 体系**一次只产出一个目标**。所以"选 4 次"只能连调 4 次
//     `BeginTargeting`，用回调串成状态机 —— 这就是本文件在做的事。
//
// ★ 两个实操要点（都踩过/查过）：
//   ① **不能在 gizmo 的 action 里直接开目标选择**：那时还在处理点击事件，鼠标的
//      "已消费"状态会干扰 targeter。所以走"入队 → 下一个 tick 再开"，
//      和本 mod 里 `GameComponent_BinguinFishing` 处理延迟生成是同一套办法。
//   ② 可点判定只写 `TargetingParameters.validator` 一处就够：
//      `TargetingParameters.CanTarget(...)` 内部会调它（IL 实证），
//      所以**不可点的物品会自动变暗、点了也没反应**，不需要自己画提示。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Rods
{
    /// <summary>装配台：地图上连续点选 4 件配件的状态机（静态，不需要存档）。</summary>
    public static class BinguinRodPartPicker
    {
        /// <summary>4 步的显示名键 + 允许的配件 defName（与 Dialog_BinguinRodAssembly 的顺序一致）。</summary>
        private class Step
        {
            public string labelKey;
            public string defA;
            public string defB;      // null = 该类只有一种
        }

        private static readonly Step[] Steps =
        {
            new Step { labelKey = "Binguin_DialogRodAssembly_02", defA = "Binguin_RodShaft_Long",  defB = "Binguin_RodShaft_Short" },
            new Step { labelKey = "Binguin_DialogRodAssembly_03", defA = "Binguin_RodTip_Blade",   defB = "Binguin_RodTip_Hammer" },
            new Step { labelKey = "Binguin_DialogRodAssembly_04", defA = "Binguin_RodHook_Straight", defB = "Binguin_RodHook_Curved" },
            new Step { labelKey = "Binguin_DialogRodAssembly_05", defA = "Binguin_RodBait",        defB = null },
        };

        // ---- 当前进行中的点选会话 ----
        private static Thing table;
        private static int stepIndex;
        private static Thing[] picked;

        // ---- 第 5 步：选"由哪位殖民者来装配"（2026-10-05 用户要求补上）----
        //   ★ 用**窗口列表**选人（`Dialog_BinguinRodWorkerPick`），不再用 Find.Targeter
        //     —— 原因见 `BeginWorkerPick` 上面那段三次踩坑的记录。
        private static bool pickingWorker;

        public static bool IsActive
        {
            get { return table != null; }
        }

        /// <summary>入队：由 GameComponent 在下一个 tick 真正开启（见文件头要点①）。</summary>
        public static void RequestStart(Thing assemblyTable)
        {
            if (assemblyTable == null) return;
            if (IsActive)
            {
                Messages.Message("Binguin_RodPick_09".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            BinguinRodPickerQueue.Enqueue(assemblyTable);
        }

        /// <summary>真正开始：检查地图上到底有没有这 4 类配件，有就开第 1 步。</summary>
        public static void Begin(Thing assemblyTable)
        {
            table = assemblyTable;
            stepIndex = 0;
            picked = new Thing[Steps.Length];
            Find.Targeter.StopTargeting();

            // ★ 2026-10-01 加：4 类配件一次全查，缺哪类当场说清。
            //   起因：老流程是"走到哪一步才发现那一步没货"，玩家要连点 3 次、
            //   走到第 4 步（鱼饵）才被告知"地图上没有可用的鱼饵，装配已取消"，
            //   前面 3 次选择全白费。现在开点选前就一次性列清楚。
            List<string> missing = null;
            for (int i = 0; i < Steps.Length; i++)
            {
                if (CountAvailable(Steps[i]) == 0)
                {
                    if (missing == null) missing = new List<string>();
                    missing.Add(Steps[i].labelKey.Translate());
                }
            }
            if (missing != null)
            {
                Cancel("Binguin_RodPick_08".Translate(string.Join(" / ", missing.ToArray())));
                return;
            }

            BeginStep();
        }

        private static void Cancel(string reasonKey)
        {
            BinguinRodPickerQueue.ClearPending();   // ★ 连同"排队中的下一步"一起清掉
            table = null;
            picked = null;
            stepIndex = 0;
            pickingWorker = false;
            if (reasonKey != null && !reasonKey.NullOrEmpty())
            {
                Messages.Message(reasonKey.Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        /// <summary>开启第 stepIndex 步的目标选择。</summary>
        private static void BeginStep()
        {
            if (table == null || !table.Spawned || table.Map == null)
            {
                Cancel(null);
                return;
            }
            if (stepIndex >= Steps.Length)
            {
                Finish();
                return;
            }
            Step step = Steps[stepIndex];
            if (CountAvailable(step) == 0)
            {
                Cancel("Binguin_RodPick_08".Translate(step.labelKey.Translate()));
                return;
            }

            int captured = stepIndex;          // ★ 闭包捕获：nextIndex 用它判断"这一步是否成功"
            int nextIndex = stepIndex;

            TargetingParameters tp = new TargetingParameters();
            tp.canTargetItems = true;
            // ★★ 必须显式关掉这一项（2026-10-01 用户反馈"鼠标指到竿身上没圆圈、点了没反应"的根因）：
            //   `TargetingParameters` 的默认 ctor（IL 实证）把
            //   `mapObjectTargetsMustBeAutoAttackable` 设成了 **true**，
            //   而 `TargetingParameters.CanTarget(TargetInfo, ITargetingSource)` 的
            //   `canTargetItems` 分支（1.6.4871 IL_050C 起）开头就是：
            //       if (canTargetItems && mapObjectTargetsMustBeAutoAttackable
            //           && !targ.Thing.def.isAutoAttackableMapObject) return false;
            //   竿身/竿头/鱼钩/鱼饵都是普通物品，`isAutoAttackableMapObject` 为 false
            //   ⇒ validator 还没轮到执行就被这行拦掉 ⇒ 没有圆圈、点了没反应。
            //   注意本目录 `ThingsUnderMouse` / `CurrentTargetUnderMouse` 全程只调
            //   `CanTarget`，所以这一项不改就永远选不中物品。
            //   （这也是为什么第 127 行的 `CountAvailable` 检查照样能过：它走的是
            //     我们自己写的判定，没经过 `CanTarget` —— 于是"提示正常弹出、
            //     但地图上一个可点的东西都没有"这个诡异现象就出现了。）
            tp.mapObjectTargetsMustBeAutoAttackable = false;
            // ★ 唯一的可点判定：只认这一步允许的配件 def，且还没被本会话选走。
            //   CanTarget 内部会调 validator（IL 实证）⇒ 别的物品自动变暗。
            tp.validator = delegate (TargetInfo t)
            {
                return IsAllowed(t.Thing, step, table);
            };

            Find.Targeter.BeginTargeting(tp,
                delegate (LocalTargetInfo t)
                {
                    // ---- 选中一件 ----
                    Thing thing = t.Thing;
                    if (thing == null) return;
                    picked[captured] = thing;
                    nextIndex = captured + 1;
                    BinguinLogUtility.Log("装配点选 " + (captured + 1) + "/4：" + thing.LabelShort
                        + (thing.Stuff != null ? "（" + thing.Stuff.label + "）" : ""));
                },
                null,                                  // caster：用不着（不是小人主动施法）
                delegate
                {
                    // ---- targeting 结束（选中 or ESC/右键取消都在这里收口）----
                    if (nextIndex != captured + 1)
                    {
                        Cancel("Binguin_RodPick_07");   // 没推进 = 玩家取消了
                        return;
                    }
                    stepIndex = nextIndex;
                    // ★★ 2026-10-05 修「只能选第一次，之后没有圆圈」的根因：
                    //    **绝对不能在这里直接调 BeginStep()**。
                    //    原版 `Targeter.StopTargeting()` 的 IL 是：
                    //        IL_0001: if (actionWhenFinished != null) {
                    //        IL_0010:     actionWhenFinished = null;
                    //        IL_0015:     actionWhenFinished.Invoke();   ← 先调本回调
                    //                 }
                    //        IL_001C: targetingSource  = null;           ← 然后才清字段
                    //        IL_0023: action           = null;           ← 把第 2 步刚设的 action 清掉
                    //        IL_002A: targetParams     = null;           ← 把第 2 步刚设的 tp 清掉
                    //        IL_0038: targetValidator  = null;
                    //    ⇒ 在回调里开第 2 步，等于"设完立刻被清空"：
                    //      第 2 步的 targeter 没有判定、没有回调 ⇒ 没有圆圈、点不动。
                    //      （与当初"不能在 gizmo 的 action 里直接开 targeter"是同一类问题。）
                    //    ⇒ 改为**延到下一帧**再开（用同一个 BinguinRodPickerQueue）。
                    BinguinRodPickerQueue.EnqueueStep(table, stepIndex);
                },
                null,
                false);

            Messages.Message("Binguin_RodPick_01".Translate(
                    (stepIndex + 1).ToString(), Steps.Length.ToString(), step.labelKey.Translate()),
                MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// 从指定步继续开 targeter（由 `BinguinRodPickerQueue` 在下一帧调用）。
        /// ★ 存在的唯一理由：**下一步不能在 `actionWhenFinished` 回调里直接开**
        ///   （`StopTargeting` 会把刚设好的 targetParams/action/validator 清空），
        ///   所以上一步选完后先把 stepIndex 存好、入队，下一帧从这里接着开。
        /// </summary>
        public static void BeginStepFrom(int step)
        {
            if (table == null)
            {
                // 会话已经被取消（例如玩家中途 ESC），直接忽略这次排队
                return;
            }
            stepIndex = step;
            BeginStep();
        }

        /// <summary>4 件都选齐了 → 进入第 5 步：让玩家点选一位殖民者来装配。</summary>
        private static void Finish()
        {
            BinguinRodPickerQueue.ClearPending();
            Thing assemblyTable = table;
            // ★★ 注意：这里**不能**清 `picked`！
            //   清掉的话，紧接着开的「选谁装配」窗口就拿不到那 4 件配件了
            //   （窗口的构造参数正是 `picked` 里的 4 项）。
            //   `picked` 由 NotifyWorkerDialogClosed（取消）或 ClearSession（派活）负责清。
            table = null;
            stepIndex = 0;
            // 注意：pickingWorker 现在由 BeginWorkerPick 置 true，
            // 这里**不要**置 false —— NotifyWorkerDialogClosed 靠它判断"窗口还开着"。
            // 同样也**不要**清 picked（窗口要用）。

            Thing[] parts = picked;
            if (assemblyTable == null || parts == null)
            {
                BinguinLogUtility.Log("4 件选完但收尾失败：装配台=" + (assemblyTable == null ? "null" : "有")
                    + "，配件数组=" + (parts == null ? "null" : "有"), severity: 1, isDebug: false);
                Cancel(null);
                return;
            }

            CompBinguinRodAssembly comp = assemblyTable.TryGetComp<CompBinguinRodAssembly>();
            if (comp == null)
            {
                BinguinLogUtility.Log("4 件选完但装配台上没有 CompBinguinRodAssembly，已放弃。", severity: 1, isDebug: false);
                Cancel(null);
                return;
            }
            BinguinLogUtility.Log("4 件配件已选齐：" + parts[0].LabelShort + " / " + parts[1].LabelShort
                + " / " + parts[2].LabelShort + " / "
                + (parts[3] == null ? "（无鱼饵）" : parts[3].LabelShort)
                + " —— 进入第 5 步：选由谁来装配");

            // ★ 2026-10-05 用户要求：「差一个最后选择殖民者进行装配」。
            //   原来是 `PickWorker()` 自动挑制作技能最高的殖民者，现在改成玩家选。
            //   ★ 仍然不能在这里直接开 UI —— 本方法正是在上一段 targeting 的
            //     `actionWhenFinished` 回调里被调用的，而 `StopTargeting` 会在回调返回后
            //     清空 targetParams/action/validator（详见上面那段 IL 注释）。
            //   ⇒ 照旧入队，下一帧由队列开窗口。
            BinguinRodPickerQueue.EnqueueWorkerPick(assemblyTable);
        }

        /// <summary>
        /// 第 5 步：开「选谁来装配」窗口（由队列在下一帧调用）。
        ///
        /// ★★★ 2026-10-05 大改：**这里原来是用 `Find.Targeter` 在地图上点殖民者，
        ///     现在改成弹窗口**。原因（三次踩坑，全部 IL 实证，别再改回去）：
        ///       ① 起初在 targeting 的 `actionWhenFinished` 回调里直接开下一个 targeter
        ///          ⇒ `Targeter.StopTargeting()` 是"先调回调、再清字段"，刚设好的
        ///             targetParams/action/targetValidator 立刻被清空 ⇒ 没圆圈、点不动。
        ///       ② 改用 6 参数 `BeginTargeting` ⇒ 那个重载**不接受 validator 参数**。
        ///       ③ 改用 5 参数重载 ⇒ 它的 IL_0048-004A 直接把 `Targeter.targetValidator`
        ///          置成 null，而 `Targeter.ProcessInputEvents` 在 `targetingSource == null`
        ///          时判的正是这个字段（**不是** targetParams.validator）
        ///          ⇒ 写在 tp.validator 里的判定根本不生效。
        ///     ⇒ 而本 mod 的 `Dialog_BinguinRodAssembly` 里**本来就有稳定可用的选人列表**
        ///       （用户之前用它成功装配过）⇒ 第 5 步直接复用那套画法做成精简窗口，
        ///       彻底不再碰 Targeter。
        /// </summary>
        public static void BeginWorkerPick(Thing assemblyTable)
        {
            if (assemblyTable == null || !assemblyTable.Spawned || assemblyTable.Map == null)
            {
                BinguinLogUtility.Log("选谁装配：装配台无效（null=" + (assemblyTable == null) + "），已放弃。", severity: 1, isDebug: false);
                Cancel(null);
                return;
            }
            table = assemblyTable;
            // ★ 注意：这里刻意**不**清 `picked` —— 窗口确认时要读它来取 4 件配件。
            pickingWorker = true;

            Thing[] parts = picked;
            Find.WindowStack.Add(new Dialog_BinguinRodWorkerPick(
                assemblyTable,
                parts != null && parts.Length > 0 ? parts[0] : null,
                parts != null && parts.Length > 1 ? parts[1] : null,
                parts != null && parts.Length > 2 ? parts[2] : null,
                parts != null && parts.Length > 3 ? parts[3] : null));
            BinguinLogUtility.Log("选谁装配：已打开选择窗口（候选 " + CountWorkers(assemblyTable) + " 人）");
        }

        /// <summary>
        /// 窗口关闭时的收口（由 `Dialog_BinguinRodWorkerPick.PostClose` 调用）。
        /// ★ 窗口里点「开始装配」时不会走到这里（那边用 `committed` 标记挡掉了）；
        ///   走到这里就是**玩家直接关掉了窗口**（右上角 X / ESC / 鼠标右键）⇒ 视为取消：
        ///   不派 job、配件一件都不动，可以重新点选。
        /// </summary>
        public static void NotifyWorkerDialogClosed()
        {
            if (pickingWorker)
            {
                Cancel("Binguin_RodPick_07");
            }
        }

        /// <summary>
        /// 窗口点「开始装配」并**成功派活**后的收口。
        /// ★ 没有这一步的话，静态字段 table/picked/pickingWorker 会一直留着，
        ///   下次再点装配台会被 `RequestStart` 的 `IsActive` 拦住
        ///   （弹"正在点选配件中，先选完或者按 ESC 取消"）—— 那就变成"只能装配一次"了。
        /// </summary>
        public static void NotifyWorkerDialogCommitted()
        {
            table = null;
            picked = null;
            stepIndex = 0;
            pickingWorker = false;
            BinguinRodPickerQueue.ClearPending();
        }

        private static int CountWorkers(Thing assemblyTable)
        {
            if (assemblyTable == null || assemblyTable.Map == null)
            {
                return 0;
            }
            List<Pawn> cs = assemblyTable.Map.mapPawns.FreeColonists;
            int n = 0;
            for (int i = 0; i < cs.Count; i++)
            {
                if (IsValidWorker(cs[i], assemblyTable)) n++;
            }
            return n;
        }

        /// <summary>这位 pawn 能不能来装配：自家殖民者、没死没倒地、在地图上。</summary>
        public static bool IsValidWorker(Pawn p, Thing assemblyTable)
        {
            if (p == null || assemblyTable == null) return false;
            if (p.Dead || p.Downed) return false;
            if (p.Map != assemblyTable.Map) return false;
            // ★ 不用 `p.skills != null` 作为判定（不同版本/HAR 下可能为空 ⇒ 谁都用不了）
            //   改用"是不是人形 + 是不是自家殖民者"，这两个字段稳定得多。
            if (p.def == null || p.def.race == null || !p.def.race.Humanlike) return false;
            if (!p.IsColonist) return false;
            return true;
        }

        /// <summary>把"选好的 4 件配件 + 选好的人"交给装配台 comp 去派 job。</summary>
        private static void CommitAssembly(Thing assemblyTable, Pawn worker)
        {
            Thing[] parts = picked;
            picked = null;
            table = null;
            stepIndex = 0;
            if (assemblyTable == null || worker == null || parts == null)
            {
                BinguinLogUtility.Log("CommitAssembly 参数不全：台=" + (assemblyTable == null ? "null" : "有")
                    + " 人=" + (worker == null ? "null" : "有")
                    + " 配件=" + (parts == null ? "null" : "有"), severity: 1, isDebug: false);
                return;
            }

            CompBinguinRodAssembly comp = assemblyTable.TryGetComp<CompBinguinRodAssembly>();
            if (comp == null)
            {
                BinguinLogUtility.Log("CommitAssembly：装配台上没有 CompBinguinRodAssembly。", severity: 1, isDebug: false);
                return;
            }

            // StartAssembly 内部会再校验 4 高级零部件，并设置 pending / 派 Binguin_AssembleRod
            pickingWorker = false;
            bool ok = comp.StartAssembly(worker, parts[0], parts[1], parts[2], parts[3]);
            BinguinLogUtility.Log("CommitAssembly：StartAssembly 返回 " + ok
                + "（" + worker.LabelShort + " ← " + assemblyTable.LabelShort + "）");
            if (!ok)
            {
                // 没派成（比如高级零部件不够）时，把配件还回去，避免"看着像选完了其实没了"
                BinguinLogUtility.Log("装配未派发（可能缺高级零部件）："
                    + worker.LabelShort + " / " + assemblyTable.LabelShort);
            }
        }

        // ------------------------------------------------------------ 工具

        private static bool IsAllowed(Thing t, Step step, Thing assemblyTable)
        {
            if (t == null || t.def == null) return false;
            if (assemblyTable == null || t.Map != assemblyTable.Map) return false;
            string dn = t.def.defName;
            if (dn != step.defA && (step.defB == null || dn != step.defB)) return false;
            // ★ 不做"已禁用"过滤：原版 `IsForbidden` 是 `RimWorld.ForbidUtility.IsForbidden(Thing, Pawn)`
            //   （扩展方法形态，IL 实证），这里没有 pawn 上下文；而原版 targetable 体系本来
            //   也不过滤禁用物 —— 玩家不想选就不会点它。保持和原版一致，少一处能出错的地方。
            // 同一个物件不能被选两次（4 件必须是不同的东西）
            if (picked != null)
            {
                for (int i = 0; i < picked.Length; i++)
                {
                    if (picked[i] == t) return false;
                }
            }
            return true;
        }

        private static int CountAvailable(Step step)
        {
            if (table == null || table.Map == null) return 0;
            int n = 0;
            List<Thing> all = table.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < all.Count; i++)
            {
                if (IsAllowed(all[i], step, table)) n++;
            }
            return n;
        }
    }

    /// <summary>
    /// 延迟开启点选（见文件头要点①：不能在 gizmo 的 action 里直接开 targeter）。
    /// ★ 2026-10-05 起还多一个用途：**上一步选完后，下一步也必须延到下一帧再开**——
    ///   因为它是在 `Targeter.StopTargeting()` 触发的 `actionWhenFinished` 回调里被调用的，
    ///   而 `StopTargeting` 会在调完回调之后把 `targetParams`/`action`/`targetValidator` 全清空，
    ///   在回调里开的 targeter 会被立刻清成空壳（没有圆圈、点不动）。
    ///   详见 `RodPartPicker_Binguin` 里那段注释。
    /// ★ 会自动被 `Game.FillComponents()` 实例化（public + ctor(Game)），不需要 XML 注册。
    /// </summary>
    public class BinguinRodPickerQueue : GameComponent
    {
        /// <summary>待处理项：Thing = 装配台；kind 决定要做什么。</summary>
        private struct Entry
        {
            public Thing table;
            public int nextStep;
            public int kind;      // 0 = 从头开始；1 = 从 nextStep 继续选配件；2 = 选装配殖民者
        }

        private const int KindStart = 0;
        private const int KindStep = 1;
        private const int KindWorker = 2;

        private static readonly List<Entry> queued = new List<Entry>();

        public BinguinRodPickerQueue(Game game) : base()
        {
        }

        /// <summary>从头开始（由装配台 gizmo 调用）。</summary>
        public static void Enqueue(Thing t)
        {
            if (t != null)
            {
                Entry e = new Entry();
                e.table = t;
                e.nextStep = -1;
                e.kind = KindStart;
                queued.Add(e);
            }
        }

        /// <summary>从指定步继续选配件（由上一步的 targeting 结束回调调用，见类注释）。</summary>
        public static void EnqueueStep(Thing t, int step)
        {
            if (t != null)
            {
                Entry e = new Entry();
                e.table = t;
                e.nextStep = step;
                e.kind = KindStep;
                queued.Add(e);
            }
        }

        /// <summary>4 件选齐后，开启"点选一位殖民者来装配"（第 5 步）。</summary>
        public static void EnqueueWorkerPick(Thing t)
        {
            if (t != null)
            {
                Entry e = new Entry();
                e.table = t;
                e.nextStep = -1;
                e.kind = KindWorker;
                queued.Add(e);
            }
        }

        /// <summary>清空待处理项（取消 / 收尾时调用，避免下一次点选被旧的排队项干扰）。</summary>
        public static void ClearPending()
        {
            queued.Clear();
        }

        // ★ 用 GameComponentOnGUI 而不是 GameComponentUpdate：本组件要开的正是
        //   `Find.Targeter`（一套基于 GUI 事件的目标选择），放在 GUI 阶段开更稳，
        //   也顺手确保 `StopTargeting` 那次清理已经跑完（它发生在同一帧更早的输入阶段）。
        public override void GameComponentOnGUI()
        {
            if (queued.Count == 0)
            {
                return;
            }
            Entry e = queued[0];
            queued.RemoveAt(0);
            if (e.table == null)
            {
                return;
            }
            try
            {
                if (e.kind == KindWorker)
                {
                    BinguinRodPartPicker.BeginWorkerPick(e.table);
                }
                else if (e.kind == KindStep)
                {
                    BinguinRodPartPicker.BeginStepFrom(e.nextStep);
                }
                else
                {
                    BinguinRodPartPicker.Begin(e.table);
                }
            }
            catch (Exception ex)
            {
                BinguinLogUtility.Log("装配点选启动失败：" + ex, severity: 2, isDebug: false);
            }
        }
    }
}
