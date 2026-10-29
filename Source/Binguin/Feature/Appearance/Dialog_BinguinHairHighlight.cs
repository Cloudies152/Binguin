using System;
using UnityEngine;
using Verse;

namespace Binguin.Feature.Appearance
{
    /// <summary>
    /// 挑染颜色选择窗口（2026-10-29）。
    /// 只改数据（GameComponent 里的 entry），渲染由 Patch_BinguinHairHighlightDraw 负责。
    /// </summary>
    public class Dialog_BinguinHairHighlight : Window
    {
        private Pawn pawn;

        public Dialog_BinguinHairHighlight(Pawn p)
        {
            pawn = p;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(380f, 330f); }
        }

        private static readonly Color[] Presets =
        {
            new Color(0.93f, 0.36f, 0.36f),   // 珊瑚红
            new Color(0.96f, 0.66f, 0.30f),   // 橙
            new Color(0.95f, 0.85f, 0.35f),   // 金
            new Color(0.45f, 0.80f, 0.45f),   // 绿
            new Color(0.40f, 0.66f, 0.95f),   // 蓝
            new Color(0.66f, 0.48f, 0.92f),   // 紫
            new Color(0.92f, 0.92f, 0.92f),   // 白
            new Color(0.18f, 0.18f, 0.20f)    // 黑
        };

        public override void DoWindowContents(Rect inRect)
        {
            GameComponent_BinguinHairHighlight gc = GameComponent_BinguinHairHighlight.Instance;
            if (gc == null || pawn == null || pawn.Dead)
            {
                return;
            }
            BinguinHairHighlightEntry e = gc.Get(pawn, true);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
                "Binguin_HairHighlight_Title".Translate().ToString());
            Text.Font = GameFont.Small;

            float y = inRect.y + 40f;
            Widgets.CheckboxLabeled(new Rect(inRect.x, y, inRect.width, 26f),
                "Binguin_HairHighlight_Enable".Translate().ToString(), ref e.enabled);
            y += 32f;

            float w = inRect.width - 96f;
            e.r = Widgets.HorizontalSlider(new Rect(inRect.x + 20f, y, w, 22f), e.r, 0f, 1f, false,
                "Binguin_HairHighlight_Red".Translate().ToString(), null, null, -1f);
            y += 28f;
            e.g = Widgets.HorizontalSlider(new Rect(inRect.x + 20f, y, w, 22f), e.g, 0f, 1f, false,
                "Binguin_HairHighlight_Green".Translate().ToString(), null, null, -1f);
            y += 28f;
            e.b = Widgets.HorizontalSlider(new Rect(inRect.x + 20f, y, w, 22f), e.b, 0f, 1f, false,
                "Binguin_HairHighlight_Blue".Translate().ToString(), null, null, -1f);
            y += 32f;

            // 预览色块
            Rect swatch = new Rect(inRect.x, y, 120f, 28f);
            Widgets.DrawBoxSolid(swatch, e.Color);
            Widgets.DrawBox(swatch, 1);
            Widgets.Label(new Rect(swatch.xMax + 8f, y + 4f, inRect.width - 130f, 24f),
                "Binguin_HairHighlight_Preview".Translate().ToString());
            y += 36f;

            // 预设色
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                "Binguin_HairHighlight_Presets".Translate().ToString());
            y += 24f;
            float bw = 34f;
            for (int i = 0; i < Presets.Length; i++)
            {
                Rect r = new Rect(inRect.x + i * (bw + 6f), y, bw, 26f);
                Widgets.DrawBoxSolid(r, Presets[i]);
                Widgets.DrawBox(r, 1);
                if (Widgets.ButtonInvisible(r))
                {
                    e.r = Presets[i].r;
                    e.g = Presets[i].g;
                    e.b = Presets[i].b;
                }
            }
            y += 34f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 40f),
                "Binguin_HairHighlight_Hint".Translate().ToString());
        }
    }
}
