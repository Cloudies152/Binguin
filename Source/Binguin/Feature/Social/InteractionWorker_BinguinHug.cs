using System.Collections.Generic;
using RimWorld;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Social
{
    /// <summary>
    /// 冰鹅族特有的「拥抱」互动（2026-11-06）。
    ///
    /// 设计：只能由冰鹅族发起、对同阵营的殖民者，随机自动触发（类似原版善良特性的美言）。
    /// 双方各获得 +5 心情（ThoughtDef: Binguin_Hugged）；ThoughtDef 里 stackLimit=1，
    /// 所以同一个人身上最多只有一层，不会叠加。
    /// </summary>
    public class InteractionWorker_BinguinHug : InteractionWorker
    {
        private const string ThoughtDefName = "Binguin_Hugged";

        private static ThoughtDef hugThoughtDef;

        private static ThoughtDef HugThoughtDef
        {
            get
            {
                if (hugThoughtDef == null)
                {
                    hugThoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(ThoughtDefName);
                }
                return hugThoughtDef;
            }
        }

        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (initiator == null || recipient == null || initiator == recipient)
            {
                return 0f;
            }
            if (initiator.Dead || recipient.Dead || initiator.Downed || recipient.Downed)
            {
                return 0f;
            }
            if (!initiator.IsColonist || !recipient.IsColonist)
            {
                return 0f;
            }
            if (!initiator.RaceProps.Humanlike || !recipient.RaceProps.Humanlike)
            {
                return 0f;
            }
            // 只有冰鹅族会主动抱人
            if (!BinguinRaceUtility.IsBinguin(initiator))
            {
                return 0f;
            }
            if (initiator.HostileTo(recipient))
            {
                return 0f;
            }
            // 已经带着「被拥抱」的心情就先不触发（反正也不会叠加）
            if (HasHugThought(initiator) || HasHugThought(recipient))
            {
                return 0f;
            }
            // ★ 2026-11-06 作者要"平均 3 天一次"：原版权重合计约 1.11，
            //   抽中概率 = w/(w+1.11)；每次尝试平均 6600 tick ⇒ w≈0.04 时约 3 天一次。
            return 0.04f;
        }

        private static bool HasHugThought(Pawn pawn)
        {
            ThoughtDef def = HugThoughtDef;
            if (def == null || pawn == null || pawn.needs == null || pawn.needs.mood == null)
            {
                return false;
            }
            List<Thought_Memory> memories = pawn.needs.mood.thoughts.memories.Memories;
            for (int i = 0; i < memories.Count; i++)
            {
                if (memories[i] != null && memories[i].def == def)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
