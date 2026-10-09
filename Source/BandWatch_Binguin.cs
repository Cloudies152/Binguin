// ============================================================================
// 冰鹅族【看演出】行为（2026-09）
//
// 用户需求：观众会【主动找能坐的地方】坐下看演出。
//
// 做法：
//   · 新增 JobDef Binguin_WatchConcert + JobDriver_BinguinWatchConcert
//     目标 A = 座位（任何 isSittable 的建筑：原版凳子/椅子都行）
//   · 一个新的 GameComponent 在演唱会进行期间，给还没入座的观众派这个 job：
//       找场地 N 格内【没被别人占】的可坐建筑 → 走过去 → 坐下等着
//       等演唱会一结束（BinguinConcert.Active 变 false）自动结束，转去离场
//   · 找不到座位就退化成"站在场地附近看"
//
// ★ 姿势说明：目标格是座位格，RimWorld 会自动按座位姿态渲染（和原版
//   "坐椅子"同一套路径）；如果实测发现是站着的，加一句 SetInitialPosture 即可。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class JobDriver_BinguinWatchConcert : JobDriver
    {
        private const int CheckInterval = 250;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 走到座位（A）；没有座位（targetA 无效）就直接原地看
            Toil go = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            go.FailOnDespawnedOrNull(TargetIndex.A);
            yield return go;

            Toil wait = Toils_General.Wait(60000, TargetIndex.A);
            wait.socialMode = RandomSocialMode.Off;
            wait.handlingFacing = true;
            // 演唱会一结束就收工
            wait.AddEndCondition(delegate
            {
                return BinguinConcert.Active ? JobCondition.Ongoing : JobCondition.Succeeded;
            });
            yield return wait;
        }
    }

    /// <summary>
    /// 主唱在开演前【站在麦克风前等着】的 job（用户要求：
    /// "建造完成后没有乐队主唱在麦克风前面显示问号等待开始演唱会"）。
    /// 走到麦克风的操作点站定，一直等到演唱会开始（BinguinConcert.Active 变 true）为止。
    /// 头顶的问号由 GameComponent_BinguinBandWatch.GameComponentUpdate 每帧画（原版同款贴图）。
    /// </summary>
    public class JobDriver_BinguinBandWaitAtMic : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil go = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            go.FailOnDespawnedOrNull(TargetIndex.A);
            yield return go;

            // ★ 一天那么长的等待；开演那一刻立刻收工（后面由 Lord 的职责树接管）
            Toil wait = Toils_General.Wait(60000, TargetIndex.A);
            wait.socialMode = RandomSocialMode.Off;
            wait.handlingFacing = true;
            wait.AddEndCondition(delegate
            {
                return BinguinConcert.Active ? JobCondition.Succeeded : JobCondition.Ongoing;
            });
            yield return wait;
        }
    }

    /// <summary>
    /// 演唱会进行中：【乐手在乐器前演奏】（用户反馈"乐队不会去麦克风方向开始演奏"）。
    /// 目标 A = 乐器（Thing）：
    ///   Toils_Goto.GotoThing(A, PathEndMode.InteractionCell) 走到乐器的【操作点】站定
    ///   （麦克风/键盘/吉他操作点在乐器北侧、鼓/贝斯在南侧，都是 Standable 且不压地标），
    ///   然后 handlingFacing 面向乐器"一直演奏"，直到演唱会结束（BinguinConcert.Active 变 false）为止。
    /// ★ 为什么要有这个 job：原来 StartConcert 只把状态置为进行中，
    ///   乐手仍然由 Lord 的 Defend 职责派"在场地附近溜达"的活 → 他们永远不会去乐器前，
    ///   玩家看到的就是"乐队站在原地不演出"。
    /// </summary>
    public class JobDriver_BinguinPlayInstrument : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 乐器没了（被拆/被炸）就收工
            this.FailOnDespawnedOrNull(TargetIndex.A);

            Toil go = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return go;

            // ★ 一天那么长的等待；散场那一刻立刻收工（之后由 EndConcert 送所有人离场）
            Toil play = Toils_General.Wait(60000, TargetIndex.A);
            play.socialMode = RandomSocialMode.Off;
            play.handlingFacing = true;
            play.AddEndCondition(delegate
            {
                return BinguinConcert.Active ? JobCondition.Ongoing : JobCondition.Succeeded;
            });
            yield return play;
        }
    }

    /// <summary>
    /// ① 开演前：把主唱按在麦克风前站着（+头顶问号）；
    /// ② 开演后：把 5 名乐手按到各自乐器前演奏，并给还没入座的观众派"看演出"的 job。
    /// </summary>
    public class GameComponent_BinguinBandWatch : GameComponent
    {
        public GameComponent_BinguinBandWatch(Game game) : base()
        {
        }

        // ★ 每帧：给等待中的主唱画原版同款"问号"（用户要求"显示问号等待开始"）
        public override void GameComponentUpdate()
        {
            try
            {
                if (!BinguinBandConcert.bandArrived) return;
                if (BinguinConcert.Active) return;
                Pawn singer = BinguinBandConcert.LeadSinger;
                if (singer == null || singer.Dead || !singer.Spawned) return;
                BinguinBandConcert.DrawQuestionMark(singer);
            }
            catch (Exception)
            {
            }
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager == null) return;
            if (Find.TickManager.TicksGame % 120 != 0) return;

            // ---- ① 开演前：主唱站到麦克风前等 ----
            if (!BinguinConcert.Active)
            {
                KeepSingerAtMic();
                return;
            }

            // ---- ② 开演后：先把乐手按到乐器前演奏（用户反馈缺的就是这一步）----
            KeepBandAtInstruments();

            // ---- ③ 再安排观众入座 ----
            if (BinguinBandConcert.audience == null || BinguinBandConcert.audience.Count == 0) return;

            try
            {
                Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
                if (map == null) return;
                IntVec3 center = BinguinBandConcert.venueCenter;
                if (!center.IsValid) return;

                List<Pawn> audience = BinguinBandConcert.audience;
                for (int i = 0; i < audience.Count; i++)
                {
                    Pawn p = audience[i];
                    if (!Valid(p, map)) continue;
                    if (p.CurJob != null && p.CurJob.def != null
                        && p.CurJob.def.defName == "Binguin_WatchConcert") continue;   // 已经入座
                    TrySeat(p, map, center);
                }
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 安排观众入座失败：" + e.Message);
            }
        }

        /// <summary>开演前：确保主唱站在麦克风前（不在就派 Binguin_BandWaitAtMic）。</summary>
        private static void KeepSingerAtMic()
        {
            try
            {
                if (!BinguinBandConcert.bandArrived) return;
                Pawn singer = BinguinBandConcert.LeadSinger;
                if (singer == null || singer.Dead || !singer.Spawned || singer.Map == null) return;
                if (singer.Downed || singer.InMentalState) return;

                JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_BandWaitAtMic");
                if (def == null) return;
                if (singer.CurJob != null && singer.CurJob.def == def) return;   // 已经在等了

                IntVec3 cell = BinguinBandConcert.FindMicCell(singer.Map);
                if (!cell.IsValid) return;

                Job job = JobMaker.MakeJob(def, cell);
                job.expiryInterval = -1;
                job.playerForced = false;
                singer.jobs.StartJob(job, JobCondition.InterruptForced);
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 安排主唱到麦克风前失败：" + e.Message);
            }
        }

        /// <summary>
        /// ★ 乐手顺序 → 乐器 defName（performers[0] = 主唱 = 麦克风）。
        /// 顺序对应 Defs/10_FoodAndJoy/BandVisit_Binguin.xml 里 BuildSketch 的摆位：
        ///   麦克风 (0,-8) / 吉他 (+2,-8) / 键盘 (-3,-8) / 鼓 (+2,-11) / 贝斯 (-3,-11)。
        /// </summary>
        private static readonly string[] BandInstruments = new string[]
        {
            "Binguin_Band_Microphone",
            "Binguin_Band_Guitar",
            "Binguin_Band_Keyboard",
            "Binguin_Band_DrumKit",
            "Binguin_Band_Bass",
        };

        private static bool loggedBandOnStage;

        /// <summary>演唱会进行中：把每位乐手按到自己的乐器前演奏（不在就派 Binguin_PlayInBand）。</summary>
        private static void KeepBandAtInstruments()
        {
            try
            {
                List<Pawn> band = BinguinBandConcert.performers;
                if (band == null || band.Count == 0) return;

                JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_PlayInBand");
                if (def == null)
                {
                    if (!loggedBandOnStage)
                    {
                        loggedBandOnStage = true;
                        Log.Warning("[冰鹅族] 找不到 JobDef Binguin_PlayInBand，乐手无法就位演奏。");
                    }
                    return;
                }

                Map map = null;
                int onStage = 0;
                for (int i = 0; i < band.Count && i < BandInstruments.Length; i++)
                {
                    Pawn p = band[i];
                    if (p == null || p.Dead || p.Downed || !p.Spawned || p.Map == null) continue;
                    if (p.InMentalState) continue;
                    if (map == null) map = p.Map;
                    if (p.Map != map) continue;

                    Thing inst = FindInstrument(map, BandInstruments[i]);
                    if (inst == null) continue;

                    // 已经在自己的乐器前演奏 → 不动他
                    if (p.CurJob != null && p.CurJob.def == def
                        && p.CurJob.targetA.Thing == inst)
                    {
                        onStage++;
                        continue;
                    }

                    Job job = JobMaker.MakeJob(def, inst);
                    job.expiryInterval = -1;
                    job.playerForced = false;
                    p.jobs.StartJob(job, JobCondition.InterruptForced);
                    onStage++;
                }

                if (!loggedBandOnStage && onStage > 0)
                {
                    loggedBandOnStage = true;
                    Log.Message("[冰鹅族] 乐队已就位开始演奏（" + onStage + " 人各自走向麦克风/吉他/键盘/鼓/贝斯）。");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 安排乐手到乐器前失败：" + e.Message);
            }
        }

        /// <summary>地图上离场地中心最近的那件指定乐器。</summary>
        private static Thing FindInstrument(Map map, string defName)
        {
            ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (d == null) return null;
            List<Thing> list = map.listerThings.ThingsOfDef(d);
            Thing best = null;
            float bestDist = float.MaxValue;
            IntVec3 center = BinguinBandConcert.venueCenter;
            for (int i = 0; i < list.Count; i++)
            {
                Thing t = list[i];
                if (t == null || t.Destroyed || !t.Spawned) continue;
                float dist = center.IsValid ? (t.Position - center).LengthHorizontal : 0f;
                if (dist < bestDist)
                {
                    best = t;
                    bestDist = dist;
                }
            }
            return best;
        }

        private static bool Valid(Pawn p, Map map)
        {
            return p != null && !p.Dead && !p.Downed && p.Spawned && p.Map == map
                && p.jobs != null && p.mindState != null;
        }

        private static void TrySeat(Pawn p, Map map, IntVec3 center)
        {
            Building seat = FindFreeSeat(p, map, center);
            if (seat == null) return;

            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_WatchConcert");
            if (def == null) return;

            Job job = JobMaker.MakeJob(def);
            job.targetA = seat;
            job.expiryInterval = -1;
            job.playerForced = false;
            p.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        /// <summary>场地 20 格内、没人占的可坐建筑。</summary>
        private static Building FindFreeSeat(Pawn p, Map map, IntVec3 center)
        {
            List<Thing> things = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            Building best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < things.Count; i++)
            {
                Building b = things[i] as Building;
                if (b == null || b.Destroyed || b.def == null || b.def.building == null) continue;
                if (!b.def.building.isSittable) continue;
                float dist = (b.Position - center).LengthHorizontal;
                if (dist > 20f) continue;
                if (dist >= bestDist) continue;
                if (!p.CanReserveAndReach(b, PathEndMode.OnCell, Danger.Some)) continue;
                if (IsTaken(map, b)) continue;
                best = b;
                bestDist = dist;
            }
            return best;
        }

        private static bool IsTaken(Map map, Building seat)
        {
            var all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn q = all[i];
                if (q == null || q.jobs == null || q.CurJob == null) continue;
                if (q.CurJob.targetA.Thing == seat) return true;
                if (q.Position == seat.Position && q.pather != null && !q.pather.Moving) return true;
            }
            return false;
        }
    }
}
