using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Binguin.Feature.Diplomacy
{
    // 没有访客超时/自动离场转换；离场由外交事件得到答复后显式接管。
    public class LordJob_BinguinDiplomat : LordJob
    {
        private IntVec3 waitingPoint;

        public LordJob_BinguinDiplomat() { }
        public LordJob_BinguinDiplomat(IntVec3 waitingPoint) { this.waitingPoint = waitingPoint; }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            graph.AddToil(new LordToil_DefendPoint(waitingPoint, new float?(8f), new float?(8f)));
            return graph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref waitingPoint, "waitingPoint", IntVec3.Invalid);
        }
    }
}
