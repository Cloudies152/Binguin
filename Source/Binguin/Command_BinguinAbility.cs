// ============================================================================
// 钓竿技能 · 自定义命令 gizmo + 直线瞄准预览（2026-10-09 用户需求）
//
// ★ 用户原话：
//   「你看一下能不能修复一下直钩钓竿的穿刺技能，让它瞄准的时候，显示范围变成一条
//     直线，然后可以瞄准 pawn 而不是地板」
//
// ★ 为什么要自定义（XML 做不到）：
//   原版 `AbilityDef.verbProperties.targetParams` 里 `canTargetLocations=true` 时，
//   `Verb_CastAbility` 的 `DrawHighlight` 画的是**目标点周围的圆/环**，而我们的
//   穿刺效果是一条**射线**（取 施法者→目标点 方向，最多射程格）。
//   ⇒ 圆形预览与实际命中范围**根本不一致**：玩家看到的圈比实际宽得多。
//   ⇒ 必须自己画。
//
// ★ 为什么用 GameComponent 每帧画，不用 Targeter 的 onGuiAction：
//   本 mod 先前实测过（见 `Command_BinguinSlide.cs` 与
//   `GameComponent_BinguinDiplomacy.cs` L443 的注释）：
//   **1.6 的 `Targeter.onGuiAction` 不生效**。所以沿用已跑通的方案 ——
//   `Command.ProcessInput` 里置"瞄准中"标记，由 GameComponent 每帧绘制，
//   原生 `actionWhenFinished` 回调里清标记。
//
// ★★ 另一个必须知道的坑（RimSage 查 `Source/RimWorld/Targeter.cs` L170-187 实证）：
//   `Targeter.StopTargeting()` 是**先调 actionWhenFinished 回调，再清字段**：
//     if (actionWhenFinished != null) { var o = actionWhenFinished;
//         actionWhenFinished = null; o(); }
//     targetingSource = null; action = null; targetParams = null;
//     highlightAction = null; targetValidator = null; ...
//   ⇒ 回调里**不要**去起下一个 targeter（会被立刻清空）；只做清理是安全的。
//   本文件只在回调里 Stop() 清标记，所以没问题。
//
// ★★★ 且 `targetValidator` 只有 **10 参重载**真的存下来
//   （`Targeter.cs:131` `this.targetValidator = targetValidator;`），
//   其余四个重载（6参/3参/5参）都会把它清成 null。
//   ⇒ 本文件用 10 参重载。
// ============================================================================

