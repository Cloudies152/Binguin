// ============================================================================
// 蟹笼「管理鱼群」对话框（2026-08-20 建，2026-10-06 按用户反馈重做）
//
// ★★ 2026-10-06 用户反馈原文：
//    「我发现了一个问题，蟹笼的管理鱼群应该是**限制捕捞数量**
//      （控制仓库鱼类上限或者水域内数量），你写成直接改变鱼群数量了。」
//    进一步澄清：「我指的是**限制整个水域的鱼类下限**，比如到某个数字就不捕捞，
//                **默认为中值**」/ 滑条或加减按钮调 /
//                原来那套直接改鱼群数量「保留，但作为高级/调试功能」。
//
// ⇒ 本窗口重做成两块：
//    **主功能**：调「本水域捕捞下限」——滑条 + 加减按钮 + 「重置为中值」。
//               鱼群 ≤ 下限时，本水域的**所有蟹笼**都停止捕捞
//               （判定在 `CompBinguinCrabTrap.CatchOnce`，
//                存储在新文件 `WaterBodyLimits_Binguin.cs`）。
//    **折叠区（高级/调试）**：原来那套直接改 `WaterBody.Population` 的
//               +/-1/10/100、充满、放空 —— 默认收起，点标题才展开。
//
// ★ 为什么下限要按"水域"存而不是按"蟹笼"存：
//   用户要的是"整个水域"的下限 ⇒ 同一片水域里的多个蟹笼必须共享一个值。
//   存单个蟹笼身上就变成各管各的。存储细节见 `MapComponent_BinguinWaterBodyLimits`。
//
// ★ WaterBody.Population 有 public setter（IL 实证），高级区直接赋值即可；
//   赋值范围自行 clamp 到 [0, MaxPopulation]。
// ============================================================================

using RimWorld;
using UnityEngine;
using Verse;

using Binguin.Feature.Rods;

namespace Binguin.Feature.Fishing
{
    // ★ 2026-09：本类有 static 资源字段（Texture2D），RimWorld 启动时会警告
    //   「probably needs a StaticConstructorOnStartup attribute ... must be loaded in the main thread」。
    //   加上该特性 → 静态构造在主线程启动时执行，警告消除、资源加载时机也正确。
    [StaticConstructorOnStartup]
    public class Dialog_BinguinManageFish : Window
    {
        private WaterBody body;

        /// <summary>高级/调试区是否展开（默认收起）。</summary>
        private bool showAdvanced;

        public Dialog_BinguinManageFish(WaterBody targetBody)
        {
            body = targetBody;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            forcePause = true;
        }

        // ★ 2026-09 性能：原来每帧都 new 一张 1×1 纯色贴图（60 张/秒，
        //   全是 GC 垃圾）——它是常量，缓存一份即可。
        private static Texture2D popBarTex;
        private static Texture2D limitBarTex;

