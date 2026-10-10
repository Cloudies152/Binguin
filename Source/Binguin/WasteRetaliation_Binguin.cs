// ============================================================================
// 冰鹅族·毒垃圾报复事件（2026-09 用户需求）
//
// ★ 原版链路（全部 IL 实证，都在 RimWorld.CompDissolutionEffect_Goodwill 里）：
//   ① 玩家把毒垃圾丢到【世界地图】上（商队丢弃 / 运输舱发射到据点附近）
//   ② 垃圾随时间溶解 → WorldUpdate 按地块分组 → TryGetAffectedSettlement
//      找到附近据点 → 扣好感 + 记 HistoryEventDefOf.ToxicWasteDumping
//   ③ Rand.Chance(...) 通过后调用 TriggerRetaliationEvent(Faction)（private static）
//   ④ 该方法把 PollutionRetaliation（丢垃圾袋）和 PollutionRaid（普通袭击）
//      塞进 tmpRetaliations，然后 RandomElement 抽一个生成任务
//      —— 【原版的 50/50 就在这里，我们不改它】
//
// ★ 用户需求：当受害派系是【冰鹅】时改成——
//     · 50% → 我们的版本：200~300 袋毒垃圾 + 10 只发狂爆炸羊，
//              空投到玩家【财富最高的房间】正中央
//     · 50% → 原版 PollutionRaid（普通袭击）
//   所以这里前缀拦 TriggerRetaliationEvent：是冰鹅就自己分流，否则原样交回原版。
//
// ★ 三个关键实现决定（都写了原因，别随手改）：
//   ① 爆炸羊的"发狂"不能在这里直接设：空投的羊在运输舱落地前
//      （约 110~180 tick）是【未 spawn】状态，这时候设 mentalState 会被丢掉。
//      改成挂一个自定义 hediff（Binguin_WasteToxemia），由它的 comp 在
//      【落地后的第一个 tick】再开 ManhunterPermanent —— hediff 只在
//      spawn 后才 tick，时机天然正确，而且不用额外的 GameComponent。
//   ② "1~2 小时致死"不去叠伤口算流血速度（不可控、还容易打断腿），
//      而是由同一个 comp 每 tick 把原版 BloodLoss.severity 线性推到 1.0
//      （BloodLoss 的 lethalSeverity = 1）。时间于是被钉死。
//   ③ 羊身上照样加几道皮外伤，只为"看起来惨"；真正的死因是失血。
//      伤口一律只打【核心部位（躯干）】，绝不碰腿 —— 用户明确要求保证能移动。
// ============================================================================

using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using Verse;
using Verse.AI;

namespace Binguin
{
    // ---------- 前缀：拦原版的毒垃圾报复分流 ----------
    public static class Patch_BinguinWasteRetaliation
    {
        // 原版签名（Mono.Cecil 实证 2026-10-01）：
        //   private static void TriggerRetaliationEvent(Faction **faction**)
        //   —— 形参名就是 `faction`，不是 `enemyFaction`。
        //   Harmony 默认按名字注入 ⇒ 名字写错整条补丁挂不上（启动日志报
        //   「Parameter "enemyFaction" not found in method …」）。
        public static bool Prefix(Faction faction)
        {
            try
            {
                if (faction == null || faction.def == null
                    || faction.def.defName != BinguinWasteRetaliation.FactionDefName)
                {
                    return true;                        // 不是冰鹅 → 原版逻辑原样跑
                }
                if (Rand.Chance(BinguinWasteRetaliation.SpecialChance))
                {
                    BinguinWasteRetaliation.DoRetaliation(faction);
                }
                else
                {
                    BinguinWasteRetaliation.DoVanillaRaid(faction);
                }
                return false;                           // 两种情况我们都处理完了
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 毒垃圾报复事件异常，已交回原版处理：" + e);
                return true;
            }
        }
    }

    public static class BinguinWasteRetaliation
    {
        public const string FactionDefName = "Binguin";
        // 用户定稿：50% 特制报复 / 50% 普通袭击
        public const float SpecialChance = 0.5f;

        // 用户定稿：200~300 袋毒垃圾 + 10 只爆炸羊
        private const int WastepackMin = 200;
        private const int WastepackMax = 300;
        private const int BoomalopeCount = 10;

        private const string BoomalopeKindDefName = "Boomalope";
        private const string ToxemiaHediffDefName = "Binguin_WasteToxemia";

        // "房间"的筛选：太小（过道/厕所）没意义，太大（露天区域）不是房间
        private const int MinRoomCells = 9;
        private const int MaxRoomCells = 400;

        private const int DropOpenDelay = 110;

