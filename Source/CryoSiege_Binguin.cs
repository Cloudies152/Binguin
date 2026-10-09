// ============================================================================
// 冰鹅族低温围攻（2026-09 用户需求）
//
// ★ 设计：不自己写围攻 AI，而是【整套复用原版围攻】，只替换两处：
//   1) 蓝图内容：Harmony 前缀拦 RimWorld.SiegeBlueprintPlacer.PlaceBlueprints
//      （该类的 IL 显示它是 SEALED+ABSTRACT 的静态类，没法继承；PlaceBlueprints 是
//       迭代器，所以用 prefix 直接给 __result 塞我们自己的蓝图序列并 return false）。
//      原版的选点（地图边缘就近）、沙袋掩体、建造/待机等行为全部保留。
//   1b) 阵地内容（2026-09 用户定稿）：**大气冷冻器 + 掩体沙袋**，
//      已按用户要求去掉两台自动加农炮。
//   2) 冲锋时机：postfix 挂 LordToil_Siege.LordToilTick —— 减员 30% 或工兵全部倒下
//      就 lord.GotoToil(突击 toil)；原版 Siege 策略完全不受影响。
//
// ★ 识别"这是我们的低温围攻"：蓝图放置时记下 (faction, map, tick)，
//   LordToilTick 里比对 lord.faction / map 与时间窗（1 天）。
//
// ★ 大气冷冻器效果：挂原版 GameCondition_TemperatureOffset（tempOffset 字段已确认）
//   → GameConditionManager.AggregateTemperatureOffset 会把它加进全图气温。
// ============================================================================

