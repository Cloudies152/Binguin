// ============================================================================
// 冰鹅族【自办演唱会】· 仪式界面的"效果乘数"（2026-10-17 作者要求）
//
// ★ 作者要求：「效果乘数不显示」+ 质量权重：
//     人数 30%（是否每个乐器都有人演出）
//     艺术等级总和 50%（总和到 60 为满）
//     灯光 20%（每个 5%，最多 4 个）
//
// ★ 为什么必须自己写这三个类：
//   原版 `Dialog_BeginRitual` 的"效果"区域，乘数条目来自
//   `RitualOutcomeEffectDef.comps` —— 每个 comp 的 `GetQualityFactor()` 返回一条。
//   我们的 outcome 没有 comps ⇒ 界面上一片空白。
//   （原版 RitualOutcomeComp 是 abstract，`Applies(LordJob_Ritual)` 必须实现。）
//
// ★ 关键：`startingQuality` 设为 0，三项 comp 的 quality 之和就是最终质量，
//   这样界面上的三条乘数加起来正好是总质量，所见即所得。
//
// ★ 计算来源：对话框打开时还没有 booking（预约要等确认后才有），
//   所以全部从 `RitualRoleAssignments` + 目标格现场算。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Binguin.Feature.Band
{
    /// <summary>三项 comp 的共用工具。</summary>
    public static class BinguinConcertCompUtil
    {
        /// <summary>演出角色 id（与 Concerts_Ritual_Binguin.xml 的 roles 对应）。</summary>
        public static readonly string[] RoleIds = new string[]
        {
            "lead", "keyboard", "drumkit", "guitar", "bass"
        };

        /// <summary>取对话框里当前已分配的所有演出者。</summary>
        public static List<Pawn> AssignedPerformers(RitualRoleAssignments assignments)
        {
            List<Pawn> list = new List<Pawn>();
            if (assignments == null) return list;
            for (int i = 0; i < RoleIds.Length; i++)
            {
                IEnumerable<Pawn> ps = assignments.AssignedPawns(RoleIds[i]);
                if (ps == null) continue;
                foreach (Pawn p in ps)
                {
                    if (p != null && !list.Contains(p)) list.Add(p);
                }
            }
            return list;
        }

        public static QualityFactor Make(string label, float quality, string tip)
        {
            QualityFactor qf = new QualityFactor();
            qf.label = label;
            qf.quality = quality;
            qf.positive = quality > 0f;
            qf.present = true;
            qf.toolTip = tip;
            qf.qualityChange = (quality >= 0f ? "+" : "") + (quality * 100f).ToString("0") + "%";
            return qf;
        }
    }

    /// <summary>人数乘数：已到位演出位 / 5，权重 30%。</summary>
    public class RitualOutcomeComp_BinguinPersonnel : RitualOutcomeComp
    {
        public override bool Applies(LordJob_Ritual ritual)
        {
            return true;
        }

        public override QualityFactor GetQualityFactor(Precept_Ritual ritual, TargetInfo ritualTarget,
            RitualObligation obligation, RitualRoleAssignments assignments, RitualOutcomeComp_Data data)
        {
            int total = BinguinConcertCompUtil.RoleIds.Length;
            int filled = BinguinConcertCompUtil.AssignedPerformers(assignments).Count;
            float ratio = total > 0 ? filled / (float)total : 0f;
            float q = BinguinConcertQuality.WeightPersonnel * ratio;
            return BinguinConcertCompUtil.Make(
                "Binguin_Interface_021".Translate().ToString() + filled + "/" + total,
                q,
                "Binguin_Interface_022".Translate().ToString() + (BinguinConcertQuality.WeightPersonnel * 100f).ToString("0") + "Binguin_Interface_023".Translate().ToString());
        }
    }

    /// <summary>艺术乘数：所有演出者艺术等级之和 / 60，权重 50%。</summary>
    public class RitualOutcomeComp_BinguinArtistic : RitualOutcomeComp
    {
        public override bool Applies(LordJob_Ritual ritual)
        {
            return true;
        }

        public override QualityFactor GetQualityFactor(Precept_Ritual ritual, TargetInfo ritualTarget,
            RitualObligation obligation, RitualRoleAssignments assignments, RitualOutcomeComp_Data data)
        {
            List<Pawn> ps = BinguinConcertCompUtil.AssignedPerformers(assignments);
            int sum = 0;
            for (int i = 0; i < ps.Count; i++) sum += BinguinConcertQuality.ArtisticOf(ps[i]);
            float ratio = sum / (float)BinguinConcertQuality.ArtisticSumForMax;
            if (ratio > 1f) ratio = 1f;
            float q = BinguinConcertQuality.WeightArtistic * ratio;
            return BinguinConcertCompUtil.Make(
                "Binguin_Interface_024".Translate().ToString() + sum + "/" + BinguinConcertQuality.ArtisticSumForMax,
                q,
                "Binguin_Interface_025".Translate().ToString()
                + BinguinConcertQuality.ArtisticSumForMax + "Binguin_Interface_026".Translate().ToString()
                + (BinguinConcertQuality.WeightArtistic * 100f).ToString("0") + "Binguin_Interface_023".Translate().ToString());
        }
    }

    /// <summary>灯光乘数：场地周围灯光数 × 5%，最多 4 个（权重 20%）。</summary>
    public class RitualOutcomeComp_BinguinLights : RitualOutcomeComp
    {
        public override bool Applies(LordJob_Ritual ritual)
        {
            return true;
        }

        public override QualityFactor GetQualityFactor(Precept_Ritual ritual, TargetInfo ritualTarget,
            RitualObligation obligation, RitualRoleAssignments assignments, RitualOutcomeComp_Data data)
        {
            int n = 0;
            if (ritualTarget.IsValid && ritualTarget.Map != null)
            {
                n = BinguinConcertQuality.CountLights(ritualTarget.Map, ritualTarget.Cell);
            }
            float q = BinguinConcertQuality.LightScore(n);
            return BinguinConcertCompUtil.Make(
                "Binguin_Interface_027".Translate().ToString() + n + "/" + BinguinConcertQuality.LightCountForMax,
                q,
                "Binguin_Interface_028".Translate().ToString() + BinguinConcertQuality.LightRadius.ToString("0")
                + "Binguin_Interface_029".Translate().ToString()
                + (BinguinConcertQuality.LightWeightPerLight * 100f).ToString("0") + "Binguin_Interface_030".Translate().ToString()
                + BinguinConcertQuality.LightCountForMax + "Binguin_Interface_031".Translate().ToString());
        }
    }
}
