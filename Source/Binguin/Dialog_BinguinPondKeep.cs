// ============================================================================
// 保留鱼数对话框（2026-08-20 用户需求）
// 设置鱼池组的"保留下限"：鱼群 ≤ 该值时停止捕捞（钓鱼/蟹笼模块），
// 让鱼繁殖恢复。范围 0 ~ 组容量（20×池数）。
// ============================================================================

using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin
{
    public class Dialog_BinguinPondKeep : Window
    {
        private CompBinguinFishPond pond;

        public Dialog_BinguinPondKeep(CompBinguinFishPond pondComp)
        {
            pond = pondComp;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            forcePause = true;
        }

        private float capCache = -1f;

        public override Vector2 InitialSize
        {
            get { return new Vector2(380f, 230f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            CompBinguinFishPond lead = pond != null ? pond.LeaderComp : null;
            if (lead == null)
            {
                return;
            }
            // ★ 2026-09 性能：GroupCapacity() 是一次全组 BFS，而对话框每帧重绘
            //   （forcePause 期间地图不变）→ 只算一次。
            if (capCache < 0f)
            {
                capCache = lead.GroupCapacity();
            }
            float cap = capCache;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "Binguin_DialogPondKeep_01".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 40f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 26f),
                "Binguin_DialogPondKeep_02".Translate() + lead.keepMinFish.ToString("0") + "Binguin_DialogPondKeep_03".Translate() + cap.ToString("0") + "）");
            y += 32f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f),
                "Binguin_DialogPondKeep_04".Translate());
            y += 46f;

            float btnW = 62f;
            float btnH = 34f;
            float x = inRect.x;
            float step = btnW + 8f;

            if (Widgets.ButtonText(new Rect(x, y, btnW, btnH), "-10")) { lead.keepMinFish = Mathf.Max(0f, lead.keepMinFish - 10f); }
            x += step;
            if (Widgets.ButtonText(new Rect(x, y, btnW, btnH), "-1")) { lead.keepMinFish = Mathf.Max(0f, lead.keepMinFish - 1f); }
            x += step;
            if (Widgets.ButtonText(new Rect(x, y, btnW, btnH), "+1")) { lead.keepMinFish = Mathf.Min(cap, lead.keepMinFish + 1f); }
            x += step;
            if (Widgets.ButtonText(new Rect(x, y, btnW, btnH), "+10")) { lead.keepMinFish = Mathf.Min(cap, lead.keepMinFish + 10f); }
            y += btnH + 12f;

            if (Widgets.ButtonText(new Rect(inRect.x, y, 120f, 32f), "Binguin_DialogPondKeep_05".Translate())) { lead.keepMinFish = 0f; }
            if (Widgets.ButtonText(new Rect(inRect.x + 130f, y, 160f, 32f), "Binguin_DialogPondKeep_06".Translate() + Mathf.FloorToInt(cap * 0.5f).ToString("0") + "）"))
            {
                lead.keepMinFish = Mathf.Floor(cap * 0.5f);
            }
        }
    }
}
