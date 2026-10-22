// ============================================================================
// 冰鹅族【自办演唱会】· 演出总控（2026-10-11）
//
// ★★ 职责（就这一件事，别的都交给已有代码）：
//     开演时把三种 job 派下去 —— 其余全部复用现有实现：
//       · 乐手  → `Binguin_PlayInBand`      （JobDriver_BinguinPlayInstrument，已有）
//       · 主唱  → `Binguin_PlayInBand`（麦克风）
//       · 观众  → `Binguin_WatchConcert`    （JobDriver_BinguinWatchConcert，已有）
//     收尾由**已有**的 `GameComponent_BinguinBandConcertEnd` 负责
//     （它检测 `BinguinConcert.Active` 由 true 变 false 就调用 `EndConcert`），
//     所以这里**不重复实现收尾**。
//
// ★★ 为什么派 job 而不是用 Lord：
//     我们的行为是**明确指派某个人去某件乐器**，用 job 最直接可控。
//     （`LordJob_BinguinCrowd` 那套是给"外团从地图边缘走进来"用的。）
//
// ★★ 观众范围：一期 = 地图上所有**能自由行动**的殖民者
//     （作者没要求手选观众；"乐手可以缺人"是明确要求）。
//
// ★★★ 2026-10-18 观众观看（本轮新增，之前**根本没有**）：
//   · 观众 job 的 targetA = 座位/站位，**targetB = 麦克风**（用来面朝舞台）。
//   · `Binguin_WatchConcert` 的驱动原来只会 `GotoThing`，而这里传的是**格子**
//     ⇒ 目标没有 Thing ⇒ 初始化 NRE ⇒ 全场没有一个人在看演出。
//     驱动已改成格子/物件分流（见 BandWatch_Binguin.cs）。
//   · 演出期间每 250 tick 由 `KeepAudienceWatching` 补派一次（已在看的不打断），
//     所以中途被别的活抢走也会自己走回来。
//   · 散场时由 `EndPerformance` 只给**正在看的人**发心情（原来这条是空的，
//     靠"乐队来访"那套 `EndConcert` 兜底，而它还会掉武器 + 给冰鹅派系 +20，
//     对自办演出是错的 ⇒ 现在那套只在来访事件 `bandArrived` 时跑）。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;
using Verse.AI;
using Binguin.Helper;

namespace Binguin.Feature.Band
{
    /// <summary>自办演出的开演入口 + 演出期间的轻量维护。</summary>
    public static class BinguinConcertDirector
    {
        /// <summary>本场演出的观众（用于收尾时"只给到场的人发心情"）。</summary>
        public static List<Pawn> Audience { get { return GameComponent_BinguinPerformanceState.Current.audience; } set { GameComponent_BinguinPerformanceState.Current.audience = value; } }

        /// <summary>本场演出的乐手（含主唱）。</summary>
        public static List<Pawn> Performers { get { return GameComponent_BinguinPerformanceState.Current.performers; } set { GameComponent_BinguinPerformanceState.Current.performers = value; } }

        /// <summary>本场是不是玩家自办的（区别于外团来访那套）。</summary>
        public static bool SelfHosted { get { return GameComponent_BinguinPerformanceState.Current.selfHosted; } set { GameComponent_BinguinPerformanceState.Current.selfHosted = value; } }

