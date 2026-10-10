// ============================================================================
// 冰鹅族外交事件（RimWorld 1.6.4871 / Humanoid Alien Races 2.0 前置）
//
// 功能：游戏开始 3 天内，向玩家殖民地边缘派遣一名冰鹅族外交官
//       （到达只发通知信，不再自动弹选择信）。
//       玩家选中殖民者 -> 右键点击外交官 -> 殖民者走到她面前 -> 弹出
//       共存意向选择信（2026-08-22 用户需求：点击外交官才进入事件）：
//         愿意共存 -> 与冰鹅派系好感 +50（外交官逗留一天后离开）
//         保持中立 -> 好感不变（稍作停留后离开）
//         无意共存 -> 好感 -75，且等到外交官走出地图边缘时才结算（离场时触发）
//
// ★ 已按 1.6 真实 API（反射自游戏程序集）重写：
//   - 1.6 的 ChoiceLetter 为抽象类，Choices 改为 IEnumerable<DiaOption>
//     （旧的 choices/Choice 列表与 LetterMaker 已不存在）
//   - 信件按钮与组件之间通过 Current.Game.GetComponent<T>() 通信
//   - GameComponent 必须提供 public ctor(Game)（Activator.CreateInstance(type, game) 调用）
//   - 离场方式：答复后外交官沿路走回地图边缘再消失（JobDefOf.Goto），
//     而非"停留结束原地消失"；玩家不答复则一直等待
//   - 保持 C# 5 语法兼容（无字符串插值等新特性），csc.exe 即可编译
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

using Binguin.Feature.Band;
using Binguin.Feature.GateShield;
using Binguin.Feature.IceCombat;
using Binguin.Feature.Raids;
using Binguin.Feature.Rods;
using Binguin.Feature.Sliding;
using Binguin.Feature.WillingCannon;
using Binguin.Helper;

namespace Binguin.Feature.Diplomacy
{
    public class GameComponent_BinguinDiplomacy : GameComponent
    {
        // 游戏开始后多少 tick 触发（1 天 = 60000 tick）
        private const int DelayTicks = 3 * 60000;

        // 外交官在得到答复后逗留的时间
        private const int StayTicksIfAccepted = 60000;  // 愿意共存：逗留一天
        private const int StayTicksIfNeutral = 12000;   // 保持中立：稍作停留
        // 无意共存：外交官立刻走向地图边缘离开；此值只是"走不出地图"时的
        // 兜底上限（3 天），正常到边缘即离场并结算 -75（见 DepartDiplomat）
        private const int RefusalMaxLeaveTicks = 180000;

        // 答复选项：1=愿意共存 2=保持中立 3=无意共存（0=尚未答复）
        public const int RespAccepted = 1;
        public const int RespNeutral = 2;
        public const int RespRefused = 3;

        private const string FactionDefName = "Binguin";
        private const string DiplomatKindDefName = "Binguin_Diplomat";
        private const string DiplomacyLetterDefName = "BinguinDiplomacyLetter";
        private const string ArrivalLetterDefName = "BinguinDiplomatArrivalLetter";
        private const string LeaveLetterDefName = "BinguinLeaveLetter";

        private bool initialized;
        private int startTick = -1;
        private bool fired;
        // ★ 冰鹅剧本开局盟友（2026-08-20 用户需求）：用冰鹅剧本开局时，
        //   玩家派系（Binguin_PlayerColony）与世界中 NPC 冰鹅派系（Binguin）
        //   直接是 100 好感盟友，无需三天后外交官询问共存。
        private bool allyApplied;

        // ★ 霜语者冷笑话（2026-08-21 用户需求）：当前叙述者为「霜语者」时，
        //   每 10 天（600000 tick）随机讲一条冷笑话（356 条库，避免紧邻重复）。
        private const int JokeIntervalTicks = 600000;   // 10 天
        private const string FrostSpeakerStorytellerDefName = "Binguin_StorytellerFrostSpeaker";
        private int nextJokeTick = -1;   // <0 = 尚未初始化（进游戏后首条 10 天到）
        private int lastJokeIndex = -1;  // 上一条下标（防紧邻重复）

        // ★ v18：寒门盾场读档后补发标志（读档/开局后首个 tick 执行一次
        //   兜底扫描，之后每 60000 tick 一次——正常穿戴已由事件驱动）
        private bool gateScanPending = true;

        // ★ v19：威灵注册表（当前穿在身上的威灵 comp，自动装填轮询只
        //   遍历它——穿上/脱下经 Notify_Equipped/Unequipped 登记注销）
        private readonly List<CompBinguinWillingCannon> willingRegistry = new List<CompBinguinWillingCannon>();
        private bool willingRebuildPending = true;   // 读档/开局后需全扫重建

        // ★ v23 冰鹅族飞天决战（2026-08-22 用户设计文档）：
        //   母舰联系状态机：0=未联系 1=联系中 2=已链接
        //   联系 13 天（780000 tick）；期间 15 波增强袭击（4~24h 间隔）
        private const int MothershipContactTicks = 13 * 60000;   // 13 天
        private const int MothershipRaidTotal = 15;
        private const string BinguinFactionDefName = "Binguin";
        private int mothershipPhase;             // 0/1/2
        private int contactStartTick = -1;
        private Thing contactRelay;              // 发起联系的发射器（拆毁→联系失败）
        private int nextRaidTick = -1;
        private int raidsLeft;

        public int MothershipPhase { get { return mothershipPhase; } }

        // ★ 2026-09 飞天结局电影式收尾：0=无 1=白屏中 2=字幕已弹出（原版
        //   飞天结局同款：白屏淡入 → 黑幕滚动《制作人员》字幕 → 自动回游戏）
        private int launchStage;
        private float launchStageTimer;
        private string launchCreditsText;

        public bool LaunchCinematicBusy { get { return launchStage != 0; } }

        // 母舰联系中剩余时间文案 / 状态
        public string MothershipStatusText()
        {
            if (mothershipPhase == 1 && contactStartTick >= 0)
            {
                int left = contactStartTick + MothershipContactTicks - GenTicks.TicksGame;
                if (left < 0) { left = 0; }
                float days = left / 60000f;
                return "Binguin_GameComponentDiplomacy_01".Translate() + days.ToString("0.0") + "Binguin_GameComponentDiplomacy_02".Translate() + (MothershipRaidTotal - raidsLeft)
                    + "/" + MothershipRaidTotal + "Binguin_GameComponentDiplomacy_03".Translate();
            }
            if (mothershipPhase == 2)
            {
                return "Binguin_GameComponentDiplomacy_04".Translate();
            }
            return "Binguin_GameComponentDiplomacy_05".Translate();
        }

