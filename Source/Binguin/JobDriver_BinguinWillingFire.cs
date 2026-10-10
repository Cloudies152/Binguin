// ============================================================================
// 威灵装甲炮击（2026-08-21 v10）
//   原版迫击炮手感：命令开火 → 小人原地站定瞄准（1 秒，可被打断取消，
//   像原版射击 warmup）→ 到点抛射。
//   · job.targetA = 落点（格）
//   · toil 1：站定 60 tick，面向落点（原版 stance 式瞄准；玩家移动/其他
//     命令会打断 job → 本次取消，不发射）
//   · toil 2：ExecuteShot（内部再校验冷却/弹/燃料/射程）
//   · 瞄准视觉（扇形充能 + 落点圈）由 GameComponent 每帧调用
//     CompBinguinWillingCannon.AimDrawTick() 绘制（静态 aimPawn 状态）
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class JobDriver_BinguinWillingFire : JobDriver
    {
        private const int AimTicks = 60;   // 1 秒

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;   // 不占用任何目标（落点无需 reservation）
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            IntVec3 dest = job.targetA.Cell;
            if (!dest.IsValid)
            {
                yield break;
            }

            // ---- toil 1：站定瞄准 1 秒 ----
            Toil aim = new Toil();
            aim.defaultCompleteMode = ToilCompleteMode.Delay;
            aim.defaultDuration = AimTicks;
            aim.initAction = delegate
            {
                Pawn actor = aim.actor;
                if (actor == null || actor.Destroyed || !actor.Spawned)
                {
                    return;
                }
                actor.rotationTracker.FaceCell(dest);
                // 注册瞄准视觉（GameComponent 每帧据此画扇形充能 + 落点圈）
                CompBinguinWillingCannon.StartAimVisual(actor, dest);
            };
            aim.tickAction = delegate
            {
                Pawn actor = aim.actor;
                if (actor == null || actor.Destroyed || !actor.Spawned)
                {
                    return;
                }
                // 持续面向落点（像原版瞄准）
                actor.rotationTracker.FaceCell(dest);
                // 强制停走：站定瞄准，不得移动
                if (actor.pather != null && actor.pather.Moving)
                {
                    actor.pather.StopDead();
                }
            };
            aim.AddFinishAction(delegate
            {
                CompBinguinWillingCannon.ClearAimVisual();
            });
            yield return aim;

            // ---- toil 2：开火 ----
            Toil fire = new Toil();
            fire.defaultCompleteMode = ToilCompleteMode.Instant;
            fire.initAction = delegate
            {
                CompBinguinWillingCannon.ClearAimVisual();
                Pawn actor = fire.actor;
                if (actor == null || actor.Destroyed || !actor.Spawned)
                {
                    return;
                }
                CompBinguinWillingCannon comp = null;
                if (actor.apparel != null && actor.apparel.WornApparel != null)
                {
                    for (int i = 0; i < actor.apparel.WornApparel.Count; i++)
                    {
                        CompBinguinWillingCannon c = actor.apparel.WornApparel[i]
                            .TryGetComp<CompBinguinWillingCannon>();
                        if (c != null)
                        {
                            comp = c;
                            break;
                        }
                    }
                }
                if (comp == null)
                {
                    return;
                }
                comp.ExecuteShot(dest);
            };
            yield return fire;
        }
    }
}
