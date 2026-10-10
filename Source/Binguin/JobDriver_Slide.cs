// ============================================================================
// 滑行冲刺 —— 企鹅滑板（腰部装备）的能力（跳跃背包式快速滑行）
//
// 2026-08-19 用户要求改为"像跳跃背包"的效果：
//   1. 快速移动：每格 1 tick（10 格 ≈ 0.17 秒滑完），接近瞬移但保留滑行轨迹
//   2. 越过敌人：逐格直接赋值 Position（穿过敌人占据的格子），LOS 检查
//      只挡墙（GenSight.LineOfSight：建筑挡视线，敌人不挡）
//   3. 移动时不可被敌人命中：滑行期间添加 Binguin_SlideImmunity hediff
//      （IncomingDamageFactor = 0，伤害归零），toil finishActions 兜底移除
//
// 2026-08-17：用户反馈"瞬移不太行，改成类似滑行动画" → 由直接位移改为本 JobDriver。
// 2026-08-18：命令来源由 Verb 改为 CompBinguinBoard（1.6 Verb 基类无 virtual GetGizmos）。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class JobDriver_Slide : JobDriver
    {
        private IntVec3 startPos;
        private IntVec3 destPos;
        private int totalTicks;
        private int ticksLeft;

        // ★ 2026-09 性能：这两个 def 名是常量，原来 tickAction 里【每 tick】查一次
        //   DefDatabase（滑行期间 60 次/秒）→ 改为懒加载静态缓存。
        private static HediffDef cachedImmunityDef;
        private static bool immunityLookedUp;
        private static FleckDef cachedIceFleck;
        private static bool iceFleckLookedUp;

        private static HediffDef ImmunityDef
        {
            get
            {
                if (!immunityLookedUp)
                {
                    immunityLookedUp = true;
                    cachedImmunityDef = DefDatabase<HediffDef>.GetNamedSilentFail("Binguin_SlideImmunity");
                }
                return cachedImmunityDef;
            }
        }

        private static FleckDef IceFleck
        {
            get
            {
                if (!iceFleckLookedUp)
                {
                    iceFleckLookedUp = true;
                    cachedIceFleck = DefDatabase<FleckDef>.GetNamedSilentFail("Binguin_IceDust");
                }
                return cachedIceFleck;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 注意：不对 TargetIndex.A 做 FailOnDestroyedOrNull —— targetA 是纯坐标
            //（IntVec3，无 thing），该检查在 1.6 可能意外触发导致 job 立即失败。
            yield return new Toil
            {
                initAction = delegate
                {
                    startPos = pawn.Position;
                    destPos = job.GetTarget(TargetIndex.A).Cell;
                    int dist = Math.Max(
                        Math.Abs(destPos.x - startPos.x),
                        Math.Abs(destPos.z - startPos.z));
                    // 跳跃背包式快速移动：每格 1 tick（60 tick/秒 → 10 格约 0.17 秒），最少 5 tick
                    totalTicks = Math.Max(5, dist);
                    ticksLeft = totalTicks;
                    // 移动期间免疫伤害（"无法被敌人命中"）
                    HediffDef immunity = ImmunityDef;
                    if (immunity != null && pawn.health != null
                        && pawn.health.hediffSet.GetFirstHediffOfDef(immunity) == null)
                    {
                        pawn.health.AddHediff(immunity);
                    }
                    Log.Message("[冰鹅族] 滑行冲刺：快速滑行开始 " + startPos + " → " + destPos + "（" + totalTicks + " tick，免疫中）");
                },
                tickAction = delegate
                {
                    ticksLeft--;
                    float t = 1f - ((float)ticksLeft / (float)totalTicks);
                    IntVec3 pos = LerpIntVec3(startPos, destPos, t);
                    if (pos.x != pawn.Position.x || pos.z != pawn.Position.z)
                    {
                        pawn.Position = pos;
                    }
                    // 冰屑拖尾
                    Map map = pawn.Map;
                    if (map != null)
                    {
                        FleckDef fleck = IceFleck;
                        if (fleck != null)
                        {
                            FleckMaker.Static(pawn.Position, map, fleck, 0.5f);
                        }
                    }
                    if (ticksLeft <= 0)
                    {
                        ReadyForNextToil();
                    }
                },
                finishActions = new List<Action>
                {
                    delegate
                    {
                        // 无论完成还是被打断，移除免疫状态
                        if (pawn != null && !pawn.Destroyed && pawn.health != null)
                        {
                            HediffDef immunity = ImmunityDef;
                            if (immunity != null)
                            {
                                Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(immunity);
                                if (h != null)
                                {
                                    pawn.health.RemoveHediff(h);
                                }
                            }
                        }
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Never
            };
            yield return new Toil
            {
                initAction = delegate { }
            };
        }

        // 1.6 的 IntVec3 没有 Lerp；自行实现（仅 x/z 平面插值）
        private static IntVec3 LerpIntVec3(IntVec3 a, IntVec3 b, float t)
        {
            int x = (int)Math.Round((double)a.x + (double)(b.x - a.x) * t);
            int z = (int)Math.Round((double)a.z + (double)(b.z - a.z) * t);
            return new IntVec3(x, 0, z);
        }
    }
}
