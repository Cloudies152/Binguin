using System;
using Binguin.Helper;
using Verse;

namespace Binguin.Feature.Appearance
{
    // 1.7 的 ParallelPreDraw 会并行进入 HAR/外观及物品图形缓存。
    // 仅本模组实体提前在主线程完成同一阶段；不禁用全局并行绘制、不吞掉异常。
    public static class Patch_BinguinMainThreadPreDraw
    {
        private static bool Owns(Thing thing)
        {
            Pawn pawn = thing as Pawn;
            if (pawn != null) return BinguinRaceUtility.IsBinguin(pawn);
            return thing != null && thing.def != null && thing.def.modContentPack != null
                && string.Equals(thing.def.modContentPack.PackageIdPlayerFacing, "binguin.race",
                    StringComparison.OrdinalIgnoreCase);
        }

        public static bool Prefix(Thing __instance, DrawPhase phase)
        {
            return phase != DrawPhase.ParallelPreDraw || UnityData.IsInMainThread || !Owns(__instance);
        }

        public static void Postfix(Thing __instance, DrawPhase phase)
        {
            if (phase == DrawPhase.EnsureInitialized && UnityData.IsInMainThread && Owns(__instance))
                __instance.DynamicDrawPhase(DrawPhase.ParallelPreDraw);
        }
    }
}
