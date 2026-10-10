// ============================================================================
// 装配钓竿 · 【从库存选配件 + 选人】窗口（2026-10-06 新增）
//
// ★★ 这是给程序员改的那个界面 —— 需求原文：
//    「写一个拼装放置殖民地里有的配件和选人的简单ui界面」
//
// 现状（本文件就是"已经能用的参考实现"）：
//    一个窗口里列 4 个槽位（竿身 / 竿头 / 鱼钩 / 鱼饵），每行显示当前选中的那件，
//    点「更换」→ 弹出该槽位的候选列表（地图上所有该类配件，带材质名）；
//    下面一排按钮选装配的殖民者；底部「开始装配」→ 交给装配台 comp 派 job。
//
// ★ 与「地图点选」的关系（用户 2026-10-06 定的）：
//    **两种入口共存**，地图点选那条路原样保留（`RodPartPicker_Binguin.cs`）。
//    本窗口是新增的第二个入口，不动原来那套。
//
// ★ 打开方式：装配台选中后，命令栏有两个 gizmo
//    「合成钓竿（地图点选）」  ← 原有
//    「合成钓竿（从库存选）」  ← 本窗口（在 Comp_BinguinRodAssembly 里挂的）
//
// ★ 提交链路（和地图点选版完全一样，不要自己另写一套）：
//    `comp.StartAssembly(worker, shaft, tip, hook, bait)`
//      → 校验 4 个高级零部件 → 设置 pending → 派 JobDef `Binguin_AssembleRod`
//      → `JobDriver_BinguinAssembleRod` 搬配件 → 生成【钓竿（未完成）】
//    ⇒ 本窗口**只负责"选"**，"搬运/生成/干活"完全不用管。
//
// ★ 4 件必须是不同的东西：每个槽位的候选列表会排除已被其它槽位选走的那几件
//    （规则与地图点选版 `IsAllowed` 一致）。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Rods
{
    /// <summary>装配钓竿：一个窗口里选完 4 件配件 + 选装配者。</summary>
    public class Dialog_BinguinRodStockPick : Window
    {
        private readonly Thing table;

        /// <summary>4 个槽位当前选中的配件（下标与 `BinguinRodPartStock.Slots` 对应）。</summary>
        private readonly Thing[] picked;

        private Pawn selectedWorker;

        /// <summary>当前正在选哪个槽位（-1 = 没有在选）。</summary>
        private int pickingSlot = -1;

        /// <summary>是否已提交（区分"派完活了"和"玩家取消"）。</summary>
        private bool committed;

        private Vector2 slotScroll;
        private Vector2 workerScroll;

        private const float RowH = 34f;
        private const float BtnW = 96f;

        public Dialog_BinguinRodStockPick(Thing assemblyTable)
        {
            table = assemblyTable;
            picked = new Thing[BinguinRodPartStock.Slots.Length];
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
            forcePause = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(660f, 470f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // ================= 标题 =================
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f),
                "Binguin_RodStock_01".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 38f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                "Binguin_RodStock_02".Translate());
            y += 24f;

            // ================= 4 个槽位 =================
            // ★ 用 ScrollView：万一以后槽位变多（或者你想加更多字段）不会溢出
            Rect slotRect = new Rect(inRect.x, y, inRect.width, RowH * BinguinRodPartStock.Slots.Length + 6f);
            Rect slotView = new Rect(0f, 0f, slotRect.width - 20f, slotRect.height);
            Widgets.BeginScrollView(slotRect, ref slotScroll, slotView);

            float sy = 0f;
            for (int i = 0; i < BinguinRodPartStock.Slots.Length; i++)
            {
                BinguinRodPartStock.Slot slot = BinguinRodPartStock.Slots[i];
                Rect row = new Rect(0f, sy, slotView.width, RowH - 4f);

                // 槽位名（竿身/竿头/鱼钩/鱼饵）
                Widgets.Label(new Rect(row.x, row.y + 5f, 84f, 22f), slot.labelKey.Translate());

                // 当前选中项
                // ★ 注意 `Translate()` 返回的是 `TaggedString`（不是 string），
                //   直接和 string 做三元运算会 CS0172 ⇒ 显式转成 string。
                string cur = picked[i] == null
                    ? "Binguin_RodStock_04".Translate().ToString()      // （未选）
                    : BinguinRodPartStock.PartLabel(picked[i]);
                Widgets.Label(new Rect(row.x + 88f, row.y + 5f, row.width - 88f - BtnW - 8f, 22f), cur);

                // 「更换」按钮
                Rect btn = new Rect(row.x + row.width - BtnW, row.y + 2f, BtnW, 26f);
                string btnLabel = picked[i] == null
                    ? "Binguin_RodStock_05".Translate().ToString()      // 选择
                    : "Binguin_RodStock_06".Translate().ToString();     // 更换
                if (Widgets.ButtonText(btn, btnLabel))
                {
                    pickingSlot = i;
                    slotScroll = Vector2.zero;
                }

                // 鼠标悬停显示完整名字（名字长了会被截断）
                if (picked[i] != null)
                {
                    TooltipHandler.TipRegion(row, BinguinRodPartStock.PartLabel(picked[i]));
                }
                sy += RowH;
            }
            Widgets.EndScrollView();
            y += slotRect.height + 6f;

            // ---- 如果正在选某个槽位，就展开候选列表 ----
            if (pickingSlot >= 0)
            {
                y = DrawCandidateList(inRect, y);
            }

            // ================= 选装配者 =================
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                "Binguin_DialogRodAssembly_06".Translate());   // 「制作人」
            y += 24f;

            List<Pawn> workers = BinguinRodPartStock.FindWorkers(table);
            if (selectedWorker == null && workers.Count > 0)
            {
                selectedWorker = workers[0];
            }

            Rect wRect = new Rect(inRect.x, y, inRect.width, 62f);
            if (workers.Count == 0)
            {
                Widgets.Label(new Rect(inRect.x + 6f, y, inRect.width - 12f, 24f),
                    "Binguin_RodPick_13".Translate());          // 没有可以来装配的殖民者
            }
            else
            {
                Rect wView = new Rect(0f, 0f, wRect.width - 20f, 34f * ((workers.Count + 3) / 4) + 8f);
                Widgets.BeginScrollView(wRect, ref workerScroll, wView);
                const float WB = 148f;      // ★ 按钮要够宽，否则长名字被截断
                const float HB = 30f;
                const float GX = 8f;
                const float GY = 4f;
                float wx = 0f, wy = 0f;
                foreach (Pawn p in workers)
                {
                    if (wx + WB > wView.width)
                    {
                        wx = 0f;
                        wy += HB + GY;
                    }
                    Rect r = new Rect(wx, wy, WB, HB);
                    if (selectedWorker == p)
                    {
                        Widgets.DrawRectFast(r, new Color(0.4f, 0.7f, 1f, 0.35f), null);
                    }
                    Widgets.DrawHighlightIfMouseover(r);
                    Widgets.Label(new Rect(r.x + 6f, r.y + 5f, r.width - 12f, 22f), p.LabelShort);
                    if (Widgets.ButtonInvisible(r))
                    {
                        selectedWorker = p;
                    }
                    wx += WB + GX;
                }
                Widgets.EndScrollView();
            }
            y += wRect.height + 4f;

            // ---- 缺件提示 ----
            List<string> missing = MissingForCurrentPick();
            if (missing.Count > 0)
            {
                // ★ `Verse.Text` 没有 Color 字段（1.6），要用 `GUI.color`。
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.4f);
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                    "Binguin_RodStock_07".Translate(string.Join(" / ", missing.ToArray())));
                GUI.color = oldColor;
            }

            // ================= 底部按钮 =================
            float by = inRect.y + inRect.height - 34f;
            Rect cancelRect = new Rect(inRect.x + inRect.width - 320f, by, 150f, 30f);
            Rect okRect = new Rect(inRect.x + inRect.width - 160f, by, 150f, 30f);

            if (Widgets.ButtonText(cancelRect, "Binguin_RodStock_09".Translate()))
            {
                Close(true);
            }

            bool canStart = CanStart();
            if (canStart)
            {
                if (Widgets.ButtonText(okRect, "Binguin_RodStock_08".Translate()))
                {
                    Commit();
                }
            }
            else
            {
                Widgets.DrawBox(okRect, 1, null);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(okRect, "Binguin_RodStock_08".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        /// <summary>
        /// 展开"当前正在选的那个槽位"的候选列表。
        /// ★ 每个槽位的候选会**排除已被其它槽位选走的件**（4 件必须不同）。
        /// </summary>
        private float DrawCandidateList(Rect inRect, float y)
        {
            int slotIndex = pickingSlot;
            BinguinRodPartStock.Slot slot = BinguinRodPartStock.Slots[slotIndex];

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                "Binguin_RodStock_03".Translate(slot.labelKey.Translate()));
            y += 22f;

            // 排除已经在别的槽位里的件
            List<Thing> exclude = new List<Thing>();
            for (int i = 0; i < picked.Length; i++)
            {
                if (i != slotIndex && picked[i] != null)
                {
                    exclude.Add(picked[i]);
                }
            }
            Map map = table != null ? table.Map : null;
            List<Thing> candidates = BinguinRodPartStock.FindParts(map, slotIndex, exclude);

            float listH = 96f;
            Rect listRect = new Rect(inRect.x, y, inRect.width, listH);
            if (candidates.Count == 0)
            {
                Widgets.Label(new Rect(listRect.x + 6f, listRect.y + 4f, listRect.width - 12f, 22f),
                    "Binguin_RodStock_10".Translate());
            }
            else
            {
                const float CB = 190f;
                const float CH = 28f;
                const float GX = 8f;
                const float GY = 4f;
                int perRow = Mathf.Max(1, Mathf.FloorToInt((listRect.width - 20f) / (CB + GX)));
                int rows = (candidates.Count + perRow - 1) / perRow;
                Rect view = new Rect(0f, 0f, listRect.width - 20f, rows * (CH + GY) + 6f);
                Widgets.BeginScrollView(listRect, ref candScroll, view);
                float cx = 0f, cy = 0f;
                foreach (Thing t in candidates)
                {
                    if (cx + CB > view.width)
                    {
                        cx = 0f;
                        cy += CH + GY;
                    }
                    Rect r = new Rect(cx, cy, CB, CH);
                    Widgets.DrawHighlightIfMouseover(r);
                    // ★ 带材质名：同一种配件可能有铁/玻璃钢/铀…，必须能区分
                    Widgets.Label(new Rect(r.x + 6f, r.y + 4f, r.width - 12f, 22f),
                        BinguinRodPartStock.PartLabel(t));
                    if (Widgets.ButtonInvisible(r))
                    {
                        // 同一件 Thing 不能占两个槽位：
                        //   先把其它槽位里指向同一件的引用清掉，再赋给当前槽位。
                        for (int k = 0; k < picked.Length; k++)
                        {
                            if (k != slotIndex && picked[k] == t)
                            {
                                picked[k] = null;
                            }
                        }
                        picked[slotIndex] = t;
                        pickingSlot = -1;
                    }
                    cx += CB + GX;
                }
                Widgets.EndScrollView();
            }
            return y + listH + 6f;
        }

        private Vector2 candScroll;

        /// <summary>还没选/选不到的槽位名（用于提示）。</summary>
        private List<string> MissingForCurrentPick()
        {
            List<string> missing = new List<string>();
            for (int i = 0; i < picked.Length; i++)
            {
                if (picked[i] == null)
                {
                    missing.Add(BinguinRodPartStock.Slots[i].labelKey.Translate());
                }
            }
            return missing;
        }

        private bool CanStart()
        {
            if (table == null || table.Destroyed || !table.Spawned)
            {
                return false;
            }
            if (selectedWorker == null)
            {
                return false;
            }
            for (int i = 0; i < picked.Length; i++)
            {
                if (picked[i] == null || picked[i].Destroyed || !picked[i].Spawned)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 提交：把 4 件配件 + 选好的人交给装配台 comp。
        /// ★ 这一步与地图点选版**走的是同一个 API**（`StartAssembly`），
        ///   所以搬运 / 生成半成品 / 干活 的后续流程完全复用，不用在此重复。
        /// </summary>
        private void Commit()
        {
            CompBinguinRodAssembly comp = table.TryGetComp<CompBinguinRodAssembly>();
            if (comp == null)
            {
                BinguinLogUtility.Log("从库存选配件：装配台上没有 CompBinguinRodAssembly，已放弃。", severity: 1, isDebug: false);
                return;
            }
            bool ok = comp.StartAssembly(selectedWorker, picked[0], picked[1], picked[2], picked[3]);
            BinguinLogUtility.Log("从库存选配件：StartAssembly 返回 " + ok
                + "（" + (selectedWorker != null ? selectedWorker.LabelShort : "null") + "）");
            if (ok)
            {
                committed = true;
                Close(true);
            }
            else
            {
                Messages.Message("Binguin_RodPick_15".Translate(), MessageTypeDefOf.RejectInput, false);
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            // ★ 本窗口不占用 `BinguinRodPartPicker` 的任何状态（两者独立），
            //   所以这里不需要像 `Dialog_BinguinRodWorkerPick` 那样回调 picker。
            //   保留 PostClose 是为了将来加"取消时清理"的逻辑。
            if (!committed)
            {
                // 玩家取消了：什么都不用做（配件没动过，只是本地选择被丢弃）
            }
        }
    }
}
