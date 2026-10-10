// ============================================================================
// 威灵装甲装填（2026-08-20 v2：用户要求小人真实搬运装填，不能直接扣库存）
// job：targetA = 库存物资（迫击炮弹或化合燃料），targetB = 穿着威灵装甲的 pawn
// 流程：走到物资旁拿起 → 走到穿戴者身边 → 装填入舱：
//   · 炮弹：CompBinguinWillingCannon.TryLoadOneShell(defName)（舱 3 上限）
//   · 燃料：TryAddFuel(数量)（上限 60）；放不下的余量留在身上
// ★ job 由「装填炮弹/补充燃料」gizmo 的对话框发起（指派最近殖民者执行）
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

using Binguin.Helper;

namespace Binguin.Feature.WillingCannon
{
    public class JobDriver_BinguinWillingLoad : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn pawn = this.pawn;
            Thing item = job.GetTarget(TargetIndex.A).Thing;
            if (item != null && !pawn.Reserve(item, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            Pawn wearer = job.GetTarget(TargetIndex.B).Thing as Pawn;
            if (wearer != null && !pawn.Reserve(wearer, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);

            // 走到穿戴者身边（ClosestTouch；穿戴者走动时若距离过远会在交付时判定失败重来）
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);

            Toil load = new Toil();
            load.defaultCompleteMode = ToilCompleteMode.Instant;
            load.initAction = delegate
            {
                Pawn actor = load.actor;
                Pawn wearer = job.GetTarget(TargetIndex.B).Thing as Pawn;
                if (actor == null || wearer == null || wearer.Destroyed)
                {
                    return;
                }
                // 距离检查：目标走远就放弃本次（job 会自然结束，可再下达）
                if (!actor.Position.AdjacentTo8WayOrInside(wearer.Position)
                    && actor.Position.DistanceTo(wearer.Position) > 2.5f)
                {
                    return;
                }
                if (actor.carryTracker == null || actor.carryTracker.CarriedThing == null)
                {
                    return;
                }
                CompBinguinWillingCannon comp = null;
                if (wearer.apparel != null && wearer.apparel.WornApparel != null)
                {
                    for (int i = 0; i < wearer.apparel.WornApparel.Count; i++)
                    {
                        CompBinguinWillingCannon c = wearer.apparel.WornApparel[i]
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
                    Messages.Message("Binguin_JobDriverWillingLoad_01".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                Thing carried = actor.carryTracker.CarriedThing;
                // ★ v9：区分炮弹与化合燃料
                //   · 炮弹：装 1 发入舱（炮弹类 = def 有 projectileWhenLoaded）
                //   · 化合燃料：倒入燃料舱（按剩余空间限量，余量留在手上带回）
                bool isFuel = carried.def != null && carried.def.defName == "Chemfuel";
                if (isFuel)
                {
                    int take = comp.TryAddFuelFromCarried(carried);
                    if (take > 0)
                    {
                        Thing part = carried.SplitOff(take);
                        if (part != null && !part.Destroyed)
                        {
                            part.Destroy();
                        }
                        BinguinLogUtility.Log("威灵装甲补充化合燃料 " + take + "（当前 "
                            + comp.FuelNow.ToString("0") + "/" + comp.FuelMaxNow.ToString("0") + "）。");
                    }
                }
                else if (comp.TryLoadOneShell(carried.def != null ? carried.def.defName : ""))
                {
                    Thing part = carried.SplitOff(1);
                    if (part != null && !part.Destroyed)
                    {
                        part.Destroy();
                    }
                    Messages.Message("Binguin_JobDriverWillingLoad_02".Translate()
                        + (carried.def != null ? carried.def.label : "") + "。",
                        wearer, MessageTypeDefOf.PositiveEvent, false);
                }
            };
            yield return load;
        }
    }
}