        private static Texture2D PopBarTex
        {
            get
            {
                if (popBarTex == null)
                {
                    popBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.25f, 0.55f, 0.95f));
                }
                return popBarTex;
            }
        }

        /// <summary>下限标记用的红色（比鱼群条深一点，便于分辨）。</summary>
        private static Texture2D LimitBarTex
        {
            get
            {
                if (limitBarTex == null)
                {
                    limitBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.90f, 0.35f, 0.25f));
                }
                return limitBarTex;
            }
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(480f, 340f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
                "Binguin_DialogManageFish_01".Translate());
            Text.Font = GameFont.Small;

            float pop = body.Population;
            float max = body.MaxPopulation;
            if (max < 1f)
            {
                max = 1f;
            }
            float limit = MapComponent_BinguinWaterBodyLimits.GetLimit(body);
            bool custom = MapComponent_BinguinWaterBodyLimits.HasExplicitLimit(body);

            float y = inRect.y + 42f;

            // ---------------- 鱼群现状 ----------------
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 30f),
                "Binguin_DialogManageFish_02".Translate() + pop.ToString("0") + "  /  " + max.ToString("0"));
            Text.Font = GameFont.Small;
            y += 36f;

            // 鱼群比例条（蓝）+ 下限刻度（红）
            Rect barRect = new Rect(inRect.x, y, inRect.width, 18f);
            Widgets.FillableBar(barRect, Mathf.Clamp01(pop / max), PopBarTex);
            float limitFrac = Mathf.Clamp01(limit / max);
            // 下限刻度：一小条红色竖标记
            Rect markRect = new Rect(barRect.x + barRect.width * limitFrac - 1.5f, barRect.y - 2f, 3f, barRect.height + 4f);
            Widgets.DrawBoxSolid(markRect, new Color(0.90f, 0.35f, 0.25f));
            y += 26f;

            // ---------------- 主功能：捕捞下限 ----------------
            // ★ `Translate()` 是 `TaggedString`：跟 string 混在拼接/三元里会 CS0172，
            //   所以先各自 `.ToString()` 再拼。
            string limitLine = "Binguin_DialogManageFish_07".Translate().ToString()
                + limit.ToString("0")
                + (custom ? "" : "Binguin_DialogManageFish_08".Translate().ToString());
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), limitLine);
            y += 24f;

            // 滑条（范围 0 ~ MaxPopulation）
            float newLimit = Widgets.HorizontalSlider(
                new Rect(inRect.x, y, inRect.width - 150f, 24f),
                limit, 0f, max, true, null, null, null, -1f);
            if (Mathf.Abs(newLimit - limit) > 0.01f)
            {
                MapComponent_BinguinWaterBodyLimits.SetLimit(body, newLimit);
                limit = MapComponent_BinguinWaterBodyLimits.GetLimit(body);
            }

            // 加减按钮（用户要的"滑条/加减按钮"两种都给）
            float bx = inRect.x + inRect.width - 144f;
            if (Widgets.ButtonText(new Rect(bx, y, 44f, 24f), "-10"))
            {
                MapComponent_BinguinWaterBodyLimits.SetLimit(body, limit - 10f);
            }
            if (Widgets.ButtonText(new Rect(bx + 48f, y, 44f, 24f), "+10"))
            {
                MapComponent_BinguinWaterBodyLimits.SetLimit(body, limit + 10f);
            }
            if (Widgets.ButtonText(new Rect(bx + 96f, y, 48f, 24f), "Binguin_DialogManageFish_09".Translate()))
            {
                MapComponent_BinguinWaterBodyLimits.ResetLimit(body);
            }
            y += 32f;

            // ---------------- 说明 ----------------
            bool blocked = !MapComponent_BinguinWaterBodyLimits.CanCatchFrom(body);
            // ★ `Translate()` 返回 `TaggedString` 而不是 string ⇒
            //   在字符串拼接里用三元运算会 CS0172，必须显式 `.ToString()`。
            string statusKey = blocked
                ? "Binguin_DialogManageFish_10".Translate().ToString()
                : "Binguin_DialogManageFish_11".Translate().ToString();
            GUI.color = blocked ? new Color(1f, 0.6f, 0.45f) : Color.white;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f), statusKey);
            GUI.color = Color.white;
            y += 22f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 40f),
                "Binguin_DialogManageFish_06".Translate());
            y += 44f;

            // ---------------- 高级 / 调试区（默认收起）----------------
            Rect advRect = new Rect(inRect.x, y, inRect.width, 24f);
            if (Widgets.ButtonText(advRect,
                (showAdvanced ? "▼ " : "▶ ") + "Binguin_DialogManageFish_12".Translate()))
            {
                showAdvanced = !showAdvanced;
            }
            y += 28f;

            if (showAdvanced)
            {
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                    "Binguin_DialogManageFish_03".Translate());
                y += 22f;

                float btnW = 62f;
                float btnH = 30f;
                float x = inRect.x;
                float step = btnW + 6f;

                if (Widgets.ButtonText(new Rect(x, y, btnW, btnH), "-100"))
                {
                    SetPopulation(body.Population - 100f);
                }
                x += step;
                if (Widgets.ButtonText(new Rect(x, y, btnW, btnH), "-10"))
                {
                    SetPopulation(body.Population - 10f);
                }
                x += step;
                if (Widgets.ButtonText(new Rect(x, y, btnW, btnH), "-1"))
                {
                    SetPopulation(body.Population - 1f);
                }
                x += step;
                if (Widgets.ButtonText(new Rect(x, y, btnW, btnH), "+1"))
                {
                    SetPopulation(body.Population + 1f);
                }
                x += step;
                if (Widgets.ButtonText(new Rect(x, y, btnW, btnH), "+10"))
                {
                    SetPopulation(body.Population + 10f);
                }
                x += step;
                if (Widgets.ButtonText(new Rect(x, y, btnW, btnH), "+100"))
                {
                    SetPopulation(body.Population + 100f);
                }
                y += btnH + 8f;

                if (Widgets.ButtonText(new Rect(inRect.x, y, 116f, 30f),
                    "Binguin_DialogManageFish_04".Translate()))
                {
                    SetPopulation(body.MaxPopulation);
                }
                if (Widgets.ButtonText(new Rect(inRect.x + 122f, y, 116f, 30f),
                    "Binguin_DialogManageFish_05".Translate()))
                {
                    SetPopulation(0f);
                }
            }
        }

        /// <summary>高级区：直接改水域鱼群（原功能，保留）。</summary>
        private void SetPopulation(float value)
        {
            float max = body.MaxPopulation;
            if (max < 1f)
            {
                max = 1f;
            }
            body.Population = Mathf.Clamp(value, 0f, max);
        }
    }
}
