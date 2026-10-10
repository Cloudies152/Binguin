// ============================================================================
// 冰鹅族【乐队来访】—— 正式任务条目 + 空投地标 + 超时判定（2026-09）
//
// 用户定稿：
//   · 走"方案 B"：C# 直接构造 Quest 实例（不写 QuestScriptDef），得到
//     任务栏里的正式条目 + 接受/拒绝。
//   · 接受时【空投地标】（DropPodUtility，本 mod 已在用，稳）。
//   · 主唱到场后：
//        第 4 小时 → 提示"该开始演唱会了"
//        第 6 小时仍未开演 → 任务失败
//   · 拆除建筑不会失败（本 mod 没有做任何拆除失败判定）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

using Binguin.Helper;

namespace Binguin.Feature.Band
{
    public static class BinguinBandQuest
    {
        public static Quest Current;

        /// <summary>发一个正式任务条目（出现在任务栏，带接受/拒绝）。</summary>
        public static Quest Offer()
        {
            try
            {
                Quest q = new Quest();
                q.name = "Binguin_BandQuest_Name".Translate();
                // ★ Quest.description 是 TaggedString
                q.description = "Binguin_BandQuest_Desc".Translate();
                q.points = 0f;
                q.hidden = false;
                // 打标签：供 GameComponent_BinguinBandMarkerDrop 轮询识别（不依赖静态字段）
                if (q.tags == null) q.tags = new List<string>();
                q.tags.Add(GameComponent_BinguinBandMarkerDrop.QuestTag);
                q.appearanceTick = Find.TickManager.TicksGame;
                q.acceptanceExpireTick = Find.TickManager.TicksGame + 15 * GenDate.TicksPerDay;
                q.SetNotYetAccepted();
                Find.QuestManager.Add(q);
                q.PostAdded();
                // ★ 2026-09 用户反馈：「任务栏显示正常，但没有任何弹窗」。
                //   原版任务发出时必定附一封「新任务」信件（LetterDefOf.NewQuest，
                //   letterClass = NewQuestLetter，IL 实证）：点它会跳到任务栏并选中该任务。
                //   我们之前只 QuestManager.Add，什么提示都没发 → 玩家只看到任务栏多一条。
                //   root == null 时原版会回退用 IncidentDefOf.GiveQuest_Random.letterDef
                //   （= NewQuest），两条取值路径都用 brtrue/brfalse 兜了 null，不会 NRE。
                QuestUtility.SendLetterQuestAvailable(q, null);
                Current = q;
                BinguinLogUtility.Log("已发出乐队来访任务条目。");
                return q;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("创建乐队任务失败，回退成普通信件：" + e, severity: 2, isDebug: false);
                Messages.Message("Binguin_BandQuest_Title".Translate(), MessageTypeDefOf.PositiveEvent);
                return null;
            }
        }

        /// <summary>由 Patch_BandQuestAccept（Quest.Accept 的 postfix）调用。</summary>
        public static void OnAccepted(Quest q)
        {
            try
            {
                Map map = Find.AnyPlayerHomeMap;
                if (map == null) return;
                // ★ 空投地标
                BinguinBandVisit.DropMarkerByPod(map);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("接受乐队任务后空投地标失败：" + e, severity: 2, isDebug: false);
            }
            GameComponent_BinguinBandScheduler comp =
                Verse.Current.Game.GetComponent<GameComponent_BinguinBandScheduler>();
            if (comp != null) comp.markerPlaced = true;
        }

        /// <summary>任务失败（主唱干等 6 小时没开演）。</summary>
        public static void Fail(string reason)
        {
            try
            {
                // 让乐队和观众走人
                BinguinBandConcert.EndConcert(Find.AnyPlayerHomeMap);
                if (Current != null && Current.State == QuestState.Ongoing)
                {
                    Current.End(QuestEndOutcome.Fail, true, true);
                }
                Current = null;
                Messages.Message(reason, MessageTypeDefOf.NegativeEvent);
                BinguinLogUtility.Log("乐队任务失败：" + reason);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("结束乐队任务失败：" + e, severity: 2, isDebug: false);
            }
        }

        /// <summary>任务成功（演唱会顺利结束）。</summary>
        public static void Succeed()
        {
            try
            {
                if (Current != null && Current.State == QuestState.Ongoing)
                {
                    Current.End(QuestEndOutcome.Success, true, true);
                }
                Current = null;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("结算乐队任务失败：" + e, severity: 2, isDebug: false);
            }
        }
    }

