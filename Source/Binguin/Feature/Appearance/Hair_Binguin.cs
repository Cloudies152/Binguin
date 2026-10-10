// ============================================================================
// 冰鹅族【发型选择】补丁（2026-09-24 重写：从"一律秃头"改成"只从冰鹅发型里挑"）
//
// ★ 机制（1.6 IL 实证）：头发是【独立渲染节点】PawnRenderNode_Hair，
//   它直接读 pawn.story.hairDef；而 hairDef 是
//   RimWorld.PawnStyleItemChooser.RandomHairFor(Pawn) 生成的
//   （PawnGenerator.TryGenerateNewPawnInternal 里调用它）。
//   1.6 已经没有老的 RimWorld.PawnHairChooser 了。
//
// ★ 历史：
//   · 2026-09 用户画完企鹅头后反馈"乐队访客头部不对"——原版头发是按【原版人头的挂点】
//     （HeadTypeDef.hairMeshSize）画在头顶的，企鹅头上叠一层人类发型就很难看。
//     当时的处理是"冰鹅一律返回 Bald"，属于临时封堵。
//   · 2026-09-24 我们自绘了冰鹅专用头发（Defs/HairDefs_Binguin.xml，
//     styleTags = BinguinHair），并在 Defs/01_RaceAndAppearance/AlienRace_Binguin.xml 的 <styleSettings> 里
//     用 styleTagsOverride 把冰鹅的可选发型限定到这一组
//     ⇒ 原版几十种人类发型【已经自动被排除】，不必再靠"一律秃头"。
//
// ★ 本补丁现在的职责：只是【多一层保险】——万一 styleSettings 没生效
//   （HAR 版本差异 / 其它 mod 干扰），也保证冰鹅头上不会长出人类发型。
//   做法：从下面 HAIR_DEF_NAMES 里取【实际加载到的】发型随机挑一个；
//   一个都没有（XML 没加载出来）就退回原版 Bald。
//
// ★ 为什么用 defName 列表而不是遍历 DefDatabase<HairDef>：
//   本 mod 全部走 DefDatabase<T>.GetNamedSilentFail（见 HarmonyPatches_Binguin.cs 的一贯写法），
//   这样不依赖 AllDefsListForReading 之类的集合 API，编译更稳。
//   ★★ 新增发型时【必须同步改两处】：这里的 HAIR_DEF_NAMES + Defs/HairDefs_Binguin.xml。
//
// ★ 注意：本补丁挂在 PawnStyleItemChooser.RandomHairFor 上，
//   见 HarmonyPatches_Binguin.cs 里 "冰鹅族不生成人类发型" 那一段（SafePatch 手挂）。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Appearance
{
    public static class Patch_BinguinNoHair
    {
        /// <summary>冰鹅族专属发型的 defName 表（★ 与 Defs/HairDefs_Binguin.xml 一一对应）。</summary>
        private static readonly string[] HAIR_DEF_NAMES =
        {
            "Binguin_Hair_Mop",
            "Binguin_Hair_Crest",
        };

        private static List<HairDef> cached;

        /// <summary>实际加载到的冰鹅发型（缓存一次；一个都没加载到时不写缓存，下次再查）。</summary>
        private static List<HairDef> OurHairs()
        {
            if (cached != null) return cached;
            List<HairDef> list = new List<HairDef>();
            try
            {
                for (int i = 0; i < HAIR_DEF_NAMES.Length; i++)
                {
                    HairDef h = DefDatabase<HairDef>.GetNamedSilentFail(HAIR_DEF_NAMES[i]);
                    if (h != null) list.Add(h);
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("查找 BinguinHair 发型失败：" + e, severity: 1, isDebug: false);
            }
            // ★ 只有"真的查到了"才写缓存：def 还没加载完时别把空表缓存住
            if (list.Count > 0) cached = list;
            return list;
        }

        /// <summary>冰鹅族只从自己的发型表里随机挑；表为空则光头。</summary>
        public static bool Prefix(Pawn pawn, ref HairDef __result)
        {
            try
            {
                // ★ 2026-09-26 种族判定收口：见 BinguinRaceUtility
                if (!BinguinRaceUtility.IsBinguin(pawn)) return true;

                List<HairDef> ours = OurHairs();
                if (ours.Count > 0)
                {
                    __result = ours[Rand.Range(0, ours.Count)];
                    return false;                 // 已经给出合法发型，不再走原版随机
                }
                if (Prefs.DevMode)
                {
                    BinguinLogUtility.Log("没找到任何 BinguinHair 发型（Defs/01_RaceAndAppearance/HairDefs_Binguin.xml 未加载？）"
                                + "→ 本次退回光头。", severity: 1, isDebug: false);
                }
                __result = HairDefOf.Bald;        // 兜底：def 没加载出来就光头
                return false;
            }
            catch (Exception)
            {
                return true;   // 出问题就按原版走，绝不因为这条补丁崩掉生成
            }
        }
    }
}