        /// <summary>
        /// 开演。由 `GameComponent_BinguinConcertBooking.StartNow()` 调用。
        /// ★ 不抛异常：任何一个环节出问题都只记日志，避免卡住存档 tick。
        /// </summary>
        public static void BeginPerformance(BinguinConcertBooking b, Map map)
        {
            try
            {
                if (b == null || map == null)
                {
                    return;
                }
                SelfHosted = true;
                Audience = new List<Pawn>();
                Performers = new List<Pawn>();
                ResetSeats();

                // ① 主唱 → 麦克风（必须传**麦克风建筑**，不能传格子！）
                //   ★★ 2026-10-17 修玩家日志里的致命红字：
                //     `JobDriver threw exception in toil GotoThing's initAction for
                //      driver=JobDriver_BinguinPlayInstrument -> NullReferenceException`
                //   原因：`JobDriver_BinguinPlayInstrument` 的 toil 是
                //     `Toils_Goto.GotoThing(TargetIndex.A, ...)` —— 它要求 A 是 **Thing**。
                //     我之前对主唱传的是 `LocalTargetInfo(IntVec3)`（只有格子），
                //     `targetA.Thing` 为 null ⇒ GotoThing 的 initAction 直接 NRE
                //     ⇒ 主唱永远走不到麦克风、演出异常。
                //   ⇒ 改成按 defName 找**麦克风的 Building 实例**再派 job。
                IntVec3 micCell = b.venueCenter.IsValid ? b.venueCenter : b.leadSinger.Position;
                Thing mic = FindNearestInstrument(map, Dialog_BinguinConcertSetup.MicDefName, micCell);
                if (mic != null)
                {
                    SendPlayJob(b.leadSinger, mic);
                }
                else
                {
                    // 兜底：真找不到麦克风时至少让他走过去（用 Goto，不碰 PlayInBand）
                    BinguinLogUtility.Log("开演时找不到麦克风建筑，主唱只走到场地中心。",
                        severity: 1, isDebug: false);
                    SendGoto(b.leadSinger, micCell);
                }
                Performers.Add(b.leadSinger);

                // ② 乐手 → 各自乐器
                //    `musicians` 的 key 是乐器 **defName**；按 defName 在地图上找最近的实例。
                if (b.musicians != null)
                {
                    foreach (KeyValuePair<string, Pawn> kv in b.musicians)
                    {
                        Pawn p = kv.Value;
                        if (p == null || p.Dead || !p.Spawned)
                        {
                            continue;   // 缺人就跳过（作者定的"乐手可缺人"）
                        }
                        Thing inst = FindNearestInstrument(map, kv.Key, b.venueCenter);
                        if (inst == null)
                        {
                            continue;   // 乐器被拆了也不阻断（作者定：只有主唱死了才取消）
                        }
                        SendPlayJob(p, inst);
                        Performers.Add(p);
                    }
                }

                // ③.5 ★ 2026-10-17：给**演出者**建 Lord
                //   原版 `RitualRoleAssignments.cs:600` 会把“处于 Lord 中、且 LordJob 不是
                //   LordJob_VoluntarilyJoinable”的小人判为 busy，返回
                //   `MessageRitualRoleBusy`（"{0} is busy."），根本选不进另一个仪式。
                //   ⇒ 不靠抢 job（会互相拉扯），而是让他们“很忙”。
                GivePerformersLord(Performers, map, b.venueCenter);

                // ③ 观众 → 看演出（殖民者 + 已经到场的贵宾）
                //    ★ 2026-10-18：名单统一由 CollectAudience 收集
                //      （排除演出者 / 倒地 / 精神崩溃 / 不在本图），
                //      并且 job 带上 targetB = 麦克风 ⇒ 观众才会**面朝舞台**看。
                List<Pawn> audience = CollectAudience(b, map);
                for (int i = 0; i < audience.Count; i++)
                {
                    SendWatchJob(audience[i], b.venueCenter, map, mic);
                }
                Audience = audience;

                // ④ 开演这一刻才生成的新贵宾（通常到场阶段已经生成过，这里是兜底）
                List<Pawn> guests = BinguinConcertGuests.SpawnInvitedGuests(b, map);
                if (guests != null && guests.Count > 0)
                {
                    if (b.guests == null) b.guests = new List<Pawn>();
                    for (int i = 0; i < guests.Count; i++)
                    {
                        Pawn g = guests[i];
                        if (g == null) continue;
                        SendWatchJob(g, b.venueCenter, map, mic);
                        b.guests.Add(g);
                        Audience.Add(g);
                    }
                    // 开演才生成的贵宾也要有 Lord，否则看完了会直接往外走
                    GiveGuestsLord(guests, map, b.venueCenter);
                }

                BinguinLogUtility.Log("自办演唱会开演：主唱 1 人、乐手 "
                    + (Performers.Count - 1) + " 人、观众 " + Audience.Count + " 人。",
                    isDebug: false);
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("开演时出错（已忽略，演出状态可能不完整）：" + e,
                    severity: 1, isDebug: false);
            }
        }

        /// <summary>把"演奏"job 派给某人；目标可以是乐器（Thing）或一个格子（麦克风位）。</summary>
        private static void SendPlayJob(Pawn p, Thing instrument)
        {
            if (p == null || instrument == null)
            {
                return;
            }
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_PlayInBand");
            if (def == null)
            {
                BinguinLogUtility.Log("找不到 JobDef Binguin_PlayInBand，乐手未派活。",
                    severity: 1, isDebug: false);
                return;
            }
            Job job = JobMaker.MakeJob(def, instrument);
            p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }


        /// <summary>把贵宾交给一个 Lord，否则他们会立刻离开地图。
        /// ★ 2026-10-17 作者反馈：「邀请访客后，演唱会开始时访客来时会直接离开地图」
        ///   ⇒ 范例参考模组已有的冰鹅族演唱会事件（BandVisitFlow_Binguin.cs:488）：
        ///     LordMaker.MakeNewLord(faction, new LordJob_BinguinCrowd(chillSpot), map, pawns)
        /// </summary>
        public static void GiveGuestsLord(List<Pawn> guests, Map map, IntVec3 venue)
        {
            if (guests == null || guests.Count == 0 || map == null) return;
            try
            {
                Dictionary<Faction, List<Pawn>> byFaction = new Dictionary<Faction, List<Pawn>>();
                for (int i = 0; i < guests.Count; i++)
                {
                    Pawn p = guests[i];
                    if (p == null || p.Dead || !p.Spawned) continue;
                    Faction f = p.Faction;
                    if (f == null) continue;
                    List<Pawn> list;
                    if (!byFaction.TryGetValue(f, out list))
                    {
                        list = new List<Pawn>();
                        byFaction[f] = list;
                    }
                    list.Add(p);
                }
                // ★ 2026-10-17：Lord 的 chillSpot 放在**舞台正前方**，
                //   让客人聚在台前听，而不是围着麦克风转（作者反馈）。
                // ★ 2026-10-18：方向不能再写死南侧（那是歌手背后）——
                //   按「主唱站位 → 麦克风」推出来的 StageFrontDir 走。
                Thing mic = FindNearestInstrument(map, Dialog_BinguinConcertSetup.MicDefName, venue);
                IntVec3 front = venue + StageFrontDir(mic) * 4;
                if (!front.Standable(map)) front = venue;
                foreach (KeyValuePair<Faction, List<Pawn>> kv in byFaction)
                {
                    Verse.AI.Group.LordMaker.MakeNewLord(kv.Key,
                        new LordJob_BinguinCrowd(front), map, kv.Value);
                }
                BinguinLogUtility.Log("已为贵宾建立 Lord（防止立刻离开地图）。",
                    isDebug: false);
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("为贵宾建 Lord 失败：" + e.Message, severity: 1, isDebug: false);
            }
        }