    /// <summary>玩家点"接受"时（Quest.Accept）→ 空投地标。</summary>
    [HarmonyPatch(typeof(Quest), "Accept")]
    public static class Patch_BandQuestAccept
    {
        public static void Postfix(Quest __instance)
        {
            try
            {
                if (__instance == null) return;
                if (BinguinBandQuest.Current != __instance) return;
                BinguinBandQuest.OnAccepted(__instance);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("接受乐队任务后处理失败：" + e, severity: 2, isDebug: false);
            }
        }
    }

    /// <summary>
    /// 轮询任务状态来空投地标。
    /// ★★ 原来靠 Quest.Accept 的 postfix，但 BinguinBandQuest.Current 是静态字段、
    ///    不存档 —— 只要"接到任务"和"点接受"之间存过一次档，钩子就永久失效
    ///    （用户实测「点击接受任务后依然没有空投地标」就是这个原因）。
    ///    改成每 250 tick 扫任务列表：看到带标记的任务处于 Ongoing 且还没投过，就投。
    /// </summary>
    public class GameComponent_BinguinBandMarkerDrop : GameComponent
    {
        public const string QuestTag = "BinguinBandVisit";
        private bool dropped;

        public GameComponent_BinguinBandMarkerDrop(Game game) : base()
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<bool>(ref dropped, "binguinBandMarkerDropped", false, false);
        }

        public override void GameComponentTick()
        {
            if (dropped) return;
            if (Find.TickManager == null) return;
            if (Find.TickManager.TicksGame % 250 != 0) return;
            try
            {
                if (!HasAcceptedQuest()) return;
                Map map = Find.AnyPlayerHomeMap;
                if (map == null) return;
                dropped = BinguinBandVisit.DropMarkerByPod(map) != null;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("轮询空投地标失败：" + e, severity: 2, isDebug: false);
            }
        }