        // 开始联系（由信号发射器命令调用）
        public void StartMothershipContact(Building relay)
        {
            if (relay == null || !relay.Spawned)
            {
                return;
            }
            if (mothershipPhase != 0)
            {
                Messages.Message("Binguin_GameComponentDiplomacy_06".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            mothershipPhase = 1;
            contactStartTick = GenTicks.TicksGame;
            contactRelay = relay;
            raidsLeft = MothershipRaidTotal;
            // 第一波：4~24 小时后
            nextRaidTick = GenTicks.TicksGame + Rand.RangeInclusive(4 * 60 * 60, 24 * 60 * 60);
            Find.LetterStack.ReceiveLetter("Binguin_GameComponentDiplomacy_07".Translate(),
                "Binguin_GameComponentDiplomacy_08".Translate()
                + "Binguin_GameComponentDiplomacy_09".Translate() + MothershipRaidTotal
                + "Binguin_GameComponentDiplomacy_10".Translate(),
                LetterDefOf.NeutralEvent, new LookTargets(relay));
            BinguinLogUtility.Log("母舰联系开始。");
        }

        // 每 tick 推进母舰状态（GameComponentTick 调）
        public void MothershipTick()
        {
            try
            {
                if (mothershipPhase != 1)
                {
                    return;
                }
                // 发射器被拆 → 联系失败
                if (contactRelay == null || contactRelay.Destroyed)
                {
                    mothershipPhase = 0;
                    contactRelay = null;
                    Find.LetterStack.ReceiveLetter("Binguin_GameComponentDiplomacy_11".Translate(),
                        "Binguin_GameComponentDiplomacy_12".Translate(),
                        LetterDefOf.NegativeEvent, (LookTargets)null);
                    return;
                }
                // 13 天到期 → 已链接
                if (GenTicks.TicksGame >= contactStartTick + MothershipContactTicks)
                {
                    mothershipPhase = 2;
                    Find.LetterStack.ReceiveLetter("Binguin_GameComponentDiplomacy_13".Translate(),
                        "Binguin_GameComponentDiplomacy_14".Translate()
                        + "Binguin_GameComponentDiplomacy_15".Translate(),
                        LetterDefOf.PositiveEvent, new LookTargets(contactRelay));
                    return;
                }
                // 袭击调度
                if (raidsLeft > 0 && GenTicks.TicksGame >= nextRaidTick)
                {
                    TriggerMothershipRaid();
                    raidsLeft--;
                    nextRaidTick = GenTicks.TicksGame + Rand.RangeInclusive(4 * 60 * 60, 24 * 60 * 60);
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("母舰状态推进异常：" + e.Message, severity: 1, isDebug: false);
            }
        }

        // 触发一波增强袭击：点数 = 正常袭击 ×1.2~1.4，可超过原版上限；
        // 只走 RaidEnemy worker → 不会出现虫灾/发狂动物/邪教咏唱/异象袭击
        private void TriggerMothershipRaid()
        {
            try
            {
                Map map = Find.AnyPlayerHomeMap;
                if (map == null)
                {
                    map = (contactRelay != null && contactRelay.Map != null) ? contactRelay.Map : null;
                }
                if (map == null)
                {
                    return;
                }
                float basePoints = StorytellerUtility.DefaultThreatPointsNow(map);
                float pts = basePoints * Rand.Range(1.2f, 1.4f);
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(
                    IncidentCategoryDefOf.ThreatBig, map);
                parms.forced = true;
                parms.points = pts;
                // 选一个敌对派系（若存在）
                parms.faction = Find.FactionManager.RandomEnemyFaction(false, false, false, TechLevel.Undefined);
                IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                BinguinLogUtility.Log("母舰联系袭击波：点数 " + pts.ToString("0") + "（剩余 " + raidsLeft + " 波）");
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("触发母舰袭击异常：" + e.Message, severity: 1, isDebug: false);
            }
        }

        // ★ 2026-09 性能：本方法被发射仓的命令栏每帧调用 → 发射仓 def 懒加载
        //   缓存（def 名是常量），不再每帧查一次 DefDatabase。
        private static ThingDef cachedCapsuleDef;
        private static bool capsuleDefLookedUp;

        private static ThingDef ShipCapsuleDef
        {
            get
            {
                if (!capsuleDefLookedUp)
                {
                    capsuleDefLookedUp = true;
                    cachedCapsuleDef = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_ShipCapsule");
                }
                return cachedCapsuleDef;
            }
        }

        // 是否有任意发射仓载有乘客
        public bool AnyShipCapsuleOccupied()
        {
            try
            {
                List<Map> maps = Find.Maps;
                ThingDef capDef = ShipCapsuleDef;
                if (capDef == null)
                {
                    return false;
                }
                for (int m = 0; m < maps.Count; m++)
                {
                    Map map = maps[m];
                    if (map == null)
                    {
                        continue;
                    }
                    List<Thing> caps = map.listerThings.ThingsOfDef(capDef);
                    for (int i = 0; i < caps.Count; i++)
                    {
                        Building_Casket c = caps[i] as Building_Casket;
                        if (c != null && c.HasAnyContents)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // 电影式起飞收尾（2026-09 用户拍板，仿原版飞天结局流程）：
        //   白屏淡入 8 秒（起飞音）→ ScreenFader 转场后弹出原版
        //   Screen_Credits 黑幕滚动《制作人员》字幕（EndCreditsSong），
        //   字幕播完自动关闭并回到可继续游玩的殖民地。
        public void BeginLaunchCinematic(string passengersNames)
        {
            try
            {
                if (launchStage != 0)
                {
                    return;
                }
                string names = passengersNames != null ? passengersNames : "";
                string creditsText =
                    "Binguin_GameComponentDiplomacy_16".Translate()
                    + "Binguin_GameComponentDiplomacy_17".Translate()
                    + (names.Length > 0
                        ? names + (string)"Binguin_GameComponentDiplomacy_18".Translate()
                        : "")
                    + "Binguin_GameComponentDiplomacy_19".Translate()
                    + "Binguin_GameComponentDiplomacy_20".Translate()
                    + "Binguin_GameComponentDiplomacy_21".Translate()
                    + "Binguin_GameComponentDiplomacy_22".Translate()
                    + "Binguin_GameComponentDiplomacy_23".Translate();
                launchCreditsText = creditsText;
                launchStage = 1;
                launchStageTimer = 8f;   // 白屏时长（秒）
                try
                {
                    Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.ShipTakeoff, null);
                }
                catch (Exception e2)
                {
                    BinguinLogUtility.Log("起飞音播放异常：" + e2.Message, severity: 1, isDebug: false);
                }
                ScreenFader.StartFade(UnityEngine.Color.white, 8f);
                BinguinLogUtility.Log("飞天收尾：白屏淡入开始（8s 后弹字幕）。");
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("飞天收尾启动异常：" + e.Message, severity: 1, isDebug: false);
            }
        }

        // 发射：带走所有舱内殖民者（飞天结局），游戏可继续
        public void TryLaunchShip()
        {
            try
            {
                if (mothershipPhase != 2)
                {
                    Messages.Message("Binguin_GameComponentDiplomacy_24".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                List<Pawn> passengers = new List<Pawn>();
                List<Map> maps = Find.Maps;
                ThingDef capDef = ShipCapsuleDef;
                if (capDef != null)
                {
                    for (int m = 0; m < maps.Count; m++)
                    {
                        Map map = maps[m];
                        if (map == null)
                        {
                            continue;
                        }
                        List<Thing> caps = map.listerThings.ThingsOfDef(capDef);
                        for (int i = 0; i < caps.Count; i++)
                        {
                            Building_Casket c = caps[i] as Building_Casket;
                            if (c != null && c.HasAnyContents && c.ContainedThing is Pawn)
                            {
                                Pawn p = (Pawn)c.ContainedThing;
                                if (p != null)
                                {
                                    passengers.Add(p);
                                }
                            }
                        }
                    }
                }
                if (passengers.Count == 0)
                {
                    Messages.Message("Binguin_GameComponentDiplomacy_25".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                // 带走的殖民者离开（结局：随母舰离去）
                foreach (Pawn p in passengers)
                {
                    try
                    {
                        if (p != null && !p.Destroyed)
                        {
                            p.Destroy(DestroyMode.Vanish);
                        }
                    }
                    catch (Exception e)
                    {
                        BinguinLogUtility.Log("乘客离场异常：" + e.Message, severity: 1, isDebug: false);
                    }
                }

                string names = "";
                for (int i = 0; i < passengers.Count; i++)
                {
                    if (passengers[i] != null)
                    {
                        names += (i > 0 ? "、" : "") + passengers[i].LabelShort;
                    }
                }

                // 发射仓本体随船离场（原版飞天结局同款：飞船建筑消失）
                if (capDef != null)
                {
                    for (int m = 0; m < maps.Count; m++)
                    {
                        Map map = maps[m];
                        if (map == null)
                        {
                            continue;
                        }
                        List<Thing> caps2 = map.listerThings.ThingsOfDef(capDef);
                        for (int i = 0; i < caps2.Count; i++)
                        {
                            try
                            {
                                if (caps2[i] != null && !caps2[i].Destroyed)
                                {
                                    caps2[i].Destroy(DestroyMode.Vanish);
                                }
                            }
                            catch (Exception e2)
                            {
                                BinguinLogUtility.Log("发射仓离场异常：" + e2.Message, severity: 1, isDebug: false);
                            }
                        }
                    }
                }

                // ★ 2026-09（用户拍板）：起飞不再弹信——白屏淡入后弹出
                //   原版同款《制作人员》滚动字幕（类似原版飞天结局）。
                BeginLaunchCinematic(names);
                BinguinLogUtility.Log("飞天结局触发（电影式收尾）：乘客 " + names);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("发射飞船异常：" + e, severity: 2, isDebug: false);
            }
        }

        public void RegisterWillingComp(CompBinguinWillingCannon comp)
        {
            if (comp != null && !willingRegistry.Contains(comp))
            {
                willingRegistry.Add(comp);
            }
        }

        public void UnregisterWillingComp(CompBinguinWillingCannon comp)
        {
            if (comp != null)
            {
                willingRegistry.Remove(comp);
            }
        }

        // ---- 滑行冲刺瞄准范围圈（2026-08-19）----
        // 用 GameComponent 每帧绘制，不依赖 Targeter 内部回调（onGuiAction 实测不生效）。
        // 玩家点击「滑行冲刺」进入目标选择 → StartSlideAim 标记；targeting 结束 → StopSlideAim。
        public static bool slideAimActive;
        public static Pawn slideAimPawn;
        public static float slideAimRange;

        public static void StartSlideAim(Pawn pawn, float range)
        {
            slideAimPawn = pawn;
            slideAimRange = range;
            slideAimActive = true;
        }

        public static void StopSlideAim()
        {
            slideAimActive = false;
            slideAimPawn = null;
        }

        // ---- 威灵炮击瞄准范围圈（2026-08-20 v8，自绘命令）----
        // 与滑行共用 GameComponent 每帧绘制方案（Targeter 内部回调 1.6 不生效）。
        // 玩家点击「发射迫击炮」→ StartCannonAim 标记；targeting 结束 → StopCannonAim。
        public static bool cannonAimActive;
        public static Pawn cannonAimPawn;
        public static float cannonAimRange;

        public static void StartCannonAim(Pawn pawn, float range)
        {
            cannonAimPawn = pawn;
            cannonAimRange = range;
            cannonAimActive = true;
        }

        public static void StopCannonAim()
        {
            cannonAimActive = false;
            cannonAimPawn = null;
        }

        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            // ★ 飞天结局电影：白屏计时结束 → 弹原版《制作人员》字幕窗口
            if (launchStage == 1)
            {
                launchStageTimer -= UnityEngine.Time.deltaTime;
                if (launchStageTimer <= 0f)
                {
                    launchStage = 2;
                    try
                    {
                        GameVictoryUtility.ShowCredits(launchCreditsText,
                            SongDefOf.EndCreditsSong, false, 4.5f);
                        BinguinLogUtility.Log("飞天字幕已弹出。");
                    }
                    catch (Exception e)
                    {
                        BinguinLogUtility.Log("字幕窗口弹出异常：" + e.Message, severity: 1, isDebug: false);
                    }
                }
            }
            if (slideAimActive && slideAimPawn != null && slideAimPawn.Spawned && slideAimPawn.Map != null)
            {
                GenDraw.DrawRadiusRing(slideAimPawn.Position, slideAimRange, new UnityEngine.Color(0.45f, 0.75f, 1f));
            }
            // 威灵炮圈（橙红色，区别于滑板浅蓝）
            if (cannonAimActive && cannonAimPawn != null && cannonAimPawn.Spawned && cannonAimPawn.Map != null)
            {
                GenDraw.DrawRadiusRing(cannonAimPawn.Position, cannonAimRange, new UnityEngine.Color(1f, 0.55f, 0.2f));
            }
            // ★ 滑板 0.2 秒前摇（2026-08-19 用户要求）：到点执行起飞
            if (CompBinguinBoard.pendingSlideTick >= 0 && GenTicks.TicksGame >= CompBinguinBoard.pendingSlideTick)
            {
                CompBinguinBoard.ExecutePendingSlide();
            }
            // ★ 威灵炮站定瞄准视觉（v10）：JobDriver 瞄准期间每帧画
            //   瞄准扇形（角度随进度张开 = 蓄力进度条）+ 落点圈
            CompBinguinWillingCannon.DrawAimVisualTick();
            // ★ 2026-10-09 直钩「穿刺」技能：瞄准时画一条【直线】射程预览
            //   （替代原版的圆形高亮 —— 那个跟实际射线范围不一致）
            BinguinPierceAim.DrawAimVisualTick();
        }

        private Pawn diplomat;
        private int leavingTick = -1;        // <0 = 尚未答复（不离开）；>=0 = 答复后开始倒计时
        private int responseKind;            // 0=未答复 1=愿意共存 2=保持中立 3=无意共存
        private bool refusalGoodwillApplied; // 无意共存 -75 是否已在外交官出图时结算（防重复）
        private IntVec3 leaveTarget = IntVec3.Invalid; // 边缘目标格
        private int nextWalkRetry = -1;       // 防刷任务：重派走路任务的间隔 tick

        // 1.6 的 Game.FillComponents() 用 Activator.CreateInstance(type, game) 实例化组件，
        // 因此必须提供 public ctor(Game)；基类只有受保护的无参构造，故 : base() 即可。
        public GameComponent_BinguinDiplomacy(Game game) : base()
        {
        }

        public override void GameComponentTick()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return;
            }

            if (!initialized)
            {
                initialized = true;
                if (startTick < 0)
                {
                    startTick = GenTicks.TicksGame;
                }
            }

            if (!allyApplied)
            {
                allyApplied = true;
                TryApplyBinguinAllyAtGameStart();
            }

            // ★ 冰鹅剧本（玩家派系=Binguin_PlayerColony）已是同族盟友，
            //   不再触发「三天后外交官询问共存」事件（该事件面向人类玩家开局）；
            //   脚本开场直接盟友化由 TryApplyBinguinAllyAtGameStart 完成。
            // ★ 2026-09 用户强调 + 加固：判定不再只看 defName，三种情况都跳过——
            //   ① 玩家派系 = Binguin_PlayerColony（冰鹅剧本）
            //   ② 玩家派系的种族就是冰鹅族（自定义剧本直接选冰鹅派系等）
            //   ③ 开局就已经和 NPC 冰鹅派系是盟友（都结盟了没必要再问共存）
            if (!fired && GenTicks.TicksGame >= startTick + DelayTicks)
            {
                Faction playerF = Faction.OfPlayer;
                bool isBinguinScenario = IsBinguinPlayerStart(playerF);
                if (!isBinguinScenario)
                {
                    TrySendDiplomat();
                }
                else
                {
                    fired = true; // 冰鹅剧本：标记已处理，避免每帧重复判断
                    BinguinLogUtility.Log("冰鹅族（同族/已结盟）开局：跳过三天后的外交官共存事件。");
                }
            }

            // leavingTick >= 0 才进入离场流程：未答复则外交官原地等待
            if (diplomat != null && !diplomat.Destroyed && leavingTick >= 0)
            {
                TickDeparture();
            }

            // ★ v19 性能优化：
            //   ① 威灵自动装填：注册表驱动——穿上/脱下由 apparel comp
            //      （Notify_Equipped/Unequipped）登记/注销，每 600 tick 只
            //      遍历"当前穿在身上的威灵"（0~N 个，无威灵时零成本）；
            //      全图衣物扫描降为低频校正：读档后首 tick + 每 6000 tick
            //      （100 秒）重建注册表一次（覆盖读档/事件漏发）。
            //   ② 寒门盾场：事件驱动 + 读档/每 6000 tick 兜底（同 v18）。
            if (GenTicks.TicksGame % 600 == 0)
            {
                try
                {
                    TickWillingRegistry();
                }
                catch (Exception ex)
                {
                    BinguinLogUtility.Log("威灵自动装填轮询异常：" + ex.Message, severity: 1, isDebug: false);
                }
                // ★ 2026-09：外交官看护（未答复期间被移出地图/死亡 → 补派）
                CareForDiplomat();
            }
            if (willingRebuildPending || gateScanPending || GenTicks.TicksGame % 6000 == 0)
            {
                willingRebuildPending = false;
                gateScanPending = false;
                try
                {
                    WillingRegistryRebuildScan();
                    GateShieldBackfillScan();
                    IceCrownAbilityBackfillScan();
                }
                catch (Exception ex)
                {
                    BinguinLogUtility.Log("穿戴装备校正扫描异常：" + ex.Message, severity: 1, isDebug: false);
                }
            }

            // ★ v23 母舰联系状态推进（飞天决战；无状态时开销≈0）
            MothershipTick();

            // ★ 霜语者冷笑话（2026-08-21）：每 10 天讲一条。
            //   v16 性能（用户拍板）：笑话检查放宽到每 60000 tick（1 游戏日）
            //   一次——笑话 10 天才一条，最多延迟 1 天完全无感。
            if (GenTicks.TicksGame % 60000 == 0)
            {
                TryTellFrostSpeakerJoke();
            }
        }

        // ★ 2026-09：判断"这是不是冰鹅族/已结盟的玩家开局"（三种情况见调用点注释）
        private static bool IsBinguinPlayerStart(Faction player)
        {
            if (player == null || player.def == null)
            {
                return false;
            }
            if (player.def.defName == "Binguin_PlayerColony")
            {
                return true;
            }
            // 玩家派系的默认成员种族就是冰鹅族
            // ★ 2026-09-26 收口：判断"兵种"用 IsBinguinKind（看 kind.race），
            //   不要用 pawn.def（第 83 条踩过这个坑：IsLeadSinger 误用 pawn.def）。
            PawnKindDef kind = player.def.basicMemberKind;
            if (BinguinRaceUtility.IsBinguinKind(kind))
            {
                return true;
            }
            // 开局即与 NPC 冰鹅派系结盟 → 不必再派外交官问共存
            Faction npc = Find.FactionManager.AllFactions.FirstOrDefault(
                delegate(Faction f) { return f != null && f.def != null
                    && f.def.defName == FactionDefName && f != player; });
            return npc != null && npc.RelationKindWith(player) == FactionRelationKind.Ally;
        }

        // 当前叙述者是否为霜语者（随 60000 tick 节流调用，无需内部缓存）
        private static bool IsFrostSpeakerActive()
        {
            try
            {
                Storyteller st = Find.Storyteller;
                return st != null && st.def != null
                    && st.def.defName == FrostSpeakerStorytellerDefName;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // 到 10 天讲一条（随机不紧邻重复），用中立信件弹出
        private void TryTellFrostSpeakerJoke()
        {
            try
            {
                if (!IsFrostSpeakerActive())
                {
                    return;
                }
                if (nextJokeTick < 0)
                {
                    // 初次：进入游戏后第 10 天讲第一条
                    nextJokeTick = GenTicks.TicksGame + JokeIntervalTicks;
                    return;
                }
                if (GenTicks.TicksGame < nextJokeTick)
                {
                    return;
                }
                nextJokeTick = GenTicks.TicksGame + JokeIntervalTicks;

                string[] jokes = BinguinJokeLibrary.Jokes;
                if (jokes == null || jokes.Length == 0)
                {
                    return;
                }
                // 随机一条，避免与上一条相同
                int idx = Rand.RangeInclusive(0, jokes.Length - 1);
                if (jokes.Length > 1 && idx == lastJokeIndex)
                {
                    idx = (idx + 1 + Rand.Range(0, jokes.Length - 1)) % jokes.Length;
                }
                lastJokeIndex = idx;
                string joke = jokes[idx];
                if (string.IsNullOrEmpty(joke))
                {
                    return;
                }
                Find.LetterStack.ReceiveLetter(
                    "Binguin_GameComponentDiplomacy_26".Translate(), joke, LetterDefOf.NeutralEvent,
                    (LookTargets)null);
                BinguinLogUtility.Log("霜语者讲冷笑话 #" + idx);
            }
            catch (Exception ex)
            {
                BinguinLogUtility.Log("霜语者冷笑话异常：" + ex.Message, severity: 1, isDebug: false);
            }
        }

        // 调试/立即测试：马上讲一条（Debug Actions）
        public void TellJokeNowForDebug()
        {
            nextJokeTick = GenTicks.TicksGame;   // 下一 tick 即触发
            BinguinLogUtility.Log("调试：霜语者将于下一 tick 讲笑话。");
        }

        // ★ v19 威灵注册表轮询（每 600 tick）：只遍历"当前穿在身上的
        //   威灵"（事件登记，通常 0~N 个；无威灵时列表为空，零成本）。
        //   顺带清理失效项（衣物被销毁等未注销的极端情况）。
        private void TickWillingRegistry()
        {
            if (willingRegistry.Count == 0)
            {
                return;
            }
            for (int i = willingRegistry.Count - 1; i >= 0; i--)
            {
                CompBinguinWillingCannon c = willingRegistry[i];
                if (c == null || c.parent == null || c.parent.Destroyed)
                {
                    willingRegistry.RemoveAt(i);
                    continue;
                }
                // 已不在任何人身上（比如被强制脱下未触发事件）→ 移除
                Pawn wearer = c.Wearer;
                if (wearer == null)
                {
                    willingRegistry.RemoveAt(i);
                    continue;
                }
                c.AutoMaintainTick();
            }
        }

        // ★ v19 注册表重建（读档后首个 tick + 每 6000 tick 校正）：
        //   读档/事件漏发时,全图扫一次把当前穿着的威灵重新登记。
        private void WillingRegistryRebuildScan()
        {
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map == null || map.mapPawns == null || !map.mapPawns.AnyFreeColonistSpawned)
                {
                    continue;
                }
                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn p = colonists[i];
                    if (p == null || p.Destroyed || !p.Spawned
                        || p.apparel == null || p.apparel.WornApparel == null)
                    {
                        continue;
                    }
                    List<Apparel> worn = p.apparel.WornApparel;
                    for (int j = 0; j < worn.Count; j++)
                    {
                        Apparel a = worn[j];
                        if (a == null || a.def == null
                            || a.def.defName != "Binguin_WillingArmor")
                        {
                            continue;
                        }
                        CompBinguinWillingCannon c = a.TryGetComp<CompBinguinWillingCannon>();
                        if (c != null)
                        {
                            RegisterWillingComp(c);
                        }
                    }
                }
            }
        }

        // ★ v18 寒门盾场兜底扫描（读档后首个 tick + 每 60000 tick）：
        //   正常穿戴/脱下已由 apparel comp 事件即时处理（v18），此兜底只
        //   覆盖：读档后（事件不会重放）与极端意外丢失。检测穿寒门盾却
        //   无场实体 → 补发。
        private static void GateShieldBackfillScan()
        {
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map == null || map.mapPawns == null || !map.mapPawns.AnyFreeColonistSpawned)
                {
                    continue;
                }
                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn p = colonists[i];
                    if (p == null || p.Destroyed || !p.Spawned
                        || p.apparel == null || p.apparel.WornApparel == null)
                    {
                        continue;
                    }
                    bool wears = false;
                    List<Apparel> worn = p.apparel.WornApparel;
                    for (int j = 0; j < worn.Count; j++)
                    {
                        Apparel a = worn[j];
                        if (a != null && a.def != null
                            && a.def.defName == "Binguin_GateShield")
                        {
                            wears = true;
                            break;
                        }
                    }
                    if (wears)
                    {
                        CompBinguinGateShieldField.EnsureFieldFor(p);
                    }
                }
            }
        }

        // ★ 2026-09 冰冠技能读档兜底（每 6000 tick + 读档后首 tick）：
        //   Ability 不随 apparel comp 存档，读档后要靠这里补发「投掷冰块」。
        private static void IceCrownAbilityBackfillScan()
        {
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map == null || map.mapPawns == null || !map.mapPawns.AnyFreeColonistSpawned)
                {
                    continue;
                }
                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn p = colonists[i];
                    if (p == null || p.Destroyed || !p.Spawned
                        || p.apparel == null || p.apparel.WornApparel == null)
                    {
                        continue;
                    }
                    List<Apparel> worn = p.apparel.WornApparel;
                    for (int j = 0; j < worn.Count; j++)
                    {
                        Apparel a = worn[j];
                        if (a == null || a.def == null || a.def.defName != "Binguin_IceCrown")
                        {
                            continue;
                        }
                        CompBinguinIceCrown comp = a.TryGetComp<CompBinguinIceCrown>();
                        if (comp != null)
                        {
                            comp.EnsureAbility();
                        }
                    }
                }
            }
        }

