// 加载方式已迁至 XML；下述占位替换描述仅记录旧实现。
// ============================================================================
// 半成品钓竿 · 自动续做 WorkGiver（2026-10-06 用户需求）
//
// 用户需求原文：「然后记得绑定制作者，制作者工作时会自动制作鱼竿」
//   ⇒ 拆成两件事：
//     ① **绑定制作者**：第 5 步选的那个人成为这根半成品的"制作者"，
//        记在 comp 上（`boundMaker`，进存档）。
//     ② **自动续做**：制作者（或任何殖民者）空闲时会**自动回来接着做**，
//        不需要玩家再点一次"继续装配"。
//
// ★ 为什么需要 WorkGiver：自定义 job 不会自动被原版工作分配系统捡起来。
//   原版 bill 之所以能"中断后自己回去做"，是因为 `WorkGiver_DoBill` 每轮工作
//   扫描都会遍历工作台上的 bill。本 mod 的装配不是 bill 体系
//   ⇒ 必须自己写一个 WorkGiver 扫描"还没做完的半成品"。
//
// ★ 工作类型用原版的 **Crafting**（手工）—— 与"制作钓竿"语义一致，
//   玩家能通过工作优先级控制它，不需要新增 WorkType（新增会让所有殖民者多一列）。
//
// ★ 绑定语义（两级，避免"绑死了就没人做"）：
//   · `boundMaker != null` ⇒ **只有这位制作者**能自动来续做（其他人不会抢，
//     但玩家仍可手动用「继续装配」派别人，那种情况会把绑定改成新的人）
//   · `boundMaker == null` ⇒ **任何有手工工作的殖民者**都能来续做
//
// ★ giverClass 由 `BinguinDefPatches` 静态构造替换（XML 零自定义类型原则），
//   与本 mod 其它 WorkGiver 完全同一套做法。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Binguin.Feature.Rods
{
    public class WorkGiver_BinguinUnfinishedRod : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode
        {
            get { return PathEndMode.Touch; }
        }

        public override Danger MaxPathDanger(Pawn pawn)
        {
            return Danger.Deadly;
        }

        /// <summary>半成品 def（懒加载缓存 + 引用比较，沿用本 mod 的 2026-09 性能惯例）。</summary>
        private static ThingDef cachedRodDef;
        private static bool rodDefLookedUp;

        private static ThingDef UnfinishedRodDef
        {
            get
            {
                if (!rodDefLookedUp)
                {
                    rodDefLookedUp = true;
                    cachedRodDef = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_UnfinishedRod");
                }
                return cachedRodDef;
            }
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            ThingDef def = UnfinishedRodDef;
            if (def == null || pawn.Map == null)
            {
                yield break;
            }
            // 半成品是可搬运物品 ⇒ 在 HaulableEver 组里
            List<Thing> all = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t != null && t.def == def)
                {
                    yield return t;
                }
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompBinguinUnfinishedRod comp = t.TryGetComp<CompBinguinUnfinishedRod>();
            if (comp == null)
            {
                return false;
            }
            if (comp.IsFinished)
            {
                return false;
            }
            if (!comp.HasAllParts)
            {
                return false;
            }
            if (t.IsForbidden(pawn))
            {
                return false;
            }

            // ★★ 2026-10-06 **防重复派活死循环**（实测踩过）：
            //   工作分配系统每 tick 都会重扫，如果我们不看"他是不是已经在做这件事"，
            //   就会一个 tick 内给同一个半成品派十几个 job，小人被反复重建 job、
            //   永远走不出第一步。实测日志：
            //     `Happy started 10 jobs in one tick. newJob=Binguin_WorkOnUnfinishedRod …`
            //   两道守卫：① 当前 job 就是这件事 → 不再派
            //             ② 队列里已经排着这件事 → 不再派
            if (pawn.CurJob != null && pawn.CurJob.def == RodJobDef
                && pawn.CurJob.targetA.Thing == t)
            {
                return false;
            }
            JobQueue q = pawn.jobs != null ? pawn.jobs.jobQueue : null;
            if (q != null)
            {
                for (int i = 0; i < q.Count; i++)
                {
                    Job queued = q[i].job;
                    if (queued != null && queued.def == RodJobDef && queued.targetA.Thing == t)
                    {
                        return false;
                    }
                }
            }

            // ★ 绑定制作者：绑了就只有他能自动来做（玩家手动派活不受此限）
            if (!forced && comp.boundMaker != null && comp.boundMaker != pawn)
            {
                return false;
            }

            if (!pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Deadly))
            {
                return false;
            }
            return true;
        }

        private static JobDef cachedRodJobDef;
        private static bool rodJobDefLookedUp;

        /// <summary>半成品工作 JobDef（懒加载缓存）。</summary>
        private static JobDef RodJobDef
        {
            get
            {
                if (!rodJobDefLookedUp)
                {
                    rodJobDefLookedUp = true;
                    cachedRodJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_WorkOnUnfinishedRod");
                }
                return cachedRodJobDef;
            }
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompBinguinUnfinishedRod comp = t.TryGetComp<CompBinguinUnfinishedRod>();
            if (comp == null)
            {
                return null;
            }
            // 顺手确认绑定：如果没人绑，就把这次来做的人绑上
            // （即：第一个来做的人成为"制作者"）
            if (comp.boundMaker == null)
            {
                comp.boundMaker = pawn;
            }
            JobDef jobDef = RodJobDef;
            if (jobDef == null)
            {
                return null;
            }
            return JobMaker.MakeJob(jobDef, t);
        }
    }
}
