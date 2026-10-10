// ============================================================================
// 冰鹅族【组合型袭击】（2026-09 用户需求）
//
// 需求原文：
//   · 可能出现「中心空投 + 破墙 + 围攻 + 工兵 + 直接进攻」里【选 2 种】的组合袭击
//   · 只有袭击点数 ≥ 6000 才会触发
//   · 点数算法 = 两个策略的点数倍率【取平均】，然后【各分一半】
//     例：10000 点，中心空投 ×0.3、破墙 ×0.5 → 平均 ×0.4 → 共 4000 → 各 2000
//
// ── 实现方式（为什么这么做）────────────────────────────────────────────────
// 原版一次袭击只有一个 RaidStrategyDef、一个到达方式、一个 Lord。
// 硬把两套战术塞进一个 Lord 会跟低温围攻那套 LordJob 打架，所以改成
// 【把同一次袭击执行两遍】，每遍只给一半点数、各自指定策略与到达方式：
//
//   1) prefix  IncidentWorker_RaidEnemy.TryExecuteWorker
//        · 判定是否点火（敌对冰鹅派系 + 点数≥6000 + 概率）
//        · 抽 2 个互不相同的策略
//        · 用原版 AdjustedRaidPoints 各算一次"单打时的最终点数" effA / effB
//          → 每半份 = (effA + effB) / 4
//            （= 先取平均 (effA+effB)/2，再对半分）
//        · 用反射把原方法跑两遍（第一遍发信、第二遍 silent 静默）
//   2) prefix  IncidentWorker_Raid.AdjustedRaidPoints → 强制返回算好的半份点数
//   3) prefix  IncidentWorker_RaidEnemy.ResolveRaidStrategy → 强制用指定策略
//
//   ★ 到达方式不用管：RaidComposition_Binguin.Patch_BinguinRaidArrival 已经
//     按策略名强制了（DropCenter→CenterDrop / DropScattered→RandomDrop / 其余→EdgeWalkIn），
//     这里算 eff 时用的是同一套映射，所以算出来的和实际跑的一致。
//   ★ AdjustedRaidPoints 上已经有一条本 mod 的 prefix 在记录 RawPoints，
//     我们这条用 Priority.Low 保证后跑，好把 RawPoints 改写成"半份点数"，
//     这样领袖/护卫的点数门槛按半份算，不会出现 2000 点的半场冒出 8000 点的领袖。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Raids
{
    [StaticConstructorOnStartup]
    public static class BinguinCombinedRaid
    {
        // ---------------- 可调参数（用户没指定概率，这里给个默认值，想改就改这几行） ----------------
        /// <summary>触发组合袭击的最低【策略计算前】点数。</summary>
        public const float MinPoints = 6000f;
        /// <summary>满足点数条件后，真的变成组合袭击的概率。</summary>
        public const float Chance = 0.35f;

        /// <summary>参与组合的 5 种袭击（用户点名的名单）。</summary>
        private static readonly string[] StrategyPool =
        {
            "Binguin_DropCenter",          // 中心空投
            "ImmediateAttackBreaching",    // 破墙
            "Siege",                       // 围攻（原版迫击炮）
            "ImmediateAttackSappers",      // 工兵
            "ImmediateAttack",             // 直接进攻
        };

        private static readonly string[] StrategyLabels =
        {
            "中心空投", "破墙", "围攻", "工兵", "直接进攻",
        };

        // ---------------- 运行期状态（主线程同步，静态字段足够） ----------------
        private static bool inProgress;
        private static RaidStrategyDef forcedStrategy;
        private static float forcedAdjustedPoints = -1f;

        private static readonly MethodInfo TryExecuteWorkerMethod =
            AccessTools.DeclaredMethod(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker");
        private static readonly MethodInfo AdjustedRaidPointsMethod =
            AccessTools.Method(typeof(IncidentWorker_Raid), "AdjustedRaidPoints");

        static BinguinCombinedRaid()
        {
            try
            {
                Harmony harmony = new Harmony("zengbing.binguin.combinedraid");
                int ok = 0;

                if (TryExecuteWorkerMethod != null)
                {
                    harmony.Patch(TryExecuteWorkerMethod, new HarmonyMethod(
                        typeof(BinguinCombinedRaid).GetMethod("TryExecuteWorkerPrefix",
                            BindingFlags.Static | BindingFlags.NonPublic)), null);
                    ok++;
                }
                else
                {
                    BinguinLogUtility.Log("组合袭击：没找到 IncidentWorker_RaidEnemy.TryExecuteWorker", severity: 1, isDebug: false);
                }

                MethodInfo resolveStrategy = AccessTools.Method(typeof(IncidentWorker_RaidEnemy),
                    "ResolveRaidStrategy", new Type[] { typeof(IncidentParms), typeof(PawnGroupKindDef) });
                if (resolveStrategy != null)
                {
                    harmony.Patch(resolveStrategy, new HarmonyMethod(
                        typeof(BinguinCombinedRaid).GetMethod("ResolveRaidStrategyPrefix",
                            BindingFlags.Static | BindingFlags.NonPublic)), null);
                    ok++;
                }

                if (AdjustedRaidPointsMethod != null)
                {
                    HarmonyMethod hm = new HarmonyMethod(
                        typeof(BinguinCombinedRaid).GetMethod("AdjustedRaidPointsPrefix",
                            BindingFlags.Static | BindingFlags.NonPublic));
                    hm.priority = Priority.Low;   // 必须晚于本 mod 记录 RawPoints 的那条
                    harmony.Patch(AdjustedRaidPointsMethod, hm, null);
                    ok++;
                }

                BinguinLogUtility.Log("组合袭击补丁已挂载 " + ok + "/3（点数 ≥ " + MinPoints
                    + " 的敌对冰鹅袭击有 " + (Chance * 100f).ToString("0") + "% 概率变成双策略组合）。");
            }
            catch (Exception ex)
            {
                BinguinLogUtility.Log("组合袭击补丁挂载失败（不影响其它功能）：" + ex, severity: 2, isDebug: false);
            }
        }

        // ==================== 判定 ====================

        private static bool Eligible(IncidentParms parms)
        {
            if (parms == null) return false;
            if (parms.faction == null || !BinguinFactions.IsHostile(parms.faction)) return false;
            if (parms.points < MinPoints) return false;
            if (parms.raidStrategy != null) return false;      // 剧本/任务已指定策略 → 不插手
            if (parms.silent) return false;
            if (parms.customLetterLabel != null) return false; // 已被别人定制过
            if (!(parms.target is Map)) return false;
            return true;
        }

        /// <summary>和 Patch_BinguinRaidArrival 里那套映射保持一致。</summary>
        private static PawnsArrivalModeDef ArrivalFor(RaidStrategyDef st)
        {
            if (st == null) return PawnsArrivalModeDefOf.EdgeWalkIn;
            if (st.defName == "Binguin_DropCenter") return PawnsArrivalModeDefOf.CenterDrop;
            if (st.defName == "Binguin_DropScattered") return PawnsArrivalModeDefOf.RandomDrop;
            return PawnsArrivalModeDefOf.EdgeWalkIn;
        }

        /// <summary>某个策略"单打"时的最终点数（把原版那串曲线跑一遍）。</summary>
        private static float EffectivePoints(IncidentWorker worker, IncidentParms parms,
            RaidStrategyDef st, out int poolIndex)
        {
            poolIndex = IndexOf(st != null ? st.defName : null);
            if (AdjustedRaidPointsMethod == null || worker == null)
            {
                return parms.points;
            }
            object[] args = new object[]
            {
                parms.points,
                ArrivalFor(st),
                st,
                parms.faction,
                parms.pawnGroupKind,
                parms.target,
                parms.raidAgeRestriction,
            };
            return (float)AdjustedRaidPointsMethod.Invoke(worker, args);
        }

        private static int IndexOf(string defName)
        {
            if (defName == null) return -1;
            for (int i = 0; i < StrategyPool.Length; i++)
            {
                if (StrategyPool[i] == defName) return i;
            }
            return -1;
        }

        private static RaidStrategyDef DefAt(int idx)
        {
            if (idx < 0 || idx >= StrategyPool.Length) return null;
            return DefDatabase<RaidStrategyDef>.GetNamedSilentFail(StrategyPool[idx]);
        }

        // ==================== 主流程 ====================

        private static bool TryExecuteWorkerPrefix(IncidentWorker_RaidEnemy __instance,
            IncidentParms parms, ref bool __result)
        {
            if (inProgress) return true;          // 第二遍（以及任何重入）走原版
            try
            {
                if (!Eligible(parms)) return true;
                if (!Rand.Chance(Chance)) return true;
                if (TryExecuteWorkerMethod == null) return true;
                if (parms.pawnGroupKind == null) return true;

                // ---- 抽两个互不相同的策略 ----
                int ia = Rand.Range(0, StrategyPool.Length);
                int ib = Rand.Range(0, StrategyPool.Length - 1);
                if (ib >= ia) ib++;
                RaidStrategyDef stA = DefAt(ia);
                RaidStrategyDef stB = DefAt(ib);
                if (stA == null || stB == null) return true;

                // ---- 点数：先各算单打值，取平均再对半分 ----
                int dummy;
                float effA = EffectivePoints(__instance, parms, stA, out dummy);
                float effB = EffectivePoints(__instance, parms, stB, out dummy);
                float half = (effA + effB) / 4f;
                if (half < 1f) return true;

                float rawPoints = parms.points;

                BinguinLogUtility.Log("组合袭击触发：" + stA.defName + " + " + stB.defName
                    + "（原始点数 " + rawPoints.ToString("0") + "，单打 " + effA.ToString("0")
                    + " / " + effB.ToString("0") + "，各分 " + half.ToString("0") + "）");

                Messages.Message("Binguin_CombinedRaid".Translate(
                        StrategyLabels[ia], StrategyLabels[ib]).ToString(),
                    MessageTypeDefOf.ThreatBig, false);

                inProgress = true;
                try
                {
                    RunHalf(__instance, parms, stA, rawPoints, half, false);
                    RunHalf(__instance, parms, stB, rawPoints, half, true);
                }
                finally
                {
                    inProgress = false;
                    forcedStrategy = null;
                    forcedAdjustedPoints = -1f;
                }

                __result = true;
                return false;   // 原方法已被我们跑过两遍，跳过
            }
            catch (Exception e)
            {
                inProgress = false;
                forcedStrategy = null;
                forcedAdjustedPoints = -1f;
                BinguinLogUtility.Log("组合袭击执行失败，已回退成普通袭击：" + e, severity: 2, isDebug: false);
                return true;
            }
        }

        /// <summary>跑一遍原版流程，指定策略与点数。</summary>
        private static void RunHalf(IncidentWorker_RaidEnemy worker, IncidentParms parms,
            RaidStrategyDef st, float rawPoints, float points, bool silent)
        {
            // 复位会被原方法改写的字段（同一个 parms 复用两次）
            parms.points = rawPoints;
            parms.raidStrategy = null;        // 由 ResolveRaidStrategyPrefix 强制
            parms.raidArrivalMode = null;     // 交回 Patch_BinguinRaidArrival 强制
            parms.silent = silent;            // 第二遍不发信

            forcedStrategy = st;
            forcedAdjustedPoints = points;

            TryExecuteWorkerMethod.Invoke(worker, new object[] { parms });

            forcedStrategy = null;
            forcedAdjustedPoints = -1f;
        }

        // ==================== 两条强制补丁 ====================

        private static bool ResolveRaidStrategyPrefix(IncidentParms parms)
        {
            if (forcedStrategy == null || parms == null) return true;
            parms.raidStrategy = forcedStrategy;
            return false;   // 跳过原版抽签
        }

        private static bool AdjustedRaidPointsPrefix(ref float __result)
        {
            if (forcedAdjustedPoints < 0f) return true;
            __result = forcedAdjustedPoints;
            // 让"点数门槛"（哨骑/护卫/领袖）按半份点数算
            BinguinRaidContext.RawPoints = forcedAdjustedPoints;
            return false;
        }
    }
}