        private static bool HasAcceptedQuest()
        {
            var list = Find.QuestManager.QuestsListForReading;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                Quest qq = list[i];
                if (qq == null || qq.tags == null) continue;
                if (qq.State != QuestState.Ongoing) continue;
                if (qq.tags.Contains(QuestTag)) return true;
            }
            return false;
        }
    }

    /// <summary>主唱到场的 4 小时提醒 / 6 小时失败。</summary>
    public class GameComponent_BinguinBandDeadline : GameComponent
    {
        private const int WarnTicks = 4 * 2500;    // 第 4 小时提醒
        private const int FailTicks = 6 * 2500;    // 第 6 小时失败

        private int arrivedTick = -1;
        private bool warned;

        public GameComponent_BinguinBandDeadline(Game game) : base()
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref arrivedTick, "binguinBandArrivedTick", -1, false);
            Scribe_Values.Look<bool>(ref warned, "binguinBandWarned", false, false);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager == null) return;
            if (Find.TickManager.TicksGame % 250 != 0) return;

            // 乐队到场那一刻开始计时
            if (BinguinBandConcert.bandArrived)
            {
                if (arrivedTick < 0)
                {
                    arrivedTick = Find.TickManager.TicksGame;
                    warned = false;
                }
            }
            else
            {
                arrivedTick = -1;
                warned = false;
                return;
            }

            if (BinguinConcert.Active) return;   // 已经开演了，不再计时

            int elapsed = Find.TickManager.TicksGame - arrivedTick;

            if (!warned && elapsed >= WarnTicks)
            {
                warned = true;
                Messages.Message("Binguin_BandQuest_Waiting".Translate(),
                    MessageTypeDefOf.CautionInput);
            }

            if (elapsed >= FailTicks)
            {
                arrivedTick = -1;
                warned = false;
                BinguinBandQuest.Fail("冰鹅族乐队干等了 6 个小时也没等到演出，收拾东西走了。");
            }
        }
    }

    /// <summary>
    /// ★★ 手搓任务（root == null）的致命坑。
    ///
    /// Verse 的 Quest.CleanupQuestParts() 最后一句是
    ///     if (root.hideOnCleanup) hiddenInUI = true;
    /// root 为 null 就是 NullReferenceException（IL 实证：IL_00B2 ldfld Quest::root）。
    /// 而 root 为 null 的任务一定会走到这里，共三条路径：
    ///     ① 发出后 15 天没人接受 → get_State() 返回 Expired → QuestManager 每 tick
    ///        调 QuestTick → CleanupQuestParts → 每 tick 一条红字（刷屏级）；
    ///     ② 我们的 Fail()/Succeed() 调 Quest.End()，End 内部第一件事就是
    ///        CleanupQuestParts → 异常在 cleanedUp = true 之前抛出
    ///        → 结算信件永远发不出去，而且之后每 tick 再炸一次；
    ///     ③ cleanedUp 卡在 false，QuestTick 会反复重试。
    ///
    /// 做法：只在 root 为 null 时，临时塞一个占位 QuestScriptDef 让原版整段逻辑跑完
    /// （占位 def 全部开关都是默认值，hideOnCleanup = false，行为与原版等价），
    /// 跑完立刻还原成 null。占位 def 只活在这一次同步调用里，绝不会被存档序列化到。
    /// root 非 null 的任务（全部原版任务）完全不受影响。
    /// </summary>
    [HarmonyPatch(typeof(Quest), "CleanupQuestParts")]
    public static class Patch_QuestCleanupNullRoot
    {
        private static QuestScriptDef placeholderRoot;

        public static void Prefix(Quest __instance)
        {
            try
            {
                if (__instance == null) return;
                if (__instance.root != null) return;
                if (placeholderRoot == null) placeholderRoot = new QuestScriptDef();
                __instance.root = placeholderRoot;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("任务清理占位 root（Prefix）失败：" + e.Message, severity: 2, isDebug: false);
            }
        }

        public static void Postfix(Quest __instance)
        {
            try
            {
                if (__instance == null || placeholderRoot == null) return;
                if (ReferenceEquals(__instance.root, placeholderRoot))
                {
                    __instance.root = null;
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("任务清理占位 root（Postfix）失败：" + e.Message, severity: 2, isDebug: false);
            }
        }
    }

    /// <summary>
    /// ★★ 2026-09 补挂：本 mod 的 Harmony 补丁全部是【手工挂载】的
    ///   （HarmonyPatches_Binguin / RodPatches_Binguin / CombinedRaid_Binguin 的静态构造里
    ///    逐条 SafePatch），工程里【没有任何 PatchAll()】。
    ///   而乐队相关的补丁只有 [HarmonyPatch] 特性、从来没人挂 → 一直是死代码：
    ///     · 地标（纪念碑蓝图）可选任意材料  MonumentMarker.AllowedStuffsFor
    ///     · 演唱会期间拦住袭击            IncidentWorker_RaidEnemy.TryExecuteWorker
    ///   这里统一补挂。每一条单独 try/catch，挂不上只影响这一条。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BinguinBandPatches
    {
        static BinguinBandPatches()
        {
            Harmony harmony = new Harmony("zengbing.binguin.band");
            int ok = 0;

            // ① 地标可选任意材料（用户要求：桌子随意材料）
            try
            {
                MethodInfo target = AccessTools.Method(typeof(MonumentMarker), "AllowedStuffsFor");
                if (target != null)
                {
                    harmony.Patch(target, null, new HarmonyMethod(
                        typeof(Patch_BandVisitFreeStuffs).GetMethod("Postfix",
                            BindingFlags.Static | BindingFlags.Public)));
                    ok++;
                }
                else
                {
                    BinguinLogUtility.Log("没找到 MonumentMarker.AllowedStuffsFor，地标材料放行未挂载。", severity: 1, isDebug: false);
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("挂载地标材料补丁失败：" + e.Message, severity: 2, isDebug: false);
            }

            // ② 演唱会期间拦下袭击（延后 8 小时）
            try
            {
                MethodInfo target = AccessTools.DeclaredMethod(
                    typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker");
                if (target != null)
                {
                    HarmonyMethod pre = new HarmonyMethod(
                        typeof(Patch_ConcertBlockRaid).GetMethod("Prefix",
                            BindingFlags.Static | BindingFlags.Public));
                    pre.priority = Priority.First;   // 必须排在组合袭击那条补丁之前
                    harmony.Patch(target, pre, null);
                    ok++;
                }
                else
                {
                    BinguinLogUtility.Log("没找到 IncidentWorker_RaidEnemy.TryExecuteWorker，演唱会拦袭击未挂载。", severity: 1, isDebug: false);
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("挂载演唱会拦袭击补丁失败：" + e.Message, severity: 2, isDebug: false);
            }

            // ③ 手搓任务（root == null）的 Quest.CleanupQuestParts 空引用兜底
            try
            {
                MethodInfo target = AccessTools.DeclaredMethod(typeof(Quest), "CleanupQuestParts");
                if (target != null)
                {
                    harmony.Patch(target,
                        new HarmonyMethod(typeof(Patch_QuestCleanupNullRoot).GetMethod("Prefix",
                            BindingFlags.Static | BindingFlags.Public)),
                        new HarmonyMethod(typeof(Patch_QuestCleanupNullRoot).GetMethod("Postfix",
                            BindingFlags.Static | BindingFlags.Public)));
                    ok++;
                }
                else
                {
                    BinguinLogUtility.Log("没找到 Quest.CleanupQuestParts，任务清理兜底未挂载。", severity: 1, isDebug: false);
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("挂载任务清理兜底补丁失败：" + e.Message, severity: 2, isDebug: false);
            }

            BinguinLogUtility.Log("乐队相关补丁已挂载 " + ok + "/3。");
        }
    }
}