        /// <summary>
        /// 演唱会结束后给**来访的派系**加好感（作者要求：听完后和所有派系加关系）。
        /// ★ 只给真正被邀请且答应的派系加；敌对派系不动。
        /// </summary>
        public static void GrantGoodwill(BinguinConcertBooking b)
        {
            if (b == null || b.invitedFactionIds == null || b.invitedFactionIds.Count == 0) return;
            if (Find.FactionManager == null) return;
            Faction player = Faction.OfPlayer;
            if (player == null) return;
            List<Faction> all = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                Faction f = all[i];
                if (f == null || f.def == null || f.IsPlayer) continue;
                if (!b.invitedFactionIds.Contains(f.loadID)) continue;
                if (f.HostileTo(player)) continue;
                try
                {
                    // ★ 2026-10-18：好感改成随质量变化的曲线（+4 ~ +12，质量 0.5 = 原来的 +8）
                    int amount = BinguinConcertQuality.GoodwillOf(b.quality);
                    if (amount <= 0) continue;
                    f.TryAffectGoodwillWith(player, amount, true, false);
                    Messages.Message("Binguin_ConcertGoodwill_Message".Translate(
                            f.Name, amount.ToString()),
                        MessageTypeDefOf.PositiveEvent);
                }
                catch (System.Exception e)
                {
                    BinguinLogUtility.Log("给 " + f.Name + " 加好感失败：" + e.Message,
                        severity: 1, isDebug: false);
                }
            }
        }

        /// <summary>已被占用的座位 id（每场演出开始时清空）。</summary>
        private static readonly HashSet<int> ClaimedSeats = new HashSet<int>();

        /// <summary>已被占用的站位（椅子格/台前格），避免两个观众挤在同一格。</summary>
        private static readonly HashSet<IntVec3> ClaimedSpots = new HashSet<IntVec3>();

        /// <summary>清空座位/站位占用表。</summary>
        public static void ResetSeats()
        {
            ClaimedSeats.Clear();
            ClaimedSpots.Clear();
        }

        /// <summary>
        /// 舞台「正前方」的单位格向量（观众该站的那一侧）。
        /// ★★ 2026-10-18 修「观众站到歌手背后」：
        ///   麦克风 XML 是 `<interactionCellOffset>(0,0,-1)</interactionCellOffset>`，
        ///   所以主唱站在麦克风**南侧**；演奏 job 又让他面朝麦克风 ⇒ 他实际面向**北**。
        ///   我上一轮把"前方"写成了南侧（`venue + (0,0,-4)`）⇒ 观众全跑到歌手背后。
        ///   ⇒ 不再写死方向，按「主唱站位 → 麦克风」这条向量推：
        ///     `mic.InteractionCell` = 主唱站位，`mic.Position` = 麦克风，差值就是主唱面朝的方向。
        ///   麦克风转了方向（按 R 旋转）也能自动跟着变。
        /// </summary>
        public static IntVec3 StageFrontDir(Thing mic)
        {
            if (mic != null && mic.def != null && mic.def.hasInteractionCell)
            {
                IntVec3 d = mic.Position - mic.InteractionCell;
                if (d.x != 0 || d.z != 0)
                {
                    return new IntVec3(System.Math.Sign(d.x), 0, System.Math.Sign(d.z));
                }
            }
            // 麦克风没了 / 没有操作点：按现有布局（主唱面朝北）兜底
            return new IntVec3(0, 0, 1);
        }

