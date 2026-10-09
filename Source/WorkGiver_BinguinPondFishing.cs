// ============================================================================
// 鱼池钓鱼 WorkGiver（2026-08-20 用户需求）
// 殖民者开启原版「钓鱼」工作类型（WorkTypeDef Fishing，Odyssey）后，
// 会自动寻找有鱼的鱼池并走过去垂钓 —— 等效原版"在自然水边自动钓鱼"。
//   - PotentialWorkThingsGlobal：地图上所有鱼池
//   - HasJobOnThing：CanFishNow()（已放苗、鱼群 > 保留数、可到达）
//   - JobOnThing：Binguin_PondFish（JobDriver_BinguinPondFish 走到池边
//     垂钓 500 tick → 扣组鱼群并产 1 条）
// ★ giverClass 由 BinguinDefPatches 静态构造替换（XML 零自定义类型）。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class WorkGiver_BinguinPondFishing : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode
        {
            get { return PathEndMode.Touch; }
        }

        // ★ 2026-09 性能：pond/job 的 def 名是常量 → 懒加载缓存，避免每次工作
        //   扫描（PotentialWorkThingsGlobal 是迭代器，每次枚举都会重跑）都查库。
        private static ThingDef cachedPondDef;

        private static ThingDef PondDef
        {
            get
            {
                if (cachedPondDef == null)
                {
                    cachedPondDef = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_FishPond");
                }
                return cachedPondDef;
            }
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            ThingDef pondDef = PondDef;
            if (pondDef == null || pawn.Map == null)
            {
                yield break;
            }
            foreach (Thing t in pawn.Map.listerThings.ThingsOfDef(pondDef))
            {
                if (t.Destroyed || t.IsForbidden(pawn))
                {
                    continue;
                }
                CompBinguinFishPond comp = t.TryGetComp<CompBinguinFishPond>();
                if (comp == null)
                {
                    continue;
                }
                // ★ 每组只以组长为扫描目标（组成员鱼群数据都在组长上，
                //   避免同一组重复排队钓鱼 job）
                if (!comp.IsLeaderComp)
                {
                    continue;
                }
                yield return t;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t.Destroyed)
            {
                return false;
            }
            // ★★ 2026-10-06 用户澄清（重要，别再加回去）：
            //   用户原话：「我指的是钓鱼工作可以算娱乐，不是指把钓鱼工作去掉变成娱乐，
            //              所以冰鹅依然会执行钓鱼工作」
            //   ⇒ 「钓鱼工作」**保留**，冰鹅照旧会被工作系统派来钓鱼。
            //     只是干活的同时娱乐条也涨（见 `JobDriver_BinguinPondFish` 里的
            //     `JoyUtility.JoyTickCheckEnd`，以及 JobDef 上的 joyKind/joyGainRate）。
            //   ⚠️ 我曾在这里加过一句 `if (BinguinRaceUtility.IsBinguin(pawn)) return false;`
            //      （把冰鹅从这份工作里排除）—— 那是**理解反了**，已删除。
            //      钓鱼工作对所有种族（含冰鹅）一视同仁。
            CompBinguinFishPond comp = t.TryGetComp<CompBinguinFishPond>();
            if (comp == null)
            {
                return false;
            }
            CompBinguinFishPond lead = comp.LeaderComp;
            // ★ 2026-08-20：尊重「保留鱼数」下限 —— 鱼群 <= keepMin 时不钓
            if (lead == null || !lead.CanFishNow())
            {
                return false;
            }
            if (!pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Deadly))
            {
                return false;
            }
            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            JobDef jd = CompBinguinFishPond.PondFishJobDef;
            if (jd == null)
            {
                return null;
            }
            Job job = new Job(jd, t);
            job.count = 1;
            return job;
        }
    }
}
