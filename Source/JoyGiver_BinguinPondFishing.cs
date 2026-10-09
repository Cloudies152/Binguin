// ============================================================================
// 冰鹅族 · 鱼池钓鱼【娱乐】JoyGiver（2026-10-06 用户需求）
//
// 用户需求原文：「把冰鹅族的钓鱼改为娱乐，即冰鹅族钓鱼的时候会增加娱乐条，
//                并且在娱乐时会主动去钓鱼」
//
// 拆成两件事：
//   ① **钓鱼时增加娱乐条** → 见 `JobDriver_BinguinPondFish`（它现在每 tick 调
//      `JoyUtility.JoyTickCheckEnd`）；并且 `Binguin_PondFish` 这个 JobDef
//      必须带 `joyKind` + `joyGainRate`（IL 实证：`JoyTickCheckEnd` 会读这两项
//      来调 `Need_Joy.GainJoy`，没有 joyKind 会直接 Log.Warning 并返回）。
//   ② **娱乐时主动去钓鱼** → 本文件。原版娱乐系统靠 `JoyGiver` 找人找点：
//      `Pawn_JobTracker` 在"需要娱乐"时遍历 `DefDatabase<JoyGiverDef>.AllDefs`，
//      调 `CanBeGivenTo` → `TryGiveJob`，拿到 job 就去做。
//
// ★★ 为什么只在冰鹅族身上生效：
//    `JoyGiverDef` **没有种族限制字段**（IL 实证，只有 giverClass / baseChance /
//    thingDefs / jobDef / joyKind 等）⇒ 只能在自己的 `CanBeGivenTo` 里判种族。
//    用本 mod 统一的 `BinguinRaceUtility.IsBinguin(pawn)`，不要自己写
//    `def.defName == "Binguin"`（见 BinguinRaceUtility.cs 顶部的收口说明）。
//
// ★ 工作侧的配合（见 `WorkGiver_BinguinPondFishing.HasJobOnThing`）：
//    那边加了"冰鹅族不做这份工作"的判断 ⇒ 冰鹅只会因为**娱乐**去钓鱼，
//    不会因为"钓鱼工作"去钓鱼。别的种族/机器人不受影响。
//
// ★ XML 零自定义类型：`JoyGiverDef.giverClass` 在 XML 里用原版 `JoyGiver_Meditate`
//   占位（`JoyGiver` 是 abstract，占位成它会导致实例化异常），
//   由 `BinguinDefPatches` 静态构造替换并清 `workerInt` 缓存。
// ============================================================================

using RimWorld;
using Verse;
using Verse.AI;

namespace Binguin
{
    /// <summary>
    /// 冰鹅族：需要娱乐时会主动去鱼池钓鱼。
    /// </summary>
    public class JoyGiver_BinguinPondFishing : JoyGiver
    {
        /// <summary>鱼池 def（懒加载缓存，沿用本 mod 的 2026-09 性能惯例）。</summary>
        private static ThingDef cachedPondDef;
        private static bool pondDefLookedUp;

        private static ThingDef PondDef
        {
            get
            {
                if (!pondDefLookedUp)
                {
                    pondDefLookedUp = true;
                    cachedPondDef = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_FishPond");
                }
                return cachedPondDef;
            }
        }

        /// <summary>
        /// 只有冰鹅族能拿到这个娱乐方式。
        /// ★ 基类的 `CanBeGivenTo` 会做一些通用校验（能不能娱乐、有没有对应能力等），
        ///   所以这里先调 base 再叠加种族判断。
        /// </summary>
        public override bool CanBeGivenTo(Pawn pawn)
        {
            if (!base.CanBeGivenTo(pawn))
            {
                return false;
            }
            return BinguinRaceUtility.IsBinguin(pawn);
        }

        /// <summary>
        /// 把候选对象（鱼池）填进 outCandidates。
        /// ★ 必须自己实现：`JoyGiverDef.thingDefs` **不会**被基类自动用上
        ///   （原版每个 JoyGiver 子类都是自己 `GetSearchSet` / 自己扫的）。
        /// ★ 访问修饰符必须是 `protected`（基类 `JoyGiver.GetSearchSet` 就是
        ///   protected；写 public 会 CS0507"无法更改访问修饰符"）。
        /// </summary>
        protected override void GetSearchSet(Pawn pawn, System.Collections.Generic.List<Thing> outCandidates)
        {
            outCandidates.Clear();
            if (pawn == null || pawn.Map == null)
            {
                return;
            }
            ThingDef def = PondDef;
            if (def == null)
            {
                return;
            }
            foreach (Thing t in pawn.Map.listerThings.ThingsOfDef(def))
            {
                if (t != null && !t.Destroyed && !t.IsForbidden(pawn))
                {
                    outCandidates.Add(t);
                }
            }
        }

        /// <summary>
        /// 挑一个鱼池，返回钓鱼 job；没有可钓的就返回 null。
        /// </summary>
        public override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
            {
                return null;
            }
            // ★ 本 mod 鱼池的鱼群数据都在"组长"上（见 CompBinguinFishPond.IsLeaderComp）
            Thing pond = FindBestPond(pawn);
            if (pond == null)
            {
                return null;
            }
            JobDef jd = CompBinguinFishPond.PondFishJobDef;
            if (jd == null)
            {
                return null;
            }
            Job job = new Job(jd, pond);
            job.count = 1;
            return job;
        }

        /// <summary>
        /// 就近挑一个"现在能钓"的鱼池。
        /// ★ 判定复用 `CompBinguinFishPond.CanFishNow()`（含"保留鱼数"下限），
        ///   与工作侧 `WorkGiver_BinguinPondFishing` 完全一致 ⇒ 两边口径不会漂。
        /// </summary>
        private static Thing FindBestPond(Pawn pawn)
        {
            ThingDef def = PondDef;
            if (def == null)
            {
                return null;
            }
            Thing best = null;
            float bestDist = float.MaxValue;
            foreach (Thing t in pawn.Map.listerThings.ThingsOfDef(def))
            {
                if (t == null || t.Destroyed || t.IsForbidden(pawn))
                {
                    continue;
                }
                CompBinguinFishPond comp = t.TryGetComp<CompBinguinFishPond>();
                if (comp == null)
                {
                    continue;
                }
                // ★ 只认组长（组成员共用组长的鱼群数据；不这么判会给同一组重复排队）
                if (!comp.IsLeaderComp)
                {
                    continue;
                }
                CompBinguinFishPond lead = comp.LeaderComp;
                if (lead == null || !lead.CanFishNow())
                {
                    continue;
                }
                // 存不存在一条能走到的路
                if (!pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Deadly))
                {
                    continue;
                }
                float d = (t.Position - pawn.Position).LengthHorizontalSquared;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = t;
                }
            }
            return best;
        }
    }
}
