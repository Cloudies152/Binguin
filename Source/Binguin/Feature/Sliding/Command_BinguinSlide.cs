// ============================================================================
// 滑行冲刺 —— 命令按钮（企鹅滑板装备按钮）
//
// 2026-08-19 v8（修复「点击后范围圈消失」）：
//   ★ 范围圈改为 GameComponent 每帧绘制：点击按钮 → StartSlideAim 标记
//     （GameComponent_BinguinDiplomacy.GameComponentUpdate 每帧画圈），
//     targeting 结束（选中/取消）→ actionWhenFinished 回调 StopSlideAim。
//     —— 不再依赖 Targeter 的 onGuiAction（实测在 1.6 不生效）。
//   ★ 悬停按钮时也画圈（GizmoUpdateOnMouseover）。
//   ★ 施法：点击目标 → action 回调 → CompBinguinBoard.TryStartSlide
//     （跳跃背包式瞬移：LOS 挡墙检查 + 直接位移，越过中间一切敌人）。
// ============================================================================

using System;
using UnityEngine;
using Verse;

using Binguin.Feature.Diplomacy;

using Binguin.Helper;

namespace Binguin.Feature.Sliding
{
    public class Command_BinguinSlide : Command
    {
        public Pawn caster;
        public float range = 10f;
        public Action<LocalTargetInfo> onTargetSelected;

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            BinguinLogUtility.Log("滑行冲刺：命令被点击，进入目标选择。");
            if (caster == null || caster.Destroyed || !caster.Spawned || caster.Map == null)
            {
                return;
            }
            RimWorld.TargetingParameters tp = new RimWorld.TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetSelf = false
            };
            // 开启 GameComponent 每帧画圈（targeting 全程可见）
            GameComponent_BinguinDiplomacy.StartSlideAim(caster, range);
            // 6 参重载：action = 选中目标回调；actionWhenFinished = targeting 结束回调（清圈）
            Find.Targeter.BeginTargeting(tp,
                delegate (LocalTargetInfo target)
                {
                    BinguinLogUtility.Log("滑行冲刺：目标已选择 " + target);
                    if (onTargetSelected != null)
                    {
                        onTargetSelected(target);
                    }
                },
                caster,
                delegate
                {
                    GameComponent_BinguinDiplomacy.StopSlideAim();
                },
                null,
                false);
        }

        // 鼠标悬停命令栏按钮时也画出可滑行范围圈
        public override void GizmoUpdateOnMouseover()
        {
            if (caster != null && caster.Spawned && caster.Map != null)
            {
                GenDraw.DrawRadiusRing(caster.Position, range, new Color(0.45f, 0.75f, 1f));
            }
        }
    }
}
