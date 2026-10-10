// ============================================================================
// 放入鱼苗对话框（2026-08-20 v2）
// 列出殖民地库存中存在的各类鱼（Fish 类目）。点选一种后：
//   - 找一条该鱼（库存/地面 haulable）→ 指派最近殖民者执行
//     JobDriver_BinguinPondStock（搬运放入鱼池，非凭空消耗）
//   - 库存没有该鱼则提示（先钓/买）
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class Dialog_BinguinStockFish : Window
    {
        private CompBinguinFishPond pond;
        private List<ThingDef> availableFish = new List<ThingDef>();
        // ★ 2026-09 性能：对话框每帧重绘（forcePause 期间库存不会变）→
        //   每行要用的"名字/营养"字符串在 BuildList 时算好，绘制循环直接用。
        private List<string> fishLabels = new List<string>();
        private List<string> fishNutri = new List<string>();

        // ★ 2026-09 性能：搬运鱼苗 job def 名是常量 → 懒加载缓存
        private static JobDef cachedStockJobDef;

        private static JobDef StockJobDef
        {
            get
            {
                if (cachedStockJobDef == null)
                {
                    cachedStockJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_PondStock");
                }
                return cachedStockJobDef;
            }
        }

        public Dialog_BinguinStockFish(CompBinguinFishPond pondComp)
        {
            pond = pondComp;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            forcePause = true;
            BuildList();
        }

        private void BuildList()
        {
            availableFish.Clear();
            if (pond == null || pond.parent == null || pond.parent.Map == null)
            {
                return;
            }
            ThingCategoryDef fishCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Fish");
            if (fishCat == null)
            {
                return;
            }
            Map map = pond.parent.Map;
            HashSet<ThingDef> present = new HashSet<ThingDef>();
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (t == null || t.def == null || t.def.thingCategories == null)
                {
                    continue;
                }
                if (t.def.thingCategories.Contains(fishCat) && !present.Contains(t.def))
                {
                    present.Add(t.def);
                }
            }
            List<ThingDef> sorted = new List<ThingDef>(present);
            sorted.Sort(delegate (ThingDef a, ThingDef b) { return a.label.CompareTo(b.label); });
            availableFish = sorted;

            // ★ 2026-08-20：若组里已确定鱼种，只允许放同种（追加），
            //   避免选到异种导致 TryStock 拒绝
            CompBinguinFishPond lead = pond.LeaderComp;
            if (lead != null && !string.IsNullOrEmpty(lead.fishDefName))
            {
                for (int i = availableFish.Count - 1; i >= 0; i--)
                {
                    if (availableFish[i].defName != lead.fishDefName)
                    {
                        availableFish.RemoveAt(i);
                    }
                }
            }
            // 名字/营养字符串一次算好（LabelCap 本身有 def 级缓存，这里省掉的是
            // 每帧的营养 stat 查询与数字格式化）
            fishLabels.Clear();
            fishNutri.Clear();
            for (int i = 0; i < availableFish.Count; i++)
            {
                ThingDef f = availableFish[i];
                fishLabels.Add(f.LabelCap);
                fishNutri.Add("Binguin_DialogStockFish_01".Translate() + f.GetStatValueAbstract(StatDefOf.Nutrition, null).ToString("0.00"));
            }
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(460f, 440f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "Binguin_DialogStockFish_02".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y + 32f, inRect.width, 40f),
                "Binguin_DialogStockFish_03".Translate());

            float y = inRect.y + 78f;
            if (availableFish.Count == 0)
            {
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 50f),
                    "Binguin_DialogStockFish_04".Translate());
                return;
            }

            float btnW = (inRect.width - 16f) / 2f;
            float btnH = 46f;
            for (int i = 0; i < availableFish.Count; i++)
            {
                ThingDef f = availableFish[i];
                int col = i % 2;
                int row = i / 2;
                Rect r = new Rect(inRect.x + col * (btnW + 8f), y + row * (btnH + 6f), btnW, btnH);
                // 滚动：窗口只显示前 6 行
                if (row >= 6)
                {
                    break;
                }
                if (Mouse.IsOver(r))
                {
                    Widgets.DrawHighlight(r);
                }
                Widgets.DrawBox(r, 1, null);
                Widgets.Label(new Rect(r.x + 6f, r.y + 4f, r.width - 12f, 22f),
                    i < fishLabels.Count ? fishLabels[i] : (string)f.LabelCap);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(r.x + 6f, r.y + 26f, r.width - 12f, 18f),
                    i < fishNutri.Count ? fishNutri[i] : "");
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(r))
                {
                    StockFish(f);
                    return;
                }
            }
        }

        private void StockFish(ThingDef fishDef)
        {
            if (pond == null || pond.parent == null || pond.parent.Map == null)
            {
                return;
            }
            Map map = pond.parent.Map;

            // 找一条该鱼（库存/地面，可搬运）
            Thing fishThing = null;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (t != null && t.def == fishDef && !t.Destroyed)
                {
                    fishThing = t;
                    break;
                }
            }
            if (fishThing == null)
            {
                Messages.Message("Binguin_DialogStockFish_05".Translate() + fishDef.LabelCap + "Binguin_DialogStockFish_06".Translate(), pond.parent,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            // 找最近殖民者
            Pawn carrier = null;
            float best = float.MaxValue;
            for (int i = 0; i < map.mapPawns.FreeColonists.Count; i++)
            {
                Pawn p = map.mapPawns.FreeColonists[i];
                if (p.Downed || p.InMentalState || p.workSettings == null)
                {
                    continue;
                }
                float d = p.Position.DistanceTo(fishThing.Position);
                if (d < best)
                {
                    best = d;
                    carrier = p;
                }
            }
            if (carrier == null)
            {
                Messages.Message("Binguin_DialogStockFish_07".Translate(), pond.parent,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            JobDef jd = StockJobDef;
            if (jd == null)
            {
                return;
            }
            Job job = new Job(jd, fishThing, pond.parent);
            job.count = 1;
            carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            Close();
        }
    }
}
