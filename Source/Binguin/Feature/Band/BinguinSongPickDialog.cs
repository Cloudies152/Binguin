// ============================================================================
// 冰鹅族【自办演唱会】· 选曲窗口（2026-10-17 作者要求）
//
// ★ 作者要求：「选曲窗口在选人后吧」
//   ⇒ 流程：点麦克风 → 原版仪式窗口选人 → **本窗口选曲（最多 4 首）**
//            → 选 1~7 天后 → 登记预约
//
// ★ 曲目来源：`BinguinConcertSongs.All`（DefDatabase<Binguin_SongDef>）
//   玩家可以往 `Defs/Features/Band/Concerts_Songs_Binguin.xml` 里加歌。
// ★ 上限 4 首，按点击顺序演出。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin.Feature.Band
{
    /// <summary>选曲：最多 4 首，按选择顺序演出。</summary>
    public class Dialog_BinguinSongPick : Window
    {
        public const int MaxSongs = 4;

        private readonly List<Binguin_SongDef> chosen = new List<Binguin_SongDef>();
        private readonly System.Action<List<Binguin_SongDef>> onConfirm;
        private Vector2 scroll;
        private readonly List<Binguin_SongDef> all;

        public Dialog_BinguinSongPick(List<Binguin_SongDef> preChosen,
            System.Action<List<Binguin_SongDef>> onConfirm)
        {
            this.onConfirm = onConfirm;
            all = BinguinConcertSongs.All;
            if (preChosen != null) chosen.AddRange(preChosen);
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            forcePause = true;
        }

        public override Vector2 InitialSize { get { return new Vector2(560f, 520f); } }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "Binguin_SongPick_Title".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 32f, inRect.width, 22f),
                "Binguin_SongPick_Desc".Translate(chosen.Count.ToString(), MaxSongs.ToString()));

            float top = 58f;
            Rect outer = new Rect(0f, top, inRect.width, inRect.height - top - 52f);
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
                        "Binguin_Interface_049".Translate().ToString() + s.requiredArtisticLevel);
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

            float by = inRect.height - 44f;
            bool can = chosen.Count > 0;
            if (!can) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(0f, by, 200f, 34f),
                "Binguin_SongPick_Confirm".Translate()) && can)
            {
                if (onConfirm != null) onConfirm(chosen);
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
