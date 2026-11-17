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
    /// 分级处理（循序渐进）：
    ///   ① 能撤退的普通敌人 → 正常走到最近的地图边缘 → 离场
    ///   ② 死战不退的敌人（Job 反复被抢）→ 改走「离场 Job」
    ///   ③ 发狂动物 / 异象实体 → 直接走「离场 Job」
    ///   ④ 地图上没有可离开的方格 → 走到离地图中心最远的地方 → 离场
    ///
    /// 不用 XML / 精神状态施加 / Hediff / Harmony Patch：
    ///   - 只「解除」精神状态，不「施加」
    ///   - 只改写 Job、脱离 Lord、移出地图（Thing / WorldPawns 层）
    /// </summary>
    public static class ForcedRetreat_Binguin
    {
        // ---------------------------------------------------------------- 时间参数
        /// <summary>Job 仍在跑时的复查间隔。</summary>
        private const int ReissueInterval = 15;

        /// <summary>两次「真正下发 Job」的最小间隔，防止每 tick 拉锯。</summary>
        private const int MinIssueCooldown = 6;

        /// <summary>
        /// 单位最近这么多个 tick 内还在移动，就不要再重发 Job
        /// （重发会 StopAll 打断寻路，反而让它走得更慢）。
        /// </summary>
        private const int ReissueRefreshTicks = 120;

        /// <summary>单个单位最长走路撤退时间：1 小时，超时改走离场 Job。</summary>
        private const int MaxWalkTicks = 2500;

        /// <summary>离场 Job 阶段最长等待时间，超时直接移出地图。</summary>
        /// <summary>离场 Job 阶段最长等待时间：2 小时（1 小时 = 2500 tick）。</summary>
        private const int MaxExitJobTicks = 2 * 2500;

        /// <summary>倒地单位最多跟踪多久（2 小时），之后丢弃跟踪（不移出）。</summary>
        private const int MaxDownedTrackTicks = 2 * 2500;

        /// <summary>已移出单位 ID 的记录上限，超出清空，避免长期存档无限增长。</summary>
        private const int MaxRemovedIds = 5000;

        /// <summary>距离目标边缘多少格内视作「已走到」。</summary>
        private const int EdgeReachRadius = 2;

        /// <summary>走路阶段被抢多少次 Job 后，判定为「死战不退」，改走离场 Job。</summary>
        private const int StealsBeforeExitJob = 6;

        /// <summary>离场 Job 阶段被抢多少次后，直接移出地图。</summary>
        private const int StealsBeforeRemove = 24;

        /// <summary>原地不动超过多少 tick 判定被墙挡住（判定后放手不管）。</summary>
        private const int StuckTicks = 120;

        /// <summary>默认持续压制时长（60 秒）。</summary>
        private const int DefaultSuppressTicks = 3600;

        /// <summary>持续压制期间的复查间隔（1 秒）。</summary>
        private const int SuppressScanInterval = 60;

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

            /// <summary>已处理完毕，等待主循环移除（避免边遍历边删）。</summary>
            public bool done;
        }

        private static readonly List<RetreatEntry> active = new List<RetreatEntry>();
        private static readonly HashSet<int> removedIds = new HashSet<int>();
        private static readonly HashSet<int> stuckIds = new HashSet<int>();

        private static int suppressUntilTick = -1;
        private static int nextSuppressScanTick;
        private static readonly List<Map> suppressMaps = new List<Map>();
        private static Faction refugeeFaction;
        private static JobDef exitMapJobDef;
        private static readonly Dictionary<int, IntVec3> farthestCellCache = new Dictionary<int, IntVec3>();

        /// <summary>当前正在撤退的单位数量。</summary>
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
        /// <param name="suppressTicks">持续压制时长；0 表示只执行一次。默认 60 秒。</param>
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

                // 多张地图各自记录（内心的呐喊会同时作用于所有地图）
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

                // 倒地：不管（不移出、不追）。
                // 但跟踪有上限，避免长期存档里越积越多：超过 2 小时后丢弃跟踪。
                if (p.Downed)
                {
                    if (now - e.startTick > MaxDownedTrackTicks)
                    {
                        e.done = true;
                    }
                    continue;
                }

                // 到达目标点 → 离开地图
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

                // 卡住判定：区分「被抢导致卡住」和「被墙挡住」
                if (p.Position != e.lastPos)
                {
                    e.lastPos = p.Position;
                    e.lastMoveTick = now;
                }
                else if (p.pather != null && p.pather.Moving)
                {
                    // 还在走，只是慢（雪地、断腿、负重）：不算卡住
                    e.lastMoveTick = now;
                }
                else if (now - e.lastMoveTick > StuckTicks)
                {
                    JobDef curDef = e.mode == RetreatMode.ExitJob ? ExitMapJobDef : JobDefOf.Goto;
                    bool jobStolen = !IsRunningJob(e, curDef);

                    if (jobStolen)
                    {
                        // ★ 被抢卡住：不结束处理，直接改走离场 Job 继续
                        if (e.mode != RetreatMode.ExitJob)
                        {
                            SwitchToExitJob(e, now, "卡住且 Job 被抢");
                        }
                        else
                        {
                            e.stealCount++;
                            if (e.stealCount >= StealsBeforeRemove)
                            {
                                BinguinLogUtility.Log(
                                    "强制撤退：" + p.LabelShort + " 离场 Job 反复被抢，直接移出地图。",
                                    isDebug: false);
                                RemoveFromMap(e);
                                e.done = true;
                                continue;
                            }
                        }

                        // 重置卡住计时，继续走
                        e.lastMoveTick = now;
                        continue;
                    }

                    // 走的是我们自己的 Job 却不动 = 被墙挡住 → 放手，交还 AI
                    BinguinLogUtility.Log(
                        "强制撤退：" + p.LabelShort + " 被障碍挡住，放弃对该单位的处理。", isDebug: false);
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
            stuckIds.Clear();
            removedIds.Clear();
            farthestCellCache.Clear();
            suppressMaps.Clear();
            suppressUntilTick = -1;
        }

        // ============================================================
        // 两种推进模式
        // ============================================================

        /// <summary>走路模式：朝目标点走；走不动就升级为离场 Job。</summary>
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
                // 正在正常移动就不打扰它（重发会打断寻路，反而让它走得更慢）
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
                // 只有「上一次下发之后又过了冷却期」才计一次被抢，
                // 否则 Job 刚下发、还没生效的瞬间也会被误判成被抢。
                if (now - e.lastIssueTick < MinIssueCooldown)
                {
                    return;
                }

                e.stealCount++;

                // 被抢太多次 → 判定「死战不退」，改走离场 Job
                if (e.stealCount >= StealsBeforeExitJob)
                {
                    SwitchToExitJob(e, now, "Job 反复被抢");
                    return;
                }
            }

            IssueJob(e, now, JobDefOf.Goto, e.target);
        }

        /// <summary>离场 Job 模式：直接让单位走原版的离场 Job 离开地图。</summary>
        private static void TickExitJob(RetreatEntry e, int now)
        {
            if (now - e.modeStartTick > MaxExitJobTicks)
            {
                BinguinLogUtility.Log(
                    "强制撤退：" + e.pawn.LabelShort + " 离场 Job 超时，直接移出地图。", isDebug: false);
                RemoveFromMap(e);
                e.done = true;
                return;
            }

            JobDef exitDef = ExitMapJobDef;
            if (exitDef == null)
            {
                // 找不到原版离场 Job：退化为直接移出
                RemoveFromMap(e);
                e.done = true;
                return;
            }

            // 离场 Job 由 driver 自己找出口，targetA 未必等于我们给的格子，
            // 所以这里只看 Job 类型，不比对目标格。
            bool running = IsRunningJobOfDef(e, exitDef);

            if (running)
            {
                // 正在正常移动就不打扰它
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
                if (e.stealCount >= StealsBeforeRemove)
                {
                    BinguinLogUtility.Log(
                        "强制撤退：" + e.pawn.LabelShort + " 离场 Job 也被拦，直接移出地图。", isDebug: false);
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
            int started = 0;
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

                // 被移出过又重新出现：先转难民阵营，再撤一次
                if (removedIds.Contains(p.thingIDNumber))
                {
                    if (!CanBeHandled(p))
                    {
                        continue;
                    }

                    NeutralizeFaction(p);

                    if (StartRetreat(p, now))
                    {
                        started++;
                    }
                    continue;
                }

                if (!IsRetreatTarget(p))
                {
                    continue;
                }

                if (StartRetreat(p, now))
                {
                    started++;
                }
            }

            if (started > 0)
            {
                BinguinLogUtility.Log("强制撤退：对 " + started + " 个单位下撤退令。", isDebug: false);
            }

            return started;
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
        // 开始一次撤退
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

            // 决定走哪种模式
            bool isEntity = IsAnomalyEntity(p);
            bool isAggroAnimal = p.RaceProps != null && p.RaceProps.Animal && p.InAggroMentalState;

            IntVec3 exit = FindExitCell(p);
            if (!exit.IsValid)
            {
                // ④ 地图上没有可离开的方格 → 走到离地图中心最远处
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
                // ③ 发狂动物 / 异象实体 → 直接离场 Job
                e.mode = RetreatMode.ExitJob;
                e.target = exit;
                BinguinLogUtility.Log(
                    "强制撤退：" + p.LabelShort + "（" + (isEntity ? "异象实体" : "发狂动物")
                    + "）直接走离场 Job。", isDebug: false);
            }
            else
            {
                // ①② 普通敌人 → 正常走过去；被抢多了在 TickWalk 里升级为离场 Job
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

        /// <summary>是否为 Anomaly 异象实体。</summary>
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

        /// <summary>
        /// 找离该单位最近、且**真正可到达**的地图边缘格。
        /// 先用原版 RCellFinder（会考虑可达性），失败再退回自己按环形扫描。
        /// </summary>
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
                // 原版工具失败时继续走下面的扫描
            }

            return FindExitCellByScan(pawn.Map, pawn.PositionHeld);
        }

        /// <summary>按环形扫描找最近的可站立边缘格（原版工具失败时的兜底）。</summary>
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

        /// <summary>
        /// 找地图上「离地图中心最远」的可站立格。
        /// 用于整张地图都没有可用边缘的情况。结果按地图缓存。
        /// </summary>
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
                            "强制撤退：找不到原版离场 Job（ExitMapBest / ExitMap），离场阶段将退化为直接移出地图。",
                            962231);
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

        /// <summary>只比对 Job 类型（离场 Job 用：driver 会自己挑出口格）。</summary>
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

        /// <summary>把单位从所属 Lord 中移除，断掉袭击领主的每 tick 派单。</summary>
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

        /// <summary>交还控制权（被墙卡住时用）。</summary>
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
                    BinguinLogUtility.Log(
                        "强制撤退：已移出单位记录超过上限，已重置（不影响本次效果）。", isDebug: false);
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
                // 只对人形单位做阵营转换：动物 / 机械体没有「难民」概念
                if (p.RaceProps != null && !p.RaceProps.Humanlike)
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

    /// <summary>逐帧驱动：RimWorld 反射扫描 GameComponent 子类并自动实例化，无需 Def。</summary>
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
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ForcedRetreat_Binguin.Clear();
            }
        }
    }
}
