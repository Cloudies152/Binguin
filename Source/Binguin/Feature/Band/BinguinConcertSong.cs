// ============================================================================
// 冰鹅族【演唱会】· 曲目库（2026-10-11 作者需求）
//
// ★ 作者原话：
//   「可以自选歌曲（做个歌曲库，可以往里面塞歌曲，在演唱会可以自定义播放）」
//   「歌曲：先只做数据层」
//
// ★ 设计：**每首歌一个 Def**，继承本文件的 `Binguin_SongDef : Def`。
//   ⇒ 「往里面塞歌曲」= 在 XML 里加一个条目，**完全不用改代码**。
//   ⇒ 条目位置：`Defs/Feature/Band/Concerts_Songs_Binguin.xml`
//
// ★ 字段（都可在 XML 里写）：
//     label                   曲名（Def 自带）
//     description             介绍（Def 自带）
//     style                   风格标签（抒情 / 激昂 / 民谣 …），只用于显示与筛选
//     durationTicks           时长（tick；1 小时 = 2500）
//     moodOffset              观众心情加成（叠加在质量档之上）
//     requiredArtisticLevel   登台所需的**艺术**技能等级（0 = 不限）
//     soundDef                音频（**占位**：一期先指向原版 Harp_Play；
//                             将来拿到 .ogg 再换。RimWorld 的音频必须是真的音频资源）
//
// ★ 音频说明（重要，别误会）：
//   `soundDef` 只能指向**已存在的** SoundDef。想加真实歌曲，
//   必须提供 .ogg 音频文件并配 SoundDef；一期**只做数据层**，
//   所以这里指向原版 `Harp_Play` 之类的占位。
//
// ★ 技能：原版没有"音乐"技能，演唱会效果统一用 **Artistic（艺术）**
//   （原版 Artistic 的定义本就包含"演奏乐器"）。见
//   `BinguinConcertQuality.cs`。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;
using Binguin.Helper;

namespace Binguin.Feature.Band
{
    /// <summary>
    /// 一首曲目。每首歌一个 Def，在 `Defs/Feature/Band/Concerts_Songs_Binguin.xml`
    /// 里声明（自定义类型可直接写在 XML，见 `Source/XML_DEFS.md`）。
    /// </summary>
    public class Binguin_SongDef : Def
    {
        /// <summary>风格标签（抒情 / 激昂 / 民谣…）。只用于显示与筛选，不参与数值。</summary>
        public string style;

        /// <summary>时长（tick）。1 小时 = 2500 tick。0 或负 = 用默认 4 小时。</summary>
        public int durationTicks;

        /// <summary>观众心情加成（叠加在质量档之上；可以是负数，例如"难听"的歌）。</summary>
        public float moodOffset;

        /// <summary>登台所需的艺术技能等级。0 = 不限。</summary>
        public int requiredArtisticLevel;

        /// <summary>音频（**占位**：一期指向原版 SoundDef）。可为空。</summary>
        public string soundDef;

        /// <summary>默认时长：4 小时（与原 BinguinConcert.DurationTicks 一致）。</summary>
        public const int DefaultDurationTicks = 4 * 2500;

        /// <summary>实际时长（XML 没写就用默认）。</summary>
        public int EffectiveDurationTicks
        {
            get { return durationTicks > 0 ? durationTicks : DefaultDurationTicks; }
        }

        /// <summary>
        /// 这个殖民者能不能唱/弹这首（只看艺术技能门槛）。
        /// 技能不够时 `reason` 给出可显示的原因。
        /// </summary>
        public bool CanPerform(Pawn p, out string reason)
        {
            reason = null;
            if (p == null || p.skills == null)
            {
                reason = "Binguin_ConcertSong_NoSkill".Translate();
                return false;
            }
            if (requiredArtisticLevel <= 0)
            {
                return true;
            }
            int lvl = p.skills.GetSkill(SkillDefOf.Artistic).Level;
            if (lvl < requiredArtisticLevel)
            {
                reason = "Binguin_ConcertSong_LevelTooLow".Translate(
                    requiredArtisticLevel.ToString(), lvl.ToString());
                return false;
            }
            return true;
        }
    }

    /// <summary>曲目库工具：查询、挑默认曲目、按技能筛可用曲目。</summary>
    public static class BinguinConcertSongs
    {
        /// <summary>所有曲目（缓存）。</summary>
        private static List<Binguin_SongDef> cached;

        /// <summary>
        /// 取全部曲目。★ 缓存一次即可 —— Def 在游戏运行期不会变。
        /// ★ 用 `DefDatabase<T>.AllDefsListForReading` 而不是 `AllDefs`：
        ///   后者每次调用都新建列表。
        /// </summary>
        public static List<Binguin_SongDef> All
        {
            get
            {
                if (cached == null)
                {
                    cached = new List<Binguin_SongDef>();
                    List<Binguin_SongDef> src = DefDatabase<Binguin_SongDef>.AllDefsListForReading;
                    if (src != null)
                    {
                        for (int i = 0; i < src.Count; i++)
                        {
                            if (src[i] != null)
                            {
                                cached.Add(src[i]);
                            }
                        }
                    }
                    if (cached.Count == 0)
                    {
                        BinguinLogUtility.Log("曲目库为空 —— 检查 Defs/Feature/Band/Concerts_Songs_Binguin.xml 是否加载。",
                            severity: 1, isDebug: false);
                    }
                }
                return cached;
            }
        }

        /// <summary>
        /// 这个小人能唱的曲目（按艺术技能过滤）。
        /// 一首都没有时返回空列表（调用方要能处理）。
        /// </summary>
        public static List<Binguin_SongDef> AvailableFor(Pawn p)
        {
            List<Binguin_SongDef> result = new List<Binguin_SongDef>();
            List<Binguin_SongDef> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                string reason;
                if (all[i].CanPerform(p, out reason))
                {
                    result.Add(all[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// 默认曲目：优先给"技能够得着"的第一首；都不够就返回第一首（让玩家自己选）。
        /// 曲目库为空时返回 null。
        /// </summary>
        public static Binguin_SongDef DefaultFor(Pawn p)
        {
            List<Binguin_SongDef> ok = AvailableFor(p);
            if (ok.Count > 0)
            {
                return ok[0];
            }
            List<Binguin_SongDef> all = All;
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>按 defName 找一首（存档读回来用）。</summary>
        public static Binguin_SongDef ByName(string defName)
        {
            if (defName.NullOrEmpty())
            {
                return null;
            }
            return DefDatabase<Binguin_SongDef>.GetNamedSilentFail(defName);
        }
    }
}
