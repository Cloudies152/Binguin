// ============================================================================
// 冰鹅族【自办演唱会】· 预约调度（2026-10-11 作者需求）
//
// ★★ 作者定的流程（原话）：
//   「通过点击**麦克风**来准备演唱会」
//   「开始演唱会：选择主唱和乐手，并**预定时间 1-7 天后**，然后给其他派系发
//     邀请函的位置在原版**通讯台**，聊天选项里多个演唱会邀请
//     （**只有开始准备演唱会并且预定时间后才可以发邀请**）」
//   「**只有主唱死了才取消**，其他不影响，因为中途可能要改建舞台」
//
// ★★ 所以本文件管"预约"这件事：
//     点麦克风 → 选主唱/乐手/曲目/日子 → `Book()` 存下来
//     → 到日子 `Tick()` 自动开演
//     → 期间通讯台可以发邀请（见 `BinguinConcertInvite.cs`）
//
// ★★ 与已有代码的关系：
//   · `BinguinConcert`（BandConcert_Binguin.cs）仍是**运行期**状态
//     （`EndTick` / `Active` / 拦袭击），本文件在开演时调用它。
//     这样"演唱会期间不触发袭击"那套逻辑完全不用改。
//   · 派活/维护播放状态在 `BinguinConcertDirector`（后续文件）。
//
// ★★ 为什么用 GameComponent：预约要**进存档**（可能发生在几天前）。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;
using Binguin.Helper;

namespace Binguin.Feature.Band
{
    /// <summary>
    /// 一场已预约的演唱会（进存档）。
    /// ★ 作者定：**只有主唱死了才取消**；乐器被拆、场地改建都不取消
    ///   （"因为中途可能要改建舞台"）。
    /// </summary>
    public class BinguinConcertBooking : IExposable
    {
        /// <summary>主唱（必填）。死了这场就取消。</summary>
        public Pawn leadSinger;

        /// <summary>乐手：乐器 defName → 小人。可缺人。</summary>
        public Dictionary<string, Pawn> musicians = new Dictionary<string, Pawn>();

        /// <summary>★ 2026-10-11：曲目改为**多首**（最多 4 首，按顺序演出）。</summary>
        public List<Binguin_SongDef> songs = new List<Binguin_SongDef>();

        /// <summary>兼容只读访问：第一首。</summary>
        public Binguin_SongDef FirstSong
        {
            get { return songs != null && songs.Count > 0 ? songs[0] : null; }
        }

        /// <summary>人员是否已到场、正在等玩家开演。</summary>
        public bool awaitingStart;

        /// <summary>到场的外派系贵宾（运行期用，不进存档）。
        /// 用于开演前把他们叫到麦克风前面游荡。</summary>
        public List<Pawn> guests = new List<Pawn>();

        /// <summary>本场演出质量 0~1（开演时计算）。</summary>
        public float quality;

        /// <summary>
        /// 本场实际掷出的档位（0 惨不忍睹 / 1 平平无奇 / 2 很精彩 / 3 难以忘怀）。
        /// ★ 2026-10-18：开演时按 `TierProbabilities(quality)` 掷**一次**，
        ///   之后散场结算只读这个值（同一场不会重掷）。
        ///   注意：开演后 booking 会转成 `performingBooking`（运行期字段，不进存档），
        ///   所以存档里这个值只在"还没开演"时有意义。
        /// </summary>
        public int outcomeTier = -1;

        /// <summary>人员到场的时刻（用于"6 小时不开演就取消"）。</summary>
        public int assembledTick;

        /// <summary>到场后已经等了几个小时。</summary>
        public float AssembledHours
        {
            get
            {
                if (!awaitingStart || assembledTick <= 0) return 0f;
                return (GenTicks.TicksGame - assembledTick) / (float)BinguinConcert.TicksPerHour;
            }
        }
        /// <summary>预定的开演时刻（tick）。</summary>
        public int scheduledTick;

        /// <summary>预定时的"几天后"（1~7），只用于显示。</summary>
        public int daysFromNow = 1;

        /// <summary>场地中心（麦克风位置）。读档后按它复核场地。</summary>
        public IntVec3 venueCenter = IntVec3.Invalid;

