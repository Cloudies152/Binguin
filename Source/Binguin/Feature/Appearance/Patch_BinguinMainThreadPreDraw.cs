using System;
using Binguin.Helper;
using Binguin.Core;
using Verse;

namespace Binguin.Feature.Appearance
{
    // 1.7 的 ParallelPreDraw 会并行进入 HAR/外观及物品图形缓存。
    // 仅本模组实体提前在主线程完成同一阶段；不禁用全局并行绘制、不吞掉异常。
    // ★ 2026-10-22：默认关闭（Mod 选项里可开）。这个补丁是没确认根因的兼容处理，
    //   而它 hook 的是全局绘制入口；一旦和第三方绘制补丁锁顺序冲突就是画面卡死。
    public static class Patch_BinguinMainThreadPreDraw
    {
        private static bool Enabled
        {
            get
            {
                BinguinSettings s = BinguinMod.Settings;
                return s != null && s.enableMainThreadPreDraw;
            }
        }

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
            if (!Enabled) return true;
            return phase != DrawPhase.ParallelPreDraw || UnityData.IsInMainThread || !Owns(__instance);
        }

        public static void Postfix(Thing __instance, DrawPhase phase)
        {
            if (!Enabled) return;
            if (phase == DrawPhase.EnsureInitialized && UnityData.IsInMainThread && Owns(__instance))
                __instance.DynamicDrawPhase(DrawPhase.ParallelPreDraw);
        }
    }
}
