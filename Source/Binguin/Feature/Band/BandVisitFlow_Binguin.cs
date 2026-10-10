// ============================================================================
// 冰鹅族【乐队来访】—— 演唱会流程（代码批 2/2，2026-09）
//
// 用户定稿：
//   · 首次任务发放：第 60~120 天之间（>60 且 <120）；之后间隔 ≥60 天
//   · 接任务（选"接受"）→ 地图上落下地标 → 玩家按蓝图建造
//   · 建完 → 乐队 5 人 + 观众 10~25 人到达
//   · 殖民者与【主唱】交谈（头顶 ?）→ 开 4 小时演唱会
//   · 散场：原地留下 1~3 把武器；10% 概率改留稀有物品
//   · 所有在场殖民者 +12 心情 / 10 天；冰鹅族 +20 好感
//
// ★ 交互入口用 FloatMenuOptionProvider 子类 —— RimWorld 1.6 会自动发现
//   所有非抽象子类（GenTypes.AllSubclassesNonAbstract），**不需要注册**。
// ★ "任务"用 ChoiceLetter（接受/拒绝）实现，而不是正式 QuestScriptDef：
//   同样的"接/拒"体验，但不用写 QuestNode，风险低得多。日志/通知在信件里。
// ============================================================================

using System;
using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

using Binguin.Feature.Rods;
using Binguin.Helper;

namespace Binguin.Feature.Band
{
    public static class BinguinBandTuning
    {
        // ---- 任务节奏（用户定稿：首次 >60 且 <120 天） ----
        public const int FirstMinDays = 61;
        public const int FirstMaxDays = 119;
        public const int IntervalDays = 60;          // 之后至少间隔 60 天

        // ---- 观众 ----
        public const int AudienceMin = 10;
        public const int AudienceMax = 25;

        // ---- 散场掉落（用户定稿：1~3 把武器；10% 改留稀有物品） ----
        public const int LootWeaponMin = 1;
        public const int LootWeaponMax = 3;
        public const float RareChance = 0.10f;

        // 稀有物品池（缺 DLC 的会自动跳过）
        public static readonly string[] RareItems =
        {
            "MechSerumHealer",          // 治愈机械液（Royalty）
            "MechSerumResurrector",     // 复活机械液（Royalty）
            "TechprofSubpersonaCore",   // 科技核心（Royalty）
        };
        public const string RareMedicineDefName = "MedicineUltratech";   // 闪耀医药（Core）
        public const int RareMedicineCount = 10;
    }

    // ========================================================================
    // 1) 邀请信（接任务 / 拒绝）
    // ========================================================================
    public class ChoiceLetter_BinguinBandInvite : ChoiceLetter
    {
        private List<DiaOption> optionList = new List<DiaOption>();

        public ChoiceLetter_BinguinBandInvite()
        {
            BuildOptions();
        }

        public override IEnumerable<DiaOption> Choices
        {
            get { return optionList; }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                BuildOptions();
            }
        }

