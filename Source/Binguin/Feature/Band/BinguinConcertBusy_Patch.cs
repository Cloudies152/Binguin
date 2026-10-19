// ============================================================================
// 冰鹅族【自办演唱会】· 演出期间"人处于忙碌状态"（2026-10-17 作者要求）
//
// ★ 作者原话：
//   「演出中途开别的节日/组件远行队 → 乐手正在忙，这样才行」
//   「如果中途开其他节日就会离开乐器，应该让殖民者在开演唱会的时候处于在忙状态」
//
// ★ 两条防线：
//   ① 演出期间**每 250 tick 重新派一次演奏 job**（见 BinguinConcertDirector
//      .KeepPerformersPlaying）—— 被别的节日抢走后会被强行叫回乐器。
//   ② 本文件：**把演出者从"可加入远行队"的名单里剔除** ——
//      远行队组队时根本选不到他们，从源头上不会被带走。
//      目标：`RimWorld.CaravanFormingUtility.AllSendablePawns(Map, bool, bool,
//             bool, bool, bool, int)` 返回 `List<Pawn>`（1.7 反射确认）。
//
// ★ 只影响"演唱会进行中"，其余时间完全不干预游戏。
// ============================================================================

using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Binguin.Helper;

namespace Binguin.Feature.Band
{
    /// <summary>判断某人此刻是否被演唱会占用。</summary>
    public static class BinguinConcertBusy
    {
        /// <summary>此人是否正在演出/观演（演出期间为 true）。</summary>
        public static bool IsBusy(Pawn p)
        {
            if (p == null) return false;
            if (!BinguinConcert.Active) return false;
            List<Pawn> perf = BinguinConcertDirector.Performers;
            if (perf != null)
            {
                for (int i = 0; i < perf.Count; i++)
                {
                    if (perf[i] == p) return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 演出期间把演出者从远行队可选名单里剔除。
    /// ★ 用 Postfix 改 `__result`（`ref List<Pawn>`），不动原版逻辑。
    /// </summary>
    [HarmonyPatch(typeof(CaravanFormingUtility))]
    public static class Patch_BinguinConcertBlockCaravan
    {
        [HarmonyPostfix]
        [HarmonyPatch("AllSendablePawns")]
        public static void Postfix(ref List<Pawn> __result)
        {
            if (__result == null || __result.Count == 0) return;
            if (!BinguinConcert.Active) return;
            try
            {
                int removed = __result.RemoveAll(
                    delegate (Pawn p) { return BinguinConcertBusy.IsBusy(p); });
                if (removed > 0)
                {
                    BinguinLogUtility.Log("演出期间从远行队名单剔除 " + removed
                        + " 名演出者（他们正忙着演出）。", isDebug: false);
                }
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("剔除演出者出远行队名单失败：" + e.Message,
                    severity: 1, isDebug: false);
            }
        }
    }
}