        // ★ 冰鹅剧本开局即盟友（好感 100，FactionRelationKind.Ally）
        private void TryApplyBinguinAllyAtGameStart()
        {
            try
            {
                Faction player = Faction.OfPlayer;
                if (player == null)
                {
                    return;
                }
                // 仅当玩家派系 = 冰鹅玩家派系（冰鹅剧本开局）时生效；
                // 原版开局（玩家=人类）不自动结盟，仍走三天外交官共存事件。
                if (player.def.defName != "Binguin_PlayerColony")
                {
                    return;
                }
                Faction npc = Find.FactionManager.AllFactions.FirstOrDefault(
                    delegate(Faction f) { return f.def.defName == FactionDefName && f != player; });
                if (npc == null || npc.defeated)
                {
                    return;
                }
                npc.TryAffectGoodwillWith(player, 1000, false, false, null, null);
                if (npc.RelationKindWith(player) != FactionRelationKind.Ally)
                {
                    npc.SetRelationDirect(player, FactionRelationKind.Ally, false, null, null);
                }
                BinguinLogUtility.Log("冰鹅剧本开局：与冰鹅派系已结为盟友（好感 100）。");
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("开局结盟设置失败: " + e.Message, severity: 1, isDebug: false);
            }
        }

        // 由共存意向选择信按钮调用（1=愿意共存 +50 立即；
        // 2=保持中立 好感不变；3=无意共存 -75 等外交官走出地图再结算）
        public void HandleResponse(int kind)
        {
            if (kind != RespAccepted && kind != RespNeutral && kind != RespRefused)
            {
                return;
            }
            if (diplomat == null || diplomat.Destroyed || !diplomat.Spawned || leavingTick >= 0)
            {
                return; // 外交官已不在或已答复过
            }
            responseKind = kind;

            Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(
                delegate(Faction f) { return f.def.defName == FactionDefName; });
            if (kind == RespAccepted && faction != null && faction != Faction.OfPlayer)
            {
                // 愿意共存：立即 +50
                faction.TryAffectGoodwillWith(Faction.OfPlayer, 50);
            }
            // 无意共存的 -75 不在这里结算——等外交官走出地图边缘时在
            // DepartDiplomat 里结算（用户：出地图再触发）。

            int stayTicks = kind == RespAccepted ? StayTicksIfAccepted
                : (kind == RespNeutral ? StayTicksIfNeutral : RefusalMaxLeaveTicks);
            leavingTick = GenTicks.TicksGame + stayTicks;
            TryStartDeparture();
        }

