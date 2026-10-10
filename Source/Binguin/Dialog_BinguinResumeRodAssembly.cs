// ============================================================================
// 【钓竿（未完成）】· 续接对话框 —— 选一位殖民者继续装配
//
// 入口：点选半成品 → 右键命令「继续装配」（见 `CompBinguinUnfinishedRod.CompGetGizmosExtra`）。
// 作用：把"中断后换人接着干"变成一步操作。
//
// ★ 画法沿用本 mod 早已稳定可用的那套"殖民者按钮列表"
//   （`Dialog_BinguinRodAssembly` / `Dialog_BinguinRodWorkerPick`），
//   **不用 `Find.Targeter`** —— 那套重载陷阱太多，详见
//   `RodPartPicker_Binguin.BeginWorkerPick` 上面那段三次踩坑记录。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin
{
    public class Dialog_BinguinResumeRodAssembly : Window
    {
        private readonly Thing rod;
        private Pawn selectedWorker;

        private const float RowH = 46f;

        public Dialog_BinguinResumeRodAssembly(Thing unfinishedRod)
        {
            rod = unfinishedRod;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
            forcePause = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(560f, 340f); }
        }

        private CompBinguinUnfinishedRod Comp
        {
            get { return rod != null ? rod.TryGetComp<CompBinguinUnfinishedRod>() : null; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "继续装配钓竿");
            Text.Font = GameFont.Small;

            float y = inRect.y + 40f;
            CompBinguinUnfinishedRod comp = Comp;
            if (comp == null)
            {
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "这件物品没有半成品数据，无法装配。");
                return;
            }

            // ---- 当前状态 ----
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), "当前配件：" + comp.PartsSummary());
            y += 24f;
            float pct = comp.workDone / comp.WorkAmountBase;
            if (pct < 0f) pct = 0f;
            if (pct > 1f) pct = 1f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                "已完成工作量：" + comp.workDone.ToString("0") + " / "
                + comp.WorkAmountBase.ToString("0")
                + "（" + (pct * 100f).ToString("0") + "%）");
            y += 30f;

            // ---- 殖民者列表 ----
            List<Pawn> workers = AvailableWorkers();
            if (selectedWorker == null && workers.Count > 0)
            {
                selectedWorker = workers[0];
            }
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), "由谁来继续装配：");
            y += 24f;

            if (workers.Count == 0)
            {
                Widgets.Label(new Rect(inRect.x + 6f, y, inRect.width - 12f, 24f),
                    "没有可以来装配的殖民者（全部倒地 / 死亡）。");
            }
            else
            {
                // ★ 2026-10-06 同选人窗口：去掉手工技能后缀、按钮加宽，保证名字显示完整
                const float BtnW = 172f;
                const float BtnH = 32f;
                const float GapX = 8f;
                const float GapY = 6f;
                float wx = inRect.x;
                float wy = y;
                foreach (Pawn p in workers)
                {
                    if (wx + BtnW > inRect.x + inRect.width)
                    {
                        wx = inRect.x;
                        wy += BtnH + GapY;
                    }
                    Rect r = new Rect(wx, wy, BtnW, BtnH);
                    if (selectedWorker == p)
                    {
                        Widgets.DrawRectFast(r, new Color(0.4f, 0.7f, 1f, 0.35f), null);
                    }
                    Widgets.DrawHighlightIfMouseover(r);
                    Widgets.Label(new Rect(r.x + 6f, r.y + 6f, r.width - 12f, 22f), p.LabelShort);
                    if (Widgets.ButtonInvisible(r))
                    {
                        selectedWorker = p;
                    }
                    wx += BtnW + GapX;
                }
            }

            // ---- 底部按钮 ----
            Rect btnRect = new Rect(inRect.x + inRect.width - 160f, inRect.y + inRect.height - 34f, 150f, 30f);
            bool canStart = selectedWorker != null && rod != null && !rod.Destroyed && comp.HasAllParts;
            if (canStart)
            {
                if (Widgets.ButtonText(btnRect, "继续装配"))
                {
                    Pawn w = selectedWorker;
                    Close(true);
                    bool ok = CompBinguinRodAssembly.StartWorkOnRod(w, rod);
                    Log.Message("[冰鹅族] 续接装配：派 job " + (ok ? "成功" : "失败") + "（" + w.LabelShort + "）");
                    if (!ok)
                    {
                        Messages.Message("Binguin_ResumeRod_NoJobDef".Translate(),
                            rod, MessageTypeDefOf.RejectInput, false);
                    }
                }
            }
            else
            {
                Widgets.DrawBox(btnRect, 1, null);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(btnRect, "继续装配");
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        /// <summary>
        /// 能来继续装配的殖民者。
        /// ★ 故意**不**用 `p.skills == null` 排除（原对话框里有这一句，
        ///   冰鹅族是 HAR 异种人，万一技能系统异常会导致列表为空 ⇒ 玩法卡死）。
        /// </summary>
        private List<Pawn> AvailableWorkers()
        {
            List<Pawn> result = new List<Pawn>();
            if (rod == null || rod.Map == null)
            {
                return result;
            }
            foreach (Pawn p in rod.Map.mapPawns.FreeColonists)
            {
                if (p == null || p.Dead || p.Downed) continue;
                result.Add(p);
            }
            return result;
        }
    }
}
