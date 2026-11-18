using System;
using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Binguin.Helper;

namespace Binguin.Feature.SkillCounter
{
    public enum RetreatMode
    {
        WalkToEdge = 0,     // 正常走到边缘
        ExitJob = 1,        // 直接走离场 Job
        FarthestPoint = 2   // 无边缘 → 走到离地图中心最远处
    }

    /// <summary>
    /// 强制撤退：三层结构。① 人形有派系 → 组撤离领主（可砸墙）；
    /// ② 动物 / 实体 / 散兵 → 个体离场 Job；③ 走不到 → 移出地图。
    /// </summary>
    public static class ForcedRetreat_Binguin
    {
        private const int ReissueInterval = 15;      // 复查间隔
        private const int MinIssueCooldown = 6;      // 重发最小间隔
        private const int MovingWindow = 120;        // 移动中不打扰
        private const int MaxWalkTicks = 2500;       // 走路超时 1 小时
        private const int MaxExitTicks = 5000;       // 离场超时 2 小时
        private const int MaxLordTicks = 5000;       // 领主超时 2 小时
        private const int MaxDownedTicks = 5000;     // 倒地跟踪上限
        private const int EdgeRadius = 2;            // 到达判定
        private const int StealsToExit = 6;          // 被抢→转离场
        private const int StealsToRemove = 24;       // 被抢→移出
        private const int StuckTicks = 120;          // 卡住判定
        private const int DefaultSuppress = 1500;    // 默认压制 30 秒
        private const int SuppressScan = 60;         // 压制期间每秒扫一次
        private const int MaxRemovedIds = 5000;

        private sealed class Entry
        {
            public Pawn pawn;
            public Map map;
            public IntVec3 target;
            public RetreatMode mode;
            public int startTick;
            public int lastIssueTick;
            public int modeStartTick;
            public int steals;
            public IntVec3 lastPos;
            public int lastMoveTick;
            public bool done;
            public bool waitUntilEdge;   // 异象实体：必须到边缘才移除
        }

        private sealed class LordGroup
        {
            public Lord lord;
            public List<Pawn> pawns = new List<Pawn>();
            public int startTick;
        }

        private static readonly List<Entry> active = new List<Entry>();
        private static readonly List<LordGroup> lordGroups = new List<LordGroup>();
        private static readonly HashSet<int> removedIds = new HashSet<int>();
        private static readonly HashSet<int> stuckIds = new HashSet<int>();
        private static readonly List<Map> suppressMaps = new List<Map>();
        private static readonly Dictionary<int, IntVec3> farthestCache = new Dictionary<int, IntVec3>();

        private static int suppressUntil = -1;
        private static int nextScan;
        private static Faction refugeeFaction;
        private static JobDef exitJobDef;

        /// <summary>压制窗口结束 tick（存档用）。</summary>
        public static int SuppressUntilTick
        {
            get { return suppressUntil; }
            set { suppressUntil = value; }
        }

        public static int ActiveCount
        {
            get { return active.Count; }
        }

        // ---------------- 对外入口 ----------------

        public static int RetreatAll(Map map, int suppressTicks = DefaultSuppress)
        {
            if (map == null)
            {
                return 0;
            }

            int now = Find.TickManager.TicksGame;

            if (suppressTicks > 0)
            {
                stuckIds.Clear();
                if (!suppressMaps.Contains(map))
                {
                    suppressMaps.Add(map);
                }
                suppressUntil = Math.Max(suppressUntil, now + suppressTicks);
                nextScan = now + SuppressScan;
            }

            return Sweep(map, now);
        }

        public static int RetreatAllMaps(int suppressTicks = DefaultSuppress)
        {
            int total = 0;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                total += RetreatAll(maps[i], suppressTicks);
            }
            return total;
        }

        /// <summary>读档后：窗口未过就重新扫一遍（条目自动重建）。</summary>
        public static void ResumeAfterLoad()
        {
            active.Clear();
            lordGroups.Clear();
            stuckIds.Clear();

            if (Find.TickManager == null || suppressUntil <= Find.TickManager.TicksGame)
            {
                return;
            }

            nextScan = Find.TickManager.TicksGame;
            suppressMaps.Clear();
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                suppressMaps.Add(maps[i]);
            }