        // ---------- 交谈入口（2026-08-22：殖民者点击外交官才进入事件） ----------

        // 该外交官是否正在殖民地等待答复（供右键菜单/交谈任务判断）
        public bool IsDiplomatWaiting(Pawn p)
        {
            return p != null && p == diplomat && !p.Destroyed && p.Spawned
                && leavingTick < 0 && responseKind == 0;
        }

        // 殖民者走到外交官面前时调用：仍未被答复才弹共存意向选择信
        public void ShowDiplomacyLetter()
        {
            try
            {
                if (diplomat == null || diplomat.Destroyed || !diplomat.Spawned)
                {
                    return;
                }
                if (leavingTick >= 0 || responseKind != 0)
                {
                    return; // 已答复过（外交官正在离场）
                }

                LetterDef letterDef = DefDatabase<LetterDef>.GetNamedSilentFail(DiplomacyLetterDefName);
                if (letterDef == null)
                {
                    return;
                }

                ChoiceLetter_BinguinDiplomacy letter = new ChoiceLetter_BinguinDiplomacy();
                letter.def = letterDef;
                letter.Label = "Binguin_GameComponentDiplomacy_27".Translate();
                letter.Text = "Binguin_GameComponentDiplomacy_28".Translate() +
                              "Binguin_GameComponentDiplomacy_29".Translate() +
                              "Binguin_GameComponentDiplomacy_30".Translate();
                letter.lookTargets = new LookTargets(diplomat);
                Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(
                    delegate(Faction f) { return f.def.defName == FactionDefName; });
                letter.relatedFaction = faction;

                Find.LetterStack.ReceiveLetter(letter);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("弹出外交对话异常：" + e.Message, severity: 1, isDebug: false);
            }
        }

