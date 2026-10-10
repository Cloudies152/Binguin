// ============================================================================
// 放苗搬运（2026-08-20 用户需求）
// 殖民者取一条指定鱼（库存/地面）→ 走到鱼池旁 → 放入（鱼销毁，
// 池 TryStock：空池定种 / 同种 +1）
// job：targetA = 鱼（Job 创建时已在 Dialog 找到），targetB = 鱼池
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class JobDriver_BinguinPondStock : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn pawn = this.pawn;
            Thing fish = job.GetTarget(TargetIndex.A).Thing;
            if (fish != null && !pawn.Reserve(fish, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            Thing pond = job.GetTarget(TargetIndex.B).Thing;
            if (pond != null && !pawn.Reserve(pond, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);

            // 1) 走到鱼旁并拿起
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            Toil pick = Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return pick;

            // 2) 走到鱼池旁
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);

            // 3) 放入：先把鱼放到池边地面（离开搬运状态），入池成功才销毁
            //    （TryStock 失败 = 异种/满池 → 鱼留在地面，不浪费）
            Toil put = new Toil();
            put.defaultCompleteMode = ToilCompleteMode.Instant;
            put.initAction = delegate
            {
                Pawn actor = put.actor;
                Thing pond = job.GetTarget(TargetIndex.B).Thing;
                if (actor == null || pond == null || pond.Destroyed
                    || actor.carryTracker == null || actor.carryTracker.CarriedThing == null)
                {
                    return;
                }
                Thing fish = actor.carryTracker.CarriedThing;
                ThingDef fishDef = fish.def;
                // 1) 先放到池边地面（Near 模式必然找得到空格，远离搬运状态）
                Thing dropped = null;
                bool droppedOk = actor.carryTracker.TryDropCarriedThing(pond.Position,
                    ThingPlaceMode.Near, out dropped);
                if (!droppedOk)
                {
                    droppedOk = actor.carryTracker.TryDropCarriedThing(actor.Position,
                        ThingPlaceMode.Near, out dropped);
                }
                if (!droppedOk)
                {
                    return;
                }
                // 2) 入池：成功 → 落地鱼销毁（= 鱼进了池）；失败 → 鱼留在地面
                CompBinguinFishPond pondComp = pond.TryGetComp<CompBinguinFishPond>();
                bool stocked = pondComp != null && pondComp.TryStock(fishDef.defName);
                if (stocked)
                {
                    if (dropped != null && !dropped.Destroyed)
                    {
                        dropped.Destroy();
                    }
                    Messages.Message("Binguin_JobDriverPondStock_01".Translate() + (fishDef.label ?? "") + "Binguin_JobDriverPondStock_02".Translate(),
                        pond, MessageTypeDefOf.PositiveEvent, false);
                }
                else
                {
                    Messages.Message("Binguin_JobDriverPondStock_03".Translate(),
                        pond, MessageTypeDefOf.RejectInput, false);
                }
            };
            yield return put;
        }
    }
}
