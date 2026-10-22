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

namespace Binguin.Helper
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

        // ★ 2026-10-22 优化：外交组件里 9 处都是
        //   `Find.FactionManager.AllFactions.FirstOrDefault(f => f.def.defName == "Binguin")`，
        //   每次都要分配闭包 + 走 LINQ。这里收成一个方法，用 for 循环（不分配闭包）。
        //
        // ⚠️ 故意**不缓存 Faction 对象**：
        //   读档时 `FactionManager` 会整个重建，静态字段里存的上一个存档的 Faction
        //   引用会变成"过期对象"（loadID 还可能和存档里的新对象撞号），
        //   比每次扫一遍派系表危险得多。派系表通常只有十几项，扫一遍是纳秒级。
        /// <summary>取「和平冰鹅派系」（派外交官那支）；没有就返回 null。</summary>
        public static Faction GetPeaceful()
        {
            FactionManager fm = Find.FactionManager;
            if (fm == null)
            {
                return null;
            }
            System.Collections.Generic.List<Faction> all = fm.AllFactionsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (IsPeaceful(all[i]))
                {
                    return all[i];
                }
            }
            return null;
        }
    }
}