        // ---------- 离场流程 ----------

        // 答复后：找地图边缘格并让外交官走过去（找不到边缘格则只按计时离场）
        private void TryStartDeparture()
        {
            if (diplomat == null || diplomat.Destroyed || !diplomat.Spawned)
            {
                return;
            }
            if (!leaveTarget.IsValid)
            {
                Map map = diplomat.Map;
                if (map == null)
                {
                    return;
                }
                IntVec3 cell;
                if (CellFinder.TryFindRandomEdgeCellWith(
                        delegate(IntVec3 c)
                        {
                            return c.Walkable(map)
                                && map.reachability.CanReach(diplomat.Position, c,
                                    PathEndMode.OnCell, TraverseMode.ByPawn);
                        },
                        map, 0.5f, out cell))
                {
                    leaveTarget = cell;
                }
            }
            if (leaveTarget.IsValid && diplomat.pather != null && !diplomat.pather.MovingNow)
            {
                diplomat.jobs.StartJob(new Job(JobDefOf.Goto, leaveTarget), JobCondition.InterruptForced);
            }
        }

        // 每 tick 检查：期满 / 已到边缘 → 离场；没在走 → 重派走路任务
        private void TickDeparture()
        {
            if (diplomat == null || diplomat.Destroyed)
            {
                diplomat = null;
                return;
            }

            if (GenTicks.TicksGame >= leavingTick)
            {
                DepartDiplomat();
                return;
            }

            if (!leaveTarget.IsValid)
            {
                return;
            }

            if (diplomat.Position.InHorDistOf(leaveTarget, 1.5f))
            {
                DepartDiplomat();
                return;
            }

            // 防御：任务被其他 AI 打断时重派（限频，避免每 tick 刷任务）
            if (diplomat.pather != null && !diplomat.pather.MovingNow
                && GenTicks.TicksGame >= nextWalkRetry)
            {
                nextWalkRetry = GenTicks.TicksGame + 120;
                diplomat.jobs.StartJob(new Job(JobDefOf.Goto, leaveTarget), JobCondition.InterruptForced);
            }
        }

