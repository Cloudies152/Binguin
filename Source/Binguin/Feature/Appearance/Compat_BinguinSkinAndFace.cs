// ============================================================================
// 三方兼容补丁（纯 C#，不动 XML 定义）—— 2026-09-23
//
// ① 「头部依然是智人头」的真凶：Nals.FacialAnimation（FA）
//    IL 实证 FacialAnimation.FaceTypeGenerator<T>..cctor：
//        raceFaceTypeList = new Dictionary<string, IEnumerable<T>>();
//        foreach (race in FAHelper.GetAllRaces())
//            raceFaceTypeList[race.defName] = AllDefs.Where(d => d.raceName == "");
//        foreach (def in AllDefs.Where(d => d.raceName != ""))
//            raceFaceTypeList[def.raceName] = AllDefs.Where(d => d.raceName == def.raceName);
//    → 没有用 <raceName> 注册过的种族，FA 一律拿【通用（智人）那套 def】；
//    FA 的头部 def 是 HeadNormal：
//        <FacialAnimation.HeadTypeDef><defName>HeadNormal</defName>
//        <texPath>Things/Pawn/Humanlike/Heads_Blank/Normal</texPath>
//    = 一张【智人空白头】贴图。只要 FAHelper.ShouldDrawPawn(pawn) 为 true，
//    FA 就会把这张智人头画上去，把 HAR 自绘的 Races/Binguin/Head/BinguinHead
//    整个盖掉 —— 表现就是「头部依然没变，用的是智人头」。
//    （这也解释了为什么日志里【一条贴图错误都没有】：HAR 那条路其实是对的，
//      请求的是我们自己的贴图；只是被 FA 盖在上面了。）
//
//    ★ 2026-09-23 最终方案：**改用 FA 自己的种族注册机制**（推荐、也是 FA 的设计意图）——
//      在 Defs/FacialAnimation_Binguin.xml 里给种族 "Binguin" 写 <raceName> 的
//      HeadTypeDef / EyeballTypeDef / BrowTypeDef / LidTypeDef / MouthTypeDef，
//      贴图由 Tools/Make-BinguinFATextures.py 生成到 Textures/Races/Binguin/FA/。
//      此时 FA 就会用【我们自己的脸】，不用再关它。
//    ★ 本文件下面那两条 prefix 降级成【兜底】：
//      只有在"装了 FA、但我们的 FA def 没加载出来"（XML 打错/被 MayRequire 跳过）时，
//      才把冰鹅的 FA 关掉 —— 否则又会退回"智人空白头盖住自绘头"的老毛病。
//      判定见 BinguinFaDefsLoaded()（延迟到第一次渲染时查，那时 def 已全部加载）。
//
// ② 「企鹅族的基因会被其他肤色基因覆盖」：
//    原版 RimWorld.Pawn_StoryTracker.get_SkinColor() = skinColorOverride ?? SkinColorBase，
//    而 get_SkinColorBase() 在 pawn 没有黑色素基因时会调
//    PawnSkinColors.RandomSkinColorGene(pawn) 【当场随机塞一个黑色素基因进去】
//    （IL 实证，见 Defs/Feature/RaceTraits/GeneDefs_Binguin.xml 顶部注释）→ 冰鹅的肤色会被那个随机基因改掉。
//    这里直接锁死：冰鹅族的 SkinColor 恒为纯白，任何肤色基因都改不动。
//    （HAR 的 <colorChannels><li><name>skin</name> first/second 也都是纯白，
//      两边一致：身体/头部贴图不会被染色。）
// ============================================================================

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Appearance
{
    [StaticConstructorOnStartup]
    public static class Compat_BinguinSkinAndFace
    {
        private static bool loggedFacialAnimation;
        private static bool loggedHeadPath;
        // 0 = 还没查过；1 = 我们的 FA def 在（正常走 FA）；2 = 不在（兜底关掉 FA）
        private static int faDefsState;

        /// <summary>
        /// 我们的 FA 种族 def 是否真的加载出来了（FA_Binguin_Head）。
        /// 延迟到第一次渲染时查：那时所有 def 都已加载完，且不改动启动耗时。
        /// 用 GenDefDatabase.GetAllDefsInDatabaseForDef(Type) 按 FA 的 HeadTypeDef 类型查，
        /// 全程反射、**不做硬依赖**（没装 FA 时直接返回 false）。
        /// </summary>
        private static bool BinguinFaDefsLoaded()
        {
            if (faDefsState != 0)
            {
                return faDefsState == 1;
            }
            bool found = false;
            try
            {
                Type headType = GenTypes.GetTypeInAnyAssembly("FacialAnimation.HeadTypeDef");
                if (headType != null)
                {
                    System.Collections.Generic.IEnumerable<Def> defs =
                        GenDefDatabase.GetAllDefsInDatabaseForDef(headType);
                    if (defs != null)
                    {
                        foreach (Def d in defs)
                        {
                            if (d != null && d.defName == "FA_Binguin_Head")
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("检查 FA 种族 def 失败（按「没加载」处理）：" + e.Message, severity: 1, isDebug: false);
            }
            faDefsState = found ? 1 : 2;
            if (!found)
            {
                BinguinLogUtility.Log("没找到 FA_Binguin_Head（Defs/FacialAnimation_Binguin.xml 未生效？）"
                    + "→ 兜底关闭冰鹅族的 FacialAnimation 渲染，避免又变成智人头。", severity: 1, isDebug: false);
            }
            return found;
        }

        static Compat_BinguinSkinAndFace()
        {
            Harmony harmony = new Harmony("binguin.race.compat");
            PatchWhiteSkin(harmony);
            PatchFacialAnimation(harmony);
            PatchHeadDiagnostic(harmony);
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------
        // ★ 2026-09-26 种族判定收口：本地这个 IsBinguin 已删除，
        //   改为直接调 BinguinRaceUtility.IsBinguin（全项目唯一判据）。

        private static HarmonyMethod HM(string name)
        {
            MethodInfo mi = typeof(Compat_BinguinSkinAndFace).GetMethod(name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return mi == null ? null : new HarmonyMethod(mi);
        }

        // ------------------------------------------------------------------
        // ① 纯白肤色锁
        // ------------------------------------------------------------------
        private static void PatchWhiteSkin(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.PropertyGetter(typeof(Pawn_StoryTracker), "SkinColor");
                HarmonyMethod post = HM("Postfix_SkinColor");
                if (target == null || post == null)
                {
                    BinguinLogUtility.Log("未找到 Pawn_StoryTracker.get_SkinColor，纯白肤色锁未挂载。", severity: 1, isDebug: false);
                    return;
                }
                harmony.Patch(target, prefix: null, postfix: post);
                BinguinLogUtility.Log("补丁已挂载：纯白肤色锁（冰鹅族肤色恒为纯白，不受任何肤色基因影响）");
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("纯白肤色锁挂载失败：" + e.Message, severity: 1, isDebug: false);
            }
        }

        /// <summary>冰鹅族：肤色恒纯白（无视黑色素基因 / 异种基因 / 随机肤色）。</summary>
        public static void Postfix_SkinColor(Pawn_StoryTracker __instance, ref Color __result)
        {
            try
            {
                if (__instance == null) return;
                Pawn p = AccessTools.Field(typeof(Pawn_StoryTracker), "pawn") != null
                    ? AccessTools.Field(typeof(Pawn_StoryTracker), "pawn").GetValue(__instance) as Pawn
                    : null;
                if (BinguinRaceUtility.IsBinguin(p))
                {
                    __result = Color.white;
                }
            }
            catch (Exception)
            {
                // 只读补丁，出错就当没挂，绝不打断渲染
            }
        }

        // ------------------------------------------------------------------
        // ② 关闭冰鹅族的 FA 面部渲染
        // ------------------------------------------------------------------
        private static void PatchFacialAnimation(Harmony harmony)
        {
            try
            {
                Type helper = GenTypes.GetTypeInAnyAssembly("FacialAnimation.FAHelper");
                if (helper == null)
                {
                    return; // 没装 FacialAnimation，什么都不用做
                }
                bool any = false;

                // (a) 真正的总闸门：DrawFaceGraphicsComp.CompRenderNodes() 第一件事就是
                //     CheckEnableDrawing(pawn)，返回 false 时【连 FA 的渲染节点都不创建】。
                //     （IL 实证：节点是 NLFacialAnimationPartNode，父节点 = 头部节点，
                //       它的 GraphicFor → controller.GetGraphic(layerType) →
                //       FA 的 <texPath>Things/Pawn/Humanlike/Heads_Blank/Normal</texPath>
                //       = 智人空白头，正好盖在 HAR 自绘头部上面。）
                Type comp = GenTypes.GetTypeInAnyAssembly("FacialAnimation.DrawFaceGraphicsComp");
                MethodInfo gate = comp != null
                    ? AccessTools.Method(comp, "CheckEnableDrawing", new Type[] { typeof(Pawn) })
                    : null;
                HarmonyMethod gatePre = HM("Prefix_CheckEnableDrawing");
                if (gate != null && gatePre != null)
                {
                    harmony.Patch(gate, prefix: gatePre, postfix: null);
                    any = true;
                }

                // (b) 兜底：FA 所有功能入口都会先问 FAHelper.ShouldDrawPawn(pawn)
                //     （PostfixAddChildren / PostfixParallelPreDraw / BaseHeadOffsetAt /
                //      头发·胡须 mesh / ControllerComp.CompTick），一并关掉。
                MethodInfo target = AccessTools.Method(helper, "ShouldDrawPawn", new Type[] { typeof(Pawn) });
                HarmonyMethod pre = HM("Prefix_ShouldDrawPawn");
                if (target != null && pre != null)
                {
                    harmony.Patch(target, prefix: pre, postfix: null);
                    any = true;
                }

                if (any)
                {
                    BinguinLogUtility.Log("补丁已挂载：关闭冰鹅族的 FacialAnimation 面部渲染（FA 会用智人空白头盖住自绘头部）");
                }
                else
                {
                    BinguinLogUtility.Log("找到 FacialAnimation 但没找到 CheckEnableDrawing/ShouldDrawPawn，FA 覆盖头部的问题未处理。", severity: 1, isDebug: false);
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("FacialAnimation 兼容补丁挂载失败：" + e.Message, severity: 1, isDebug: false);
            }
        }

        /// <summary>DrawFaceGraphicsComp.CheckEnableDrawing(Pawn)：兜底用（只在我们的 FA def 缺失时关掉）。</summary>
        public static bool Prefix_CheckEnableDrawing(Pawn pawn, ref bool __result)
        {
            if (!BinguinRaceUtility.IsBinguin(pawn) || BinguinFaDefsLoaded())
            {
                return true; // 正常情况：我们注册过种族，让 FA 自己画我们的脸
            }
            __result = false;
            return false;
        }

        /// <summary>FAHelper.ShouldDrawPawn(Pawn)：兜底用（只在我们的 FA def 缺失时关掉）。</summary>
        public static bool Prefix_ShouldDrawPawn(Pawn pawn, ref bool __result)
        {
            if (!BinguinRaceUtility.IsBinguin(pawn) || BinguinFaDefsLoaded())
            {
                return true; // 正常情况：让 FA 画我们的脸
            }
            __result = false;
            if (!loggedFacialAnimation)
            {
                loggedFacialAnimation = true;
                BinguinLogUtility.Log("已对冰鹅族关闭 FacialAnimation 面部渲染（兜底），头部改用本 mod 自绘贴图。");
            }
            return false;
        }

        // ------------------------------------------------------------------
        // ③ 头部贴图自检（只在日志里打一次，方便排查"头还是不对"）
        // ------------------------------------------------------------------
        private static void PatchHeadDiagnostic(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(PawnRenderNode_Head), "GraphicFor",
                    new Type[] { typeof(Pawn) });
                HarmonyMethod post = HM("Postfix_HeadGraphicFor");
                if (target == null || post == null)
                {
                    return;
                }
                harmony.Patch(target, prefix: null, postfix: post);
            }
            catch (Exception)
            {
                // 纯诊断，失败无所谓
            }
        }

        public static void Postfix_HeadGraphicFor(Pawn pawn, ref Graphic __result)
        {
            try
            {
                if (loggedHeadPath || !BinguinRaceUtility.IsBinguin(pawn)) return;
                loggedHeadPath = true;
                string path = "null";
                if (__result != null)
                {
                    FieldInfo fi = AccessTools.Field(__result.GetType(), "path")
                        ?? AccessTools.Field(typeof(Graphic), "path");
                    object v = fi != null ? fi.GetValue(__result) : null;
                    path = v != null ? v.ToString() : __result.GetType().Name;
                }
                BinguinLogUtility.Log("头部贴图自检：HAR 实际使用的头部贴图 = " + path
                    + "（应为 Races/Binguin/Head/BinguinHead）");

                // FA 接线自检：冰鹅到底有没有 FA 的面部组件？
                // FA 的 Patches/Race_Patches.xml 只给【defName=Human】加组件；
                // 我们的种族是 ParentName="Human" 的 ThingDef_AlienRace，
                // comps 是继承下来的（补丁先于继承解析生效）→ 正常情况下应该有。
                // 若这里是"无"，FA 根本不会画冰鹅的脸（会一直用 HAR 自绘头），
                // 那就需要在 Defs/Feature/Appearance/AlienRace_Binguin.xml 里显式补 comps（见交接文档第 85 条）。
                bool hasFaComp = false;
                System.Collections.Generic.List<ThingComp> comps = pawn.AllComps;
                if (comps != null)
                {
                    for (int i = 0; i < comps.Count; i++)
                    {
                        ThingComp c = comps[i];
                        if (c != null && c.GetType().Name == "DrawFaceGraphicsComp")
                        {
                            hasFaComp = true;
                            break;
                        }
                    }
                }
                bool hasDefs = BinguinFaDefsLoaded();
                BinguinLogUtility.Log("FA 自检：冰鹅身上 FA 面部组件 = " + (hasFaComp ? "有" : "无")
                    + "，本 mod 的 FA 种族 def(FA_Binguin_Head) = " + (hasDefs ? "有" : "无")
                    + (hasFaComp && hasDefs ? " → FA 会用我们自绘的脸。" : " → 请把这一行发给开发者。"));
            }
            catch (Exception)
            {
            }
        }
    }
}
