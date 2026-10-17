// ============================================================================
// 冰鹅族【自办演唱会】· 演出总控（2026-10-11）
//
// ★★ 职责（就这一件事，别的都交给已有代码）：
//     开演时把三种 job 派下去 —— 其余全部复用现有实现：
//       · 乐手  → `Binguin_PlayInBand`      （JobDriver_BinguinPlayInstrument，已有）
//       · 主唱  → `Binguin_PlayInBand`（麦克风）
//       · 观众  → `Binguin_WatchConcert`    （JobDriver_BinguinWatchConcert，已有）
//     收尾由**已有**的 `GameComponent_BinguinBandConcertEnd` 负责
//     （它检测 `BinguinConcert.Active` 由 true 变 false 就调用 `EndConcert`），
//     所以这里**不重复实现收尾**。
//
// ★★ 为什么派 job 而不是用 Lord：
//     我们的行为是**明确指派某个人去某件乐器**，用 job 最直接可控。
//     （`LordJob_BinguinCrowd` 那套是给"外团从地图边缘走进来"用的。）
//
// ★★ 观众范围：一期 = 地图上所有**能自由行动**的殖民者
//     （作者没要求手选观众；"乐手可以缺人"是明确要求）。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Binguin.Helper;

namespace Binguin.Feature.Band
{
    /// <summary>自办演出的开演入口 + 演出期间的轻量维护。</summary>
    public static class BinguinConcertDirector
    {
        /// <summary>本场演出的观众（用于收尾时"只给到场的人发心情"）。</summary>
        public static List<Pawn> Audience = new List<Pawn>();

        /// <summary>本场演出的乐手（含主唱）。</summary>
        public static List<Pawn> Performers = new List<Pawn>();

        /// <summary>本场是不是玩家自办的（区别于外团来访那套）。</summary>
        public static bool SelfHosted;

        /// <summary>
        /// 开演。由 `GameComponent_BinguinConcertBooking.StartNow()` 调用。
        /// ★ 不抛异常：任何一个环节出问题都只记日志，避免卡住存档 tick。
        /// </summary>
        public static void BeginPerformance(BinguinConcertBooking b, Map map)
        {
            try
            {
                if (b == null || map == null)
                {
                    return;
                }
                SelfHosted = true;
                Audience = new List<Pawn>();
                Performers = new List<Pawn>();

                // ① 主唱 → 麦克风（找不到麦克风就退回场地中心，不阻断演出）
                IntVec3 micCell = b.venueCenter.IsValid ? b.venueCenter : b.leadSinger.Position;
                SendPlayJob(b.leadSinger, micCell);
                Performers.Add(b.leadSinger);

                // ② 乐手 → 各自乐器
                //    `musicians` 的 key 是乐器 **defName**；按 defName 在地图上找最近的实例。
                if (b.musicians != null)
                {
                    foreach (KeyValuePair<string, Pawn> kv in b.musicians)
                    {
                        Pawn p = kv.Value;
                        if (p == null || p.Dead || !p.Spawned)
                        {
                            continue;   // 缺人就跳过（作者定的"乐手可缺人"）
                        }
                        Thing inst = FindNearestInstrument(map, kv.Key, b.venueCenter);
                        if (inst == null)
                        {
                            continue;   // 乐器被拆了也不阻断（作者定：只有主唱死了才取消）
                        }
                        SendPlayJob(p, inst);
                        Performers.Add(p);
                    }
                }

                // ③ 观众 → 看演出（地图上所有能自由行动的殖民者）
                List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn c = colonists[i];
                    if (c == null || c.Dead)
                    {
                        continue;
                    }
                    if (Performers.Contains(c))
                    {
                        continue;   // 表演者不给自己当观众
                    }
                    SendWatchJob(c, b.venueCenter);
                    Audience.Add(c);
                }

                // ④ 外派系贵宾 → 从地图边缘进场（通讯台发过邀请、对方答应了的）
                List<Pawn> guests = BinguinConcertGuests.SpawnInvitedGuests(b, map);
                for (int i = 0; i < guests.Count; i++)
                {
                    SendWatchJob(guests[i], b.venueCenter);
                    Audience.Add(guests[i]);
                }

                BinguinLogUtility.Log("自办演唱会开演：主唱 1 人、乐手 "
                    + (Performers.Count - 1) + " 人、观众 " + Audience.Count + " 人。",
                    isDebug: false);
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("开演时出错（已忽略，演出状态可能不完整）：" + e,
                    severity: 1, isDebug: false);
            }
        }