        // 离场（外交官走出地图边缘）：先结算「无意共存 -75」，再发离开信、移除
        private void DepartDiplomat()
        {
            if (diplomat == null || diplomat.Destroyed)
            {
                diplomat = null;
                return;
            }

            // ★ 2026-08-22 用户需求：无意共存的好感惩罚等到外交官走出地图
            //   边缘时才触发（此前在答复时立即扣，外交官还没离开地图）。
            if (responseKind == RespRefused && !refusalGoodwillApplied)
            {
                refusalGoodwillApplied = true;
                Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(
                    delegate(Faction f) { return f.def.defName == FactionDefName; });
                if (faction != null && faction != Faction.OfPlayer)
                {
                    faction.TryAffectGoodwillWith(Faction.OfPlayer, -75);
                    BinguinLogUtility.Log("外交官走出地图：无意共存结算 -75 好感。");
                }
            }

            SendLeaveLetter();

            diplomat.DeSpawn();
            diplomat.Destroy();
            diplomat = null;
            leaveTarget = IntVec3.Invalid;
        }

        private void SendLeaveLetter()
        {
            string leaveText;
            if (responseKind == RespNeutral)
            {
                leaveText = "Binguin_GameComponentDiplomacy_31".Translate();
            }
            else if (responseKind == RespRefused)
            {
                leaveText = "Binguin_GameComponentDiplomacy_32".Translate();
            }
            else
            {
                // 愿意共存（含旧档遗留未区分的情形，默认按愿意共存处理）
                leaveText = "Binguin_GameComponentDiplomacy_33".Translate();
            }

            LetterDef leaveDef = DefDatabase<LetterDef>.GetNamedSilentFail(LeaveLetterDefName);
            if (leaveDef != null && diplomat != null)
            {
                Find.LetterStack.ReceiveLetter("Binguin_GameComponentDiplomacy_34".Translate(), leaveText, leaveDef, new LookTargets(diplomat));
            }
        }

        // ---------- 派遣外交官 ----------
        // public：供 DebugActions_Binguin 调试命令与测试调用
        public void TrySendDiplomat()
        {
            fired = true;

            Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(
                delegate(Faction f) { return f.def.defName == FactionDefName; });
            if (faction == null || faction.defeated || faction == Faction.OfPlayer)
            {
                return;
            }

            Map map = Find.AnyPlayerHomeMap;
            if (map == null)
            {
                return;
            }

            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(DiplomatKindDefName);
            if (kind == null)
            {
                return;
            }

            Pawn newDiplomat = PawnGenerator.GeneratePawn(kind, faction);

            IntVec3 cell;
            if (!CellFinder.TryFindRandomEdgeCellWith(
                    delegate(IntVec3 c) { return c.Walkable(map) && map.reachability.CanReachColony(c); },
                    map, 0.7f, out cell))
            {
                cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 12);
            }
            GenSpawn.Spawn(newDiplomat, cell, map);
            diplomat = newDiplomat;

