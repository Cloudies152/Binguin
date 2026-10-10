// ============================================================================
// 与冰鹅族外交官交谈（RimWorld 1.6）
//
// 2026-08-22：殖民者右键外交官 → 走近她身边 → 弹出共存意向选择信。
// 若在她被答复/离开前没走到，任务在到达时发现外交官已不在等候则无事结束。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

using Binguin.Helper;

namespace Binguin.Feature.Diplomacy
{
    public class JobDriver_BinguinDiplomatTalk : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true; // 交谈不占用 Reservation（外交官不是资源）
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);

            // 走到外交官面前
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            // 到面前：若她仍在等候（未被答复），弹共存意向选择信
            Toil talk = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = delegate
                {
                    try
                    {
                        GameComponent_BinguinDiplomacy gc = Current.Game != null
                            ? Current.Game.GetComponent<GameComponent_BinguinDiplomacy>() : null;
                        if (gc != null)
                        {
                            gc.ShowDiplomacyLetter();
                        }
                    }
                    catch (Exception e)
                    {
                        BinguinLogUtility.Log("外交官交谈收尾异常：" + e.Message, severity: 1, isDebug: false);
                    }
                }
            };
            yield return talk;
        }
    }
}
