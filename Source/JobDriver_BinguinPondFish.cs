// ============================================================================
// 在鱼池钓鱼
// 殖民者走到鱼池旁 → 面向鱼池 → 垂钓 500 tick → 产出 1 条该池鱼种
// （由 CompBinguinFishPond.TryCatchOne 扣鱼群并生成鱼）
//
// ★★ 2026-10-06 用户需求（含一次澄清，务必看清）：
//    原话：「把冰鹅族的钓鱼改为娱乐，即冰鹅族钓鱼的时候会增加娱乐条，
//           并且在娱乐时会主动去钓鱼」
//    澄清：「我指的是**钓鱼工作可以算娱乐**，不是指把钓鱼工作去掉变成娱乐，
//           **所以冰鹅依然会执行钓鱼工作**」
//    ⇒ 三件事同时成立：
//      ① **钓鱼工作保留**（WorkGiver 照旧给冰鹅派活，不排除任何种族）
//      ② 干活时**同时**涨娱乐条（本文件下面的 tickAction）
//      ③ 娱乐条低时也会**主动**去钓鱼（`JoyGiver_BinguinPondFishing`，另一条入口）
//
//    ★ 为什么必须显式调 `JoyUtility.JoyTickCheckEnd`（IL 实证，别删）：
//      只给 JobDef 写 `<joyKind>` **不会自动加娱乐** ——
//      `JoyTickCheckEnd` 是唯一真正调 `Need_Joy.GainJoy` 的地方，它的算法是：
//          GainJoy( extraJoyGainFactor × jobDef.joyGainRate × 0.36 / 2500 × delta,
//                   jobDef.joyKind )
//
//    ★★ 为什么必须 `job.ignoreJoyTimeAssignment = true`（关键，别删）：
//      `JoyTickCheckEnd` 里有一段（IL_0145 起）：
//          if (!job.ignoreJoyTimeAssignment
//              && !TimetableUtility.GetTimeAssignment(pawn).allowJoy
//              && !job.doUntilGatheringEnded)
//              { EndJobWith(JobCondition.16); return true; }   // ★ 直接结束 job！
//      ⇒ 钓鱼**工作**可能在"工作/睡眠"时段被派（那时 `allowJoy == false`），
//        不设这个标志的话，job 会在第一 tick 就被娱乐逻辑掐掉、
//        **鱼一条也钓不上来**。设成 true 即跳过这段检查。
//        （其余判定照旧：`needs.joy == null` 会结束 job；娱乐满按
//          `fullJoyAction` 处理 —— 下面已限制成"只有冰鹅才走娱乐逻辑"。）
//
//    ★ 只让冰鹅族吃到娱乐条：用户说的是"**冰鹅族**钓鱼的时候会增加娱乐条"。
//      别的种族（含原版人类）做同一份钓鱼工作时**不该**白拿娱乐
//      ⇒ tickAction 里先判 `BinguinRaceUtility.IsBinguin(actor)`。
//
//    ★ `joySource` 传 null 的后果：不会乘 `StatDefOf.JoyGainFactor`
//      （IL: `joySource == null` 会跳过那一段直接结算）。
//      本 mod 的鱼池建筑 XML 没写 `building.joyKind`，所以不传建筑是对的 ——
//      传了反而会因为"joySource.joyKind 与 jobDef.joyKind 不一致"报 ErrorOnce。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class JobDriver_BinguinPondFish : JobDriver
    {
        private const int WorkTicks = 500;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            // ★ 2026-09：删掉重复的 FailOn(lambda) —— FailOnDestroyedOrNull 已经
            //   覆盖同一条件，而 lambda 每次建 job 都要多分配一个闭包。

            // 1) 走到鱼池旁（任一边相邻格）
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            // 2) 面向鱼池，垂钓
            Toil fish = new Toil();
            fish.defaultCompleteMode = ToilCompleteMode.Delay;
            fish.defaultDuration = WorkTicks;
            fish.handlingFacing = true;
            fish.initAction = delegate
            {
                Pawn actor = fish.actor;
                if (actor != null && TargetA.Thing != null)
                {
                    Rot4 rot = Rot4.FromIntVec3(TargetA.Thing.Position - actor.Position);
                    actor.Rotation = rot;
                }
                // ★★ 见文件头：「钓鱼工作也可能在非娱乐时段被派」，
                //   不设这个标志会被 JoyTickCheckEnd 第一 tick 就掐掉 job。
                if (actor != null && actor.CurJob != null)
                {
                    actor.CurJob.ignoreJoyTimeAssignment = true;
                }
            };

            // ★★ 娱乐条推进（2026-10-06 用户需求：钓鱼工作也算娱乐）。
            //   每 tick 调一次；`JoyTickCheckEnd` 内部按 delta 结算。
            //   ★ 只有冰鹅族走这一步（用户指名"冰鹅族钓鱼时增加娱乐条"）。
            fish.tickAction = delegate
            {
                Pawn actor = fish.actor;
                if (actor == null)
                {
                    return;
                }
                if (!BinguinRaceUtility.IsBinguin(actor))
                {
                    return;
                }
                // 1f = 这一 tick 的 delta（job 每 tick 结算一次）。
                // ★ fullJoyAction 用 **None**（IL 实证枚举只有三个值）：
                //     EndJob = 0 / GoToNextToil = 1 / None = 2
                //   "None" 的语义是**什么都不做** —— 正是我们要的：
                //   这是钓鱼**工作**，娱乐满了也**不要**去结束/推进 job，
                //   该干完 500 tick 钓上一条鱼（用户澄清的重点）。
                //   娱乐满了 GainJoy 自己会顶在 100%，不需要我们掐 job。
                JoyUtility.JoyTickCheckEnd(actor, 1,
                    JoyTickFullJoyAction.None, 1f, null);
            };

            fish.AddFinishAction(delegate
            {
                CompBinguinFishPond comp = null;
                Map map = fish.actor != null ? fish.actor.Map : null;
                if (TargetA.Thing != null && !TargetA.Thing.Destroyed)
                {
                    comp = TargetA.Thing.TryGetComp<CompBinguinFishPond>();
                }
                // ★ 2026-09-24「钓到企鹅！」：先按 0.78% 概率判定能不能钓上一名冰鹅难民
                //   （60 天冷却）。钓到了就【不扣鱼群、不产鱼】—— 渔获已经换成冰鹅了，
                //   再扣一次池里的鱼等于白扣。
                if (map != null && comp != null && fish.actor != null
                    && BinguinFishingCatchUtility.TryReplaceCatch(fish.actor, map, "冰鹅鱼池"))
                {
                    return;
                }
                if (comp != null)
                {
                    comp.TryCatchOne();
                    // ★ 2026-08-20：自动钓鱼时不弹 Messages（避免刷屏）；
                    //   鱼直接产出在池边地上，搬运工会收走
                }
            });
            yield return fish;
        }
    }
}