        /// <summary>
        /// 找一个观众位置。
        /// ★ 2026-10-17 作者要求：「听众不应该在麦克风面，参考之前的演唱会事件逻辑，
        ///   自动寻找附近能坐的位置，或者在麦克风前几格」
        /// ★ 2026-10-18：补上"台前优先 + 防重复占位"，并按 <paramref name="frontDir"/>
        ///   （主唱面朝的方向）判定"前面"，不再写死南北：
        ///   ① 先找舞台**正前方**的椅子（背后的椅子一律不要 —— 坐背后不如站前面）；
        ///   ② 没椅子就站在麦克风正前方 3~7 格、左右 ±3 格；
        ///   ③ 再不行才用原版随机近走格。
        ///   每一步都跳过已被本场其它观众占掉的格子。
        /// </summary>
        public static IntVec3 AudienceSpot(Pawn p, IntVec3 venue, Map map, IntVec3 frontDir)
        {
            if (!venue.IsValid || map == null) return IntVec3.Invalid;
            if (frontDir.x == 0 && frontDir.z == 0) frontDir = new IntVec3(0, 0, 1);
            IntVec3 side = new IntVec3(frontDir.z, 0, frontDir.x);   // 与 frontDir 垂直

            // ① 椅子：**只认舞台正前方**的（作者要的是"在麦克风前面看"；
            //    坐在背后还不如站到前面）
            Building seat = FindSeat(p, venue, map, frontDir);
            if (seat != null)
            {
                ClaimedSeats.Add(seat.thingIDNumber);
                IntVec3 at = seat.Position;
                // 极少数座位格不可站立（例如被其它建筑占住）→ 退到操作点
                if (!at.Standable(map))
                {
                    IntVec3 ic = seat.InteractionCell;
                    if (ic.IsValid && ic.InBounds(map) && ic.Standable(map)) at = ic;
                }
                ClaimedSpots.Add(at);
                return at;
            }

            // ③ 麦克风正前方 3~7 格，左右 ±3 格
            for (int k = 0; k < 16; k++)
            {
                IntVec3 c = venue + frontDir * Rand.RangeInclusive(3, 7)
                    + side * Rand.RangeInclusive(-3, 3);
                if (!c.InBounds(map)) continue;
                if (!c.Standable(map)) continue;
                if (c.GetEdifice(map) != null) continue;     // 别站到乐器/墙上
                if (c.GetFirstPawn(map) != null) continue;   // 已经有人站这了
                if (ClaimedSpots.Contains(c)) continue;
                if (!p.CanReach(c, PathEndMode.OnCell, Danger.Some)) continue;
                ClaimedSpots.Add(c);
                return c;
            }

            // ④ 兜底：场地前方附近随机一个能站、没人占的格子
            IntVec3 near = CellFinder.RandomClosewalkCellNear(venue + frontDir * 4,
                map, 3, delegate (IntVec3 x)
                {
                    return x.Standable(map) && !ClaimedSpots.Contains(x) && x.GetEdifice(map) == null;
                });
            if (near.IsValid && !ClaimedSpots.Contains(near))
            {
                ClaimedSpots.Add(near);
                return near;
            }
            return venue;
        }

        /// <summary>
        /// 场地附近**舞台正前方**的可坐建筑（按 frontDir 投影判定，背后的一律不要）。
        /// </summary>
        private static Building FindSeat(Pawn p, IntVec3 venue, Map map, IntVec3 frontDir)
        {
            if (map.listerBuildings == null) return null;
            List<Building> all = map.listerBuildings.allBuildingsColonist;
            Building best = null;
            float bestDist = 999999f;
            for (int i = 0; i < all.Count; i++)
            {
                Building bd = all[i];
                if (bd == null || bd.def == null || bd.def.building == null) continue;
                if (!bd.def.building.isSittable) continue;
                if (bd is Building_Bed) continue;              // 床不算"座位"
                if (ClaimedSeats.Contains(bd.thingIDNumber)) continue;
                IntVec3 cell = bd.Position;
                if (!cell.IsValid || ClaimedSpots.Contains(cell)) continue;
                if (!cell.Standable(map)) continue;             // 站不上去的"座位"没法坐
                if (cell.GetFirstPawn(map) != null) continue;   // 已经有人坐了
                float d2 = (cell - venue).LengthHorizontalSquared;
                if (d2 > 196f) continue;                       // 14 格
                // 把座位相对麦克风的位移投到 frontDir 上：负值 = 歌手背后，直接跳过
                IntVec3 rel = cell - venue;
                if (rel.x * frontDir.x + rel.z * frontDir.z < 0) continue;
                if (!p.CanReserveAndReach(bd, PathEndMode.OnCell, Danger.Some)) continue;
                if (d2 < bestDist)
                {
                    bestDist = d2;
                    best = bd;
                }
            }
            return best;
        }

