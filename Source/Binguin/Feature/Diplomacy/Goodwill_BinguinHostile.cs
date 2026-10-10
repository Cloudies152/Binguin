// ============================================================================
// 霜牙派系的【自然好感 = -100】（2026-09 用户定稿）
//
// 用户要求：
//   "这个好感不是恒 -100，只是默认是 -100，并趋向 -100，
//    可以拉好感拉回中立和友善"
//
// 也就是：
//   · 世界开局好感 = -100
//   · 好感会【持续回落到 -100】（送礼拉上去之后过一阵又会掉回来）
//   · 但【不是锁死】—— 随时可以再送礼/和谈把它拉回中立甚至友善
//
// ── 为什么不用原版现成的两个开关（都 IL 实证过）────────────────────────────
//   · <permanentEnemy>true</permanentEnemy>  → 恒 -100 且永远拉不回来 ✗
//   · <naturalEnemy>true</naturalEnemy>      → 走原版 GoodwillSituationWorker_NaturalEnemy，
//     它返回的是 **-130**（IL：ldc.i4 -130），不是我们要的 -100 ✗
//
// ── 做法 ────────────────────────────────────────────────────────────────────
//   ① Defs/Faction_BinguinHostile.xml 里挂一个自建的 GoodwillSituationDef
//      「BinguinHostileGoodwill」，这里把它的 workerClass 换成下面这个类。
//      GoodwillSituationManager 会把所有 situation 的 offset 加起来当作"自然好感"，
//      好感每天朝它回落 → 目标正好是 -100。
//   ② 开局的初始好感由 GameComponent 强制设一次（只设一次，之后玩家随便拉）。
//
//   ★ 这样"回落"是原版机制在跑（DiplomacyTuning.NaturalGoodwillDailyChange /
//     GoodwillChangeTowardsNaturalGoodwillFactor），我们只负责给出 -100 这个目标值。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Diplomacy
{
    /// <summary>自然好感 = -100（只对霜牙派系生效，其它派系一律 0）。</summary>
    public class GoodwillSituationWorker_BinguinHostile : GoodwillSituationWorker
    {
        public const int NaturalOffset = -100;

        public override int GetNaturalGoodwillOffset(Faction other)
        {
            if (other == null || other.def == null) return 0;
            return other.def.defName == BinguinFactions.Hostile ? NaturalOffset : 0;
        }
    }

    [StaticConstructorOnStartup]
    public static class BinguinHostileGoodwillSetup
    {
        public const string SituationDefName = "BinguinHostileGoodwill";

        static BinguinHostileGoodwillSetup()
        {
            try
            {
                GoodwillSituationDef def = DefDatabase<GoodwillSituationDef>.GetNamedSilentFail(SituationDefName);
                if (def == null)
                {
                    BinguinLogUtility.Log("没找到 GoodwillSituationDef「" + SituationDefName
                        + "」—— 霜牙的自然好感未生效（好感会停在初始值）。", severity: 1, isDebug: false);
                    return;
                }
                def.workerClass = typeof(GoodwillSituationWorker_BinguinHostile);

                // 清掉可能已经缓存好的 worker 实例（懒加载字段，反射写，防止 XML 里的原版 worker 被复用）
                FieldInfo wi = AccessTools.Field(typeof(GoodwillSituationDef), "workerInt");
                if (wi != null)
                {
                    wi.SetValue(def, null);
                }

                BinguinLogUtility.Log("霜牙自然好感已设为 " + GoodwillSituationWorker_BinguinHostile.NaturalOffset
                    + "（会持续回落，但可被送礼/和谈拉回）。");
            }
            catch (Exception ex)
            {
                BinguinLogUtility.Log("设置霜牙自然好感失败（不影响其它功能）：" + ex, severity: 2, isDebug: false);
            }
        }
    }

    /// <summary>开局把霜牙派系对玩家的好感强制设成 -100（只做一次，之后交给原版自然回落）。</summary>
    public class GameComponent_BinguinHostileGoodwill : GameComponent
    {
        private bool applied;

        // 注意：本项目的 GameComponent 子类都用 base()（1.6 的 GameComponent 没有接 Game 的构造）
        public GameComponent_BinguinHostileGoodwill(Game game) : base()
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<bool>(ref applied, "binguinHostileGoodwillApplied", false, false);
        }

        public override void GameComponentTick()
        {
            if (applied) return;
            if (Find.TickManager == null || Find.TickManager.TicksGame < 60) return;
            try
            {
                Apply();
            }
            catch (Exception ex)
            {
                BinguinLogUtility.Log("设置霜牙初始好感失败：" + ex, severity: 2, isDebug: false);
                applied = true;   // 别每 tick 刷屏
            }
        }

        private void Apply()
        {
            if (Find.FactionManager == null) return;
            Faction player = Faction.OfPlayer;
            if (player == null) return;

            int n = 0;
            foreach (Faction f in Find.FactionManager.AllFactions)
            {
                if (f == null || f.def == null || f.def.defName != BinguinFactions.Hostile) continue;
                if (f == player) continue;

                // ★ FactionRelation 存的是 baseGoodwill（IL 实证：BaseGoodwillWith 读的就是
                //   FactionRelation.baseGoodwill），显示值是它再叠加各种 situation 偏移，
                //   所以不要直接写字段 —— 用公开 API TryAffectGoodwillWith 调差值最稳。
                int target = GoodwillSituationWorker_BinguinHostile.NaturalOffset;
                int current = f.GoodwillWith(player);
                if (current != target)
                {
                    f.TryAffectGoodwillWith(player, target - current, false, false, null, null);
                }
                if (f.RelationKindWith(player) != FactionRelationKind.Hostile)
                {
                    f.SetRelationDirect(player, FactionRelationKind.Hostile, false, null, null);
                }
                n++;
            }

            applied = true;
            BinguinLogUtility.Log("霜牙派系初始好感已设为 "
                + GoodwillSituationWorker_BinguinHostile.NaturalOffset + "（共 " + n + " 个派系）。");
        }
    }
}
