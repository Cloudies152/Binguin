// ============================================================================
// 鱼饵 —— 记录制作时投入的食材效果（最多两种），供高级钓竿读取
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Binguin
{
    public class CompProperties_BinguinRodBait : CompProperties
    {
        public CompProperties_BinguinRodBait()
        {
            compClass = typeof(CompBinguinRodBait);
        }
    }

    public class CompBinguinRodBait : ThingComp
    {
        public List<BinguinBaitEffect> effects = new List<BinguinBaitEffect>();

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look<BinguinBaitEffect>(ref effects, "effects", LookMode.Value);
        }

        public override string CompInspectStringExtra()
        {
            if (effects == null || effects.Count == 0)
            {
                return "Binguin_CompRodBait_01".Translate();
            }
            List<string> parts = new List<string>();
            for (int i = 0; i < effects.Count; i++)
            {
                parts.Add(BinguinRodUtility.EffectLabel(effects[i]));
            }
            // ★ 2026-09：去掉 .ToArray()（每次检视少分配一个数组）
            return "Binguin_CompRodBait_02".Translate() + string.Join(" + ", parts);
        }
    }
}
