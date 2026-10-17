// ============================================================================
// 冰鹅族【自办演唱会】· 通讯台邀请函（2026-10-11 作者需求）
//
// ★★ 作者原话：
//   「给其他派系发邀请函的位置在原版**通讯台**，聊天选项里多个演唱会邀请
//     （**只有开始准备演唱会并且预定时间后才可以发邀请**）」
//
// ★★ 做法：Harmony **postfix 补** `Building_CommsConsole.GetFloatMenuOptions`
//   （`Source/RimWorld/Building_CommsConsole.cs:83`），在聊天选项里追加一条
//   「演唱会邀请（XX）」。**不改原版任何 Def**（符合 Source/XML_DEFS.md 的规范）。
//
// ★★ 只邀"可以邀请的派系"：
//   非敌对（`!HostileTo(player)`）· 有定居点/是真实派系 · 每个派系只邀一次。
//   接受与否按好感度决定（好感高就来，低就婉拒）。
//
// ★★ 接受后：**到开演那天**这些派系的贵宾才会来（本文件只记住名额；
//   开演时由 `BinguinConcertDirector` 生成客人 —— 见 `SpawnInvitedGuests`）。
//
// ★ 本文件不引用任何 Ideology 类型 ⇒ 没装 DLC 也不会崩。
// ============================================================================

using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Binguin.Helper;

namespace Binguin.Feature.Band
{
    /// <summary>通讯台上的「演唱会邀请」。</summary>
    [HarmonyPatch(typeof(Building_CommsConsole))]
    public static class Patch_BinguinConcertInvite
    {
        /// <summary>
        /// 给通讯台的右键菜单追加一条邀请项。
        /// ★ 用 Postfix 追加（不动原版逻辑）。
        /// ★ `__result` 是 `IEnumerable<FloatMenuOption>`；原版可能返回 null 或延迟序列，
        ///   所以先原样放行、再把我们的接在后面。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("GetFloatMenuOptions")]
        public static IEnumerable<FloatMenuOption> Postfix(
            IEnumerable<FloatMenuOption> __result, Building_CommsConsole __instance, Pawn myPawn)
        {
            if (__result != null)
            {
                foreach (FloatMenuOption o in __result)
                {
                    yield return o;
                }
            }
            if (__instance == null || __instance.Map == null)
            {
                yield break;
            }
            // 通讯台本身能不能用（没电/太阳耀斑时原版会自己给拒绝项，
            // 我们只在"能用"的前提下追加，避免和原版拒绝项打架）
            if (!__instance.CanUseCommsNow)
            {
                yield break;
            }

            BinguinConcertBooking booking = GameComponent_BinguinConcertBooking.ActiveBooking;
            // ★ 作者要求：**只有开始准备演唱会、并且预定时间后**才能发邀请
            if (booking == null || !booking.cancelReason.NullOrEmpty())
            {
                yield break;
            }

            foreach (Faction f in InvitableFactions())
            {
                Faction cap = f;
                string label = "Binguin_ConcertInvite_Menu".Translate(cap.Name);
                FloatMenuOption opt = new FloatMenuOption(label, delegate
                {
                    SendInvite(cap, booking);
                });
                opt.tooltip = new TipSignal("Binguin_ConcertInvite_MenuDesc".Translate(cap.Name));
                yield return opt;
            }
        }

        /// <summary>可以邀请的派系（未邀过、非敌对、非玩家、有名字）。</summary>
        private static List<Faction> InvitableFactions()
        {
            List<Faction> result = new List<Faction>();
            Faction player = Faction.OfPlayer;
            if (player == null || Find.FactionManager == null)
            {
                return result;
            }
            BinguinConcertBooking booking = GameComponent_BinguinConcertBooking.ActiveBooking;
            List<Faction> all = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                Faction f = all[i];
                if (f == null || f.IsPlayer || f.def == null)
                {
                    continue;
                }
                if (f.def.hidden || f.Name.NullOrEmpty())
                {
                    continue;
                }
                if (f.HostileTo(player))
                {
                    continue;   // 敌对不来
                }
                if (booking != null && booking.invitedFactionIds != null
                    && booking.invitedFactionIds.Contains(f.loadID))
                {
                    continue;   // 已经邀过
                }
                result.Add(f);
            }
            return result;
        }

