// ============================================================================
// 装备授予 hediff（企鹅滑板：穿戴时获得「滑板疾行」hediff，移速 +20%）
//
// 2026-08-18 用户要求：企鹅滑板穿着效果改为"给殖民者 hediff 里获得 +20% 移动能力"。
// 实现：Apparel comp 实现 ICompApparelNotify_Equipped/Unequipped，
//   穿戴时添加 hediff、脱下时移除（读档后 Pawn_ApparelTracker 重建会回调 Equipped）。
// ============================================================================

using RimWorld;
using Verse;

namespace Binguin
{
    public class CompProperties_ApparelGrantHediff : CompProperties
    {
        public HediffDef hediffDef;

        public CompProperties_ApparelGrantHediff()
        {
            compClass = typeof(CompApparelGrantHediff);
        }
    }

    public class CompApparelGrantHediff : ThingComp
    {
        public CompProperties_ApparelGrantHediff Props
        {
            get { return (CompProperties_ApparelGrantHediff)props; }
        }

        // 1.6 的 ThingComp 自带穿戴/脱下钩子（Apparel comp 用）
        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            if (pawn == null || pawn.health == null || Props.hediffDef == null)
            {
                return;
            }
            if (pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef) == null)
            {
                pawn.health.AddHediff(Props.hediffDef);
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            if (pawn == null || pawn.health == null)
            {
                return;
            }
            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
            if (h != null)
            {
                pawn.health.RemoveHediff(h);
            }
        }
    }
}
