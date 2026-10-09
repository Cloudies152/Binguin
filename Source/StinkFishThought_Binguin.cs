// ============================================================================
// 臭鱼罐头 · 臭气心情（2026-10-05 重做版）
//
// ★★ 为什么推倒重做（前两版都失败，证据如下）：
//   诊断日志实测（用户测试，智人 Rin）：
//     `[冰鹅族·气体诊断] Rin(Human) 处于臭气中：density=11 ｜ 我的hediff在不在=没有
//        ｜ IsBinguin=False ｜ IsAffectedByExposure=True ｜ 有needs=True ｜ 有mood=True`
//   ⇒ 气体是好的（浓度 11）、种族/needs/mood 全都满足，
//      **而"我的 hediff 在不在 = 没有"** —— 虽然条件 (`density 11 >= 阈值 1`) 已经满足。
//      存档里 `Binguin_StinkFishSmell` 出现 **0 次** ⇒ 那个自定义 hediff 从未被加上。
//   ⇒ 旧方案是"自定义 Hediff + BinguinDefPatches 替换 hediffClass + 靠
//     `Hediff.TickInterval` 检测气体并自己把自己加给 pawn"——
//     这条链有一环没生效，且没能定位到（IL 逐条核对过 TickInterval/PostAdd 都是对的）。
//     **不再在这条路上耗**。
//
// ★ 新方案 = 照抄原版 `ThoughtWorker_RotStink` / `ThoughtWorker_RotStinkLingering`
//   的做法（Core\Defs\ThoughtDefs\Thoughts_Situation_Special.xml 里那两个），
//   用**纯 ThoughtWorker** 承载心情，彻底不要自定义 hediff：
//     · 没有 hediff 就没有"加不上"的问题，也没有存档字段要维护
//     · 心情值直接由 XML 里 ThoughtDef 的 `baseMoodEffect` 给出（-60），
//       不需要跟 `HediffComp_ThoughtSetter` 的 moodOffset 覆盖做加法配合
//     · 持续性用**原版就有的** `Pawn_MindState.lastRotStinkTick`（public int、
//       已被原版 ExposeData 存档）⇒ 3 小时的自然衰减白拿，不用自己写 CompExposeData
//
// ★ 触发条件（与用户最终定稿一致）：
//   · 站在臭气里（`GasUtility.GasDensity(pos, map, GasType.RotStink) > 0`）
//     → 立刻 -60，并不断刷新"最后一次闻到"的时间戳
//   · 离开后 3 小时（7500 tick）内仍然 -60
//   · 冰鹅族不受影响（原版 `GasUtility.IsAffectedByExposure` 对冰鹅族返回 false）
//
// ★★ 气体为什么仍然用原版 `RotStink`（用户问过"为什么不重新定义一个气体"）：
//   不是偷懒，是**原版没给扩展位**（三处 IL 实证）：
//     ① `Verse.GasType` 是原版硬编码枚举：BlindSmoke=0 / ToxGas=8 / RotStink=16 / DeadlifeDust=24
//     ② 气体存在一个 `UInt32[]` 里，`GasGrid.DensityAt` 是
//        `(gasDensity[cell] >> (gasType & 31)) & 255`，而 `SetDirect` 的 IL 是
//        `blind | (toxic << 8) | (rotStink << 16) | (deadlife << 24)`
//        ⇒ **一格只有 4 个字节、四个气体类型已经占满**，没有第 5 个类型的位置
//     ③ 耗散速度也是按类型写死的字段（DissipationAmount_BlindSmoke/_ToxGas/_RotStink/_DeadlifeDust）
//   而且自建类型也无法被 `CompProperties_ReleaseGas` 的 XML `<gasType>` 认出来
//   （那个字段按名字从字符串解析）。
//   ⇒ 所以"重新定义一个气体"在 1.6 里做不了；能做的是**让心情由我自己的
//     ThoughtWorker 负责**，而不再依赖任何跟腐气绑定的东西。这正是本文件。
//   ⚠ 遗留（已知、用户已知悉）：非冰鹅种族吸腐气时**原版 `ThoughtWorker_RotStink`
//     仍会给 -10/-16 心情**（那是原版行为，与本 mod 无关）。要压掉它得另外
//     给那两个原版 ThoughtDef 打补丁，尚未做。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Binguin
{
    /// <summary>
    /// 冰鹅族臭鱼味：站在臭气里 → -60；离开后 3 小时内仍 -60。
    /// XML 里挂在 ThoughtDef `Binguin_StinkFishSmell` 的 `workerClass` 上。
    /// </summary>
    public class ThoughtWorker_BinguinStinkFish : ThoughtWorker
    {
        /// <summary>离开臭气后的延续时长（小时）。用户定稿 3 小时。</summary>
        public const float LingerHours = 3f;

        /// <summary>同上，换算成 tick（1 小时 = 2500 tick）。</summary>
        public const int LingerTicks = (int)(LingerHours * 2500f);   // 7500

        /// <summary>
        /// 气体浓度判定门槛：**只要 &gt; 0 就算闻到**。
        /// ★ 与原版 `ThoughtWorker_RotStink` 完全一致（它的 IL 是 `density != 0` 即 -
        ///   上一版我写了 51，实测浓度只有 11 ⇒ 原版 -10 亮了、我的不亮，这是当时的假象来源）。
        /// </summary>
        private const byte MinDensity = 1;

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            try
            {
                if (p == null || !p.Spawned || p.Map == null || p.mindState == null)
                {
                    return ThoughtState.Inactive;
                }

                byte density = GasUtility.GasDensity(p.Position, p.Map, GasType.RotStink);
                if (density >= MinDensity)
                {
                    // 正在闻：刷新"最后一次闻到"的时间戳。
                    // ★ 借用原版字段（public、原版自己 ExposeData）⇒ 3 小时衰减白拿。
                    p.mindState.lastRotStinkTick = GenTicks.TicksGame;
                    return ThoughtState.ActiveAtStage(0);
                }

                // 不在气里：看是不是还在 3 小时的延续期内
                if (p.mindState.lastRotStinkTick > 0
                    && GenTicks.TicksGame - p.mindState.lastRotStinkTick < LingerTicks)
                {
                    return ThoughtState.ActiveAtStage(0);
                }

                return ThoughtState.Inactive;
            }
            catch (Exception e)
            {
                // ThoughtWorker 抛异常会让思考系统每帧刷屏，这里兜住并只报一次
                Log.WarningOnce("[冰鹅族] 臭鱼味 Thought 异常：" + e.Message, 0x5B17E001);
                return ThoughtState.Inactive;
            }
        }
    }

    /// <summary>
    /// 把臭鱼味记忆挂到/摘离 pawn（替代上一版那个从不生效的自定义 hediff）。
    /// ★ 为什么还要显式挂记忆：本 mod 已有的 `ThoughtWorker_BinguinRodBait` 用的是
    ///   "ThoughtDef 自带 workerClass"这条路，原版虫胶饵那个能正常工作，
    ///   但虫胶饵是**持续状态**（一直在就一直在）。而"离开后还延续 3 小时"这种
    ///   **记忆型**语义，用记忆更稳（原生支持 moodOffset、原生存档、不会被
    ///   nullifyingHediffs 之类误杀）。
    ///   ⇒ 双保险：worker 负责判定与刷新时间戳；本补丁负责保证记忆存在/被清理。
    ///
    /// 挂载点：`GasUtility.PawnGasEffectsTickInterval(Pawn, int)` ——
    ///   public static，被 `Pawn.TickInterval` 直接调用，**只要小人在气里就必然执行**
    ///   （诊断日志已实测到它真的被调用：`Rin(Human) density=11`）。
    /// </summary>
    public static class Patch_BinguinStinkFishThought
    {
        private static ThoughtDef cachedThought;
        private static bool thoughtLookedUp;

        private static ThoughtDef StinkThought
        {
            get
            {
                if (!thoughtLookedUp)
                {
                    thoughtLookedUp = true;
                    cachedThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Binguin_StinkFishSmell");
                }
                return cachedThought;
            }
        }

        public static void Postfix(Pawn pawn)
        {
            try
            {
                if (pawn == null || !pawn.Spawned || pawn.Map == null)
                {
                    return;
                }
                // 只处理"有心情系统"的 pawn（动物没有人格心情）
                if (pawn.needs == null || pawn.needs.mood == null
                    || pawn.needs.mood.thoughts == null || pawn.needs.mood.thoughts.memories == null)
                {
                    return;
                }

                ThoughtDef def = StinkThought;
                if (def == null)
                {
                    return;
                }

                byte density = GasUtility.GasDensity(pawn.Position, pawn.Map, GasType.RotStink);
                MemoryThoughtHandler mem = pawn.needs.mood.thoughts.memories;

                if (density >= 1)
                {
                    // 进/在气里：确保有那条记忆（ThoughtWorker 会持续刷新延续计时）
                    if (mem.GetFirstMemoryOfDef(def) == null)
                    {
                        Thought_Memory m = ThoughtMaker.MakeThought(def) as Thought_Memory;
                        if (m != null)
                        {
                            m.permanent = false;   // 由 ThoughtWorker 的延续窗口决定何时结束
                            mem.TryGainMemory(m, null);
                        }
                    }
                    return;
                }

                // 不在气里：延续期过了就把记忆摘掉
                // （worker 已经返回 Inactive 时，记忆本身也会被游戏判为可弃，
                //   这里显式清一次更干净，避免残留在"心情"面板里）
                if (pawn.mindState != null
                    && pawn.mindState.lastRotStinkTick > 0
                    && GenTicks.TicksGame - pawn.mindState.lastRotStinkTick >= ThoughtWorker_BinguinStinkFish.LingerTicks)
                {
                    if (mem.GetFirstMemoryOfDef(def) != null)
                    {
                        mem.RemoveMemoriesOfDef(def);
                    }
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[冰鹅族] 臭鱼味记忆挂载异常：" + e.Message, 0x5B17E002);
            }
        }
    }
}
