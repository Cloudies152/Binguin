// ============================================================================
// 装配钓竿 · 第 5 步「选谁来装配」窗口（2026-10-05 新增）
//
// ★★ 为什么用窗口而不是地图点选（重要，别再改回 Find.Targeter）：
//   本来这一步是"在地图上点一位殖民者"，用 `Find.Targeter.BeginTargeting`。
//   但那条路踩了三次坑、且都不容易看出来：
//     ① 起初在 targeting 的 `actionWhenFinished` 回调里直接开下一个 targeter
//        ⇒ `Targeter.StopTargeting()` 的 IL 是"先调回调、再清字段"，
//          刚设好的 targetParams/action/targetValidator 会被立刻清空 ⇒ 没圆圈。
//     ② 改用 6 参数 `BeginTargeting` ⇒ 它不接受 validator 参数；
//     ③ 改用 5 参数重载 ⇒ 它的 IL_0048-004A 直接把 `Targeter.targetValidator`
//        置成 null，而 `Targeter.ProcessInputEvents` 在 `targetingSource == null` 时
//        判的正是这个字段（不是 targetParams.validator）
//        ⇒ 写在 tp.validator 里的判定根本不生效。
//   而本文件夹里**原本就有一个稳定可用的选人 UI**（`Dialog_BinguinRodAssembly`
//   的制作人列表），你之前用那个对话框成功装配过。
//   ⇒ 第 5 步直接复用同一套画法做成一个精简窗口，不再碰 Targeter。
//
// ★ 流程：4 件配件点完后弹本窗口 → 点一位殖民者 → 「开始装配」
//         → `CompBinguinRodAssembly.StartAssembly` 派 Binguin_AssembleRod
//         → 小人开始搬配件（见 JobDriver_BinguinAssembleRod）。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin
{
    public class Dialog_BinguinRodWorkerPick : Window
    {
        private readonly Thing table;
        private readonly Thing shaft;
        private readonly Thing tip;
        private readonly Thing hook;
        private readonly Thing bait;

        private Pawn selectedWorker;
        private bool committed;

        private const float RowH = 46f;

        public Dialog_BinguinRodWorkerPick(Thing assemblyTable, Thing partShaft, Thing partTip,
            Thing partHook, Thing partBait)
        {
            table = assemblyTable;
            shaft = partShaft;
            tip = partTip;
            hook = partHook;
            bait = partBait;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
            forcePause = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(560f, 320f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f),
                "Binguin_RodPick_10".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 40f;

            // ---- 已选好的 4 件配件（让玩家确认没选错）----
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                "Binguin_RodPick_12".Translate());
            y += 24f;
            Widgets.Label(new Rect(inRect.x + 6f, y, inRect.width - 12f, 22f),
                Label(shaft) + " / " + Label(tip) + " / " + Label(hook) + " / " + Label(bait));
            y += RowH;

            // ---- 殖民者列表（★ 沿用原对话框那套画法）----
            List<Pawn> workers = AvailableWorkers();
            if (selectedWorker == null && workers.Count > 0)
            {
                selectedWorker = workers[0];
            }

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                "Binguin_DialogRodAssembly_06".Translate());
            y += 24f;

            if (workers.Count == 0)
            {
                Widgets.Label(new Rect(inRect.x + 6f, y, inRect.width - 12f, 24f),
                    "Binguin_RodPick_13".Translate());
            }
            else
            {
                // ★★ 2026-10-06 用户反馈「选人阶段显示不全，就不用显示手工技能了」：
                //   ① **去掉手工技能后缀**（原来 126px 的按钮里塞"名字+（制作 N）"会被截断）
                //   ② 按钮加宽到 172px，名字完整显示
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
                    // 只显示名字（不拼技能），保证显示完整
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
            bool canStart = selectedWorker != null && table != null && !table.Destroyed;
            if (canStart)
            {
                if (Widgets.ButtonText(btnRect, "Binguin_RodPick_14".Translate()))
                {
                    Pawn w = selectedWorker;
                    // ★ 先把 committed 置位：紧接着的 `Close(true)` 会触发 `PostClose`
                    //   → `NotifyWorkerDialogClosed()`，那里靠这个标记区分
                    //   "玩家取消了" vs "已经派活了"，避免刚派完 job 就被当成取消。
                    committed = true;
                    Commit(w);
                    Close(true);
                }
            }
            else
            {
                Widgets.DrawBox(btnRect, 1, null);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(btnRect, "Binguin_RodPick_14".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        /// <summary>
        /// 关窗口时收口。`committed` 为真说明是"点了开始装配"引起的关闭，
        /// 不能当成取消（那样会把刚派下去的 job 状态清掉）。
        /// </summary>
        public override void PostClose()
        {
            base.PostClose();
            if (!committed)
            {
                BinguinRodPartPicker.NotifyWorkerDialogClosed();
            }
        }

        private void Commit(Pawn worker)
        {
            if (table == null || worker == null)
            {
                BinguinRodPartPicker.NotifyWorkerDialogCommitted();//参数不全复位
                return;
            }
            CompBinguinRodAssembly comp = table.TryGetComp<CompBinguinRodAssembly>();
            if (comp == null)
            {
                Log.Warning("[冰鹅族] 选谁装配：装配台上没有 CompBinguinRodAssembly，已放弃。");
                BinguinRodPartPicker.NotifyWorkerDialogCommitted();//依旧复位
                return;
            }
            bool ok = comp.StartAssembly(worker, shaft, tip, hook, bait);
            Log.Message("[冰鹅族] 选谁装配：StartAssembly 返回 " + ok + "（" + worker.LabelShort + "）");
            if (ok)
            {
                // ★ 成功派活后清掉 picker 的会话状态，否则静态 table/picked 一直留着，
                //   下次点装配台会被 "正在点选配件中" 拦住（等于只能装配一次）。
                BinguinRodPartPicker.NotifyWorkerDialogCommitted();
            }
            else
            {
                Messages.Message("Binguin_RodPick_15".Translate(), MessageTypeDefOf.RejectInput, false);
                BinguinRodPartPicker.NotifyWorkerDialogCommitted();//同是复位
            }
        }

        private static string Label(Thing t)
        {
            return t == null ? "（无）" : t.LabelShort;
        }

        /// <summary>
        /// 能来装配的殖民者。
        /// ★ 故意**不**用 `p.skills == null` 排除（原对话框里有这一句，
        ///   冰鹅族是 HAR 异种人，万一技能系统异常会导致"谁都不在列表里"⇒ 玩法卡死）。
        /// </summary>
        private List<Pawn> AvailableWorkers()
        {
            List<Pawn> result = new List<Pawn>();
            if (table == null || table.Map == null)
            {
                return result;
            }
            foreach (Pawn p in table.Map.mapPawns.FreeColonists)
            {
                if (p == null || p.Dead || p.Downed) continue;
                result.Add(p);
            }
            return result;
        }
    }
}
