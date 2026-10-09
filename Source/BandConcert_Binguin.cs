// ============================================================================
// 冰鹅族【演唱会】状态 + 演唱会期间的袭击处理（代码批 2a，2026-09）
//
// 用户定稿：
//   · 演唱会时长 = 【4 小时】（1 小时 = 2500 tick → 10000 tick）
//   · 演唱会进行期间【不触发袭击】
//   · 中途来袭击 → 【两层处理】（用户说的"或者"我按两层都做，更稳）：
//       第一层：袭击事件直接拦下来，延后 8 小时再发生
//       第二层：万一还是打起来了（例如演唱会开始前就在路上的），
//               让敌人当场撤退，并弹"敌人被音乐所打动，决定撤退"
//
// ★ 拦截点选 IncidentWorker_RaidEnemy.TryExecuteWorker，用 Priority.First
//   保证排在组合袭击那条补丁之前（否则先跑组合袭击就白拦了）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace Binguin
{
    /// <summary>演唱会运行期状态（存档在 GameComponent 里）。</summary>
    public static class BinguinConcert
    {
        /// <summary>1 小时 = 2500 tick。</summary>
        public const int TicksPerHour = 2500;
        /// <summary>演唱会时长：4 小时。</summary>
        public const int DurationTicks = 4 * TicksPerHour;
        /// <summary>袭击被拦下后延后多久：8 小时。</summary>
        public const int RaidDelayTicks = 8 * TicksPerHour;

        public static int EndTick = -1;

        public static bool Active
        {
            get { return EndTick > 0 && GenTicks.TicksGame < EndTick; }
        }

        public static void Start(int ticks)
        {
            EndTick = GenTicks.TicksGame + ticks;
            Log.Message("[冰鹅族] 演唱会开始，持续 " + (ticks / (float)TicksPerHour).ToString("0.#") + " 小时。");
        }

        public static void Stop()
        {
            EndTick = -1;
        }
    }

    /// <summary>演唱会期间把袭击拦下来、延后 8 小时。</summary>
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    public static class Patch_ConcertBlockRaid
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(IncidentWorker __instance, IncidentParms parms)
        {
            try
            {
                if (!BinguinConcert.Active) return true;
                if (parms == null || parms.faction == null) return true;
                Faction player = Faction.OfPlayer;
                if (player == null || !parms.faction.HostileTo(player)) return true;

                // 延后 8 小时再打
                if (Find.Storyteller != null && Find.Storyteller.incidentQueue != null && __instance != null)
                {
                    Find.Storyteller.incidentQueue.Add(__instance.def,
                        GenTicks.TicksGame + BinguinConcert.RaidDelayTicks, parms);
                }
                Messages.Message("Binguin_Concert_EnemyWatching".Translate(),
                    MessageTypeDefOf.NeutralEvent);
                Log.Message("[冰鹅族] 演唱会期间拦下一场袭击，延后 8 小时。");
                return false;   // 本次不执行
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 演唱会拦袭击失败（放行）：" + e.Message);
                return true;
            }
        }
    }

    /// <summary>演唱会期间已经打起来的敌人 → 当场劝退。</summary>
    public static class BinguinConcertRetreat
    {
        /// <summary>让地图上所有敌对小人撤离地图。</summary>
        public static int MakeRaidersRetreat(Map map, bool showMessage)
        {
            if (map == null) return 0;
            Faction player = Faction.OfPlayer;
            if (player == null) return 0;

            int n = 0;
            // 1.6 里 AllPawnsSpawned 是 IReadOnlyList<Pawn>
            var all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p == null || p.Dead || p.Downed) continue;
                if (!p.HostileTo(player)) continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) continue;

                IntVec3 exit;
                if (!RCellFinder.TryFindBestExitSpot(p, out exit))
                {
                    continue;
                }
                try
                {
                    p.jobs.StopAll();
                    p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, exit),
                        JobCondition.InterruptForced);
                    n++;
                }
                catch (Exception)
                {
                    // 单个小人失败不影响其它人
                }
            }

            if (showMessage && n > 0)
            {
                Messages.Message("Binguin_Concert_EnemyMoved".Translate(), MessageTypeDefOf.PositiveEvent);
                Log.Message("[冰鹅族] 演唱会劝退敌人 " + n + " 名。");
            }
            return n;
        }
    }

    /// <summary>把演唱会状态存进存档（放在已有的乐队组件里一起存）。</summary>
    public class GameComponent_BinguinConcertState : GameComponent
    {
        public GameComponent_BinguinConcertState(Game game) : base()
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref BinguinConcert.EndTick, "binguinConcertEndTick", -1, false);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager == null) return;
            // 演唱会期间定期劝退已经打起来的敌人（每 250 tick 扫一次，够快也不费）
            if (Find.TickManager.TicksGame % 250 != 0) return;
            if (!BinguinConcert.Active) return;
            try
            {
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    BinguinConcertRetreat.MakeRaidersRetreat(maps[i], true);
                }
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 演唱会劝退敌人失败：" + e.Message);
            }
        }
    }

    /// <summary>调试命令：手动开关演唱会（用于测试袭击拦截/劝退）。</summary>
    public static class DebugActions_Concert
    {
        [DebugAction("冰鹅族", "演唱会：立刻开始（4 小时）")]
        public static void StartNow()
        {
            BinguinConcert.Start(BinguinConcert.DurationTicks);
            Messages.Message("（调试）演唱会开始，持续 4 小时。",
                MessageTypeDefOf.TaskCompletion);
        }

        [DebugAction("冰鹅族", "演唱会：立刻结束")]
        public static void StopNow()
        {
            BinguinConcert.Stop();
            Messages.Message("（调试）演唱会结束。", MessageTypeDefOf.TaskCompletion);
        }

        [DebugAction("冰鹅族", "演唱会：马上劝退场上敌人")]
        public static void RetreatNow()
        {
            int n = BinguinConcertRetreat.MakeRaidersRetreat(Find.CurrentMap, true);
            Log.Message("[冰鹅族] （调试）劝退 " + n + " 名敌人。");
        }
    }
}
