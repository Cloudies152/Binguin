using System;
using LudeonTK;
using RimWorld;
using Verse;
using Binguin.Helper;

namespace Binguin.Feature.SkillCounter
{
    /// <summary>
    /// 内心的呐喊（技能对抗）
    /// ============================================================
    /// 全图效果：地图上所有敌对 / 发狂的中立单位撤退（见 ForcedRetreat_Binguin）。
    ///
    /// 设计要点：**没有施法者**。
    ///   有些 mod 会「反向击杀释放技能的单位」，所以本技能不由任何 Pawn 释放，
    ///   而是由「世界本身」触发（GameComponent / 事件 / 脚本调用），
    ///   因此不存在可以被反制、反杀、打断的施法单位。
    ///
    /// 冷却：15 天（900,000 tick），只由本组件记录，不依赖任何 Def 或 Comp。
    /// </summary>
    public static class InnerCry_Binguin
    {
        /// <summary>技能名。</summary>
        public const string Label = "内心的呐喊";

        /// <summary>冷却：15 天。</summary>
        public const int CooldownTicks = 900000;

        /// <summary>当前是否可用。</summary>
        public static bool IsReady
        {
            get
            {
                GameComponent_BinguinInnerCry comp = GameComponent_BinguinInnerCry.Instance;
                return comp != null && comp.IsReady;
            }
        }

        /// <summary>剩余冷却 tick（已可用时为 0）。</summary>
        public static int RemainingTicks
        {
            get
            {
                GameComponent_BinguinInnerCry comp = GameComponent_BinguinInnerCry.Instance;
                return comp == null ? 0 : comp.RemainingTicks;
            }
        }

        /// <summary>
        /// 触发「内心的呐喊」：全图敌对单位撤退。
        /// 没有施法者——调用方可以是事件、任务、UI 按钮或脚本。
        /// </summary>
        /// <param name="ignoreCooldown">true 时忽略冷却（调试 / 剧情用）。</param>
        /// <returns>受影响地图数；冷却中返回 -1。</returns>
        public static int Trigger(bool ignoreCooldown = false)
        {
            GameComponent_BinguinInnerCry comp = GameComponent_BinguinInnerCry.Instance;
            if (comp == null)
            {
                BinguinLogUtility.WarningOnce("内心的呐喊：找不到状态组件，无法触发。", 962230);
                return -1;
            }

            return comp.Trigger(ignoreCooldown);
        }
    }

    /// <summary>
    /// 「内心的呐喊」状态组件：记录冷却 + 执行效果。
    /// RimWorld 反射自动注册 GameComponent，因此不需要任何 Def / XML。
    /// </summary>
    public class GameComponent_BinguinInnerCry : GameComponent
    {
        /// <summary>上次触发时的 tick（存档保存）。</summary>
        public int lastUsedTick = -999999;

        /// <summary>默认从未使用。</summary>
        private const int NeverUsed = -999999;

        public GameComponent_BinguinInnerCry(Game game)
            : base()
        {
        }

        public static GameComponent_BinguinInnerCry Instance
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game.GetComponent<GameComponent_BinguinInnerCry>();
            }
        }

        /// <summary>是否可用。</summary>
        public bool IsReady
        {
            get
            {
                if (lastUsedTick <= NeverUsed + 1)
                {
                    return true;
                }

                return Find.TickManager.TicksGame - lastUsedTick >= InnerCry_Binguin.CooldownTicks;
            }
        }

        /// <summary>剩余冷却 tick。</summary>
        public int RemainingTicks
        {
            get
            {
                if (IsReady)
                {
                    return 0;
                }

                return InnerCry_Binguin.CooldownTicks - (Find.TickManager.TicksGame - lastUsedTick);
            }
        }

        /// <summary>执行技能：对每张地图发动强制撤退。</summary>
        public int Trigger(bool ignoreCooldown)
        {
            if (!ignoreCooldown && !IsReady)
            {
                return -1;
            }

            lastUsedTick = Find.TickManager.TicksGame;

            int maps = 0;
            int affected = 0;
            System.Collections.Generic.List<Map> all = Find.Maps;
            for (int i = 0; i < all.Count; i++)
            {
                int n = ForcedRetreat_Binguin.RetreatAll(all[i]);
                if (n > 0)
                {
                    affected += n;
                    maps++;
                }
            }

            BinguinLogUtility.Log(
                "内心的呐喊：已发动（" + maps + " 张地图 / " + affected + " 个单位），冷却 "
                + (InnerCry_Binguin.CooldownTicks / 60000) + " 天。", isDebug: false);

            Messages.Message(
                InnerCry_Binguin.Label + "：全图的敌意被压了下去。",
                MessageTypeDefOf.NeutralEvent);

            return maps;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref lastUsedTick, "binguinInnerCryLastTick", NeverUsed);
        }
    }

    /// <summary>调试入口：直接触发技能（无施法者）。</summary>
    public static class InnerCry_Debug
    {
        [DebugAction("冰鹅族", "技能对抗：内心的呐喊（忽略冷却）",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_Trigger()
        {
            int n = InnerCry_Binguin.Trigger(true);
            if (n >= 0)
            {
                Messages.Message("内心的呐喊已发动（" + n + " 张地图）。", MessageTypeDefOf.NeutralEvent);
            }
            else
            {
                Messages.Message("内心的呐喊：触发失败。", MessageTypeDefOf.RejectInput);
            }
        }

        [DebugAction("冰鹅族", "技能对抗：内心的呐喊 冷却状态",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_Status()
        {
            int remain = InnerCry_Binguin.RemainingTicks;
            string text = remain <= 0
                ? "内心的呐喊：可用。"
                : "内心的呐喊：冷却中，剩余 " + (remain / 60000f).ToString("0.0") + " 天。";
            Messages.Message(text, MessageTypeDefOf.NeutralEvent);
        }
    }
}
