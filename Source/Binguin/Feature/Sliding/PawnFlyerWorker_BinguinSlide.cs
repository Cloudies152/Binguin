// ============================================================================
// 企鹅滑板 —— 贴地飞行器 worker（终极方案：从源头消除飞行高度）
//
// 2026-08-19 最后修订：用户测试 v9.2（postfix 压平 effectivePos/effectiveHeight）
// 后仍见"低飞"。原因分析：postfix 依赖飞行中 pawn 的 apparel 状态（WearsBoard），
// 且反射写字段在渲染链中可能被覆盖，不够可靠。
// ★ 根治方案：自定义 PawnFlyerWorker，GetHeight() 恒返回 0。
//   1.6 中 effectiveHeight = worker.GetHeight(progress)（PawnFlyer.RecomputePosition
//   内调用），effectivePos = groundPos + AltIncVect*effectiveHeight + forward*
//   heightFactor*effectiveHeight → effectiveHeight=0 时 effectivePos == groundPos
//   （纯地面坐标），小人完全贴地渲染，无需任何反射/Harmony。
// ★ 结构依据（反射实测 1.6.4871）：
//   - Verse.PawnFlyerWorker：非抽象、GetHeight(Single) virtual、
//     public ctor(PawnFlyerProperties)（Worker getter 用
//     Activator.CreateInstance(workerClass, new object[]{ properties }) 实例化）
//   - 基类 GetHeight = GenMath.InverseParabola(progress) ← 抛物线高度即"飞行"来源
//   - ThingDef.pawnFlyer 是 public 字段；workerClass 是 private（AccessTools 反射改）
// ============================================================================

using Verse;

namespace Binguin.Feature.Sliding
{
    // 贴地滑行 worker：飞行高度恒为 0 → 滑行完全贴地（保留 PawnFlyer 全部机制：
    // 无回弹/越过敌人/飞行中不可命中/LOS 挡墙）
    public class PawnFlyerWorker_BinguinSlide : PawnFlyerWorker
    {
        public PawnFlyerWorker_BinguinSlide(PawnFlyerProperties properties)
            : base(properties)
        {
        }

        public override float GetHeight(float progress)
        {
            return 0f;
        }
    }
}
