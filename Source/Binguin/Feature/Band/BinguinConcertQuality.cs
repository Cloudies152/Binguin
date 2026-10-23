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
//     0 乏味 / 1 普通 / 2 难忘 / 3 共鸣
//
// ★★ 2026-10-18 作者要求：「想一下四档概率和质量效果曲线，可参考原版文化质量和成功率曲线」
//     ⇒ 以前是"质量落在哪个阈值区间就一定是哪一档"，现在是**按概率掷档**：
//       · `TierProbabilities(q)`：四条二次曲线（见下方大段注释），作者给了 0/50/100 三个锚点
//       · `RollTier(q)`：开演时掷一次，结果存进 booking.outcomeTier
//       · `MoodOffsetOf(tier)`：-2 / +2 / +5 / +10（作者 2026-10-18 给定）
//       · `GoodwillOf(q)`：4 + 8q（质量 0.5 时 +8）
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

        // ══════════════════════════════════════════════════════════════════
        // 四档概率曲线（2026-10-18 作者给定锚点）
        //
        // ★ 作者给的三个锚点（质量 0 / 50 / 100 时四档的百分比）：
        //     质量   0 → 乏味 30% / 普通 30% / 难忘 30% / 共鸣 10%
        //     质量  50 → 乏味 10% / 普通 20% / 难忘 50% / 共鸣 20%
        //     质量 100 → 乏味  5% / 普通 10% / 难忘 60% / 共鸣 25%
        //
        // ★ 拟合方式：每档过这 3 个点，唯一确定一条**二次曲线**
        //     P_t(q) = a·q² + b·q + c
        //   用节点 0 / 0.5 / 1 的拉格朗日基函数：
        //     L0 = 2q² - 3q + 1    L1 = 4q(1 - q)    L2 = 2q² - q
        //     P = P(0)·L0 + P(0.5)·L1 + P(1)·L2
        //   解出来就是下面三行系数：
        //     乏味  P0 =  0.30q² - 0.55q + 0.30
        //     普通  P1 =            0.30  - 0.20q
        //     难忘  P2 = -0.20q² + 0.50q + 0.30
        //     共鸣  P3 = -0.10q² + 0.25q + 0.10
        //
        //   性质（为什么可以放心直接用）：
        //     · P0+P1+P2+P3 在三个锚点都等于 1，而"和"本身也是二次曲线
        //       ⇒ 恒等于 1（不需要手动归一化，代码里仍归一化一次防浮点误差）
        //     · 四条曲线在 [0,1] 上都不为负（最小值分别是 0 / 0 / 0.3 / 0.1）
        //     · 单调性：乏味单调降、普通单调降、难忘/共鸣单调升
        //       ⇒ 质量越高，分布越往右压，但**低质量也永远有 10% 共鸣**（作者要的"惊喜感"）
        //
        //   实算样例（质量: 乏味/普通/难忘/共鸣）：
        //     0.0 → 30/30/30/10    0.3 → 16/24/43/17
        //     0.5 → 10/20/50/20    0.7 →  6/16/55/23
        //     1.0 →  5/10/60/25
        //   期望心情（-2/+2/+5/+10）：质量 0 → +2.5，0.5 → +4.7，1 → +5.6
        // ══════════════════════════════════════════════════════════════════

        /// <summary>四档数量。</summary>
        public const int TierCount = 4;

        // P_t(q) = TierCoefA[t]·q² + TierCoefB[t]·q + TierCoefC[t]
        private static readonly float[] TierCoefA = new float[] { 0.30f, 0.00f, -0.20f, -0.10f };
        private static readonly float[] TierCoefB = new float[] { -0.55f, -0.20f, 0.50f, 0.25f };
        private static readonly float[] TierCoefC = new float[] { 0.30f, 0.30f, 0.30f, 0.10f };

        /// <summary>单档概率（0~1）。</summary>
        public static float TierProbability(int tier, float quality)
        {
            if (tier < 0 || tier >= TierCount) return 0f;
            float q = Mathf.Clamp01(quality);
            float p = TierCoefA[tier] * q * q + TierCoefB[tier] * q + TierCoefC[tier];
            return p > 0f ? p : 0f;
        }

        /// <summary>四档概率（理论上和恒为 1，这里再归一化一次防浮点误差）。</summary>
        public static float[] TierProbabilities(float quality)
        {
            float q = Mathf.Clamp01(quality);
            float[] p = new float[TierCount];
            float sum = 0f;
            for (int i = 0; i < TierCount; i++)
            {
                p[i] = TierProbability(i, q);
                sum += p[i];
            }
            if (sum <= 0f)
            {
                // 理论上不会发生（曲线上和为 1）；兜底按"难忘"
                p[2] = 1f;
                return p;
            }
            for (int i = 0; i < TierCount; i++) p[i] /= sum;
            return p;
        }

        /// <summary>此刻概率最高的档位（只用于文案/兜底，不是演出结果）。</summary>
        public static int TierOf(float quality)
        {
            float[] p = TierProbabilities(quality);
            int best = 0;
            for (int i = 1; i < TierCount; i++)
            {
                if (p[i] > p[best]) best = i;
            }
            return best;
        }

        /// <summary>
        /// 按概率掷一次档位（**开演时掷一次**，结果存进 booking，散场按它结算）。
        /// </summary>
        public static int RollTier(float quality)
        {
            float[] p = TierProbabilities(quality);
            float r = Rand.Value;
            float acc = 0f;
            for (int i = 0; i < TierCount; i++)
            {
                acc += p[i];
                if (r < acc) return i;
            }
            return TierCount - 1;
        }

        /// <summary>
        /// 档位 → 心情（10 天）。★ 2026-10-18 作者给定：-2 / +2 / +5 / +10。
        /// 最差的一档是**负数**（办砸了确实影响心情），最好的一档 +10。
        /// </summary>
        public static float MoodOffsetOf(int tier)
        {
            switch (tier)
            {
                case 0: return -2f;
                case 1: return 2f;
                case 2: return 5f;
                default: return 10f;
            }
        }

        /// <summary>带正负号的心情文本（日志/提示用，例如 "+5" / "-2"）。</summary>
        public static string MoodOffsetText(int tier)
        {
            float v = MoodOffsetOf(tier);
            return (v >= 0f ? "+" : "") + v.ToString("0");
        }

        /// <summary>
        /// 质量 → 来访派系好感（连续曲线）：+4 ~ +12，质量 0.5 时正好 +8（原固定值）。
        /// </summary>
        public static int GoodwillOf(float quality)
        {
            return Mathf.RoundToInt(4f + 8f * Mathf.Clamp01(quality));
        }

        /// <summary>给玩家看的四档概率预测（开演按钮的悬浮说明用）。</summary>
        public static string PredictionLine(float quality)
        {
            float q = Mathf.Clamp01(quality);
            float[] p = TierProbabilities(q);
            return "Binguin_ConcertSetup_Prediction".Translate(
                TierKey(0).Translate(), Pct(p[0]),
                TierKey(1).Translate(), Pct(p[1]),
                TierKey(2).Translate(), Pct(p[2]),
                TierKey(3).Translate(), Pct(p[3]),
                Pct(q));
        }

        private static string Pct(float v)
        {
            return Mathf.RoundToInt(v * 100f).ToString();
        }

        // ── 预测字符串缓存 ──
        // gizmo 的 defaultDesc 每帧都会重新取；而 `Compute` 要扫全图的灯光/乐器，
        // 直接每帧算太浪费（第 54 条优化轮的原则）。250 tick 内复用同一结果。
        private static BinguinConcertBooking cachedPredictionBooking;
        private static int cachedPredictionTick = -99999;
        private static string cachedPredictionLine;

        /// <summary>带缓存的预测行（给 gizmo 用）。</summary>
        public static string CachedPredictionLine(BinguinConcertBooking b, Map map)
        {
            if (b == null) return "";
            if (b == cachedPredictionBooking && GenTicks.TicksGame - cachedPredictionTick < 250
                && cachedPredictionLine != null)
            {
                return cachedPredictionLine;
            }
            cachedPredictionBooking = b;
            cachedPredictionTick = GenTicks.TicksGame;
            cachedPredictionLine = PredictionLine(Compute(b, map));
            return cachedPredictionLine;
        }

        /// <summary>档位文案的翻译键（0 乏味 / 1 普通 / 2 难忘 / 3 共鸣）。</summary>
        public static string TierKey(int tier)
        {
            switch (tier)
            {
                case 0: return "Binguin_ConcertSelf_Quality_Dull";
                case 1: return "Binguin_ConcertSelf_Quality_Ordinary";
                case 2: return "Binguin_ConcertSelf_Quality_Memorable";
                default: return "Binguin_ConcertSelf_Quality_Resonant";
            }
        }

        public static string TierLabel(float quality)
        {
            return TierKey(TierOf(quality)).Translate();
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
            // ★ 2026-10-23 文案：原来是把几段碎片硬拼（"人数 3/5（30%）艺术总和 60/60（50%）…"），
            //   读起来像日志。改成一条完整句，参数交给翻译键。
            return "Binguin_ConcertDetail_Line".Translate(
                filled.ToString(), TotalSlots.ToString(),
                art.ToString(), ArtisticSumForMax.ToString(),
                lights.ToString(), LightCountForMax.ToString(),
                (q * 100f).ToString("0"), TierLabel(q)).ToString();
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
