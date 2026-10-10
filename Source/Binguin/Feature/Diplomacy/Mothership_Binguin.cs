// ============================================================================
// 冰鹅族飞天决战（2026-08-22 用户设计文档）
//   研究「天问」→ 大型信号发射器（露天，与冰鹅族盟友）→「尝试联系冰鹅族
//   母舰」→ 13 天联系期 + 15 波增强袭击（4~24h 间隔，点数 ×1.2~1.4）→
//   链接成功 → 飞船型发射仓装人 →「发射飞船」→ 冰鹅族飞天结局。
//
//   组件（全部由 BinguinDefPatches 静态构造挂载，XML 零自定义类型）：
//   · PlaceWorker_BinguinOpenAir    —— 发射器必须露天（不可室内）
//   · CompBinguinSignalRelay        —— 发射器命令：尝试联系母舰
//   · CompBinguinShipCapsule        —— 发射仓：右键进入 / 放出 / 发射结局
//   · JobDriver_BinguinBoardCapsule —— 走进发射仓（收纳进舱）
//   状态机（联系中/已链接 + 袭击调度）存于 GameComponent_BinguinDiplomacy
//   （每 tick 推进，随存档保存）。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

using Binguin.Helper;

namespace Binguin.Feature.Diplomacy
{
    // ============ 露天放置校验（大型信号发射器） ============
    public class PlaceWorker_BinguinOpenAir : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            ThingDef def = checkingDef as ThingDef;
            IntVec2 size = def != null ? def.size : IntVec2.One;
            foreach (IntVec3 c in GenAdj.CellsOccupiedBy(loc, rot, size))
            {
                if (c.Roofed(map))
                {
                    return new AcceptanceReport("Binguin_Mothership_01".Translate());
                }
            }
            return true;
        }
    }

    // ============ 大型信号发射器 comp ============
    public class CompProperties_BinguinSignalRelay : CompProperties
    {
        public CompProperties_BinguinSignalRelay()
        {
            compClass = typeof(CompBinguinSignalRelay);
        }
    }

    public class CompBinguinSignalRelay : ThingComp
    {
        // 迭代器内不能 try/catch（CS1626），全部 helper 化
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Gizmo g = TryCreateContactGizmo();
            if (g != null)
            {
                yield return g;
            }
        }

        // ★ 2026-09 性能：命令栏/检视面板每帧调用，而 GetComponent 会遍历游戏的
        //   组件列表 → 解析一次后缓存（组件实例在一局游戏内不变）。
        private GameComponent_BinguinDiplomacy gcCache;

        private GameComponent_BinguinDiplomacy GameComp
        {
            get
            {
                if (gcCache == null && Current.Game != null)
                {
                    gcCache = Current.Game.GetComponent<GameComponent_BinguinDiplomacy>();
                }
                return gcCache;
            }
        }

        private Gizmo TryCreateContactGizmo()
        {
            try
            {
                GameComponent_BinguinDiplomacy gc = GameComp;
                if (gc == null)
                {
                    return null;
                }
                int phase = gc.MothershipPhase;
                if (phase == 2)
                {
                    // 已链接：不再提供启动
                    return null;
                }
                if (phase == 1)
                {
                    // 联系中：给一个状态按钮（禁用，仅展示剩余时间）
                    Command_Action busy = new Command_Action
                    {
                        defaultLabel = "Binguin_Mothership_02".Translate(),
                        defaultDesc = gc.MothershipStatusText(),
                        action = delegate { }
                    };
                    busy.Disable("Binguin_Mothership_03".Translate());
                    return busy;
                }
                // 未联系：启动命令
                Command_Action cmd = new Command_Action
                {
                    defaultLabel = "Binguin_Mothership_04".Translate(),
                    defaultDesc = "Binguin_Mothership_05".Translate(),
                    action = delegate { TryBeginContact(); }
                };
                string why = WhyDisabled();
                if (why != null)
                {
                    cmd.Disable(why);
                }
                return cmd;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("信号命令创建异常：" + e.Message, severity: 1, isDebug: false);
                return null;
            }
        }

        private string WhyDisabled()
        {
            Building b = parent as Building;
            if (b == null || !b.Spawned)
            {
                return "Binguin_Mothership_06".Translate();
            }
            // 露天
            foreach (IntVec3 c in b.OccupiedRect())
            {
                if (c.Roofed(b.Map))
                {
                    return "Binguin_Mothership_07".Translate();
                }
            }
            // 盟友
            if (!BinguinFactionHelper.IsAllyWithBinguin())
            {
                return "Binguin_Mothership_08".Translate();
            }
            return null;
        }

        private void TryBeginContact()
        {
            try
            {
                GameComponent_BinguinDiplomacy gc = GameComp;
                if (gc == null)
                {
                    return;
                }
                string why = WhyDisabled();
                if (why != null)
                {
                    Messages.Message(why, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                gc.StartMothershipContact(parent as Building);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("开始联系异常：" + e, severity: 2, isDebug: false);
            }
        }

        public override string CompInspectStringExtra()
        {
            GameComponent_BinguinDiplomacy gc = GameComp;
            if (gc == null)
            {
                return null;
            }
            if (gc.MothershipPhase == 1)
            {
                return "Binguin_Mothership_09".Translate() + gc.MothershipStatusText();
            }
            if (gc.MothershipPhase == 2)
            {
                return "Binguin_Mothership_10".Translate();
            }
            return null;
        }
    }

    // 冰鹅派系盟友判断（静态工具）
    public static class BinguinFactionHelper
    {
        public static bool IsAllyWithBinguin()
        {
            try
            {
                // ★ 2026-09 性能：AllFactions 是 yield 迭代器（每次调用分配状态机），
                //   而本方法被"WhyDisabled"在选中发射器时每帧调用 → 改用可直接
                //   下标访问的列表，零分配。
                Faction binguin = null;
                Faction player = Faction.OfPlayer;
                List<Faction> all = Find.FactionManager.AllFactionsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    Faction f = all[i];
                    // ★ 2026-09-26 收口：这是【派系】判定（不是种族），归 BinguinFactions 管。
                    if (f != null && !f.defeated && BinguinFactions.IsPeaceful(f) && f != player)
                    {
                        binguin = f;
                        break;
                    }
                }
                if (binguin == null)
                {
                    return false;
                }
                return binguin.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    // ============ 飞船型发射仓 comp ============
    public class CompProperties_BinguinShipCapsule : CompProperties
    {
        public CompProperties_BinguinShipCapsule()
        {
            compClass = typeof(CompBinguinShipCapsule);
        }
    }

    public class CompBinguinShipCapsule : ThingComp
    {
        // 舱内乘客 = Building_Casket 容器里的人（原版容器，随建筑存档）
        private Building_Casket Casket
        {
            get { return parent as Building_Casket; }
        }

        public Pawn Passenger
        {
            get
            {
                Building_Casket c = Casket;
                if (c == null || !c.HasAnyContents)
                {
                    return null;
                }
                return c.ContainedThing as Pawn;
            }
        }

        // 右键发射仓 → 「进入发射仓」（殖民者走进舱内收纳）
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            FloatMenuOption opt = TryCreateBoardOption(selPawn);
            if (opt != null)
            {
                yield return opt;
            }
        }

        private FloatMenuOption TryCreateBoardOption(Pawn selPawn)
        {
            try
            {
                if (selPawn == null || !selPawn.IsColonist || selPawn.Downed
                    || selPawn.InMentalState || !selPawn.Spawned)
                {
                    return null;
                }
                Building_Casket c = Casket;
                if (c == null || c.HasAnyContents)
                {
                    return null; // 已有人
                }
                Thing target = parent;
                return new FloatMenuOption("Binguin_Mothership_11".Translate(),
                    delegate
                    {
                        JobDef jd = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_BoardCapsule");
                        if (jd == null)
                        {
                            return;
                        }
                        Job job = new Job(jd, target);
                        selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    });
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("发射仓菜单异常：" + e.Message, severity: 1, isDebug: false);
                return null;
            }
        }

        // ★ 2026-09 性能：命令栏每帧重建 → 复用同一个临时列表（它不会逃逸出
        //   本次枚举，外部观察不到），省掉每帧一个 List 分配。
        private readonly List<Gizmo> tmpGizmos = new List<Gizmo>();

        // ★ 2026-09 性能：GetComponent 会遍历游戏组件列表，而命令栏/检视面板
        //   每帧调用 → 解析一次后缓存（组件实例在一局游戏内固定）。
        private GameComponent_BinguinDiplomacy gcCache;

        private GameComponent_BinguinDiplomacy GameComp
        {
            get
            {
                if (gcCache == null && Current.Game != null)
                {
                    gcCache = Current.Game.GetComponent<GameComponent_BinguinDiplomacy>();
                }
                return gcCache;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            TryCreateGizmos(tmpGizmos);
            for (int i = 0; i < tmpGizmos.Count; i++)
            {
                yield return tmpGizmos[i];
            }
        }

        private void TryCreateGizmos(List<Gizmo> result)
        {
            result.Clear();
            try
            {
                // 1) 放出舱内人员
                Building_Casket c = Casket;
                if (c != null && c.HasAnyContents)
                {
                    result.Add(new Command_Action
                    {
                        defaultLabel = "Binguin_Mothership_12".Translate(),
                        defaultDesc = "Binguin_Mothership_13".Translate(),
                        action = delegate
                        {
                            try
                            {
                                c.EjectContents();
                            }
                            catch (Exception e)
                            {
                                BinguinLogUtility.Log("放出异常：" + e.Message, severity: 1, isDebug: false);
                            }
                        }
                    });
                }
                // 2) 发射飞船（母舰已链接且有人在内）
                GameComponent_BinguinDiplomacy gc = GameComp;
                if (gc != null && gc.MothershipPhase == 2)
                {
                    Command_Action launch = new Command_Action
                    {
                        defaultLabel = "Binguin_Mothership_14".Translate(),
                        defaultDesc = "Binguin_Mothership_15".Translate(),
                        action = delegate { gc.TryLaunchShip(); }
                    };
                    bool any = gc.AnyShipCapsuleOccupied();
                    if (!any)
                    {
                        launch.Disable("Binguin_Mothership_16".Translate());
                    }
                    result.Add(launch);
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("发射仓命令异常：" + e.Message, severity: 1, isDebug: false);
            }
        }

        public override string CompInspectStringExtra()
        {
            GameComponent_BinguinDiplomacy gc = GameComp;
            Pawn p = Passenger;
            string s = p != null ? "Binguin_Mothership_17".Translate() + p.LabelShort : "Binguin_Mothership_18".Translate();
            if (gc != null && gc.MothershipPhase == 2)
            {
                s += "Binguin_Mothership_19".Translate();
            }
            return s;
        }
    }

    // ============ 走进发射仓 job ============
    //
    // ★ 2026-09 修复（用户实测：殖民者走到仓边进不去 / 舱内恒空 / 发射提示
    //   「没有殖民者进入发射仓」）：
    //   原版 Building_Casket.TryAcceptThing 对【地图上已生成的小人】走
    //   holdingOwner→TryTransferToContainer 转移分支——该分支对持有者是 Map
    //   的单位会打日志
    //     "Can't transfer items to or from Maps directly. ..."
    //   并返回失败，而 TryAcceptThing 会【忽略返回值假成功】（实测日志里每次
    //   进舱前都出现该警告、消息却显示已进入，舱内实际是空的）。
    //   修复 = 先让殖民者明确离图（DeSpawn，不销毁），再交给内舱容器收纳
    //   （走 TryAdd 分支）；任何失败都自动放回原地，绝不吞人。
    public class JobDriver_BinguinBoardCapsule : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            Toil board = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = delegate { BoardNow(); }
            };
            yield return board;
        }

        private void BoardNow()
        {
            Pawn p = pawn;
            Map map = (p != null && !p.Destroyed && p.Spawned) ? p.Map : null;
            IntVec3 pos = p != null ? p.Position : IntVec3.Invalid;
            try
            {
                Building_Casket casket = job.GetTarget(TargetIndex.A).Thing as Building_Casket;
                if (casket == null || casket.Destroyed || !casket.Spawned)
                {
                    return;
                }
                if (casket.HasAnyContents)
                {
                    Messages.Message("Binguin_Mothership_20".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                if (p == null || p.Destroyed || !p.Spawned || p.Downed || p.InMentalState)
                {
                    return;
                }

                BinguinLogUtility.Log("进舱开始：" + p.LabelShort + " -> " + casket.LabelShort);

                // 关键：先离图再收纳（绕开原版对地图单位的转移分支）
                p.DeSpawn(DestroyMode.Vanish);

                if (!casket.TryAcceptThing(p, false))
                {
                    Respawn(p, map, pos);
                    Messages.Message("Binguin_Mothership_21".Translate(), MessageTypeDefOf.RejectInput, false);
                    BinguinLogUtility.Log("进舱失败（已把殖民者放回原地）。");
                    return;
                }

                Messages.Message(p.LabelShort + "Binguin_Mothership_22".Translate(),
                    MessageTypeDefOf.NeutralEvent, false);
                BinguinLogUtility.Log("" + p.LabelShort + " 进入发射仓（舱内已收纳="
                    + (casket.ContainedThing == p) + "）。");
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("进舱异常：" + e, severity: 2, isDebug: false);
                Respawn(p, map, pos);
            }
        }

        // 兜底：失败/异常时把已离图的殖民者放回原地
        private static void Respawn(Pawn p, Map map, IntVec3 pos)
        {
            try
            {
                if (p == null || p.Destroyed || p.Spawned || map == null || !pos.IsValid)
                {
                    return;
                }
                GenSpawn.Spawn(p, pos, map);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("进舱回滚放回失败：" + e, severity: 2, isDebug: false);
            }
        }
    }
}
