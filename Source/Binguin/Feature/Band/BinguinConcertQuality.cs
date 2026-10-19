// ============================================================================
// 冰鹅族【自办演唱会】· 演出质量计算（2026-10-17 作者给出的权重）
//
// ★★ 作者原话：
//   「演出质量取决于人数，权重30%（是否每个乐器都有人演出），
//     艺术等级总和，权重50%（越高越好，等级总和到60为最高质量），
//     灯光数量，权重20%（最高质量要4个，每个提供5%）。」
//
// ★★ 公式（结果 0~1）：
//     quality = 0.30 × 人数比例
//             + 0.50 × min(艺术总和 / 60, 1)
//             + 灯光贡献（每个 5%，最多 4 个 ⇒ 最高 20%）
//
//   · 人数比例 = 已到位的演出位 / 总演出位
//     总演出位 = 主唱 1 + 键盘/架子鼓/吉他/贝斯 4 = **5**
//     （"是否每个乐器都有人演出" ⇒ 每个乐器算一个位）
//   · 艺术总和 = 所有演出者 Artistic（艺术）技能等级之和；
//     注意原版**没有音乐技能**，模组统一用 Artistic
//   · 灯光 = 场地周围 25 格内的「演唱会灯组 / 临时演唱会灯组」数量
//
// ★★ 档位（quality → 4 档），用于文案与心情
//     < 0.25 惨不忍睹 / < 0.50 平平无奇 / < 0.75 很精彩 / ≥ 0.75 难以忘怀
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin.Feature.Band
{
    /// <summary>演唱会质量：按作者给定的三项权重计算。</summary>
    public static class BinguinConcertQuality
    {
        // ── 权重与上限（作者指定，不要随意改） ──
        public const float WeightPersonnel = 0.30f;
        public const float WeightArtistic = 0.50f;
        public const float LightWeightPerLight = 0.05f;
        public const int LightCountForMax = 4;     // 4 个灯 = 满 20%
        public const int ArtisticSumForMax = 60;   // 艺术总和 60 = 满 50%

        /// <summary>演出用的 4 件乐器（不含麦克风/主唱位）。</summary>
        public static readonly string[] InstrumentDefNames = new string[]
        {
            "Binguin_Band_Keyboard",
            "Binguin_Band_DrumKit",
            "Binguin_Band_Guitar",
            "Binguin_Band_Bass",
        };

        /// <summary>两件灯光建筑的 defName。</summary>
        public static readonly string[] LightDefNames = new string[]
        {
            "Binguin_ConcertLight",
            "Binguin_ConcertLightRigTemp",
        };

        /// <summary>灯光计数半径（格）。</summary>
        public const float LightRadius = 25f;

        public static int ArtisticOf(Pawn p)
        {
            if (p == null || p.skills == null) return 0;
            SkillRecord s = p.skills.GetSkill(SkillDefOf.Artistic);
            return s != null ? s.Level : 0;
        }

        /// <summary>总演出位：主唱 1 + 乐器 4。</summary>
        public static int TotalSlots
        {
            get { return 1 + InstrumentDefNames.Length; }
        }

        /// <summary>已到位的演出位数量（人还活着且在场）。</summary>
        public static int FilledSlots(BinguinConcertBooking b)
        {
            if (b == null) return 0;
            int n = 0;
            if (Usable(b.leadSinger)) n++;
            for (int i = 0; i < InstrumentDefNames.Length; i++)
            {
                Pawn p = MusicianOf(b, InstrumentDefNames[i]);
                if (Usable(p)) n++;
            }
            return n;
        }

        /// <summary>人数比例 0~1（权重 30%）。</summary>
        public static float PersonnelRatio(BinguinConcertBooking b)
        {
            return FilledSlots(b) / (float)TotalSlots;
        }

        /// <summary>所有演出者的艺术等级之和（权重 50% 的分子）。</summary>
        public static int ArtisticSum(BinguinConcertBooking b)
        {
            if (b == null) return 0;
            int sum = 0;
            if (Usable(b.leadSinger)) sum += ArtisticOf(b.leadSinger);
            for (int i = 0; i < InstrumentDefNames.Length; i++)
            {
                Pawn p = MusicianOf(b, InstrumentDefNames[i]);
                if (Usable(p)) sum += ArtisticOf(p);
            }
            return sum;
        }

        /// <summary>灯光贡献 0~0.20（每个 5%，最多 4 个）。</summary>
        public static float LightScore(int lightCount)
        {
            int n = lightCount < 0 ? 0 : (lightCount > LightCountForMax ? LightCountForMax : lightCount);
            return n * LightWeightPerLight;
        }

        /// <summary>场地周围的灯光数量。</summary>
        public static int CountLights(Map map, IntVec3 center)
        {
            if (map == null || !center.IsValid || map.listerBuildings == null) return 0;
            List<Building> all = map.listerBuildings.allBuildingsColonist;
            int n = 0;
            float r2 = LightRadius * LightRadius;
            for (int i = 0; i < all.Count; i++)
            {
                Building bd = all[i];
                if (bd == null || bd.def == null) continue;
                bool isLight = false;
                for (int k = 0; k < LightDefNames.Length; k++)
                {
                    if (bd.def.defName == LightDefNames[k]) { isLight = true; break; }
                }
                if (!isLight) continue;
                if ((bd.Position - center).LengthHorizontalSquared > r2) continue;
                n++;
            }
            return n;
        }

        /// <summary>最终质量 0~1。</summary>
        public static float Compute(BinguinConcertBooking b, Map map)
        {
            if (b == null) return 0f;
            IntVec3 center = b.venueCenter.IsValid ? b.venueCenter
                : (b.leadSinger != null ? b.leadSinger.Position : IntVec3.Invalid);
            float personnel = PersonnelRatio(b);
            float artistic = Mathf.Clamp01(ArtisticSum(b) / (float)ArtisticSumForMax);
            float light = LightScore(CountLights(map, center));
            return Mathf.Clamp01(WeightPersonnel * personnel + WeightArtistic * artistic + light);
        }

        /// <summary>档位：0 惨不忍睹 / 1 平平无奇 / 2 很精彩 / 3 难以忘怀。</summary>
        public static int TierOf(float quality)
        {
            if (quality < 0.25f) return 0;
            if (quality < 0.50f) return 1;
            if (quality < 0.75f) return 2;
            return 3;
        }

        /// <summary>档位文案的翻译键。</summary>
        public static string TierKey(int tier)
        {
            switch (tier)
            {
                case 0: return "Binguin_ConcertSelf_Quality_Terrible";
                case 1: return "Binguin_ConcertSelf_Quality_Boring";
                case 2: return "Binguin_ConcertSelf_Quality_Fun";
                default: return "Binguin_ConcertSelf_Quality_Unforgettable";
            }
        }

        public static string TierLabel(float quality)
        {
            return TierKey(TierOf(quality)).Translate();
        }

        /// <summary>该档位给参与者的心情加成。</summary>
        public static float MoodOffsetOf(float quality)
        {
            switch (TierOf(quality))
            {
                case 0: return 2f;
                case 1: return 4f;
                case 2: return 6f;
                default: return 8f;
            }
        }

        /// <summary>给玩家看的一行明细（用于信件/消息）。</summary>
        public static string DetailLine(BinguinConcertBooking b, Map map)
        {
            if (b == null) return "";
            IntVec3 center = b.venueCenter.IsValid ? b.venueCenter
                : (b.leadSinger != null ? b.leadSinger.Position : IntVec3.Invalid);
            int filled = FilledSlots(b);
            int art = ArtisticSum(b);
            int lights = CountLights(map, center);
            float q = Compute(b, map);
            return "人数 " + filled + "/" + TotalSlots + "（30%）"
                + " · 艺术总和 " + art + "/" + ArtisticSumForMax + "（50%）"
                + " · 灯光 " + lights + "/" + LightCountForMax + "（20%）"
                + " ⇒ 质量 " + (q * 100f).ToString("0") + "%【" + TierLabel(q) + "】";
        }

        // ── 内部 ──
        private static bool Usable(Pawn p)
        {
            return p != null && !p.Dead;
        }

        private static Pawn MusicianOf(BinguinConcertBooking b, string instrumentDefName)
        {
            if (b == null || b.musicians == null) return null;
            Pawn p;
            return b.musicians.TryGetValue(instrumentDefName, out p) ? p : null;
        }
    }
}