using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin
{
    /// <summary>
    /// 穿刺瞄准状态 + 直线预览绘制。
    /// ★ 由 `Command_BinguinPierce` 置位，`GameComponent_BinguinDiplomacy`
    ///   每帧调用 `DrawAimVisualTick()` 绘制（和滑板/威灵炮共用那个组件，
    ///   省得再挂一个 GameComponent）。
    /// </summary>
    public static class BinguinPierceAim
    {
        public static bool active;
        public static Pawn caster;
        public static float range = 10f;

        // ★ 材质缓存（Mono.Cecil 实证：`GenDraw.DrawLineBetween` 有两个可用重载 ——
        //   `(Vector3, Vector3, SimpleColor, float)` 和 `(Vector3, Vector3, Material, float)`。
        //   前者用的 `Verse.SimpleColor` **是个只有 8 个值的枚举**（White/Red/Green/
        //   Blue/Magenta/Yellow/Cyan/Orange），给不了我们要的淡蓝 ⇒ 必须走 Material。
        //   `SolidColorMaterials.SimpleSolidColorMaterial` 内部有字典缓存，
        //   但每帧查一次字典不如自己缓住，所以静态存下来。）
        private static Material lineMat;
        private static Material dotMat;

        private static Material LineMat
        {
            get
            {
                if (lineMat == null)
                {
                    lineMat = SolidColorMaterials.SimpleSolidColorMaterial(
                        new Color(0.85f, 0.95f, 1f, 0.90f));
                }
                return lineMat;
            }
        }

        private static Material DotMat
        {
            get
            {
                if (dotMat == null)
                {
                    dotMat = SolidColorMaterials.SimpleSolidColorMaterial(
                        new Color(0.85f, 0.95f, 1f, 0.55f));
                }
                return dotMat;
            }
        }

        public static void Start(Pawn pawn, float r)
        {
            caster = pawn;
            range = r > 0f ? r : 10f;
            active = true;
        }

        public static void Stop()
        {
            active = false;
            caster = null;
        }

        /// <summary>
        /// 取"施法者 → 目标格"量化到八方向后的单位方向。
        /// ★ 故意 public static 抽出来：**预览与实伤必须用同一个算法**，
        ///   否则又会变成"看到的和打到的不是一回事"。
        ///   `CompAbilityEffect_BinguinPierce.Apply` 也调这个方法。
        /// </summary>
        public static IntVec3 DirectionTo(IntVec3 from, IntVec3 to)
        {
            Vector3 v = (to - from).ToVector3();
            if (v.sqrMagnitude < 0.01f)
            {
                return IntVec3.Zero;
            }
            v.Normalize();
            int dx = Mathf.Clamp(Mathf.RoundToInt(v.x), -1, 1);
            int dz = Mathf.Clamp(Mathf.RoundToInt(v.z), -1, 1);
            if (dx == 0 && dz == 0)
            {
                return IntVec3.Zero;
            }
            return new IntVec3(dx, 0, dz);
        }

        /// <summary>从施法者沿 `dir` 走，返回射程内能到达的最后一格（出界即止）。</summary>
        public static IntVec3 EndCell(Pawn pawn, IntVec3 dir, float r)
        {
            Map map = pawn.Map;
            if (map == null || dir == IntVec3.Zero)
            {
                return pawn.Position;
            }
            IntVec3 cur = pawn.Position;
            int steps = Mathf.RoundToInt(r);
            for (int i = 1; i <= steps; i++)
            {
                IntVec3 next = pawn.Position + dir * i;
                if (!next.InBounds(map))
                {
                    break;
                }
                cur = next;
            }
            return cur;
        }

        /// <summary>每帧绘制（由 `GameComponent_BinguinDiplomacy.GameComponentUpdate` 调用）。</summary>
        public static void DrawAimVisualTick()
        {
            if (!active || caster == null || !caster.Spawned || caster.Map == null)
            {
                return;
            }
            try
            {
                Map map = caster.Map;
                IntVec3 mouse = UI.MouseCell();
                if (!mouse.InBounds(map))
                {
                    return;
                }
                IntVec3 dir = DirectionTo(caster.Position, mouse);
                if (dir == IntVec3.Zero)
                {
                    return;
                }
                IntVec3 end = EndCell(caster, dir, range);

                // ① 主线
                GenDraw.DrawLineBetween(
                    caster.Position.ToVector3Shifted(),
                    end.ToVector3Shifted(),
                    LineMat,
                    0.30f);

                // ② 沿途每格中心点一个小十字（"能打到哪几格"一目了然）
                int steps = Mathf.RoundToInt(range);
                for (int i = 1; i <= steps; i++)
                {
                    IntVec3 c = caster.Position + dir * i;
                    if (!c.InBounds(map))
                    {
                        break;
                    }
                    Vector3 p = c.ToVector3Shifted();
                    float h = 0.08f;
                    GenDraw.DrawLineBetween(
                        new Vector3(p.x - h, p.y, p.z), new Vector3(p.x + h, p.y, p.z),
                        DotMat, 0.10f);
                    GenDraw.DrawLineBetween(
                        new Vector3(p.x, p.y, p.z - h), new Vector3(p.x, p.y, p.z + h),
                        DotMat, 0.10f);
                    if (c == end)
                    {
                        break;
                    }
                }

                // ③ 终点圈，强调"这一格是射线尽头"
                GenDraw.DrawRadiusRing(end, 0.45f, new Color(0.85f, 0.95f, 1f, 0.90f));
            }
            catch (Exception)
            {
                // 画预览绝不能影响游戏 —— 出任何问题就结束这次瞄准
                Stop();
            }
        }
    }

    /// <summary>
    /// 直钩「穿刺」的命令按钮：接管瞄准，改成直线范围 + 允许瞄准 pawn。
    /// ★ 通过 `AbilityDef.gizmoClass` 挂载
    ///   （`Ability.cs:1047` 用 `Activator.CreateInstance(def.gizmoClass, this, pawn)`）。
    /// </summary>
    public class Command_BinguinPierce : Command_Ability
    {
        public Command_BinguinPierce(Ability ability, Pawn pawn) : base(ability, pawn)
        {
        }

        /// <summary>取技能射程（`AbilityDef.verbProperties.range`）。</summary>
        private float AbilityRange()
        {
            try
            {
                if (ability != null && ability.verb != null && ability.verb.verbProps != null)
                {
                    return ability.verb.verbProps.range;
                }
            }
            catch (Exception)
            {
            }
            return 10f;
        }

        public override void ProcessInput(Event ev)
        {
            // ★ 不调 base.ProcessInput —— 那会走原版"verb 自己画圈"的路径。
            //   这里自己实现等效的三件事：音效 / 取消 Designator / 起 targeter。
            //   ★ 音效写法照抄本 mod 已跑通的 `GameComponent_BinguinDiplomacy.cs:304`：
            //     `SoundDef.PlayOneShotOnCamera()` **不存在**
            //     （Mono.Cecil 实证 SoundDef 上没有这个方法，编译报 CS1061）；
            //     正确入口是静态的 `Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDef, Map)`。
            Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_Tiny, null);
            Find.DesignatorManager.Deselect();

            Pawn p = this.Pawn;
            if (p == null || !p.Spawned || p.Map == null || ability == null)
            {
                return;
            }

            float r = AbilityRange();

            // ★ 用户需求：允许瞄准 pawn（同时保留 locations，地板照样能点）
            TargetingParameters tp = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetSelf = false
            };

            BinguinPierceAim.Start(p, r);

            // 10 参重载：唯一真正支持 targetValidator 的那个（RimSage 实证 Targeter.cs:131）
            Find.Targeter.BeginTargeting(
                tp,
                delegate (LocalTargetInfo target)   // action：选中后交给原版走前摇
                {
                    ability.QueueCastingJob(target, LocalTargetInfo.Invalid);
                },
                null,                               // highlightAction
                delegate (LocalTargetInfo target)   // targetValidator：射程校验
                {
                    // ★ 用 (a-b).LengthHorizontal 算格距（IntVec3 没有 DistanceTo，
                    //   已核实）；+0.5 容差，免得边缘格点不动
                    if (!target.IsValid)
                    {
                        return false;
                    }
                    IntVec3 d = target.Cell - p.Position;
                    float dist = Mathf.Sqrt(d.x * d.x + d.z * d.z);
                    return dist <= r + 0.5f;
                },
                p,                                  // caster
                delegate                            // actionWhenFinished：只清标记
                {
                    BinguinPierceAim.Stop();
                },
                null,                               // mouseAttachment
                false,                              // playSoundOnAction
                null,                               // onGuiAction
                null);                              // onUpdateAction
        }

        /// <summary>鼠标悬停在按钮上时，先给一个圆形射程提示（真正的直线在点下去之后）。</summary>
        public override void GizmoUpdateOnMouseover()
        {
            Pawn p = this.Pawn;
            if (p != null && p.Spawned && p.Map != null)
            {
                GenDraw.DrawRadiusRing(p.Position, AbilityRange(),
                    new Color(0.85f, 0.95f, 1f, 0.35f));
            }
        }
    }
}
