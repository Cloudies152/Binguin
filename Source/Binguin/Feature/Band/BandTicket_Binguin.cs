// ============================================================================
// 冰鹅族【演唱会门票 · Live House 收入】（2026-10-06 用户需求）
//
// 用户需求原文：「帮我想想乐队这块还有什么能设计的东西」→ 选定
//   **A2 演唱会经济**，先做其中最小可用的一块：**门票**。
//
// 设计原则（与用户讨论时定下的调子）：
//   · **收入必须挂在实际到场的人身上** —— 不来人就没钱，玩家有动力把场地
//     建得像样、把地标保护好。
//   · **场地越好单价越高** —— 给"建场地"这件事一个可量化的正反馈
//     （原来建完场地其实没什么回报）。
//   · 数值全部集中在 `BinguinBandTuning`，改数值只改那一处。
//
// 关键设计决策：
//   ① **不能数座椅** —— 原版没有"数座位"的现成 API（`ReservationUtility`
//      那套是给预定用的）。所以场地评分改成"**场地周围能站人的格子数**"，
//      也就是"Live House 的容纳量"。这既避开了缺失的 API，逻辑也更直观：
//      场地越大越讲究 → 站位越多 → 单价越高。
//   ② **在开演瞬间结算**（不是散场）—— 观众已经到场坐好了，票房当场入袋；
//      散场时再算的话，如果玩家中途读档/乐队被清掉就可能漏结算。
//   ③ **银两实物掉落**在场地边（不是凭空加数字）—— 玩家看得见、能捡，
//      和散场掉武器是同一套观感（复用 `GenPlace` + `DropCellFinder`）。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Band
{
    public static class BinguinBandTicket
    {
        // ---- 门票调参（集中在这里，方便一处改动）----

        /// <summary>每位观众的基础票价（银）。</summary>
        public const float BasePricePerVisitor = 15f;

        /// <summary>场地评分的搜索半径（格）。</summary>
        public const int VenueRadius = 14;

        /// <summary>
        /// 场地评分基准容量：到这个数就算"合格场地"（系数 1.0）。
        /// 3×1 装配台级别的小场子大约只有几十格，正经的 Live House 会场
        /// 铺开一片座椅/地板能到 150+ 格。
        /// </summary>
        public const int VenueBaselineCells = 60;

        /// <summary>场地评分对票价的最大加成（0.6 = 最多 +60%）。</summary>
        public const float MaxVenueBonus = 0.6f;

        /// <summary>票价总额上限（防止极端存档里出现天文数字）。</summary>
        public const int MaxRevenue = 20000;

        /// <summary>本次演唱会的票房（开演时写入，供检视/调试查看）。</summary>
        public static int LastRevenue = -1;

        /// <summary>本次演唱会实际到场的观众数（开演时快照）。</summary>
        public static int LastVisitors;

        /// <summary>本次演唱会的场地评分系数（开演时快照）。</summary>
        public static float LastVenueFactor = 1f;

        /// <summary>
        /// 开演时结算门票。返回本次票房（银）。
        /// ★ 由 `BinguinBandConcert.StartConcert` 在 `BinguinConcert.Start` 之后调用。
        /// </summary>
        public static int SettleAtStart(Map map)
        {
            LastRevenue = -1;
            LastVisitors = 0;
            LastVenueFactor = 1f;

            if (map == null)
            {
                return 0;
            }

            // ---- ① 到场人数：优先用真正"到场"的观众（Spawned 且没死）----
            int visitors = CountLiveAudience(map);
            if (visitors <= 0)
            {
                // 没观众就不收门票（不弹空消息）
                BinguinLogUtility.Log("演唱会开演，但没有到场观众 ⇒ 不收门票。");
                return 0;
            }

            // ---- ② 场地评分：场地周围能站人的格子数 ----
            float venueFactor = VenueFactor(map, BinguinBandConcert.venueCenter);

            // ---- ③ 票房 ----
            int revenue = Mathf.RoundToInt(visitors * BasePricePerVisitor * venueFactor);
            if (revenue < 0)
            {
                revenue = 0;
            }
            if (revenue > MaxRevenue)
            {
                revenue = MaxRevenue;
            }

            LastVisitors = visitors;
            LastVenueFactor = venueFactor;
            LastRevenue = revenue;

            // ---- ④ 银两实物掉在场地边 ----
            bool placed = GiveSilver(map, BinguinBandConcert.venueCenter, revenue);

            // ---- ⑤ 通知 + 日志 ----
            if (placed)
            {
                Messages.Message(
                    "Binguin_Ticket_Income".Translate(revenue, visitors,
                        BasePricePerVisitor.ToString("0"), venueFactor.ToString("0.00")).ToString(),
                    MessageTypeDefOf.PositiveEvent, false);
            }
            else
            {
                Messages.Message(
                    "Binguin_Ticket_Income2".Translate(revenue).ToString(),
                    MessageTypeDefOf.PositiveEvent, false);
            }
            BinguinLogUtility.Log("演唱会门票结算：观众 " + visitors + " 人 × 基础 "
                + BasePricePerVisitor.ToString("0") + " 银 × 场地系数 "
                + venueFactor.ToString("0.00") + " = " + revenue
                    + "Binguin_Ticket_Dropped".Translate(placed).ToString());
            return revenue;
        }

        /// <summary>数真正到场的观众（还活着、还在场上）。</summary>
        private static int CountLiveAudience(Map map)
        {
            int n = 0;
            List<Pawn> list = BinguinBandConcert.audience;
            if (list == null)
            {
                return 0;
            }
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p != null && !p.Dead && p.Spawned && p.Map == map)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// 场地评分系数：1.0 ~ (1 + MaxVenueBonus)。
        /// ★ 口径 = "场地周围能站人的格子数"（= Live House 容纳量）。
        ///   不用 `GenGrid.Standable` 而是用 `Walkable` + `!Impassable`：
        ///   观众其实是"站着看"的，草/雪地也能站，只要不是墙就行。
        /// </summary>
        public static float VenueFactor(Map map, IntVec3 center)
        {
            if (map == null || !center.IsValid)
            {
                return 1f;
            }
            IntVec3 c = center;
            if (!c.InBounds(map) || !c.Walkable(map))
            {
                // 场地中心不可站（比如地标所在格被占了）⇒ 往旁边找个能站的格子当中心
                if (!CellFinder.TryFindRandomCellNear(center, map, 6,
                        x => x.Walkable(map), out c))
                {
                    return 1f;
                }
            }

            int standable = 0;
            int r = VenueRadius;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    IntVec3 cell = new IntVec3(c.x + dx, 0, c.z + dz);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }
                    if (cell.Walkable(map) && !cell.Impassable(map))
                    {
                        standable++;
                    }
                }
            }

            float extra = (standable - VenueBaselineCells) / (float)VenueBaselineCells;
            if (extra < 0f)
            {
                extra = 0f;
            }
            if (extra > 1f)
            {
                extra = 1f;
            }
            return 1f + extra * MaxVenueBonus;
        }

        /// <summary>
        /// 把银两实物放在场地边。
        /// ★ 优先 `DropCellFinder.TryFindDropSpotNear`（原版空投同款），
        ///   失败退回"场地中心附近能站的格子"，再失败就放弃（调用方会改口径提示）。
        /// </summary>
        private static bool GiveSilver(Map map, IntVec3 center, int amount)
        {
            if (map == null || amount <= 0)
            {
                return false;
            }
            ThingDef silverDef = ThingDefOf.Silver;
            if (silverDef == null)
            {
                return false;
            }
            Thing silver = ThingMaker.MakeThing(silverDef, null);
            if (silver == null)
            {
                return false;
            }
            silver.stackCount = amount;

            IntVec3 spot = center;
            // ★ 签名（IL 实证）：TryFindDropSpotNear(center, map, out result,
            //   allowFogged, canRoofPunch, allowIndoors, size, mustBeReachableFromCenter)
            //   共 8 个参数 —— 少一个 allowIndoors 会编译不过。
            if (!DropCellFinder.TryFindDropSpotNear(center, map, out spot,
                    false, false, false, null, false))
            {
                if (!CellFinder.TryFindRandomCellNear(center, map, 10,
                        x => x.Walkable(map) && !x.Fogged(map), out spot))
                {
                    spot = center;
                }
            }
            GenPlace.TryPlaceThing(silver, spot, map, ThingPlaceMode.Near, null);
            if (!silver.Spawned)
            {
                // 真放不下 ⇒ 退回原版的"直接给玩家"（不丢钱）
                if (!silver.Destroyed)
                {
                    silver.Destroy(DestroyMode.Vanish);
                }
                Thing silver2 = ThingMaker.MakeThing(silverDef, null);
                if (silver2 != null)
                {
                    silver2.stackCount = amount;
                    GenPlace.TryPlaceThing(silver2, center, map, ThingPlaceMode.Near, null);
                    return silver2.Spawned;
                }
                return false;
            }
            return true;
        }
    }
}