            // ★ 2026-09 修复：生成后自动走进殖民地内部（路径会探开迷雾），
            //   避免外交官站在地图边缘的迷雾里 → 玩家“收到信却看不到人”。
            TryWalkDiplomatIntoColony(newDiplomat);

            // ★ 2026-08-22：到达只发【通知信】——共存意向的选项信不再自动弹出，
            //   玩家须选中一名殖民者、右键点击外交官（走近交谈）才进入事件。
            LetterDef arrivalDef = DefDatabase<LetterDef>.GetNamedSilentFail(ArrivalLetterDefName);
            if (arrivalDef == null)
            {
                return;
            }

            string arrivalText = "Binguin_GameComponentDiplomacy_35".Translate();
            Find.LetterStack.ReceiveLetter("Binguin_GameComponentDiplomacy_36".Translate(), arrivalText,
                arrivalDef, new LookTargets(newDiplomat));
            BinguinLogUtility.Log("外交官已到达，等待殖民者右键交谈。");
        }

        // 外交官走进殖民地：选一名殖民者附近的可走格作为等待点（路径自动
        // 探开迷雾），让她出现在可见区，方便玩家看到并右键交谈。
        private void TryWalkDiplomatIntoColony(Pawn d)
        {
            try
            {
                if (d == null || d.Destroyed || !d.Spawned || d.Map == null)
                {
                    return;
                }
                Map map = d.Map;
                Pawn anchor = null;
                List<Pawn> colonists = map.mapPawns.FreeColonists;
                if (colonists != null)
                {
                    for (int i = 0; i < colonists.Count; i++)
                    {
                        Pawn p = colonists[i];
                        if (p != null && !p.Destroyed && p.Spawned)
                        {
                            anchor = p;
                            break;
                        }
                    }
                }
                IntVec3 dest = map.Center;
                if (anchor != null)
                {
                    IntVec3 near = CellFinder.RandomClosewalkCellNear(anchor.Position, map, 8,
                        delegate(IntVec3 c)
                        {
                            return c.Walkable(map)
                                && map.reachability.CanReach(d.Position, c,
                                    PathEndMode.OnCell, TraverseMode.ByPawn);
                        });
                    if (near.IsValid)
                    {
                        dest = near;
                    }
                }
                if (!dest.IsValid || !dest.Walkable(map))
                {
                    dest = d.Position;
                }
                if (dest.IsValid && dest.Walkable(map) && d.pather != null && d.jobs != null)
                {
                    d.jobs.StartJob(new Job(JobDefOf.Goto, dest), JobCondition.InterruptForced);
                    BinguinLogUtility.Log("外交官正走进殖民地等待点 " + dest);
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("外交官走进殖民地失败：" + e.Message, severity: 1, isDebug: false);
            }
        }

        // 外交官看护（每 600 tick）：尚未答复期间若外交官被莫名移出地图/
        // 死亡，重新补派一位，保证事件不静默丢失（带 5 分钟冷却防刷）。
        private int nextDiplomatResendTick = -1;

        private void CareForDiplomat()
        {
            try
            {
                if (diplomat == null || leavingTick >= 0 || responseKind != 0)
                {
                    return; // 没有等待答复的外交官（答复后走正常离场流程）
                }
                if (!diplomat.Destroyed)
                {
                    if (diplomat.Spawned)
                    {
                        return; // 一切正常
                    }
                    BinguinLogUtility.Log("外交官在答复前被移出地图，准备补派。", severity: 1, isDebug: false);
                }
                else
                {
                    BinguinLogUtility.Log("外交官在答复前死亡/消失，准备补派。", severity: 1, isDebug: false);
                }
                if (GenTicks.TicksGame < nextDiplomatResendTick)
                {
                    return; // 冷却中（防止反复秒死刷屏）
                }
                nextDiplomatResendTick = GenTicks.TicksGame + 300000; // 5 分钟
                diplomat = null;
                TrySendDiplomat();
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("外交官看护异常：" + e.Message, severity: 1, isDebug: false);
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look<bool>(ref initialized, "initialized", false, false);
            Scribe_Values.Look<int>(ref startTick, "startTick", -1, false);
            Scribe_Values.Look<bool>(ref fired, "fired", false, false);
            Scribe_References.Look<Pawn>(ref diplomat, "diplomat", false);
            Scribe_Values.Look<int>(ref leavingTick, "leavingTick", -1, false);
            Scribe_Values.Look<int>(ref responseKind, "responseKind", 0, false);
            Scribe_Values.Look<bool>(ref refusalGoodwillApplied, "refusalGoodwillApplied", false, false);
            Scribe_Values.Look<IntVec3>(ref leaveTarget, "leaveTarget", IntVec3.Invalid, false);
            Scribe_Values.Look<int>(ref nextWalkRetry, "nextWalkRetry", -1, false);
            Scribe_Values.Look<int>(ref nextJokeTick, "nextJokeTick", -1, false);
            Scribe_Values.Look<int>(ref lastJokeIndex, "lastJokeIndex", -1, false);
            // v18/v19：每次读档后做一次护盾场兜底补发 + 威灵注册表重建
            //（穿戴事件不会在读档时重放）
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                gateScanPending = true;
                willingRebuildPending = true;
            }
            // v23：母舰联系状态（飞天决战）
            Scribe_Values.Look<int>(ref mothershipPhase, "mothershipPhase", 0, false);
            Scribe_Values.Look<int>(ref contactStartTick, "contactStartTick", -1, false);
            Scribe_References.Look<Thing>(ref contactRelay, "contactRelay", false);
            Scribe_Values.Look<int>(ref nextRaidTick, "nextRaidTick", -1, false);
            Scribe_Values.Look<int>(ref raidsLeft, "raidsLeft", 0, false);
        }
    }

    // ============================================================================
    // 开发者调试命令（开启 Dev Mode 后：Debug Actions 菜单 → "冰鹅族" 分类）
    //  - 立即派遣外交官：跳过 3 天等待，立刻触发外交事件（生成外交官+到达通知信）
    //  - 诊断/修复派系领袖：检查冰鹅派系领袖，尝试生成并捕获真实异常（用于排查"首席 null"）
    // ============================================================================
    public static class DebugActions_Binguin
    {
        // ★ 特性实参必须是编译期常量，不能用 .Translate()（CS0182）—— 开发者菜单专用，保留中文原文
        // ★ 毒垃圾报复的测试入口（2026-09）：
        //   正常触发要"组商队 → 跑到冰鹅据点附近 → 丢毒垃圾 → 等它溶解 → 拼概率"，
        //   基本没法测。这里给一个一键触发的开发者菜单项。
        //   注意特性实参必须是编译期常量，不能用 .Translate()（CS0182）。
        [DebugAction("冰鹅族", "毒垃圾报复：立即触发特制版")]
        public static void TriggerWasteRetaliationNow()
        {
            Faction f = Find.FactionManager.AllFactions.FirstOrDefault(
                delegate(Faction x) { return BinguinFactions.IsPeaceful(x); });
            if (f == null)
            {
                BinguinLogUtility.Log("未找到冰鹅派系。");
                return;
            }
            BinguinWasteRetaliation.DoRetaliation(f);
        }

