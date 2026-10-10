// ============================================================================
// 合成钓竿对话框 —— 按材质精确选择 4 个配件 + 指定制作人
//
// 布局：
//   竿身 / 竿头 / 鱼钩 / 鱼饵 四行（材质标注，点击选择）；
//   制作人一行（殖民者列表）；底部提示 4 个高级零部件需求；
//   「开始装配」→ CompBinguinRodAssembly.StartAssembly 派制作 job
//   （殖民者到装配台工作 → 完成后按技能生成品质并产出鱼竿）。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin.Feature.Rods
{
    public class Dialog_BinguinRodAssembly : Window
    {
        private Thing table;
        private Thing selectedShaft;
        private Thing selectedTip;
        private Thing selectedHook;
        private Thing selectedBait;
        private Pawn selectedWorker;

        private const float RowH = 52f;
        private const float ItemH = 46f;

        public Dialog_BinguinRodAssembly(Thing assemblyTable)
        {
            table = assemblyTable;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            forcePause = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(580f, 560f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "Binguin_DialogRodAssembly_01".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 38f;
            DrawPartRow(inRect, y, "Binguin_DialogRodAssembly_02".Translate(), "Binguin_RodShaft_Long", "Binguin_RodShaft_Short", ref selectedShaft);
            y += RowH + 6f;
            DrawPartRow(inRect, y, "Binguin_DialogRodAssembly_03".Translate(), "Binguin_RodTip_Blade", "Binguin_RodTip_Hammer", ref selectedTip);
            y += RowH + 6f;
            DrawPartRow(inRect, y, "Binguin_DialogRodAssembly_04".Translate(), "Binguin_RodHook_Straight", "Binguin_RodHook_Curved", ref selectedHook);
            y += RowH + 6f;
            DrawPartRow(inRect, y, "Binguin_DialogRodAssembly_05".Translate(), "Binguin_RodBait", null, ref selectedBait);
            y += RowH + 6f;

            // 制作人选择（★ 显示所有自由殖民者，不强制 Crafting 已开启——
            //   用户反馈"无法选择"是因为过滤太严；默认选中第一个）
            List<Pawn> workers = AvailableWorkers();
            if (selectedWorker == null && workers.Count > 0)
            {
                selectedWorker = workers[0];
            }
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), "Binguin_DialogRodAssembly_06".Translate());
            float wx = inRect.x;
            foreach (Pawn p in workers)
            {
                Rect r = new Rect(wx, y + 24f, 120f, ItemH - 4f);
                bool isSel = selectedWorker == p;
                if (isSel)
                {
                    Widgets.DrawRectFast(r, new Color(0.4f, 0.7f, 1f, 0.35f), null);
                }
                Widgets.DrawHighlightIfMouseover(r);
                string skill = p.skills != null && p.skills.GetSkill(SkillDefOf.Crafting) != null
                    ? "Binguin_DialogRodAssembly_07".Translate() + p.skills.GetSkill(SkillDefOf.Crafting).Level + "）"
                    : "";
                Widgets.Label(new Rect(r.x + 4f, r.y + 3f, r.width - 8f, 20f), p.LabelShort + skill);
                if (Widgets.ButtonInvisible(r))
                {
                    selectedWorker = p;
                }
                wx += 126f;
            }
            y += RowH + 6f;

            // 高级零部件需求
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f),
                "Binguin_DialogRodAssembly_08".Translate()
                + AvailableComponents().ToString("0") + "）");

            // 属性预览
            Text.Font = GameFont.Small;
            string preview = BuildPreview();
            Rect previewRect = new Rect(inRect.x, y + 26f, inRect.width, 110f);
            Widgets.DrawBox(previewRect, 1, null);
            Widgets.Label(previewRect, preview);

            // 装配按钮
            Rect btnRect = new Rect(inRect.x + inRect.width - 160f, inRect.y + inRect.height - 34f, 150f, 30f);
            bool canStart = selectedShaft != null && selectedTip != null && selectedHook != null && selectedWorker != null && AvailableComponents() >= 4;
            if (!canStart)
            {
                Widgets.DrawBox(btnRect, 1, null);
                Text.Anchor = TextAnchor.MiddleCenter;
                string need = selectedWorker == null ? "Binguin_DialogRodAssembly_09".Translate() : "Binguin_DialogRodAssembly_10".Translate();
                Widgets.Label(btnRect, need);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else if (Widgets.ButtonText(btnRect, "Binguin_DialogRodAssembly_11".Translate(), true, false, true))
            {
                StartAssembly();
            }
        }

        private List<Pawn> AvailableWorkers()
        {
            List<Pawn> result = new List<Pawn>();
            foreach (Pawn p in table.Map.mapPawns.FreeColonists)
            {
                if (p == null || p.Dead || p.Downed) continue;
                if (p.skills == null) continue;
                result.Add(p);
            }
            return result;
        }

        private float AvailableComponents()
        {
            float count = 0f;
            foreach (Thing t in table.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (t != null && t.def != null && t.def.defName == "ComponentSpacer")
                {
                    count += t.stackCount;
                }
            }
            return count;
        }

        private void DrawPartRow(Rect inRect, float y, string label, string defA, string defB, ref Thing selected)
        {
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), label);
            float x = inRect.x;
            List<Thing> items = AvailableThings(defA, defB);
            for (int i = 0; i < items.Count && x + 150f <= inRect.x + inRect.width; i++)
            {
                Thing item = items[i];
                Rect r = new Rect(x, y + 22f, 146f, ItemH);
                bool isSel = selected == item;
                if (isSel)
                {
                    Widgets.DrawRectFast(r, new Color(0.4f, 0.7f, 1f, 0.35f), null);
                }
                Widgets.DrawHighlightIfMouseover(r);
                string stuffLabel = item.Stuff != null ? item.Stuff.label : "";
                Widgets.Label(new Rect(r.x + 4f, r.y + 3f, r.width - 8f, 20f), item.def.label + (stuffLabel != "" ? "（" + stuffLabel + "）" : ""));
                Widgets.Label(new Rect(r.x + 4f, r.y + 23f, r.width - 8f, 20f), "×" + item.stackCount);
                if (Widgets.ButtonInvisible(r))
                {
                    selected = item;
                }
                x += 152f;
            }
            if (items.Count == 0)
            {
                Widgets.Label(new Rect(x, y + 22f, 200f, ItemH), "Binguin_DialogRodAssembly_12".Translate());
            }
        }

        private List<Thing> AvailableThings(string defA, string defB)
        {
            List<Thing> result = new List<Thing>();
            if (table == null || table.Map == null) return result;
            foreach (Thing t in table.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (t == null || t.def == null) continue;
                if (t.def.defName == defA || (defB != null && t.def.defName == defB))
                {
                    result.Add(t);
                }
            }
            return result;
        }

        private string BuildPreview()
        {
            if (selectedShaft == null || selectedTip == null || selectedHook == null)
            {
                return "Binguin_DialogRodAssembly_13".Translate();
            }
            bool longShaft = selectedShaft.def.defName == BinguinRodUtility.ShaftLong;
            bool bladeTip = selectedTip.def.defName == BinguinRodUtility.TipBlade;
            bool straightHook = selectedHook.def.defName == BinguinRodUtility.HookStraight;
            ThingDef shaftStuff = selectedShaft.Stuff;
            ThingDef tipStuff = selectedTip.Stuff;
            ThingDef hookStuff = selectedHook.Stuff;

            float dmgMult = 1f;
            if (longShaft) dmgMult *= 1.2f;
            dmgMult *= BinguinRodUtility.DamageMultOf(tipStuff, bladeTip);
            float cdMult = 1f;
            if (!longShaft) cdMult *= 0.8f;
            cdMult *= BinguinRodUtility.CooldownMultOf(shaftStuff);
            float durMult = BinguinRodUtility.DurabilityMultOf(hookStuff);

            string baitLine = "";
            CompBinguinRodBait baitComp = selectedBait != null ? selectedBait.TryGetComp<CompBinguinRodBait>() : null;
            if (baitComp != null && baitComp.effects != null && baitComp.effects.Count > 0)
            {
                List<string> bs = new List<string>();
                for (int i = 0; i < baitComp.effects.Count; i++)
                {
                    bs.Add(BinguinRodUtility.EffectLabel(baitComp.effects[i]));
                }
                baitLine = "Binguin_DialogRodAssembly_14".Translate() + string.Join("+", bs.ToArray());
            }

            string skillLine = straightHook
                ? "Binguin_DialogRodAssembly_15".Translate()
                : "Binguin_DialogRodAssembly_16".Translate();

            if (bladeTip)
            {
                return "Binguin_DialogRodAssembly_17".Translate() + dmgMult.ToString("0.00") + "Binguin_DialogRodAssembly_18".Translate()
                    + "Binguin_DialogRodAssembly_19".Translate() + cdMult.ToString("0.00") + "（1.6s/1.2s）"
                    + "Binguin_DialogRodAssembly_20".Translate() + durMult.ToString("0.00")
                    + skillLine + baitLine;
            }
            return "Binguin_DialogRodAssembly_21".Translate() + dmgMult.ToString("0.00") + "Binguin_DialogRodAssembly_22".Translate()
                + "Binguin_DialogRodAssembly_23".Translate() + cdMult.ToString("0.00") + "（2.0s/1.6s）"
                + "Binguin_DialogRodAssembly_24".Translate() + durMult.ToString("0.00")
                + skillLine + baitLine;
        }

        private void StartAssembly()
        {
            if (table == null || table.Map == null) return;
            CompBinguinRodAssembly comp = table.TryGetComp<CompBinguinRodAssembly>();
            if (comp == null) return;
            if (comp.StartAssembly(selectedWorker, selectedShaft, selectedTip, selectedHook, selectedBait))
            {
                Close();
            }
        }
    }
}