        /// <summary>把"观看"job 派给某人（A = 座位/站位，B = 舞台）。
        /// ★ 2026-10-18：**必须同时给 B**，否则驱动只能朝自己的格子发呆；
        ///   只有把麦克风放进 B，`Toils_General.Wait(ticks, B)` 才会让人面向舞台。</summary>
        private static void SendWatchJob(Pawn p, IntVec3 center, Map map, Thing stage)
        {
            if (p == null)
            {
                return;
            }
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(WatchJobDefName);
            if (def == null)
            {
                BinguinLogUtility.Log("找不到 JobDef " + WatchJobDefName + "，观众未派活。",
                    severity: 1, isDebug: false);
                return;
            }
            IntVec3 frontDir = StageFrontDir(stage);
            IntVec3 spot = AudienceSpot(p, center, map, frontDir);
            Job job = JobMaker.MakeJob(def);
            job.targetA = new LocalTargetInfo(spot.IsValid ? spot : center);
            if (stage != null)
            {
                job.targetB = new LocalTargetInfo(stage);
            }
            p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        /// <summary>观众 job 的 defName（每 250 tick 都要比对，别反复查 DefDatabase）。</summary>
        private const string WatchJobDefName = "Binguin_WatchConcert";

        /// <summary>
        /// 演出期间**重新派奏乐 job**。
        /// ★ 2026-10-17 修 bug：「如果中途开其他节日就会离开乐器」
        ///   ⇒ 原因是演出中只在开演那一刻派了一次 job，被别的节日抢走就没人叫回来。
        ///   现在每 250 tick 重新强制派一次，把他们**按在乐器上**。
        /// </summary>
        public static void KeepPerformersPlaying(BinguinConcertBooking b, Map map)
        {
            if (b == null || map == null) return;
            IntVec3 venue = b.venueCenter.IsValid ? b.venueCenter
                : (b.leadSinger != null ? b.leadSinger.Position : IntVec3.Invalid);
            if (!venue.IsValid) return;
            // ★★ 2026-10-17 修正：**他已经在弹就不要发新 job**。
            //   无脑重派会每 250 tick 打断一次，造成：
            //     · 每次都重新走路 → 在琴边反复抖动
            //     · 和对方仪式互相抢人（他派一次、我抢回来、他再派……）
            //   现在只在"他确实不在弹"时才补一次；
            //   防抢人改由“给演出者建 Lord”（见 GivePerformersLord），
            //   那是原版自带的“很忙”机制：
            //     RitualRoleAssignments.cs:600
            //       if (lordJob != null && !(lordJob is LordJob_VoluntarilyJoinable))
            //           return "MessageRitualRoleBusy".Translate(p);   // "{0} is busy."
            JobDef playDef = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_PlayInBand");
            Thing mic = FindNearestInstrument(map, Dialog_BinguinConcertSetup.MicDefName, venue);
            if (b.leadSinger != null && !b.leadSinger.Dead && !IsPlaying(b.leadSinger, playDef))
            {
                if (mic != null) SendPlayJob(b.leadSinger, mic);
                else SendGoto(b.leadSinger, venue);
            }
            if (b.musicians != null)
            {
                foreach (KeyValuePair<string, Pawn> kv in b.musicians)
                {
                    Pawn p = kv.Value;
                    if (p == null || p.Dead || !p.Spawned) continue;
                    if (IsPlaying(p, playDef)) continue;
                    Thing inst = FindNearestInstrument(map, kv.Key, venue);
                    if (inst != null) SendPlayJob(p, inst);
                }
            }
        }

        /// <summary>他是不是正在弹（已在弹就不要发新 job）。</summary>
        private static bool IsPlaying(Pawn p, JobDef playDef)
        {
            if (p == null || playDef == null) return false;
            try { return p.CurJobDef == playDef; }
            catch (System.Exception) { return false; }
        }

        // ── 观众（★ 2026-10-18 新增：之前**没有任何代码**让人在麦克风前看演出）──

        /// <summary>
        /// 演出期间：把观众**按在麦克风前面看演出**。每 250 tick 调一次。
        /// · 已经在看（`CurJobDef == Binguin_WatchConcert`）的不打断 —— 免得每次重派都
        ///   打断走路、在座位和门口之间反复抖（演出者那边踩过同样的坑，见 KeepPerformersPlaying）。
        /// · 被别的活抢走（吃饭/睡觉/别的仪式）就补派一次。
        /// · 名单每次现算，不依赖静态字段 ⇒ 中途读档也不会丢。
        /// </summary>
        public static void KeepAudienceWatching(BinguinConcertBooking b, Map map)
        {
            if (b == null || map == null) return;
            IntVec3 venue = b.venueCenter.IsValid ? b.venueCenter
                : (b.leadSinger != null ? b.leadSinger.Position : IntVec3.Invalid);
            if (!venue.IsValid) return;
            try
            {
                Thing mic = FindNearestInstrument(map, Dialog_BinguinConcertSetup.MicDefName, venue);
                List<Pawn> audience = CollectAudience(b, map);
                for (int i = 0; i < audience.Count; i++)
                {
                    Pawn p = audience[i];
                    if (IsWatching(p)) continue;
                    SendWatchJob(p, venue, map, mic);
                }
                Audience = audience;
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("安排观众看演出失败：" + e.Message,
                    severity: 1, isDebug: false);
            }
        }

        /// <summary>
        /// 本场观众 = 能自由行动的殖民者 + 已到场的受邀贵宾（排除演出者本人）。
        /// ★ 作者没要求手选观众，所以默认"全殖民地都来看"。
        /// </summary>
        private static List<Pawn> CollectAudience(BinguinConcertBooking b, Map map)
        {
            List<Pawn> list = new List<Pawn>();
            if (map == null) return list;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn c = colonists[i];
                if (!CanAttend(c, b, map)) continue;
                list.Add(c);
            }
            if (b != null && b.guests != null)
            {
                for (int i = 0; i < b.guests.Count; i++)
                {
                    Pawn g = b.guests[i];
                    if (!CanAttend(g, b, map)) continue;
                    if (!list.Contains(g)) list.Add(g);
                }
            }
            return list;
        }

        /// <summary>这个人能不能当观众（活着、在地上、没倒、没疯、不是演出者）。</summary>
        private static bool CanAttend(Pawn p, BinguinConcertBooking b, Map map)
        {
            if (p == null || p.Dead || p.Downed || !p.Spawned || p.Map != map) return false;
            if (p.InMentalState) return false;   // 精神崩溃的看不了演出
            if (p.Drafted) return false;         // 被征召的（玩家在指挥战斗）不抢
            if (p.jobs == null) return false;
            if (p.InBed()) return false;         // 躺床养伤的别拖出来看演出
            if (p.health != null && p.health.capacities != null
                && !p.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                return false;                    // 走不动的人（婴儿/重伤）不派
            }
            return !IsPerformer(b, p);
        }

        /// <summary>是不是本场演出者（主唱/乐手）——演出者不看演出。</summary>
        private static bool IsPerformer(BinguinConcertBooking b, Pawn p)
        {
            if (b == null || p == null) return false;
            if (b.leadSinger == p) return true;
            if (b.musicians != null)
            {
                foreach (KeyValuePair<string, Pawn> kv in b.musicians)
                {
                    if (kv.Value == p) return true;
                }
            }
            return false;
        }