        /// <summary>场地所在地图的 uniqueID（多地图存档用）。</summary>
        public int mapId = -1;

        /// <summary>已发出邀请的派系（避免重复邀请同一个）。</summary>
        public List<int> invitedFactionIds = new List<int>();

        /// <summary>答应来的人数：派系 loadID → 人数。</summary>
        public Dictionary<int, int> acceptedGuests = new Dictionary<int, int>();

        /// <summary>取消原因（非空表示这场已经废了，等 Tick 清理）。</summary>
        public string cancelReason;

        // ── 运行时缓存（不进存档）──
        private Map cachedMap;

        /// <summary>场地所在地图。</summary>
        public Map Map
        {
            get
            {
                if (cachedMap == null && mapId >= 0 && Find.Maps != null)
                {
                    for (int i = 0; i < Find.Maps.Count; i++)
                    {
                        if (Find.Maps[i].uniqueID == mapId)
                        {
                            cachedMap = Find.Maps[i];
                            break;
                        }
                    }
                }
                return cachedMap;
            }
        }

        /// <summary>还有多少 tick 开演（可为负 = 已过期）。</summary>
        public int TicksUntilStart
        {
            get { return scheduledTick - GenTicks.TicksGame; }
        }

        /// <summary>还有几天（向上取整，用于显示）。</summary>
        public int DaysUntilStart
        {
            get
            {
                int t = TicksUntilStart;
                if (t <= 0)
                {
                    return 0;
                }
                return (t + GenDate.TicksPerDay - 1) / GenDate.TicksPerDay;
            }
        }

        /// <summary>主唱是否还能上台（死了 / 没了就不行）。</summary>
        public bool LeadSingerUsable
        {
            get
            {
                return leadSinger != null && !leadSinger.Dead
                    && leadSinger.Spawned && leadSinger.Map != null;
            }
        }

        /// <summary>
        /// 取"乐器 defName → 乐手"，只保留**还活着**的乐手。
        /// 少人不算错（作者定的"乐手可缺人"）。
        /// </summary>
        public List<Pawn> LiveMusicians()
        {
            List<Pawn> result = new List<Pawn>();
            if (musicians == null)
            {
                return result;
            }
            foreach (KeyValuePair<string, Pawn> kv in musicians)
            {
                Pawn p = kv.Value;
                if (p != null && !p.Dead && p.Spawned)
                {
                    result.Add(p);
                }
            }
            return result;
        }

