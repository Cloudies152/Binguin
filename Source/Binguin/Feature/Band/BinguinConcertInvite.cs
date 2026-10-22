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
                // ★★ 2026-10-22 修作者反馈「邀请多个派系，只来了一个」：
                //   贵宾们会带着 `LordJob_BinguinCrowd` 的防守职责，**互有敌意的两拨会在场地里打起来**
                //   （事件观众那边早就有这条检查，自办邀请这里漏了）。
                //   ⇒ 已邀请的派系里有和它敌对的，就不再提供这条邀请。
                if (booking != null && booking.invitedFactionIds != null
                    && booking.invitedFactionIds.Count > 0)
                {
                    bool clash = false;
                    for (int k = 0; k < all.Count; k++)
                    {
                        Faction other = all[k];
                        if (other == null || other == f) continue;
                        if (!booking.invitedFactionIds.Contains(other.loadID)) continue;
                        if (f.HostileTo(other))
                        {
                            clash = true;
                            break;
                        }
                    }
                    if (clash)
                    {
                        BinguinLogUtility.Log("不提供邀请：" + f.Name
                            + " 与已邀请的派系互有敌意，同场会打起来。", severity: 1, isDebug: false);
                        continue;
                    }
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
            List<Faction> alreadySpawned = new List<Faction>();
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
                // ★★ 2026-10-22：互有敌意的派系不能同场 —— 贵宾带的是
                //   `LordJob_BinguinCrowd`（DefendPoint 职责），两拨敌对会在场地里打起来，
                //   结果就是"只来一个派系"。先到的留下，后来的这次就不来了。
                Faction clash = null;
                for (int k = 0; k < alreadySpawned.Count; k++)
                {
                    if (f.HostileTo(alreadySpawned[k]))
                    {
                        clash = alreadySpawned[k];
                        break;
                    }
                }
                if (clash != null)
                {
                    Messages.Message("Binguin_ConcertGuestHostile".Translate(f.Name, clash.Name),
                        MessageTypeDefOf.NegativeEvent);
                    BinguinLogUtility.Log("贵宾名单里 " + f.Name + " 与 " + clash.Name
                        + " 互有敌意，本次跳过（否则会在场地打起来）。", severity: 1, isDebug: false);
                    continue;
                }
                PawnKindDef kind = PickGuestKind(f);
                if (kind == null)
                {
                    continue;
                }
                int countBefore = spawned.Count;
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
                // 诊断用：这个派系实际来了几个人（2026-10-22 排查"只来一个派系"）
                if (spawned.Count > countBefore)
                {
                    alreadySpawned.Add(f);
                }
                BinguinLogUtility.Log("贵宾：" + f.Name + " 到场 " + (spawned.Count - countBefore)
                    + " 人（预约 " + kv.Value + " 人）。", isDebug: false);
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
            // ★★ 2026-10-17 自测发现：「已邀请 太阳帝国 3 人」但**没有“贵宾到场”日志**
            //   ⇒ `f.def.basicMemberKind` 对部分派系为 null，代码里直接 `continue` 静默跳过了。
            //   ⇒ 加三级兜底。
            if (f.def.basicMemberKind != null)
            {
                return f.def.basicMemberKind;
            }
            // ① 从该派系的 pawnGroupMakers 里挑一个人形种族
            try
            {
                if (f.def.pawnGroupMakers != null)
                {
                    for (int i = 0; i < f.def.pawnGroupMakers.Count; i++)
                    {
                        PawnGroupMaker pgm = f.def.pawnGroupMakers[i];
                        if (pgm == null || pgm.options == null) continue;
                        for (int k = 0; k < pgm.options.Count; k++)
                        {
                            PawnKindDef kd = pgm.options[k].kind;
                            if (kd != null && kd.RaceProps != null && kd.RaceProps.Humanlike)
                            {
                                BinguinLogUtility.Log("派系 " + f.Name
                                    + " 没有 basicMemberKind，改用 " + kd.defName,
                                    severity: 1, isDebug: false);
                                return kd;
                            }
                        }
                    }
                }
            }
            catch (System.Exception) { }
            // ② 最后兜底：普通村民
            BinguinLogUtility.Log("派系 " + f.Name
                + " 找不到合适的贵宾模板，回退到 Villager。",
                severity: 1, isDebug: false);
            return PawnKindDefOf.Villager;
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
