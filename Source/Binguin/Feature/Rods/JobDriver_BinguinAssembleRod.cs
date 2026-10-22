// 装配搬运队列只在预定阶段建立；数量和顺序随任务保存，读档不重新扫描材料。
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Binguin.Helper;

namespace Binguin.Feature.Rods
{
    public class JobDriver_BinguinAssembleRod : JobDriver
    {
        private List<Thing> haulTargets = new List<Thing>();
        private List<int> haulCounts = new List<int>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref haulTargets, "haulTargets", LookMode.Reference);
            Scribe_Collections.Look(ref haulCounts, "haulCounts", LookMode.Value);
        }
        private CompBinguinRodAssembly AssemblyComp()
        {
            Thing table = job.targetA.Thing;
            return table != null ? table.TryGetComp<CompBinguinRodAssembly>() : null;
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;
            CompBinguinRodAssembly comp = AssemblyComp();
            if (comp == null) return false;
            haulTargets.Clear();
            haulCounts.Clear();
            Thing[] parts = { comp.pendingShaft, comp.pendingTip, comp.pendingHook, comp.pendingBait };
            foreach (Thing part in parts)
            {
                if (part == null || part.Destroyed || !pawn.Reserve(part, job, 1, 1, null, errorOnFailed)) return false;
                haulTargets.Add(part);
                haulCounts.Add(1);
            }
            List<Thing> spacers = new List<Thing>(pawn.Map.listerThings.ThingsOfDef(ThingDefOf.ComponentSpacer));
            spacers.Sort((a, b) => a.Position.DistanceToSquared(comp.parent.Position).CompareTo(b.Position.DistanceToSquared(comp.parent.Position)));
            int needed = 4;
            foreach (Thing sp in spacers)
            {
                if (needed == 0) break;
                if (sp.IsForbidden(pawn) || !pawn.CanReserveAndReach(sp, PathEndMode.ClosestTouch, Danger.Some)) continue;
                int count = System.Math.Min(needed, sp.stackCount);
                if (!pawn.Reserve(sp, job, 1, count, null, errorOnFailed)) continue;
                haulTargets.Add(sp);
                haulCounts.Add(count);
                needed -= count;
            }
            return needed == 0;
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            // 不按运行时物品是否销毁、已搬数量删减 Toil；失效目标由对应 Toil 终止任务。
            for (int i = 0; i < haulTargets.Count; i++)
            {
                int index = i;
                yield return Toils_General.Do(delegate
                {
                    job.SetTarget(TargetIndex.B, haulTargets[index]);
                    job.count = haulCounts[index];
                });
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch).FailOnDespawnedOrNull(TargetIndex.B);
                yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, false, true);
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
                yield return ToilDropAtTable();
            }
            yield return Toils_General.Do(delegate
            {
                CompBinguinRodAssembly comp = AssemblyComp();
                Thing rod = comp != null ? comp.SpawnUnfinishedRod() : null;
                if (rod == null)
                {
                    Messages.Message("Binguin_AssembleRod_SpawnFailed".Translate(), job.targetA.Thing, MessageTypeDefOf.RejectInput, false);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                CompBinguinRodAssembly.StartWorkOnRod(pawn, rod);
            });
        }
        private Toil ToilDropAtTable()
        {
            Toil toil = new Toil();
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                if (actor == null)
                {
                    return;
                }
                Thing carried = actor.carryTracker != null ? actor.carryTracker.CarriedThing : null;
                if (carried == null)
                {
                    return;
                }
                Map map = actor.Map;
                if (map == null)
                {
                    return;
                }

                Building table = job.targetA.Thing as Building;
                IntVec3 preferred = table != null ? table.InteractionCell : job.targetA.Cell;

                Thing placed = null;
                string how = "";

                // ① 原版做法：目标格 + Near（会找旁边的空格）
                if (preferred.IsValid && preferred.InBounds(map))
                {
                    if (actor.carryTracker.TryDropCarriedThing(preferred, ThingPlaceMode.Near, out placed, null))
                    {
                        how = "Near@" + preferred;
                    }
                }
                // ② 直接放到目标格本身
                if (placed == null && preferred.IsValid && preferred.InBounds(map))
                {
                    if (actor.carryTracker.TryDropCarriedThing(preferred, ThingPlaceMode.Direct, out placed, null))
                    {
                        how = "Direct@" + preferred;
                    }
                }
                // ③ 放到小人脚下
                if (placed == null)
                {
                    if (actor.carryTracker.TryDropCarriedThing(actor.Position, ThingPlaceMode.Direct, out placed, null))
                    {
                        how = "Direct@" + actor.Position + "(脚下)";
                    }
                }

                if (placed != null)
                {
                    BinguinLogUtility.Log("配件已放下：" + placed.LabelShort + " → " + how);
                }
                else
                {
                    // ④ 全失败：保持拿着（绝不销毁）。后面的 SpawnUnfinishedRod 仍能
                    //    通过 holder 转移把配件从手上收进半成品，所以功能不受影响。
                    BinguinLogUtility.Log("配件暂时没找到落点，继续由 " + actor.LabelShort
                        + " 拿着：" + carried.LabelShort + "（稍后仍会被装进半成品）");
                }
            };
            return toil;
        }
    }
}
