// ============================================================================
// 冰爆弹损伤 worker 体系（2026-08-20）
//
// 原机制（DamageDef.additionalHediffs，按实际结算后伤害累积）在护甲
// 减伤/完全格挡时冰爆会缩水甚至为零——与用户意图不符。
// 用户规格：命中冰爆% = 武器【面板伤害】× 倍率（1 点面板伤害 = 1% 冰爆，
//   终极武器 = 面板伤害的【两倍】冰爆），品质越高面板伤害越高冰爆越猛；
//   即使弹头被护甲完全挡住，面板冰爆依然全额积累。
// 实现：DamageInfo.Amount 在进入 Apply 时 = 该发武器的面板伤害
//   （已含品质系数，护甲尚未结算）→ 先记录，再走原版伤害流程，
//   最后给目标加 面板伤害×倍率 的冰爆严重度（与护甲结果无关）。
//
// worker 家族：
//   DamageWorker_BinguinIceBullet    : DamageWorker_AddInjury  ×1（突击/精确步枪）
//   DamageWorker_BinguinIceBulletUltra : 继承上式          ×2（终极激光机枪）
//   DamageWorker_BinguinBluntUltra   : DamageWorker_Blunt   ×2（终极企鹅飞踢 60 钝伤）
//
// ★ XML 零自定义类型：各 DamageDef 的 workerClass 用原版类占位，
//   BinguinDefPatches 静态构造替换并清 DamageDef.workerInt 缓存。
// ============================================================================

using RimWorld;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.IceCombat
{
    // 共享的冰爆累积逻辑（所有冰爆 worker 共用）
    public static class IceBurstHelper
    {
        private static HediffDef cachedIceBurstDef;

        public static void Add(Pawn pawn, float icePercent)
        {
            if (pawn == null || pawn.Dead || pawn.health == null || pawn.health.hediffSet == null)
            {
                return;
            }
            if (icePercent <= 0f)
            {
                return;
            }
            if (cachedIceBurstDef == null)
            {
                cachedIceBurstDef = DefDatabase<HediffDef>.GetNamedSilentFail("Binguin_IceBurst");
            }
            if (cachedIceBurstDef == null)
            {
                return;
            }
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(cachedIceBurstDef, false);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(cachedIceBurstDef, pawn);
                // ★ 原版新 hediff 默认 severity=0.5 —— 必须归零，从 0% 开始累积
                hediff.Severity = 0f;
                pawn.health.AddHediff(hediff, null, null, null);
                if (!pawn.health.hediffSet.hediffs.Contains(hediff))
                {
                    BinguinLogUtility.Log("冰爆 hediff 添加失败（pawn=" + pawn.LabelShort + "）", severity: 1, isDebug: false);
                    return;
                }
            }
            // 叠加：已有则在同一实例上累加
            hediff.Severity += icePercent * 0.01f;
            // ★ 命中即检查：到 100% 立刻触发冻碎部位并扣除 100%（不依赖 comp tick）
            if (hediff.Severity >= 1f - 0.001f)
            {
                hediff.Severity -= 1f;
                HediffComp_BinguinIceBurst.TryDestroyRandomPart(pawn);
            }
        }
    }

    // 冰爆 ×1：普通冰爆弹（突击/精确步枪弹药）
    public class DamageWorker_BinguinIceBullet : DamageWorker_AddInjury
    {
        protected virtual float IceMultiplier
        {
            get { return 1f; }
        }

        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            Pawn pawn = victim as Pawn;
            // ★ 护甲结算前的 Amount = 该发武器的面板伤害
            float panelDamage = dinfo.Amount;
            DamageResult result = base.Apply(dinfo, victim);
            if (pawn != null && panelDamage > 0f)
            {
                IceBurstHelper.Add(pawn, panelDamage * IceMultiplier);
            }
            return result;
        }
    }

    // 冰爆 ×2：终极激光机枪（继承弹伤体系）
    public class DamageWorker_BinguinIceBulletUltra : DamageWorker_BinguinIceBullet
    {
        protected override float IceMultiplier
        {
            get { return 2f; }
        }
    }

    // 冰爆 ×2：终极企鹅飞踢（保持原版钝击语义 DamageWorker_Blunt）
    public class DamageWorker_BinguinBluntUltra : DamageWorker_Blunt
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            Pawn pawn = victim as Pawn;
            float panelDamage = dinfo.Amount;
            DamageResult result = base.Apply(dinfo, victim);
            if (pawn != null && panelDamage > 0f)
            {
                IceBurstHelper.Add(pawn, panelDamage * 2f);
            }
            return result;
        }
    }
}
