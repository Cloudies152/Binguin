// ============================================================================
// 冰鹅族【自办演唱会】· 选曲 + 选日期窗口（2026-10-17 作者要求；2026-10-18 合并日期）
//
// ★ 作者要求（2026-10-17）：「选曲窗口在选人后吧」
// ★ 作者要求（2026-10-18）：「把选择多少天后开始的选项和选择歌曲的页面放到一起」
//   ⇒ 现在一个窗口里同时选：**曲目（最多 4 首）+ 1~7 天后**，
//     确认后直接登记预约（不再弹第二个 FloatMenu 选天数）。
//
// 流程：点麦克风 → 原版仪式窗口选人 → **本窗口（选曲 + 选日期）** → 登记预约
//
// ★ 曲目来源：`BinguinConcertSongs.All`（DefDatabase<Binguin_SongDef>）
//   玩家可以往 `Defs/Feature/Band/Concerts_Songs_Binguin.xml` 里加歌。
// ★ 上限 4 首，按点击顺序演出。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin.Feature.Band
{
    /// <summary>选曲 + 选日期：最多 4 首（按选择顺序演出），1~7 天后开演。</summary>
    public class Dialog_BinguinSongPick : Window
    {
        public const int MaxSongs = 4;
        /// <summary>最远可以定到几天后（与 `BinguinConcertBooking` 的夹取范围一致）。</summary>
        public const int MaxDays = 7;

        private readonly List<Binguin_SongDef> chosen = new List<Binguin_SongDef>();
        private readonly System.Action<List<Binguin_SongDef>, int> onConfirm;
        private Vector2 scroll;
        private readonly List<Binguin_SongDef> all;
        private int days = 1;

        public Dialog_BinguinSongPick(List<Binguin_SongDef> preChosen,
            System.Action<List<Binguin_SongDef>, int> onConfirm, int preDays = 1)
        {
            this.onConfirm = onConfirm;
            all = BinguinConcertSongs.All;
            if (preChosen != null) chosen.AddRange(preChosen);
            if (preDays >= 1 && preDays <= MaxDays) days = preDays;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            forcePause = true;
        }

        public override Vector2 InitialSize { get { return new Vector2(580f, 560f); } }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "Binguin_SongPick_Title".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 32f, inRect.width, 22f),
                "Binguin_SongPick_Desc".Translate(chosen.Count.ToString(), MaxSongs.ToString()));

            // ★ 底部预留：天数行（34）+ 按钮行（44）
            const float bottomH = 96f;
            float top = 58f;
            Rect outer = new Rect(0f, top, inRect.width, inRect.height - top - bottomH);
            Widgets.DrawMenuSection(outer);
            float rowH = 46f;
            Rect view = new Rect(0f, 0f, outer.width - 22f, Mathf.Max(outer.height - 8f, all.Count * rowH + 6f));
            Widgets.BeginScrollView(
                new Rect(outer.x + 4f, outer.y + 4f, outer.width - 8f, outer.height - 8f),
                ref scroll, view);
            for (int i = 0; i < all.Count; i++)
            {
                Binguin_SongDef s = all[i];
                if (s == null) continue;
                bool on = chosen.Contains(s);
                Rect row = new Rect(2f, i * rowH + 2f, view.width - 4f, rowH - 4f);
                if (on) Widgets.DrawHighlightSelected(row);
                else Widgets.DrawHighlightIfMouseover(row);

                Widgets.Label(new Rect(row.x + 6f, row.y + 3f, row.width - 12f, 20f),
                    (on ? "☑ " : "☐ ") + s.label);
                // ★ 2026-10-17 作者：「选歌时候不要显示小时」「下面也不需要备注抒情什么的」
                //   演唱会时长固定 4 小时，曲目只影响听到什么。
                //   所以下面只保留最低艺术等级提示（有用），不显示风格/时长。
                if (s.requiredArtisticLevel > 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.6f);
                    Widgets.Label(new Rect(row.x + 18f, row.y + 22f, row.width - 24f, 20f),
                        "需艺术 " + s.requiredArtisticLevel);
                    GUI.color = Color.white;
                }

                if (Widgets.ButtonInvisible(row))
                {
                    if (on) chosen.Remove(s);
                    else if (chosen.Count < MaxSongs) chosen.Add(s);
                    else Messages.Message("Binguin_SongPick_Full".Translate(MaxSongs.ToString()),
                        MessageTypeDefOf.RejectInput, false);
                }
            }
            Widgets.EndScrollView();

            // ── ★ 2026-10-18：把「多少天后开演」并到这一页 ──
            float dy = inRect.height - bottomH + 6f;
            Widgets.Label(new Rect(0f, dy, 100f, 22f),
                "Binguin_ConcertSetup_Days".Translate());
            days = Mathf.RoundToInt(Widgets.HorizontalSlider(
                new Rect(105f, dy, 220f, 22f), days, 1f, MaxDays, true, null, "1", MaxDays.ToString(), 1f));
            Widgets.Label(new Rect(335f, dy, 200f, 22f),
                "Binguin_ConcertSetup_DaysValue".Translate(days.ToString()));

            float by = inRect.height - 44f;
            bool can = chosen.Count > 0;
            if (!can) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(0f, by, 200f, 34f),
                "Binguin_SongPick_Confirm".Translate()) && can)
            {
                if (onConfirm != null) onConfirm(chosen, days);
                Close();
            }
            GUI.color = Color.white;
            if (Widgets.ButtonText(new Rect(208f, by, 140f, 34f),
                "Binguin_ConcertSetup_Cancel".Translate()))
            {
                Close();
            }
        }
    }
}