        // ---------- 特制报复：空投毒垃圾 + 发狂爆炸羊 ----------
        public static void DoRetaliation(Faction faction)
        {
            Map map = PickMap();
            if (map == null)
            {
                Log.Warning("[冰鹅族] 毒垃圾报复：找不到玩家地图，本次跳过。");
                return;
            }
            float wealth;
            IntVec3 spot = FindRichestRoomCenter(map, out wealth);
            if (!spot.IsValid)
            {
                // 没有合格房间（比如全露天基地）→ 退到"殖民地中心附近找个空地"
                if (!CellFinder.TryFindRandomCellNear(map.Center, map, 20,
                        delegate(IntVec3 c) { return c.Standable(map) && !c.Fogged(map); }, out spot))
                {
                    spot = DropCellFinder.TradeDropSpot(map);
                }
                Log.Message("[冰鹅族] 毒垃圾报复：没有找到合格房间，落点退到 " + spot + "。");
            }

            List<Thing> payload = new List<Thing>();

            // ① 毒垃圾袋（原版 PollutionRetaliation 也是一个个 new 出来的，见其 IL）
            int want = Rand.RangeInclusive(WastepackMin, WastepackMax);
            int made = 0;
            for (int i = 0; i < want; i++)
            {
                Thing pack = ThingMaker.MakeThing(ThingDefOf.Wastepack, null);
                if (pack == null)
                {
                    break;
                }
                payload.Add(pack);
                made++;
            }

            // ② 发狂爆炸羊
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(BoomalopeKindDefName);
            HediffDef toxemia = DefDatabase<HediffDef>.GetNamedSilentFail(ToxemiaHediffDefName);
            int animals = 0;
            if (kind != null)
            {
                for (int i = 0; i < BoomalopeCount; i++)
                {
                    Pawn p = PawnGenerator.GeneratePawn(kind, null);   // 无派系 = 野生动物
                    if (p == null)
                    {
                        continue;
                    }
                    // ★ 状态必须在【进运输舱之前】就挂好：hediff 与伤势会跟着
                    //   同一个 Pawn 实例落地；而"发狂"由 hediff 的 comp 在落地后开
                    //   （见文件头 ① 的说明）。
                    ApplyToxemiaState(p, toxemia);
                    payload.Add(p);
                    animals++;
                }
            }
            else
            {
                Log.Warning("[冰鹅族] 毒垃圾报复：找不到 Boomalope 这个 PawnKindDef（没装 Biotech？）。");
            }

            if (payload.Count == 0)
            {
                Log.Warning("[冰鹅族] 毒垃圾报复：什么都没生成出来，本次跳过。");
                return;
            }

            // ③ 空投（砸穿屋顶，落在房间正中央）
            DropPodUtility.DropThingsNear(spot, map, payload, DropOpenDelay,
                false, false, true, false, true, faction);

            Find.LetterStack.ReceiveLetter(
                "Binguin_WasteRetaliation_LetterLabel".Translate(),
                "Binguin_WasteRetaliation_LetterText".Translate(made, animals),
                LetterDefOf.ThreatBig);

            Log.Message("[冰鹅族] 毒垃圾报复（冰鹅特制版）：落点 " + spot + "（该房间财富 "
                + wealth.ToString("0") + "），空投毒垃圾 " + made + " 袋 + 发狂爆炸羊 "
                + animals + " 只。");
        }

