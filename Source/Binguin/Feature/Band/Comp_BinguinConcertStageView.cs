using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin.Feature.Band
{
    public class CompProperties_BinguinConcertStageView : CompProperties
    {
        public CompProperties_BinguinConcertStageView()
        {
            compClass = typeof(CompBinguinConcertStageView);
        }
    }

    /// <summary>
    /// 演唱会舞台的「现场视角」按钮（2026-11-08）。挂在舞台 Def 上，选中舞台就能开。
    /// </summary>
    public class CompBinguinConcertStageView : ThingComp
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            Command_Action cmd = new Command_Action();
            cmd.defaultLabel = "Binguin_ConcertScene_Open".Translate().ToString();
            cmd.defaultDesc = "Binguin_ConcertScene_OpenDesc".Translate().ToString();
            cmd.icon = ContentFinder<Texture2D>.Get("Things/Building/Joy/Binguin/UI/ConcertStage_MenuIcon", false);
            Thing stage = parent;
            cmd.action = delegate
            {
                Find.WindowStack.Add(new Window_BinguinConcertScene(stage));
            };
            yield return cmd;
        }
    }
}
