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
    /// <summary>
    /// 强制撤退（技能对抗）
    /// ============================================================
    /// 需求：让全图所有「敌对」或「发狂的中立」单位撤退，**且无论如何都要生效**。
    ///
    /// 为什么不用常规做法：
    ///   - 精神状态（MentalState）→ 可被 mod 免疫 / 清除
    ///   - Hediff（如 Fleeing）    → 可被免疫 / 驱散
    ///   - XML Def 属性            → 可被补丁改写
    ///   - Harmony Patch           → 可被其他补丁覆盖或跳过
    ///
    /// 本实现只走引擎底层、没有「免疫」判定的通道：
    ///   1) 直接改写 Pawn 的 Job（JobDefOf.Goto），强制其朝最近的地图边缘移动；
    ///   2) 每 15 tick 复查并重新下发（对手动改 Job 的 AI 也无效化）；
    ///   3) 走到边缘 / 超过时限 / Job 被完全拦截 → 直接 DeSpawn + PassToWorld，
    ///      移出地图（只经过 Thing / WorldPawns 层）。
    ///
    /// 三重兜底：
    ///   A) 地图没有可走边缘 → 直接移出地图（不再静默失败）
    ///   B) 效果后一段时间内持续复查：被移除后又被重新刷出来的单位，会再被撤一次
    ///   C) 任务绑定单位（IsQuestLodger）跳过，避免崩任务
    /// </summary>
    public static class ForcedRetreat_Binguin
    {
        /// <summary>重新下发撤退指令的间隔（tick）。</summary>
        private const int ReissueInterval = 15;

        /// <summary>单个单位最长撤退时间（tick），超时直接移出地图。</summary>
        private const int MaxRetreatTicks = 900;

        /// <summary>距离目标边缘多少格内视作「已走到」。</summary>
        private const int EdgeReachRadius = 2;

        /// <summary>默认的持续压制时长（tick，60 秒）。</summary>
        private const int DefaultSuppressTicks = 3600;

        /// <summary>持续压制期间的复查间隔（tick，1 秒）。</summary>
        private const int SuppressScanInterval = 60;

        /// <summary>连续被抢多少次 Job 后，判定「走不出去」，改为瞬移到边缘或直接移出。</summary>
        private const int MaxStealBeforeTeleport = 12;

        /// <summary>连续被抢多少次 Job 后，放弃撤退演出，直接移出地图。</summary>
        private const int MaxStealBeforeRemove = 40;

        private sealed class RetreatEntry
        {
            public Pawn pawn;
            public Map map;
            public IntVec3 target;
            public int startTick;
            public int lastIssueTick;
            public bool removeNow;

            /// <summary>Job 被抢走的次数。连续被抢说明 AI 在跟我们拉锯。</summary>
            public int stealCount;

            /// <summary>上一次所在格，用于判断是否卡住（被墙挡住）。</summary>
            public IntVec3 lastPos;

            /// <summary>最后一次发生位移的 tick。</summary>
            public int lastMoveTick;
        }

        /// <summary>原地不动超过多少 tick 判定为「被墙挡住 / 走不通」。</summary>
        private const int StuckTicksBeforeTeleport = 120;

        private static readonly List<RetreatEntry> active = new List<RetreatEntry>();

        /// <summary>已被移出地图过的单位（用于识别「又被刷回来」的单位）。</summary>
        private static readonly HashSet<int> removedIds = new HashSet<int>();

        private static int suppressUntilTick = -1;
        private static int nextSuppressScanTick;
        private static Map suppressMap;
        private static Faction refugeeFaction;

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
        /// <param name="suppressTicks">
        /// 持续压制时长：这段时间内每秒复查一次地图，新出现 / 被重新刷出的敌对单位会继续被撤。
        /// 传 0 表示只执行一次。默认 3600 tick（60 秒）。
        /// </param>
        /// <returns>本次直接下达撤退令的单位数量。</returns>
        public static int RetreatAll(Map map, int suppressTicks = DefaultSuppressTicks)
        {
            if (map == null)
            {
                return 0;
            }

            int now = Find.TickManager.TicksGame;

            if (suppressTicks > 0)
            {
                suppressMap = map;
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

        /// <summary>逐帧推进。由 GameComponent_BinguinForcedRetreat 调用。</summary>
        public static void Tick()
        {
            int now = Find.TickManager.TicksGame;

            // 兜底 B：持续压制窗口内定期复查
            if (suppressMap != null
                && now <= suppressUntilTick
                && now >= nextSuppressScanTick)
            {
                nextSuppressScanTick = now + SuppressScanInterval;
                SweepMap(suppressMap, now);
            }

            if (active.Count == 0)
            {
                return;
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                RetreatEntry e = active[i];
                Pawn p = e.pawn;

                if (p == null || p.Dead || !p.Spawned || p.Map != e.map)
                {
                    active.RemoveAt(i);
                    continue;
                }

                // 兜底 A：一开始就没找到边缘 → 直接移出地图
                if (e.removeNow)
                {
                    RemoveFromMap(e);
                    active.RemoveAt(i);
                    continue;
                }

                // 倒地的不处理：让他躺着（会被俘虏/失血），既不追也不移出
                if (p.Downed)
                {
                    continue;
                }

                if (p.Position.InHorDistOf(e.target, EdgeReachRadius) || now - e.startTick > MaxRetreatTicks)
                {
                    RemoveFromMap(e);
                    active.RemoveAt(i);
                    continue;
                }

                // 无法移动（不是倒地，而是瘫痪等）：等恢复；超时由上面的时限处理
                if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                {
                    continue;
                }

                // 卡死检测：长时间没挪窝 = 被墙 / 门挡住，直接瞬移到边缘
                if (p.Position != e.lastPos)
                {
                    e.lastPos = p.Position;
                    e.lastMoveTick = now;
                }
                else if (now - e.lastMoveTick > StuckTicksBeforeTeleport)
                {
                    BinguinLogUtility.Log(
                        "强制撤退：" + p.LabelShort + " 被障碍挡住，瞬移到地图边缘。", isDebug: false);
                    TryTeleportToExit(e);
                    e.lastMoveTick = now;
                    continue;
                }

                if (now - e.lastIssueTick >= ReissueInterval || !IsRunningRetreatJob(e))
                {
                    // 被抢的次数：每发现一次 Job 不是我们的，就记一笔
                    if (!IsRunningRetreatJob(e))
                    {
                        e.stealCount++;
                    }

                    bool blocked = IssueRetreatJob(e, now);
                    if (blocked)
                    {
                        RemoveFromMap(e);
                        active.RemoveAt(i);
                        continue;
                    }

                    // 抢得太多：先尝试直接瞬移到边缘（跳过寻路与 Job）
                    if (e.stealCount >= MaxStealBeforeTeleport && e.stealCount < MaxStealBeforeRemove)
                    {
                        TryTeleportToExit(e);
                    }

                    // 还在拉锯：放弃撤退演出，直接移出地图
                    if (e.stealCount >= MaxStealBeforeRemove)
                    {
                        BinguinLogUtility.Log(
                            "强制撤退：" + p.LabelShort + " 的 Job 反复被抢，直接移出地图。", isDebug: false);
                        RemoveFromMap(e);
                        active.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>清空跟踪（读档 / 换图时用）。</summary>
        public static void Clear()
        {
            active.Clear();
            suppressMap = null;
            suppressUntilTick = -1;
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

                // 被移出过又重新出现的单位：先把阵营转成中立，再撤一次
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

        /// <summary>自己人、囚犯、奴隶、任务居客一律不处理。</summary>
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

        /// <summary>
        /// 把单位阵营转为「难民阵营」（原版 OutlanderRefugee，中立）。
        /// 之后再走一遍撤退流程，让它离开地图。
        /// </summary>
        private static void NeutralizeFaction(Pawn p)
        {
            try
            {
                Faction target = GetRefugeeFaction();

                if (target == null)
                {
                    // 原版难民派系不可用时的兜底：无派系 = 中立
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
                BinguinLogUtility.WarningOnce(
                    "强制撤退：转中立失败。" + ex.Message, 962216);
            }
        }

        /// <summary>难民派系：优先复用世界已有的，没有就生成一个（隐藏派系，不显示在地图上）。</summary>
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
                BinguinLogUtility.WarningOnce(
                    "强制撤退：生成难民派系失败。" + ex.Message, 962218);
                return null;
            }
        }

        private static bool IsRetreatTarget(Pawn p)
        {
            if (!CanBeHandled(p))
            {
                return false;
            }

            if (p.HostileTo(Faction.OfPlayer))
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
        // 内部实现
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
            e.stealCount = 0;
            e.lastPos = p.PositionHeld;
            e.lastMoveTick = now;

            IntVec3 exit = FindExitCell(p.Map, p.PositionHeld);
            if (!exit.IsValid)
            {
                // 兜底 A：地图没有可走的边缘（四面环山 / 深水 / 全封闭）
                e.target = p.PositionHeld;
                e.removeNow = true;
                active.Add(e);
                BinguinLogUtility.Log(
                    "强制撤退：地图无可用边缘，直接移出 " + p.LabelShort + "。", isDebug: false);
                return true;
            }

            e.target = exit;
            e.removeNow = false;
            active.Add(e);
            IssueRetreatJob(e, now);
            return true;
        }

        /// <summary>找离该单位最近的可站立地图边缘格。</summary>
        private static IntVec3 FindExitCell(Map map, IntVec3 from)
        {
            IntVec3 best = IntVec3.Invalid;
            int bestDist = int.MaxValue;

            int maxRing = Math.Max(8, map.Size.x / 4);
            for (int ring = 0; ring < maxRing; ring++)
            {
                for (int x = ring; x < map.Size.x - ring; x++)
                {
                    CheckEdgeCell(map, from, new IntVec3(x, 0, ring), ref best, ref bestDist);
                    CheckEdgeCell(map, from, new IntVec3(x, 0, map.Size.z - 1 - ring), ref best, ref bestDist);
                }

                for (int z = ring; z < map.Size.z - ring; z++)
                {
                    CheckEdgeCell(map, from, new IntVec3(ring, 0, z), ref best, ref bestDist);
                    CheckEdgeCell(map, from, new IntVec3(map.Size.x - 1 - ring, 0, z), ref best, ref bestDist);
                }

                if (best.IsValid && ring > 2)
                {
                    break;
                }
            }

            return best;
        }

        private static void CheckEdgeCell(Map map, IntVec3 from, IntVec3 c, ref IntVec3 best, ref int bestDist)
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

        private static bool IsRunningRetreatJob(RetreatEntry e)
        {
            if (e.pawn.jobs == null || e.pawn.CurJob == null)
            {
                return false;
            }

            if (e.pawn.CurJobDef != JobDefOf.Goto)
            {
                return false;
            }

            return e.pawn.CurJob.targetA.Cell == e.target;
        }

        /// <returns>true 表示被彻底拦截，应改为移出地图。</returns>
        private static bool IssueRetreatJob(RetreatEntry e, int now)
        {
            try
            {
                Pawn p = e.pawn;

                // ★ 先剥离精神状态的接管（狂暴 / 猎杀 / 恐惧等）。
                //   精神状态会压住 Job，清掉之后 Job 才能真正跑起来。
                ClearMentalState(p);

                // ★ 再把它从 Lord 里踢出去。
                //   绝大多数「Job 被抢」的根源就是袭击领主（LordJob_AssaultColony）
                //   的 toil 在每 tick 给成员重新派单；踢出 lord 后没人再抢。
                DetachFromLord(p);

                // 抢走控制权：停掉一切正在做的事（含战斗 / 攻击 / 搬运）
                p.jobs.StopAll(false, true);

                Job job = new Job(JobDefOf.Goto, e.target);
                job.locomotionUrgency = LocomotionUrgency.Sprint;
                job.expiryInterval = 600;
                job.checkOverrideOnExpire = false;

                // ★ 路被门挡住时允许砸门（1.7 的 Job 没有 canBash，墙靠下面的卡死检测处理）
                job.canBashDoors = true;

                p.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);

                // 断掉当前目标，避免边走边回身打人
                p.mindState.enemyTarget = null;

                e.lastIssueTick = now;
                return !IsRunningRetreatJob(e);
            }
            catch (Exception ex)
            {
                BinguinLogUtility.WarningOnce(
                    "强制撤退：下发撤退 Job 失败，将改为移出地图。" + ex.Message, 962214);
                return true;
            }
        }

        /// <summary>
        /// 清掉单位当前的精神状态，让 Job 能真正生效。
        /// 只做「解除」，不做「施加」，因此不受任何「精神状态免疫」影响。
        /// </summary>
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

                // RecoverFromState 可能因为 mod 保护没生效，再补一次硬复位
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

        /// <summary>把单位从所属 Lord（袭击队 / 商队护卫等）中移除，断掉 AI 派单来源。</summary>
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

        /// <summary>
        /// 直接把单位瞬移到撤退目标格。
        /// 绕过寻路与 Job——被抢 Job / 走不通时的第二级手段。
        /// </summary>
        private static void TryTeleportToExit(RetreatEntry e)
        {
            Pawn p = e.pawn;
            if (p == null || !p.Spawned || !e.target.IsValid)
            {
                return;
            }

            try
            {
                if (!e.target.InBounds(e.map) || !e.target.Standable(e.map))
                {
                    return;
                }

                // 目标格被别的单位占了就先跳过（下一 tick 再试）
                if (e.target.GetFirstPawn(e.map) != null)
                {
                    return;
                }

                p.Position = e.target;
                p.Notify_Teleported(false, false);
            }
            catch (Exception ex)
            {
                BinguinLogUtility.WarningOnce(
                    "强制撤退：瞬移到边缘失败。" + ex.Message, 962220);
            }
        }

        /// <summary>
        /// 直接把单位移出地图。
        /// 只经过 Thing / WorldPawns 层，不经过 hediff、精神状态、免疫、抗性等任何判定。
        /// </summary>
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
                BinguinLogUtility.WarningOnce(
                    "强制撤退：移出地图失败。" + ex.Message, 962215);
            }
        }

        // ============================================================
        // 开发用入口（技能暂不实装，先用调试菜单验证）
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

    /// <summary>
    /// 逐帧驱动。
    /// RimWorld 通过反射扫描所有 GameComponent 子类并自动实例化，
    /// 因此不需要任何 Def / XML 就能拿到一个每 tick 的回调。
    /// </summary>
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