        /// <summary>把"演奏"job 派给某人；目标可以是乐器（Thing）或一个格子（麦克风位）。</summary>
        private static void SendPlayJob(Pawn p, Thing instrument)
        {
            if (p == null || instrument == null)
            {
                return;
            }
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_PlayInBand");
            if (def == null)
            {
                BinguinLogUtility.Log("找不到 JobDef Binguin_PlayInBand，乐手未派活。",
                    severity: 1, isDebug: false);
                return;
            }
            Job job = JobMaker.MakeJob(def, instrument);
            p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        /// <summary>把一个格子当"乐器位"派活（麦克风只有格子时用）。</summary>
        private static void SendPlayJob(Pawn p, IntVec3 cell)
        {
            if (p == null)
            {
                return;
            }
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_PlayInBand");
            if (def == null)
            {
                return;
            }
            Job job = JobMaker.MakeJob(def);
            job.targetA = new LocalTargetInfo(cell);
            p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        /// <summary>把"观看"job 派给某人（目标 = 场地中心那一格）。</summary>
        private static void SendWatchJob(Pawn p, IntVec3 center)
        {
            if (p == null)
            {
                return;
            }
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_WatchConcert");
            if (def == null)
            {
                BinguinLogUtility.Log("找不到 JobDef Binguin_WatchConcert，观众未派活。",
                    severity: 1, isDebug: false);
                return;
            }
            Job job = JobMaker.MakeJob(def);
            job.targetA = new LocalTargetInfo(center);
            p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        /// <summary>按乐器 defName 找离场地中心最近的实例。</summary>
        private static Thing FindNearestInstrument(Map map, string instrumentDefName, IntVec3 near)
        {
            if (map == null || instrumentDefName.NullOrEmpty())
            {
                return null;
            }
            Thing best = null;
            float bestDist = float.MaxValue;
            List<Building> all = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < all.Count; i++)
            {
                Building bd = all[i];
                if (bd == null || bd.def == null || bd.def.defName != instrumentDefName)
                {
                    continue;
                }
                float d = (bd.Position - near).LengthHorizontalSquared;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = bd;
                }
            }
            return best;
        }

        /// <summary>
        /// 本场结束后由**已有**的 `GameComponent_BinguinBandConcertEnd` 调用
        /// （它检测 `BinguinConcert.Active` 由 true 变 false）。
        /// 这里只清自办演出的运行时状态。
        /// </summary>
        public static void OnPerformanceEnded()
        {
            Audience = new List<Pawn>();
            Performers = new List<Pawn>();
            SelfHosted = false;
        }

        /// <summary>
        /// 【人员到场】—— 到预定日子时调用（**还没开演**，只是让大家走到场地）。
        /// ★ 作者要求：「开始之前需要弹个信封显示人员到场，然后再由玩家主动开始演唱会」
        /// ⇒ 这里只派"走过去"的 job，演出本身等玩家点麦克风。
        /// </summary>
        public static void AssembleOnSite(BinguinConcertBooking b, Map map)
        {
            try
            {
                if (b == null || map == null) return;
                IntVec3 venue = b.venueCenter.IsValid
                    ? b.venueCenter
                    : (b.leadSinger != null ? b.leadSinger.Position : IntVec3.Invalid);

                SendGoto(b.leadSinger, venue);

                if (b.musicians != null)
                {
                    foreach (KeyValuePair<string, Pawn> kv in b.musicians)
                    {
                        Pawn p = kv.Value;
                        if (p == null || p.Dead || !p.Spawned) continue;
                        Thing inst = FindNearestInstrument(map, kv.Key, venue);
                        SendGoto(p, inst != null ? inst.Position : venue);
                    }
                }

                List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn c = colonists[i];
                    if (c == null || c.Dead) continue;
                    if (c == b.leadSinger) continue;
                    SendGoto(c, venue);
                }

                BinguinLogUtility.Log("演唱会人员已到场，等玩家点麦克风开演。", isDebug: false);

                // ★ 2026-10-11 修「邀请的人没有来」：
                //   贵宾原来在 `BeginPerformance`（开演）时才生成 —— 但作者要的是
                //   「信封显示人员到场」时就应该一起到场。⇒ 挪到到场阶段生成。
                List<Pawn> guests = BinguinConcertGuests.SpawnInvitedGuests(b, map);
                for (int i = 0; i < guests.Count; i++)
                {
                    SendGoto(guests[i], venue);
                }
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("人员到场时出错（已忽略）：" + e.Message, severity: 1, isDebug: false);
            }
        }

        /// <summary>派一个"走到某格"的 job（不依赖我们自己的 JobDriver）。</summary>
        private static void SendGoto(Pawn p, IntVec3 cell)
        {
            if (p == null || p.Dead || !p.Spawned || !cell.IsValid) return;
            try
            {
                Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
                p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
            catch (System.Exception) { }
        }

        /// <summary>
        /// 到场后**持续把人留在位置上**。
        /// ★ 2026-10-11 修「主唱没有到自己的位置」：
        ///   原来只派一次 `JobDefOf.Goto`，走到就结束，小人随即干别的去了。
        ///   ⇒ 改为在等待开演期间**每 250 tick 重新派一次 Goto**（幂等、无副作用），
        ///     这样人一直待在麦克风/各自乐器旁边，直到玩家开演。
        /// </summary>
        public static void KeepPerformersInPlace(BinguinConcertBooking b, Map map)
        {
            if (b == null || map == null || b.leadSinger == null) return;
            IntVec3 venue = b.venueCenter.IsValid ? b.venueCenter : b.leadSinger.Position;
            SendGoto(b.leadSinger, venue);
            if (b.musicians != null)
            {
                foreach (KeyValuePair<string, Pawn> kv in b.musicians)
                {
                    Pawn p = kv.Value;
                    if (p == null || p.Dead || !p.Spawned) continue;
                    Thing inst = FindNearestInstrument(map, kv.Key, venue);
                    SendGoto(p, inst != null ? inst.InteractionCell : venue);
                }
            }
        }

        /// <summary>送客 / 收工（6 小时没开演被取消时用）。</summary>
        public static void SendEveryoneHome(BinguinConcertBooking b, Map map)
        {
            try
            {
                if (b == null) return;
                if (b.musicians != null)
                {
                    foreach (KeyValuePair<string, Pawn> kv in b.musicians)
                    {
                        if (kv.Value != null)
                        {
                            kv.Value.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                        }
                    }
                }
            }
            catch (System.Exception) { }
        }
    }
}
