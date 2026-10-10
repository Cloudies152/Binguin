// 加载方式已迁至 XML；下述占位替换描述仅记录旧实现。
// ============================================================================
// 高级钓竿 Harmony 补丁 + 心情 ThoughtWorker
//
// 1) 人肉饵：击杀敌人后心情 +5（patch Pawn.Kill，击杀者装备人肉饵鱼竿）
// 2) 扭曲肉饵：征召时不会崩溃（patch MentalStateHandler.TryStartMentalState）
// 3) 虫胶饵：心情 +3（ThoughtWorker_BinguinRodBait，workerClass 由
//    BinguinDefPatches 静态构造替换到 ThoughtDef）
//
// ★ 沿用项目手动 Patch 模式（[HarmonyPatch] 特性不会自动应用），
//   StaticConstructorOnStartup + new Harmony("binguin.race")。
// ============================================================================

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

using Binguin.Helper;

namespace Binguin.Feature.Rods
{
    [StaticConstructorOnStartup]
    public static class RodPatches_Binguin
    {
        static RodPatches_Binguin()
        {
            Harmony harmony = new Harmony("binguin.race.rod");

            // 1) 击杀心情（人肉饵）——★ 每个 patch 独立 try/catch：
            //    一个失败不能连累另一个（之前 prefix 参数名写错导致两个全没挂）
            try
            {
                MethodInfo killTarget = typeof(Pawn).GetMethod("Kill", BindingFlags.Public | BindingFlags.Instance);
                if (killTarget != null)
                {
                    harmony.Patch(killTarget,
                        prefix: new HarmonyMethod(typeof(Patch_BinguinRod_Kill)
                            .GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public)));
                    BinguinLogUtility.Log("击杀心情补丁已挂载（人肉饵）");
                }
            }
            catch (Exception ex)
            {
                BinguinLogUtility.Log("击杀心情补丁挂载失败：" + ex, severity: 2, isDebug: false);
            }

            // 2) 征召不崩溃（扭曲肉饵）
            try
            {
                MethodInfo msTarget = typeof(MentalStateHandler).GetMethod(
                    "TryStartMentalState", BindingFlags.Public | BindingFlags.Instance);
                if (msTarget != null)
                {
                    harmony.Patch(msTarget,
                        prefix: new HarmonyMethod(typeof(Patch_BinguinRod_MentalState)
                            .GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public)));
                    BinguinLogUtility.Log("征召不崩溃补丁已挂载（扭曲肉饵）");
                }
            }
            catch (Exception ex)
            {
                BinguinLogUtility.Log("征召不崩溃补丁挂载失败：" + ex, severity: 2, isDebug: false);
            }
        }
    }

    // ---- 人肉饵：击杀 +5（prefix 在 Pawn.Kill 前记录击杀者） ----
    public static class Patch_BinguinRod_Kill
    {
        // ★ Harmony 按参数名注入：Pawn.Kill 的参数名是 dinfo / exactCulprit（不是 hediff）
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit)
        {
            try
            {
                if (__instance == null || __instance.RaceProps == null || !__instance.RaceProps.Humanlike)
                {
                    return;
                }
                Pawn killer = null;
                if (dinfo.HasValue && dinfo.Value.Instigator != null)
                {
                    killer = dinfo.Value.Instigator as Pawn;
                }
                if (killer == null || killer == __instance || killer.Dead)
                {
                    return;
                }
                if (killer.equipment == null || killer.equipment.Primary == null)
                {
                    return;
                }
                CompBinguinRod rod = killer.equipment.Primary.TryGetComp<CompBinguinRod>();
                if (rod == null || !rod.HasEffect(BinguinBaitEffect.HumanMeat))
                {
                    return;
                }
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Binguin_RodBaitHumanKill");
                if (thought != null)
                {
                    killer.needs.mood.thoughts.memories.TryGainMemory(thought);
                }
            }
            catch (Exception ex)
            {
                BinguinLogUtility.Log("击杀心情补丁异常：" + ex.Message, severity: 1, isDebug: false);
            }
        }
    }

    // ---- 扭曲肉饵：征召时不会崩溃 ----
    public static class Patch_BinguinRod_MentalState
    {
        public static bool Prefix(MentalStateHandler __instance, MentalStateDef stateDef, bool causedByMood, bool causedByDamage, bool causedByPsycast, Pawn otherPawn)
        {
            try
            {
                // ★ MentalStateHandler.pawn 是 private 字段，用 AccessTools 反射读取
                Pawn pawn = (Pawn)AccessTools.Field(typeof(MentalStateHandler), "pawn").GetValue(__instance);
                if (pawn == null || pawn.equipment == null || pawn.equipment.Primary == null)
                {
                    return true;
                }
                CompBinguinRod rod = pawn.equipment.Primary.TryGetComp<CompBinguinRod>();
                if (rod == null || !rod.HasEffect(BinguinBaitEffect.TwistedMeat))
                {
                    return true;
                }
                if (pawn.Drafted)
                {
                    return false;   // 征召中：免疫精神崩溃（扭曲肉饵效果）
                }
                return true;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    // ---- 虫胶饵：心情 +3（situational） ----
    public class ThoughtWorker_BinguinRodBait : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || p.equipment == null || p.equipment.Primary == null)
            {
                return false;
            }
            CompBinguinRod rod = p.equipment.Primary.TryGetComp<CompBinguinRod>();
            if (rod == null || !rod.HasEffect(BinguinBaitEffect.Jelly))
            {
                return false;
            }
            return true;
        }
    }
}
