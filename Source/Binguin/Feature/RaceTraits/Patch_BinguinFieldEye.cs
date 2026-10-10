// ============================================================================
// 野外作战仿生眼 —— 开枪时无视天气精度减益
//
// 1.6 反编译确认（Mono.Cecil 读 Assembly-CSharp.dll）：
//   Verse.ShotReport.HitReportFor(Thing caster, Verb verb, LocalTargetInfo target) 内部：
//     bool ignoreMaluses = verb.EquipmentSource != null
//          && verb.EquipmentSource.TryGetComp<CompUniqueWeapon>(out var c)
//          && c.IgnoreAccuracyMaluses;        // ← Odyssey 独特武器的"无视精度减益"
//     ...
//     if (!ignoreMaluses && !(caster.Position.Roofed(map) && target.Cell.Roofed(map)))
//         factorFromWeather = map.weatherManager.CurWeatherAccuracyMultiplier;   // IL_01AC
//     else
//         factorFromWeather = 1f;
//   ★ 全程序集扫描：CurWeatherAccuracyMultiplier 只被 HitReportFor 调用这一次 →
//     把结果结构体里的 factorFromWeather 改回 1f 就等于"完全无视天气"。
//   ★ TotalEstimatedHitChance = Clamp01(AimOnTargetChance × PassCoverChance)，
//     命中率说明 UI 与实际开火判定共用同一个 ShotReport 结构体，
//     所以本补丁对"面板显示"与"真打出去的子弹"同时生效。
//
// ★ 坑：ShotReport 是 struct，不能用 AccessTools.FieldRef（那个委托签名是 Invoke(T)，
//   传 struct 会改到副本上，静默无效）；必须用 StructFieldRef：Invoke(ref T) 返回 ref F。
//
// 挂载：HarmonyPatches_Binguin 静态构造里手动 Patch（与本文件其它补丁一致）。
// ============================================================================

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.RaceTraits
{
    // 判定：某个 pawn 身上是否装着野外作战仿生眼
    public static class BinguinFieldEyeUtility
    {
        public const string HediffDefName = "Binguin_FieldCombatBionicEye";

        private static HediffDef cachedDef;
        private static bool lookedUp;

        public static HediffDef FieldEyeDef
        {
            get
            {
                if (!lookedUp)
                {
                    lookedUp = true;
                    cachedDef = DefDatabase<HediffDef>.GetNamedSilentFail(HediffDefName);
                }
                return cachedDef;
            }
        }

        public static bool HasFieldEye(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return false;
            }
            HediffDef def = FieldEyeDef;
            if (def == null)
            {
                return false;
            }
            return pawn.health.hediffSet.GetFirstHediffOfDef(def, false) != null;
        }
    }

    public static class Patch_BinguinFieldEye
    {
        private static FieldInfo weatherFactorField;
        private static bool fieldTried;

        // ShotReport.factorFromWeather（私有字段，1.6 反射确认字段名）
        // 已装箱的常量 1f（避免每次命中都新建一个装箱对象）
        private static readonly object BoxedOne = 1f;

        private static FieldInfo WeatherFactorField
        {
            get
            {
                if (!fieldTried)
                {
                    fieldTried = true;
                    weatherFactorField = AccessTools.Field(typeof(ShotReport), "factorFromWeather");
                    if (weatherFactorField == null)
                    {
                        BinguinLogUtility.Log("野外作战仿生眼：找不到 ShotReport.factorFromWeather，天气免疫未生效。", severity: 2, isDebug: false);
                    }
                }
                return weatherFactorField;
            }
        }

        // ShotReport.HitReportFor 的 postfix：把"天气精度倍率"改回 1
        //
        // ★ 坑：ShotReport 是 struct，而 __result 是 ref 参数。
        //   · AccessTools.FieldRef / StructFieldRef 都用"返回 ref 的委托"，
        //     .NET Framework 4.0 的 csc（C# 5）不支持 ref 返回 → CS0648 编译不过。
        //   · FieldInfo.SetValueDirect 需要 TypedReference，Mono 上不稳。
        //   → 用"装箱往返"：把结构体装箱成 object，在箱子里改字段，
        //     再拆箱赋回 ref 参数（ref 参数赋值调用方可见）。C# 5 完全合法。
        public static void HitReportForPostfix(Thing caster, ref ShotReport __result)
        {
            Pawn pawn = caster as Pawn;
            if (pawn == null)
            {
                return;
            }
            if (!BinguinFieldEyeUtility.HasFieldEye(pawn))
            {
                return;
            }
            FieldInfo f = WeatherFactorField;
            if (f == null)
            {
                return;
            }
            try
            {
                object boxed = __result;
                // ★ 2026-09：1f 直接传会被装箱成新对象（每次命中都分配一个）
                //   → 用静态缓存的那个已装箱 float，FieldInfo.SetValue 会自行拆箱。
                f.SetValue(boxed, BoxedOne);
                __result = (ShotReport)boxed;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("野外作战仿生眼天气免疫写入失败: " + e.Message, severity: 1, isDebug: false);
            }
        }
    }
}
