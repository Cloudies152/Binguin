// ============================================================================
// 直钩「穿刺」—— 继承 Verb 的施法组件（2026-10 用户需求）
//
// ★ 用户原话：
//   「将其原本的代码注释掉，改用继承 verb 的组件。效果为：点击技能，选择范围。
//     范围绘制方法为施法者（caster）到准星（target）的一条直线。target 除了
//     地面还应该有 pawn。施法后正常释放特效，发射冰刺，对路径上的所有非友好
//     的 pawn 造成伤害。」
//
// ★★ 为什么换成 Verb 子类就够，不用再自己接管 targeter：
//   （反编译实证，rw16）
//   ① `Command_Ability.ProcessInput()` 走的就是
//        Find.Targeter.BeginTargeting(ability.verb)
//      —— 瞄准参数直接取 `verb.verbProps.targetParams`，所以
//      `<canTargetPawns>true</canTargetPawns>` 就是「能瞄 pawn」，
//      不需要像原来那样自己拼 TargetingParameters。
//   ② `Targeter.TargeterUpdate()` 每帧调
//        targetingSource.DrawHighlight(CurrentTargetUnderMouse(true))
//      —— `targetingSource` 就是 `ability.verb`。所以只要 override
//      `DrawHighlight`，预览就自动跟着原版瞄准流程走。
//   ③ `VerbTracker.InitVerbsFromZero()` 用
//        (Verb)Activator.CreateInstance(verbProperties.verbClass)
//      造 Verb ⇒ XML 里把 `<verbClass>` 写成这个类名即可挂上，
//      并且 `Ability.Initialize()` 里
//        if (VerbTracker.PrimaryVerb is IAbilityVerb av) av.Ability = this;
//      会回填 `ability`（`Verb_CastAbility` 已实现 IAbilityVerb）。
//
//   ⇒ 因此原来那套「自定义 gizmoClass + GameComponent 每帧画」的做法
//     （`Command_BinguinPierce` / `BinguinPierceAim`）已经不需要了，
//     按用户要求整段注释保留在 `Command_BinguinAbility.cs` 里。
//
// ★ 与「施法范围圈」的关系（用户 2026-10 追加需求「绘制出圆圈（施法范围）」）：
//   原来那套 DIY 方案的毛病是「只有圆、没有线」，而圆又跟实际射线对不上；
//   现在是**两者都要**：
//     · 圆圈 = 施法范围（`verbProps.DrawRadiusRing`，原版白色那圈）
//     · 直线 = 这一发实际打到的格（`LineCells` → `GenDraw.DrawFieldEdges`）
//   所以本类的 `DrawHighlight` 不调 base，但会**手动把范围圈画回来**，
//   再把直线叠加在上面。
//
// ★ 为什么不直接调 base：
//   `Verb_CastAbility.DrawHighlight` 除了范围圈之外，还画
//     `GenDraw.DrawTargetHighlightWithLayer(...)` / 目标点半径环
//   —— 那些是「以目标点为圆心」的范围，对一条射线没意义。
//
// ★★ 预览与实伤共用同一份算法：
//   `LineCells(...)` 同时被本类的 `DrawHighlight`（画）与
//   `CompAbilityEffect_BinguinPierce.Apply`（打）调用 —— 结构上保证
//   「看到的线 = 打到的格」，不会再出现「线画在一个方向、打另一个方向」。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin.Feature.Rods
{
    /// <summary>
    /// 直钩「穿刺」的 Verb：继承 <see cref="Verb_CastAbility"/>。
    /// 瞄准时画「施法范围圈（原版那圈）+ 施法者 → 准星的一条直线」，
    /// 圈表示最大射程、线表示这一发实际打到的格。
    /// 施法本身（前摇 / 冷却 / 特效 / 伤害）仍走原版 Ability 管线，
    /// 由 <c>CompAbilityEffect_BinguinPierce</c> 结算。
    /// </summary>
    public class Verb_BinguinPierce : Verb_CastAbility
    {
        /// <summary>Def 里没写 range / range &lt;= 0 时的兜底射程（格）。</summary>
        public const float FallbackRange = 10f;

        /// <summary>直线高亮颜色（淡蓝，与冰系特效一致）。</summary>
        private static readonly Color LineColor = new Color(0.85f, 0.95f, 1f, 0.90f);

        /// <summary>
        /// 瞄准期间每帧调用（`Targeter.TargeterUpdate`）。
        /// ★ 不调 base（`Verb_CastAbility.DrawHighlight` 会画「目标点附近」的高亮，
        ///   对射线没意义），但**保留它画的那圈施法范围环** ——
        ///   用户要求「绘制出圆圈（施法范围）」，所以手动调
        ///   `verbProps.DrawRadiusRing`，然后再叠加这条直线。
        /// </summary>
        public override void DrawHighlight(LocalTargetInfo target)
        {
            Pawn casterPawn = CasterPawn;
            if (casterPawn == null || !casterPawn.Spawned)
            {
                return;
            }
            Map map = casterPawn.Map;
            // `GenDraw.DrawFieldEdges` 内部用的是 `Find.CurrentMap`，
            // 施法者不在当前地图上就别画（否则会画到别人的格子上）。
            if (map == null || map != Find.CurrentMap)
            {
                return;
            }

            // ① 施法范围圈（原版那圈，白色）：最大能打到多远一目了然。
            //    内部已带 minRange 环 / 行星模式 / `drawHighlightWithLineOfSight`
            //    的处理，直接复用最稳。
            verbProps.DrawRadiusRing(casterPawn.Position, this);

            // 目标合法就用准星所在格；不合法（超射程 / 不可选）退回鼠标格，
            // 这样即使瞄准超范围也仍然看得到这条线的走向。
            IntVec3 aim = target.IsValid ? target.Cell : UI.MouseCell();
            if (!aim.InBounds(map))
            {
                return;
            }

            List<IntVec3> cells = LineCells(casterPawn.Position, aim, EffectiveRange, map);
            if (cells.Count > 0)
            {
                // ② 把射线经过的每一格整格高亮 —— 「能打到哪几格」一目了然
                GenDraw.DrawFieldEdges(cells, LineColor);
                // ③ 终点圈，强调「这一格是射线尽头」
                GenDraw.DrawRadiusRing(cells[cells.Count - 1], 0.45f, LineColor);
            }
            if (target.IsValid)
            {
                // ④ 准星本身的高亮（瞄 pawn 时就框在那个 pawn 上）
                GenDraw.DrawTargetHighlightWithLayer(target.CenterVector3, AltitudeLayer.MetaOverlays);
            }
        }

        /// <summary>
        /// 取技能射程，异常/缺失时兜底。
        /// ★ 优先用 `verb.EffectiveRange` —— 那正是 `DrawHighlight` 与
        ///   `Verb_CastAbility.ValidateTarget`（射程校验）用的值
        ///   （`verbProps.AdjustedRange(this, Caster)`，会被天气的
        ///   `CurWeatherMaxRangeCap` 压上限），这样预览/校验/实伤三者一致。
        /// </summary>
        public static float RangeOf(Ability ability)
        {
            if (ability != null && ability.verb != null)
            {
                float r = ability.verb.EffectiveRange;
                if (r > 0f)
                {
                    return r;
                }
                if (ability.verb.verbProps != null && ability.verb.verbProps.range > 0f)
                {
                    return ability.verb.verbProps.range;
                }
            }
            return FallbackRange;
        }

        /// <summary>
        /// 施法者格 → 准星格的格序列（不含施法者自己那格），到射程外或出界为止。
        /// ★★ 这是「预览」和「实伤」的**唯一**一份范围算法，两边必须都走这里。
        /// </summary>
        public static List<IntVec3> LineCells(IntVec3 origin, IntVec3 aim, float range, Map map)
        {
            List<IntVec3> cells = new List<IntVec3>();
            if (map == null || range <= 0f || origin == aim)
            {
                return cells;
            }
            // ★ 必须复制一份：`GenSight.BresenhamCellsBetween` 返回的是它自己的
            //   静态 tmpCells，复用会被后续调用冲掉（本列表要一直用到伤害结算完）。
            List<IntVec3> raw = GenSight.BresenhamCellsBetween(origin, aim);
            for (int i = 1; i < raw.Count; i++)
            {
                IntVec3 c = raw[i];
                if (!c.InBounds(map))
                {
                    break;
                }
                // 射程按格距算；+0.5 容差，免得射程边缘那一格时有时无
                float dx = c.x - origin.x;
                float dz = c.z - origin.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) > range + 0.5f)
                {
                    break;
                }
                cells.Add(c);
            }
            return cells;
        }
    }
}
