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

        private sealed class RetreatEntry
        {
            public Pawn pawn;
            public Map map;
            public IntVec3 target;
            public int startTick;
            public int lastIssueTick;
            public bool removeNow;
        }

        private static readonly List<RetreatEntry> active = new List<RetreatEntry>();

        private static int suppressUntilTick = -1;
        private static int nextSuppressScanTick;
        private static Map suppressMap;

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

                if (p.Position.InHorDistOf(e.target, EdgeReachRadius) || now - e.startTick > MaxRetreatTicks)
                {
                    RemoveFromMap(e);
                    active.RemoveAt(i);
                    continue;
                }

                // 倒地 / 无法移动：等它恢复；超过时限由上面的超时处理
                if (p.Downed || !p.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                {
                    continue;
                }

                if (now - e.lastIssueTick >= ReissueInterval || !IsRunningRetreatJob(e))
                {
                    bool blocked = IssueRetreatJob(e, now);
                    if (blocked)
                    {
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

        private static bool IsRetreatTarget(Pawn p)
        {
            if (p == null || p.Dead || !p.Spawned || p.Destroyed)
            {
                return false;
            }

            // 兜底 C：任务绑定单位不动，避免任务失败 / 报错
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

                // 抢走控制权：停掉一切正在做的事（含战斗 / 攻击 / 搬运）
                p.jobs.StopAll(false, true);

                Job job = new Job(JobDefOf.Goto, e.target);
                job.locomotionUrgency = LocomotionUrgency.Sprint;
                job.expiryInterval = 600;
                job.checkOverrideOnExpire = false;

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
