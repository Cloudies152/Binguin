using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using RimWorld;

namespace Binguin.Feature.Band
{
    internal class BinguinSeatAvailability
    {
        internal int tick;
        internal List<Building> seats = new List<Building>();
        internal HashSet<Thing> targets = new HashSet<Thing>();
        internal HashSet<IntVec3> occupiedCells = new HashSet<IntVec3>();
    }

    // 演出状态归属于当前游戏；静态入口只负责访问，不跨存档持有 Pawn。
    public class GameComponent_BinguinPerformanceState : GameComponent
    {
        public List<Pawn> audience = new List<Pawn>();
        public List<Pawn> performers = new List<Pawn>();
        public List<Pawn> visitingAudience = new List<Pawn>();
        public List<Pawn> visitingPerformers = new List<Pawn>();
        public bool selfHosted;
        public bool bandArrived;
        public IntVec3 venue = IntVec3.Invalid;
        public int visitingMapId = -1;
        internal readonly Dictionary<Map, BinguinSeatAvailability> seatCache = new Dictionary<Map, BinguinSeatAvailability>();
        public Quest activeQuest;
        public Thing activeMarker;
        public int endTick = -1;

        public GameComponent_BinguinPerformanceState(Game game) { }
        public static GameComponent_BinguinPerformanceState Current
        {
            get { return Verse.Current.Game.GetComponent<GameComponent_BinguinPerformanceState>(); }
        }
        public Map VisitingMap
        {
            get { return Find.Maps.Find(m => m.uniqueID == visitingMapId); }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref audience, "audience", LookMode.Reference);
            Scribe_Collections.Look(ref performers, "performers", LookMode.Reference);
            Scribe_Collections.Look(ref visitingAudience, "visitingAudience", LookMode.Reference);
            Scribe_Collections.Look(ref visitingPerformers, "visitingPerformers", LookMode.Reference);
            Scribe_Values.Look(ref selfHosted, "selfHosted");
            Scribe_Values.Look(ref bandArrived, "bandArrived");
            Scribe_Values.Look(ref venue, "venue", IntVec3.Invalid);
            Scribe_Values.Look(ref visitingMapId, "visitingMapId", -1);
            Scribe_References.Look(ref activeMarker, "activeMarker");
            Scribe_Values.Look(ref endTick, "endTick", -1);
            Scribe_References.Look(ref activeQuest, "activeQuest");
        }

        // 只释放本次演出创建的 Crowd Lord；不干扰其他仪式或队伍。
        public static void ReleaseParticipants(IEnumerable<Pawn> participants)
        {
            HashSet<Pawn> seen = new HashSet<Pawn>();
            foreach (Pawn p in participants)
            {
                if (p == null || !seen.Add(p) || p.Destroyed || p.Map == null) continue;
                Lord lord = p.GetLord();
                if (lord != null && lord.LordJob is LordJob_BinguinCrowd)
                    lord.Notify_PawnLost(p, PawnLostCondition.ForcedToJoinOtherLord);
                if (p.mindState != null && p.GetLord() == null) p.mindState.duty = null;
                if (p.CurJob != null && (p.CurJob.def.defName == "Binguin_PlayInBand"
                    || p.CurJob.def.defName == "Binguin_WatchConcert"))
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced);
                if (p.Faction != Faction.OfPlayer && p.GetLord() == null && p.Spawned)
                    LordMaker.MakeNewLord(p.Faction, new LordJob_ExitMapBest(), p.Map, new List<Pawn> { p });
            }
        }
    }
}
