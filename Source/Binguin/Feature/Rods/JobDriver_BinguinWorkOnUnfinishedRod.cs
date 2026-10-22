// 加载方式已迁至 XML；下述占位替换描述仅记录旧实现。
// ============================================================================
// 【钓竿（未完成）】的工作 job
// JobDef = Binguin_WorkOnUnfinishedRod（targetA = 半成品物品，targetB = 目标装配台）
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

using Binguin.Helper;

namespace Binguin.Feature.Rods
{
    public class JobDriver_BinguinWorkOnUnfinishedRod : JobDriver
    {
        private CompBinguinUnfinishedRod RodComp
        {
            get
            {
                Thing t = job.targetA.Thing;
                return t != null ? t.TryGetComp<CompBinguinUnfinishedRod>() : null;
            }
        }

        //同时 Reserve 半成品 + 目标台子
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            job.count = 1;
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }

            Thing rod = job.targetA.Thing;
            if (rod == null || !rod.Spawned || rod.Map == null)
            {
                return false;
            }

            // ① 优先：半成品旁边已有的装配台
            Thing table = FindTableNearRod(rod);
            // ② 否则：找最近的装配台
            if (table == null)
            {
                table = FindNearestTable(rod,pawn);
            }
            if (table == null)
            {
                return false;   // 地图上没有装配台，不接活
            }

            job.SetTarget(TargetIndex.B, table);
            // 台子也要预定（同一台同时只给一个 job 用）
            if (!pawn.Reserve(table, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);   //  台子没了也停

            CompBinguinUnfinishedRod comp = RodComp;
            if (comp == null)
            {
                BinguinLogUtility.Log("半成品工作 job：目标没有 CompBinguinUnfinishedRod ⇒ 直接结束。", severity: 1, isDebug: false);
                yield break;
            }
            if (!comp.HasAllParts)
            {
                BinguinLogUtility.Log("半成品工作 job：配件不齐（" + comp.PartsSummary() + "）⇒ 直接结束。", severity: 1, isDebug: false);
                Messages.Message("Binguin_WorkOnRod_PartsIncomplete".Translate(),
                    job.targetA.Thing, MessageTypeDefOf.RejectInput, false);
                yield break;
            }

            comp.RollQualityIfNeeded(pawn);
            if (comp.boundMaker == null)
            {
                comp.boundMaker = pawn;
                BinguinLogUtility.Log("半成品已绑定制作者：" + pawn.LabelShort);
            }

            //  搬运逻辑：半成品不在台子旁 → 搬过去
            Thing rod = job.targetA.Thing;
            Thing table = job.targetB.Thing;
            if (table == null || !table.Spawned || table.Destroyed)
            {
                Messages.Message("Binguin_WorkOnRod_NoTable".Translate(),
                    rod, MessageTypeDefOf.RejectInput, false);
                yield break;
            }

            // 即使半成品已在台边，也使用相同 Toil 序列，保证存读档索引稳定。

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, false, false);

            //  先走到台子旁边（任意相邻格），在这里放下半成品
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                .FailOnDespawnedOrNull(TargetIndex.B);

            yield return ToilDropAtTable(TargetIndex.B);