        /// <summary>
        /// 发邀请并立刻判定对方来不来（按好感度）。
        /// ★ 好感度 0 = 中立；越高越可能来。敌对的上面已经滤掉了。
        /// </summary>
        private static void SendInvite(Faction f, BinguinConcertBooking booking)
        {
            if (f == null || booking == null)
            {
                return;
            }
            if (booking.invitedFactionIds == null)
            {
                booking.invitedFactionIds = new List<int>();
            }
            if (booking.acceptedGuests == null)
            {
                booking.acceptedGuests = new Dictionary<int, int>();
            }
            booking.invitedFactionIds.Add(f.loadID);

            int goodwill = f.PlayerGoodwill;
            // 好感 0（中立）就有 60% 概率；好感越高越稳
            float chance = 0.6f + goodwill / 200f;
            if (chance > 0.98f)
            {
                chance = 0.98f;
            }
            if (chance < 0.1f)
            {
                chance = 0.1f;
            }

            if (Rand.Value > chance)
            {
                Messages.Message("Binguin_ConcertInvite_Declined".Translate(f.Name),
                    MessageTypeDefOf.NeutralEvent);
                return;
            }

            int guests = Rand.RangeInclusive(2, 5);
            booking.acceptedGuests[f.loadID] = guests;
            Messages.Message("Binguin_ConcertInvite_Accepted".Translate(
                f.Name, guests.ToString()), MessageTypeDefOf.PositiveEvent);
            BinguinLogUtility.Log("已邀请 " + f.Name + " 派人观演（" + guests + " 人）。",
                isDebug: false);
        }
    }

    /// <summary>开演时把"答应来的贵宾"生成到地图上。</summary>
    public static class BinguinConcertGuests
    {
        /// <summary>
        /// 按预约里"已答应的派系"生成客人。由 `BinguinConcertDirector` 开演时调用。
        /// ★ 生成成功后清空 `acceptedGuests`，避免重复生成。
        /// </summary>
        /// <param name="center">生成中心（传场地就直接生在现场；不传就从地图边缘入场）。</param>
        public static List<Pawn> SpawnInvitedGuests(BinguinConcertBooking booking, Map map,
            IntVec3 center = default(IntVec3))
        {
            List<Pawn> spawned = new List<Pawn>();
            if (booking == null || map == null || booking.acceptedGuests == null
                || booking.acceptedGuests.Count == 0)
            {
                return spawned;
            }
            if (Find.FactionManager == null || map.mapPawns == null)
            {
                return spawned;
            }

            IntVec3 entry = FindEntryCell(map);
            List<int> handled = new List<int>();
            foreach (KeyValuePair<int, int> kv in booking.acceptedGuests)
            {
                Faction f = null;
                List<Faction> all = Find.FactionManager.AllFactionsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null && all[i].loadID == kv.Key)
                    {
                        f = all[i];
                        break;
                    }
                }
                handled.Add(kv.Key);
                if (f == null)
                {
                    continue;   // 派系没了（被灭等）—— 跳过
                }
                PawnKindDef kind = PickGuestKind(f);
                if (kind == null)
                {
                    continue;
                }
                for (int n = 0; n < kv.Value; n++)
                {
                    try
                    {
                        Pawn p = PawnGenerator.GeneratePawn(kind, f);
                        if (p == null)
                        {
                            continue;
                        }
                        IntVec3 at = entry;
                        if (center.IsValid)
                        {
                            IntVec3 near = CellFinder.RandomClosewalkCellNear(center, map, 6);
                            if (near.IsValid) at = near;
                        }
                        GenSpawn.Spawn(p, at, map, WipeMode.Vanish);
                        spawned.Add(p);
                    }
                    catch (System.Exception e)
                    {
                        BinguinLogUtility.Log("生成演唱会贵宾失败：" + e.Message,
                            severity: 1, isDebug: false);
                    }
                }
            }
            for (int i = 0; i < handled.Count; i++)
            {
                booking.acceptedGuests.Remove(handled[i]);
            }
            if (spawned.Count > 0)
            {
                BinguinLogUtility.Log("演唱会贵宾到场 " + spawned.Count + " 人。", isDebug: false);
            }
            return spawned;
        }

        /// <summary>挑一个该派系的小人模板（拿不到就返回 null）。</summary>
        private static PawnKindDef PickGuestKind(Faction f)
        {
            if (f == null || f.def == null)
            {
                return null;
            }
            // ★ `FactionDef` 没有 pawnKinds 列表；取"普通成员"模板
            //   （与 `BandVisitFlow_Binguin.cs:389` 同一写法）。
            return f.def.basicMemberKind;
        }

        /// <summary>找一个地图边缘的落点（客人从边缘进场）。</summary>
        private static IntVec3 FindEntryCell(Map map)
        {
            try
            {
                IntVec3 c;
                if (CellFinder.TryFindRandomEdgeCellWith(
                    delegate (IntVec3 x) { return x.Standable(map) && !x.Fogged(map); },
                    map, CellFinder.EdgeRoadChance_Neutral, out c))
                {
                    return c;
                }
            }
            catch (System.Exception)
            {
                // 落点找失败就退回"地图中心附近的随机格"
            }
            return CellFinder.RandomCell(map);
        }
    }
}
