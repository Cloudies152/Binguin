// ============================================================================
// 冰鹅族袭击编成（2026-09 用户需求）
//
// ★ 点数门槛用【袭击策略计算前的点数】：
//   IncidentWorker_Raid.AdjustedRaidPoints(float points, ...) 的第 1 个参数就是原值
//   （Mono.Cecil 读 IL 确认：之后才依次乘 arrivalMode.pointsFactorCurve、
//     strategy.pointsFactorCurve、ageRestriction.threatPointsFactor、
//     planetLayer.raidPointsFactor）→ prefix 取这个值存进 BinguinRaidContext.RawPoints。
//   哨骑 ≥2000、领袖护卫 ≥6000、领袖 ≥8000；不够就降级成士兵。
//   所以"10000 点满点袭击被围攻策略压到 6000"时，领袖/护卫照样出现（用的还是 10000）。
//
// ★ 每次袭击数量上限：领袖 1、领袖护卫 2 —— 在 PawnGenerator.GeneratePawn 前缀里数数，
//   超了就改成士兵（在生成前换 kind，装备/武器自动按新 kind 生成，不留残迹）。
//
// ★ 装备后处理挂在 RaidStrategyWorker.MakeLords(IncidentParms, List<Pawn>) 前缀：
//   IL 确认该方法收到的是【已经生成好的整队 pawn】（方法体只做 SplitIntoGroups + 建 Lord），
//   所以能一次看到全队 —— "护盾背包每场最多 2~4 个"这种全队上限才算得准。
//
// ★ 只影响本 mod 的袭击 kind；动物（驮兽）、机械体、其它派系完全不动。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Raids
{
    // 一场袭击的上下文（主线程同步执行，静态字段足够）
    public static class BinguinRaidContext
    {
        public static float RawPoints = -1f;
        public static int RaidStartTick = -1;
        public static int LeaderCount;
        public static int GuardCount;
        // 本场袭击的策略 defName（破墙 / 工兵 / 围攻 …）
        public static string StrategyDefName = null;

        // 允许出现【冰鹅族工兵】的袭击策略（2026-09 用户需求：
        //   只在破墙袭击 / 工兵袭击 / 特殊围攻袭击里出现）
        // 破墙袭击 / 破墙袭击（智能）/ 工兵袭击 / 低温围攻（见 CryoSiege_Binguin.cs）
        // ★ 2026-09 性能：原实现用 LINQ 的数组 Contains（走 EqualityComparer +
        //   枚举器，每次生成袭击 pawn 都会调用）→ 改为序数字符串直接比较，零分配。
        public static bool IsSapperRaid
        {
            get
            {
                string s = StrategyDefName;
                return s == "ImmediateAttackBreaching"
                    || s == "ImmediateAttackBreachingSmart"
                    || s == "ImmediateAttackSappers"
                    || s == "Binguin_CryoSiege";
            }
        }

        public static bool IsBreachRaid
        {
            get
            {
                return StrategyDefName == "ImmediateAttackBreaching"
                    || StrategyDefName == "ImmediateAttackBreachingSmart";
            }
        }

        public static void BeginRaid(float points, string strategyDefName)
        {
            RawPoints = points;
            StrategyDefName = strategyDefName;
            RaidStartTick = GenTicks.TicksGame;
            LeaderCount = 0;
            GuardCount = 0;
        }

        public static void EndRaid()
        {
            RawPoints = -1f;
            StrategyDefName = null;
            RaidStartTick = -1;
        }

        // 是否处于"袭击生成窗口"（10 秒内）；防止异常路径下状态残留影响和平生成
        public static bool InRaidWindow
        {
            get
            {
                if (RawPoints < 0f || RaidStartTick < 0)
                {
                    return false;
                }
                return GenTicks.TicksGame - RaidStartTick < 600;
            }
        }
    }

    // 常用 kind 的缓存查询
    public static class BinguinRaidKinds
    {
        public const string MilitiaName = "Binguin_Militia";
        public const string SoldierName = "Binguin_Soldier";
        public const string ScoutName = "Binguin_Scout";
        public const string GuardName = "Binguin_LeaderGuard";
        public const string LeaderName = "Binguin_Leader";
        public const string FighterName = "Binguin_Fighter";
        public const string SapperName = "Binguin_Sapper";

        private static readonly Dictionary<string, PawnKindDef> cache = new Dictionary<string, PawnKindDef>();

        public static PawnKindDef Get(string defName)
        {
            PawnKindDef def;
            if (cache.TryGetValue(defName, out def))
            {
                return def;
            }
            def = DefDatabase<PawnKindDef>.GetNamedSilentFail(defName);
            cache[defName] = def;
            return def;
        }

        public static PawnKindDef Militia { get { return Get(MilitiaName); } }
        public static PawnKindDef Soldier { get { return Get(SoldierName); } }

        public static bool IsRaidKind(PawnKindDef kd)
        {
            if (kd == null)
            {
                return false;
            }
            string n = kd.defName;
            return n == MilitiaName || n == SoldierName || n == ScoutName
                || n == GuardName || n == LeaderName || n == FighterName
                || n == SapperName;
        }
    }

    // ---------- 1) 记录"策略计算前"的点数，并重置本场计数 ----------
    public static class Patch_BinguinRaidPoints
    {
        // ★★★ 2026-09 真凶修复：原版参数名是 raidStrategy（不是 strategy）★★★
        //   Harmony 按【参数名】绑定原方法参数，名字写错会抛
        //   "Parameter "strategy" not found in method ..."，
        //   而它发生在静态构造函数里 → 整个 HarmonyPatches_Binguin 静态构造中断 →
        //   其后 5 条补丁（含低温围攻蓝图）全部静默失效。
        //   实测证据：Player.log「Error in static constructor of Binguin.Patch.HarmonyPatches_Binguin ...」。
        //   原版签名：AdjustedRaidPoints(float points, PawnsArrivalModeDef raidArrivalMode,
        //              RaidStrategyDef raidStrategy, Faction faction, PawnGroupKindDef groupKind,
        //              IIncidentTarget target, RaidAgeRestrictionDef ageRestriction)
        public static void Prefix(float points, RaidStrategyDef raidStrategy)
        {
            try
            {
                BinguinRaidContext.BeginRaid(points, raidStrategy != null ? raidStrategy.defName : null);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("记录袭击点数失败: " + e.Message, severity: 1, isDebug: false);
            }
        }
    }

    // ---------- 1b) 冰鹅族袭击的【到达方式】强制（2026-09 用户定稿） ----------
    //   用户要的 8 类袭击里有「四散空投 5% / 中心空投 5%」，而原版"空投"并不是
    //   独立策略，只是策略的到达方式；且 ImmediateAttack 的到达方式里
    //   CenterDrop(1000 点 3.5) / RandomDrop(1.9) 权重极高（同场 EdgeWalkIn 才 1.0），
    //   所以 25% 的"直接进攻"里绝大多数会自己变成空投 → 用户设定的百分比会失真。
    //   → 这里只对【冰鹅派系】直接指定到达方式：
    //       · Binguin_DropCenter    → CenterDrop（中心空投）
    //       · Binguin_DropScattered → RandomDrop（四散空投）
    //       · 其余（低温围攻/围攻/破墙/工兵/直接进攻/等待进攻）→ EdgeWalkIn 徒步进场
    //   ★ 必须同时把 __result 置 true：调用方是
    //     if (!TryResolveRaidArriveMode(parms)) { Log.Error(...); parms.raidArrivalMode = EdgeWalkIn; }
    //     —— prefix 返回 false 时 Harmony 会把返回值留成 default(bool)=false，
    //     那样调用方会当失败处理（报错 + 覆盖我们的选择）。
    //   ★ 指定方式在当前地图不可用（例如太空层）时返回 true 交回原版逻辑。
    public static class Patch_BinguinRaidArrival
    {
        public static bool Prefix(IncidentParms parms, ref bool __result)
        {
            try
            {
                if (parms == null || parms.faction == null || parms.faction.def == null
                    || !BinguinFactions.IsBinguinFaction(parms.faction))
                {
                    return true;
                }
                // 已经指定过（剧本/任务/快速军事支援）→ 不干预
                if (parms.raidArrivalMode != null || parms.raidArrivalModeForQuickMilitaryAid)
                {
                    return true;
                }
                RaidStrategyDef st = parms.raidStrategy;
                if (st == null)
                {
                    return true;
                }
                PawnsArrivalModeDef want;
                if (st.defName == "Binguin_DropCenter")
                {
                    want = PawnsArrivalModeDefOf.CenterDrop;
                }
                else if (st.defName == "Binguin_DropScattered")
                {
                    want = PawnsArrivalModeDefOf.RandomDrop;
                }
                else
                {
                    want = PawnsArrivalModeDefOf.EdgeWalkIn;
                }
                if (want == null || want.Worker == null || !want.Worker.CanUseWith(parms))
                {
                    return true;   // 该到达方式在此地图不可用 → 交回原版
                }
                parms.raidArrivalMode = want;
                __result = true;
                return false;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("强制袭击到达方式失败（已回退原版）：" + e.Message, severity: 1, isDebug: false);
                return true;
            }
        }
    }

    // ---------- 2) 生成前：点数门槛 + 每场数量上限 ----------
    public static class Patch_BinguinRaidKinds
    {
        public static void GeneratePawnPrefix(ref PawnGenerationRequest request)
        {
            try
            {
                if (!BinguinRaidContext.InRaidWindow)
                {
                    return;
                }
                PawnKindDef kd = request.KindDef;
                if (kd == null)
                {
                    return;
                }
                string n = kd.defName;
                float pts = BinguinRaidContext.RawPoints;

                if (n == BinguinRaidKinds.LeaderName)
                {
                    // 领袖：≥8000 点且本场还没出过
                    if (pts < 8000f || BinguinRaidContext.LeaderCount >= 1)
                    {
                        request.KindDef = DowngradeFromLeader(pts);
                        return;
                    }
                    BinguinRaidContext.LeaderCount++;
                    return;
                }

                if (n == BinguinRaidKinds.GuardName)
                {
                    // 领袖护卫：≥6000 点且本场最多 2 个
                    if (pts < 6000f || BinguinRaidContext.GuardCount >= 2)
                    {
                        request.KindDef = BinguinRaidKinds.Soldier;
                        return;
                    }
                    BinguinRaidContext.GuardCount++;
                    return;
                }

                if (n == BinguinRaidKinds.SapperName)
                {
                    // 工兵：只在破墙 / 工兵 / 特殊围攻袭击里出现，其它袭击降级成士兵
                    if (!BinguinRaidContext.IsSapperRaid)
                    {
                        request.KindDef = BinguinRaidKinds.Soldier;
                    }
                    return;
                }

                if (n == BinguinRaidKinds.ScoutName)
                {
                    // 哨骑：≥3000 点
                    if (pts < 3000f)
                    {
                        request.KindDef = BinguinRaidKinds.Soldier;
                    }
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("袭击单位门槛判定失败: " + e.Message, severity: 1, isDebug: false);
            }
        }

        // 领袖被挡下时：有护卫资格就给护卫，否则给士兵
        private static PawnKindDef DowngradeFromLeader(float pts)
        {
            if (pts >= 6000f && BinguinRaidContext.GuardCount < 2)
            {
                PawnKindDef guard = BinguinRaidKinds.Get(BinguinRaidKinds.GuardName);
                if (guard != null)
                {
                    BinguinRaidContext.GuardCount++;
                    return guard;
                }
            }
            return BinguinRaidKinds.Soldier;
        }
    }

    // ---------- 3) 整队生成完毕后：装备后处理 ----------
    public static class Patch_BinguinRaidGear
    {
        public static void MakeLordsPrefix(IncidentParms parms, List<Pawn> pawns)
        {
            try
            {
                BinguinRaidGear.Apply(parms, pawns);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("袭击装备后处理失败: " + e.Message, severity: 1, isDebug: false);
            }
            finally
            {
                BinguinRaidContext.EndRaid();
            }
        }
    }

    public static class BinguinRaidGear
    {
        // 原版工业级武器（50% 概率替换用）
        private static readonly string[] VanillaIndustrialGuns = new string[]
        {
            "Gun_PumpShotgun", "Gun_AssaultRifle", "Gun_HeavySMG",
            "Gun_MachinePistol", "Gun_BoltActionRifle", "Gun_Revolver",
            "Gun_IncendiaryLauncher"
        };

        // 小概率火箭筒
        private static readonly string[] RocketLaunchers = new string[]
        {
            "Gun_DoomsdayRocket", "Gun_TripleRocket"
        };

        private const float RocketChance = 0.05f;      // 士兵小概率
        private const float VanillaGunChance = 0.5f;   // 民兵/士兵 50%
        private const float BeltChance = 0.3f;         // 30% 背包

        public static void Apply(IncidentParms parms, List<Pawn> pawns)
        {
            if (pawns == null || pawns.Count == 0)
            {
                return;
            }
            bool anyOurs = false;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p != null && BinguinRaidKinds.IsRaidKind(p.kindDef))
                {
                    anyOurs = true;
                    break;
                }
            }
            if (!anyOurs)
            {
                return;
            }

            bool toxic = IsToxicEnvironment(parms);

            // ★★ 2026-09 用户定稿：护盾背包（原版低角护盾包 Apparel_PackBroadshield）
            //   给民兵/士兵/哨骑的发放规则 —— 改成【小概率 + 每场硬上限 4 个】：
            //     · 携带率每场掷一次：2%~4%（用户："100 个里面只有 2~4 个带"）
            //     · ★ 2026-09 三次定稿：【不设每场上限】，纯概率
            //       （用户："算了去掉上限吧，感觉没什么必要"）
            //     · 可携带者 ≥20 人时【至少 1 个】（保留）
            //   实现：先数一遍本场可携带者，再把"目标个数"折算成每人的实际概率。
            //   ★ 关于"一般 2~4 个"：那是指【后期大规模袭击】里自然出现的数量
            //     （100 人 × 2~4% = 2~4 个），不是硬性下限 —— 中小规模袭击
            //     按概率可能只有 1 个甚至 0 个，符合预期。
            int eligibleForShield = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn ep = pawns[i];
                if (ep == null || ep.kindDef == null || ep.apparel == null
                    || ep.RaceProps == null || !ep.RaceProps.Humanlike)
                {
                    continue;
                }
                string ek = ep.kindDef.defName;
                if ((ek == BinguinRaidKinds.MilitiaName || ek == BinguinRaidKinds.SoldierName
                        || ek == BinguinRaidKinds.ScoutName) && CurrentBelt(ep) == null)
                {
                    eligibleForShield++;
                }
            }
            float shieldCarryRate = Rand.Range(0.02f, 0.04f);          // 2%~4%
            int shieldTarget = UnityEngine.Mathf.Max(0,
                UnityEngine.Mathf.RoundToInt(eligibleForShield * shieldCarryRate));
            if (eligibleForShield >= 20 && shieldTarget < 1)
            {
                shieldTarget = 1;   // 大部队袭击至少保证 1 个盾包
            }
            float shieldChance = eligibleForShield > 0
                ? (float)shieldTarget / eligibleForShield : 0f;
            int shieldUsed = 0;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.kindDef == null || p.RaceProps == null || !p.RaceProps.Humanlike)
                {
                    continue;
                }
                string k = p.kindDef.defName;
                bool militia = k == BinguinRaidKinds.MilitiaName;
                bool soldier = k == BinguinRaidKinds.SoldierName;
                bool scout = k == BinguinRaidKinds.ScoutName;
                bool guard = k == BinguinRaidKinds.GuardName;
                bool leader = k == BinguinRaidKinds.LeaderName;
                bool sapper = k == BinguinRaidKinds.SapperName;
                if (!(militia || soldier || scout || guard || leader || sapper))
                {
                    continue;
                }

                // (0) 冰鹅族工兵（2026-09 用户需求）：
                //     建造技能必定 >10；装备按袭击类型决定
                //       · 破墙袭击 → 手榴弹（Weapon_GrenadeFrag）
                //       · 工兵袭击 → 护盾腰带（Apparel_ShieldBelt）+ 量产型钓竿（Binguin_FishingRod）
                //       · 其它允许的袭击（特殊围攻，待做）→ 保持默认士兵式装备
                if (sapper)
                {
                    SetHighConstructionSkill(p);
                    if (BinguinRaidContext.IsBreachRaid)
                    {
                        ReplaceWeapon(p, "Weapon_GrenadeFrag");
                    }
                    else if (BinguinRaidContext.StrategyDefName == "ImmediateAttackSappers")
                    {
                        ReplaceWeapon(p, "Binguin_FishingRod");
                        if (CurrentBelt(p) == null)
                        {
                            GiveBelt(p, "Apparel_ShieldBelt");
                        }
                    }
                }

                // (1) 武器：士兵小概率火箭筒；民兵/士兵 50% 换原版工业武器
                if (soldier && Rand.Chance(RocketChance))
                {
                    ReplaceWeapon(p, RocketLaunchers.RandomElement());
                }
                else if ((soldier || militia) && Rand.Chance(VanillaGunChance))
                {
                    ReplaceWeapon(p, VanillaIndustrialGuns.RandomElement());
                }

                // (2) 背包（腰带层）
                //   ★ 2026-09 用户确认：
                //     · 领袖护卫【不】发腰带装备 —— 寒门盾本身就是 Belt 层
                //       （Defs/Feature/GateShield/Apparel_GateShield.xml layers=Belt 已核对），腰带位被占住，
                //       原版护盾背包/烟罐包根本穿不上去；
                //     · 改由【领袖】必定携带护盾背包（领袖的腰带位是空的）。
                if (leader)
                {
                    if (CurrentBelt(p) == null)
                    {
                        // ★ 2026-09 用户要求：领袖带【护盾背包】而不是【护盾腰带】。
                        //   护盾背包 = 原版 Royalty 的 Apparel_PackBroadshield
                        //   （label "low-shield pack"，一次性部署 30 秒低角护盾，
                        //     用护盾核心 BroadshieldCore 在机工台制作）。
                        //   ★ 为什么必须换：护盾腰带（Apparel_ShieldBelt）会
                        //     【禁止穿戴者射击】—— 领袖拿着极激急击机枪却打不出子弹；
                        //     低角护盾是"子弹只出不进"，领袖照常开火。
                        //   未装 Royalty / 找不到该 def 时回退到护盾腰带。
                        if (!GiveBelt(p, "Apparel_PackBroadshield"))
                        {
                            GiveBelt(p, "Apparel_ShieldBelt");
                        }
                        shieldUsed++;
                    }
                }
                else if ((militia || soldier || scout) && CurrentBelt(p) == null && Rand.Chance(BeltChance))
                {
                    // 小概率拿到护盾背包（低角护盾：能出不能进，可边打边射）；
                    // 没抽到 / 超过每场上限的仍按原设计带烟罐包 / 消防背包。
                    bool gotShieldPack = false;
                    if (shieldChance > 0f && Rand.Chance(shieldChance))
                    {
                        gotShieldPack = GiveBelt(p, "Apparel_PackBroadshield");
                    }
                    if (gotShieldPack)
                    {
                        shieldUsed++;
                    }
                    else
                    {
                        GiveBelt(p, Rand.Bool ? "Apparel_SmokepopBelt" : "Apparel_FirefoampopPack");
                    }
                }

                // (3) 有毒环境：民兵/士兵不戴头盔、改戴原版防毒面具
                //     （Apparel_GasMask 与头盔同属 Overhead 层，EnsureGasMask 会先摘掉
                //      头上的东西再戴面具 —— 2026-09 用户明确要求"士兵有毒环境不带头盔，改带面具"）
                if (toxic && (militia || soldier))
                {
                    EnsureGasMask(p);
                }

                // (4) 护卫/领袖确保身上有活力水
                if ((guard || leader) && !HasThing(p, ThingDefOf.GoJuice))
                {
                    GiveInventory(p, ThingDefOf.GoJuice);
                }

                // (5) 携带食物：1~3 份（2026-09 用户要求）
                //     原版 GiveRandomFood 是按 invNutrition 换算固定份数的，
                //     这里在袭击生成后改成随机 1~3 份；领袖用奢侈食物，其余简单/精致随机。
                SetFood(p, leader);
            }

            BinguinLogUtility.Log("袭击装备后处理：本场 " + pawns.Count + " 人，策略 "
                + (BinguinRaidContext.StrategyDefName ?? "?") + "，护盾背包目标 "
                + shieldTarget + " 个（携带率 " + (shieldCarryRate * 100f).ToString("0.0")
                + "%，可携带者 " + eligibleForShield + "），已发 " + shieldUsed
                + (toxic ? "，毒环境 → 发防毒面具" : ""));
        }

        // ---------- 工具方法 ----------

        // 目标地块是否"有毒"：地块污染 > 0.1，或目标地图正在下毒雨/毒霾
        private static bool IsToxicEnvironment(IncidentParms parms)
        {
            if (parms == null || parms.target == null)
            {
                return false;
            }
            PlanetTile tile = parms.target.Tile;
            if (tile.Valid && Find.WorldGrid[tile].pollution > 0.1f)
            {
                return true;
            }
            Map map = parms.target as Map;
            if (map != null && map.gameConditionManager != null
                && map.gameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout))
            {
                return true;
            }
            return false;
        }

        // ★ 2026-09 性能：装备后处理里同一批 defName 会对每个袭击 pawn 重复查询
        //   DefDatabase（写一次袭击要查几十次）→ 统一走懒加载字典缓存
        //   （未命中/为 null 也缓存，避免反复查找不存在的 def）。
        private static readonly Dictionary<string, ThingDef> defCache = new Dictionary<string, ThingDef>();

        private static ThingDef CachedDef(string defName)
        {
            ThingDef def;
            if (defCache.TryGetValue(defName, out def))
            {
                return def;
            }
            def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            defCache[defName] = def;
            return def;
        }

        private static void ReplaceWeapon(Pawn p, string weaponDefName)
        {
            if (p.equipment == null)
            {
                return;
            }
            ThingDef def = CachedDef(weaponDefName);
            if (def == null)
            {
                return;
            }
            ThingWithComps old = p.equipment.Primary;
            if (old != null)
            {
                p.equipment.Remove(old);
                old.Destroy(DestroyMode.Vanish);
            }
            ThingWithComps w = ThingMaker.MakeThing(def, null) as ThingWithComps;
            if (w != null)
            {
                p.equipment.AddEquipment(w);
            }
        }

        private static Apparel CurrentBelt(Pawn p)
        {
            if (p.apparel == null)
            {
                return null;
            }
            List<Apparel> worn = p.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].def.apparel != null && worn[i].def.apparel.layers.Contains(ApparelLayerDefOf.Belt))
                {
                    return worn[i];
                }
            }
            return null;
        }

        // 返回是否真的穿上了（def 缺失/穿不上时返回 false，调用方可回退到别的装备）
        private static bool GiveBelt(Pawn p, string defName)
        {
            if (p.apparel == null)
            {
                return false;
            }
            ThingDef def = CachedDef(defName);
            if (def == null)
            {
                return false;
            }
            Apparel a = ThingMaker.MakeThing(def, null) as Apparel;
            if (a != null)
            {
                p.apparel.Wear(a, false);
                return true;
            }
            return false;
        }

        // 毒环境：头盔与防毒面具同属 Overhead 层，戴面具前先把头上的东西摘掉
        private static void EnsureGasMask(Pawn p)
        {
            if (p.apparel == null)
            {
                return;
            }
            ThingDef maskDef = CachedDef("Apparel_GasMask");
            if (maskDef == null)
            {
                return;
            }
            List<Apparel> worn = p.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].def == maskDef)
                {
                    return;
                }
            }
            // ★ 2026-09 性能：原来先收集到一个临时 List 再删（每个 pawn 分配一次）。
            //   倒序就地删除即可（删当前下标不会影响更低下标），零分配。
            for (int i = worn.Count - 1; i >= 0; i--)
            {
                Apparel a = worn[i];
                if (a.def.apparel != null && a.def.apparel.layers.Contains(ApparelLayerDefOf.Overhead))
                {
                    p.apparel.Remove(a);
                    a.Destroy(DestroyMode.Vanish);
                }
            }
            Apparel mask = ThingMaker.MakeThing(maskDef, null) as Apparel;
            if (mask != null)
            {
                p.apparel.Wear(mask, false);
            }
        }

        // 工兵：建造技能必定 >10（随机 11~20）
        private static void SetHighConstructionSkill(Pawn p)
        {
            if (p.skills == null)
            {
                return;
            }
            SkillRecord rec = p.skills.GetSkill(SkillDefOf.Construction);
            if (rec == null)
            {
                return;
            }
            // ★ 1.6 的 SkillRecord 只有只读属性 TotallyDisabled（由基因/能力算出来的），
            //   不能直接写；正常人形默认就是可用的，这里只改等级。
            rec.Level = Rand.RangeInclusive(11, 20);
            rec.xpSinceLastLevel = 0f;
        }

        // ★ 2026-09：改为序数比较的辅助方法（原为 string[] + LINQ Contains）
        private static bool IsMealDefName(string defName)
        {
            return defName == "MealSimple" || defName == "MealFine"
                || defName == "MealSurvivalPack" || defName == "MealLavish"
                || defName == "Pemmican";
        }

        // 食物改成 1~3 份（领袖 = 奢侈食物，其余 = 简单/精致随机）
        private static void SetFood(Pawn p, bool leader)
        {
            if (p.inventory == null || p.inventory.innerContainer == null)
            {
                return;
            }
            // ★ 2026-09 性能：倒序就地移除（原来每个 pawn 都要分配一个临时 List，
            //   且用 LINQ 的 Contains 做餐名匹配）。
            ThingOwner inv = p.inventory.innerContainer;
            for (int i = inv.Count - 1; i >= 0; i--)
            {
                Thing t = inv[i];
                if (t != null && t.def != null && IsMealDefName(t.def.defName))
                {
                    inv.Remove(t);
                    t.Destroy(DestroyMode.Vanish);
                }
            }
            string defName = leader ? "MealLavish" : (Rand.Bool ? "MealSimple" : "MealFine");
            ThingDef def = CachedDef(defName);
            if (def == null)
            {
                return;
            }
            Thing meal = ThingMaker.MakeThing(def, null);
            if (meal == null)
            {
                return;
            }
            meal.stackCount = Rand.RangeInclusive(1, 3);
            p.inventory.innerContainer.TryAdd(meal, true);
        }

        private static bool HasThing(Pawn p, ThingDef def)
        {
            if (p.inventory == null || p.inventory.innerContainer == null)
            {
                return false;
            }
            // ★ 2026-09：原实现用 LINQ Any（每次分配闭包 + 迭代器）→ 直接按下标遍历
            ThingOwner inv = p.inventory.innerContainer;
            for (int i = 0; i < inv.Count; i++)
            {
                Thing t = inv[i];
                if (t != null && t.def == def)
                {
                    return true;
                }
            }
            return false;
        }

        private static void GiveInventory(Pawn p, ThingDef def)
        {
            if (p.inventory == null || p.inventory.innerContainer == null)
            {
                return;
            }
            Thing t = ThingMaker.MakeThing(def, null);
            if (t != null)
            {
                p.inventory.innerContainer.TryAdd(t, true);
            }
        }
    }
}
