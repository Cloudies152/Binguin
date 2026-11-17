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
    /// 本实现只使用引擎底层、无法被「免疫」的通道：
    ///   1) 直接改写 Pawn 的 Job（JobDefOf.Goto），强制其朝最近的地图边缘移动；
    ///   2) 每 15 tick 复查并重新下发（对手动改 Job 的 AI 也无效化）；
    ///   3) 走到边缘、超过时限、或 Job 被完全拦截 → 直接 DeSpawn + PassToWorld，
    ///      把单位移出地图（只经过 Thing / WorldPawns 层，
    ///      不经过任何 hediff / 精神状态 / 免疫判定）。
    ///
    /// 不新增任何 Def（无需 XML），逐帧推进由反射自动注册的 GameComponent 提供。
    /// </summary>
    public static class ForcedRetreat_Binguin
    {
        /// <summary>重新下发撤退指令的间隔（tick）。</summary>
        private const int ReissueInterval = 15;

        /// <summary>最长撤退时间（tick），超时直接移出地图。</summary>
        private const int MaxRetreatTicks = 900;

        /// <summary>距离目标边缘多少格内视作「已走到」。</summary>
        private const int EdgeReachRadius = 2;

        private sealed class RetreatEntry
        {
            public Pawn pawn;
            public Map map;
            public IntVec3 target;
            public int startTick;
            public int lastIssueTick;
            public bool jobBlocked;
        }

        private static readonly List<RetreatEntry> active = new List<RetreatEntry>();

        /// <summary>当前正在撤退的单位数量。</summary>
        public static int ActiveCount
        {
            get { return active.Count; }
        }

        // ============================================================
        // 对外入口
        // ============================================================

        /// <summary>
        /// 让指定地图上所有敌对 / 发狂的中立单位撤退。返回受影响数量。
        /// 技能效果直接调用本方法即可，不做任何「可否生效」的前置判定。
        /// </summary>
        public static int RetreatAll(Map map)
        {
            if (map == null)
            {
                return 0;
            }

            List<Pawn> targets = new List<Pawn>();
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (IsRetreatTarget(p))
                {
                    targets.Add(p);
                }
            }

            int started = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (StartRetreat(targets[i]))
                {
                    started++;
                }
            }

            if (started > 0)
            {
                BinguinLogUtility.Log("强制撤退：已对 " + started + " 个单位下撤退令。", isDebug: false);
            }

            return started;
        }

        /// <summary>对所有已生成地图生效。</summary>
        public static int RetreatAllMaps()
        {
            int total = 0;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                total += RetreatAll(maps[i]);
            }
            return total;
        }

        /// <summary>逐帧推进。由 GameComponent_BinguinForcedRetreat 调用。</summary>
        public static void Tick()
        {
            if (active.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                RetreatEntry e = active[i];
                Pawn p = e.pawn;

                if (p == null || p.Dead || !p.Spawned || p.Map != e.map)
                {
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
                    IssueRetreatJob(e, now);

                    // 下发不出去 → 视为被彻底拦截，直接移出地图
                    if (e.jobBlocked)
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
        }

        // ============================================================
        // 目标筛选
        // ============================================================

        private static bool IsRetreatTarget(Pawn p)
        {
            if (p == null || p.Dead || !p.Spawned || p.Destroyed)
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

        private static bool StartRetreat(Pawn p)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].pawn == p)
                {
                    return false;
                }
            }

            IntVec3 exit = FindExitCell(p.Map, p.PositionHeld);
            if (!exit.IsValid)
            {
                return false;
            }

            RetreatEntry e = new RetreatEntry();
            e.pawn = p;
            e.map = p.Map;
            e.target = exit;
            e.startTick = Find.TickManager.TicksGame;
            e.lastIssueTick = -999;
            e.jobBlocked = false;

            active.Add(e);
            IssueRetreatJob(e, e.startTick);
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

        private static void IssueRetreatJob(RetreatEntry e, int now)
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
                e.jobBlocked = !IsRunningRetreatJob(e);
            }
            catch (Exception ex)
            {
                e.jobBlocked = true;
                BinguinLogUtility.WarningOnce(
                    "强制撤退：下发撤退 Job 失败，将改为移出地图。" + ex.Message, 962214);
            }
        }

        /// <summary>
        /// 直接把单位移出地图。
        /// 只经过 Thing / WorldPawns 层，不经过 hediff、精神状态、免疫、抗性等任何判定，
        /// 因此无法被常见防护方式拦截。
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
                    // 完全移出地图：只经过 Thing / WorldPawns 层
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
