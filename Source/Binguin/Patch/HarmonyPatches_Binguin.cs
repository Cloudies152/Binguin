// ============================================================================
// 快速滑冰基因 —— Harmony 挂钩实现（方案：直接挂在移动成本计算处）
//
// 思路（来自朋友建议）：不轮询地形，而是在 1.6 计算"进入某格所需成本"的
// 入口 Verse.AI.Pawn_PathFollower.CostToMoveIntoCell 上挂钩：
//   如果该 pawn 带有滑冰基因，且目标格是冰/雪/水面 → 把成本压到
//   "平地一格的成本"（= 1 / moveSpeed，无地形减益），而不是更快。
//
// ★ 1.6 结构（2026-08-17 反射确认）：
//   - Pawn_PathFollower 有两个 CostToMoveIntoCell：
//       [static]  float CostToMoveIntoCell(Pawn pawn, IntVec3 c)   ← 真正的成本计算
//       [instance] float CostToMoveIntoCell(IntVec3 c)             ← 转发：return 静态版(pawn, c)
//   - 只挂静态版，实例版会转发到它，避免同一次移动重复执行。
//   - 手动注册和 [HarmonyPatch] 都应指定重载参数类型；歧义不代表特性注册不可用。
//
// ★ 其他踩坑：
//   - Harmony 版本：官方 brrainz.harmony = 工坊 2009463077（0Harmony v2.4.1 / HarmonyLib 2.x）。
//     不能用 HugsLib 工坊页（818773962）附带的 v1.2.0.1（1.x）编译，否则游戏启动黑屏。
// ============================================================================

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

using Binguin.Feature.Appearance;
using Binguin.Feature.Diplomacy;
using Binguin.Feature.Fishing;
using Binguin.Feature.RaceTraits;
using Binguin.Feature.Raids;

using Binguin.Helper;