            //  放下后，走到台子的交互格（正前方）—— 这是干活位置
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell)
                .FailOnDespawnedOrNull(TargetIndex.B);

            // ================================================================
            // 干活 toil —— 复刻原版 `Toils_Recipe.DoRecipeWork`
            // ================================================================
            Toil work = new Toil();



            work.initAction = delegate
            {
                CompBinguinUnfinishedRod c = RodComp;
                if (c == null) return;
                float remaining = c.WorkAmountBase - c.workDone;
                BinguinLogUtility.Log("开始装配半成品：已有进度 " + c.workDone.ToString("0")
                    + " / " + c.WorkAmountBase.ToString("0")
                    + "，本次剩余 " + remaining.ToString("0")
                    + "（" + pawn.LabelShort + "，速度 ×"
                    + c.WorkSpeedMultiplier(pawn, WorkTable()).ToString("0.00") + "）");
            };

            const int Interval = 10;
            work.tickAction = delegate
            {
                CompBinguinUnfinishedRod c = RodComp;
                if (c == null || c.parent == null || c.parent.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (!Gen.IsHashIntervalTick(pawn, Interval)) return;
                int delta = Interval;

                if (CompBinguinUnfinishedRod.RecipeWorkSkill != null && pawn.skills != null)
                {
                    pawn.skills.Learn(CompBinguinUnfinishedRod.RecipeWorkSkill,
                        0.1f * JobDriver_BinguinWorkOnUnfinishedRod.WorkSkillLearnFactor * delta,
                        false, false);
                }

                float speed = c.WorkSpeedMultiplier(pawn, WorkTable());
                c.workDone = System.Math.Min(c.WorkAmountBase, c.workDone + speed * delta);
                if (c.workDone >= c.WorkAmountBase)
                {
                    c.workDone = c.WorkAmountBase;
                    BinguinLogUtility.Log("半成品装配完成，进入收尾（" + pawn.LabelShort + "）");
                    ReadyForNextToil();
                }
            };

            work.defaultCompleteMode = ToilCompleteMode.Never;
            work.WithEffect(EffecterDefOf.ConstructMetal, TargetIndex.A);
            work.WithProgressBar(TargetIndex.A, delegate
            {
                CompBinguinUnfinishedRod c = RodComp;
                if (c == null) return 0f;
                float total = c.WorkAmountBase;
                if (total <= 0f) return 1f;
                float p = c.workDone / total;
                if (p < 0f) p = 0f;
                if (p > 1f) p = 1f;
                return p;
            }, false, -0.5f, false);
            work.activeSkill = delegate
            {
                return CompBinguinUnfinishedRod.RecipeWorkSkill;
            };
            yield return work;

            // ---- 收尾 ----
            yield return Toils_General.Do(delegate
            {
                CompBinguinUnfinishedRod c = RodComp;
                if (c != null) c.FinishIntoRod(pawn);
            });
        }

        public const float WorkSkillLearnFactor = 1f;

        // 优先信 job.targetB（TryMakePreToilReservations 选定的台子）
        private Thing WorkTable()
        {
            Thing t = job.targetB.Thing;
            if (t != null && t.Spawned && !t.Destroyed
                && t.TryGetComp<CompBinguinRodAssembly>() != null)
            {
                return t;
            }
            // fallback：物理位置
            Thing rod = job.targetA.Thing;
            if (rod == null || !rod.Spawned || rod.Map == null) return null;
            Map map = rod.Map;
            Building here = rod.Position.GetEdifice(map);
            if (here != null && here.TryGetComp<CompBinguinRodAssembly>() != null) return here;
            for (int i = 0; i < 8; i++)
            {
                IntVec3 cc = rod.Position + GenAdj.AdjacentCells[i];
                if (!cc.InBounds(map)) continue;
                Building b = cc.GetEdifice(map);
                if (b != null && b.TryGetComp<CompBinguinRodAssembly>() != null) return b;
            }
            return null;
        }

        //  找半成品旁边的装配台
        public static Thing FindTableNearRod(Thing rod)
        {
            if (rod == null || !rod.Spawned || rod.Map == null) return null;
            Map map = rod.Map;
            Building here = rod.Position.GetEdifice(map);
            if (here != null && here.TryGetComp<CompBinguinRodAssembly>() != null) return here;
            for (int i = 0; i < 8; i++)
            {
                IntVec3 cc = rod.Position + GenAdj.AdjacentCells[i];
                if (!cc.InBounds(map)) continue;
                Building b = cc.GetEdifice(map);
                if (b != null && b.TryGetComp<CompBinguinRodAssembly>() != null) return b;
            }
            return null;
        }

        //  找离半成品最近的装配台
        // 找离半成品最近的、未被预订的装配台
        public static Thing FindNearestTable(Thing rod, Pawn pawn)
        {
            if (rod == null || !rod.Spawned || rod.Map == null) return null;
            Map map = rod.Map;
            Thing best = null;
            float bestDist = float.MaxValue;
            List<Thing> all = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (t.TryGetComp<CompBinguinRodAssembly>() == null) continue;
                // ★ 跳过已被别人预订的台子
                if (pawn != null && !pawn.CanReserve(t, 1, -1, null, false)) continue;
                float d = (t.Position - rod.Position).LengthHorizontalSquared;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = t;
                }
            }
            return best;
        }

        //  把手上搬着的东西放到装配台旁边
        private Toil ToilDropAtTable(TargetIndex tableInd)
        {
            Toil toil = new Toil();
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                if (actor == null) return;
                Thing carried = actor.carryTracker != null ? actor.carryTracker.CarriedThing : null;
                if (carried == null) return;
                Map map = actor.Map;
                if (map == null) return;

                Building table = job.GetTarget(tableInd).Thing as Building;
                if (table == null) return;

                // ★★★ 优先放到台子自己那一格（半成品压在台面上，小人站交互格）
                //   旧代码先试交互格 —— 但小人自己就站在交互格，放不下
                //   ⇒ 顺序改成：① 台子那一格（Direct） → ② 交互格（Near） → ③ 脚下
                IntVec3 tableCell = table.Position;
                IntVec3 icell = table.InteractionCell;

                Thing placed = null;

                // ① 台子那一格（Direct 精确放）
                if (tableCell.IsValid && tableCell.InBounds(map))
                {
                    if (actor.carryTracker.TryDropCarriedThing(tableCell, ThingPlaceMode.Direct, out placed, null))
                    {
                        BinguinLogUtility.Log("[诊断] 半成品放到台子格：" + tableCell);
                        return;
                    }
                }
                // ② 交互格（Near，找旁边空格）
                if (placed == null && icell.IsValid && icell.InBounds(map))
                {
                    if (actor.carryTracker.TryDropCarriedThing(icell, ThingPlaceMode.Near, out placed, null))
                    {
                        BinguinLogUtility.Log("[诊断] 半成品放到交互格附近：" + placed.Position);
                        return;
                    }
                }
                // ③ 脚下
                if (placed == null)
                {
                    if (actor.carryTracker.TryDropCarriedThing(actor.Position, ThingPlaceMode.Direct, out placed, null))
                    {
                        BinguinLogUtility.Log("[诊断] 半成品放到脚下：" + actor.Position);
                        return;
                    }
                }
                // ④ 全失败：保持拿着（后面 SpawnUnfinishedRod 逻辑不受影响）
                BinguinLogUtility.Log("[诊断] 半成品没找到落点，继续由 " + actor.LabelShort + " 拿着");
            };
            return toil;
        }
    }
}