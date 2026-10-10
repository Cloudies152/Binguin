// ============================================================================
// 鱼类爱好基因：食用含有鱼类的食物时获得心情记忆 +8。
// 判定：食物本身是 Fish_*（生鱼），或餐食配料（CompIngredients）含 Fish_*。
// ============================================================================

using System;
using RimWorld;
using Verse;

namespace Binguin.Feature.RaceTraits
{
    public class Gene_BinguinFishLover : Gene
    {
        private const string FishThoughtDefName = "Binguin_AteFish";

        public override void Notify_IngestedThing(Thing thing, int numTaken)
        {
            base.Notify_IngestedThing(thing, numTaken);
            if (pawn == null || pawn.needs == null || pawn.needs.mood == null)
            {
                return;
            }
            if (thing == null || !ContainsFish(thing))
            {
                return;
            }
            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(FishThoughtDefName);
            if (thought != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(thought, null);
            }
        }

        private bool ContainsFish(Thing thing)
        {
            if (thing.def == null)
            {
                return false;
            }
            if (thing.def.defName.StartsWith("Fish_", StringComparison.Ordinal))
            {
                return true;
            }
            CompIngredients comp = thing.TryGetComp<CompIngredients>();
            if (comp != null)
            {
                for (int i = 0; i < comp.ingredients.Count; i++)
                {
                    ThingDef ing = comp.ingredients[i];
                    if (ing != null && ing.defName.StartsWith("Fish_", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