namespace Binguin.Patch
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatches_Binguin
    {
        // ★★★ 2026-09 重大加固（本轮实测抓到的真凶）★★★
        //   每个补丁【单独 try/catch】。历史教训：Patch_BinguinRaidPoints 的前缀把
        //   原版参数名 raidStrategy 写成了 strategy → Harmony 抛
        //   "Parameter "strategy" not found in method ..." → 【整个静态构造中断】→
        //   它后面注册的 5 条补丁（袭击点数 / 袭击单位门槛 / 袭击装备 /
        //   低温围攻蓝图 / 低温围攻冲锋判定）全部静默失效，
        //   玩家表现为「低温围攻完全没实现，还是原版迫击炮围攻」。
        //   现在任何单条补丁挂载失败都只影响它自己，并在日志里明确报出来。
        private static int patchOk;
        private static int patchFail;

        private static HarmonyMethod HM(Type type, string method)
        {
            if (type == null || string.IsNullOrEmpty(method))
            {
                return null;
            }
            MethodInfo mi = type.GetMethod(method,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return mi == null ? null : new HarmonyMethod(mi);
        }

        private static void SafePatch(Harmony harmony, MethodInfo target, string label,
            Type prefixType, string prefixMethod, Type postfixType, string postfixMethod)
        {
            try
            {
                if (target == null)
                {
                    patchFail++;
                    BinguinLogUtility.Log("未找到目标方法，补丁未挂载：" + label, severity: 1, isDebug: false);
                    return;
                }
                HarmonyMethod pre = HM(prefixType, prefixMethod);
                HarmonyMethod post = HM(postfixType, postfixMethod);
                if (pre == null && post == null)
                {
                    patchFail++;
                    BinguinLogUtility.Log("补丁方法未找到，补丁未挂载：" + label, severity: 1, isDebug: false);
                    return;
                }
                harmony.Patch(target, prefix: pre, postfix: post);
                patchOk++;
                BinguinLogUtility.Log("补丁已挂载：" + label);
            }
            catch (Exception e)
            {
                patchFail++;
                BinguinLogUtility.Log("补丁挂载失败（只影响这一条，其余补丁照常）：" + label
                    + " -> " + e.Message, severity: 2, isDebug: false);
            }
        }

        static HarmonyPatches_Binguin()
        {
            Harmony harmony = new Harmony("binguin.race");

            // 静态版（真计算）：static float CostToMoveIntoCell(Pawn, IntVec3)
            MethodInfo staticTarget = typeof(Pawn_PathFollower).GetMethod(
                "CostToMoveIntoCell",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (staticTarget != null)
            {
                SafePatch(harmony, staticTarget, "滑冰：Pawn_PathFollower.CostToMoveIntoCell（静态版；实例版为纯转发，无需重复挂钩）",
                    null, null, typeof(Patch_PawnPathFollower_CostToMoveIntoCell), "PostfixStatic");
            }

            // ★ 2026-09 性能优化：不再给实例版 CostToMoveIntoCell(IntVec3) 挂补丁。
            //   IL 实证（Tools/IlDump.ps1）该实例方法的函数体只有三行：
            //       ldarg.0 / ldfld pawn / ldarg.1 / call 静态版 / ret
            //   即【纯转发】——静态版 postfix 已经把结果改好了，实例 postfix 是
            //   完全重复的第二遍计算（还要额外一次 FieldInfo.GetValue 反射装箱）。
            //   而这个是全游戏最热的路径（寻路每评估一格调用一次），
            //   去掉后路径成本计算的开销减半，行为完全不变。
            // （日志由 SafePatch 统一输出，这里不再重复打一条）

            // 企鹅滑板无品质（2026-08-18 用户要求）：
            // 1.6 品质判定统一走 QualityUtility.TryGetQuality（内部查 Thing.GetComp<CompQuality>()）。
            // 滑板 def 无 CompQuality → 本应无品质；但制作/生成路径可能被原版附加品质，
            // 这里直接对滑板强制返回"无品质"，保证任何 UI（物品栏/详情/装备栏）都不显示品质。
            MethodInfo tryGetQuality = typeof(QualityUtility).GetMethod(
                "TryGetQuality",
                new Type[] { typeof(Thing), typeof(QualityCategory).MakeByRefType() });
            if (tryGetQuality != null)
            {
                SafePatch(harmony, tryGetQuality, "滑板无品质：QualityUtility.TryGetQuality",
                    typeof(Patch_QualityUtility_TryGetQuality), "Prefix", null, null);
            }
            else
            {
                BinguinLogUtility.Log("未找到 QualityUtility.TryGetQuality，滑板无品质补丁未挂载。", severity: 1, isDebug: false);
            }

            // 滑行免疫表现（2026-08-19 用户要求）：
            //   1) 滑行中被命中 → 显示小字号 "miss" 浮字 + 伤害清零（双保险）
            //   2) 取消受击动画：跳过 Pawn_DrawTracker.Notify_DamageApplied（抖动/闪白）
            //     与 Pawn_StanceTracker.Notify_DamageTaken（击退）
            // ★ 2026-08-19 重大修复：必须用 typeof(Thing) 查找 TakeDamage！
            //   用 typeof(Pawn).GetMethod("TakeDamage", ...) 拿到的 MethodInfo 会被
            //   Harmony 拒绝（"You can only patch implemented methods/constructors.
            //   Patch the declared method Verse.Thing::TakeDamage instead"），导致
            //   静态构造函数抛异常中断 → 其后的免疫表现补丁与贴地补丁（RecomputePosition
            //   postfix）全部未注册！这正是"贴地动画未生效"的真正根因。
            //   1.6 中 Pawn 未声明自己的 TakeDamage，调用走 Thing.TakeDamage
            //   （非虚），patch Thing 版即可拦截所有 Pawn 的受击。
            MethodInfo takeDamage = typeof(Thing).GetMethod(
                "TakeDamage", new Type[] { typeof(DamageInfo) });
            if (takeDamage != null)
            {
                SafePatch(harmony, takeDamage, "滑行免疫：Thing.TakeDamage（伤害清零 + miss 浮字）",
                    typeof(Patch_SlideImmunity), "TakeDamagePrefix", null, null);
            }
            MethodInfo drawNotify = typeof(Pawn_DrawTracker).GetMethod(
                "Notify_DamageApplied", BindingFlags.Public | BindingFlags.Instance);
            if (drawNotify != null)
            {
                SafePatch(harmony, drawNotify, "滑行免疫：Pawn_DrawTracker.Notify_DamageApplied（取消受击抖动）",
                    typeof(Patch_SlideImmunity), "DrawNotifyPrefix", null, null);
            }
            MethodInfo stanceNotify = typeof(Pawn_StanceTracker).GetMethod(
                "Notify_DamageTaken", BindingFlags.Public | BindingFlags.Instance);
            if (stanceNotify != null)
            {
                SafePatch(harmony, stanceNotify, "滑行免疫：Pawn_StanceTracker.Notify_DamageTaken（取消击退）",
                    typeof(Patch_SlideImmunity), "StanceNotifyPrefix", null, null);
            }

            // 滑板飞行器"低飞近似贴地"（2026-08-19 用户要求不要"跳起来"）：
            // ★ 渲染入口是 PawnFlyer.DynamicDrawPhaseAt（不是 DrawAt）——它内部先
            //   RecomputePosition() 重算 effectivePos（含飞行高度），再渲染小人。
            //   因此 patch RecomputePosition 的 postfix：滑板飞行器算完后把高度压平。
            MethodInfo recomputePos = typeof(PawnFlyer).GetMethod(
                "RecomputePosition",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (recomputePos != null)
            {
                SafePatch(harmony, recomputePos, "滑板飞行贴地：PawnFlyer.RecomputePosition",
                    null, null, typeof(Patch_SlideImmunity), "RecomputePositionPostfix");
            }
            else
            {
                BinguinLogUtility.Log("未找到 PawnFlyer.RecomputePosition，贴地补丁未挂载。", severity: 1, isDebug: false);
            }

            if (takeDamage != null || drawNotify != null || stanceNotify != null)
            {
                BinguinLogUtility.Log("滑行免疫表现补丁已挂载：miss 浮字 + 取消受击动画");
            }
            else
            {
                BinguinLogUtility.Log("滑行免疫表现补丁挂载失败（未找到目标方法）。", severity: 1, isDebug: false);
            }

            // 野外作战仿生眼（2026-09）：开枪无视天气精度减益。
            // 反编译确认：全程序集只有 Verse.ShotReport.HitReportFor 读取
            // WeatherManager.CurWeatherAccuracyMultiplier，postfix 把
            // factorFromWeather 改回 1 即可完全免疫天气减益。
            MethodInfo hitReportFor = AccessTools.Method(typeof(ShotReport), "HitReportFor");
            if (hitReportFor != null)
            {
                SafePatch(harmony, hitReportFor, "野外作战仿生眼：ShotReport.HitReportFor（无视天气减益）",
                    null, null, typeof(Patch_BinguinFieldEye), "HitReportForPostfix");
            }
            else
            {
                BinguinLogUtility.Log("未找到 ShotReport.HitReportFor，仿生眼天气免疫未生效。", severity: 1, isDebug: false);
            }

            // 通讯台招募 → 默认冰鹅族殖民者（2026-09 用户需求）。
            // 给 PawnGenerator.GeneratePawn(PawnGenerationRequest) 挂 prefix：
            // 玩家派系（冰鹅族剧本）请求生成人形 pawn 时，直接把 kind 换成
            // 开局池里的 Binguin_Colonist。
            MethodInfo generatePawn = AccessTools.Method(typeof(PawnGenerator), "GeneratePawn",
                new Type[] { typeof(PawnGenerationRequest) });
            if (generatePawn != null)
            {
                SafePatch(harmony, generatePawn, "招募默认冰鹅族殖民者：PawnGenerator.GeneratePawn",
                    typeof(Patch_BinguinPlayerRecruit), "GeneratePawnPrefix", null, null);
            }
            else
            {
                BinguinLogUtility.Log("未找到 PawnGenerator.GeneratePawn，招募补丁未挂载。", severity: 1, isDebug: false);
            }

            // ---- 钓到企鹅！（2026-09-24 用户需求）----
            // 原版钓鱼：FishingUtility.GetCatchesFor 的 postfix —— 钓到东西之后，
            // 按 0.78% 概率把渔获换成一名倒地（麻痹症 2 天）的冰鹅难民，60 天冷却。
            // ★ mod 自己的鱼池不走这里，它在 JobDriver_BinguinPondFish 里直接调
            //   BinguinFishingCatchUtility.TryReplaceCatch（见 Fishing_Binguin.cs）。
            // ★ 蟹笼【故意不挂】（自动产出，挂了会变成挂机刷冰鹅），理由写在 Fishing_Binguin.cs 顶部。
            // ★ 找不到也【不算错误】：没装 Odyssey 时原版钓鱼本来就不存在，
            //   这时只影响"原版水域"那一路，鱼池照样能钓到冰鹅。
            MethodInfo getCatches = AccessTools.Method(typeof(FishingUtility), "GetCatchesFor",
                new Type[] { typeof(Pawn), typeof(IntVec3), typeof(bool), typeof(bool).MakeByRefType() });
            if (getCatches != null)
            {
                SafePatch(harmony, getCatches, "钓到企鹅！原版钓鱼区：FishingUtility.GetCatchesFor",
                    typeof(Patch_BinguinFishingCatch), "Postfix", null, null);
            }
            else
            {
                BinguinLogUtility.Log("未找到 FishingUtility.GetCatchesFor（未装奥德赛？）"
                    + "——原版水域不会钓到冰鹅，鱼池/蟹笼不受影响。");
            }

            // ---- 冰鹅族袭击编成（2026-09 用户需求）----
            // ① 记录"袭击策略计算前"的点数：IncidentWorker_Raid.AdjustedRaidPoints 第 1 个参数
            MethodInfo adjustedPoints = AccessTools.Method(typeof(IncidentWorker_Raid), "AdjustedRaidPoints");
            if (adjustedPoints != null)
            {
                SafePatch(harmony, adjustedPoints, "袭击点数记录：IncidentWorker_Raid.AdjustedRaidPoints",
                    typeof(Patch_BinguinRaidPoints), "Prefix", null, null);
            }
            else
            {
                BinguinLogUtility.Log("未找到 AdjustedRaidPoints，袭击点数门槛将退回使用调整后的点数。", severity: 1, isDebug: false);
            }

            // ② 生成前：点数门槛 + 每次袭击数量上限（挂在同一个 GeneratePawn 上）
            if (generatePawn != null)
            {
                SafePatch(harmony, generatePawn, "袭击单位门槛：哨骑≥2000 / 护卫≥6000 / 领袖≥8000，护卫≤2、领袖≤1",
                    typeof(Patch_BinguinRaidKinds), "GeneratePawnPrefix", null, null);
            }

            // ③ 整队生成完毕后：武器/背包/防毒面具后处理
            MethodInfo makeLords = AccessTools.Method(typeof(RaidStrategyWorker), "MakeLords");
            if (makeLords != null)
            {
                SafePatch(harmony, makeLords, "袭击装备后处理：RaidStrategyWorker.MakeLords",
                    typeof(Patch_BinguinRaidGear), "MakeLordsPrefix", null, null);
            }
            else
            {
                BinguinLogUtility.Log("未找到 RaidStrategyWorker.MakeLords，袭击装备后处理未挂载。", severity: 1, isDebug: false);
            }

            // ④ 到达方式强制（2026-09 用户定稿）：只对冰鹅派系
            //    —— 空投两项走自己的策略，其余一律徒步进场（见 RaidComposition_Binguin.cs）
            MethodInfo resolveArrive = AccessTools.Method(typeof(IncidentWorker_Raid), "TryResolveRaidArriveMode");
            if (resolveArrive != null)
            {
                SafePatch(harmony, resolveArrive, "袭击到达方式（空投 / 徒步）：IncidentWorker_Raid.TryResolveRaidArriveMode",
                    typeof(Patch_BinguinRaidArrival), "Prefix", null, null);
            }
            else
            {
                BinguinLogUtility.Log("未找到 TryResolveRaidArriveMode，到达方式未接管（空投比例按原版）。", severity: 1, isDebug: false);
            }

            // ---- 冰鹅族不生成人类发型（2026-09 用户实测：画了企鹅头之后
            //      "头部不对"——原版头发会以人头的挂点叠在企鹅头顶）----
            MethodInfo randomHair = AccessTools.Method(typeof(PawnStyleItemChooser), "RandomHairFor",
                new Type[] { typeof(Pawn) });
            if (randomHair != null)
            {
                SafePatch(harmony, randomHair, "冰鹅族不生成人类发型：PawnStyleItemChooser.RandomHairFor",
                    typeof(Patch_BinguinNoHair), "Prefix", null, null);
            }
            else
            {
                BinguinLogUtility.Log("未找到 PawnStyleItemChooser.RandomHairFor，冰鹅头上会叠人类发型。", severity: 1, isDebug: false);
            }

            // ---- 冰鹅族【饥饱阈值】（2026-10-01 用户需求）----
            // 背景：营养上限抬到 1.2 后，"开始饿"的绝对营养值会跟着抬高（阈值按百分比算）：
            //       上限 1.0 时 0.45×0.8×1.0 = 0.36；上限 1.2 时变成 0.432。
            //   用户要求把"开始饿"的绝对位置拉回改版前的 0.36。
            // ★ 目标 `RaceProperties.FoodLevelPercentageWantEat` 是【计算属性】
            //   （switch ResolvedDietCategory 直接返回常数），没有 XML 字段可覆盖
            //   ⇒ 只能挂 Harmony 后置补丁。推导见 Source/FoodThreshold_Binguin.cs。
            MethodInfo wantEat = AccessTools.PropertyGetter(
                typeof(RaceProperties), "FoodLevelPercentageWantEat");
            if (wantEat != null)
            {
                SafePatch(harmony, wantEat,
                    "冰鹅族饥饱阈值（上限 1.2 下把开始饿的绝对值拉回 0.36）：RaceProperties.FoodLevelPercentageWantEat",
                    null, null,
                    typeof(Patch_BinguinFoodThreshold), "Postfix");
            }
            else
            {
                BinguinLogUtility.Log("未找到 RaceProperties.FoodLevelPercentageWantEat，"
                    + "饥饱阈值未调整（上限仍是 1.2，开始饿会变成 0.432）。", severity: 1, isDebug: false);
            }

            // ---- 臭鱼罐头：把臭鱼味记忆挂给站在臭气里的 pawn
            //      （2026-10-05 重做版；旧的自定义 hediff 方案已废弃删除）----
            // 目标 `Verse.GasUtility.PawnGasEffectsTickInterval(Pawn, int)`：public static，
            // 由 `Pawn.TickInterval` 直接调用 ⇒ 只要小人在气里就必然执行
            // （诊断日志已实测到它确实被调用：`Rin(Human) density=11`）。
            // ★ 心情本身由 ThoughtWorker_BinguinStinkFish 承担（挂在 ThoughtDef 的
            //   workerClass 上，见 XML）；这里只负责"保证那条记忆存在/被清理"。
            MethodInfo gasTick = AccessTools.Method(typeof(GasUtility), "PawnGasEffectsTickInterval");
            if (gasTick == null)
            {
                // 兜底：显式指定参数类型再查一次（按名查法有时会因重载/解析问题返回 null）
                gasTick = AccessTools.Method(typeof(GasUtility), "PawnGasEffectsTickInterval",
                    new Type[] { typeof(Pawn), typeof(int) });
            }
            if (gasTick == null)
            {
                gasTick = typeof(GasUtility).GetMethod("PawnGasEffectsTickInterval",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                    | BindingFlags.Instance);
            }
            if (gasTick != null)
            {
                BinguinLogUtility.Log("臭鱼罐头气体挂钩目标已锁定：" + gasTick);
                // ① 挂臭鱼味记忆（正式功能）
                SafePatch(harmony, gasTick,
                    "臭鱼罐头臭鱼味：GasUtility.PawnGasEffectsTickInterval -> Patch_BinguinStinkFishThought",
                    null, null,
                    typeof(Patch_BinguinStinkFishThought), "Postfix");
            }
            else
            {
                BinguinLogUtility.Log("未找到 GasUtility.PawnGasEffectsTickInterval，"
                    + "臭鱼味心情不会生效（气体本身仍然生效）。", severity: 1, isDebug: false);
            }

            // ---- 冰鹅族低温围攻（2026-09 用户需求）----
            // ① 把原版围攻的蓝图换成：大气冷冻器 + 2 台自动加农炮 + 掩体沙袋
            //    （SiegeBlueprintPlacer 是 SEALED+ABSTRACT 的静态类，PlaceBlueprints 是迭代器
            //      → 只能用 prefix 给 __result 塞自己的序列并 return false）
            MethodInfo placeBlueprints = AccessTools.Method(typeof(SiegeBlueprintPlacer), "PlaceBlueprints");
            if (placeBlueprints != null)
            {
                SafePatch(harmony, placeBlueprints, "低温围攻蓝图：SiegeBlueprintPlacer.PlaceBlueprints",
                    typeof(Patch_BinguinCryoSiegeBlueprints), "Prefix", null, null);
            }
            else
            {
                BinguinLogUtility.Log("未找到 SiegeBlueprintPlacer.PlaceBlueprints，低温围攻将搭原版迫击炮阵地。", severity: 1, isDebug: false);
            }

            // ①b 识别：围攻 toil 初始化时判断"这是不是我们的低温围攻"
            //    （蓝图就是在 Init 里放的 —— 用 LordJob 类型判定，最可靠）
            MethodInfo siegeInit = AccessTools.DeclaredMethod(typeof(LordToil_Siege), "Init");
            if (siegeInit != null)
            {
                SafePatch(harmony, siegeInit, "低温围攻 toil 识别：LordToil_Siege.Init",
                    typeof(Patch_BinguinCryoSiegeToilInit), "Prefix", null, null);
            }
            else
            {
                BinguinLogUtility.Log("未找到 LordToil_Siege.Init，低温围攻只能靠阵地记录识别。", severity: 1, isDebug: false);
            }

            // ①d 毒垃圾报复：拦原版"丢垃圾→报复"的分流，冰鹅族走我们自己的版本
            //    （原版 CompDissolutionEffect_Goodwill.TriggerRetaliationEvent 是
            //      private static，Harmony 补得了）
            MethodInfo wasteRetaliation = AccessTools.Method(
                typeof(CompDissolutionEffect_Goodwill), "TriggerRetaliationEvent");
            if (wasteRetaliation != null)
            {
                SafePatch(harmony, wasteRetaliation, "毒垃圾报复（冰鹅特制）：CompDissolutionEffect_Goodwill.TriggerRetaliationEvent",
                    typeof(Patch_BinguinWasteRetaliation), "Prefix", null, null);
            }
            else
            {
                BinguinLogUtility.Log("未找到 TriggerRetaliationEvent，毒垃圾报复事件未挂载。", severity: 1, isDebug: false);
            }

            // ①c2 建造者限定为工兵（两层，见 Patch_BinguinCryoSiegeBuilders 类注释）
            //    ① 资格层：CanBeBuilder 判 false（私有方法，Harmony 补得了，
            //       但它只有约 10 条 IL，可能被 JIT 内联 → 单靠它不保险）
            MethodInfo canBeBuilder = AccessTools.Method(typeof(LordToil_Siege), "CanBeBuilder");
            if (canBeBuilder != null)
            {
                SafePatch(harmony, canBeBuilder, "低温围攻只管工兵建造①：LordToil_Siege.CanBeBuilder",
                    null, null, typeof(Patch_BinguinCryoSiegeBuilders), "CanBeBuilderPostfix");
            }
            else
            {
                BinguinLogUtility.Log("未找到 LordToil_Siege.CanBeBuilder，改用职责重分配兜底。", severity: 1, isDebug: false);
            }

            //    ② 兜底层：UpdateAllDuties 分完职责后重分配（public virtual，一定跑得到）
            MethodInfo updateDuties = AccessTools.Method(typeof(LordToil_Siege), "UpdateAllDuties");
            if (updateDuties != null)
            {
                SafePatch(harmony, updateDuties, "低温围攻只管工兵建造②：LordToil_Siege.UpdateAllDuties",
                    null, null, typeof(Patch_BinguinCryoSiegeBuilders), "UpdateAllDutiesPostfix");
            }
            else
            {
                BinguinLogUtility.Log("未找到 LordToil_Siege.UpdateAllDuties，建造者限定可能无效。", severity: 1, isDebug: false);
            }

            // ①c 低温围攻不掉迫击炮弹：拦 LordToil_Siege.DropSupplies
            MethodInfo dropSupplies = AccessTools.DeclaredMethod(typeof(LordToil_Siege), "DropSupplies");
            if (dropSupplies != null)
            {
                SafePatch(harmony, dropSupplies, "低温围攻不掉炮弹：LordToil_Siege.DropSupplies",
                    typeof(Patch_BinguinCryoSiegeNoShells), "Prefix", null, null);
            }
            else
            {
                BinguinLogUtility.Log("未找到 LordToil_Siege.DropSupplies，低温围攻仍会掉迫击炮弹。", severity: 1, isDebug: false);
            }

            // ② 冲锋时机：减员 30% 或工兵全灭 → GotoToil(突击)
            MethodInfo siegeTick = AccessTools.Method(typeof(LordToil_Siege), "LordToilTick");
            if (siegeTick != null)
            {
                SafePatch(harmony, siegeTick, "低温围攻冲锋判定：LordToil_Siege.LordToilTick",
                    null, null, typeof(Patch_BinguinCryoSiegeAssault), "Postfix");
            }
            else
            {
                BinguinLogUtility.Log("未找到 LordToil_Siege.LordToilTick，冲锋判定未挂载。", severity: 1, isDebug: false);
            }

            // ★ 汇总：以后只要看到「失败/跳过 > 0」就说明有条补丁没挂上（日志上方有具体原因）
            if (patchFail > 0)
            {
                BinguinLogUtility.Log("补丁注册完成：成功 " + patchOk + " 条，失败/跳过 " + patchFail + " 条 —— 请看上方具体原因！", severity: 2, isDebug: false);
            }
            else
            {
                BinguinLogUtility.Log("补丁注册完成：成功 " + patchOk + " 条，无失败。");
            }
        }
    }

    // 补丁方法由上方显式注册；如迁移为特性注册，应指定重载并移除对应的手动注册。
    public static class Patch_PawnPathFollower_CostToMoveIntoCell
    {
        private static GeneDef cachedSkatingGene;
        private static bool skatingGeneLookedUp;

        // ★ 2026-09 性能：这里原来用 "== null" 当"没查过"的哨兵——一旦基因 def
        //   不存在（改名/未加载），每个寻路格都会重新查一次 DefDatabase。
        //   改为独立 bool 标记，查过一次就不再查（本方法在寻路热路径上）。
        private static GeneDef SkatingGene
        {
            get
            {
                if (!skatingGeneLookedUp)
                {
                    skatingGeneLookedUp = true;
                    cachedSkatingGene = DefDatabase<GeneDef>.GetNamedSilentFail("Binguin_Gene_Skating");
                }
                return cachedSkatingGene;
            }
        }

        // 静态版 postfix（真计算处；__result 是 Harmony 特殊参数，必须用此名）
        public static void PostfixStatic(Pawn pawn, ref float __result, IntVec3 c)
        {
            ApplySkating(pawn, ref __result, c);
        }

        // ★ 2026-09：实例版 postfix 已删除（见文件头说明——实例方法为纯转发，
        //   静态版 postfix 覆盖全部调用路径，原实现是重复计算）。

        private static void ApplySkating(Pawn pawn, ref float __result, IntVec3 c)
        {
            if (__result <= 0f)
            {
                return;
            }
            GeneDef gene = SkatingGene;
            if (gene == null)
            {
                return;
            }
            if (pawn == null || pawn.genes == null || !pawn.genes.HasActiveGene(gene))
            {
                return;
            }
            Map map = pawn.Map;
            if (map == null)
            {
                return;
            }
            if (IsIceSnowOrWater(map, c))
            {
                // 平地一格成本 ≈ FlatCellCostFactor / moveSpeed。
                // 校准记录（2026-08-17）：
                //   F=1  → 比普通人在平地快约 20 倍
                //   F=20 → 比普通人在平地快约 2 倍
                //   F=40 → 平地速度（当前值）。若仍偏快/偏慢，调整此常量。
                float moveSpeed = pawn.GetStatValue(StatDefOf.MoveSpeed);
                float flatCost = FlatCellCostFactor / (moveSpeed > 0.01f ? moveSpeed : 0.01f);
                if (__result > flatCost)
                {
                    __result = flatCost;
                }
            }
        }

        // 用户实测校准：真实平地一格成本 ≈ 40 / 移速（2026-08-17 第二次校准）
        private const float FlatCellCostFactor = 40f;

        private static bool IsIceSnowOrWater(Map map, IntVec3 cell)
        {
            // 雪是地形覆盖层：有积雪即视为雪面
            if (map.snowGrid.GetDepth(cell) > 0.05f)
            {
                return true;
            }
            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain == null)
            {
                return false;
            }
            string name = terrain.defName;
            // 原版冰面/水面 + 本 MOD 的冰鹅地板（Binguin_*，见 Defs/Feature/Floors/TerrainDefs_Floors.xml）
            return name.StartsWith("Ice", StringComparison.Ordinal)
                || name.StartsWith("Water", StringComparison.Ordinal)
                || name.StartsWith("Binguin_", StringComparison.Ordinal);
        }
    }

    // 企鹅滑板无品质：Prefix 返回 false = 跳过原 TryGetQuality，视为无品质（qc=Normal）
    // ★ 注意：TryGetQuality 的签名是 TryGetQuality(Thing t, out QualityCategory qc)，
    //   Harmony prefix 里 out 参数要用原参数名 qc（ref），不能用 __result（那绑定到返回值 bool）。
    public static class Patch_QualityUtility_TryGetQuality
    {
        public static bool Prefix(Thing t, ref QualityCategory qc)
        {
            if (t != null && t.def != null && t.def.defName == "Binguin_PenguinBoard")
            {
                qc = QualityCategory.Normal;
                return false;
            }
            return true;
        }
    }

    // 滑行免疫表现：滑行（Binguin_SlideImmunity hediff）期间被命中 →
    // 伤害清零 + 显示小字号 "miss" 浮字 + 取消受击动画/击退
    public static class Patch_SlideImmunity
    {
        private static readonly System.Reflection.FieldInfo DrawTrackerPawnField =
            AccessTools.Field(typeof(Pawn_DrawTracker), "pawn");

        private static HediffDef cachedImmunity;
        private static bool immunityDefLookedUp;

        // ★ 2026-09：同上——避免 def 缺失时每次受击事件都重查 DefDatabase
        private static HediffDef ImmunityDef
        {
            get
            {
                if (!immunityDefLookedUp)
                {
                    immunityDefLookedUp = true;
                    cachedImmunity = DefDatabase<HediffDef>.GetNamedSilentFail("Binguin_SlideImmunity");
                }
                return cachedImmunity;
            }
        }

        private static bool HasImmunity(Pawn pawn)
        {
            return pawn != null && pawn.health != null && ImmunityDef != null
                && pawn.health.hediffSet.GetFirstHediffOfDef(ImmunityDef) != null;
        }

        // 1) 被命中时（Thing.TakeDamage，1.6 中 Pawn 未 override，统一走这里）：
        //    伤害清零 + 显示 miss 浮字。注意 __instance 类型是 Thing（非 Pawn），
        //    因为 patch 目标是 Thing.TakeDamage，prefix 参数类型必须与之一致。
        public static void TakeDamagePrefix(Thing __instance, ref DamageInfo dinfo)
        {
            Pawn pawn = __instance as Pawn;
            if (pawn != null && HasImmunity(pawn))
            {
                dinfo.SetAmount(0f);
                ShowMiss(pawn);
            }
        }

        private static void ShowMiss(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || !pawn.Spawned)
            {
                return;
            }
            try
            {
                MoteText mote = (MoteText)ThingMaker.MakeThing(ThingDefOf.Mote_Text);
                mote.exactPosition = pawn.Position.ToVector3Shifted();
                mote.text = "miss";
                mote.textColor = new UnityEngine.Color(0.75f, 0.8f, 1f);
                mote.overrideTimeBeforeStartFadeout = 0.6f;
                mote.linearScale = new UnityEngine.Vector3(0.7f, 0.7f, 0.7f); // 小字号
                GenSpawn.Spawn(mote, pawn.Position, pawn.Map);
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("miss 浮字创建失败: " + e.Message, severity: 1, isDebug: false);
            }
        }

        // 2) 受击动画（Pawn_DrawTracker.Notify_DamageApplied）：免疫中跳过
        public static bool DrawNotifyPrefix(Pawn_DrawTracker __instance)
        {
            Pawn pawn = null;
            if (__instance != null && DrawTrackerPawnField != null)
            {
                pawn = (Pawn)DrawTrackerPawnField.GetValue(__instance);
            }
            return !HasImmunity(pawn);
        }

        // 3) 击退（Pawn_StanceTracker.Notify_DamageTaken）：免疫中跳过
        public static bool StanceNotifyPrefix(Pawn_StanceTracker __instance)
        {
            Pawn pawn = __instance != null ? __instance.pawn : null;
            return !HasImmunity(pawn);
        }

        // 4) 贴地滑行飞行器双保险：RecomputePosition 计算完成后压平高度。
        //    ★ 2026-08-19 终极方案：主要靠自定义 worker（GetHeight=0）根治，
        //      此 postfix 仅作双保险。判定改用 __instance.def.defName
        //      （不再依赖飞行中 pawn 的 apparel 状态——那是 v9.2 postfix
        //      可能静默失效的疑点）。
        private static readonly FieldInfo EffectivePosField =
            AccessTools.Field(typeof(PawnFlyer), "effectivePos");
        private static readonly FieldInfo EffectiveHeightField =
            AccessTools.Field(typeof(PawnFlyer), "effectiveHeight");

        public static void RecomputePositionPostfix(PawnFlyer __instance)
        {
            try
            {
                if (__instance == null || __instance.def == null)
                {
                    return;
                }
                if (__instance.def.defName != "Binguin_BoardFlyer")
                {
                    return;
                }
                if (EffectivePosField != null)
                {
                    UnityEngine.Vector3 ep = (UnityEngine.Vector3)EffectivePosField.GetValue(__instance);
                    EffectivePosField.SetValue(__instance, new UnityEngine.Vector3(ep.x, 0f, ep.z));
                }
                if (EffectiveHeightField != null)
                {
                    EffectiveHeightField.SetValue(__instance, 0f);
                }
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("滑板飞行贴地渲染失败: " + e.Message, severity: 1, isDebug: false);
            }
        }

        // ★ 2026-09：删除 WearsBoard()——v9.3 起贴地判定改用飞行器 defName，
        //   全仓库已无调用者（死代码）。
    }
}
