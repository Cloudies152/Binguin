// ============================================================================
// 冰鹅族【派系】判定（2026-09）
//
// 2026-09 起冰鹅族拆成两个派系：
//   · Binguin        和平派「冰鹅」     —— 中立、派外交官（外交逻辑不变）
//   · BinguinHostile 敌对派「冰鹅·霜牙」—— permanentEnemy（好感恒 -100）、
//                                          袭击概率略低于海盗、可用冰鹅族特有袭击
//
// ★ 注意区分两种 defName，别搞混：
//   · 种族 ThingDef / PawnKindDef 的 defName == "Binguin"  → 那是【种族】，没变
//   · 派系 FactionDef 的 defName                            → 现在有两个
//   本文件只管【派系】。判种族的地方（BinguinBodySwitch、Patch_BinguinPlayerRecruit 等）
//   保持原样，不要换成这里的方法。
// ============================================================================

using RimWorld;
using Verse;

namespace Binguin
{
    public static class BinguinFactions
    {
        public const string Peaceful = "Binguin";
        public const string Hostile = "BinguinHostile";

        /// <summary>是不是冰鹅族任一派系（和平派或敌对派）。</summary>
        public static bool IsBinguinFaction(FactionDef def)
        {
            if (def == null) return false;
            string n = def.defName;
            return n == Peaceful || n == Hostile;
        }

        public static bool IsBinguinFaction(Faction f)
        {
            return f != null && IsBinguinFaction(f.def);
        }

        /// <summary>是不是冰鹅族【敌对派】（组合袭击等的点火条件）。</summary>
        public static bool IsHostile(FactionDef def)
        {
            return def != null && def.defName == Hostile;
        }

        public static bool IsHostile(Faction f)
        {
            return f != null && IsHostile(f.def);
        }

        /// <summary>和平派（派外交官的那一支）。</summary>
        public static bool IsPeaceful(Faction f)
        {
            return f != null && f.def != null && f.def.defName == Peaceful;
        }
    }
}