        /// <summary>他此刻是不是正在"看演出"。</summary>
        private static bool IsWatching(Pawn p)
        {
            if (p == null || p.CurJobDef == null) return false;
            return p.CurJobDef.defName == WatchJobDefName;
        }

        /// <summary>
        /// 演出结束（自办）：只给**正在看演出的人**发心情，然后清运行期状态。
        /// ★ 2026-10-18：原来这个收尾是空的，全靠"乐队来访"那套
        ///   `BinguinBandConcert.EndConcert` 兜底 —— 而它还会**掉武器 + 给冰鹅派系 +20 好感**，
        ///   对自办演唱会是不对的。现在那套只在 `bandArrived`（来访事件）时跑，
        ///   自办演出由这里收尾：发心情 + 放贵宾回家 + 清座位表。
        /// </summary>
        public static void EndPerformance(BinguinConcertBooking b)
        {
            int n = 0;
            // 本场掷出的档位（读档后 b 可能是 null ⇒ 退回"最可能档位"）
            int tier = (b != null && b.outcomeTier >= 0 && b.outcomeTier < BinguinConcertQuality.TierCount)
                ? b.outcomeTier
                : BinguinConcertQuality.TierOf(b != null ? b.quality : 0.5f);
            try
            {
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Binguin_AttendedConcert");
                // 观众名单有两个来源，合并去重：
                //   ① `Audience`（演出期间每 250 tick 刷新，最完整）
                //   ② 此刻 CurJobDef 还是"看演出"的人（防止收尾瞬间 job 刚好结束）
                HashSet<Pawn> watchers = new HashSet<Pawn>();
                if (Audience != null)
                {
                    for (int i = 0; i < Audience.Count; i++)
                    {
                        if (Audience[i] != null) watchers.Add(Audience[i]);
                    }
                }
                List<Map> maps = Find.Maps;
                for (int m = 0; m < maps.Count; m++)
                {
                    Map map = maps[m];
                    if (map == null || map.mapPawns == null) continue;
                    // 1.6+ 里 AllPawnsSpawned 是 IReadOnlyList<Pawn>（不能声明成 List）
                    var all = map.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (IsWatching(all[i])) watchers.Add(all[i]);
                    }
                }

                foreach (Pawn p in watchers)
                {
                    if (p == null || p.Dead || !p.Spawned) continue;
                    if (thought == null || p.needs == null || p.needs.mood == null) continue;
                    // ★ 2026-10-18：按本场掷出的档位给对应阶段的心情
                    //   （ThoughtDef 有 4 个 stage：6 / 10 / 14 / 18）
                    Thought_Memory memory = ThoughtMaker.MakeThought(thought, tier);
                    p.needs.mood.thoughts.memories.TryGainMemory(memory, null);
                    n++;
                }
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("演唱会收尾发心情失败：" + e.Message,
                    severity: 1, isDebug: false);
            }

            try
            {
                // 贵宾看完就走：解散他们的 Lord，原版思维树会自己把他们送出地图
                if (b != null && b.guests != null && !b.guests.NullOrEmpty())
                {
                    ReleaseGuestLords(b.guests);
                }
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("送贵宾离场失败：" + e.Message, severity: 1, isDebug: false);
            }

            // ★ 把参与者从"演出中"的持久化状态里放回日常
            //   （他那边 `GameComponent_BinguinPerformanceState` 存了 performers/audience，
            //     不清的话读档后会被当成还在演出）
            try
            {
                GameComponent_BinguinPerformanceState.ReleaseParticipants(Performers);
                GameComponent_BinguinPerformanceState.ReleaseParticipants(Audience);
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("释放演出参与者失败：" + e.Message, severity: 1, isDebug: false);
            }

