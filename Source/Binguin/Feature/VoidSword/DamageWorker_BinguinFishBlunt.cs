// 加载方式已迁至 XML；下述占位替换描述仅记录旧实现。
// ============================================================================
// 尚方宝剑 · 鱼的斩击损伤 worker（2026-10-06）
//
// 用户规格：「每次斩击 10 钝器伤害（附带冰爆），100% 钝器穿透」。
//
// ★ 为什么单独做一个 DamageDef + worker，而不是复用 `Binguin_PenguinDamage`：
//   那个是「终极企鹅飞踢」的（冰爆 ×2、走 Blunt 语义），冰爆倍率写死在 worker 里。
//   尚方宝剑的冰爆要**按每次斩击 10% 累积**（14 次斩击 → 140%，即起码冻碎一个部位），
//   倍率不同 ⇒ 单独一条 DamageDef，worker 里按 1:1 累积（10 点伤害 = 10% 冰爆）。
//
// ★ 冰爆逻辑复用 `IceBurstHelper.Add`（与突击步枪/精确步枪/激光机枪/企鹅飞踢同一套），
//   所以冰爆的行为、显示（`Hediff_BinguinIceBurst` 的百分比括号）、
//   满 100% 随机冻碎部位的规则完全一致，不需要在这里重复实现。
//
// ★ 穿透：`armorPenetration` 在 XML 的 `<armorPenetration>1</armorPenetration>` 上给，
//   1 = 100% 钝器穿透（原版 ArmorPenetration 是 0~1 的比例）。
//
// ★ 自定义类型由对应功能的 XML 声明，使用完整命名空间和程序集名。
//   占位，由 `BinguinDefPatches` 静态构造替换成并清 `workerInt` 缓存。
// ============================================================================

using RimWorld;
using Verse;

using Binguin.Feature.IceCombat;

namespace Binguin.Feature.VoidSword
{
    /// <summary>
    /// 尚方宝剑的单次鱼斩：钝击结算 + 按面板伤害 1:1 累积冰爆。
    /// </summary>
    public class DamageWorker_BinguinFishBlunt : DamageWorker_Blunt
    {
        /// <summary>冰爆倍率（1 点面板伤害 = 1% 冰爆）。</summary>
        protected virtual float IceMultiplier
        {
            get { return 1f; }
        }

        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            Pawn pawn = victim as Pawn;
            // ★ 护甲结算前的 Amount = 这一发的面板伤害（与冰爆弹体系同一套思路：
            //   即使被护甲完全挡掉，冰爆也照常全额累积）
            float panelDamage = dinfo.Amount;
            DamageResult result = base.Apply(dinfo, victim);
            if (pawn != null && panelDamage > 0f)
            {
                IceBurstHelper.Add(pawn, panelDamage * IceMultiplier);
            }
            return result;
        }
    }
}
