// ============================================================================
// 制作鱼饵对话框 —— 选择 1~2 种食材效果，消耗对应食物生成鱼饵
//
// 每种效果消耗 20 营养的对应食物（素=植物食品、肉=普通肉、虫肉=虫肉、
// 扭曲肉=扭曲肉、人肉=人肉、虫胶=虫胶、无忌口=任意食物）。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin.Feature.Rods
{
    public class Dialog_BinguinBait : Window
    {
        private Thing table;
        private BinguinBaitEffect effect1 = BinguinBaitEffect.None;
        private BinguinBaitEffect effect2 = BinguinBaitEffect.None;

        private const float NutritionPerEffect = 1f;    // 每效果消耗 1 营养（2026-08-19 用户调低）

        private static readonly BinguinBaitEffect[] AllEffects = new BinguinBaitEffect[]
        {
            BinguinBaitEffect.Vegetable,
            BinguinBaitEffect.Meat,
            BinguinBaitEffect.InsectMeat,
            BinguinBaitEffect.TwistedMeat,
            BinguinBaitEffect.HumanMeat,
            BinguinBaitEffect.Jelly,
            BinguinBaitEffect.Omnivorous
        };

        public Dialog_BinguinBait(Thing assemblyTable)
        {
            table = assemblyTable;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            forcePause = true;
        }

        // ★ 2026-09 性能：本对话框 forcePause 且每帧重绘，而 AvailableNutritionFor
        //   要遍历全图可搬运物。原来每帧最多算 4 次（摘要 2 次 + 按钮 2 次）→
        //   按 (效果, 库存是否变化) 缓存，且每 30 tick 或选择变化时才重算。
        private float avail1 = -1f;
        private float avail2 = -1f;
        private BinguinBaitEffect availKey1 = BinguinBaitEffect.None;
        private BinguinBaitEffect availKey2 = BinguinBaitEffect.None;
        private int availCalcTick = -1000;

        private float AvailableFor(BinguinBaitEffect e)
        {
            if (e == BinguinBaitEffect.None)
            {
                return 0f;
            }
            bool stale = GenTicks.TicksGame - availCalcTick > 30;
            if (stale || availKey1 != effect1 || availKey2 != effect2)
            {
                availCalcTick = GenTicks.TicksGame;
                availKey1 = effect1;
                availKey2 = effect2;
                avail1 = effect1 == BinguinBaitEffect.None ? 0f
                    : BinguinRodUtility.AvailableNutritionFor(table.Map, effect1);
                avail2 = effect2 == BinguinBaitEffect.None ? 0f
                    : BinguinRodUtility.AvailableNutritionFor(table.Map, effect2);
            }
            return e == effect1 ? avail1 : avail2;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(480f, 420f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "Binguin_DialogBait_01".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, 24f),
                "Binguin_DialogBait_02".Translate() + NutritionPerEffect.ToString("0.#") + "Binguin_DialogBait_03".Translate());

            float y = inRect.y + 66f;
            DrawEffectSelector(inRect, y, "Binguin_DialogBait_04".Translate(), ref effect1);
            y += 92f;
            DrawEffectSelector(inRect, y, "Binguin_DialogBait_05".Translate(), ref effect2);
            y += 92f;

            // 消耗预览
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 60f), BuildSummary());

            // 制作按钮
            Rect btnRect = new Rect(inRect.x + inRect.width - 140f, inRect.y + inRect.height - 34f, 130f, 30f);
            bool canMake = effect1 != BinguinBaitEffect.None
                && AvailableFor(effect1) >= NutritionPerEffect - 0.01f
                && (effect2 == BinguinBaitEffect.None
                    || AvailableFor(effect2) >= NutritionPerEffect - 0.01f);
            if (!canMake)
            {
                Widgets.DrawBox(btnRect, 1, null);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(btnRect, "Binguin_DialogBait_06".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else if (Widgets.ButtonText(btnRect, "Binguin_DialogBait_07".Translate(), true, false, true))
            {
                MakeBait();
            }
        }

        private void DrawEffectSelector(Rect inRect, float y, string label, ref BinguinBaitEffect current)
        {
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), label);
            float x = inRect.x;
            for (int i = 0; i < AllEffects.Length; i++)
            {
                BinguinBaitEffect e = AllEffects[i];
                Rect r = new Rect(x, y + 24f, 64f, 48f);
                bool isSel = current == e;
                if (isSel)
                {
                    Widgets.DrawRectFast(r, new Color(0.4f, 0.7f, 1f, 0.35f), null);
                }
                Widgets.DrawHighlightIfMouseover(r);
                Widgets.Label(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, 20f), BinguinRodUtility.EffectLabel(e));
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(r.x + 2f, r.y + 20f, r.width - 4f, 26f), BinguinRodUtility.EffectDesc(e));
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(r))
                {
                    current = current == e ? BinguinBaitEffect.None : e;
                }
                x += 66f;
            }
            // 清除按钮
            Rect clear = new Rect(x, y + 24f, 64f, 48f);
            if (Widgets.ButtonText(clear, "Binguin_DialogBait_08".Translate()))
            {
                current = BinguinBaitEffect.None;
            }
        }

        private string BuildSummary()
        {
            // ★ 2026-09：改用缓存后的可用营养（原来两处各扫一次全图）
            List<string> parts = new List<string>();
            if (effect1 != BinguinBaitEffect.None)
            {
                parts.Add(BinguinRodUtility.EffectLabel(effect1) + "Binguin_DialogBait_09".Translate()
                    + AvailableFor(effect1).ToString("0.0")
                    + " / " + NutritionPerEffect.ToString("0") + "Binguin_DialogBait_10".Translate());
            }
            if (effect2 != BinguinBaitEffect.None)
            {
                parts.Add(BinguinRodUtility.EffectLabel(effect2) + "Binguin_DialogBait_11".Translate()
                    + AvailableFor(effect2).ToString("0.0")
                    + " / " + NutritionPerEffect.ToString("0") + "Binguin_DialogBait_12".Translate());
            }
            if (parts.Count == 0)
            {
                return "Binguin_DialogBait_13".Translate();
            }
            return "Binguin_DialogBait_14".Translate() + string.Join(" + ", parts);
        }

        private void MakeBait()
        {
            Map map = table.Map;
            if (map == null) return;
            ThingDef baitDef = DefDatabase<ThingDef>.GetNamedSilentFail(BinguinRodUtility.Bait);
            if (baitDef == null) return;

            List<BinguinBaitEffect> effects = new List<BinguinBaitEffect>();
            if (effect1 != BinguinBaitEffect.None)
            {
                float c1 = BinguinRodUtility.ConsumeNutritionFor(map, effect1, NutritionPerEffect);
                if (c1 > 0.01f) effects.Add(effect1);
            }
            if (effect2 != BinguinBaitEffect.None)
            {
                float c2 = BinguinRodUtility.ConsumeNutritionFor(map, effect2, NutritionPerEffect);
                if (c2 > 0.01f) effects.Add(effect2);
            }
            if (effects.Count == 0)
            {
                Messages.Message("Binguin_DialogBait_15".Translate(), table, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Thing bait = ThingMaker.MakeThing(baitDef);
            CompBinguinRodBait comp = bait.TryGetComp<CompBinguinRodBait>();
            if (comp != null)
            {
                comp.effects = effects;
            }
            GenPlace.TryPlaceThing(bait, table.Position, map, ThingPlaceMode.Near, null);
            Messages.Message("Binguin_DialogBait_16".Translate(), bait, MessageTypeDefOf.PositiveEvent, true);
            Close();
        }
    }
}
