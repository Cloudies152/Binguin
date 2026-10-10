// ============================================================================
// 冰爆状态 comp（2026-08-20 用户需求「突袭冷冻武器」）
//
// 机制（用户 v2 改定）：
//   - 冰爆 = HediffDef Binguin_IceBurst（严重度 0~1 = 0~100%），由自定义
//     DamageWorker_BinguinIceBullet 在【护甲结算前】按武器面板伤害累积：
//     1 点面板伤害 = 1% 冰爆（被护甲完全挡掉也照样积累）。
//   - 本 comp 每 tick 检查：严重度 >= 1（100%）→ **纯随机**摧毁目标一个
//     部位（含头部/大脑/核心等一切未缺失部位，抽到致命部位即死亡，
//     用户 v2 明确要求包含头部；不做任何排除）——触发后严重度 -1
//     （重新积累，可再次触发），**不弹消息**（战斗频繁会刷屏）。
//   - 摧毁方式 = 原版截肢语义：先移除该部位及全部子部位上的旧伤口/hediff，
//     再添加 MissingBodyPart（子树由原版 PartIsMissing 沿父链判定为缺失）。
//
// ★ XML 零自定义类型：comps 由 BinguinDefPatches 静态构造挂载到 HediffDef。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Binguin
{
    public class HediffCompProperties_BinguinIceBurst : HediffCompProperties
    {
        public HediffCompProperties_BinguinIceBurst()
        {
            compClass = typeof(HediffComp_BinguinIceBurst);
        }
    }

    public class HediffComp_BinguinIceBurst : HediffComp
    {
        // 每个游戏 tick 调用（兜底：命中时已同步触发，这里防其它来源的严重度）
        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead || pawn.health == null || pawn.health.hediffSet == null)
            {
                return;
            }
            if (parent.Severity >= 1f - 0.001f)
            {
                // 消耗 100% 冰爆 → 冻碎一个随机部位（纯随机，含头部/核心）
                parent.Severity -= 1f;
                TryDestroyRandomPart(pawn);
            }
        }

        // 供 DamageWorker_BinguinIceBullet 命中时同步调用（100% 立即触发，
        // 不依赖 tick；comp 的 CompPostTick 仅兜底，双保险不会重复——
        // 触发后 severity 已 -1 < 1）
        public static void TryDestroyRandomPart(Pawn pawn)
        {
            // ★ 2026-09 性能：GetNotMissingParts 返回的是 IEnumerable（内部新建），
            //   原来手工 foreach 拷贝 —— 改成 List 构造器一次拷贝（少一次枚举开销，
            //   并保证 Count/RandomElement 仍是 O(1)）。
            List<BodyPartRecord> candidates = new List<BodyPartRecord>(
                pawn.health.hediffSet.GetNotMissingParts(
                    BodyPartHeight.Undefined, BodyPartDepth.Undefined));
            if (candidates.Count == 0)
            {
                return;
            }
            BodyPartRecord target = candidates.RandomElement<BodyPartRecord>();

            // 1) 先清掉该部位与全部子部位上的旧伤/状态（截肢语义）
            // ★ 2026-09 性能：子树成员判断改用 HashSet（原为 List.Contains，
            //   与 hediff 数量相乘是 O(hediff × 子树)）。
            List<BodyPartRecord> subtree = new List<BodyPartRecord>();
            CollectSubtree(target, subtree);
            HashSet<BodyPartRecord> subtreeSet = new HashSet<BodyPartRecord>(subtree);
            List<Hediff> toRemove = new List<Hediff>();
            for (int i = 0; i < pawn.health.hediffSet.hediffs.Count; i++)
            {
                Hediff h = pawn.health.hediffSet.hediffs[i];
                if (h != null && h.Part != null && subtreeSet.Contains(h.Part))
                {
                    toRemove.Add(h);
                }
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                pawn.health.RemoveHediff(toRemove[i]);
            }

            // 2) 添加缺失（原版语义：该部位及其下所有子部位视为失去；
            //    ★ 不弹消息——战斗命中频繁会刷屏，用户 v2 要求静默）
            Hediff missing = HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn, target);
            pawn.health.AddHediff(missing, target, null, null);
        }

        private static void CollectSubtree(BodyPartRecord part, List<BodyPartRecord> list)
        {
            list.Add(part);
            if (part.parts != null)
            {
                for (int i = 0; i < part.parts.Count; i++)
                {
                    if (part.parts[i] != null)
                    {
                        CollectSubtree(part.parts[i], list);
                    }
                }
            }
        }
    }
}
