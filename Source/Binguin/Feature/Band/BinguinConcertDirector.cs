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
        public static List<Pawn> Audience = new List<Pawn>();

        /// <summary>本场演出的乐手（含主唱）。</summary>
        public static List<Pawn> Performers = new List<Pawn>();

        /// <summary>本场是不是玩家自办的（区别于外团来访那套）。</summary>
        public static bool SelfHosted;

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
                ResetMusic();

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

                // ③ 观众 → 看演出（地图上所有能自由行动的殖民者）
                List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn c = colonists[i];
                    if (c == null || c.Dead)
                    {
                        continue;
                    }
                    if (Performers.Contains(c))
                    {
                        continue;   // 表演者不给自己当观众
                    }
                    SendWatchJob(c, b.venueCenter, map);
                    Audience.Add(c);
                }

                // ④ 外派系贵宾 → 从地图边缘进场（通讯台发过邀请、对方答应了的）
                List<Pawn> guests = BinguinConcertGuests.SpawnInvitedGuests(b, map);
                for (int i = 0; i < guests.Count; i++)
                {
                    SendWatchJob(guests[i], b.venueCenter, map);
                    Audience.Add(guests[i]);
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


        // ── 音乐播放（★ 2026-10-17 作者反馈「没有播放音乐」）──
        //
        // 原因：曲目 SoundDef 从头到尾**从未被播放**。
        //   厂商 API：（Verse）Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDef, Map)
        //   演唱会固定 4 小时，曲目只影响听到什么 ⇒ 按选择顺序一首首放，
        //   每首放完（按它自己的 durationTicks）再接下一首，放完就安静。
        private static int songIndex;
        private static int nextSongTick;

        /// <summary>开演时重置音乐播放进度。</summary>
        public static void ResetMusic()
        {
            songIndex = 0;
            nextSongTick = GenTicks.TicksGame;
        }

        /// <summary>每 60 tick 调一次：到点就放下一首。</summary>
        public static void TickMusic(BinguinConcertBooking b)
        {
            if (b == null || b.songs == null || b.songs.Count == 0) return;
            if (GenTicks.TicksGame < nextSongTick) return;
            if (songIndex >= b.songs.Count) return;
            Binguin_SongDef s = b.songs[songIndex];
            int dur = 10000;
            if (s != null)
            {
                dur = s.EffectiveDurationTicks;
                // ★ `Binguin_SongDef.soundDef` 存的是 **defName 字符串**（方便 XML 里写引用），
                //   所以先查 DefDatabase 拿到 SoundDef 再播。
                SoundDef clip = s.soundDef.NullOrEmpty()
                    ? null : DefDatabase<SoundDef>.GetNamedSilentFail(s.soundDef);
                if (clip == null && !s.soundDef.NullOrEmpty())
                {
                    BinguinLogUtility.Log("找不到 SoundDef：" + s.soundDef, severity: 1, isDebug: false);
                }
                if (clip != null && b.Map != null)
                {
                    try
                    {
                        // ★★ 2026-10-17 自测发现的错误：
                        //   `PlayOneShotOnCamera` 要求 SoundDef 有 **on-camera subSounds**，
                        //   而我们的曲目 SoundDef 是 `MapOnly` → 报
                        //   "Tried to play ... on camera but it has no on-camera subSounds."
                        //   ⇒ 改用 `PlayOneShot(clip, SoundInfo.InMap(...))`，
                        //     从场地那一格发声，与 MapOnly 一致。
                        IntVec3 at = b.venueCenter.IsValid ? b.venueCenter
                            : (b.leadSinger != null ? b.leadSinger.Position : IntVec3.Invalid);
                        Verse.Sound.SoundInfo info;
                        if (at.IsValid)
                            info = Verse.Sound.SoundInfo.InMap(new TargetInfo(at, b.Map),
                                Verse.Sound.MaintenanceType.None);
                        else
                            info = Verse.Sound.SoundInfo.OnCamera(Verse.Sound.MaintenanceType.None);
                        Verse.Sound.SoundStarter.PlayOneShot(clip, info);
                        BinguinLogUtility.Log("演唱会播放第 " + (songIndex + 1)
                            + " 首：" + s.label, isDebug: false);
                    }
                    catch (System.Exception e)
                    {
                        BinguinLogUtility.Log("播放曲目失败：" + e.Message,
                            severity: 1, isDebug: false);
                    }
                }
            }
            nextSongTick = GenTicks.TicksGame + dur;
            songIndex++;
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
                foreach (KeyValuePair<Faction, List<Pawn>> kv in byFaction)
                {
                    Verse.AI.Group.LordMaker.MakeNewLord(kv.Key,
                        new LordJob_BinguinCrowd(venue), map, kv.Value);
                }
                BinguinLogUtility.Log("已为贵宾建立 Lord（防止立刻离开地图）。",
                    isDebug: false);
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("为贵宾建 Lord 失败：" + e.Message, severity: 1, isDebug: false);
            }
        }

        /// <summary>已被占用的座位 id（每场演出开始时清空）。</summary>
        private static readonly HashSet<int> ClaimedSeats = new HashSet<int>();

        /// <summary>清空座位占用表。</summary>
        public static void ResetSeats()
        {
            ClaimedSeats.Clear();
        }

        /// <summary>
        /// 找一个观众位置。
        /// ★ 2026-10-17 作者要求：「听众不应该在麦克风面，参考之前的演唱会事件逻辑，
        ///   自动寻找附近能坐的位置，或者在麦克风前几格」
        ///   ⇒ 优先找场地附近**可坐的建筑**（椅子/座位），
        ///     没有就站在麦克风**前方 4~±2 格**，不挤在麦克风上。
        /// </summary>
        public static IntVec3 AudienceSpot(Pawn p, IntVec3 venue, Map map)
        {
            if (!venue.IsValid || map == null) return IntVec3.Invalid;

            // ① 找座位（场地 14 格内，未被占）
            if (map.listerBuildings != null)
            {
                List<Building> all = map.listerBuildings.allBuildingsColonist;
                float best = 999f;
                IntVec3 bestCell = IntVec3.Invalid;
                for (int i = 0; i < all.Count; i++)
                {
                    Building bd = all[i];
                    if (bd == null || bd.def == null || bd.def.building == null) continue;
                    if (!bd.def.building.isSittable) continue;
                    if (ClaimedSeats.Contains(bd.thingIDNumber)) continue;
                    IntVec3 cell = bd.InteractionCell.IsValid ? bd.InteractionCell : bd.Position;
                    float d2 = (cell - venue).LengthHorizontalSquared;
                    if (d2 > 196f) continue;            // 14 格
                    if (d2 < best) { best = d2; bestCell = cell; }
                }
                if (bestCell.IsValid)
                {
                    return bestCell;
                }
            }

            // ② 没座位 ⇒ 麦克风前方 4 ± 2 格
            for (int k = 0; k < 8; k++)
            {
                IntVec3 c = venue + new IntVec3(0, 0, -(4 + Rand.RangeInclusive(-2, 2)));
                c = CellFinder.RandomClosewalkCellNear(c, map, 2);
                if (c.IsValid && c != venue) return c;
            }
            return venue;
        }

        /// <summary>把"观看"job 派给某人（目标 = 座位或麦克风前方的位置）。</summary>
        private static void SendWatchJob(Pawn p, IntVec3 center, Map map)
        {
            if (p == null)
            {
                return;
            }
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_WatchConcert");
            if (def == null)
            {
                BinguinLogUtility.Log("找不到 JobDef Binguin_WatchConcert，观众未派活。",
                    severity: 1, isDebug: false);
                return;
            }
            IntVec3 spot = AudienceSpot(p, center, map);
            Job job = JobMaker.MakeJob(def);
            job.targetA = new LocalTargetInfo(spot.IsValid ? spot : center);
            p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

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
        /// 本场结束后由**已有**的 `GameComponent_BinguinBandConcertEnd` 调用
        /// （它检测 `BinguinConcert.Active` 由 true 变 false）。
        /// 这里只清自办演出的运行时状态。
        /// </summary>
        public static void OnPerformanceEnded()
        {
            Audience = new List<Pawn>();
            Performers = new List<Pawn>();
            SelfHosted = false;
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

            // 外派系贵宾：**还在路上就继续走**，到了场边才游荡
            if (b.guests != null)
            {
                for (int i = 0; i < b.guests.Count; i++)
                {
                    Pawn g = b.guests[i];
                    if (g == null || g.Dead || !g.Spawned) continue;
                    if ((g.Position - venue).LengthHorizontalSquared > 144f)   // 12 格以外
                        SendGoto(g, venue);
                    else
                        SendWanderNear(g, venue, map);
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