        // ---------- 另外 50%：走原版 PollutionRaid（普通袭击） ----------
        public static void DoVanillaRaid(Faction faction)
        {
            Map map = PickMap();
            if (map == null)
            {
                return;
            }
            // 复刻原版 TriggerRetaliationEvent 里搭 Slate 的那几行（IL 实证），
            // 只是把候选列表固定成 PollutionRaid 一个 —— 相当于原版 50/50 里的另一半。
            Slate slate = new Slate();
            slate.Set<Map>("map", map, false);
            slate.Set<Faction>("enemyFaction", faction, false);
            slate.Set<float>("points", StorytellerUtility.DefaultThreatPointsNow(map), false);
            QuestScriptDef raid = QuestScriptDefOf.PollutionRaid;
            if (raid != null && raid.CanRun(slate, map))
            {
                QuestUtility.GenerateQuestAndMakeAvailable(raid, slate);
                Log.Message("[冰鹅族] 毒垃圾报复：本次走原版 PollutionRaid（普通袭击）。");
                return;
            }
            // 兜底：连任务都开不了就直接按一次普通袭击打
            Log.Warning("[冰鹅族] 毒垃圾报复：PollutionRaid 不可用，改为直接发起一次袭击。");
            IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail("RaidEnemy");
            if (def == null || def.Worker == null)
            {
                return;
            }
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(
                IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = faction;
            parms.forced = true;
            def.Worker.TryExecute(parms);
        }

        // ---------- 给一只爆炸羊套上"必死"状态 ----------
        private static void ApplyToxemiaState(Pawn p, HediffDef toxemia)
        {
            if (p == null || p.Dead || p.health == null)
            {
                return;
            }
            // ① 看起来惨：几道皮外伤。★ 只打核心部位（躯干），绝不碰腿，
            //    保证它还能跑能咬（用户明确要求"不能打断腿"）。
            BodyPartRecord core = (p.RaceProps != null && p.RaceProps.body != null)
                ? p.RaceProps.body.corePart : null;
            if (core != null)
            {
                int cuts = Rand.RangeInclusive(3, 5);
                for (int i = 0; i < cuts; i++)
                {
                    Hediff_Injury inj = HediffMaker.MakeHediff(HediffDefOf.Cut, p, core)
                        as Hediff_Injury;
                    if (inj == null)
                    {
                        break;
                    }
                    inj.Severity = Rand.Range(4f, 8f);
                    p.health.AddHediff(inj, core);
                }
            }
            // ② 必死：毒血 hediff，comp 会把 BloodLoss 在 1~2 小时内推到 1.0
            if (toxemia != null && !p.health.hediffSet.HasHediff(toxemia))
            {
                p.health.AddHediff(toxemia);
            }
        }

        // ---------- 找玩家地图 ----------
        private static Map PickMap()
        {
            Map map = null;
            try
            {
                // canBeSpace = false：绝不要把垃圾和爆炸羊丢进太空地图
                map = QuestGen_Get.GetMap(false, null, false);
            }
            catch (Exception)
            {
            }
            if (map == null)
            {
                map = Find.AnyPlayerHomeMap;
            }
            return map;
        }

        // ---------- 找"财富最高的房间"的中心格 ----------
        private static IntVec3 FindRichestRoomCenter(Map map, out float bestWealth)
        {
            bestWealth = 0f;
            IntVec3 best = IntVec3.Invalid;
            if (map == null || map.regionGrid == null)
            {
                return best;
            }
            IReadOnlyList<Room> rooms = map.regionGrid.AllRooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                Room r = rooms[i];
                if (r == null || r.PsychologicallyOutdoors)
                {
                    continue;                       // 露天区域不算"房间"
                }
                if (r.CellCount < MinRoomCells || r.CellCount > MaxRoomCells)
                {
                    continue;
                }
                float w = r.GetStat(RoomStatDefOf.Wealth);
                if (w <= bestWealth)
                {
                    continue;
                }
                bestWealth = w;
                best = r.ExtentsClose.CenterCell;
            }
            return best;
        }
    }

    // ---------- 毒血 hediff 的 comp ----------
    public class HediffCompProperties_BinguinWasteToxemia : HediffCompProperties
    {
        // 从中毒到失血致死的时间（tick）。1 小时 = 2500 tick。
        // 用户要求 1~2 小时 → 默认 3750 tick（= 1.5 小时）。
        public int ticksToDeath = 3750;
        // 落地后是否自动永久发狂（爆炸羊用）
        public bool makeManhunter = true;

        public HediffCompProperties_BinguinWasteToxemia()
        {
            this.compClass = typeof(HediffComp_BinguinWasteToxemia);
        }
    }

    public class HediffComp_BinguinWasteToxemia : HediffComp
    {
        private int ticksAlive;

        public HediffCompProperties_BinguinWasteToxemia Props
        {
            get { return (HediffCompProperties_BinguinWasteToxemia)this.props; }
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            Pawn p = this.Pawn;
            if (p == null || p.Dead || p.health == null)
            {
                return;
            }

            // ① 落地即永久发狂。
            //    ★ 只有 spawn 之后 hediff 才会 tick（Pawn_HealthTracker.HealthTick），
            //      所以这里天然是"运输舱落地之后"的时机 —— 这正是为什么要用
            //      hediff 而不是在空投前直接设 mentalState（那时 pawn 还没 spawn，
            //      设了会被丢掉）。
            if (Props.makeManhunter && p.Spawned && p.mindState != null
                && p.mindState.mentalStateHandler != null
                && p.mindState.mentalStateHandler.CurStateDef != MentalStateDefOf.ManhunterPermanent)
            {
                p.mindState.mentalStateHandler.TryStartMentalState(
                    MentalStateDefOf.ManhunterPermanent, null, true);
            }

            // ② 把失血线性推到 1.0 → 到点必死（BloodLoss 的 lethalSeverity = 1）
            ticksAlive++;
            int total = Props.ticksToDeath > 0 ? Props.ticksToDeath : 3750;
            float target = (float)ticksAlive / (float)total;
            if (target > 1f)
            {
                target = 1f;
            }
            Hediff loss = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (loss == null)
            {
                loss = p.health.AddHediff(HediffDefOf.BloodLoss);
            }
            if (loss != null)
            {
                loss.Severity = target;
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look<int>(ref this.ticksAlive, "ticksAlive", 0, false);
        }
    }
}