        [DebugAction("冰鹅族", "毒垃圾报复：走原版 PollutionRaid")]
        public static void TriggerWasteRaidNow()
        {
            Faction f = Find.FactionManager.AllFactions.FirstOrDefault(
                delegate(Faction x) { return BinguinFactions.IsPeaceful(x); });
            if (f == null)
            {
                BinguinLogUtility.Log("未找到冰鹅派系。");
                return;
            }
            BinguinWasteRetaliation.DoVanillaRaid(f);
        }

        // ★ 2026-09：修「老存档里的冰鹅肤色改不过来」。
        //   根因：HAR 把肤色通道【存进了存档】（AlienComp.colorChannels 走 Scribe_Collections），
        //        所以改 Defs/01_RaceAndAppearance/AlienRace_Binguin.xml 只影响【新生成】的小人；老小人会永远保留当初抽到的值。
        //   本命令把场上所有冰鹅的 skin 通道 first 覆写成纯白（= pawn.story.SkinColor 变白），
        //   second 归零（= 贴图不做 colorTwo 染色），然后刷新渲染。
        //   注意：AlienComp 及其元组类型在 HAR 里是 internal 的，所以走反射，避免编译期依赖。
        [DebugAction("冰鹅族", "重算所有冰鹅肤色（修老存档）")]
        public static void RecomputeBinguinSkinColor()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                BinguinLogUtility.Log("没有当前地图。");
                return;
            }
            int done = 0;
            int failed = 0;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                // ★ 2026-09-26 种族判定收口：见 BinguinRaceUtility
                if (!BinguinRaceUtility.IsBinguin(p)) continue;
                if (p.story == null) continue;
                object comp = null;
                var comps = p.AllComps;
                if (comps != null)
                {
                    for (int j = 0; j < comps.Count; j++)
                    {
                        if (comps[j] != null && comps[j].GetType().FullName == "AlienRace.AlienComp")
                        {
                            comp = comps[j];
                            break;
                        }
                    }
                }
                if (comp == null) { failed++; continue; }
                try
                {
                    MethodInfo mi = comp.GetType().GetMethod("OverwriteColorChannel",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (mi == null) { failed++; continue; }
                    // 本文件没有 using UnityEngine，全限定写
                    UnityEngine.Color? white = new UnityEngine.Color?(UnityEngine.Color.white);
                    UnityEngine.Color? clear = new UnityEngine.Color?(new UnityEngine.Color(0f, 0f, 0f, 0f));
                    mi.Invoke(comp, new object[] { "skin", white, clear });
                    if (p.Drawer != null && p.Drawer.renderer != null)
                    {
                        p.Drawer.renderer.SetAllGraphicsDirty();
                    }
                    done++;
                }
                catch (Exception ex)
                {
                    failed++;
                    BinguinLogUtility.Log("重算肤色失败(" + p.LabelShort + "): " + ex.Message, severity: 1, isDebug: false);
                }
            }
            BinguinLogUtility.Log("重算肤色完成：成功 " + done + "，失败 " + failed + "。");
            Messages.Message("冰鹅族：已重算 " + done + " 只的肤色通道"
                + (failed > 0 ? "，失败 " + failed + " 只（详见日志）" : "。"),
                MessageTypeDefOf.NeutralEvent);
        }

        [DebugAction("冰鹅族", "立即派遣外交官")]
        public static void SendDiplomatNow()
        {
            GameComponent_BinguinDiplomacy comp = Current.Game.GetComponent<GameComponent_BinguinDiplomacy>();
            if (comp != null)
            {
                comp.TrySendDiplomat();
                BinguinLogUtility.Log("已触发外交事件（外交官会在地图边缘生成）。");
            }
            else
            {
                BinguinLogUtility.Log("外交组件不存在（请先进入一个殖民地）。");
            }
        }

        [DebugAction("冰鹅族", "霜语者：马上讲一条冷笑话")]
        public static void TellJokeNow()
        {
            GameComponent_BinguinDiplomacy comp = Current.Game.GetComponent<GameComponent_BinguinDiplomacy>();
            if (comp != null)
            {
                comp.TellJokeNowForDebug();
            }
            else
            {
                BinguinLogUtility.Log("外交组件不存在。");
            }
        }

        [DebugAction("冰鹅族", "诊断/修复派系领袖")]
        public static void FixFactionLeader()
        {
            Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(
                delegate(Faction f) { return BinguinFactions.IsPeaceful(f); });
            if (faction == null)
            {
                BinguinLogUtility.Log("未找到冰鹅派系。");
                return;
            }
            if (faction.leader != null)
            {
                BinguinLogUtility.Log("领袖已存在：" + faction.leader.LabelShort);
                return;
            }

            // 1) 先尝试 vanilla 生成（1.6 的 TryGenerateLeader，可能 public/private）
            MethodInfo tg = typeof(Faction).GetMethod("TryGenerateLeader",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (tg != null)
            {
                try
                {
                    tg.Invoke(faction, null);
                }
                catch (Exception e)
                {
                    BinguinLogUtility.Log("TryGenerateLeader 异常：" + (e.InnerException != null ? e.InnerException.ToString() : e.ToString()), severity: 2, isDebug: false);
                }
            }
            if (faction.leader != null)
            {
                BinguinLogUtility.Log("修复成功：" + faction.leader.LabelShort);
                return;
            }

            // 2) 手动生成并捕获真实异常
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Binguin_Leader");
            if (kind == null)
            {
                BinguinLogUtility.Log("找不到 PawnKindDef Binguin_Leader！请检查 Defs/03_PawnKinds/PawnKindDefs_Binguin.xml。", severity: 2, isDebug: false);
                return;
            }
            Pawn pawn = null;
            try
            {
                pawn = PawnGenerator.GeneratePawn(kind, faction);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("PawnGenerator.GeneratePawn 异常：" + e, severity: 2, isDebug: false);
                return;
            }
            if (pawn == null)
            {
                BinguinLogUtility.Log("PawnGenerator.GeneratePawn 返回 null。", severity: 2, isDebug: false);
                return;
            }
            BinguinLogUtility.Log("手动生成领袖成功：" + pawn.LabelShort);

            // 3) 反射写入派系领袖字段（1.6 字段名以 leaderCore 优先，兼容 leader）
            FieldInfo lf = typeof(Faction).GetField("leaderCore",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (lf == null)
            {
                lf = typeof(Faction).GetField("leader",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            }
            if (lf != null)
            {
                lf.SetValue(faction, pawn);
                BinguinLogUtility.Log("已写入派系领袖字段 " + lf.Name);
            }
            else
            {
                BinguinLogUtility.Log("未找到派系领袖字段（leaderCore/leader），无法写入。", severity: 2, isDebug: false);
            }
        }
    }
}
