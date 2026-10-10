// ============================================================================
// 企鹅滑板 —— 「滑行冲刺」命令 Comp（装备命令栏按钮 + 冷却管理）
//
// 2026-08-19 终版 v5：
//   ★ 命令收集：穿戴中装备的命令由 Apparel.GetWornGizmos → CompGetWornGizmosExtra()
//     收集（v3 用户实测按钮显示正常）。
//   ★ 按钮：自定义 Command_BinguinSlide（显示稳定，不依赖原版 VerbTarget UI）。
//   ★ 范围/施法：Command_BinguinSlide.ProcessInput → BeginTargeting(verb 重载)
//     → Targeter 画范围圈 + 点击后 Verb_SlideDash.TryCastShot 派滑行 job。
//   ★ 冷却：本 Comp 管理（nextUseTick，10 秒），按钮冷却置灰 + TryCastShot 拦截。
//   ★ 防御：命令创建/施法全程异常保护，不破坏命令栏其他按钮。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class CompProperties_BinguinBoard : CompProperties
    {
        public float range = 10f;          // 滑行半径（格）
        public int cooldownTicks = 600;    // 冷却 10 秒（60 tick/秒）

        public CompProperties_BinguinBoard()
        {
            compClass = typeof(CompBinguinBoard);
        }
    }

    public class CompBinguinBoard : ThingComp
    {
        private int nextUseTick = -1;
        // ★ 静态：ExecutePendingSlide（前摇起飞）是静态方法，免疫计时也静态化
        //   （同一时刻只有一个滑板前摇，多滑板同时滑行的免疫同 tick 到期可接受）
        private static int immunityExpireTick = -1;   // 瞬移后短暂免疫的到期 tick

        // 瞬移后免疫时长（0.5 秒 = 30 tick，防落地瞬间被集火）
        private const int ImmunityTicksAfterSlide = 30;

        public CompProperties_BinguinBoard Props
        {
            get { return (CompProperties_BinguinBoard)props; }
        }

        private Pawn Wearer
        {
            get { return (parent as Apparel) != null ? ((Apparel)parent).Wearer : null; }
        }

        public bool CanUseNow
        {
            get { return Find.TickManager.TicksGame >= nextUseTick; }
        }

        public void MarkUsed()
        {
            nextUseTick = Find.TickManager.TicksGame + Props.cooldownTicks;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<int>(ref nextUseTick, "nextUseTick", -1, false);
            // 免疫计时是瞬态静态字段，不存档
        }

        public override void CompTick()
        {
            base.CompTick();
            if (immunityExpireTick > 0 && GenTicks.TicksGame >= immunityExpireTick)
            {
                immunityExpireTick = -1;
                RemoveImmunity(Wearer);
            }
        }

        // ★ 2026-09 性能：def 名是常量 → 懒加载缓存（加/摘免疫时不再查库）
        private static HediffDef cachedImmunityDef;
        private static bool immunityLookedUp;
        private static ThingDef cachedBoardFlyerDef;
        private static bool boardFlyerLookedUp;

        // 贴地滑行飞行器 def（ExecutePendingSlide 每次滑行都要用 → 缓存）
        private static ThingDef BoardFlyerDef
        {
            get
            {
                if (!boardFlyerLookedUp)
                {
                    boardFlyerLookedUp = true;
                    cachedBoardFlyerDef = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_BoardFlyer");
                }
                return cachedBoardFlyerDef;
            }
        }

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

        private static void AddImmunity(Pawn pawn)
        {
            HediffDef def = ImmunityDef;
            if (pawn != null && pawn.health != null && def != null
                && pawn.health.hediffSet.GetFirstHediffOfDef(def) == null)
            {
                pawn.health.AddHediff(def);
            }
        }

        private static void RemoveImmunity(Pawn pawn)
        {
            HediffDef def = ImmunityDef;
            if (pawn != null && pawn.health != null && def != null)
            {
                Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (h != null)
                {
                    pawn.health.RemoveHediff(h);
                }
            }
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            // 迭代器内不能 try-catch（CS1626），命令创建提取到普通方法，异常只记日志不传播
            Gizmo gizmo = TryCreateCommand();
            if (gizmo != null)
            {
                yield return gizmo;
            }
        }

        private Gizmo TryCreateCommand()
        {
            try
            {
                Pawn wearer = Wearer;
                if (wearer == null || wearer.Destroyed || !wearer.Spawned || wearer.Map == null)
                {
                    return null;
                }

                Command_BinguinSlide cmd = new Command_BinguinSlide
                {
                    defaultLabel = "Binguin_CompBoard_01".Translate(),
                    defaultDesc = "Binguin_CompBoard_02".Translate()
                        + Props.range.ToString("0") + "Binguin_CompBoard_03".Translate()
                        + Mathf.RoundToInt(Props.cooldownTicks / 60f) + "Binguin_CompBoard_04".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Things/Apparel/Binguin/PenguinBoard", false),
                    hotKey = null,
                    caster = wearer,
                    range = Props.range
                };
                if (!CanUseNow)
                {
                    int secondsLeft = (int)Mathf.Ceil((nextUseTick - Find.TickManager.TicksGame) / 60f);
                    cmd.Disable("Binguin_CompBoard_05".Translate() + secondsLeft + "Binguin_CompBoard_06".Translate());
                }
                Pawn wearerForCallback = wearer;
                cmd.onTargetSelected = delegate (LocalTargetInfo target)
                {
                    TryStartSlide(wearerForCallback, target);
                };
                return cmd;
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 滑板命令创建异常（已拦截，不影响其他命令）: " + e);
                return null;
            }
        }

        // 施法：跳跃背包式 —— LOS 挡墙检查 + 直接瞬移到目标（越过中间一切敌人）
        public void TryStartSlide(Pawn wearer, LocalTargetInfo target)
        {
            try
            {
                Log.Message("[冰鹅族] 滑行冲刺：收到目标 " + target);
                if (wearer == null || wearer.Destroyed || !wearer.Spawned)
                {
                    Log.Warning("[冰鹅族] 滑行冲刺：施法者无效。");
                    return;
                }
                Map map = wearer.Map;
                if (map == null)
                {
                    return;
                }
                if (!CanUseNow)
                {
                    Log.Warning("[冰鹅族] 滑行冲刺：冷却中。");
                    return;
                }
                if (!target.IsValid || target.HasThing)
                {
                    Messages.Message("Binguin_CompBoard_07".Translate(), MessageTypeDefOf.NeutralEvent, false);
                    return;
                }
                IntVec3 dest = target.Cell;
                if (!dest.IsValid || !dest.Walkable(map) || dest == wearer.Position)
                {
                    Messages.Message("Binguin_CompBoard_08".Translate(), MessageTypeDefOf.NeutralEvent, false);
                    return;
                }
                if (!wearer.Position.InHorDistOf(dest, Props.range))
                {
                    Messages.Message("Binguin_CompBoard_09".Translate() + Props.range.ToString("0") + "Binguin_CompBoard_10".Translate(), MessageTypeDefOf.NeutralEvent, false);
                    return;
                }
                // 跳跃背包式：不能越过墙（LOS 检查；敌人不挡视线 → 可以越过敌人）
                if (!GenSight.LineOfSight(wearer.Position, dest, map))
                {
                    Messages.Message("Binguin_CompBoard_11".Translate(), MessageTypeDefOf.NeutralEvent, false);
                    return;
                }
                // ★ 0.2 秒前摇（用户 2026-08-19 要求）：转向目标 + 记录待滑行，
                //   GameComponent 每帧检查，到点执行 ExecutePendingSlide
                wearer.rotationTracker.FaceCell(dest);
                MoteMaker.ThrowText(wearer.Position.ToVector3Shifted(), map, "Binguin_CompBoard_12".Translate(), 0.9f);
                pendingSlidePawn = wearer;
                pendingSlideDest = dest;
                pendingSlideTick = GenTicks.TicksGame + 12;   // 0.2 秒
                // 冷却立即生效（防前摇期间连点）
                MarkUsed();
                Log.Message("[冰鹅族] 滑行冲刺：前摇 0.2s 后起飞 " + wearer.Position + " → " + dest);
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 滑行冲刺施法异常（已拦截）: " + e);
            }
        }

        // ---- 滑行前摇（0.2 秒）：GameComponent_BinguinDiplomacy.GameComponentUpdate 调用 ----
        public static Pawn pendingSlidePawn;
        public static IntVec3 pendingSlideDest = IntVec3.Invalid;
        public static int pendingSlideTick = -1;

        public static void ExecutePendingSlide()
        {
            pendingSlideTick = -1;
            Pawn wearer = pendingSlidePawn;
            IntVec3 dest = pendingSlideDest;
            pendingSlidePawn = null;
            pendingSlideDest = IntVec3.Invalid;
            try
            {
                if (wearer == null || wearer.Destroyed || !wearer.Spawned || wearer.Map == null)
                {
                    Log.Warning("[冰鹅族] 滑行冲刺：前摇结束但施法者无效，取消。");
                    return;
                }
                if (!dest.IsValid || !dest.Walkable(wearer.Map) || dest == wearer.Position)
                {
                    Log.Warning("[冰鹅族] 滑行冲刺：前摇结束但目标无效，取消。");
                    return;
                }
                Map map = wearer.Map;
                Log.Message("[冰鹅族] 滑行冲刺：起飞 " + wearer.Position + " → " + dest);
                // 落地短暂免疫（防落点被集火；飞行途中 pawn 在飞行器里天然不可被命中）
                AddImmunity(wearer);
                immunityExpireTick = GenTicks.TicksGame + ImmunityTicksAfterSlide;
                // ★ 照抄原版跳跃背包（JumpUtility.DoJump）：用 PawnFlyer 飞行实体，
                //   pawn 进入飞行器飞向目标，全程不在地图上 → 无回弹、天然不可被命中、
                //   越过一切敌人（LOS 已挡墙）
                // ★ 2026-08-19 终极方案：用自定义贴地飞行器 Binguin_BoardFlyer
                //   （workerClass = PawnFlyerWorker_BinguinSlide，GetHeight 恒 0 →
                //   effectiveHeight=0 → effectivePos=groundPos 完全贴地，不再"低飞"）
                ThingDef flyerDef = BoardFlyerDef;
                if (flyerDef == null)
                {
                    flyerDef = ThingDefOf.PawnFlyer; // 兜底（理论上不会发生，PatchBoardFlyer 已挂载）
                }
                PawnFlyer flyer = PawnFlyer.MakeFlyer(
                    flyerDef,
                    wearer,
                    dest,
                    null,   // flightEffecterDef（可选：冰屑/飞行效果）
                    null,   // soundLanding
                    false,  // flyWithCarriedThing
                    null,   // inheritVector
                    null,   // ability
                    default(LocalTargetInfo));
                GenSpawn.Spawn(flyer, wearer.Position, map);
                // 起飞尘土（照抄原版）
                FleckMaker.ThrowDustPuff(wearer.Position.ToVector3Shifted(), map, 1f);
                // 原版跳跃背包会选中飞行器（保持镜头跟随）
                if (Find.Selector.IsSelected(wearer))
                {
                    Find.Selector.Select(flyer, false, false);
                }
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 滑行冲刺起飞异常（已拦截）: " + e);
            }
        }
    }
}
