// ============================================================================
// 装配钓竿 · 【从库存选配件】辅助类（2026-10-06 新增）
//
// 用途：给 `Dialog_BinguinRodStockPick`（一个窗口内选完 4 件配件 + 选人）提供
//       "扫出地图上所有可用配件"和"校验"的逻辑。
//
// ★ 与 `BinguinRodPartPicker`（地图点选版）的关系：
//      **完全独立、互不影响**。地图点选那条路保留原样（用户要求"保留地图点选"），
//      本类是给新窗口用的另一套查询。两边的分类规则必须保持一致：
//      顺序 = 竿身 → 竿头 → 鱼钩 → 鱼饵。
//
// ★ 配件的分类规则（与 `BinguinRodPartPicker.Steps` 完全一致，改一处要改两处）：
//      竿身：Binguin_RodShaft_Long / Binguin_RodShaft_Short
//      竿头：Binguin_RodTip_Blade  / Binguin_RodTip_Hammer
//      鱼钩：Binguin_RodHook_Straight / Binguin_RodHook_Curved
//      鱼饵：Binguin_RodBait（只有一种）
//
// ★ "殖民地里有的配件"的扫描范围（用户 2026-10-06 定的）：
//      **整个地图**上的可搬运物品（`ThingRequestGroup.HaulableEver`），
//      不做储存区/距离过滤 —— 简单直接，散落在外面地上的也能选。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Binguin.Feature.Rods
{
    /// <summary>装配钓竿：配件的分类与查询（新窗口专用，不依赖地图点选那套）。</summary>
    public static class BinguinRodPartStock
    {
        /// <summary>4 个槽位的定义（顺序固定：竿身 → 竿头 → 鱼钩 → 鱼饵）。</summary>
        public class Slot
        {
            /// <summary>槽位显示名的语言键。</summary>
            public string labelKey;
            /// <summary>该槽位允许的 defName（第一个）</summary>
            public string defA;
            /// <summary>该槽位允许的 defName（第二个；null = 该槽位只有一种）</summary>
            public string defB;

            public bool Accepts(ThingDef def)
            {
                if (def == null)
                {
                    return false;
                }
                string dn = def.defName;
                return dn == defA || (defB != null && dn == defB);
            }
        }

        /// <summary>
        /// 4 个槽位。
        /// ★★ 分类规则与 `RodPartPicker_Binguin.Steps` 必须完全一致 ——
        ///    两处都改了才不会出现"地图点选能选、新窗口选不到"这种怪事。
        /// </summary>
        public static readonly Slot[] Slots = new Slot[]
        {
            new Slot { labelKey = "Binguin_DialogRodAssembly_02", defA = "Binguin_RodShaft_Long",   defB = "Binguin_RodShaft_Short" },
            new Slot { labelKey = "Binguin_DialogRodAssembly_03", defA = "Binguin_RodTip_Blade",    defB = "Binguin_RodTip_Hammer" },
            new Slot { labelKey = "Binguin_DialogRodAssembly_04", defA = "Binguin_RodHook_Straight", defB = "Binguin_RodHook_Curved" },
            new Slot { labelKey = "Binguin_DialogRodAssembly_05", defA = "Binguin_RodBait",         defB = null },
        };

        /// <summary>
        /// 扫出地图上所有属于该槽位的配件（可选顺序 = listerThings 的顺序）。
        /// ★ `exclude` 用来排除已经被别的槽位选走的那几件
        ///   （4 件必须是不同的东西，与地图点选版的规则一致）。
        /// </summary>
        public static List<Thing> FindParts(Map map, int slotIndex, IList<Thing> exclude)
        {
            List<Thing> result = new List<Thing>();
            if (map == null || slotIndex < 0 || slotIndex >= Slots.Length)
            {
                return result;
            }
            Slot slot = Slots[slotIndex];
            List<Thing> all = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.def == null || t.Destroyed || !t.Spawned)
                {
                    continue;
                }
                if (!slot.Accepts(t.def))
                {
                    continue;
                }
                if (exclude != null && exclude.Contains(t))
                {
                    continue;
                }
                result.Add(t);
            }
            return result;
        }

        /// <summary>
        /// 地图上该槽位的可用件数（用于"缺哪类"提示）。
        /// </summary>
        public static int CountParts(Map map, int slotIndex)
        {
            return FindParts(map, slotIndex, null).Count;
        }

        /// <summary>
        /// 找出"一件都没有"的槽位名（返回语言键对应的显示名列表）。
        /// ★ 为什么开窗前就要查：老流程是"走到那一步才发现没货"，
        ///   玩家白点前面几次（地图点选版当初就踩过这个坑，见其文件头注释）。
        /// </summary>
        public static List<string> MissingSlotLabels(Map map)
        {
            List<string> missing = new List<string>();
            for (int i = 0; i < Slots.Length; i++)
            {
                if (CountParts(map, i) == 0)
                {
                    missing.Add(Slots[i].labelKey.Translate());
                }
            }
            return missing;
        }

        /// <summary>
        /// 能来装配的殖民者。
        /// ★ 判定与本 mod 其它地方一致（`BinguinRodPartPicker.IsValidWorker`）：
        ///   活着 / 没倒地 / 同一张地图 / 人形 / 自家殖民者。
        /// ★ 故意**不**用 `p.skills == null` 排除 —— 冰鹅族是 HAR 异种人，
        ///   技能系统万一异常会导致"谁都不在列表里"⇒ 玩法直接卡死。
        /// </summary>
        public static List<Pawn> FindWorkers(Thing assemblyTable)
        {
            List<Pawn> result = new List<Pawn>();
            if (assemblyTable == null || assemblyTable.Map == null)
            {
                return result;
            }
            Map map = assemblyTable.Map;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead || p.Downed)
                {
                    continue;
                }
                if (p.def == null || p.def.race == null || !p.def.race.Humanlike)
                {
                    continue;
                }
                if (!p.IsColonist)
                {
                    continue;
                }
                result.Add(p);
            }
            return result;
        }

        /// <summary>配件的显示名：名字 +（材质），方便区分"铁竿身"和"玻璃钢竿身"。</summary>
        public static string PartLabel(Thing t)
        {
            if (t == null)
            {
                return "（未选）";
            }
            if (t.Stuff != null)
            {
                return t.LabelShort + "（" + t.Stuff.label + "）";
            }
            return t.LabelShort;
        }
    }
}
