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
    /// <summary>撤退模式（按难度循序渐进）。</summary>
    public enum RetreatMode
    {
        /// <summary>普通敌人：正常走到地图边缘再离开。</summary>
        WalkToEdge = 0,

        /// <summary>死战不退 / 发狂动物 / 异象实体：直接走「离场 Job」。</summary>
        ExitJob = 1,

        /// <summary>地图没有可离开的边缘格：走到离地图中心最远处再离开。</summary>
        FarthestPoint = 2
    }

    /// <summary>
    /// 强制撤退（技能对抗）
    /// ============================================================
    /// 三层结构，能走原版就走原版：
    ///
    ///   ① 主路径：人形敌对 + 有派系
    ///      从原袭击领主里摘出来 → 新建 LordJob_ExitMapBest(canDig: true) 领主
    ///      → 由游戏自己驱动他们走到边缘离图；canDig 让他们砸墙砸门开路（原版逻辑）
    ///
    ///   ② 个体路径：动物 / 异象实体 / 无派系单位 / 领主送不走的漏网之鱼
    ///      直接下发原版离场 Job（ExitMapBest / ExitMap）；
    ///      地图没有可走边缘时，走到「离地图中心最远处」再离开
    ///
    ///   ③ 兜底：走不到边缘、超时、Job 被拦
    ///      DeSpawn + PassToWorld 移出地图（绕过 hediff / 精神状态 / 免疫判定）
    ///
    /// 被移除后又重新刷出来的单位：转「难民阵营」并再撤一次（持续压制窗口内）。
    /// </summary>
    public static class ForcedRetreat_Binguin
    {
        // ---------------------------------------------------------------- 时间参数
        /// <summary>Job 仍在跑时的复查间隔。</summary>
        private const int ReissueInterval = 15;

        /// <summary>两次「真正下发 Job」的最小间隔。</summary>
        private const int MinIssueCooldown = 6;

        /// <summary>移动中就不重发 Job 的判定窗口。</summary>
        private const int ReissueRefreshTicks = 120;

        /// <summary>单个单位最长走路撤退时间：1 小时。</summary>
        private const int MaxWalkTicks = 2500;

        /// <summary>离场 Job 阶段最长等待：2 小时。</summary>
        private const int MaxExitJobTicks = 2 * 2500;

        /// <summary>领主撤退的最大时长：2 小时，超时把剩下的转个体处理。</summary>
        private const int MaxLordTicks = 2 * 2500;

        /// <summary>倒地单位最多跟踪 2 小时。</summary>
        private const int MaxDownedTrackTicks = 2 * 2500;

        /// <summary>距离目标边缘多少格内视作到达。</summary>
        private const int EdgeReachRadius = 2;

        /// <summary>走路被抢多少次后升级为离场 Job。</summary>
        private const int StealsBeforeExitJob = 6;

        /// <summary>离场阶段被抢多少次后直接移出。</summary>
        private const int StealsBeforeRemove = 24;

        /// <summary>原地不动多少 tick 判定被挡住。</summary>
        private const int StuckTicks = 120;

        /// <summary>默认持续压制时长：30 秒。</summary>
        private const int DefaultSuppressTicks = 1500;

        /// <summary>压制期间复查间隔：1 秒。</summary>
        private const int SuppressScanInterval = 60;

        /// <summary>已移出单位 ID 记录上限。</summary>
        private const int MaxRemovedIds = 5000;

        // ---------------------------------------------------------------- 数据结构
        private sealed class RetreatEntry
        {
            public Pawn pawn;
            public Map map;
            public IntVec3 target;
            public RetreatMode mode;
            public int startTick;
            public int lastIssueTick;
            public int modeStartTick;
            public int stealCount;
            public IntVec3 lastPos;
            public int lastMoveTick;
            public bool done;
            public bool waitUntilEdge;
        }

        /// <summary>一支正在撤离的领主队伍。</summary>
        private sealed class LordRetreat
        {
            public Map map;
            public Lord lord;
            public List<Pawn> pawns = new List<Pawn>();
            public int startTick;
            public bool dismissed;
        }

        private static readonly List<RetreatEntry> active = new List<RetreatEntry>();
        private static readonly List<LordRetreat> lordRetreats = new List<LordRetreat>();
        private static readonly HashSet<int> removedIds = new HashSet<int>();
        private static readonly HashSet<int> stuckIds = new HashSet<int>();

        private static readonly List<Map> suppressMaps = new List<Map>();
        private static int suppressUntilTick = -1;
        private static int nextSuppressScanTick;
        private static Faction refugeeFaction;
        private static JobDef exitMapJobDef;
        private static readonly Dictionary<int, IntVec3> farthestCellCache = new Dictionary<int, IntVec3>();

        /// <summary>压制窗口结束的 tick（供存档保存）。</summary>
        public static int SuppressUntilTick
        {
            get { return suppressUntilTick; }
            set { suppressUntilTick = value; }
        }

        /// <summary>当前正在个体处理的单位数量（调试用）。</summary>
        public static int ActiveCount
        {
            get { return active.Count; }
        }

        // ============================================================
        // 对外入口
        // ============================================================

        /// <summary>
        /// 让指定地图上所有敌对 / 发狂的中立单位撤退。
        /// </summary>
        /// <param name="map">目标地图。</param>
        /// <param name="suppressTicks">持续压制时长；0 表示只执行一次。默认 30 秒。</param>
        public static int RetreatAll(Map map, int suppressTicks = DefaultSuppressTicks)
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

                suppressUntilTick = Math.Max(suppressUntilTick, now + suppressTicks);
                nextSuppressScanTick = now + SuppressScanInterval;
            }

            return SweepMap(map, now);
        }

        /// <summary>对所有已生成地图生效。</summary>
        public static int RetreatAllMaps(int suppressTicks = DefaultSuppressTicks)
        {
            int total = 0;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                total += RetreatAll(maps[i], suppressTicks);
            }
            return total;
        }

        /// <summary>
        /// 读档恢复：如果压制窗口还没结束，直接重新扫一遍（条目自动重建）。
        /// </summary>
        public static void ResumeAfterLoad()
        {
            active.Clear();
            lordRetreats.Clear();
            stuckIds.Clear();

            if (Find.TickManager == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (suppressUntilTick <= now)
            {
                return;
            }

            nextSuppressScanTick = now;
            suppressMaps.Clear();

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                suppressMaps.Add(maps[i]);
                SweepMap(maps[i], now);
            }

            BinguinLogUtility.Log("强制撤退：读档后恢复压制窗口，已重新扫描地图。", isDebug: false);
        }

        /// <summary>逐帧推进。</summary>
        public static void Tick()
        {
            int now = Find.TickManager.TicksGame;

            if (suppressMaps.Count > 0
                && now <= suppressUntilTick
                && now >= nextSuppressScanTick)
            {
                nextSuppressScanTick = now + SuppressScanInterval;
                for (int m = 0; m < suppressMaps.Count; m++)
                {
                    Map sm = suppressMaps[m];
                    if (sm != null && Find.Maps.Contains(sm))
                    {
                        SweepMap(sm, now);
                    }
                }
            }

            TickLordRetreats(now);

            if (active.Count == 0)
            {
                return;
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                RetreatEntry e = active[i];
                Pawn p = e.pawn;

                if (e.done)
                {
                    active.RemoveAt(i);
                    continue;
                }

                if (p == null || p.Dead || !p.Spawned || p.Map != e.map)
                {
                    active.RemoveAt(i);
                    continue;
                }

                if (p.Downed)
                {
                    if (now - e.startTick > MaxDownedTrackTicks)
                    {
                        e.done = true;
                    }
                    continue;
                }

                if (p.Position.InHorDistOf(e.target, EdgeReachRadius))
                {
                    RemoveFromMap(e);
                    active.RemoveAt(i);
                    continue;
                }

                if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                {
                    continue;
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
                    JobDef curDef = e.mode == RetreatMode.ExitJob ? ExitMapJobDef : JobDefOf.Goto;
                    bool jobStolen = !IsRunningJob(e, curDef);

                    if (jobStolen)
                    {
                        if (e.mode != RetreatMode.ExitJob)
                        {
                            SwitchToExitJob(e, now, "卡住且 Job 被抢");
                        }
                        else
                        {
                            e.stealCount++;
                            if (e.stealCount >= StealsBeforeRemove)
                            {
                                RemoveFromMap(e);
                                e.done = true;
                                continue;
                            }
                        }

                        e.lastMoveTick = now;
                        continue;
                    }

                    // 被挡住
                    if (IsHumanlike(e.pawn))
                    {
                        // 人形：保持撤退状态，靠原版砸门/砸墙逻辑继续开路
                        e.lastMoveTick = now;
                        IssueJob(e, now, e.mode == RetreatMode.ExitJob ? ExitMapJobDef : JobDefOf.Goto, e.target);
                        continue;
                    }

                    // 其他生物：放手，交还 AI
                    stuckIds.Add(p.thingIDNumber);
                    ReleasePawn(p);
                    active.RemoveAt(i);
                    continue;
                }

                switch (e.mode)
                {
                    case RetreatMode.ExitJob:
                        TickExitJob(e, now);
                        break;

                    default:
                        TickWalk(e, now);
                        break;
                }
            }
        }

        /// <summary>清空跟踪（读档 / 换图时用）。</summary>
        public static void Clear()
        {
            active.Clear();
            lordRetreats.Clear();
            stuckIds.Clear();
            removedIds.Clear();
            farthestCellCache.Clear();
            suppressMaps.Clear();
            suppressUntilTick = -1;
            nextSuppressScanTick = 0;
        }

        // ============================================================
        // ① 领主路径
        // ============================================================

        private static void TickLordRetreats(int now)
        {
            for (int i = lordRetreats.Count - 1; i >= 0; i--)
            {
                LordRetreat lr = lordRetreats[i];
                if (lr.dismissed)
                {
                    lordRetreats.RemoveAt(i);
                    continue;
                }

                bool anyLeft = false;
                for (int j = 0; j < lr.pawns.Count; j++)
                {
                    Pawn p = lr.pawns[j];
                    if (p != null && p.Spawned && !p.Dead)
                    {
                        anyLeft = true;
                        break;
                    }
                }

                if (!anyLeft)
                {
                    lr.dismissed = true;
                    lordRetreats.RemoveAt(i);
                    BinguinLogUtility.Log("强制撤退：一支撤离队伍已全部离开地图。", isDebug: false);
                    continue;
                }

                if (now - lr.startTick > MaxLordTicks)
                {
                    // 领主带不走：把剩下的转成个体处理
                    for (int j = 0; j < lr.pawns.Count; j++)
                    {
                        Pawn p = lr.pawns[j];
                        if (p != null && p.Spawned && !p.Dead)
                        {
                            StartRetreat(p, now);
                        }
                    }

                    lr.dismissed = true;
                    lordRetreats.RemoveAt(i);
                    BinguinLogUtility.Log(
                        "强制撤退：撤离队伍超时，剩余单位转为个体处理。", isDebug: false);
                }
            }
        }

        /// <summary>为一批人形敌对单位建立「撤离地图」领主。</summary>
        private static int StartLordRetreats(Map map, List<Pawn> pawns, int now)
        {
            if (pawns.Count == 0)
            {
                return 0;
            }

            // 按派系分组（同一派系一起走）
            Dictionary<Faction, List<Pawn>> groups = new Dictionary<Faction, List<Pawn>>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                Faction f = p.Faction;
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

                list.Add(p);
            }

            int started = 0;
            foreach (KeyValuePair<Faction, List<Pawn>> kv in groups)
            {
                Faction faction = kv.Key;
                List<Pawn> list = kv.Value;

                // 先从原领主里摘出来，否则两个领主会互相抢
                for (int i = 0; i < list.Count; i++)
                {
                    DetachFromLord(list[i]);
                }

                Lord lord = null;
                try
                {
                    // canDig: true → 原版逻辑允许他们砸墙砸门开路
                    // 1.7 构造顺序是 (移动速度, 是否能挖/砸)
                    LordJob job = new LordJob_ExitMapBest(LocomotionUrgency.Sprint, true);
                    lord = LordMaker.MakeNewLord(faction, job, map, list);
                }
                catch (Exception ex)
                {
                    BinguinLogUtility.WarningOnce(
                        "强制撤退：建立撤离队伍失败，改为个体处理。" + ex.Message, 962240);
                    lord = null;
                }

                if (lord == null)
                {
                    continue;
                }

                LordRetreat lr = new LordRetreat();
                lr.map = map;
                lr.lord = lord;
                lr.startTick = now;
                for (int i = 0; i < list.Count; i++)
                {
                    lr.pawns.Add(list[i]);
                }

                lordRetreats.Add(lr);
                started += list.Count;

                BinguinLogUtility.Log(
                    "强制撤退：" + faction.Name + " 的 " + list.Count + " 个单位组成撤离队伍。",
                    isDebug: false);
            }

            return started;
        }

        // ============================================================
        // ② 个体路径
        // ============================================================

        private static void TickWalk(RetreatEntry e, int now)
        {
            if (now - e.startTick > MaxWalkTicks)
            {
                SwitchToExitJob(e, now, "走路超时");
                return;
            }

            bool running = IsRunningJob(e, JobDefOf.Goto);
            if (running)
            {
                if (now - e.lastMoveTick < ReissueRefreshTicks)
                {
                    return;
                }

                if (now - e.lastIssueTick < ReissueInterval)
                {
                    return;
                }
            }
            else
            {
                if (now - e.lastIssueTick < MinIssueCooldown)
                {
                    return;
                }

                e.stealCount++;
                if (e.stealCount >= StealsBeforeExitJob)
                {
                    SwitchToExitJob(e, now, "Job 反复被抢");
                    return;
                }
            }

            IssueJob(e, now, JobDefOf.Goto, e.target);
        }

        private static void TickExitJob(RetreatEntry e, int now)
        {
            if (!e.waitUntilEdge && now - e.modeStartTick > MaxExitJobTicks)
            {
                RemoveFromMap(e);
                e.done = true;
                return;
            }

            JobDef exitDef = ExitMapJobDef;
            if (exitDef == null)
            {
                RemoveFromMap(e);
                e.done = true;
                return;
            }

            bool running = IsRunningJobOfDef(e, exitDef);

            if (running)
            {
                if (now - e.lastMoveTick < ReissueRefreshTicks)
                {
                    return;
                }

                if (now - e.lastIssueTick < ReissueInterval)
                {
                    return;
                }
            }
            else
            {
                if (now - e.lastIssueTick < MinIssueCooldown)
                {
                    return;
                }

                e.stealCount++;

                if (!e.waitUntilEdge && e.stealCount >= StealsBeforeRemove)
                {
                    RemoveFromMap(e);
                    e.done = true;
                    return;
                }
            }

            IssueJob(e, now, exitDef, e.target);
        }

        private static void SwitchToExitJob(RetreatEntry e, int now, string reason)
        {
            e.mode = RetreatMode.ExitJob;
            e.modeStartTick = now;
            e.stealCount = 0;
            e.lastIssueTick = -999;

            BinguinLogUtility.Log(
                "强制撤退：" + e.pawn.LabelShort + " 改走离场 Job（" + reason + "）。", isDebug: false);
        }

        // ============================================================
        // 扫描与筛选
        // ============================================================

        private static int SweepMap(Map map, int now)
        {
            List<Pawn> lordable = new List<Pawn>();
            List<Pawn> solo = new List<Pawn>();
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (p == null || p.Dead || !p.Spawned || p.Destroyed)
                {
                    continue;
                }

                if (stuckIds.Contains(p.thingIDNumber))
                {
                    continue;
                }

                // 被移出过又重新出现：先转难民阵营，再撤一次（走个体路径）
                if (removedIds.Contains(p.thingIDNumber))
                {
                    if (!CanBeHandled(p))
                    {
                        continue;
                    }

                    NeutralizeFaction(p);

                    if (StartRetreat(p, now))
                    {
                        solo.Add(p);
                    }
                    continue;
                }

                if (!IsRetreatTarget(p))
                {
                    continue;
                }

                if (IsLordCapable(p))
                {
                    lordable.Add(p);
                }
                else if (CanBeHandled(p))
                {
                    if (StartRetreat(p, now))
                    {
                        solo.Add(p);
                    }
                }
            }

            int started = StartLordRetreats(map, lordable, now) + solo.Count;
            if (started > 0)
            {
                BinguinLogUtility.Log("强制撤退：对 " + started + " 个单位下撤退令。", isDebug: false);
            }

            return started;
        }

        /// <summary>人形 + 有派系 → 可以加入撤离领主。</summary>
        private static bool IsLordCapable(Pawn p)
        {
            if (p.Faction == null)
            {
                return false;
            }

            if (IsAnomalyEntity(p))
            {
                return false;
            }

            return IsHumanlike(p);
        }

        private static bool IsHumanlike(Pawn p)
        {
            return p.RaceProps != null && p.RaceProps.Humanlike;
        }

        private static bool CanBeHandled(Pawn p)
        {
            if (p.IsQuestLodger())
            {
                return false;
            }

            if (p.IsColonist || p.IsPrisonerOfColony || p.IsSlaveOfColony)
            {
                return false;
            }

            if (p.Faction != null && p.Faction.def != null && p.Faction.def.isPlayer)
            {
                return false;
            }

            return true;
        }

        private static bool IsRetreatTarget(Pawn p)
        {
            if (!CanBeHandled(p))
            {
                return false;
            }

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return false;
            }

            if (p.HostileTo(player))
            {
                return true;
            }

            if (p.InAggroMentalState)
            {
                return true;
            }

            MentalStateDef msd = p.MentalStateDef;
            if (msd != null
                && (msd == MentalStateDefOf.Manhunter || msd == MentalStateDefOf.ManhunterPermanent))
            {
                return true;
            }

            return false;
        }

        // ============================================================
        // 开始一次个体撤退
        // ============================================================

        private static bool StartRetreat(Pawn p, int now)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].pawn == p)
                {
                    return false;
                }
            }

            RetreatEntry e = new RetreatEntry();
            e.pawn = p;
            e.map = p.Map;
            e.startTick = now;
            e.lastIssueTick = -999;
            e.modeStartTick = now;
            e.stealCount = 0;
            e.lastPos = p.PositionHeld;
            e.lastMoveTick = now;

            bool isEntity = IsAnomalyEntity(p);
            bool isAggroAnimal = p.RaceProps != null && p.RaceProps.Animal && p.InAggroMentalState;

            IntVec3 exit = FindExitCell(p);
            if (!exit.IsValid)
            {
                e.mode = RetreatMode.FarthestPoint;
                e.target = FindFarthestCell(p.Map);
                if (!e.target.IsValid)
                {
                    return false;
                }

                BinguinLogUtility.Log(
                    "强制撤退：" + p.LabelShort + " 无可用边缘，改走「离地图中心最远处」。", isDebug: false);
            }
            else if (isEntity || isAggroAnimal)
            {
                e.mode = RetreatMode.ExitJob;
                e.target = exit;
                e.waitUntilEdge = isEntity;

                BinguinLogUtility.Log(
                    "强制撤退：" + p.LabelShort + "（" + (isEntity ? "异象实体" : "发狂动物")
                    + "）直接走离场 Job。", isDebug: false);
            }
            else
            {
                e.mode = RetreatMode.WalkToEdge;
                e.target = exit;
            }

            active.Add(e);

            if (e.mode == RetreatMode.ExitJob)
            {
                JobDef exitDef = ExitMapJobDef;
                if (exitDef != null)
                {
                    IssueJob(e, now, exitDef, e.target);
                }
            }
            else
            {
                IssueJob(e, now, JobDefOf.Goto, e.target);
            }

            return true;
        }

        private static bool IsAnomalyEntity(Pawn p)
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

        // ============================================================
        // 目标点计算
        // ============================================================

        /// <summary>找离该单位最近、且真正可到达的地图边缘格。</summary>
        private static IntVec3 FindExitCell(Pawn pawn)
        {
            IntVec3 vanillaSpot;
            try
            {
                if (RCellFinder.TryFindBestExitSpot(pawn, out vanillaSpot))
                {
                    return vanillaSpot;
                }
            }
            catch (Exception)
            {
            }

            return FindExitCellByScan(pawn.Map, pawn.PositionHeld);
        }

        private static IntVec3 FindExitCellByScan(Map map, IntVec3 from)
        {
            IntVec3 best = IntVec3.Invalid;
            int bestDist = int.MaxValue;

            int maxRing = Math.Max(8, map.Size.x / 4);
            for (int ring = 0; ring < maxRing; ring++)
            {
                for (int x = ring; x < map.Size.x - ring; x++)
                {
                    CheckCell(map, from, new IntVec3(x, 0, ring), ref best, ref bestDist);
                    CheckCell(map, from, new IntVec3(x, 0, map.Size.z - 1 - ring), ref best, ref bestDist);
                }

                for (int z = ring; z < map.Size.z - ring; z++)
                {
                    CheckCell(map, from, new IntVec3(ring, 0, z), ref best, ref bestDist);
                    CheckCell(map, from, new IntVec3(map.Size.x - 1 - ring, 0, z), ref best, ref bestDist);
                }

                if (best.IsValid && ring > 2)
                {
                    break;
                }
            }

            return best;
        }

        /// <summary>找地图上离地图中心最远的可站立格（按地图缓存）。</summary>
        private static IntVec3 FindFarthestCell(Map map)
        {
            IntVec3 cached;
            if (farthestCellCache.TryGetValue(map.uniqueID, out cached))
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

                    int dist = (c - center).LengthHorizontalSquared;
                    if (dist > bestDist)
                    {
                        bestDist = dist;
                        best = c;
                    }
                }
            }

            farthestCellCache[map.uniqueID] = best;
            return best;
        }

        private static void CheckCell(Map map, IntVec3 from, IntVec3 c, ref IntVec3 best, ref int bestDist)
        {
            if (!c.InBounds(map))
            {
                return;
            }

            if (!c.Walkable(map) || !c.Standable(map))
            {
                return;
            }

            int dist = (c - from).LengthHorizontalSquared;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = c;
            }
        }

        // ============================================================
        // Job 下发
        // ============================================================

        private static JobDef ExitMapJobDef
        {
            get
            {
                if (exitMapJobDef == null)
                {
                    exitMapJobDef = DefDatabase<JobDef>.GetNamedSilentFail("ExitMapBest");
                    if (exitMapJobDef == null)
                    {
                        exitMapJobDef = DefDatabase<JobDef>.GetNamedSilentFail("ExitMap");
                    }

                    if (exitMapJobDef == null)
                    {
                        BinguinLogUtility.WarningOnce(
                            "强制撤退：找不到原版离场 Job，离场阶段将退化为直接移出地图。", 962231);
                    }
                }
                return exitMapJobDef;
            }
        }

        private static bool IsRunningJob(RetreatEntry e, JobDef def)
        {
            if (e.pawn.jobs == null || e.pawn.CurJob == null || def == null)
            {
                return false;
            }

            if (e.pawn.CurJobDef != def)
            {
                return false;
            }

            return e.pawn.CurJob.targetA.Cell == e.target;
        }

        private static bool IsRunningJobOfDef(RetreatEntry e, JobDef def)
        {
            if (e.pawn.jobs == null || e.pawn.CurJob == null || def == null)
            {
                return false;
            }

            return e.pawn.CurJobDef == def;
        }

        private static void IssueJob(RetreatEntry e, int now, JobDef def, IntVec3 cell)
        {
            try
            {
                Pawn p = e.pawn;

                ClearMentalState(p);
                DetachFromLord(p);

                p.jobs.StopAll(false, true);

                Job job = new Job(def, cell);
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
                BinguinLogUtility.WarningOnce(
                    "强制撤退：下发 Job 失败，将改为移出地图。" + ex.Message, 962214);
                RemoveFromMap(e);
                e.done = true;
            }
        }

        /// <summary>解除单位当前精神状态（只解除，不施加）。</summary>
        private static void ClearMentalState(Pawn p)
        {
            try
            {
                if (!p.InMentalState)
                {
                    return;
                }

                MentalState state = p.MentalState;
                if (state == null)
                {
                    return;
                }

                MentalStateDef def = state.def;
                state.RecoverFromState();

                if (p.InMentalState && p.mindState != null && p.mindState.mentalStateHandler != null)
                {
                    p.mindState.mentalStateHandler.Reset();
                }

                BinguinLogUtility.Log(
                    "强制撤退：已解除 " + p.LabelShort + " 的精神状态「"
                    + (def != null ? def.label : "未知") + "」。", isDebug: false);
            }
            catch (Exception ex)
            {
                BinguinLogUtility.WarningOnce(
                    "强制撤退：解除精神状态失败。" + ex.Message, 962221);
            }
        }

        private static void DetachFromLord(Pawn p)
        {
            try
            {
                Lord lord = p.GetLord();
                if (lord == null)
                {
                    return;
                }

                lord.RemovePawn(p);
            }
            catch (Exception ex)
            {
                BinguinLogUtility.WarningOnce(
                    "强制撤退：脱离 Lord 失败。" + ex.Message, 962219);
            }
        }

        private static void ReleasePawn(Pawn p)
        {
            try
            {
                if (p.jobs != null && p.CurJob != null)
                {
                    JobDef cur = p.CurJobDef;
                    if (cur == JobDefOf.Goto || cur == ExitMapJobDef)
                    {
                        p.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                    }
                }
            }
            catch (Exception ex)
            {
                BinguinLogUtility.WarningOnce(
                    "强制撤退：交还控制权失败。" + ex.Message, 962222);
            }
        }

        // ============================================================
        // 移出地图 / 阵营转换
        // ============================================================

        private static void RemoveFromMap(RetreatEntry e)
        {
            Pawn p = e.pawn;
            if (p == null || p.Destroyed)
            {
                return;
            }

            try
            {
                Lord lord = p.Spawned ? p.GetLord() : null;

                removedIds.Add(p.thingIDNumber);
                if (removedIds.Count > MaxRemovedIds)
                {
                    removedIds.Clear();
                }

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

                BinguinLogUtility.Log("强制撤退：已将 " + p.LabelShort + " 移出地图。", isDebug: false);
            }
            catch (Exception ex)
            {
                BinguinLogUtility.WarningOnce("强制撤退：移出地图失败。" + ex.Message, 962215);
            }
        }

        /// <summary>把单位阵营转为「难民阵营」（原版 OutlanderRefugee）。</summary>
        private static void NeutralizeFaction(Pawn p)
        {
            try
            {
                if (!IsHumanlike(p))
                {
                    return;
                }

                Faction target = GetRefugeeFaction();

                if (target == null)
                {
                    if (p.Faction == null)
                    {
                        return;
                    }

                    BinguinLogUtility.WarningOnce(
                        "强制撤退：找不到难民派系，退化为无派系中立。", 962217);
                    p.SetFaction(null);
                    p.mindState.enemyTarget = null;
                    return;
                }

                if (p.Faction == target)
                {
                    return;
                }

                Lord lord = p.GetLord();
                if (lord != null)
                {
                    lord.Notify_PawnLost(p, PawnLostCondition.Undefined);
                }

                string oldFaction = p.Faction != null ? p.Faction.Name : "无";
                p.SetFaction(target);
                p.mindState.enemyTarget = null;

                BinguinLogUtility.Log(
                    "强制撤退：" + p.LabelShort + " 由「" + oldFaction + "」转入难民阵营，再次撤退。",
                    isDebug: false);
            }
            catch (Exception ex)
            {
                BinguinLogUtility.WarningOnce("强制撤退：转中立失败。" + ex.Message, 962216);
            }
        }

        private static Faction GetRefugeeFaction()
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
                BinguinLogUtility.WarningOnce("强制撤退：生成难民派系失败。" + ex.Message, 962218);
                return null;
            }
        }

        // ============================================================
        // 开发用入口
        // ============================================================

        [DebugAction("冰鹅族", "技能对抗：全图强制撤退",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_RetreatAll()
        {
            int n = RetreatAll(Find.CurrentMap);
            Messages.Message("已强制 " + n + " 个单位开始撤退。", MessageTypeDefOf.NeutralEvent);
        }

        [DebugAction("冰鹅族", "技能对抗：撤退（仅一次，无持续压制）",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_RetreatOnce()
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