        private void BuildOptions()
        {
            optionList.Clear();

            DiaOption accept = new DiaOption("Binguin_BandInvite_Accept".Translate());
            accept.action = delegate
            {
                GameComponent_BinguinBandScheduler comp =
                    Current.Game.GetComponent<GameComponent_BinguinBandScheduler>();
                if (comp != null) comp.OnAccepted();
            };
            optionList.Add(accept);

            DiaOption refuse = new DiaOption("Binguin_BandInvite_Refuse".Translate());
            refuse.action = delegate
            {
                GameComponent_BinguinBandScheduler comp =
                    Current.Game.GetComponent<GameComponent_BinguinBandScheduler>();
                if (comp != null) comp.OnRefused();
                Find.LetterStack.RemoveLetter(this);
            };
            optionList.Add(refuse);
        }
    }

    // ========================================================================
    // 2) 调度组件：发邀请 → 接任务后落标 → 建完后交给演出流程
    // ========================================================================
    public class GameComponent_BinguinBandScheduler : GameComponent
    {
        public int nextInviteTick = -1;   // 下一次发邀请的 tick
        public int lastVisitTick = -1000000;
        public bool invitePending;        // 已发信、等玩家答复
        public bool markerPlaced;         // 已落标
        public bool concertFinished;      // 本轮已结束

        public GameComponent_BinguinBandScheduler(Game game) : base()
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref nextInviteTick, "binguinBandNextInviteTick", -1, false);
            Scribe_Values.Look<int>(ref lastVisitTick, "binguinBandLastVisitTick", -1000000, false);
            Scribe_Values.Look<bool>(ref invitePending, "binguinBandInvitePending", false, false);
            Scribe_Values.Look<bool>(ref markerPlaced, "binguinBandMarkerPlaced", false, false);
            Scribe_Values.Look<bool>(ref concertFinished, "binguinBandConcertFinished", false, false);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager == null) return;
            if (Find.TickManager.TicksGame % 250 != 0) return;
            if (Current.Game == null) return;

            int now = Find.TickManager.TicksGame;

            // 首次排期：第 60~120 天之间
            if (nextInviteTick < 0)
            {
                int days = Rand.Range(BinguinBandTuning.FirstMinDays, BinguinBandTuning.FirstMaxDays);
                nextInviteTick = days * GenDate.TicksPerDay;
                BinguinLogUtility.Log("乐队来访已排期：第 " + days + " 天。");
                return;
            }

            if (invitePending || markerPlaced) return;
            if (now < nextInviteTick) return;

            SendInvite();
        }

        private void SendInvite()
        {
            try
            {
                Map map = Find.AnyPlayerHomeMap;
                if (map == null) return;
                invitePending = true;
                // ★ 走正式【任务条目】（用户定稿方案 B：C# 直接构造 Quest）
                BinguinBandQuest.Offer();
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("发送乐队邀请失败：" + e, severity: 2, isDebug: false);
                invitePending = false;
                nextInviteTick = Find.TickManager.TicksGame + 5 * GenDate.TicksPerDay;
            }
        }

        public void OnAccepted()
        {
            invitePending = false;
            Map map = Find.AnyPlayerHomeMap;
            if (map == null) return;
            MonumentMarker m = BinguinBandVisit.SpawnMarker(map);
            if (m == null)
            {
                Messages.Message("Binguin_Band_NoVenue".Translate(),
                    MessageTypeDefOf.NeutralEvent);
                Retire();
                return;
            }
            markerPlaced = true;
            Messages.Message("Binguin_Band_MarkerSent".Translate(),
                MessageTypeDefOf.PositiveEvent);
        }

        public void OnRefused()
        {
            invitePending = false;
            Retire();
            Messages.Message("Binguin_Band_Declined".Translate(), MessageTypeDefOf.NeutralEvent);
        }

        /// <summary>本轮结束：排下一次（间隔 ≥60 天）。</summary>
        public void Retire()
        {
            lastVisitTick = Find.TickManager.TicksGame;
            nextInviteTick = lastVisitTick + BinguinBandTuning.IntervalDays * GenDate.TicksPerDay;
            markerPlaced = false;
            concertFinished = false;
        }
    }

    // ========================================================================
    // 3) 与主唱交谈 → 开演唱会（右键菜单，自动被发现）
    // ========================================================================
    public class FloatMenuOptionProvider_BinguinBand : FloatMenuOptionProvider
    {
        protected override bool Drafted { get { return true; } }
        protected override bool Undrafted { get { return true; } }
        protected override bool Multiselect { get { return true; } }

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            if (clickedPawn == null) return null;
            if (!BinguinBandConcert.IsLeadSinger(clickedPawn)) return null;
            if (BinguinConcert.Active) return null;

            Pawn actor = context.FirstSelectedPawn;
            if (actor == null || actor.Faction != Faction.OfPlayer) return null;

            FloatMenuOption opt = new FloatMenuOption(
                "Binguin_Band_TalkToSinger".Translate(), delegate
                {
                    BinguinBandConcert.StartConcert(clickedPawn);
                });
            return opt;
        }
    }

    // ========================================================================
    // 4) 演出：生成乐队 + 观众；散场结算
    // ========================================================================
    public static class BinguinBandConcert
    {
        /// <summary>当前这场的乐队与观众（存档）</summary>
        public static List<Pawn> performers = new List<Pawn>();
        public static List<Pawn> audience = new List<Pawn>();
        public static IntVec3 venueCenter = IntVec3.Invalid;
        public static bool bandArrived;

        public static bool IsLeadSinger(Pawn p)
        {
            // ★★★ 2026-09-23 修复「主唱头顶有问号但不能互动」：
            //   小人身上 pawn.def 是【种族】ThingDef（Binguin），不是 PawnKindDef！
            //   乐队成员是 Binguin 种族 + Binguin_BandMember 职业，所以
            //   p.def.defName == "Binguin_BandMember" 永远为 false →
            //   右键菜单提供者拿不到选项 → 只有问号（问号走 LeadSinger，不查这里）。
            //   必须查 kindDef.defName（本工程其它地方都是这么写的）。
            if (p == null || p.kindDef == null) return false;
            return p.kindDef.defName == "Binguin_BandMember" && performers.Count > 0 && performers[0] == p;
        }

        /// <summary>主唱（乐队第一个成员）。</summary>
        public static Pawn LeadSinger
        {
            get
            {
                if (performers == null || performers.Count == 0) return null;
                Pawn p = performers[0];
                return (p != null && !p.Destroyed) ? p : null;
            }
        }

        /// <summary>
        /// 麦克风【操作点】所在的格子（主唱开演前站这儿等，头顶问号）。
        /// 每次现查地图上的麦克风，不缓存 —— 读档后静态字段会丢。
        /// </summary>
        public static IntVec3 FindMicCell(Map map)
        {
            try
            {
                if (map == null) return IntVec3.Invalid;
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_Band_Microphone");
                if (def == null) return IntVec3.Invalid;
                List<Thing> list = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < list.Count; i++)
                {
                    Thing t = list[i];
                    if (t == null || t.Destroyed || !t.Spawned) continue;
                    if (t.def != null && t.def.hasInteractionCell)
                    {
                        IntVec3 c = t.InteractionCell;
                        if (c.IsValid && c.InBounds(map) && c.Standable(map)) return c;
                    }
                    return t.Position;
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("找麦克风操作点失败：" + e.Message, severity: 1, isDebug: false);
            }
            return IntVec3.Invalid;
        }

        // ★★★ 2026-09 用户要求：开演前主唱头顶显示"问号"（原来没有这个提示）。
        //   原版的问号是 RimWorld.OverlayDrawer.RenderQuestionMarkOverlay(Thing)，
        //   但它是 private 调不到，所以这里照抄它的画法（IL 实证）：
        //     pos = thing.DrawPos；y = 覆盖层高度；pawn 还要 x += size.x - 0.52、z += size.z - 0.45
        //     然后用 MeshPool.plane05 + UI/Overlays/QuestionMark 材质画一个 mesh。
        private static Material questionMarkMat;

        public static void DrawQuestionMark(Pawn p)
        {
            try
            {
                if (p == null || !p.Spawned || p.Map == null) return;
                if (questionMarkMat == null)
                {
                    questionMarkMat = MaterialPool.MatFrom("UI/Overlays/QuestionMark",
                        ShaderDatabase.MetaOverlay);
                }
                if (questionMarkMat == null) return;
                Vector3 pos = p.DrawPos;
                pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                if (p.def != null)
                {
                    pos.x += p.def.size.x - 0.52f;
                    pos.z += p.def.size.z - 0.45f;
                }
                Graphics.DrawMesh(MeshPool.plane05, pos, Quaternion.identity, questionMarkMat, 0);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>场地建完后，乐队与观众【从地图边缘入场】，然后到场地附近闲逛等开演。</summary>
        /// <remarks>
        /// ★★★ 2026-09 用户实测修复（"直接刷出来就直接离开了"）：
        ///   根因在【原版 Humanlike 思维树】的兜底分支——
        ///     Data/Core/Defs/ThinkTreeDefs/Humanlike.xml 第 453 行：
        ///       &lt;!-- Final backup: If you're just here for no apparent reason, and not a
        ///             colonist, leave the map --&gt;
        ///       ThinkNode_ConditionalColonist(invert) → JobGiver_ExitMapBest
        ///   我们原来只是 GenSpawn 出来、不给任何 lord / 职责 → 它们身上没有 job，
        ///   思维树每 tick 走一遍，直接命中这条兜底 → 当场往地图边缘走、走掉。
        ///   修法：给每个派系建一个 Lord（职责树），它们就一直在"守着场地"，
        ///   永远不会落到兜底分支；同时 LordJob 的初始 toil 是 LordJob_Travel，
        ///   会自动把它们从边缘【走】到场地，到了以后由 Defend 职责的
        ///   JobGiver_WanderNearDutyLocation 在场地附近来回溜达等开演。
        /// </remarks>
        public static void Arrive(Map map, MonumentMarker marker)
        {
            if (map == null || bandArrived) return;
            try
            {
                venueCenter = marker.Position;
                performers.Clear();
                audience.Clear();

                Faction fac = FindBinguinFaction();
                PawnKindDef bandKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Binguin_BandMember");
                if (bandKind == null)
                {
                    BinguinLogUtility.Log("找不到 Binguin_BandMember，乐队未生成。", severity: 1, isDebug: false);
                    return;
                }

                // ---- ★ 入场点：地图边缘（用户要求"从边缘入场"） ----
                IntVec3 entry = FindEntryCell(map);

                // ---- 乐队 5 人 ----
                for (int i = 0; i < 5; i++)
                {
                    Pawn p = SpawnOne(bandKind, fac, map, entry);
                    if (p != null) performers.Add(p);
                }

                // ---- 观众 10~25 人（与玩家和冰鹅族都不敌对） ----
                // ★ 已选中的派系之间也要求互不敌对：现在观众有 Lord + Defend 职责，
                //   两拨互相敌对的观众会在演唱会上打起来（原来没 Lord 时只会各自离场）。
                List<Faction> chosen = new List<Faction>();
                int want = Rand.RangeInclusive(BinguinBandTuning.AudienceMin, BinguinBandTuning.AudienceMax);
                int tries = 0;
                while (audience.Count < want && tries < want * 6)
                {
                    tries++;
                    Faction f = RandomFriendlyFaction(fac, chosen);
                    if (f == null) break;
                    if (!chosen.Contains(f)) chosen.Add(f);
                    PawnKindDef kind = f.def != null ? f.def.basicMemberKind : null;
                    if (kind == null || kind.race == null || !kind.race.race.Humanlike) continue;
                    Pawn p = SpawnOne(kind, f, map, entry);
                    if (p != null) audience.Add(p);
                }

                // ---- ★ 闲逛点：场地中心附近一个能站的格子 ----
                IntVec3 chill = FindChillSpot(map, venueCenter);
                MakeCrowdLords(map, performers, chill);
                MakeCrowdLords(map, audience, chill);

                bandArrived = true;
                Messages.Message("Binguin_Band_Arrived".Translate(audience.Count).ToString(),
                    MessageTypeDefOf.PositiveEvent);
                BinguinLogUtility.Log("乐队 5 人 + 观众 " + audience.Count + " 人已从边缘入场（闲逛点 " + chill + "）。");
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("乐队到场失败：" + e, severity: 2, isDebug: false);
            }
        }

        /// <summary>入场点：地图边缘（优先道路）。</summary>
        private static IntVec3 FindEntryCell(Map map)
        {
            try
            {
                IntVec3 cell;
                if (RCellFinder.TryFindRandomPawnEntryCell(out cell, map, 0.3f, false, null))
                {
                    return cell;
                }
            }
            catch (Exception)
            {
            }
            try
            {
                // 兜底：随便找个能站的边缘格
                IntVec3 c2;
                if (CellFinder.TryFindRandomEdgeCellWith(x => x.Walkable(map) && !x.Fogged(map),
                        map, CellFinder.EdgeRoadChance_Ignore, out c2))
                {
                    return c2;
                }
            }
            catch (Exception)
            {
            }
            BinguinLogUtility.Log("找不到地图边缘入场点，退回地图中心生成。", severity: 1, isDebug: false);
            return map.Center;
        }

        /// <summary>场地中心附近一个能站、能走到的闲逛点。</summary>
        private static IntVec3 FindChillSpot(Map map, IntVec3 center)
        {
            try
            {
                IntVec3 c;
                if (CellFinder.TryFindRandomCellNear(center, map, 10,
                        x => x.Standable(map) && !x.Fogged(map), out c))
                {
                    return c;
                }
            }
            catch (Exception)
            {
            }
            return center;
        }

        /// <summary>
        /// 按派系分组建 Lord（一个 Lord 只能有一个派系）。
        /// ★ 建失败也不影响：只是那几个小人会退回原版行为（可能自己离场）。
        /// </summary>
        private static void MakeCrowdLords(Map map, List<Pawn> pawns, IntVec3 chill)
        {
            if (map == null || pawns == null || pawns.Count == 0) return;
            try
            {
                Dictionary<Faction, List<Pawn>> groups = new Dictionary<Faction, List<Pawn>>();
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p == null || p.Dead || !p.Spawned || p.Map != map) continue;
                    if (p.Faction == null) continue;
                    if (LordUtility.GetLord(p) != null) continue;   // 已经在别的 lord 里
                    List<Pawn> list;
                    if (!groups.TryGetValue(p.Faction, out list))
                    {
                        list = new List<Pawn>();
                        groups.Add(p.Faction, list);
                    }
                    list.Add(p);
                }
                foreach (KeyValuePair<Faction, List<Pawn>> kv in groups)
                {
                    try
                    {
                        LordMaker.MakeNewLord(kv.Key, new LordJob_BinguinCrowd(chill), map, kv.Value);
                    }
                    catch (Exception e)
                    {
                        BinguinLogUtility.Log("给 " + kv.Key.Name + " 的观众建 Lord 失败："
                            + e.Message + "Binguin_Band_MayLeave".Translate(), severity: 1, isDebug: false);
                    }
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("建立观众 Lord 失败：" + e, severity: 2, isDebug: false);
            }
        }

        private static Pawn SpawnOne(PawnKindDef kind, Faction fac, Map map, IntVec3 around)
        {
            try
            {
                IntVec3 cell;
                if (!CellFinder.TryFindRandomCellNear(around, map, 12,
                        c => c.Standable(map) && !c.Fogged(map), out cell))
                {
                    if (!CellFinder.TryFindRandomCellNear(around, map, 30,
                            c => c.Standable(map), out cell))
                    {
                        cell = around;
                    }
                }
                Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, fac));
                FixBinguinBodyType(p);
                GenSpawn.Spawn(p, cell, map, Rot4.Random);
                return p;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("生成一个观众/乐手失败：" + e.Message, severity: 1, isDebug: false);
                return null;
            }
        }

        /// <summary>
        /// ★ 2026-09 修（Player.log 实证）：
        ///   Failed to find any textures at .../BandOutfit_Hulk 和 .../BandOutfit_Fat
        ///   —— 冰鹅族的 RaceDef 只允许 Female / Thin 两种成人体型
        ///   （Defs/Race_Binguin.xml 的 &lt;bodyTypes&gt;，贴图也只画了这两种），
        ///   但 PawnGenerator 偶尔还是会给出 Fat / Hulk（HAR 的 CheckBodyType 没兜住），
        ///   于是乐队服找不到 Fat/Hulk 贴图 → 红字 + 花屏。
        ///   这里在【生成后、落地前】把冰鹅族的体型纠正回允许的两种
        ///   （女性 → Female，男性 → Thin，与 RaceDef 的 default 值一致）。
        ///   其它种族的观众不受影响。
        /// </summary>
        private static void FixBinguinBodyType(Pawn p)
        {
            try
            {
                if (p == null || p.story == null) return;
                // ★ 2026-09-26 种族判定收口：见 BinguinRaceUtility
                if (!BinguinRaceUtility.IsBinguin(p)) return;
                BodyTypeDef bt = p.story.bodyType;
                if (bt != null && (bt.defName == "Female" || bt.defName == "Thin")) return;
                p.story.bodyType = (p.gender == Gender.Male) ? BodyTypeDefOf.Thin : BodyTypeDefOf.Female;
                BinguinLogUtility.Log("纠正了一个乐队成员的体型："
                    + (bt == null ? "(空)" : bt.defName) + " → " + p.story.bodyType.defName);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("纠正体型失败：" + e.Message, severity: 1, isDebug: false);
            }
        }

        private static Faction FindBinguinFaction()
        {
            foreach (Faction f in Find.FactionManager.AllFactions)
            {
                if (f != null && f.def != null && f.def.defName == BinguinFactions.Peaceful) return f;
            }
            return null;
        }

        /// <summary>
        /// 与玩家、冰鹅族、以及【已经选中的观众派系】都不敌对的派系（随机一个）。
        /// ★ 后面这条是 2026-09 加的：观众现在带 Lord + Defend 职责，
        ///   互相敌对的观众会在演唱会现场开打。
        /// </summary>
        private static Faction RandomFriendlyFaction(Faction binguin, List<Faction> chosen)
        {
            Faction player = Faction.OfPlayer;
            List<Faction> ok = new List<Faction>();
            foreach (Faction f in Find.FactionManager.AllFactions)
            {
                if (f == null || f.def == null || f.IsPlayer) continue;
                if (!f.def.humanlikeFaction) continue;
                if (f.HostileTo(player)) continue;
                if (binguin != null && f.HostileTo(binguin)) continue;
                bool clash = false;
                if (chosen != null)
                {
                    for (int i = 0; i < chosen.Count; i++)
                    {
                        if (chosen[i] != null && chosen[i] != f && f.HostileTo(chosen[i]))
                        {
                            clash = true;
                            break;
                        }
                    }
                }
                if (clash) continue;
                ok.Add(f);
            }
            if (ok.Count == 0) return null;
            return ok[Rand.Range(0, ok.Count)];
        }

        /// <summary>开演唱会（由交互触发）。</summary>
        public static void StartConcert(Pawn singer)
        {
            if (BinguinConcert.Active) return;
            BinguinConcert.Start(BinguinConcert.DurationTicks);
            Messages.Message("Binguin_Band_ConcertStart".Translate(),
                MessageTypeDefOf.PositiveEvent);
            // ★ 2026-10-06 用户需求：live house 门票收入
            //   （在"开演瞬间"结算，而不是散场 —— 观众已经到场坐好了，
            //    票房当场入袋；散场再算的话中途读档/乐队被清掉就可能漏结算）
            try
            {
                BinguinBandTicket.SettleAtStart(singer != null ? singer.Map : Find.AnyPlayerHomeMap);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("门票结算失败：" + e, severity: 2, isDebug: false);
            }
        }

        /// <summary>演唱会结束：结算心情/好感/掉落，然后所有人离场。</summary>
        public static void EndConcert(Map map)
        {
            try
            {
                // ---- 1) 在场殖民者 +12 心情 / 10 天 ----
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Binguin_AttendedConcert");
                int n = 0;
                if (thought != null && map != null)
                {
                    var colonists = map.mapPawns.FreeColonistsSpawned;
                    for (int i = 0; i < colonists.Count; i++)
                    {
                        Pawn c = colonists[i];
                        if (c.needs == null || c.needs.mood == null) continue;
                        c.needs.mood.thoughts.memories.TryGainMemory(thought, null);
                        n++;
                    }
                }

                // ---- 2) 原地留下 1~3 把武器；10% 改留稀有物品 ----
                string lootDesc = DropLoot(map, venueCenter);

                // ---- 3) 冰鹅族 +20 好感 ----
                Faction fac = FindBinguinFaction();
                Faction player = Faction.OfPlayer;
                if (fac != null && player != null)
                {
                    fac.TryAffectGoodwillWith(player, 20);
                }

                // ---- 4) 全员离场 ----
                SendEveryoneAway(map);

                bandArrived = false;
                performers.Clear();
                audience.Clear();

                Messages.Message("Binguin_Band_ConcertEnd".Translate()
                    + (n > 0 ? "Binguin_Band_ColonistsAttended".Translate(n).ToString() : "") + lootDesc,
                    MessageTypeDefOf.PositiveEvent);
                BinguinLogUtility.Log("演唱会结束：听众 " + n + " 人，掉落 " + lootDesc);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("演唱会结算失败：" + e, severity: 2, isDebug: false);
            }
        }

        /// <summary>散场掉落。返回一句描述。</summary>
        private static string DropLoot(Map map, IntVec3 center)
        {
            if (map == null || !center.IsValid) return "";
            bool rare = Rand.Chance(BinguinBandTuning.RareChance);
            if (rare)
            {
                Thing t = MakeRareItem();
                if (t != null)
                {
                    DropNear(t, map, center);
                    return "Binguin_Band_LeftGift".Translate(t.LabelShort).ToString();
                }
            }
            int count = Rand.RangeInclusive(BinguinBandTuning.LootWeaponMin, BinguinBandTuning.LootWeaponMax);
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                Thing w = MakeRandomWeapon();
                if (w == null) continue;
                DropNear(w, map, center);
                made++;
            }
            return made > 0 ? "Binguin_Band_LeftGift2".Translate(made).ToString() : "";
        }

        private static Thing MakeRareItem()
        {
            // 先试三件 Royalty 稀有品
            List<string> pool = new List<string>(BinguinBandTuning.RareItems);
            pool.Add(BinguinBandTuning.RareMedicineDefName);
            pool.Shuffle();
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == BinguinBandTuning.RareMedicineDefName)
                {
                    ThingDef med = DefDatabase<ThingDef>.GetNamedSilentFail(BinguinBandTuning.RareMedicineDefName);
                    if (med != null)
                    {
                        Thing t = ThingMaker.MakeThing(med, null);
                        t.stackCount = BinguinBandTuning.RareMedicineCount;
                        return t;
                    }
                }
                else
                {
                    ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(pool[i]);
                    if (d != null) return ThingMaker.MakeThing(d, null);
                }
            }
            return null;
        }

        private static Thing MakeRandomWeapon()
        {
            List<ThingDef> pool = new List<ThingDef>();
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
            {
                if (d.IsWeapon && !d.MadeFromStuff && d.techLevel != TechLevel.Undefined
                    && d.techLevel <= TechLevel.Spacer && d.BaseMarketValue > 100f)
                {
                    pool.Add(d);
                }
                else if (d.IsWeapon && d.MadeFromStuff && d.techLevel != TechLevel.Undefined
                    && d.techLevel <= TechLevel.Spacer)
                {
                    pool.Add(d);
                }
            }
            if (pool.Count == 0) return null;
            ThingDef def = pool[Rand.Range(0, pool.Count)];
            ThingDef stuff = null;
            if (def.MadeFromStuff && def.stuffCategories != null && def.stuffCategories.Count > 0)
            {
                // 随便挑一种能用得上的材料
                foreach (ThingDef m in DefDatabase<ThingDef>.AllDefs)
                {
                    if (m.stuffProps == null) continue;
                    if (m.stuffProps.categories == null) continue;
                    for (int i = 0; i < m.stuffProps.categories.Count; i++)
                    {
                        if (def.stuffCategories.Contains(m.stuffProps.categories[i])) { stuff = m; break; }
                    }
                    if (stuff != null) break;
                }
            }
            Thing w = ThingMaker.MakeThing(def, stuff);
            // 带品质
            CompQuality q = w.TryGetComp<CompQuality>();
            if (q != null)
            {
                q.SetQuality(QualityUtility.GenerateQualityRandomEqualChance(), ArtGenerationContext.Outsider);
            }
            return w;
        }

        private static void DropNear(Thing t, Map map, IntVec3 center)
        {
            IntVec3 cell;
            if (!CellFinder.TryFindRandomCellNear(center, map, 8, c => c.Standable(map), out cell))
            {
                cell = center;
            }
            GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near);
        }

        /// <summary>
        /// 散场：让乐队和观众【走出地图离开】。
        /// ★★★ 2026-09 改动（配合"从边缘入场"）：他们现在都是 Lord 成员，
        ///   光派一个 Goto 任务没用——Lord 的职责树会在任务结束后立刻把 job 顶掉。
        ///   正确做法是【解散 Lord】：LordManager.RemoveLord → Lord.Cleanup →
        ///   Lord.RemovePawn 会把每个小人的 mindState.duty 清成 null、解除 pawn.lord，
        ///   于是原版 Humanlike 思维树的兜底分支（JobGiver_ExitMapBest，
        ///   exitMapOnArrival = true）会自己把它们送到地图边缘并消失。
        ///   没有 Lord 的（建 Lord 失败的）再退回手动送边缘，双保险。
        /// </summary>
        private static void SendEveryoneAway(Map map)
        {
            if (map == null) return;
            Faction player = Faction.OfPlayer;

            List<Pawn> all = new List<Pawn>();
            for (int i = 0; i < performers.Count; i++)
            {
                if (performers[i] != null) all.Add(performers[i]);
            }
            for (int i = 0; i < audience.Count; i++)
            {
                if (audience[i] != null) all.Add(audience[i]);
            }

            // 1) 先停掉手上的活（含"看演出"的 job），免得他们端着 job 不走
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p == null || p.Dead || !p.Spawned || p.Map != map) continue;
                if (player != null && p.Faction == player) continue;
                try { p.jobs.StopAll(); }
                catch (Exception) { }
            }

            // 2) 解散他们所属的 Lord（清掉职责 → 原版思维树接手送他们离场）
            List<Lord> lords = new List<Lord>();
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p == null || p.Dead || !p.Spawned || p.Map != map) continue;
                try
                {
                    Lord l = LordUtility.GetLord(p);
                    if (l != null && !lords.Contains(l)) lords.Add(l);
                }
                catch (Exception)
                {
                }
            }
            for (int i = 0; i < lords.Count; i++)
            {
                try { map.lordManager.RemoveLord(lords[i]); }
                catch (Exception e) { BinguinLogUtility.Log("解散观众队伍失败：" + e.Message, severity: 1, isDebug: false); }
            }

            // 3) 兜底：仍然没有 Lord 的（或上面失败的）手动送回地图边缘
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p == null || p.Dead || !p.Spawned || p.Map != map) continue;
                if (player != null && p.Faction == player) continue;
                try
                {
                    if (LordUtility.GetLord(p) != null) continue;
                    IntVec3 exit;
                    if (RCellFinder.TryFindBestExitSpot(p, out exit))
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.Goto, exit);
                        job.exitMapOnArrival = true;    // 走到边缘就地消失（原版同款）
                        p.jobs.StartJob(job, JobCondition.InterruptForced);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }

    // ========================================================================
    // 4b) 观众/乐队的"守场"Lord —— 从边缘走进来 + 在场地附近闲逛等开演
    // ========================================================================
    /// <summary>
    /// 只会【走到场地、然后在附近闲逛】的 LordJob（没有任何离场转换）。
    ///
    /// ★★ 为什么必须有它（用户实测"直接刷出来就直接离开"的根因）：
    ///   原版 Humanlike 思维树最后有一条兜底分支
    ///   （Data/Core/Defs/ThinkTreeDefs/Humanlike.xml : 453
    ///    "Final backup: If you're just here for no apparent reason, and not a
    ///     colonist, leave the map" → ThinkNode_ConditionalColonist(invert)
    ///     → JobGiver_ExitMapBest）。没有 lord / 没有职责的小人身上一空下来
    ///   就会命中它，于是当场往地图边缘走掉。给了 Lord 之后走的是职责树
    ///   （DutyDefOf.Defend → JobGiver_WanderNearDutyLocation），就再也不会命中兜底。
    ///
    /// ★ 为什么用 LordJob_VisitColony 不够：
    ///   它内部固定构造 LordToil_DefendPoint(chillSpot, null, null)（防守半径 28 格，
    ///   见 IL：两个 initobj Nullable&lt;float&gt;），对"围在舞台边上看演出"太散了；
    ///   而且它自带 8000~22000 tick 后自动离场 + "访客离开"消息 + 送礼检查。
    ///   这里自己写一个最小版：只有"走到场地 + 附近闲逛"，离场完全由我们控制
    ///   （EndConcert → 解散 Lord）。
    ///
    /// ★ 初始 toil 是 LordJob_Travel(chillSpot)：小人会自己从地图边缘【走】到场地，
    ///   到了以后 Trigger_Memo("TravelArrived") 切到 LordToil_DefendPoint。
    /// </summary>
    public class LordJob_BinguinCrowd : LordJob
    {
        /// <summary>防守/闲逛的中心（演唱会场地）。</summary>
        private IntVec3 chillSpot;

        public LordJob_BinguinCrowd()
        {
        }

        public LordJob_BinguinCrowd(IntVec3 chillSpot)
        {
            this.chillSpot = chillSpot;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();

            // ① 从（地图边缘）走到场地。AttachSubgraph 返回的是主图本身，
            //    之后再取 StartingToil 拿到的就是子图的起始 toil（LordJob_Travel 的）。
            LordToil travelToil = graph.AttachSubgraph(
                new LordJob_Travel(chillSpot).CreateGraph()).StartingToil;
            graph.StartingToil = travelToil;

            // ② 到了以后：守在场地附近 + 在半径内溜达
            //    （Defend 职责树的最后一条就是 JobGiver_WanderNearDutyLocation）
            LordToil_DefendPoint toil = new LordToil_DefendPoint(
                chillSpot, new float?(12f), new float?(10f));
            graph.AddToil(toil);

            // ③ 到达后切换（和原版 LordJob_VisitColony 一模一样：
            //    Trigger_Memo("TravelArrived")，由 LordToil_Travel 在人到齐时发出）
            Transition arrive = new Transition(travelToil, toil, false, true);
            arrive.AddTrigger(new Trigger_Memo("TravelArrived"));
            graph.AddTransition(arrive, false);

            return graph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<IntVec3>(ref chillSpot, "binguinCrowdChillSpot");
        }
    }

    // ========================================================================
    // 5) 演唱会到点自动结算
    // ========================================================================
    public class GameComponent_BinguinBandConcertEnd : GameComponent
    {
        private bool wasActive;

        public GameComponent_BinguinBandConcertEnd(Game game) : base()
        {
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager == null) return;
            if (Find.TickManager.TicksGame % 60 != 0) return;
            bool now = BinguinConcert.Active;
            if (wasActive && !now)
            {
                wasActive = false;
                BinguinBandConcert.EndConcert(Find.AnyPlayerHomeMap);
            }
            else if (now)
            {
                wasActive = true;
            }
        }
    }

    // ========================================================================
    // 6) 调试命令
    // ========================================================================
    public static class DebugActions_BandFlow
    {
        [DebugAction("冰鹅族", "乐队来访：立刻发邀请信")]
        public static void InviteNow()
        {
            GameComponent_BinguinBandScheduler c =
                Current.Game.GetComponent<GameComponent_BinguinBandScheduler>();
            if (c == null) return;
            c.nextInviteTick = Find.TickManager.TicksGame;
            c.invitePending = false;
        }

        [DebugAction("冰鹅族", "乐队来访：让乐队和观众到场")]
        public static void ArriveNow()
        {
            MonumentMarker m = BinguinBandVisit.FindMarker(Find.CurrentMap);
            if (m == null)
            {
                Messages.Message("地图上没有演唱会地标。", MessageTypeDefOf.RejectInput);
                return;
            }
            BinguinBandConcert.Arrive(Find.CurrentMap, m);
        }

        [DebugAction("冰鹅族", "乐队来访：立刻结算散场")]
        public static void EndNow()
        {
            BinguinBandConcert.EndConcert(Find.CurrentMap);
        }

        /// <summary>
        /// ★ 2026-10-06：门票调试命令。
        /// 不用真等一整场演出 —— 直接按当前场上观众数 + 场地评分结算一次票房，
        /// 并把银两掉在场地上。用来快速验收数值与场地系数。
        /// </summary>
        [DebugAction("冰鹅族", "乐队来访：结算门票（按当前观众）")]
        public static void TicketNow()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("没有当前地图。", MessageTypeDefOf.RejectInput, false);
                return;
            }
            // 没观众时提示一下，免得以为是坏了
            int before = 0;
            if (BinguinBandConcert.audience != null)
            {
                for (int i = 0; i < BinguinBandConcert.audience.Count; i++)
                {
                    Pawn p = BinguinBandConcert.audience[i];
                    if (p != null && !p.Dead && p.Spawned) before++;
                }
            }
            int revenue = BinguinBandTicket.SettleAtStart(map);
            BinguinLogUtility.Log("门票调试：到场观众 " + before + " 人，票房 " + revenue
                + " 银（场地系数 " + BinguinBandTicket.LastVenueFactor.ToString("0.00") + "）");
        }

        /// <summary>★ 2026-10-06：只报场地评分，不掉钱 —— 用来调场地数值。</summary>
        [DebugAction("冰鹅族", "乐队来访：查看场地系数")]
        public static void VenueFactorNow()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            IntVec3 center = BinguinBandConcert.venueCenter;
            if (!center.IsValid)
            {
                Messages.Message("还没有场地（找主唱开演后才有 venueCenter）。",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            float f = BinguinBandTicket.VenueFactor(map, center);
            Messages.Message("场地系数 = " + f.ToString("0.00")
                + "（基准容量 " + BinguinBandTicket.VenueBaselineCells + " 格，"
                + "半径 " + BinguinBandTicket.VenueRadius + " 格）",
                MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