        public void ExposeData()
        {
            Scribe_Values.Look<float>(ref quality, "binguinBookingQuality", 0f, false);
            Scribe_Values.Look<int>(ref outcomeTier, "binguinBookingTier", -1, false);
            Scribe_References.Look<Pawn>(ref leadSinger, "binguinBookingSinger");
            Scribe_Collections.Look<Binguin_SongDef>(ref songs, "binguinBookingSongs", LookMode.Def);
            Scribe_Values.Look<int>(ref scheduledTick, "binguinBookingTick", 0, false);
            Scribe_Values.Look<int>(ref daysFromNow, "binguinBookingDays", 1, false);
            Scribe_Values.Look<IntVec3>(ref venueCenter, "binguinBookingVenue", IntVec3.Invalid, false);
            Scribe_Values.Look<int>(ref mapId, "binguinBookingMap", -1, false);
            Scribe_Values.Look<string>(ref cancelReason, "binguinBookingCancel", null, false);
            Scribe_Values.Look<bool>(ref awaitingStart, "binguinBookingAwaiting", false, false);
            Scribe_Values.Look<int>(ref assembledTick, "binguinBookingAssembledTick", 0, false);
            Scribe_Collections.Look<string, Pawn>(ref musicians, "binguinBookingMusicians",
                LookMode.Value, LookMode.Reference);
            Scribe_Collections.Look<int>(ref invitedFactionIds, "binguinBookingInvited",
                LookMode.Value);
            Scribe_Collections.Look<int, int>(ref acceptedGuests, "binguinBookingGuests",
                LookMode.Value, LookMode.Value);

            // ★ 读档后清掉缓存的地图引用，让它按 mapId 重新找
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                cachedMap = null;
                if (songs == null) { songs = new List<Binguin_SongDef>(); }
                if (musicians == null)
                {
                    musicians = new Dictionary<string, Pawn>();
                }
                if (invitedFactionIds == null)
                {
                    invitedFactionIds = new List<int>();
                }
                if (acceptedGuests == null)
                {
                    acceptedGuests = new Dictionary<int, int>();
                }
            }
        }
    }

    /// <summary>
    /// 预约总控（进存档）。挂在 `GameComponent` 上。
    /// ★ 全图**同时只能有一场**预约（作者没要求多场；先按互斥做，最简单也最不容易出错）。
    /// </summary>
    public class GameComponent_BinguinConcertBooking : GameComponent
    {
        /// <summary>正在演出的那一场（运行期，不进存档）。用于演出中重派 job。</summary>
        public BinguinConcertBooking performingBooking;

        /// <summary>
        /// ★ 2026-10-17 作者要求：「第 2~4 首会响，在演唱会后也继续播放」
        /// ⇒ 歌单播放与演出脱钩：演出结束后这个引用仍然保留，
        ///   直到全部曲目放完才清空。
        /// </summary>
        public BinguinConcertBooking musicPlaylist;

        /// <summary>上一场演唱会结束的时刻（用于 5 天冷却）。</summary>
        public int lastConcertEndTick = -999999;

        private bool wasPerforming;

        /// <summary>当前预约（null = 没有）。</summary>
        public BinguinConcertBooking booking;

        /// <summary>有没有一场还没办的预约。</summary>
        public bool HasBooking
        {
            get { return booking != null && booking.cancelReason.NullOrEmpty(); }
        }

        public GameComponent_BinguinConcertBooking(Game game)
        {
        }

        /// <summary>取当前预约（可能为 null）。</summary>
        public static GameComponent_BinguinConcertBooking Instance
        {
            get
            {
                if (Verse.Current.Game == null)
                {
                    return null;
                }
                return Verse.Current.Game.GetComponent<GameComponent_BinguinConcertBooking>();
            }
        }

        /// <summary>当前预约（可能为 null）。便捷访问。</summary>
        /// ★ 名字不叫 `Current` —— 那会与 `Verse.Current` 冲突（编译报错）。
        public static BinguinConcertBooking ActiveBooking
        {
            get
            {
                GameComponent_BinguinConcertBooking c = Instance;
                return c != null ? c.booking : null;
            }
        }

        /// <summary>有没有一场还有效的预约。</summary>
        public static bool AnyActiveBooking
        {
            get
            {
                BinguinConcertBooking b = ActiveBooking;
                return b != null && b.cancelReason.NullOrEmpty();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look<BinguinConcertBooking>(ref booking, "binguinConcertBooking");
            Scribe_Values.Look<int>(ref lastConcertEndTick, "binguinLastConcertEnd", -999999, false);
        }

        /// <summary>
        /// 登记一场预约。`days` 会被夹到 1~7（作者要求）。
        /// 已有一场有效预约时**不覆盖**，返回 false（调用方给提示）。
        /// </summary>
        /// <summary>冷却还剩几天（0 = 可再办）。作者要求每场之间冷却 5 天。</summary>
        public float CooldownDaysLeft
        {
            get
            {
                if (lastConcertEndTick <= -999999) return 0f;
                int elapsed = GenTicks.TicksGame - lastConcertEndTick;
                int need = 5 * GenDate.TicksPerDay;
                if (elapsed >= need) return 0f;
                return (need - elapsed) / (float)GenDate.TicksPerDay;
            }
        }

        public bool Book(Pawn leadSinger, Dictionary<string, Pawn> musicians,
            List<Binguin_SongDef> songs, int days, Map map, IntVec3 venueCenter)
        {
            if (leadSinger == null || map == null)
            {
                return false;
            }
            if (HasBooking)
            {
                return false;
            }
            // 5 天冷却（作者 2026-10-11 要求）
            if (CooldownDaysLeft > 0f)
            {
                return false;
            }
            if (days < 1)
            {
                days = 1;
            }
            if (days > 7)
            {
                days = 7;
            }

            BinguinConcertBooking b = new BinguinConcertBooking();
            b.leadSinger = leadSinger;
            b.musicians = musicians != null ? musicians : new Dictionary<string, Pawn>();
            b.songs = songs != null ? new List<Binguin_SongDef>(songs) : new List<Binguin_SongDef>();
            b.daysFromNow = days;
            b.scheduledTick = GenTicks.TicksGame + days * GenDate.TicksPerDay;
            b.venueCenter = venueCenter;
            b.mapId = map.uniqueID;
            booking = b;

            BinguinLogUtility.Log("已登记演唱会预约：主唱 " + leadSinger.LabelShort
                + "，乐手 " + b.musicians.Count + " 人，曲目 "
                + (songs != null && songs.Count > 0 ? songs[0].label + " 等 " + songs.Count + " 首" : "（无）")
                + "，" + days + " 天后开演。", isDebug: false);
            return true;
        }

        /// <summary>取消预约（作者要求：主唱死了就取消）。</summary>
        public void Cancel(string reason)
        {
            if (booking == null)
            {
                return;
            }
            BinguinLogUtility.Log("演唱会预约已取消：" + reason, isDebug: false);
            booking = null;
        }

        /// <summary>
        /// 每 tick 检查：到点了就开演；主唱没了就取消。
        /// ★ 作者定：**只有主唱死了才取消**，其他情况（乐器被拆、场地改建）不取消。
        /// </summary>
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            // 每 60 tick 查一次就够（1 秒），省性能
            if (Find.TickManager == null || Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            // ★ 2026-10-11：检测"演出结束"以便记录 5 天冷却的起点
            if (wasPerforming && !BinguinConcert.Active)
            {
                wasPerforming = false;
                // ★ 2026-10-18：自办演出的收尾（只给"正在看"的人发心情 + 放贵宾回家）。
                //   必须在 GrantGoodwill 之前/之后都行，这里先收尾再结算好感。
                BinguinConcertDirector.EndPerformance(performingBooking);
                // ★ 2026-10-17：演唱会结束后给来访派系加好感
                BinguinConcertDirector.GrantGoodwill(performingBooking);
                performingBooking = null;
                // ★ musicPlaylist 故意不清：歌单要在演唱会之后继续放完
                lastConcertEndTick = GenTicks.TicksGame;
                BinguinLogUtility.Log("演唱会结束，5 天冷却开始。", isDebug: false);
            }
            else if (BinguinConcert.Active)
            {
                wasPerforming = true;
            }

            // ★ 2026-10-17 修 bug：演出中被别的节日抢走 ⇒ 每 250 tick 重新派奏乐 job
            // ★ 2026-10-17：歌单独立于演出状态 —— 散场后也要把剩下的歌放完
            if (musicPlaylist != null)
            {
                BinguinConcertDirector.TickMusic(musicPlaylist);
                if (BinguinConcertDirector.IsMusicFinished(musicPlaylist))
                {
                    BinguinLogUtility.Log("演唱会歌单已全部播完，恢复常规背景音乐。",
                        isDebug: false);
                    musicPlaylist = null;
                }
            }

            if (BinguinConcert.Active && performingBooking != null
                && (GenTicks.TicksGame % 250) == 0)
            {
                BinguinConcertDirector.KeepPerformersPlaying(performingBooking, performingBooking.Map);
                // ★ 2026-10-18 新增：观众也要一直待在麦克风前面看
                //   （原来只在开演那一刻派一次，被别的活抢走就再也不回来了）
                BinguinConcertDirector.KeepAudienceWatching(performingBooking, performingBooking.Map);
            }

            BinguinConcertBooking b = booking;
            if (b == null)
            {
                return;
            }

            // ★ 2026-10-11 修「主唱没有到自己的位置」：
            //   等待开演期间，每 250 tick 重新把人叫回麦克风/乐器旁。
            if (b.awaitingStart
                && GenTicks.TicksGame - b.assembledTick > 0
                && (GenTicks.TicksGame % 250) == 0)
            {
                // ★ 2026-10-17：不只演出者，**观众也要在麦克风前游荡**
                BinguinConcertDirector.KeepCrowdInPlace(b, b.Map);
            }

            // ① 主唱没了 ⇒ 取消（作者明确要求的唯一取消条件）
            if (!b.LeadSingerUsable)
            {
                string who = b.leadSinger != null ? b.leadSinger.LabelShort : "（失踪）";
                Messages.Message("Binguin_ConcertSetup_LeadLost".Translate(),
                    MessageTypeDefOf.NegativeEvent);
                Cancel("主唱 " + who + " 已无法登台");
                return;
            }

            // ② 到点 ⇒ 【人员到场】+ 弹信封，等玩家主动开演
            //    ★ 2026-10-11 作者要求：
            //      「一次性放完就行，但是开始之前需要弹个信封显示人员到场，
            //        然后再由玩家主动开始演唱会，
            //        如果超过 6 小时都不开始的话，演唱会自动取消」
            if (!b.awaitingStart && b.TicksUntilStart <= 0)
            {
                BinguinConcertDirector.AssembleOnSite(b, b.Map);
                b.awaitingStart = true;
                b.assembledTick = GenTicks.TicksGame;
                SendArrivedLetter(b);
                return;
            }

            // ③ 已到场但玩家 6 小时没开演 ⇒ 取消
            if (b.awaitingStart && b.AssembledHours > 6f)
            {
                Messages.Message("Binguin_ConcertSetup_TimeoutCancelled".Translate(),
                    MessageTypeDefOf.NegativeEvent);
                BinguinConcertDirector.SendEveryoneHome(b, b.Map);
                Cancel("到场后 6 小时未开演");
            }
        }

        /// <summary>「演唱会准备就绪」信封。</summary>
        private void SendArrivedLetter(BinguinConcertBooking b)
        {
            try
            {
                string songList = "";
                if (b.songs != null)
                {
                    for (int i = 0; i < b.songs.Count; i++)
                    {
                        if (b.songs[i] == null) continue;
                        if (songList.Length > 0) songList += "、";
                        songList += b.songs[i].label;
                    }
                }
                Pawn singer = b.leadSinger;
                string text = "Binguin_ConcertArrived_Text".Translate(
                    singer != null ? singer.LabelShort : "？",
                    b.musicians != null ? b.musicians.Count.ToString() : "0",
                    songList.NullOrEmpty() ? "-" : songList);
                ChoiceLetter letter = LetterMaker.MakeLetter(
                    "Binguin_ConcertArrived_Title".Translate(), text,
                    LetterDefOf.PositiveEvent);
                Find.LetterStack.ReceiveLetter(letter);
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("发送到场信件失败：" + e.Message, severity: 1, isDebug: false);
            }
        }

        /// <summary>到点开演：交给运行期的 `BinguinConcert` + `BinguinConcertDirector`。</summary>
        /// <summary>玩家主动开演（由麦克风 gizmo 调用）。</summary>
        public void StartNow(BinguinConcertBooking b)
        {
            Map map = b.Map;
            if (map == null)
            {
                Cancel("找不到预定的地图");
                return;
            }
            // ★ 2026-10-17 作者：「开始演唱会时不用弹仪式质量消息」⇒ 只计算、不弹消息。
            //   质量仍会在仪式界面的“效果乘数”里显示。
            b.quality = BinguinConcertQuality.Compute(b, map);
            // ★ 2026-10-18：四档是**按概率掷**的，开演时掷一次并写进存档
            b.outcomeTier = BinguinConcertQuality.RollTier(b.quality);
            BinguinLogUtility.Log("本场演出质量 " + (b.quality * 100f).ToString("0")
                + "% ⇒ 掷出档位【" + BinguinConcertQuality.TierKey(b.outcomeTier).Translate()
                + "】（心情 " + BinguinConcertQuality.MoodOffsetText(b.outcomeTier)
                + "，来访好感 +" + BinguinConcertQuality.GoodwillOf(b.quality) + "）",
                isDebug: false);

            // ★ 2026-10-17 作者：「演唱会时间是固定的4小时」
            //   曲目只是为了让玩家开 1 倍速时能听到四首歌，**不影响时长**。
            performingBooking = b;
            musicPlaylist = b;          // ★ 歌单开始：散场后会继续放完
            BinguinConcert.Start(BinguinConcert.DurationTicks);
            BinguinConcertDirector.BeginPerformance(b, map);
            booking = null;   // 预约转成"运行中"，由 Director 接手
        }
    }
}