using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Binguin
{
    // 低温围攻的识别信息
    public static class BinguinCryoSiegeContext
    {
        public const string StrategyDefName = "Binguin_CryoSiege";
        public const string FreezerDefName = "Binguin_AtmosphereFreezer";

        // 大气冷冻器自毁（满 24 小时）→ 强制冲锋
        public static int SelfDestructTick = -1;

        public static bool JustSelfDestructed
        {
            get { return SelfDestructTick >= 0 && GenTicks.TicksGame - SelfDestructTick < 1200; }
        }

        public static Faction SiegeFaction;
        public static Map SiegeMap;
        public static int PlacedTick = -1;

        public static void MarkPlaced(Faction faction, Map map)
        {
            SiegeFaction = faction;
            SiegeMap = map;
            PlacedTick = GenTicks.TicksGame;
        }

        // ============================================================
        // ★★ 2026-09 真凶修复（第二次）：低温围攻"还是原版迫击炮阵地"★★
        //   时序（全部 IL 实证）：
        //     ① 袭击生成：AdjustedRaidPoints → 记下策略名 Binguin_CryoSiege
        //     ② 生成结束：Patch_BinguinRaidGear.MakeLordsPrefix 的 finally
        //        → BinguinRaidContext.EndRaid() → **策略名被清空**
        //     ③ 部队走到落点附近后：LordToil_Siege.Init
        //        → SiegeBlueprintPlacer.PlaceBlueprints(Data.siegeCenter, ...)
        //   原来第 ③ 步用 IsCryoSiegeStrategy（= 策略名 == Binguin_CryoSiege）判断，
        //   而它在第 ② 步就已经变成 null → **永远 false** → 永远走原版蓝图。
        //   → 改为在【我们的 worker 创建 LordJob 时】（第 ② 步之内、此时还知道自己是
        //     低温围攻）记录 (地图, 派系, 落点, tick)，第 ③ 步用这份记录识别。
        // ============================================================
        // 当前正在初始化的围攻 toil 是不是我们的低温围攻（由 LordJob 类型判定）
        public static bool ToilIsCryoSiege;

        public static Map PendingMap;
        public static Faction PendingFaction;
        public static IntVec3 PendingSpot = IntVec3.Invalid;
        public static int PendingTick = -1;

        public static void MarkPendingSiege(Map map, Faction faction, IntVec3 spot)
        {
            PendingMap = map;
            PendingFaction = faction;
            PendingSpot = spot;
            PendingTick = GenTicks.TicksGame;
            Log.Message("[冰鹅族] 低温围攻：已记录本场阵地（落点 " + spot
                + "），部队站定后放置冷冻器蓝图。");
        }

        public static bool IsPendingSiege(Map map, Faction faction, IntVec3 center)
        {
            if (map == null || PendingMap == null || PendingMap != map)
            {
                return false;
            }
            if (faction == null || PendingFaction == null || PendingFaction != faction)
            {
                return false;
            }
            if (PendingTick < 0 || GenTicks.TicksGame - PendingTick > 60000)
            {
                return false;   // 1 天窗口
            }
            // 落点只作参考：原版把 LordJob.siegeSpot 存进 LordToilData_Siege.siegeCenter，
            // 正常一致；万一不一致也照放（否则又会退回"永远不生效"），但记一条日志。
            if (PendingSpot.IsValid && !center.InHorDistOf(PendingSpot, 3f))
            {
                Log.Warning("[冰鹅族] 低温围攻：蓝图落点与记录不一致（记录 " + PendingSpot
                    + "，实际 " + center + "），仍按低温围攻处理。");
            }
            return true;
        }

        // 这场围攻是不是我们放的（派系 + 地图 + 1 天时间窗）
        public static bool IsOurSiege(Lord lord)
        {
            if (lord == null || SiegeFaction == null || SiegeMap == null || PlacedTick < 0)
            {
                return false;
            }
            if (lord.faction != SiegeFaction)
            {
                return false;
            }
            if (lord.lordManager == null || lord.lordManager.map != SiegeMap)
            {
                return false;
            }
            return GenTicks.TicksGame - PlacedTick < 60000;
        }

        // 当前这场袭击是不是低温围攻（用我们之前记录的策略名）
        public static bool IsCryoSiegeStrategy
        {
            get { return BinguinRaidContext.StrategyDefName == StrategyDefName; }
        }
    }

    // ============================================================
    // ★★ 低温围攻专属 LordJob（2026-09 根治做法）★★
    //   为什么要专属类型：蓝图是在 LordToil_Siege.Init 里放的，那时
    //   BinguinRaidContext 的策略上下文早被 EndRaid() 清空（见下面长注释），
    //   用「策略名」识别必然失败。而 LordJob 的【类型】是一路带到最后、
    //   而且随存档序列化的 → 用类型识别 100% 可靠。
    //   LordJob_Siege 的 (Faction, IntVec3, float) 构造器是 public，
    //   CreateGraph 继承原版（围攻击器/待机/建炮等行为全不变）。
    // ============================================================
    public class LordJob_BinguinCryoSiege : LordJob_Siege
    {
        // 存档反序列化需要无参构造（原版所有 LordJob 子类同款写法）
        public LordJob_BinguinCryoSiege()
        {
        }

        public LordJob_BinguinCryoSiege(Faction faction, IntVec3 siegeSpot, float blueprintPoints)
            : base(faction, siegeSpot, blueprintPoints)
        {
        }
    }

    // ---------- 0) 识别：围攻 toil 初始化时判断"这是不是我们的低温围攻" ----------
    //   时序：LordToil_Siege.Init() → SiegeBlueprintPlacer.PlaceBlueprints(...)
    //   所以在这里（Init 的 prefix）把标志置好，蓝图前缀立刻就能读到。
    public static class Patch_BinguinCryoSiegeToilInit
    {
        public static void Prefix(LordToil_Siege __instance)
        {
            try
            {
                Lord lord = __instance != null ? __instance.lord : null;
                BinguinCryoSiegeContext.ToilIsCryoSiege =
                    lord != null && lord.LordJob is LordJob_BinguinCryoSiege;
            }
            catch (Exception e)
            {
                BinguinCryoSiegeContext.ToilIsCryoSiege = false;
                Log.Warning("[冰鹅族] 低温围攻 toil 识别异常：" + e.Message);
            }
        }
    }

    // ---------- 0b) 低温围攻不掉迫击炮弹 ----------
    //   原版 LordToil_Siege.LordToilTick 每 500 tick 统计"围攻中心半径 20 内的炮弹数"：
    //       if (shellCount < 4) DropSupplies(TryFindRandomShellDef(Turret_Mortar,...), 6);
    //   （IL 实证，硬编码 4/6）——我们的阵地没有迫击炮，所以这个判定永远成立，
    //   于是炮弹一遍遍地往下掉（用户实测现象）。加固炮管同理。
    //   → 直接拦 DropSupplies：只对【我们的低温围攻】屏蔽炮弹与加固炮管，食物照常。
    public static class Patch_BinguinCryoSiegeNoShells
    {
        public static bool Prefix(LordToil_Siege __instance, ThingDef thingDef)
        {
            try
            {
                if (thingDef == null || __instance == null)
                {
                    return true;
                }
                Lord lord = __instance.lord;
                if (lord == null || !(lord.LordJob is LordJob_BinguinCryoSiege))
                {
                    return true;   // 原版围攻 / 其它策略：不干预
                }
                if (thingDef.IsShell || thingDef == ThingDefOf.ReinforcedBarrel)
                {
                    return false;  // 低温围攻不需要炮弹，也不需要加固炮管
                }
                return true;       // 食物等照常投放
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    // 策略 worker：仍用原版围攻逻辑，只是加一道"仅冰鹅族可用"的门
    public class RaidStrategyWorker_BinguinCryoSiege : RaidStrategyWorker_Siege
    {
        // ★ 2026-09 用户定稿（第三版）：【就近原则】
        //   · 不再主动往外推：原版围攻已经选好了离他们入境方向最近的边缘点
        //   · 只做两件事：(1) 把点夹进"离边界 10 格"的可用区内（那圈不让造东西），
        //                   (2) 若该点放不下冷冻器，就近螺旋找最近一个能放下的点
        private const int EdgeMargin = 10;
        private static readonly System.Reflection.FieldInfo SpotField =
            AccessTools.Field(typeof(LordJob_Siege), "siegeSpot");
        // 换 LordJob 类型时要把原版算好的另外两个值一并搬过去
        private static readonly System.Reflection.FieldInfo BlueprintPointsField =
            AccessTools.Field(typeof(LordJob_Siege), "blueprintPoints");
        private static readonly System.Reflection.FieldInfo FactionField =
            AccessTools.Field(typeof(LordJob_Siege), "faction");

        protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
        {
            // ★★★ 2026-09 历史：这里曾经把低温围攻【全队】的建造技能强抬到 8 ★★★
            //   当时的结论链（全部 IL 实证）：
            //     · GenConstruct.CanConstruct 会检查
            //       pawn.skills(Construction).Level >= t.def.constructionSkillPrerequisite，
            //       而冷冻器蓝图一开始门槛写的是 **6**；
            //     · 原版 LordToil_Siege.SetAsBuilder 只把技能补到
            //       max(沙袋 0, 迫击炮 0) = 0 —— 原版营地本来就没有技能门槛，
            //       所以这个坑原版永远碰不到；
            //     · "谁当建造者"是原版从【全队】里挑的（CanBeBuilder 只看建造/灭火
            //       有没有被背景故事禁用），不挑我们的工兵 → 3×3 冷冻器永远等不到人。
            //   → 真凶是【门槛 6】，不是技能本身。门槛已经在 Defs/CryoSiege_Binguin.xml
            //     里降到 **0**（与沙袋/迫击炮一致），所以这里强抬技能纯属多余。
            //   ★ 2026-09 用户定稿：**删掉强抬技能**——袭击者全队建造 8 级看着很假
            //     （选中任意一个民兵都是 8 级），而且门槛为 0 之后本来就人人能建。
            //     代价：低技能建造者速度慢（技能 0 的建造速度只有一半左右），
            //     冷冻器（7500 工作量）会盖得比原来久 → 玩家准备时间反而更长，可接受；
            //     真要调快就该调 WorkToBuild，而不是偷偷改袭击者的技能。

            LordJob baseJob = base.MakeLordJob(parms, map, pawns, raidSeed);
            // ★ 把原版算好的 LordJob_Siege 换成【我们的子类】：
            //   落点/蓝图点数直接沿用原版的计算结果（反射读三个字段），
            //   这样既不改原版选点逻辑，又能用类型识别低温围攻。
            LordJob job = baseJob;
            try
            {
                LordJob_Siege siegeJob = baseJob as LordJob_Siege;
                if (siegeJob != null && !(siegeJob is LordJob_BinguinCryoSiege))
                {
                    object spotObj = SpotField != null ? SpotField.GetValue(siegeJob) : null;
                    object pointsObj = BlueprintPointsField != null ? BlueprintPointsField.GetValue(siegeJob) : null;
                    object factionObj = FactionField != null ? FactionField.GetValue(siegeJob) : null;
                    if (spotObj is IntVec3 && pointsObj is float && factionObj is Faction)
                    {
                        job = new LordJob_BinguinCryoSiege((Faction)factionObj, (IntVec3)spotObj, (float)pointsObj);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[冰鹅族] 低温围攻 LordJob 包装失败（退回原版 LordJob）：" + ex.Message);
            }
            // ★ 兜底记录（若下面修正了落点会覆盖）
            if (job != null && map != null && parms != null)
            {
                BinguinCryoSiegeContext.MarkPendingSiege(map, parms.faction, IntVec3.Invalid);
                BinguinCryoSiegeContext.MarkPlaced(parms.faction, map);
            }
            try
            {
                if (job == null || map == null || SpotField == null)
                {
                    return job;
                }
                IntVec3 spot = (IntVec3)SpotField.GetValue(job);
                if (!spot.IsValid)
                {
                    return job;
                }
                int minX = EdgeMargin;
                int maxX = map.Size.x - 1 - EdgeMargin;
                int minZ = EdgeMargin;
                int maxZ = map.Size.z - 1 - EdgeMargin;
                // (1) 最小位移夹进可用区（就近原则：只在越界时才挪）
                IntVec3 clamped = new IntVec3(
                    UnityEngine.Mathf.Clamp(spot.x, minX, maxX), 0,
                    UnityEngine.Mathf.Clamp(spot.z, minZ, maxZ));
                // (2) 就近找一个能放下大气冷冻器(3x3)的点
                IntVec3 target = FindNearestViableSpot(clamped, map, minX, maxX, minZ, maxZ);
                if (target.IsValid && target != spot)
                {
                    SpotField.SetValue(job, target);
                    Log.Message("[冰鹅族] 低温围攻选点（就近原则）：" + spot + " → " + target);
                }
                // ★ 记录最终落点（PlaceBlueprints 收到的就是这个值）
                if (target.IsValid && parms != null)
                {
                    BinguinCryoSiegeContext.MarkPendingSiege(map, parms.faction, target);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[冰鹅族] 低温围攻选点修正失败: " + ex.Message);
            }
            return job;
        }

        // 从候选点开始由近到远找第一个能放下冷冻器的位置（只在小范围内找，避免绕远路）
        private static IntVec3 FindNearestViableSpot(IntVec3 center, Map map, int minX, int maxX, int minZ, int maxZ)
        {
            ThingDef freezer = DefDatabase<ThingDef>.GetNamedSilentFail(BinguinCryoSiegeContext.FreezerDefName);
            if (freezer == null)
            {
                return center;
            }
            if (CanHost(freezer, center, map))
            {
                return center;
            }
            for (int radius = 2; radius <= 18; radius += 2)
            {
                for (int dx = -radius; dx <= radius; dx += 2)
                {
                    for (int dz = -radius; dz <= radius; dz += 2)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dz) != radius)
                        {
                            continue;
                        }
                        IntVec3 c = new IntVec3(
                            UnityEngine.Mathf.Clamp(center.x + dx, minX, maxX), 0,
                            UnityEngine.Mathf.Clamp(center.z + dz, minZ, maxZ));
                        if (CanHost(freezer, c, map))
                        {
                            return c;
                        }
                    }
                }
            }
            return center;
        }

        private static bool CanHost(ThingDef def, IntVec3 c, Map map)
        {
            try
            {
                return c.InBounds(map) && GenConstruct.CanPlaceBlueprintAt(def, c, Rot4.North, map).Accepted;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
        {
            if (parms == null || parms.faction == null || parms.faction.def == null)
            {
                return false;
            }
            // 低温围攻是本 mod 的专属打法，只有冰鹅派系能用（否则海盗也会来搭我们的冷冻器）
            // ★ 2026-09：冰鹅族拆成和平派 / 敌对派两个 FactionDef，两边都能用。
            if (!BinguinFactions.IsBinguinFaction(parms.faction))
            {
                return false;
            }
            // ★ 2026-09 用户要求：低温围攻不能出现在【友方支援】里。
            //   IL 实证：原版 RaidEnemy 与 RaidFriendly 走同一套策略选择
            //   （DefDatabase<RaidStrategyDef>.AllDefs.Where(CanUseStrategy)
            //    + TryRandomElementByWeight）→ 盟友来"支援"时也会抽到本策略。
            //   所以这里直接要求：这一场必须是【敌对】袭击。
            Faction player = Faction.OfPlayer;
            if (player == null || !parms.faction.HostileTo(player))
            {
                return false;
            }
            return base.CanUseWith(parms, groupKind);
        }
    }

    // ---------- 1) 换掉围攻蓝图：大气冷冻器 + 掩体沙袋（2026-09 去掉自动加农炮） ----------
    public static class BinguinCryoSiegeBlueprints
    {
        // ★ 2026-09：删掉从未使用的 points 形参（调用点同步改）
        public static IEnumerable<Blueprint_Build> Place(IntVec3 center, Map map, Faction faction)
        {
            List<Blueprint_Build> list = new List<Blueprint_Build>();
            if (map == null || faction == null)
            {
                return list;
            }
            // ★★★ 2026-09 实测定论：冷冻器【绝对不能放在部队集合点上】★★★
            //   实测证据（同一份代码只改这一处）：
            //     · 冷冻器放集结点正中心 → 2.8 游戏小时里框架数恒为 0，没人造；
            //     · 冷冻器离开集结点（哪怕只偏 8 格）→ 立刻建起来了。
            //   机制：LordToil_Siege 把 40+ 袭击者的集合点（duty focus）就设在
            //   Data.siegeCenter 上；工兵要干活必须能站到冷冻器旁边的空格
            //   （GenConstruct.CanTouchTargetFromValidCell → 原版逐格找"好落脚点"），
            //   而那一圈格子永远挤满自己人 → 建造工作给不出来。
            //   沙袋是 1×1、又散在四周，所以不受影响。
            //   ⚠ 之前我一度以为"小人站在蓝图上会挡建造"（那个具体假设确实被 IL 否掉了），
            //     就撤掉了偏移 —— 结果立刻又没人造了。
            //     教训：别用一个子假设的证伪去推翻实测结论。
            //
            //   ★ 2026-09 用户定稿布局（第七轮）：
            //     集合点仍是 16×16 沙袋环的中心（部队站位不再"歪 8 格"），
            //     大气冷冻器【挪到环外面、贴着沙袋外墙】：
            //         地图中心 …… [ 16×16 阵地（部队在此集合） ] 冷冻器
            //     既保住了"冷冻器远离集合点"（工兵挤得进去、能建造），
            //     又让沙袋环相对部队是正的。
            IntVec3 gather = center;                 // 部队集合点 = 沙袋环中心（原版 LordToil_Siege 用它）
            int half = RingSide / 2;                 // 16/2 = 8
            int x0 = gather.x - half;
            int x1 = gather.x + half - 1;            // 共 RingSide 格
            int z0 = gather.z - half;
            int z1 = gather.z + half - 1;

            // 1) 大气冷冻器：贴着沙袋外墙、背离地图中心的那一侧（3×3）
            ThingDef freezer = DefDatabase<ThingDef>.GetNamedSilentFail(BinguinCryoSiegeContext.FreezerDefName);
            IntVec3 freezerSpot = IntVec3.Invalid;
            bool freezerPlaced = false;
            if (freezer != null)
            {
                freezerSpot = FindFreezerSpotOutsideRing(freezer, gather, map, x0, x1, z0, z1);
                if (freezerSpot.IsValid)
                {
                    Blueprint_Build bp = TryPlace(freezer, freezerSpot, map, faction);
                    if (bp != null)
                    {
                        list.Add(bp);
                        freezerPlaced = true;
                    }
                }
                Log.Message("[冰鹅族] 低温围攻：冷冻器落点 " + freezerSpot
                    + "（沙袋环中心/集合点 " + gather + "，环外贴墙放置，"
                    + (freezerPlaced ? "蓝图已放" : "放不下，本场没有冷冻器") + "）");
            }
            // ★★ 2026-09 实测第四轮：蓝图放下后【我们直接把建材放到阵地上】★★
            //   原版围攻的建材靠 LordToil_Siege.Init 里的"空投补给"环节送
            //   （DropPodUtility.DropThingsNear，还会把物资标记为 forbidden）。
            //   实测：1×1 的沙袋（只要布料）建起来了，3×3 的冷冻器（要钢/玻璃钢/
            //   高级零部件）迟迟等不到材料 → 交付材料的工作给不出来 → 永远停在蓝图。
            //   这里不再依赖那条链路：蓝图放好后立刻就地生成所需建材
            //   （不带 forbidden），保证"交付材料 → 框架 → 建成"能跑通。
            if (freezerPlaced && freezerSpot.IsValid && freezer != null && freezer.costList != null)
            {
                int spawnedKinds = 0;
                for (int i = 0; i < freezer.costList.Count; i++)
                {
                    ThingDefCountClass cost = freezer.costList[i];
                    if (cost == null || cost.thingDef == null || cost.count <= 0)
                    {
                        continue;
                    }
                    Thing mat = ThingMaker.MakeThing(cost.thingDef, null);
                    if (mat == null)
                    {
                        continue;
                    }
                    mat.stackCount = cost.count;
                    if (GenPlace.TryPlaceThing(mat, freezerSpot, map, ThingPlaceMode.Near, null))
                    {
                        spawnedKinds++;
                    }
                }
                Log.Message("[冰鹅族] 低温围攻：已就地投放建材 " + spawnedKinds
                    + " 种（不依赖原版空投补给，避免工兵等不到材料）。");
            }
            int sandbagsPlaced = 0;
            // ★ 2026-09 用户要求：去掉两台自动加农炮（不需要了）——
            //   阵地只剩 大气冷冻器 + 掩体沙袋。
            // 2) 掩体沙袋：围【部队集合点】一圈【16×16 方环】，四角各去掉 7 格
            //    （角点 1 格 + 沿两条边各 3 格 = 1+2+2+2 = 7，形成斜切开口）
            //    环中心 = 集合点 gather（冷冻器在环外）；边界 x0/x1/z0/z1 上面已算好
            ThingDef sandbag = DefDatabase<ThingDef>.GetNamedSilentFail("Sandbags");
            if (sandbag != null)
            {
                for (int x = x0; x <= x1; x++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        bool onRing = (x == x0 || x == x1 || z == z0 || z == z1);
                        if (!onRing || IsCornerCut(x, z, x0, x1, z0, z1))
                        {
                            continue;
                        }
                        // ★★ 必须给材质：沙袋是 stuffable（madeFromStuff），
                        //   传 null 会产生 "Cannot get AdjustedCostList for Sandbags with
                        //   null Stuff" + "Frame_Sandbags is madeFromStuff but stuff=null"
                        //   的报错刷屏，而且【工人根本建不起来】。
                        //   原版 SiegeBlueprintPlacer 用的是 ThingDefOf.Cloth（IL 实证）。
                        Blueprint_Build bp = TryPlace(sandbag, new IntVec3(x, 0, z), map, faction,
                            ThingDefOf.Cloth);
                        if (bp != null)
                        {
                            list.Add(bp);
                            sandbagsPlaced++;
                        }
                    }
                }
            }
            BinguinCryoSiegeContext.MarkPlaced(faction, map);
            // ★ 诊断：分开报"冷冻器放没放下"和"沙袋放了几个"——
            //   以前只报总数，看不出冷冻器是不是被地形/占位挡掉了。
            Log.Message("[冰鹅族] 低温围攻阵地蓝图：" + list.Count + " 个（大气冷冻器 "
                + (freezerPlaced ? "1" : "0（环外四面都放不下）")
                + " + 沙袋 " + sandbagsPlaced + "/" + (RingSide * 4 - 4 - 4 * 7)
                + "，环中心 " + gather + "，冷冻器 " + freezerSpot + "）");
            return list;
        }

        // 沙袋方环参数（用户定稿：16×16，四角各去掉 7 格）
        private const int RingSide = 16;
        private const int CornerCutDepth = 3;   // 角点 + 每条边各 3 格 = 7 格

        // ★ 冷冻器落点：优先"背离地图中心的那一侧、贴着沙袋外墙"
        //   （用户定稿：16×16 阵地卡在地图中心和冷冻器中间，冷冻器贴着沙袋放），
        //   这一侧放不下就换另外三面，四面都不行再就近螺旋兜底。
        private static IntVec3 FindFreezerSpotOutsideRing(ThingDef def, IntVec3 gather, Map map,
            int x0, int x1, int z0, int z1)
        {
            IntVec3 away = gather - map.Center;
            // 四个方向按"离地图中心越远越优先"排序（主轴向优先）
            IntVec3[] dirs = new IntVec3[4];
            int n = 0;
            if (Math.Abs(away.x) >= Math.Abs(away.z))
            {
                if (away.x >= 0) { dirs[n++] = new IntVec3(1, 0, 0); dirs[n++] = new IntVec3(-1, 0, 0); }
                else { dirs[n++] = new IntVec3(-1, 0, 0); dirs[n++] = new IntVec3(1, 0, 0); }
                if (away.z >= 0) { dirs[n++] = new IntVec3(0, 0, 1); dirs[n++] = new IntVec3(0, 0, -1); }
                else { dirs[n++] = new IntVec3(0, 0, -1); dirs[n++] = new IntVec3(0, 0, 1); }
            }
            else
            {
                if (away.z >= 0) { dirs[n++] = new IntVec3(0, 0, 1); dirs[n++] = new IntVec3(0, 0, -1); }
                else { dirs[n++] = new IntVec3(0, 0, -1); dirs[n++] = new IntVec3(0, 0, 1); }
                if (away.x >= 0) { dirs[n++] = new IntVec3(1, 0, 0); dirs[n++] = new IntVec3(-1, 0, 0); }
                else { dirs[n++] = new IntVec3(-1, 0, 0); dirs[n++] = new IntVec3(1, 0, 0); }
            }
            for (int i = 0; i < n; i++)
            {
                IntVec3 c = FreezerSpotHuggingWall(dirs[i], gather, x0, x1, z0, z1);
                if (CanHostFreezer(def, c, map))
                {
                    return c;
                }
            }
            IntVec3 spiral = FindNearbyFreezerSpot(def, gather, map);
            if (spiral.IsValid)
            {
                Log.Message("[冰鹅族] 低温围攻：环外四面都放不下冷冻器，就近挪到 " + spiral + "。");
            }
            return spiral;
        }

        // 某一侧的"贴墙"落点：冷冻器 3×3 以格子为中心放置
        // （GenAdj.OccupiedRect：min = center - size/2），
        // 所以中心放在"墙外第 2 格"时，3×3 的靠内那一列正好紧贴沙袋墙。
        private static IntVec3 FreezerSpotHuggingWall(IntVec3 dir, IntVec3 gather,
            int x0, int x1, int z0, int z1)
        {
            if (dir.x > 0)
            {
                return new IntVec3(x1 + 2, 0, gather.z);
            }
            if (dir.x < 0)
            {
                return new IntVec3(x0 - 2, 0, gather.z);
            }
            if (dir.z > 0)
            {
                return new IntVec3(gather.x, 0, z1 + 2);
            }
            return new IntVec3(gather.x, 0, z0 - 2);
        }

        // 一个格子是否落在"角部开口"里（4 个角，曼哈顿距离 ≤ CornerCutDepth 的环上格）
        private static bool IsCornerCut(int x, int z, int x0, int x1, int z0, int z1)
        {
            for (int i = 0; i < 4; i++)
            {
                int cx = ((i & 1) == 0) ? x0 : x1;
                int cz = ((i & 2) == 0) ? z0 : z1;
                if (Math.Abs(x - cx) + Math.Abs(z - cz) <= CornerCutDepth)
                {
                    return true;
                }
            }
            return false;
        }

        // 环外都放不下时的就近兜底：由近及远螺旋找第一个能放下冷冻器的点
        // ★ 起点定在【环外】（RingSide/2 + 2），避免兜底时又把冷冻器塞回部队集合点
        private static IntVec3 FindNearbyFreezerSpot(ThingDef def, IntVec3 center, Map map)
        {
            for (int radius = RingSide / 2 + 2; radius <= 26; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dz) != radius)
                        {
                            continue;
                        }
                        IntVec3 c = new IntVec3(center.x + dx, 0, center.z + dz);
                        if (CanHostFreezer(def, c, map))
                        {
                            return c;
                        }
                    }
                }
            }
            return IntVec3.Invalid;
        }

        // 该点能否放下这个建筑（与 worker 里的 CanHost 同款；蓝图放置阶段用）
        private static bool CanHostFreezer(ThingDef def, IntVec3 c, Map map)
        {
            try
            {
                return def != null && c.InBounds(map)
                    && GenConstruct.CanPlaceBlueprintAt(def, c, Rot4.North, map).Accepted;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Blueprint_Build TryPlace(ThingDef def, IntVec3 center, Map map, Faction faction,
            ThingDef stuff = null)
        {
            try
            {
                if (!center.InBounds(map))
                {
                    return null;
                }
                // ★ 有材质时必须把 stuff 一并传给"能否放置"检查（原版同款）
                if (!GenConstruct.CanPlaceBlueprintAt(def, center, Rot4.North, map,
                        stuffDef: stuff).Accepted)
                {
                    return null;
                }
                return GenConstruct.PlaceBlueprintForBuild(def, center, map, Rot4.North, faction, stuff);
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 放置低温围攻蓝图失败（" + def.defName + "）: " + e.Message);
                return null;
            }
        }
    }

    // 前缀：只对低温围攻生效，其余（含原版 Siege）原样走原版
    public static class Patch_BinguinCryoSiegeBlueprints
    {
        public static bool Prefix(IntVec3 placeCenter, Map map, Faction placeFaction, float points,
            ref IEnumerable<Blueprint_Build> __result)
        {
            try
            {
                // ★ 2026-09 修复：改用"待放置阵地"记录识别（策略上下文此时已被清空，
                //   见 BinguinCryoSiegeContext 顶部注释）。策略名仍作为第二条判据。
                // 识别顺序：① LordJob 类型（最可靠，随存档）② 阵地记录 ③ 策略名
                bool byToil = BinguinCryoSiegeContext.ToilIsCryoSiege;
                bool pending = !byToil && BinguinCryoSiegeContext.IsPendingSiege(map, placeFaction, placeCenter);
                if (!byToil && !pending && !BinguinCryoSiegeContext.IsCryoSiegeStrategy)
                {
                    return true;
                }
                if (placeFaction == null || placeFaction.def == null
                    || !BinguinFactions.IsBinguinFaction(placeFaction))
                {
                    return true;
                }
                __result = BinguinCryoSiegeBlueprints.Place(placeCenter, map, placeFaction);
                Log.Message("[冰鹅族] 低温围攻蓝图：已替换原版迫击炮阵地（落点 " + placeCenter
                    + "，识别方式 " + (byToil ? "LordJob 类型" : (pending ? "阵地记录" : "策略名")) + "）");
                return false;
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 低温围攻蓝图替换失败，回退原版: " + e.Message);
                return true;
            }
        }
    }

    // ---------- 1c) 建造者限定为【工兵】（2026-09 用户要求） ----------
    //   原版 LordToil_Siege.UpdateAllDuties 从【全队】里随机挑建造者（IL 实证）：
    //     · 候选池 = CanBeBuilder(p)，实现只有两行：
    //         if (p.WorkTypeIsDisabled(Construction)) return false;
    //         if (p.WorkTypeIsDisabled(Firefighter))  return false;
    //         return true;
    //       —— 【不看技能、也不看兵种】；
    //     · 建造者人数 = RoundToInt(全队人数 × desiredBuilderFraction)，
    //       BuilderCountFraction = 0.25~0.40（IL 实证）→ 40 人的围攻就是 10~16 个建造者，
    //       全部是从民兵/士兵里随手挑的，跟"工兵"这个兵种毫无关系。
    //   → 用户要求：让【指定的工兵】去建造。
    //
    //   ★ 两层实现（互为保险，结果一致，重复执行也无副作用）：
    //     ① Postfix CanBeBuilder：非工兵直接判 false —— 原版自己的分支就会把他们
    //        安排成 SetAsDefender，建造名额自然全落进工兵池（最优雅，但
    //        CanBeBuilder 只有约 10 条 IL，**存在被 JIT 内联导致补丁不生效的风险**）；
    //     ② Postfix UpdateAllDuties：等原版分完职责后做一次"重新分配"——
    //        把非工兵的建造职责撤掉、换工兵补满同样的名额。UpdateAllDuties 是
    //        public virtual 的大方法，不可能被内联，**这条一定跑得到**。
    //        ①生效时这里自然是空操作。
    //
    //   ★ 兜底：场上一个能用的工兵都没有（工兵全灭 / 这场本来没派工兵）→ 不插手，
    //     完全保留原版结果。工兵不是每场袭击都有（RaidComposition_Binguin 按策略决定），
    //     所以这条兜底是必需的，否则会出现"没人建造、阵地永远盖不完"。
    //
    //   ★ 下面的 SetAsBuilder / SetAsDefender 是按 IL 逐条复刻原版私有方法的
    //     （原版这两个方法是 private，改派职责只能自己写；RimWorld 1.6 已冻结，
    //      复刻风险很低）。SetAsBuilder 的关键副作用：它会把建造者**除建造外
    //      的所有工作类型全部禁用**（原版就这么干的），所以撤销建造职责时
    //      要顺手 EnableAndInitialize() 把工作设置还原。
    public static class Patch_BinguinCryoSiegeBuilders
    {
        // ---- ① 资格层：非工兵不给建造资格（见类注释里的内联风险说明） ----
        public static void CanBeBuilderPostfix(LordToil_Siege __instance, Pawn p, ref bool __result)
        {
            try
            {
                if (!__result || p == null)
                {
                    return;
                }
                Lord lord = __instance != null ? __instance.lord : null;
                if (!BinguinCryoSiegeContext.IsOurSiege(lord) || IsSapper(p))
                {
                    return;
                }
                if (!HasUsableSapper(lord))
                {
                    return;                       // 没工兵可用 → 交回原版
                }
                __result = false;
            }
            catch (Exception)
            {
                // 出错按原版结果走，绝不影响袭击
            }
        }

        // ---- ② 兜底层：原版分完职责后重新分配（一定跑得到） ----
        public static void UpdateAllDutiesPostfix(LordToil_Siege __instance)
        {
            try
            {
                Lord lord = __instance != null ? __instance.lord : null;
                if (!BinguinCryoSiegeContext.IsOurSiege(lord))
                {
                    return;
                }
                List<Pawn> pawns = lord.ownedPawns;
                if (pawns == null || pawns.Count == 0 || DataOf(__instance) == null)
                {
                    return;
                }
                // 原版本次到底挑了几个建造者（照抄这个名额，不自己另算）
                int wantBuilders = 0;
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (HasBuildDuty(pawns[i]))
                    {
                        wantBuilders++;
                    }
                }
                // 一个建造者都没有 = 还在"落地后全员防御"阶段（ticksInToil < 450）
                // 或者原版没找到合格人选 → 不插手
                if (wantBuilders == 0 || !HasUsableSapper(lord))
                {
                    return;
                }
                int demoted = 0;
                int promoted = 0;
                int sapperBuilders = 0;
                // ① 非工兵的建造职责全部撤销
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (HasBuildDuty(p) && !IsSapper(p))
                    {
                        SetAsDefender(__instance, p);
                        demoted++;
                    }
                }
                // ② 数一下还剩几个工兵建造者
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (HasBuildDuty(pawns[i]) && IsSapper(pawns[i]))
                    {
                        sapperBuilders++;
                    }
                }
                // ③ 用工兵把名额补满
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (sapperBuilders + promoted >= wantBuilders)
                    {
                        break;
                    }
                    Pawn p = pawns[i];
                    if (!IsSapper(p) || HasBuildDuty(p) || p.Dead || p.Downed)
                    {
                        continue;
                    }
                    if (p.WorkTypeIsDisabled(WorkTypeDefOf.Construction)
                        || p.WorkTypeIsDisabled(WorkTypeDefOf.Firefighter))
                    {
                        continue;
                    }
                    SetAsBuilder(__instance, p);
                    promoted++;
                }
                if (demoted > 0 || promoted > 0)
                {
                    Log.Message("[冰鹅族] 低温围攻：建造者已限定为工兵 —— 撤掉 " + demoted
                        + " 个非工兵，补上 " + promoted + " 个工兵（本场建造名额 " + wantBuilders + "）。");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 低温围攻建造者重分配失败（保留原版分工）：" + e.Message);
            }
        }

        public static bool IsSapper(Pawn p)
        {
            return p != null && p.kindDef != null
                && p.kindDef.defName == BinguinRaidKinds.SapperName;
        }

        private static bool HasBuildDuty(Pawn p)
        {
            return p != null && !p.Dead && p.mindState != null
                && p.mindState.duty != null && p.mindState.duty.def == DutyDefOf.Build;
        }

        // 与 CanBeBuilder 同款判定（只看建造/灭火有没有被背景故事禁用），
        // 外加"没死没倒地"——倒地的工兵干不了活，不该算作"还有工兵"。
        private static bool HasUsableSapper(Lord lord)
        {
            List<Pawn> pawns = lord != null ? lord.ownedPawns : null;
            if (pawns == null)
            {
                return false;
            }
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (!IsSapper(p) || p.Dead || p.Downed)
                {
                    continue;
                }
                if (!p.WorkTypeIsDisabled(WorkTypeDefOf.Construction)
                    && !p.WorkTypeIsDisabled(WorkTypeDefOf.Firefighter))
                {
                    return true;
                }
            }
            return false;
        }

        // ★ 原版 LordToil_Siege.Data 是【private 属性】（反射实证），
        //   但基类 LordToil.data 是 public 字段 → 从基类字段取再转型。
        private static LordToilData_Siege DataOf(LordToil_Siege toil)
        {
            return toil != null ? toil.data as LordToilData_Siege : null;
        }

        // ---- 下面两个是按 IL 逐条复刻原版 LordToil_Siege 的私有方法 ----
        // 原版 SetAsDefender 只有两行有效逻辑
        private static void SetAsDefender(LordToil_Siege toil, Pawn p)
        {
            LordToilData_Siege data = DataOf(toil);
            p.mindState.duty = new PawnDuty(DutyDefOf.Defend, data.siegeCenter, -1f);
            p.mindState.duty.radius = data.baseRadius;
            // 原版 SetAsBuilder 会把除建造外的工作全禁用；这里退回防御就还原回去
            if (p.workSettings != null)
            {
                p.workSettings.EnableAndInitialize();
            }
        }

        // 原版 SetAsBuilder：建造职责 + 建造技能补到 max(沙袋, 迫击炮) 门槛
        // + 只留建造一个工作类型（priority 1），其余全 Disable
        private static void SetAsBuilder(LordToil_Siege toil, Pawn p)
        {
            LordToilData_Siege data = DataOf(toil);
            p.mindState.duty = new PawnDuty(DutyDefOf.Build, data.siegeCenter, -1f);
            p.mindState.duty.radius = data.baseRadius;
            int minSkill = Math.Max(ThingDefOf.Sandbags.constructionSkillPrerequisite,
                ThingDefOf.Turret_Mortar.constructionSkillPrerequisite);
            if (p.skills != null)
            {
                p.skills.GetSkill(SkillDefOf.Construction).EnsureMinLevelWithMargin(minSkill);
            }
            if (p.workSettings == null)
            {
                return;
            }
            p.workSettings.EnableAndInitialize();
            List<WorkTypeDef> allWork = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            for (int i = 0; i < allWork.Count; i++)
            {
                if (allWork[i] == WorkTypeDefOf.Construction)
                {
                    p.workSettings.SetPriority(allWork[i], 1);
                }
                else
                {
                    p.workSettings.Disable(allWork[i]);
                }
            }
        }
    }

    // ---------- 2) 冲锋时机：减员 30% 或工兵全灭 ----------
    public static class Patch_BinguinCryoSiegeAssault
    {
        private static readonly Dictionary<int, int> initialCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> sapperSeen = new Dictionary<int, int>();
        private static readonly HashSet<int> assaulted = new HashSet<int>();

        public static void Postfix(LordToil_Siege __instance)
        {
            try
            {
                Lord lord = __instance != null ? __instance.lord : null;
                if (!BinguinCryoSiegeContext.IsOurSiege(lord))
                {
                    return;
                }
                // ★ 诊断（2026-09）：每 2500 tick 报一次"本场有几个工兵（DutyDefOf.Build）
                //   以及阵地还剩多少蓝图/框架"，用来判断"没人建造"到底卡在哪一步。
                if (GenTicks.TicksGame % 5000 == 0)
                {
                    ReportBuilderStatus(lord);
                }
                int id = lord.loadID;
                // ★ 2026-09 性能：冲锋一旦判定过，这个 postfix 原来还会每 tick
                //   继续数一遍整队 pawn（围攻可能持续很久）。这里直接早退并
                //   清掉记账，之后每 tick 只剩一次字典查找。
                if (assaulted.Contains(id))
                {
                    initialCounts.Remove(id);
                    sapperSeen.Remove(id);
                    return;
                }
                List<Pawn> pawns = lord.ownedPawns;
                if (pawns == null || pawns.Count == 0)
                {
                    return;
                }
                int alive = 0;
                int sappersAlive = 0;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p == null)
                    {
                        continue;
                    }
                    bool ok = !p.Dead && !p.Downed;
                    if (ok)
                    {
                        alive++;
                    }
                    if (p.kindDef != null && p.kindDef.defName == BinguinRaidKinds.SapperName && ok)
                    {
                        sappersAlive++;
                    }
                }
                // ★ 2026-09 性能：同一 tick 内同一个 key 原来要查 3 次字典
                //   （ContainsKey + 赋值 + 取值）→ 合并成一次 TryGetValue。
                int initial;
                if (!initialCounts.TryGetValue(id, out initial))
                {
                    initial = pawns.Count;
                    initialCounts[id] = initial;
                }
                int seen;
                sapperSeen.TryGetValue(id, out seen);
                if (sappersAlive > seen)
                {
                    sapperSeen[id] = sappersAlive;
                    seen = sappersAlive;
                }
                bool lost30 = alive <= initial - Math.Max(1, (int)Math.Ceiling(initial * 0.3f));
                bool sappersWiped = seen > 0 && sappersAlive == 0;
                bool freezerDone = BinguinCryoSiegeContext.JustSelfDestructed;   // 冷冻器自毁满 24 小时
                if ((lost30 || sappersWiped || freezerDone) && !assaulted.Contains(id))
                {
                    assaulted.Add(id);
                    ForceAssault(lord, sappersWiped, freezerDone);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 低温围攻冲锋判定异常: " + e.Message);
            }
        }

        // 诊断：统计"正在执行建造职责"的工兵数量 + 场地上的蓝图/框架数
        private static void ReportBuilderStatus(Lord lord)
        {
            try
            {
                int builders = 0;
                int total = 0;
                int sappersAlive = 0;      // 场上可用工兵数（限定建造者的池子大小）
                int sapperBuilders = 0;    // 其中拿到建造职责的
                List<Pawn> pawns = lord.ownedPawns;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p == null || p.Dead || p.mindState == null || p.mindState.duty == null)
                    {
                        continue;
                    }
                    total++;
                    bool isSapper = Patch_BinguinCryoSiegeBuilders.IsSapper(p);
                    if (isSapper && !p.Downed)
                    {
                        sappersAlive++;
                    }
                    if (p.mindState.duty.def == DutyDefOf.Build)
                    {
                        builders++;
                        if (isSapper)
                        {
                            sapperBuilders++;
                        }
                    }
                }
                int blueprints = 0;
                int frames = 0;
                Map map = lord.Map;
                IntVec3 center = BinguinCryoSiegeContext.PendingSpot;
                ThingDef freezerDef = DefDatabase<ThingDef>.GetNamedSilentFail(
                    BinguinCryoSiegeContext.FreezerDefName);
                if (map != null && freezerDef != null)
                {
                    List<Thing> bpList = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
                    for (int i = 0; i < bpList.Count; i++)
                    {
                        if (bpList[i].def.entityDefToBuild == freezerDef)
                        {
                            blueprints++;
                        }
                    }
                    List<Thing> frameList = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
                    for (int i = 0; i < frameList.Count; i++)
                    {
                        if (frameList[i].def.entityDefToBuild == freezerDef)
                        {
                            frames++;
                        }
                    }
                }
                // ★ 深一层诊断：工兵当前到底在干什么 + 冷冻器蓝图的材料够不够
                int buildingNow = 0;
                int haulingNow = 0;
                int idlingNow = 0;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p == null || p.Dead || p.CurJob == null || p.CurJob.def == null)
                    {
                        idlingNow++;
                        continue;
                    }
                    if (p.CurJob.def == JobDefOf.FinishFrame
                        || p.CurJob.def == JobDefOf.DoBill)
                    {
                        buildingNow++;
                    }
                    else if (p.CurJob.def == JobDefOf.HaulToContainer
                        || p.CurJob.def == JobDefOf.HaulToCell
                        || p.CurJob.def == JobDefOf.HaulToTransporter)
                    {
                        haulingNow++;
                    }
                }
                // ★★ 逐条复现原版"能不能建造"的判定（WorkGiver_ConstructDeliverResources
                //   ToBlueprints.HasJobOnThing / GenConstruct.CanConstruct 的每一步），
                //   直接指名道姓说清楚卡在哪一条。
                string why = "";
                if (map != null && freezerDef != null)
                {
                    List<Thing> bps = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
                    for (int i = 0; i < bps.Count; i++)
                    {
                        if (bps[i].def.entityDefToBuild != freezerDef)
                        {
                            continue;
                        }
                        Thing target = bps[i];
                        Pawn probe = null;
                        for (int k = 0; k < pawns.Count; k++)
                        {
                            Pawn cand = pawns[k];
                            if (cand != null && !cand.Dead && cand.mindState != null
                                && cand.mindState.duty != null
                                && cand.mindState.duty.def == DutyDefOf.Build)
                            {
                                probe = cand;
                                break;
                            }
                        }
                        if (probe == null)
                        {
                            why = "Binguin_CryoSiege_01".Translate();
                            break;
                        }
                        bool canTouch = GenConstruct.CanTouchTargetFromValidCell(target, probe);
                        Thing blocker = GenConstruct.FirstBlockingThing(target, probe);
                        bool canHaul = GenConstruct.CanConstruct(target, probe,
                            WorkTypeDefOf.Construction, false, JobDefOf.HaulToContainer);
                        bool canFinish = GenConstruct.CanConstruct(target, probe,
                            WorkTypeDefOf.Construction, false, JobDefOf.FinishFrame);
                        int skill = probe.skills != null
                            ? probe.skills.GetSkill(SkillDefOf.Construction).Level : -1;
                        why = "Binguin_CryoSiege_02".Translate() + probe.LabelShort + "Binguin_CryoSiege_03".Translate() + skill + "）："
                            + "Binguin_CryoSiege_04".Translate() + canTouch
                            + "Binguin_CryoSiege_05".Translate() + (blocker != null ? blocker.LabelShort : (string)"Binguin_CryoSiege_06".Translate())
                            + "Binguin_CryoSiege_07".Translate() + canHaul
                            + "Binguin_CryoSiege_08".Translate() + canFinish;
                        break;
                    }
                }
                string matInfo = "Binguin_CryoSiege_09".Translate();
                if (map != null && freezerDef != null)
                {
                    List<Thing> groups = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
                    for (int i = 0; i < groups.Count; i++)
                    {
                        if (groups[i].def.entityDefToBuild != freezerDef)
                        {
                            continue;
                        }
                        Blueprint bpThing = groups[i] as Blueprint;
                        List<ThingDefCountClass> costs = bpThing != null ? bpThing.TotalMaterialCost() : null;
                        if (costs == null)
                        {
                            matInfo = "Binguin_CryoSiege_10".Translate();
                            break;
                        }
                        string s = "";
                        for (int c = 0; c < costs.Count; c++)
                        {
                            ThingDefCountClass cost = costs[c];
                            int nearby = 0;
                            List<Thing> all = map.listerThings.ThingsOfDef(cost.thingDef);
                            for (int k = 0; k < all.Count; k++)
                            {
                                if (all[k] != null && !all[k].Destroyed
                                    && all[k].Position.InHorDistOf(center, 30f))
                                {
                                    nearby += all[k].stackCount;
                                }
                            }
                            s += cost.thingDef.defName + " " + cost.count + "/" + nearby + "  ";
                        }
                        matInfo = "Binguin_CryoSiege_11".Translate() + s + "）";
                        break;
                    }
                }
                Log.Message("[冰鹅族] 低温围攻诊断：存活 " + total + " 人，建造职责 " + builders
                    + " 人（其中工兵 " + sapperBuilders + " / 场上工兵 " + sappersAlive
                    + "）" + (sappersAlive > 0 && sapperBuilders == 0
                        ? "  ★ 建造名额没落到工兵身上，说明限定没生效！" : "")
                    + "；正在建造 " + buildingNow + " / 搬运 " + haulingNow + " / 空闲 " + idlingNow
                    + "；冷冻器 蓝图 " + blueprints + " / 框架 " + frames + " " + matInfo);
                if (why.Length > 0)
                {
                    Log.Message("[冰鹅族] 低温围攻诊断" + why);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 低温围攻诊断异常：" + e.Message);
            }
        }

        private static void ForceAssault(Lord lord, bool sappersWiped, bool freezerDone)
        {
            if (lord.Graph == null || lord.Graph.lordToils == null)
            {
                return;
            }
            for (int i = 0; i < lord.Graph.lordToils.Count; i++)
            {
                LordToil toil = lord.Graph.lordToils[i];
                if (toil is LordToil_AssaultColony)
                {
                    lord.GotoToil(toil);
                    Messages.Message(freezerDone
                        ? "Binguin_CryoSiege_12".Translate()
                        : (sappersWiped
                            ? "Binguin_CryoSiege_13".Translate()
                            : "Binguin_CryoSiege_14".Translate()),
                        MessageTypeDefOf.ThreatBig, false);
                    Log.Message("[冰鹅族] 低温围攻转冲锋（工兵全灭=" + sappersWiped
                        + "，冷冻器自毁=" + freezerDone + "）");
                    return;
                }
            }
        }
    }

    // ---------- 3a) 降温/回暖 GameCondition ----------
    // ★ 2026-09 用户要求：冷冻器被摧毁后，气温要在 2 小时内自动恢复正常（渐变，不是瞬回）。
    //   继承原版 GameCondition_TemperatureOffset（tempOffset 字段驱动全图气温），
    //   把"每小时 -10℃"和"摧毁后 2 小时线性回到 0"都放进 Condition 里跑 ——
    //   因为建筑没了以后，只有 Condition 还活着能继续 tick。
    public class GameCondition_BinguinCryoFreeze : GameCondition_TemperatureOffset
    {
        private const float DegreesPerHour = 10f;
        private const float MinTemp = -273f;
        private const int RecoveryTicks = 5000;      // 2 小时 = 5000 tick

        // ★ 2026-09 优化：原字段名 startTick 会【隐藏】原版 RimWorld.GameCondition.startTick
        //   （编译告警 CS0108，且一旦本类里误用基类的 TicksPassed/Expired 就会读到
        //   基类那个字段 → 条件秒过期）。改名彻底避开同名遮蔽。
        private int cryoStartTick = -1;
        private int recoveryStartTick = -1;
        private float offsetAtRecoveryStart;

        // 冷冻器被摧毁/拆除时调用：开始 2 小时回暖
        public void StartRecovery()
        {
            if (recoveryStartTick < 0)
            {
                recoveryStartTick = GenTicks.TicksGame;
                offsetAtRecoveryStart = tempOffset;
                Log.Message("[冰鹅族] 大气冷冻器已失效，气温将在 2 小时内回升到正常。");
            }
        }

        public override void GameConditionTick()
        {
            base.GameConditionTick();
            if (cryoStartTick < 0)
            {
                cryoStartTick = GenTicks.TicksGame;
            }
            if (recoveryStartTick >= 0)
            {
                float t = (GenTicks.TicksGame - recoveryStartTick) / (float)RecoveryTicks;
                if (t >= 1f)
                {
                    tempOffset = 0f;
                    End();
                    return;
                }
                tempOffset = UnityEngine.Mathf.Lerp(offsetAtRecoveryStart, 0f, t);
                return;
            }
            float hours = (GenTicks.TicksGame - cryoStartTick) / 2500f;
            tempOffset = UnityEngine.Mathf.Max(MinTemp, -DegreesPerHour * hours);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref cryoStartTick, "cryoStartTick", -1, false);
            Scribe_Values.Look<int>(ref recoveryStartTick, "cryoRecoveryTick", -1, false);
            Scribe_Values.Look<float>(ref offsetAtRecoveryStart, "cryoRecoveryOffset", 0f, false);
        }
    }

    // ---------- 3) 大气冷冻器的降温 Comp ----------
    public class CompProperties_BinguinCryoFreezer : CompProperties
    {
        // ★ 2026-09 用户定稿：每小时降温 10℃，没有下限（最低压到绝对零度 -273℃）
        public float degreesPerHour = 10f;
        // 运转总时长 tick：60000 = 24 小时，到点自毁并逼冰鹅族总攻
        public int lifetimeTicks = 60000;
        // 每次续期时长 tick（比运转时长略长，靠 CompTickRare 续）
        public int conditionDurationTicks = 60000;

        public CompProperties_BinguinCryoFreezer()
        {
            compClass = typeof(CompBinguinCryoFreezer);
        }
    }

    public class CompBinguinCryoFreezer : ThingComp
    {
        private const string ConditionDefName = "Binguin_CryoFreeze";
        private static GameConditionDef cryoCondDef;
        private static bool cryoCondLookedUp;
        private GameCondition condition;
        // 开始运转的 tick（-1 = 还没开始）
        private int startTick = -1;

        private CompProperties_BinguinCryoFreezer Props
        {
            get { return (CompProperties_BinguinCryoFreezer)props; }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (startTick < 0)
            {
                startTick = GenTicks.TicksGame;
            }
            StartCooling(true);
        }

        // ★ 2026-09 修复：startTick（24 小时自毁倒计时起点）原先没存档，
        //   而 PostSpawnSetup 在读档时也会被调用 → 每次读档倒计时都从 0 重来，
        //   玩家反复存读档就永远等不到自毁/总攻。现在随档保存。
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<int>(ref startTick, "cryoFreezerStartTick", -1, false);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (startTick < 0)
            {
                startTick = GenTicks.TicksGame;
            }
            // 降温/回暖的斜坡全部由 GameCondition_BinguinCryoFreeze 负责，这里只保证它存在
            StartCooling(false);
            if (condition != null)
            {
                condition.Duration = Props.conditionDurationTicks;
            }
            // ★ 满 24 小时：自毁 + 逼冰鹅族总攻（气温随后 2 小时自动回升）
            if (GenTicks.TicksGame - startTick >= Props.lifetimeTicks)
            {
                Log.Message("[冰鹅族] 大气冷冻器运转满 24 小时，自毁并触发总攻。");
                BinguinCryoSiegeContext.SelfDestructTick = GenTicks.TicksGame;
                condition = null;
                if (parent != null && parent.Spawned)
                {
                    parent.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private void StartCooling(bool announce)
        {
            Map map = parent.Map;
            if (map == null || map.gameConditionManager == null)
            {
                return;
            }
            // ★ 2026-09 性能：CompTickRare 每 250 tick 调一次 StartCooling，
            //   原来每次都查一次 DefDatabase → 懒加载缓存（查过就不再查）。
            if (!cryoCondLookedUp)
            {
                cryoCondLookedUp = true;
                cryoCondDef = DefDatabase<GameConditionDef>.GetNamedSilentFail(ConditionDefName);
            }
            GameConditionDef def = cryoCondDef;
            if (def == null)
            {
                return;
            }
            // 已经有同类效果 → 只续时间
            List<GameCondition> active = map.gameConditionManager.ActiveConditions;
            for (int i = 0; i < active.Count; i++)
            {
                GameCondition_TemperatureOffset t0 = active[i] as GameCondition_TemperatureOffset;
                if (t0 != null && t0.def == def)
                {
                    t0.Duration = Props.conditionDurationTicks;
                    condition = t0;
                    return;
                }
            }
            GameCondition c = GameConditionMaker.MakeCondition(def, Props.conditionDurationTicks);
            GameCondition_TemperatureOffset t = c as GameCondition_TemperatureOffset;
            if (t != null)
            {
                // 起始偏移为 0，之后每小时累积 -degreesPerHour℃（CompTickRare 里推进）
                t.tempOffset = 0f;
            }
            map.gameConditionManager.RegisterCondition(c);
            condition = c;
            if (announce)
            {
                Log.Message("[冰鹅族] 低温围攻：大气冷冻器【已建成并开始运转】"
                    + "（每小时 -" + Props.degreesPerHour.ToString("0") + "℃，"
                    + (Props.lifetimeTicks / 2500) + " 小时后自毁）。");
                Messages.Message("Binguin_CryoSiege_15".Translate()
                    + Props.degreesPerHour.ToString("0") + "Binguin_CryoSiege_16".Translate()
                    + (Props.lifetimeTicks / 2500) + "Binguin_CryoSiege_17".Translate(),
                    parent, MessageTypeDefOf.ThreatSmall, false);
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            try
            {
                // ★ 摧毁后不立刻结束，改为让 Condition 在 2 小时内线性回暖
                GameCondition_BinguinCryoFreeze cryo = condition as GameCondition_BinguinCryoFreeze;
                if (cryo != null)
                {
                    cryo.StartRecovery();
                }
                else if (condition != null && !condition.Permanent)
                {
                    condition.End();
                }
            }
            catch (Exception)
            {
            }
            condition = null;
        }
    }
}