            Audience = new List<Pawn>();
            Performers = new List<Pawn>();
            SelfHosted = false;
            ResetSeats();
            BinguinLogUtility.Log("自办演唱会结束：" + n + " 名观众获得了心情加成（档位【"
                + BinguinConcertQuality.TierKey(tier).Translate() + "】，心情 "
                + BinguinConcertQuality.MoodOffsetText(tier) + "）。",
                isDebug: false);
        }

        /// <summary>解散贵宾的 Lord（清掉职责后他们会自己往地图边缘走）。</summary>
        private static void ReleaseGuestLords(List<Pawn> guests)
        {
            List<Lord> lords = new List<Lord>();
            for (int i = 0; i < guests.Count; i++)
            {
                Pawn g = guests[i];
                if (g == null || g.Dead || !g.Spawned) continue;
                Lord l = g.GetLord();
                if (l != null && !lords.Contains(l)) lords.Add(l);
            }
            for (int i = 0; i < lords.Count; i++)
            {
                Lord l = lords[i];
                if (l == null || l.Map == null || l.Map.lordManager == null) continue;
                try
                {
                    l.Map.lordManager.RemoveLord(l);
                }
                catch (System.Exception e)
                {
                    BinguinLogUtility.Log("解散贵宾 Lord 失败：" + e.Message,
                        severity: 1, isDebug: false);
                }
            }
        }

        /// <summary>
        /// 给**演出者**建 Lord（让原版认为他们“很忙”）。
        /// ★ 2026-10-17 作者反馈：「演出中途开别的节日……乐手正在忙」
        ///   ⇒ 不靠抢 job（会互相拉扯），而是让他们**处于 Lord 中**：
        ///     `RitualRoleAssignments.cs:600` 会直接返回
        ///     `MessageRitualRoleBusy`（"{0} is busy."），根本选不进另一个仪式。
        ///   用模组已有的 `LordJob_BinguinCrowd`（非 LordJob_VoluntarilyJoinable）。
        /// </summary>
        public static void GivePerformersLord(List<Pawn> performers, Map map, IntVec3 venue)
        {
            if (performers == null || performers.Count == 0 || map == null) return;
            try
            {
                List<Pawn> valid = new List<Pawn>();
                for (int i = 0; i < performers.Count; i++)
                {
                    Pawn p = performers[i];
                    if (p == null || p.Dead || !p.Spawned) continue;
                    if (p.GetLord() != null) continue;   // 已在 Lord 中就不重复建
                    valid.Add(p);
                }
                if (valid.Count == 0) return;
                Verse.AI.Group.LordMaker.MakeNewLord(Faction.OfPlayer,
                    new LordJob_BinguinCrowd(venue), map, valid);
                BinguinLogUtility.Log("已为 " + valid.Count
                    + " 名演出者建 Lord（原版会把他们判为 busy，其他仪式抢不走）。",
                    isDebug: false);
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("为演出者建 Lord 失败：" + e.Message, severity: 1, isDebug: false);
            }
        }

        /// <summary>按乐器 defName 找离场地中心最近的实例。</summary>
        private static Thing FindNearestInstrument(Map map, string instrumentDefName, IntVec3 near)
        {
            if (map == null || instrumentDefName.NullOrEmpty())
            {
                return null;
            }
            Thing best = null;
            float bestDist = float.MaxValue;
            List<Building> all = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < all.Count; i++)
            {
                Building bd = all[i];
                if (bd == null || bd.def == null || bd.def.defName != instrumentDefName)
                {
                    continue;
                }
                float d = (bd.Position - near).LengthHorizontalSquared;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = bd;
                }
            }
            return best;
        }

        /// <summary>
        /// 【兼容旧调用】收尾。
        /// ★ 2026-10-18：真正的收尾已改成 `EndPerformance(booking)`（发心情 + 放贵宾走）；
        ///   这个无参版本现在没有调用点，只留着防止旧存档/外部脚本反射调用时炸掉。
        /// </summary>
        public static void OnPerformanceEnded()
        {
            // 先把参与者从演出状态里放回日常（他这边的持久化状态），
            // 再走我们的收尾（发心情、放贵宾、清座位表）。
            GameComponent_BinguinPerformanceState.ReleaseParticipants(Performers);
            GameComponent_BinguinPerformanceState.ReleaseParticipants(Audience);
            EndPerformance(null);
        }

        /// <summary>
        /// 【人员到场】—— 到预定日子时调用（**还没开演**，只是让大家走到场地）。
        /// ★ 作者要求：「开始之前需要弹个信封显示人员到场，然后再由玩家主动开始演唱会」
        /// ⇒ 这里只派"走过去"的 job，演出本身等玩家点麦克风。
        /// </summary>
        public static void AssembleOnSite(BinguinConcertBooking b, Map map)
        {
            try
            {
                if (b == null || map == null) return;
                IntVec3 venue = b.venueCenter.IsValid
                    ? b.venueCenter
                    : (b.leadSinger != null ? b.leadSinger.Position : IntVec3.Invalid);

                SendGoto(b.leadSinger, venue);

                if (b.musicians != null)
                {
                    foreach (KeyValuePair<string, Pawn> kv in b.musicians)
                    {
                        Pawn p = kv.Value;
                        if (p == null || p.Dead || !p.Spawned) continue;
                        Thing inst = FindNearestInstrument(map, kv.Key, venue);
                        SendGoto(p, inst != null ? inst.Position : venue);
                    }
                }

                List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn c = colonists[i];
                    if (c == null || c.Dead) continue;
                    if (c == b.leadSinger) continue;
                    SendGoto(c, venue);
                }

                BinguinLogUtility.Log("演唱会人员已到场，等玩家点麦克风开演。", isDebug: false);

                // ★ 2026-10-11 修「邀请的人没有来」：
                //   贵宾原来在 `BeginPerformance`（开演）时才生成 —— 但作者要的是
                //   「信封显示人员到场」时就应该一起到场。⇒ 挪到到场阶段生成。
                // ★ 2026-10-17 作者要求：「不要直接生成在场地周围，还是走进来更好」
                //   ⇒ 仍从**地图边缘**生成，然后让他们自己走过来。
                //   名单存进 booking.guests，由 KeepCrowdInPlace 负责：
                //   路上继续走 → 到了就在场边游荡。
                List<Pawn> guests = BinguinConcertGuests.SpawnInvitedGuests(b, map);
                if (b.guests == null) b.guests = new List<Pawn>();
                for (int i = 0; i < guests.Count; i++)
                {
                    if (guests[i] != null) b.guests.Add(guests[i]);
                }
                // ★ 先给 Lord，否则外派系小人没有 duty，生成后立刻往地图边缘走
                GiveGuestsLord(guests, map, venue);
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("人员到场时出错（已忽略）：" + e.Message, severity: 1, isDebug: false);
            }
        }

        /// <summary>
        /// 让某人在场地附近**游荡**等待开演。
        /// ★ 2026-10-17 作者要求：「观众应该到麦克风前面游荡等待演唱会开始」
        /// ⇒ 用原版 `JobDefOf.GotoWander`（走过去就会随便走动），而不是站死在一点。
        /// </summary>
        public static void SendWanderNear(Pawn p, IntVec3 center, Map map)
        {
            if (p == null || p.Dead || !p.Spawned || map == null || !center.IsValid) return;
            try
            {
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(center, map, 6);
                if (!cell.IsValid) cell = center;
                Job job = JobMaker.MakeJob(JobDefOf.GotoWander, cell);
                p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
            catch (System.Exception) { }
        }

        /// <summary>
        /// 等待开演期间：演出者守在乐器旁，**观众（殖民者 + 外派系贵宾）
        /// 在麦克风前游荡**。每 250 tick 重新叫一次，保证人留在现场。
        /// </summary>
        public static void KeepCrowdInPlace(BinguinConcertBooking b, Map map)
        {
            if (b == null || map == null) return;
            KeepPerformersInPlace(b, map);
            IntVec3 venue = b.venueCenter.IsValid ? b.venueCenter
                : (b.leadSinger != null ? b.leadSinger.Position : IntVec3.Invalid);
            if (!venue.IsValid) return;

            // 外派系贵宾
            // ★★ 2026-10-17 修「访客又不见了」：
            //   模组自己的注释就写了（BandVisitFlow_Binguin.cs:783）：
            //     `Lord.RemovePawn` 会清空 duty、解除 pawn.lord，
            //     于是原版 JobGiver_ExitMapBest 会把它们送出地图。
            //   ⇒ 这里**自愈**：发现贵宾没有 lord 就立即重建，
            //     避免代码别处（或原版）把 lord 解散后人跑光。
            if (b.guests != null)
            {
                List<Pawn> lostLord = null;
                for (int i = 0; i < b.guests.Count; i++)
                {
                    Pawn g = b.guests[i];
                    if (g == null || g.Dead || !g.Spawned) continue;
                    if (g.GetLord() == null)
                    {
                        if (lostLord == null) lostLord = new List<Pawn>();
                        lostLord.Add(g);
                    }
                    if ((g.Position - venue).LengthHorizontalSquared > 144f)   // 12 格以外
                        SendGoto(g, venue);
                    else
                        SendWanderNear(g, venue, map);
                }
                if (lostLord != null && lostLord.Count > 0)
                {
                    GiveGuestsLord(lostLord, map, venue);
                    BinguinLogUtility.Log("贵宾丢了 Lord，已重建 " + lostLord.Count
                        + " 人（否则他们会自己走出地图）。",
                        severity: 1, isDebug: false);
                }
            }
            // 本方殖民者（减掉演出者本人）：同样先走过去再游荡
            List<Pawn> cols = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < cols.Count; i++)
            {
                Pawn c = cols[i];
                if (c == null || c.Dead) continue;
                if (c == b.leadSinger) continue;
                if (b.musicians != null && b.musicians.ContainsValue(c)) continue;
                if ((c.Position - venue).LengthHorizontalSquared > 144f)
                    SendGoto(c, venue);
                else
                    SendWanderNear(c, venue, map);
            }
        }

        /// <summary>派一个"走到某格"的 job（不依赖我们自己的 JobDriver）。</summary>
        private static void SendGoto(Pawn p, IntVec3 cell)
        {
            if (p == null || p.Dead || !p.Spawned || !cell.IsValid) return;
            try
            {
                Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
                p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
            catch (System.Exception) { }
        }

        /// <summary>
        /// 到场后**持续把人留在位置上**。
        /// ★ 2026-10-11 修「主唱没有到自己的位置」：
        ///   原来只派一次 `JobDefOf.Goto`，走到就结束，小人随即干别的去了。
        ///   ⇒ 改为在等待开演期间**每 250 tick 重新派一次 Goto**（幂等、无副作用），
        ///     这样人一直待在麦克风/各自乐器旁边，直到玩家开演。
        /// </summary>
        public static void KeepPerformersInPlace(BinguinConcertBooking b, Map map)
        {
            if (b == null || map == null || b.leadSinger == null) return;
            IntVec3 venue = b.venueCenter.IsValid ? b.venueCenter : b.leadSinger.Position;
            SendGoto(b.leadSinger, venue);
            if (b.musicians != null)
            {
                foreach (KeyValuePair<string, Pawn> kv in b.musicians)
                {
                    Pawn p = kv.Value;
                    if (p == null || p.Dead || !p.Spawned) continue;
                    Thing inst = FindNearestInstrument(map, kv.Key, venue);
                    SendGoto(p, inst != null ? inst.InteractionCell : venue);
                }
            }
        }

        /// <summary>送客 / 收工（6 小时没开演被取消时用）。</summary>
        public static void SendEveryoneHome(BinguinConcertBooking b, Map map)
        {
            try
            {
                if (b == null) return;
                List<Pawn> all = b.LiveMusicians();
                if (b.leadSinger != null) all.Add(b.leadSinger);
                if (b.guests != null) all.AddRange(b.guests);
                GameComponent_BinguinPerformanceState.ReleaseParticipants(all);
                if (b.musicians != null)
                {
                    foreach (KeyValuePair<string, Pawn> kv in b.musicians)
                    {
                        if (kv.Value != null)
                        {
                            kv.Value.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                        }
                    }
                }
            }
            catch (System.Exception) { }
        }
    }
}