            Log("读档后恢复压制窗口。");
        }

        public static void Tick()
        {
            int now = Find.TickManager.TicksGame;

            if (suppressMaps.Count > 0 && now <= suppressUntil && now >= nextScan)
            {
                nextScan = now + SuppressScan;
                for (int i = 0; i < suppressMaps.Count; i++)
                {
                    if (Find.Maps.Contains(suppressMaps[i]))
                    {
                        Sweep(suppressMaps[i], now);
                    }
                }
            }

            TickLords(now);

            for (int i = active.Count - 1; i >= 0; i--)
            {
                Entry e = active[i];
                if (e.done || e.pawn == null || e.pawn.Dead || !e.pawn.Spawned || e.pawn.Map != e.map)
                {
                    active.RemoveAt(i);
                    continue;
                }

                if (TickEntry(e, now))
                {
                    active.RemoveAt(i);
                }
            }
        }

        public static void Clear()
        {
            active.Clear();
            lordGroups.Clear();
            stuckIds.Clear();
            removedIds.Clear();
            farthestCache.Clear();
            suppressMaps.Clear();
            suppressUntil = -1;
            nextScan = 0;
        }

        // ---------------- 领主路径 ----------------

        private static void TickLords(int now)
        {
            for (int i = lordGroups.Count - 1; i >= 0; i--)
            {
                LordGroup g = lordGroups[i];
                bool anyLeft = false;
                for (int j = 0; j < g.pawns.Count; j++)
                {
                    Pawn p = g.pawns[j];
                    if (p != null && p.Spawned && !p.Dead)
                    {
                        anyLeft = true;
                        break;
                    }
                }

                if (!anyLeft)
                {
                    lordGroups.RemoveAt(i);
                    continue;
                }

                if (now - g.startTick > MaxLordTicks)
                {
                    // 领主送不走：剩下的转个体
                    for (int j = 0; j < g.pawns.Count; j++)
                    {
                        if (g.pawns[j] != null && g.pawns[j].Spawned)
                        {
                            StartEntry(g.pawns[j], now);
                        }
                    }
                    lordGroups.RemoveAt(i);
                    Log("撤离队伍超时，剩余单位转个体处理。");
                }
            }
        }

        /// <summary>同派系合并成一支撤离领主；canDig 让原版逻辑砸墙砸门。</summary>
        private static int StartLords(Map map, List<Pawn> pawns, int now)
        {
            Dictionary<Faction, List<Pawn>> groups = new Dictionary<Faction, List<Pawn>>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Faction f = pawns[i].Faction;
                if (f == null)
                {
                    continue;
                }

                List<Pawn> list;
                if (!groups.TryGetValue(f, out list))
                {
                    list = new List<Pawn>();
                    groups[f] = list;
                }
                list.Add(pawns[i]);
            }

            int count = 0;
            foreach (KeyValuePair<Faction, List<Pawn>> kv in groups)
            {
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    Detach(kv.Value[i]);
                }

                Lord lord;
                try
                {
                    lord = LordMaker.MakeNewLord(kv.Key,
                        new LordJob_ExitMapBest(LocomotionUrgency.Sprint, true), map, kv.Value);
                }
                catch (Exception ex)
                {
                    Warn("建立撤离领主失败：" + ex.Message, 962240);
                    lord = null;
                }

                if (lord == null)
                {
                    continue;
                }

                LordGroup g = new LordGroup();
                g.lord = lord;
                g.pawns.AddRange(kv.Value);
                g.startTick = now;
                lordGroups.Add(g);
                count += kv.Value.Count;
            }

            return count;
        }

        // ---------------- 个体路径 ----------------

        /// <returns>true 表示该条目结束（已移出 / 已放手）。</returns>
        private static bool TickEntry(Entry e, int now)
        {
            Pawn p = e.pawn;

            if (p.Downed)
            {
                return now - e.startTick > MaxDownedTicks;
            }

            if (p.Position.InHorDistOf(e.target, EdgeRadius))
            {
                Remove(e);
                return true;
            }

            if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                return false;
            }

            // 卡住判定
            if (p.Position != e.lastPos)
            {
                e.lastPos = p.Position;
                e.lastMoveTick = now;
            }
            else if (p.pather != null && p.pather.Moving)
            {
                e.lastMoveTick = now;
            }
            else if (now - e.lastMoveTick > StuckTicks)
            {
                JobDef curDef = e.mode == RetreatMode.ExitJob ? ExitJobDef : JobDefOf.Goto;
                if (!IsRunning(e, curDef, true))
                {
                    // 被抢：继续追，累计后升级 / 移出
                    e.steals++;
                    e.lastMoveTick = now;
                    if (e.mode != RetreatMode.ExitJob)
                    {
                        ToExitJob(e, now, "卡住且被抢");
                    }
                    else if (!e.waitUntilEdge && e.steals >= StealsToRemove)
                    {
                        Remove(e);
                        return true;
                    }
                    return false;
                }

                if (IsHumanlike(p))
                {
                    // 人形：保持撤退状态，继续砸门开路
                    e.lastMoveTick = now;
                    Issue(e, now, e.mode == RetreatMode.ExitJob ? ExitJobDef : JobDefOf.Goto);
                    return false;
                }

                // 其他生物：放手
                stuckIds.Add(p.thingIDNumber);
                Release(p);
                return true;
            }

            return Advance(e, now);
        }

        /// <returns>true 表示条目结束。</returns>
        private static bool Advance(Entry e, int now)
        {
            bool exitMode = e.mode == RetreatMode.ExitJob;
            JobDef def = exitMode ? ExitJobDef : JobDefOf.Goto;

            if (!exitMode && now - e.startTick > MaxWalkTicks)
            {
                ToExitJob(e, now, "走路超时");
                return false;
            }

            if (exitMode && !e.waitUntilEdge && now - e.modeStartTick > MaxExitTicks)
            {
                Remove(e);
                return true;
            }

            if (def == null)
            {
                Remove(e);
                return true;
            }

            if (IsRunning(e, def, !exitMode))
            {
                if (now - e.lastMoveTick < MovingWindow || now - e.lastIssueTick < ReissueInterval)
                {
                    return false;
                }
            }
            else
            {
                if (now - e.lastIssueTick < MinIssueCooldown)
                {
                    return false;
                }

                e.steals++;
                if (!exitMode && e.steals >= StealsToExit)
                {
                    ToExitJob(e, now, "Job 反复被抢");
                    return false;
                }
                if (exitMode && !e.waitUntilEdge && e.steals >= StealsToRemove)
                {
                    Remove(e);
                    return true;
                }
            }

            Issue(e, now, def);
            return false;
        }

        private static void ToExitJob(Entry e, int now, string reason)
        {
            e.mode = RetreatMode.ExitJob;
            e.modeStartTick = now;
            e.steals = 0;
            e.lastIssueTick = -999;
            Log(e.pawn.LabelShort + " 改走离场 Job（" + reason + "）。");
        }

        // ---------------- 扫描 ----------------

        private static int Sweep(Map map, int now)
        {
            List<Pawn> lordable = new List<Pawn>();
            int solo = 0;
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;

            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p == null || p.Dead || !p.Spawned || p.Destroyed)
                {
                    continue;
                }

                if (stuckIds.Contains(p.thingIDNumber))
                {
                    continue;
                }

                // 被移出过又刷回来：转难民阵营再撤
                if (removedIds.Contains(p.thingIDNumber))
                {
                    if (CanHandle(p) && Neutralize(p))
                    {
                        if (StartEntry(p, now))
                        {
                            solo++;
                        }
                    }
                    continue;
                }

                if (!IsTarget(p) || !CanHandle(p))
                {
                    continue;
                }

                if (p.Faction != null && !IsEntity(p) && IsHumanlike(p))
                {
                    lordable.Add(p);
                }
                else if (StartEntry(p, now))
                {
                    solo++;
                }
            }

            int started = StartLords(map, lordable, now) + solo;
            if (started > 0)
            {
                Log("对 " + started + " 个单位下撤退令。");
            }
            return started;
        }

        private static bool StartEntry(Pawn p, int now)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].pawn == p)
                {
                    return false;
                }
            }

            Entry e = new Entry();
            e.pawn = p;
            e.map = p.Map;
            e.startTick = now;
            e.lastIssueTick = -999;
            e.modeStartTick = now;
            e.lastPos = p.PositionHeld;
            e.lastMoveTick = now;

            bool entity = IsEntity(p);
            bool madAnimal = p.RaceProps != null && p.RaceProps.Animal && p.InAggroMentalState;

            IntVec3 exit = FindExit(p);
            if (!exit.IsValid)
            {
                e.mode = RetreatMode.FarthestPoint;
                e.target = FarthestCell(p.Map);
                if (!e.target.IsValid)
                {
                    return false;
                }
                Log(p.LabelShort + " 无可用边缘，改走「离地图中心最远处」。");
            }
            else if (entity || madAnimal)
            {
                e.mode = RetreatMode.ExitJob;
                e.target = exit;
                e.waitUntilEdge = entity;
            }
            else
            {
                e.mode = RetreatMode.WalkToEdge;
                e.target = exit;
            }

            active.Add(e);
            Issue(e, now, e.mode == RetreatMode.ExitJob ? ExitJobDef : JobDefOf.Goto);
            return true;
        }

        // ---------------- 筛选 ----------------

        private static bool IsTarget(Pawn p)
        {
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return false;
            }

            if (p.HostileTo(player) || p.InAggroMentalState)
            {
                return true;
            }

            MentalStateDef msd = p.MentalStateDef;
            return msd != null
                && (msd == MentalStateDefOf.Manhunter || msd == MentalStateDefOf.ManhunterPermanent);
        }

        private static bool CanHandle(Pawn p)
        {
            if (p.IsQuestLodger() || p.IsColonist || p.IsPrisonerOfColony || p.IsSlaveOfColony)
            {
                return false;
            }

            return p.Faction == null || p.Faction.def == null || !p.Faction.def.isPlayer;
        }

        private static bool IsHumanlike(Pawn p)
        {
            return p.RaceProps != null && p.RaceProps.Humanlike;
        }

        private static bool IsEntity(Pawn p)
        {
            try
            {
                return p.IsEntity;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---------------- 位置 ----------------

        private static IntVec3 FindExit(Pawn pawn)
        {
            IntVec3 spot;
            try
            {
                if (RCellFinder.TryFindBestExitSpot(pawn, out spot))
                {
                    return spot;
                }
            }
            catch (Exception)
            {
            }

            // 兜底：从外圈往里扫最近的可行走格
            Map map = pawn.Map;
            for (int ring = 0; ring < 60; ring++)
            {
                for (int x = ring; x < map.Size.x - ring; x++)
                {
                    IntVec3 a = new IntVec3(x, 0, ring);
                    IntVec3 b = new IntVec3(x, 0, map.Size.z - 1 - ring);
                    if (a.Walkable(map) && a.Standable(map)) return a;
                    if (b.Walkable(map) && b.Standable(map)) return b;
                }
                for (int z = ring; z < map.Size.z - ring; z++)
                {
                    IntVec3 a = new IntVec3(ring, 0, z);
                    IntVec3 b = new IntVec3(map.Size.x - 1 - ring, 0, z);
                    if (a.Walkable(map) && a.Standable(map)) return a;
                    if (b.Walkable(map) && b.Standable(map)) return b;
                }
            }

            return IntVec3.Invalid;
        }

        private static IntVec3 FarthestCell(Map map)
        {
            IntVec3 cached;
            if (farthestCache.TryGetValue(map.uniqueID, out cached))
            {
                return cached;
            }

            IntVec3 center = map.Center;
            IntVec3 best = IntVec3.Invalid;
            int bestDist = -1;

            for (int x = 0; x < map.Size.x; x++)
            {
                for (int z = 0; z < map.Size.z; z++)
                {
                    IntVec3 c = new IntVec3(x, 0, z);
                    if (!c.Walkable(map) || !c.Standable(map))
                    {
                        continue;
                    }

                    int d = (c - center).LengthHorizontalSquared;
                    if (d > bestDist)
                    {
                        bestDist = d;
                        best = c;
                    }
                }
            }

            farthestCache[map.uniqueID] = best;
            return best;
        }

        // ---------------- Job ----------------

        private static JobDef ExitJobDef
        {
            get
            {
                if (exitJobDef == null)
                {
                    exitJobDef = DefDatabase<JobDef>.GetNamedSilentFail("ExitMapBest")
                        ?? DefDatabase<JobDef>.GetNamedSilentFail("ExitMap");
                }
                return exitJobDef;
            }
        }

        private static bool IsRunning(Entry e, JobDef def, bool matchTarget)
        {
            if (e.pawn.jobs == null || e.pawn.CurJob == null || def == null)
            {
                return false;
            }

            if (e.pawn.CurJobDef != def)
            {
                return false;
            }

            return !matchTarget || e.pawn.CurJob.targetA.Cell == e.target;
        }

        private static void Issue(Entry e, int now, JobDef def)
        {
            if (def == null)
            {
                Remove(e);
                e.done = true;
                return;
            }

            Pawn p = e.pawn;
            try
            {
                if (p.InMentalState && p.MentalState != null)
                {
                    p.MentalState.RecoverFromState();
                    if (p.InMentalState)
                    {
                        p.mindState.mentalStateHandler.Reset();
                    }
                }

                Detach(p);
                p.jobs.StopAll(false, true);

                Job job = new Job(def, e.target);
                job.locomotionUrgency = LocomotionUrgency.Sprint;
                job.expiryInterval = 600;
                job.checkOverrideOnExpire = false;
                job.canBashDoors = true;

                p.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
                p.mindState.enemyTarget = null;
                e.lastIssueTick = now;
            }
            catch (Exception ex)
            {
                Warn("下发撤退 Job 失败：" + ex.Message, 962214);
                Remove(e);
                e.done = true;
            }
        }

        private static void Detach(Pawn p)
        {
            try
            {
                Lord lord = p.GetLord();
                if (lord != null)
                {
                    lord.RemovePawn(p);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void Release(Pawn p)
        {
            try
            {
                if (p.jobs != null && p.CurJob != null)
                {
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                }
            }
            catch (Exception)
            {
            }
        }

        // ---------------- 移出 / 阵营 ----------------

        private static void Remove(Entry e)
        {
            Pawn p = e.pawn;
            if (p == null || p.Destroyed)
            {
                return;
            }

            try
            {
                removedIds.Add(p.thingIDNumber);
                if (removedIds.Count > MaxRemovedIds)
                {
                    removedIds.Clear();
                }

                Lord lord = p.Spawned ? p.GetLord() : null;
                if (p.Spawned)
                {
                    p.DeSpawn(DestroyMode.Vanish);
                }
                if (!p.Destroyed)
                {
                    Find.WorldPawns.PassToWorld(p);
                }
                if (lord != null)
                {
                    lord.Notify_PawnLost(p, PawnLostCondition.Undefined);
                }

                Log("已将 " + p.LabelShort + " 移出地图。");
            }
            catch (Exception ex)
            {
                Warn("移出地图失败：" + ex.Message, 962215);
            }
        }

        /// <returns>是否已完成转换。</returns>
        private static bool Neutralize(Pawn p)
        {
            if (!IsHumanlike(p))
            {
                return true;
            }

            try
            {
                Faction target = RefugeeFaction();
                if (target == null || p.Faction == target)
                {
                    return true;
                }

                Lord lord = p.GetLord();
                if (lord != null)
                {
                    lord.Notify_PawnLost(p, PawnLostCondition.Undefined);
                }

                string old = p.Faction != null ? p.Faction.Name : "无";
                p.SetFaction(target);
                p.mindState.enemyTarget = null;
                Log(p.LabelShort + " 由「" + old + "」转入难民阵营。");
                return true;
            }
            catch (Exception ex)
            {
                Warn("转中立失败：" + ex.Message, 962216);
                return false;
            }
        }

        private static Faction RefugeeFaction()
        {
            FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail("OutlanderRefugee");
            if (def == null)
            {
                return null;
            }

            if (refugeeFaction != null && refugeeFaction.def == def && !refugeeFaction.defeated)
            {
                return refugeeFaction;
            }

            Faction existing = Find.FactionManager.FirstFactionOfDef(def);
            if (existing != null && !existing.defeated)
            {
                refugeeFaction = existing;
                return existing;
            }

            try
            {
                Faction created = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def));
                if (created != null)
                {
                    Find.FactionManager.Add(created);
                    refugeeFaction = created;
                }
                return created;
            }
            catch (Exception ex)
            {
                Warn("生成难民派系失败：" + ex.Message, 962218);
                return null;
            }
        }

        private static void Log(string msg)
        {
            BinguinLogUtility.Log("强制撤退：" + msg, isDebug: false);
        }

        private static void Warn(string msg, int key)
        {
            BinguinLogUtility.WarningOnce("强制撤退：" + msg, key);
        }

        // ---------------- 调试 ----------------

        [DebugAction("冰鹅族", "技能对抗：全图强制撤退",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugRetreat()
        {
            int n = RetreatAll(Find.CurrentMap);
            Messages.Message("已强制 " + n + " 个单位开始撤退。", MessageTypeDefOf.NeutralEvent);
        }

        [DebugAction("冰鹅族", "技能对抗：撤退（单次，无压制）",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugRetreatOnce()
        {
            int n = RetreatAll(Find.CurrentMap, 0);
            Messages.Message("已强制 " + n + " 个单位开始撤退（单次）。", MessageTypeDefOf.NeutralEvent);
        }
    }

    /// <summary>逐帧驱动 + 存档压制窗口。</summary>
    public class GameComponent_BinguinForcedRetreat : GameComponent
    {
        public GameComponent_BinguinForcedRetreat(Game game)
            : base()
        {
        }

        public override void GameComponentUpdate()
        {
            ForcedRetreat_Binguin.Tick();
        }

        public override void ExposeData()
        {
            base.ExposeData();

            int until = ForcedRetreat_Binguin.SuppressUntilTick;
            Scribe_Values.Look<int>(ref until, "binguinForcedRetreatUntil", -1);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                ForcedRetreat_Binguin.SuppressUntilTick = until;
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ForcedRetreat_Binguin.ResumeAfterLoad();
            }
        }
    }
}
