// ============================================================================
// Def 补丁（静态构造，XML 解析之后执行）
//
// 背景：本环境的 GenTypes 类型缓存会在某些 mod 早期触发时固化，
//       导致 XML 里引用自定义类型（CompProperties_* / VerbClass）时
//       "Could not find type"（一体机/滑板因此消失）。
//       清 GenTypes 缓存会黑屏（1.6 内部 NRE），故改为：
//       ★ XML 里【零自定义类型引用】（全部用原版类型 → defs 必加载成功），
//         自定义 Comp / Verb 在此静态构造里用代码添加到 def。
//       ★ ThingDef.verbs 与 TargetParameters 是 internal（1.6），
//         用 HarmonyLib 的 AccessTools 反射访问。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Binguin
{
    [StaticConstructorOnStartup]
    public static class BinguinDefPatches
    {
        static BinguinDefPatches()
        {
            PatchAutoStoneMachine();
            PatchPenguinBoard();
            PatchSlideJob();
            PatchBoardFlyer();
            PatchRecycler();
            PatchAtmosphereController();
            PatchCryoSiege();
            PatchWasteToxemia();
            PatchMeteorGuide();
            PatchFishing();
            PatchMasterFishing();
            PatchVoidSword();
            PatchFrostAssault();
            VerifyStinkFish();
            PatchScoutArmorSlide();
            PatchWillingCannon();
            PatchGateShieldField();
            PatchMothership();
            PatchDiplomacy();
            PatchBinguinBlueprintTraders();
            PatchIceCrownAbility();
            PatchRaidStrategyWeights();
            VerifyTraderStocks();
        }

        // ====================================================================
        // ★ 2026-09-26 冰鹅商人【卖鱼】自检（用户需求）
        //
        //   Defs/TraderKinds_Binguin.xml 是【脚本生成】的
        //   （Tools\Make-BinguinTraderKinds.py）：把原版三个 trader kind 的
        //   stockGenerators 整段照抄过来、末尾追加一个鱼生成器。
        //
        //   ★★ 为什么不"继承原版 + 只加鱼"：实测
        //      `Verse.DirectXmlToObject.ListFromXml<T>()` 直接 new 一个空 List 再填，
        //      **没有 Clear、也不会合并父 def 的 list**
        //      ⇒ 子 def 一写 <stockGenerators>，父 def 那张表就被【整体替换】，
        //        结果会是"只有鱼的商人"。所以必须整段复制。
        //
        //   判据（每个自定义 kind）：生成器数量 == 对应原版数量 + 1，且含 Fish 生成器。
        //   ★ 读 categoryDef 要反射：该字段在 StockGenerator_Category 上是 private
        //     （只有基类的 countRange 是 public，Mono.Cecil 实证）。
        // ====================================================================
        private static readonly string[,] FishTraderPairs =
        {
            { "Binguin_Settlement_Standard",    "Base_Outlander_Standard" },
            { "Binguin_Caravan_BulkGoods",      "Caravan_Outlander_BulkGoods" },
            { "Binguin_Caravan_CombatSupplier", "Caravan_Outlander_CombatSupplier" },
        };

        /// <summary>反射读 StockGenerator_Category.categoryDef.defName（该字段是 private）。</summary>
        private static string CategoryDefNameOf(StockGenerator gen)
        {
            try
            {
                StockGenerator_Category cat = gen as StockGenerator_Category;
                if (cat == null) return null;
                FieldInfo fi = typeof(StockGenerator_Category).GetField("categoryDef",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fi == null) return null;
                ThingCategoryDef def = fi.GetValue(cat) as ThingCategoryDef;
                return def != null ? def.defName : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void VerifyTraderStocks()
        {
            try
            {
                // ★★ 先给作战商挂上"卖冰鹅族武器"的生成器（2026-09-26 用户需求）。
                //   必须在自检【之前】挂：自检要数生成器的数量。
                PatchCombatSupplierWeapons();

                string lines = "";
                bool allOk = true;
                for (int i = 0; i < FishTraderPairs.GetLength(0); i++)
                {
                    string mineName = FishTraderPairs[i, 0];
                    string parentName = FishTraderPairs[i, 1];

                    TraderKindDef mine = DefDatabase<TraderKindDef>.GetNamedSilentFail(mineName);
                    TraderKindDef parent = DefDatabase<TraderKindDef>.GetNamedSilentFail(parentName);

                    if (mine == null)
                    {
                        lines += "\n    [缺失] " + mineName + "（Defs/11_World/TraderKindDefs_Binguin.xml 没加载？）";
                        allOk = false;
                        continue;
                    }
                    int n = mine.stockGenerators != null ? mine.stockGenerators.Count : 0;
                    int p = (parent != null && parent.stockGenerators != null) ? parent.stockGenerators.Count : -1;

                    bool fishFound = false;
                    bool sawFishCategory = false;
                    if (mine.stockGenerators != null)
                    {
                        for (int g = 0; g < mine.stockGenerators.Count; g++)
                        {
                            if (CategoryDefNameOf(mine.stockGenerators[g]) == "Fish")
                            {
                                fishFound = true;
                                sawFishCategory = true;
                                break;
                            }
                        }
                    }
                    // 反射读不到时（字段改名/权限变化）退回"数量多 1"判据
                    if (!sawFishCategory && p > 0 && n == p + 1) fishFound = true;

                    lines += "\n    " + mineName + " = " + n + " 个生成器（原版 " + parentName + " = " + p
                        + "），含鱼=" + (fishFound ? "有" : "无");

                    // ★ 没装 Odyssey 时鱼生成器带 MayRequire 会被整条跳过 ⇒
                    //   数量等于"原版 + 武器生成器"，这是【预期】行为，不算异常。
                    if (!ModsConfig.OdysseyActive)
                    {
                        int expectNoOdyssey = (mineName == "Binguin_Caravan_CombatSupplier") ? p + 1 : p;
                        if (p > 0 && n != expectNoOdyssey)
                        {
                            lines += "  ← 无 Odyssey，应等于 " + expectNoOdyssey + " 但不相等，需检查";
                            allOk = false;
                        }
                        continue;
                    }

                    // 装了 Odyssey 就必须"原版数量 + 1（鱼）"；
                    // 作战商另外还有 +1（冰鹅族武器生成器，见 PatchCombatSupplierWeapons）
                    int expect = p + 1;
                    if (mineName == "Binguin_Caravan_CombatSupplier") expect += 1;
                    if (!fishFound || n <= 1 || (p > 0 && n != expect))
                    {
                        allOk = false;
                    }
                }

                if (allOk)
                {
                    Log.Message("[冰鹅族] 商人卖鱼自检通过"
                        + (ModsConfig.OdysseyActive
                            ? "：鱼生成器已生效（据点 + 大宗杂货商队 + 战斗补给商队；稀有品贸易商按用户要求不带鱼）。"
                            : "：未装 Odyssey，鱼生成器按 MayRequire 跳过（此时商人 = 原版行为，符合预期）。")
                        + lines);
                }
                else
                {
                    Log.Warning("[冰鹅族] 商人卖鱼自检【异常】：def 继承可能没生效 ——"
                        + "若某个 kind 只剩 1 个生成器就是'只剩鱼'。补救办法：在"
                        + " Defs/11_World/TraderKindDefs_Binguin.xml 里把原版 stockGenerators 整段复制过来再追加鱼。" + lines);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 商人自检本身出错：" + e.Message);
            }
        }

        // ====================================================================
        // ★ 2026-09-26 作战商【出售冰鹅族武器】（用户需求）
        //
        //   「给作战商加 2-5 把冰鹅族武器出售，只能是冷冻武器及以下的武器
        //     （霰雪无垠武器不售卖）」
        //
        //   ★ 为什么走 C# 而不是 XML：本 mod 的 XML 里【零自定义类型引用】
        //     （原因见本文件顶部：GenTypes 缓存早期固化会导致 "Could not find type"），
        //     所以自定义的 StockGenerator 子类只能在这里用代码加进 def。
        //
        //   ★ 生成器本体：Source/TraderStock_BinguinWeapons.cs
        //     它按"配方的研究前置"筛选武器，并排除霰雪无垠层级。
        // ====================================================================
        private const string CombatSupplierDefName = "Binguin_Caravan_CombatSupplier";

        private static void PatchCombatSupplierWeapons()
        {
            try
            {
                TraderKindDef def = DefDatabase<TraderKindDef>.GetNamedSilentFail(CombatSupplierDefName);
                if (def == null)
                {
                    Log.Warning("[冰鹅族] 找不到 " + CombatSupplierDefName + "，作战商卖武器未挂载。");
                    return;
                }
                if (def.stockGenerators == null)
                {
                    def.stockGenerators = new List<StockGenerator>();
                }
                // 幂等：已经挂过就不再挂（防止重复调用导致双倍武器）
                for (int i = 0; i < def.stockGenerators.Count; i++)
                {
                    if (def.stockGenerators[i] is StockGenerator_BinguinWeapons) return;
                }
                def.stockGenerators.Add(new StockGenerator_BinguinWeapons());
                Log.Message("[冰鹅族] 作战商卖武器已挂载｜可售武器池 = "
                    + StockGenerator_BinguinWeapons.DescribePool()
                    + "（霰雪无垠层级的武器已排除：企鹅飞踢！！ / 极激急击机枪）");
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 作战商卖武器挂载失败：" + e);
            }
        }

        // ====================================================================
        // ★ 2026-09 用户定稿：冰鹅派系的袭击构成（百分比 = 权重，总和 100）
        //   低温围攻 20 / 原版围攻 5 / 破墙 10 / 工兵 10 /
        //   四散空投 5 / 中心空投 5 / 直接进攻 25 / 等待进攻 20
        //
        //   ★ 实现方式（IL 实证过的机制）：
        //     IncidentWorker_RaidEnemy.ResolveRaidStrategy 里
        //       候选 = DefDatabase<RaidStrategyDef>.AllDefs.Where(CanUseWith)
        //       权重 = RaidStrategyWorker.SelectionWeightForFaction(map, faction, points)
        //     而 SelectionWeightForFaction 会【优先】使用该 def 的
        //     selectionWeightCurvesPerFaction 里匹配本派系的那条曲线，
        //     没有匹配才回落到默认的 selectionWeightPerPointsCurve。
        //   → 所以我们只给【冰鹅】派系加一条专用曲线：其它派系（海盗/外来者/
        //     机械族…）的袭击构成完全不变，原版策略的默认曲线也一个字没动。
        //   ★ 破墙有两条 def（普通 + 智能），各 5 = 合计 10%。
        //   ★ 智能直接进攻（ImmediateAttackSmart）用户名单里没有 → 显式归零。
        // ====================================================================
        private static readonly Dictionary<string, float> BinguinRaidWeights =
            new Dictionary<string, float>
        {
            { "Binguin_CryoSiege", 20f },               // 低温围攻
            { "Siege", 5f },                            // 原版围攻（迫击炮）
            { "ImmediateAttackBreaching", 5f },         // 破墙（合计 10）
            { "ImmediateAttackBreachingSmart", 5f },
            { "ImmediateAttackSappers", 10f },          // 工兵
            { "Binguin_DropScattered", 5f },            // 四散空投
            { "Binguin_DropCenter", 5f },               // 中心空投
            { "ImmediateAttack", 25f },                 // 直接进攻
            { "StageThenAttack", 20f },                 // 等待进攻
            { "ImmediateAttackSmart", 0f },             // 智能直接进攻：不在名单 → 0
        };

        // ★ FactionCurve.selectionWeightPerPointsCurve 是【私有字段】（Cecil 实证），
        //   原版 XML 里能写是因为它的加载器走反射；我们同样用反射赋值。
        private static readonly FieldInfo FactionCurveField =
            AccessTools.Field(typeof(FactionCurve), "selectionWeightPerPointsCurve");

        private static SimpleCurve ConstantWeightCurve(float v)
        {
            // 两点常数曲线（比单点更稳妥，语义与 XML 里的 <li>(0, 1)</li> 等价）
            List<CurvePoint> pts = new List<CurvePoint>();
            pts.Add(new CurvePoint(0f, v));
            pts.Add(new CurvePoint(100000f, v));
            return new SimpleCurve(pts);
        }

        private static void PatchRaidStrategyWeights()
        {
            try
            {
                // ★ 2026-09：冰鹅族拆成和平派 / 敌对派，两边都给同一套冰鹅族袭击构成
                //   （低温围攻是 mod 专属建筑，必须两个派系都挂曲线，否则只有一边会搭冷冻器）。
                FactionDef peaceful = DefDatabase<FactionDef>.GetNamedSilentFail(BinguinFactions.Peaceful);
                FactionDef hostile = DefDatabase<FactionDef>.GetNamedSilentFail(BinguinFactions.Hostile);
                if (peaceful == null && hostile == null)
                {
                    Log.Warning("[冰鹅族] 未找到任何冰鹅派系，袭击构成未设置（沿用原版权重）。");
                    return;
                }
                int n = 0;
                foreach (RaidStrategyDef def in DefDatabase<RaidStrategyDef>.AllDefs)
                {
                    float w;
                    if (!BinguinRaidWeights.TryGetValue(def.defName, out w))
                    {
                        continue;
                    }
                    if (def.selectionWeightCurvesPerFaction == null)
                    {
                        def.selectionWeightCurvesPerFaction = new List<FactionCurve>();
                    }
                    // 幂等：先清掉可能已存在的冰鹅条目（防重复挂载）
                    for (int i = def.selectionWeightCurvesPerFaction.Count - 1; i >= 0; i--)
                    {
                        FactionCurve old = def.selectionWeightCurvesPerFaction[i];
                        if (old != null && (old.faction == peaceful || old.faction == hostile))
                        {
                            def.selectionWeightCurvesPerFaction.RemoveAt(i);
                        }
                    }
                    if (AddFactionWeight(def, peaceful, w))
                    {
                        n++;
                    }
                    if (AddFactionWeight(def, hostile, w))
                    {
                        n++;
                    }
                }
                Log.Message("[冰鹅族] 袭击构成已设置（和平派 + 敌对派）：低温围攻 20% / 原版围攻 5% / "
                    + "破墙 10% / 工兵 10% / 四散空投 5% / 中心空投 5% / "
                    + "直接进攻 25% / 等待进攻 20% —— 共 " + n + " 条策略");
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 设置袭击构成失败（沿用原版权重）：" + e.Message);
            }
        }

        /// <summary>给某个派系挂一条常数权重曲线；def 或 faction 为空则返回 false。</summary>
        private static bool AddFactionWeight(RaidStrategyDef def, FactionDef faction, float w)
        {
            if (def == null || faction == null || FactionCurveField == null)
            {
                return false;
            }
            FactionCurve entry = new FactionCurve();
            entry.faction = faction;
            FactionCurveField.SetValue(entry, ConstantWeightCurve(w));
            def.selectionWeightCurvesPerFaction.Add(entry);
            return true;
        }

        // ★ 2026-09：WorkGiverDef.workerInt（Worker 属性的懒加载缓存）的反射字段
        //   只解析一次复用（原来 802/893/923 三处各查一遍 AccessTools.Field）。
        //   注意 ThoughtDef.workerInt 是另一个类型，仍单独解析（见 PatchFishing）。
        private static readonly FieldInfo WorkGiverWorkerIntField =
            AccessTools.Field(typeof(WorkGiverDef), "workerInt");

        // ★ 2026-10-06：JoyGiverDef.workerInt 同理（替换 giverClass 后要清缓存）。
        //   `JoyGiverDef.Worker` 属性懒加载实例化占位类，不清缓存就换了也不生效。
        private static readonly FieldInfo JoyGiverWorkerIntField =
            AccessTools.Field(typeof(JoyGiverDef), "workerInt");

        // 冰冠技能「投掷冰块」（2026-09 用户需求）：
        //   ① AbilityDef Binguin_AbilityThrowIce 挂效果 comp（投 5 块灵造冰岩）
        //   ② ThingDef Binguin_SpiritIceRock 挂 6 小时消融 comp
        //   ③ ThingDef Binguin_IceCrown 挂"装备即获得技能"comp
        // 全部运行时挂载（XML 零自定义类型，本项目惯例）。
        private static void PatchIceCrownAbility()
        {
            try
            {
                AbilityDef ability = DefDatabase<AbilityDef>.GetNamedSilentFail("Binguin_AbilityThrowIce");
                if (ability != null)
                {
                    if (ability.comps == null)
                    {
                        // ★ AbilityDef.comps 的元素类型是 AbilityCompProperties（不是 CompProperties）
                        ability.comps = new List<AbilityCompProperties>();
                    }
                    ability.comps.Add(new CompProperties_BinguinThrowIce());
                    Log.Message("[冰鹅族] 冰冠技能已挂载：投掷冰块（5 块灵造冰岩 / 3 充能 / 1 小时恢复）");
                }
                else
                {
                    Log.Message("[冰鹅族] 未找到 Binguin_AbilityThrowIce 技能 def。");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 冰冠技能效果挂载失败：" + ex);
            }

            try
            {
                ThingDef rock = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_SpiritIceRock");
                if (rock != null)
                {
                    if (rock.comps == null)
                    {
                        rock.comps = new List<CompProperties>();
                    }
                    rock.comps.Add(new CompProperties_BinguinSpiritIce());
                    Log.Message("[冰鹅族] 灵造冰岩已挂载：440 耐久 / 6 小时消融");
                }
            }
            catch (Exception ex2)
            {
                Log.Error("[冰鹅族] 灵造冰岩 comp 挂载失败：" + ex2);
            }

            try
            {
                ThingDef crown = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_IceCrown");
                if (crown != null)
                {
                    if (crown.comps == null)
                    {
                        crown.comps = new List<CompProperties>();
                    }
                    crown.comps.Add(new CompProperties_BinguinIceCrown());
                    Log.Message("[冰鹅族] 冰冠已挂载：装备即获得「投掷冰块」技能");
                }
            }
            catch (Exception ex3)
            {
                Log.Error("[冰鹅族] 冰冠技能授予 comp 挂载失败：" + ex3);
            }
        }

        // 冰鹅族蓝图（霰雪无垠 科技图纸，2026-09 用户需求）：
        //   · 稀有贸易商（稀有品商队 / 轨道稀有品商）：30% 概率出售
        //   · 冰鹅族据点（Base_Outlander_Standard + 派系过滤 Binguin）：70% 概率
        // 运行时挂载（XML 零自定义类型）；科技图纸 ThingDef 本身由原版
        // ThingDefGenerator_Techprints 依 ResearchProjectDef.techprintCount 自动生成。
        private static void PatchBinguinBlueprintTraders()
        {
            try
            {
                ThingDef bp = DefDatabase<ThingDef>.GetNamedSilentFail("Techprint_Binguin_ResearchEndlessSnow");
                if (bp == null)
                {
                    Log.Message("[冰鹅族] 未找到霰雪无垠科技图纸（未安装 Royalty？），蓝图商人未挂载。");
                    return;
                }
                // 稀有贸易商：不限派系，30% 概率（2026-09 用户改定：原 50%）
                MountBlueprintGenerator("Caravan_Outlander_Exotic", 0.3f, null);
                MountBlueprintGenerator("Orbital_Exotic", 0.3f, null);
                // 冰鹅族据点：仅冰鹅派系，70% 概率
                MountBlueprintGenerator("Base_Outlander_Standard", 0.7f, "Binguin");
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 蓝图商人挂载失败：" + ex);
            }
        }

        private static void MountBlueprintGenerator(string traderKindDefName, float chance, string onlyFaction)
        {
            try
            {
                TraderKindDef tk = DefDatabase<TraderKindDef>.GetNamedSilentFail(traderKindDefName);
                if (tk == null)
                {
                    Log.Message("[冰鹅族] 未找到商人类型 " + traderKindDefName + "，蓝图库存未挂载。");
                    return;
                }
                if (tk.stockGenerators == null)
                {
                    tk.stockGenerators = new List<StockGenerator>();
                }
                for (int i = 0; i < tk.stockGenerators.Count; i++)
                {
                    StockGenerator_BinguinBlueprint exist = tk.stockGenerators[i] as StockGenerator_BinguinBlueprint;
                    if (exist != null && exist.onlyFactionDefName == onlyFaction)
                    {
                        return; // 已挂载过（幂等）
                    }
                }
                StockGenerator_BinguinBlueprint sg = new StockGenerator_BinguinBlueprint();
                sg.chance = chance;
                sg.onlyFactionDefName = onlyFaction;
                tk.stockGenerators.Add(sg);
                sg.ResolveReferences(tk);
                sg.countRange = new IntRange(1, 1);
                Log.Message("[冰鹅族] 蓝图库存已挂载：" + traderKindDefName
                    + "（概率 " + (chance * 100f).ToString("0") + "%"
                    + (string.IsNullOrEmpty(onlyFaction) ? "" : "，仅 " + onlyFaction + " 派系") + "）");
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 蓝图库存挂载失败(" + traderKindDefName + ")：" + ex);
            }
        }

        // 外交官交谈 job（2026-08-22：殖民者右键外交官 → 走近 → 弹共存选择信）：
        // driverClass 由代码挂载（XML 零自定义类型，避免 GenTypes 缓存问题）
        private static void PatchDiplomacy()
        {
            try
            {
                JobDef talkJob = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_DiplomatTalk");
                if (talkJob != null)
                {
                    talkJob.driverClass = typeof(JobDriver_BinguinDiplomatTalk);
                    Log.Message("[冰鹅族] 外交官交谈 job 已挂载：JobDriver_BinguinDiplomatTalk");
                }
                else
                {
                    Log.Message("[冰鹅族] 未找到 Binguin_DiplomatTalk job，外交官交谈入口未挂载。");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 外交官交谈 job 挂载失败：" + ex);
            }
        }

        // 冰鹅族飞天决战（2026-08-22 用户设计文档，天问研究解锁）：
        //   ① 大型信号发射器：露天 PlaceWorker + 联系命令 comp
        //   ② 飞船型发射仓：进舱 job driver + 发射/放出 comp（容器=原版
        //      Building_Casket thingClass，已写在 XML）
        //   ③ 状态机在 GameComponent_BinguinDiplomacy（13 天/15 波/结局）
        private static void PatchMothership()
        {
            try
            {
                ThingDef relay = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_SignalRelay");
                if (relay != null)
                {
                    if (relay.placeWorkers == null)
                    {
                        relay.placeWorkers = new List<Type>();
                    }
                    bool hasPw = false;
                    for (int i = 0; i < relay.placeWorkers.Count; i++)
                    {
                        if (relay.placeWorkers[i] == typeof(PlaceWorker_BinguinOpenAir))
                        {
                            hasPw = true;
                            break;
                        }
                    }
                    if (!hasPw)
                    {
                        relay.placeWorkers.Add(typeof(PlaceWorker_BinguinOpenAir));
                    }
                    if (relay.comps == null)
                    {
                        relay.comps = new List<CompProperties>();
                    }
                    relay.comps.Add(new CompProperties_BinguinSignalRelay());
                    Log.Message("[冰鹅族] 信号发射器已挂载：露天校验 + 联系命令");
                }
                else
                {
                    Log.Message("[冰鹅族] 未找到 Binguin_SignalRelay（天问未激活？），信号发射器未挂载。");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 信号发射器挂载失败：" + ex);
            }

            try
            {
                ThingDef capsule = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_ShipCapsule");
                if (capsule != null)
                {
                    if (capsule.comps == null)
                    {
                        capsule.comps = new List<CompProperties>();
                    }
                    capsule.comps.Add(new CompProperties_BinguinShipCapsule());
                    Log.Message("[冰鹅族] 飞船发射仓已挂载：CompBinguinShipCapsule（进入/放出/发射）");
                }
                else
                {
                    Log.Message("[冰鹅族] 未找到 Binguin_ShipCapsule（天问未激活？），发射仓未挂载。");
                }
            }
            catch (Exception ex2)
            {
                Log.Error("[冰鹅族] 飞船发射仓挂载失败：" + ex2);
            }

            try
            {
                JobDef boardJob = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_BoardCapsule");
                if (boardJob != null)
                {
                    boardJob.driverClass = typeof(JobDriver_BinguinBoardCapsule);
                    Log.Message("[冰鹅族] 进入发射仓 job 已挂载：JobDriver_BinguinBoardCapsule");
                }
            }
            catch (Exception ex3)
            {
                Log.Error("[冰鹅族] 进舱 job 挂载失败：" + ex3);
            }
        }

        // 寒门盾外层护盾场（2026-08-21 v3 → v18 事件驱动）：
        //   ① 隐形实体 Binguin_GateShieldField 挂跟随 comp；
        //   ② 寒门盾 apparel 挂事件 comp（穿上即时生成/脱下即时销毁）
        private static void PatchGateShieldField()
        {
            try
            {
                ThingDef field = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_GateShieldField");
                if (field != null)
                {
                    if (field.comps == null)
                    {
                        field.comps = new List<CompProperties>();
                    }
                    field.comps.Add(new CompProperties_BinguinGateShieldField());
                    Log.Message("[冰鹅族] 寒门盾场已挂载：CompBinguinGateShieldField（跟随穿戴者，脱下自毁）");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 寒门盾场挂载失败：" + ex);
            }
            try
            {
                ThingDef shield = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_GateShield");
                if (shield != null)
                {
                    if (shield.comps == null)
                    {
                        shield.comps = new List<CompProperties>();
                    }
                    // 防重复挂载（热重载等场景）
                    bool exists = false;
                    for (int i = 0; i < shield.comps.Count; i++)
                    {
                        if (shield.comps[i] is CompProperties_BinguinGateShieldBearer)
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        shield.comps.Add(new CompProperties_BinguinGateShieldBearer());
                    }
                    Log.Message("[冰鹅族] 寒门盾已挂载事件驱动：CompBinguinGateShieldBearer（穿上生成/脱下销毁护盾场）");
                }
            }
            catch (Exception ex2)
            {
                Log.Error("[冰鹅族] 寒门盾事件 comp 挂载失败：" + ex2);
            }
        }

        // 威灵装甲（2026-08-20 用户需求）：内置迫击炮弹舱/燃料舱 comp
        private static void PatchWillingCannon()
        {
            try
            {
                ThingDef armor = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_WillingArmor");
                if (armor == null)
                {
                    Log.Message("[冰鹅族] 未找到 Binguin_WillingArmor，威灵炮 comp 未挂载。");
                    return;
                }
                if (armor.comps == null)
                {
                    armor.comps = new List<CompProperties>();
                }
                armor.comps.Add(new CompProperties_BinguinWillingCannon());
                // ★ v8：不再用原版 verb/reloadable（v5-v7 用户三连败无法发射）。
                //   炮击 = 滑板同款自绘命令（3 按钮 + Targeter 圈选 + 直接抛射），
                //   全部由 CompBinguinWillingCannon 自管，无需 verbs 替换。
                Log.Message("[冰鹅族] 威灵装甲已挂载：CompBinguinWillingCannon（v8 自绘命令：3 弹舱 / 60 燃料 / 每发 10 / 30s 冷却）");
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 威灵炮 comp 挂载失败：" + ex);
            }

            // 装填 job（真实搬运装填，2026-08-20 v2）：driverClass 挂载
            try
            {
                JobDef loadJob = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_WillingLoad");
                if (loadJob == null)
                {
                    Log.Message("[冰鹅族] 未找到 Binguin_WillingLoad job，装填未挂载。");
                }
                else
                {
                    loadJob.driverClass = typeof(JobDriver_BinguinWillingLoad);
                    Log.Message("[冰鹅族] 威灵装填 job 已挂载：JobDriver_BinguinWillingLoad（小人真实搬运装填）");
                }
            }
            catch (Exception ex2)
            {
                Log.Error("[冰鹅族] 威灵装填 job 挂载失败：" + ex2);
            }

            // 炮击 job（v10 站定瞄准 1s）：driverClass 挂载
            try
            {
                JobDef fireJob = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_WillingFire");
                if (fireJob == null)
                {
                    Log.Message("[冰鹅族] 未找到 Binguin_WillingFire job，炮击未挂载。");
                }
                else
                {
                    fireJob.driverClass = typeof(JobDriver_BinguinWillingFire);
                    Log.Message("[冰鹅族] 威灵炮击 job 已挂载：JobDriver_BinguinWillingFire（站定瞄准 1s 后开火）");
                }
            }
            catch (Exception ex3)
            {
                Log.Error("[冰鹅族] 威灵炮击 job 挂载失败：" + ex3);
            }
        }

        // 哨骑装甲滑行技能（2026-08-20 用户需求：和企鹅滑板一样的滑行技能）
        // 复用企鹅滑板的 CompBinguinBoard（CompGetWornGizmosExtra → 滑行冲刺
        // 按钮 → PawnFlyer 贴地飞行）；滑板/飞行器/免疫 hediff/job 均已有。
        private static void PatchScoutArmorSlide()
        {
            try
            {
                ThingDef armor = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_ScoutArmor");
                if (armor == null)
                {
                    Log.Message("[冰鹅族] 未找到 Binguin_ScoutArmor，滑行技能未挂载。");
                    return;
                }
                if (armor.comps == null)
                {
                    armor.comps = new List<CompProperties>();
                }
                armor.comps.Add(new CompProperties_BinguinBoard());
                Log.Message("[冰鹅族] 哨骑装甲已挂载滑行技能：CompBinguinBoard（同企鹅滑板：半径 10 格 / 冷却 10 秒）");
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 哨骑装甲滑行技能挂载失败：" + ex);
            }
        }

        // 突袭冷冻武器（2026-08-20 用户需求 v2）：
        //   1) 冰爆弹损伤 workerClass → DamageWorker_BinguinIceBullet
        //      （护甲结算前按面板伤害累积冰爆，被护甲挡掉也照加）
        //   2) 冰爆状态 comp 挂载到 HediffDef（满 100% 纯随机冻碎部位）
        private static void PatchFrostAssault()
        {
            try
            {
                PatchFrostAssaultInner();
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 突袭冷冻武器挂载失败：" + ex);
            }
        }

        private static void PatchFrostAssaultInner()
        {
            // 1) 各冰爆损伤 def 的 damage worker 替换（XML 用原版 worker 占位）
            //    ×1 普通冰爆弹（突击/精确步枪）；×2 终极（企鹅飞踢钝击/激光机枪）
            MountIceWorker("Binguin_IceBulletDamage", typeof(DamageWorker_BinguinIceBullet),
                "Binguin_DefPatches_01".Translate());
            MountIceWorker("Binguin_PenguinDamage", typeof(DamageWorker_BinguinBluntUltra),
                "Binguin_DefPatches_02".Translate());
            MountIceWorker("Binguin_LaserDamage", typeof(DamageWorker_BinguinIceBulletUltra),
                "Binguin_DefPatches_03".Translate());

            // 2) 冰爆状态：hediffClass 换自定义显示（百分比括号 + 浅蓝标签）
            HediffDef ice = DefDatabase<HediffDef>.GetNamedSilentFail("Binguin_IceBurst");
            if (ice == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_IceBurst hediff，冰爆 comp 未挂载。");
                return;
            }
            ice.hediffClass = typeof(Hediff_BinguinIceBurst);
            // 3) 冰爆状态 comp（满 100% 纯随机冻碎部位，含头部）
            if (ice.comps == null)
            {
                ice.comps = new List<HediffCompProperties>();
            }
            ice.comps.Add(new HediffCompProperties_BinguinIceBurst());
            Log.Message("[冰鹅族] 冰爆已挂载：Hediff_BinguinIceBurst（(36%) 进度 + 浅蓝）+ HediffComp_BinguinIceBurst（满 100% 纯随机冻碎部位，静默）");
        }

        // ====================================================================
        // 臭鱼罐头 · 自检（2026-10-05 重做后简化）
        //
        //   ★★ 历史：原来这里叫 `PatchStinkFish()`，干两件事：
        //     ① 给 HediffDef `Binguin_StinkFishSmell` 加
        //        HediffCompProperties_ThoughtSetter（原版）+ 自定义 HediffComp
        //     ② 把它的 `hediffClass` 换成自定义的 `Hediff_BinguinStinkFish`
        //     **结果：那个 hediff 从未被加到任何 pawn 上**（存档 0 次），
        //     诊断实测条件明明满足（`density=11`、智人、有 needs/mood）却就是不生效。
        //     整条"自定义 Hediff 自我激活"的路子已废弃、相关代码已删
        //     （Hediff_BinguinStinkFish / HediffCompProperties_BinguinStinkFish，
        //      备份在 `W:\dsh\zengbing\_backup_StinkFishComp_Binguin.cs.bak`）。
        //
        //   ★ 现在完全由 `ThoughtWorker_BinguinStinkFish` 承载心情（见
        //     Source/StinkFishThought_Binguin.cs），XML 里直接用 `workerClass` 指定。
        //     这里只保留一段启动自检 + 日志，方便一眼看出臭鱼罐头有没有加载。
        // ====================================================================
        private static void VerifyStinkFish()
        {
            ThoughtDef smellThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Binguin_StinkFishSmell");
            if (smellThought == null)
            {
                // 没装 Odyssey 时 AdvancedFishing 文件夹不加载 ⇒ 这里必然找不到，
                // 属于预期情况，用 Message 而不是 Warning。
                Log.Message("[冰鹅族] 未找到 Binguin_StinkFishSmell（未装 Odyssey？）"
                    + "——臭鱼罐头相关内容未加载，其余内容不受影响。");
                return;
            }

            // 自检：workerClass 必须是我们的自定义 ThoughtWorker，否则心情不会生效
            bool workerOk = smellThought.workerClass == typeof(ThoughtWorker_BinguinStinkFish);
            int stageCount = smellThought.stages != null ? smellThought.stages.Count : 0;
            float mood = stageCount > 0 ? smellThought.stages[0].baseMoodEffect : 0f;

            if (!workerOk)
            {
                Log.Warning("[冰鹅族] Binguin_StinkFishSmell 的 workerClass 不是 "
                    + typeof(ThoughtWorker_BinguinStinkFish).Name + "（实际="
                    + (smellThought.workerClass == null ? "null" : smellThought.workerClass.Name)
                    + "）⇒ 臭鱼味心情不会生效！");
                return;
            }

            Log.Message("[冰鹅族] 臭鱼罐头已挂载："
                + "炮弹 Binguin_StinkFishCan（烹饪台：1 任意鱼 + 20 钢铁，需科技：增冰鹅鹅）"
                + " / 陷阱 Binguin_StinkFishCanTrap"
                + " / 气体 RotStink"
                + " / 心情 ThoughtDef Binguin_StinkFishSmell（worker="
                + typeof(ThoughtWorker_BinguinStinkFish).Name
                + "，段数=" + stageCount + "，baseMoodEffect=" + mood
                + "，离开臭气 " + ThoughtWorker_BinguinStinkFish.LingerHours + " 小时后消失）");
        }

        // 换 workerClass 并清 DamageDef.workerInt 缓存
        private static void MountIceWorker(string defName, Type workerType, string desc)
        {
            DamageDef dd = DefDatabase<DamageDef>.GetNamedSilentFail(defName);
            if (dd == null)
            {
                Log.Message("[冰鹅族] 未找到 " + defName + "（" + desc + "未挂载）。");
                return;
            }
            dd.workerClass = workerType;
            FieldInfo wf = AccessTools.Field(typeof(DamageDef), "workerInt");
            if (wf != null)
            {
                wf.SetValue(dd, null);
            }
            Log.Message("[冰鹅族] 冰爆 worker 已挂载：" + defName + " -> " + workerType.Name + "（" + desc + "，护甲不削减）");
        }

        // 采切石一体机：添加自动采石/切石 Comp（comps 是 public）
        private static void PatchAutoStoneMachine()
        {
            ThingDef machine = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_AutoStoneMachine");
            if (machine == null)
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_AutoStoneMachine，一体机 Comp 未挂载。");
                return;
            }
            CompProperties_BinguinAutoStone comp = new CompProperties_BinguinAutoStone();
            comp.workIntervalTicks = 5000;   // 2 小时一块
            comp.bricksPerStone = 20;
            machine.comps.Add(comp);
            Log.Message("[冰鹅族] 一体机自动采石 Comp 已挂载。");
        }

        // 企鹅滑板：添加 hediff Comp + 滑行冲刺命令 Comp
        //（2026-08-19 终版 v5：命令按钮 = CompGetWornGizmosExtra 返回自定义
        //   Command_BinguinSlide（v3 实测显示正常）；范围/施法 = BeginTargeting
        //   verb 重载（Targeter 画圈 + TryCastShot 派滑行 job）。
        //   不再往 def.verbs 挂 Verb —— 1.6 的 apparel verbs 命令在穿戴后不生成
        //   （8-18 与 8-19 两次实测按钮不显示）。）
        private static void PatchPenguinBoard()
        {
            ThingDef board = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_PenguinBoard");
            if (board == null)
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_PenguinBoard，滑板补丁未挂载。");
                return;
            }

            // 1) hediff Comp（穿戴 → 滑板疾行 +20% 移速）
            HediffDef speedHediff = DefDatabase<HediffDef>.GetNamedSilentFail("Binguin_BoardSpeed");
            if (speedHediff != null)
            {
                CompProperties_ApparelGrantHediff comp = new CompProperties_ApparelGrantHediff();
                comp.hediffDef = speedHediff;
                board.comps.Add(comp);
            }

            // 2) 滑行冲刺命令 Comp
            CompProperties_BinguinBoard boardComp = new CompProperties_BinguinBoard();
            boardComp.range = 10f;
            boardComp.cooldownTicks = 600;
            board.comps.Add(boardComp);

            // 3) 脚下滑板绘制 Comp（穿戴时体现在人物脚下）
            board.comps.Add(new CompProperties_BinguinBoardDraw());

            Log.Message("[冰鹅族] 企鹅滑板 hediff Comp、命令 Comp 与脚下滑板 Comp 已挂载。");
        }

        // 滑行 job：driverClass 由代码挂载（XML 零自定义类型，避免 GenTypes 缓存问题）
        private static void PatchSlideJob()
        {
            JobDef slideJob = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_Slide");
            if (slideJob == null)
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_Slide job，driverClass 未挂载。");
                return;
            }
            slideJob.driverClass = typeof(JobDriver_Slide);
            Log.Message("[冰鹅族] 滑行 job driverClass 已挂载（JobDriver_Slide）。");
        }

        // 贴地滑行飞行器（2026-08-19 终极方案）：
        // XML 里 workerClass 用原版 PawnFlyerWorker 占位（零自定义类型），
        // 这里换成自定义 PawnFlyerWorker_BinguinSlide（GetHeight 恒 0）。
        // workerClass 是 private 字段，用 AccessTools 反射写。
        private static void PatchBoardFlyer()
        {
            ThingDef flyer = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_BoardFlyer");
            if (flyer == null)
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_BoardFlyer，贴地飞行器未挂载。");
                return;
            }
            if (flyer.pawnFlyer == null)
            {
                Log.Warning("[冰鹅族] Binguin_BoardFlyer 缺少 pawnFlyer 属性，贴地 worker 未挂载。");
                return;
            }
            FieldInfo workerField = AccessTools.Field(typeof(PawnFlyerProperties), "workerClass");
            if (workerField == null)
            {
                Log.Warning("[冰鹅族] 找不到 PawnFlyerProperties.workerClass 字段，贴地 worker 未挂载。");
                return;
            }
            workerField.SetValue(flyer.pawnFlyer, typeof(PawnFlyerWorker_BinguinSlide));
            Log.Message("[冰鹅族] 贴地滑行飞行器已挂载：workerClass = PawnFlyerWorker_BinguinSlide（GetHeight=0，完全贴地）");
        }

        // 垃圾回收器（2026-08-19 v2）：原版原子化器机制 + 自定义产出。
        // XML 里 comps 用原版 CompProperties_Atomizer（thingDef=Wastepack,
        // stackLimit=25, ticksPerAtomize=20000=8小时）占位（零自定义类型），
        // 这里把 compClass 换成 CompBinguinRecycler（继承 CompAtomizer，
        // 自实现 CompTick 回收计时+产出矿物）。原版 WorkGiver_HaulToAtomizer
        // 按 CompProperties_Atomizer.thingDef 自动搬运垃圾进来。
        private static void PatchRecycler()
        {
            ThingDef recycler = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_Recycler");
            if (recycler == null)
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_Recycler，回收器 Comp 未挂载。");
                return;
            }
            CompProperties_Atomizer atomizerProps = null;
            for (int i = 0; i < recycler.comps.Count; i++)
            {
                CompProperties_Atomizer cp = recycler.comps[i] as CompProperties_Atomizer;
                if (cp != null)
                {
                    atomizerProps = cp;
                    break;
                }
            }
            if (atomizerProps == null)
            {
                Log.Warning("[冰鹅族] Binguin_Recycler 缺少 CompProperties_Atomizer，回收器 Comp 未挂载。");
                return;
            }
            atomizerProps.compClass = typeof(CompBinguinRecycler);
            Log.Message("[冰鹅族] 垃圾回收器 Comp 已挂载：CompBinguinRecycler（25 垃圾 / 8h 回收 1 个 / 每次 5-10 矿物）");
        }

        // 低温围攻（2026-09）：大气冷冻器挂降温 Comp + 把策略 worker 换成我们的子类。
        // ★ XML 里 workerClass 写的是原版 RaidStrategyWorker_Siege（保持"XML 零自定义类型"惯例），
        //   这里在静态构造里替换成 RaidStrategyWorker_BinguinCryoSiege（多加一道"仅冰鹅族可用"的门）。
        // 毒垃圾报复（2026-09 用户需求）：毒血 hediff 的 comp。
        // XML 里 HediffDef 故意不写 comps（本 MOD「XML 零自定义类型」惯例），
        // 运行时在这里挂。数值也放在这里（和一体机/大气控制仪同款做法）：
        //   ticksToDeath 3750 = 1.5 小时（用户要求 1~2 小时致死）
        private static void PatchWasteToxemia()
        {
            HediffDef tox = DefDatabase<HediffDef>.GetNamedSilentFail("Binguin_WasteToxemia");
            if (tox == null)
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_WasteToxemia，毒垃圾报复的爆炸羊不会自灭。");
                return;
            }
            if (tox.comps == null)
            {
                tox.comps = new List<HediffCompProperties>();
            }
            tox.comps.Add(new HediffCompProperties_BinguinWasteToxemia
            {
                ticksToDeath = 3750,
                makeManhunter = true
            });
            Log.Message("[冰鹅族] 毒血 hediff 已挂载：CompBinguinWasteToxemia（1.5 小时失血致死 + 落地发狂）");
        }

        private static void PatchCryoSiege()
        {
            ThingDef freezer = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_AtmosphereFreezer");
            if (freezer == null)
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_AtmosphereFreezer，低温围攻建筑 Comp 未挂载。");
            }
            else
            {
                freezer.comps.RemoveAll(delegate(CompProperties c) { return c is CompProperties_Power; });
                freezer.comps.Add(new CompProperties_BinguinCryoFreezer());
                Log.Message("[冰鹅族] 大气冷冻器 Comp 已挂载：CompBinguinCryoFreezer（-30℃ 全图降温）");
            }
            // GameCondition 换成我们自己的子类（自带降温斜坡 + 摧毁后 2 小时回暖）
            GameConditionDef cond = DefDatabase<GameConditionDef>.GetNamedSilentFail("Binguin_CryoFreeze");
            if (cond != null)
            {
                cond.conditionClass = typeof(GameCondition_BinguinCryoFreeze);
                Log.Message("[冰鹅族] Binguin_CryoFreeze 已换成 GameCondition_BinguinCryoFreeze（降温 −10℃/h，摧毁后 2 小时回暖）");
            }
            else
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_CryoFreeze 天气条件。");
            }
            RaidStrategyDef strat = DefDatabase<RaidStrategyDef>.GetNamedSilentFail("Binguin_CryoSiege");
            if (strat != null)
            {
                strat.workerClass = typeof(RaidStrategyWorker_BinguinCryoSiege);
                Log.Message("[冰鹅族] 低温围攻策略 worker 已替换：RaidStrategyWorker_BinguinCryoSiege（仅冰鹅族可用）");
            }
            else
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_CryoSiege 策略。");
            }
        }

        // 大气控制仪（2026-08-19）：自定义天气控制 Comp。
        // XML 用原版 CompProperties_Power 占位，这里追加 CompBinguinWeather
        //（comps 是 public，直接 Add）。
        private static void PatchAtmosphereController()
        {
            ThingDef controller = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_AtmosphereController");
            if (controller == null)
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_AtmosphereController，大气控制 Comp 未挂载。");
                return;
            }
            CompProperties_BinguinWeather weather = new CompProperties_BinguinWeather();
            // ★ 2026-09 用户修订：改为「充能 24 小时才能用一次，充满自动低耗能」。
            //   显式赋值必须用负值（消耗）！正数会变成发电（2026-08-19 踩过）。
            weather.rechargeTicks = 60000;      // 24 小时（1 小时 = 2500 tick）
            weather.chargePower = -700f;        // 充能功耗 700W（2026-09 用户定稿）
            weather.chargedIdlePower = -10f;    // 充满后低耗能待机 10W
            controller.comps.Add(weather);
            Log.Message("[冰鹅族] 大气控制仪 Comp 已挂载：CompBinguinWeather（充能 24h/次，满后 10W 低耗能）");
        }

        // 陨石引导器（2026-08-19 星空眺望 v5）：扫描工作管线。
        // XML 里 comps 用原版 CompProperties_Scanner 占位（scanSpeedStat=ResearchSpeed，
        // 殖民者研究工作驱动进度），这里把 compClass 换成 CompBinguinMeteorGuide
        // （继承 CompScanner 基类——无 bedrock 检查，冰盖可用不弹提示）。
        private static void PatchMeteorGuide()
        {
            ThingDef guide = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_MeteorGuide");
            if (guide == null)
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_MeteorGuide，陨石引导 Comp 未挂载。");
                return;
            }
            CompProperties_Scanner scannerProps = null;
            for (int i = 0; i < guide.comps.Count; i++)
            {
                CompProperties_Scanner cp = guide.comps[i] as CompProperties_Scanner;
                if (cp != null)
                {
                    scannerProps = cp;
                    break;
                }
            }
            if (scannerProps == null)
            {
                Log.Warning("[冰鹅族] Binguin_MeteorGuide 缺少 CompProperties_Scanner，陨石引导 Comp 未挂载。");
                return;
            }
            scannerProps.compClass = typeof(CompBinguinMeteorGuide);
            scannerProps.scanFindMtbDays = 999f;        // 不按 MTB 判定（用 10% 制）
            scannerProps.scanFindGuaranteedDays = 3f;   // 保底：3 标准工作天满进度必发现
            Log.Message("[冰鹅族] 陨石引导器 Comp 已挂载：CompBinguinMeteorGuide（研究工作驱动/10% 判定/20 存储/10 天过期/信件）");
        }

        // 进阶钓鱼学（2026-08-19，奥德赛 DLC 专属）：蟹笼 + 打窝用具 + 量产型钓竿。
        // defs 在 AdvancedFishing/ 文件夹，由 LoadFolders.xml 门控，
        // 仅 Odyssey 激活时加载 → 这里 GetNamedSilentFail 拿不到就是没加载，静默跳过。
        // ★ 静态构造里任何未捕获异常都会中断整个静态构造 → 后续挂载全部失效
        //   （交接文档 §4.1 教训），整体 try/catch 防御。
        private static void PatchFishing()
        {
            try
            {
                PatchFishingInner();
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 进阶钓鱼学挂载失败：" + ex);
            }
        }

        private static void PatchFishingInner()
        {
            // 1) 蟹笼：捕鱼 Comp + 水域/冰盖 PlaceWorker
            ThingDef trap = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_CrabTrap");
            if (trap == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_CrabTrap（奥德赛未激活？），蟹笼未挂载。");
            }
            else
            {
                CompProperties_BinguinCrabTrap trapComp = new CompProperties_BinguinCrabTrap();
                trapComp.intervalTicks = 15000;   // 6 小时（6:00/12:00/18:00/24:00）
                // ★ comps 是 List，XML 没写 comps 节点时默认 null，必须判空！
                if (trap.comps == null)
                {
                    trap.comps = new List<CompProperties>();
                }
                trap.comps.Add(trapComp);
                // ★ placeWorkers 是 List<Type>，XML 没写时默认 null，必须判空初始化！
                //   （否则 Add 抛 NRE → 静态构造中断 → 后面的 giverClass 替换全没执行 →
                //     WorkGiverDef 占位类 WorkGiver_Scanner 是 abstract → 每帧实例化异常）
                if (trap.placeWorkers == null)
                {
                    trap.placeWorkers = new List<Type>();
                }
                trap.placeWorkers.Add(typeof(PlaceWorker_BinguinWaterArea));
                Log.Message("[冰鹅族] 蟹笼 Comp 已挂载：CompBinguinCrabTrap（每 6h 捕鱼 / 任意位置可放，水域外全球鱼种）");
            }

            // 2) 打窝用具：XML 用原版 CompProperties_ThingContainer 占位，
            //    这里换 compClass（props 保持原版类型，自定义值用常量）；
            //    + 水域/每水域 2 个上限的 PlaceWorker
            ThingDef bait = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_FishingBait");
            if (bait == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_FishingBait（奥德赛未激活？），打窝用具未挂载。");
            }
            else
            {
                bool found = false;
                for (int i = 0; i < bait.comps.Count; i++)
                {
                    CompProperties_ThingContainer cp = bait.comps[i] as CompProperties_ThingContainer;
                    if (cp != null)
                    {
                        cp.compClass = typeof(CompBinguinFishingBait);
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Log.Warning("[冰鹅族] Binguin_FishingBait 缺少 CompProperties_ThingContainer，打窝用具未挂载。");
                }
                if (bait.placeWorkers == null)
                {
                    bait.placeWorkers = new List<Type>();
                }
                bait.placeWorkers.Add(typeof(PlaceWorker_BinguinBaitLimit));
                Log.Message("[冰鹅族] 打窝用具 Comp 已挂载：CompBinguinFishingBait（营养 5 / 每天耗 1 / +50% 增速，每水域限 2 个）");
            }

            // 3) 打窝投喂 WorkGiver：XML giverClass 用原版具体类 WorkGiver_Researcher 占位
            //    （WorkGiver_Scanner 是 abstract，占位会导致每帧实例化异常）
            WorkGiverDef fillBait = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Binguin_FillFishingBait");
            if (fillBait == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_FillFishingBait（奥德赛未激活？），打窝投喂未挂载。");
            }
            else
            {
                fillBait.giverClass = typeof(WorkGiver_BinguinFillBait);
                // ★ workerInt 是 Worker 属性的懒加载缓存：若在静态构造前已被
                //   占位类型实例化过，替换 giverClass 不会生效 → 反射清缓存
                FieldInfo workerField = WorkGiverWorkerIntField;
                if (workerField != null)
                {
                    workerField.SetValue(fillBait, null);
                }
                Log.Message("[冰鹅族] 打窝投喂 WorkGiver 已挂载：WorkGiver_BinguinFillBait");
            }

            // 3b) 半成品钓竿【自动续做】WorkGiver（2026-10-06 用户需求）
            //     用户原文：「记得绑定制作者，制作者工作时会自动制作鱼竿」。
            //     没有这个 giver 的话，自定义 job 不会被工作分配系统捡起来 ——
            //     玩家不点「继续装配」就永远没人做。
            WorkGiverDef unfinishedRodWork = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Binguin_WorkOnUnfinishedRod");
            if (unfinishedRodWork == null)
            {
                Log.Warning("[冰鹅族] 未找到 WorkGiverDef Binguin_WorkOnUnfinishedRod"
                    + "（Defs/WorkGivers_Binguin.xml 没加载？），半成品不会自动续做！");
            }
            else
            {
                unfinishedRodWork.giverClass = typeof(WorkGiver_BinguinUnfinishedRod);
                // ★ 同 fillBait：`workerInt` 是懒加载缓存，若已被占位类型实例化过，
                //   替换 giverClass 不会生效 → 反射清缓存
                FieldInfo rodWorkerField = WorkGiverWorkerIntField;
                if (rodWorkerField != null)
                {
                    rodWorkerField.SetValue(unfinishedRodWork, null);
                }
                Log.Message("[冰鹅族] 半成品钓竿自动续做 WorkGiver 已挂载："
                    + "WorkGiver_BinguinUnfinishedRod（工作类型 = Crafting）");
            }

            // 4) 量产型钓竿：equippedStatOffsets（FishingYield +0.5）直接写在 XML，无需补丁
            ThingDef rod = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_FishingRod");
            if (rod != null)
            {
                Log.Message("[冰鹅族] 量产型钓竿已就位：近战（竿柄 Poke 18 / 竿头 Blunt 25）+ 钓鱼收获率 +50%");
            }

            // 5) 鱼池（2026-08-20）：养殖 comp + 鱼食容器 + job driverClass
            ThingDef pond = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_FishPond");
            if (pond == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_FishPond（奥德赛未激活？），鱼池未挂载。");
            }
            else
            {
                // 5a) 鱼食槽：XML 用原版 CompProperties_ThingContainer 占位
                //     （继承结构：CompBinguinPondFeed : CompThingContainer），
                //     这里换 compClass，实现 IStoreSettingsParent（ITab_Storage
                //      手动筛选鱼食，同打窝用具）+ 营养换算/消耗接口
                bool feedFound = false;
                if (pond.comps != null)
                {
                    for (int i = 0; i < pond.comps.Count; i++)
                    {
                        CompProperties_ThingContainer cp = pond.comps[i] as CompProperties_ThingContainer;
                        if (cp != null)
                        {
                            cp.compClass = typeof(CompBinguinPondFeed);
                            feedFound = true;
                            break;
                        }
                    }
                }
                if (!feedFound)
                {
                    Log.Warning("[冰鹅族] Binguin_FishPond 缺少 CompProperties_ThingContainer，鱼食槽未挂载。");
                }
                // 5b) 养殖 comp（组管理/放苗/钓鱼/繁殖/蟹笼/保留下限）
                if (pond.comps == null)
                {
                    pond.comps = new List<CompProperties>();
                }
                pond.comps.Add(new CompProperties_BinguinFishPond());
                Log.Message("[冰鹅族] 鱼池 Comp 已挂载：CompBinguinFishPond + CompBinguinPondFeed（20 条/池，相连合并，内置钓鱼/鱼食/蟹笼模块，鱼食 ITab 筛选同打窝）");

                // 5c) 连接材质（2026-08-20 用户需求：相连池子材质变化连成一体）
                //     ★ v1（graphicClass 替换）会让放置时的蓝图 ghost 管线
                //       NRE（GhostGraphicFor）→ 无法放置（Player.log 实测）。
                //     v2：def.graphic 保持原版 Graphic_Single（ghost/UI 全正常），
                //       只换 thingClass → Building_BinguinPond 覆写 Print()：
                //       地图网格打印时按连接 mask 选 FishPond_l0..l14 变体
                //       （相连侧池沿开口=水面相接，整组视觉一个大池）。
                try
                {
                    pond.thingClass = typeof(Building_BinguinPond);
                    Log.Message("[冰鹅族] 鱼池连接材质已挂载：Building_BinguinPond.Print（相连侧池沿开口，视觉连成一个大池）");
                }
                catch (Exception gex)
                {
                    Log.Warning("[冰鹅族] 鱼池连接材质挂载失败（保持原贴图）：" + gex);
                }
            }
            JobDef pondFishJob = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_PondFish");
            if (pondFishJob == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_PondFish job（奥德赛未激活？），钓鱼未挂载。");
            }
            else
            {
                pondFishJob.driverClass = typeof(JobDriver_BinguinPondFish);
                Log.Message("[冰鹅族] 鱼池钓鱼 job 已挂载：JobDriver_BinguinPondFish");
            }

            // 6) 鱼池自动钓鱼工作（2026-08-20）：接入原版 Fishing 工作类型，
            //    殖民者开钓鱼工作后自动来鱼池垂钓（低于保留鱼数自动停）
            WorkGiverDef pondFishWork = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Binguin_PondFishWork");
            if (pondFishWork == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_PondFishWork（奥德赛未激活？），自动钓鱼未挂载。");
            }
            else
            {
                pondFishWork.giverClass = typeof(WorkGiver_BinguinPondFishing);
                FieldInfo workerF = WorkGiverWorkerIntField;
                if (workerF != null)
                {
                    workerF.SetValue(pondFishWork, null);
                }
                Log.Message("[冰鹅族] 鱼池自动钓鱼工作已挂载：WorkGiver_BinguinPondFishing（原版 Fishing 工作）");
            }

            // 6.5) ★ 2026-10-06 用户需求：「把冰鹅族的钓鱼改为娱乐……
            //      并且在娱乐时会主动去钓鱼」
            //      JoyGiverDef 的 giverClass 用原版 JoyGiver_Meditate 占位
            //      （JoyGiver 是 abstract，占位成它会报异常）→ 这里替换。
            //      同时把 JobDef 的 joyKind / joyGainRate 兜底补上：
            //      XML 里已经写了，但万一被别的 mod 覆盖/漏加载，
            //      没有 joyKind 的话 JoyTickCheckEnd 会直接 Warning 并返回
            //      ⇒ 娱乐条一动不动。
            JobDef pondFishJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_PondFish");
            if (pondFishJobDef != null)
            {
                if (pondFishJobDef.joyKind == null)
                {
                    JoyKindDef meditative = DefDatabase<JoyKindDef>.GetNamedSilentFail("Meditative");
                    if (meditative != null)
                    {
                        pondFishJobDef.joyKind = meditative;
                        Log.Warning("[冰鹅族] Binguin_PondFish 的 joyKind 缺失，已兜底补成 Meditative。");
                    }
                    else
                    {
                        Log.Error("[冰鹅族] 找不到 JoyKindDef Meditative，钓鱼无法加娱乐条！");
                    }
                }
                if (pondFishJobDef.joyGainRate <= 0f)
                {
                    pondFishJobDef.joyGainRate = 1f;
                }
            }

            JoyGiverDef pondFishJoy = DefDatabase<JoyGiverDef>.GetNamedSilentFail("Binguin_PondFishingJoy");
            if (pondFishJoy == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_PondFishingJoy（奥德赛未激活？），钓鱼娱乐未挂载。");
            }
            else
            {
                pondFishJoy.giverClass = typeof(JoyGiver_BinguinPondFishing);
                if (JoyGiverWorkerIntField != null)
                {
                    JoyGiverWorkerIntField.SetValue(pondFishJoy, null);
                }
                Log.Message("[冰鹅族] 钓鱼【娱乐】已挂载：JoyGiver_BinguinPondFishing"
                    + "（仅冰鹅族；娱乐条低时主动去鱼池钓鱼）");
            }

            // 7) 放苗 job（2026-08-20）：殖民者搬运 1 条鱼放入鱼池
            JobDef pondStockJob = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_PondStock");
            if (pondStockJob == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_PondStock job（奥德赛未激活？），放苗未挂载。");
            }
            else
            {
                pondStockJob.driverClass = typeof(JobDriver_BinguinPondStock);
                Log.Message("[冰鹅族] 放苗 job 已挂载：JobDriver_BinguinPondStock（殖民者搬运鱼苗入池）");
            }

            // 8) 鱼食自动投喂工作（2026-08-20）：接入原版 Hauling 工作类型，
            //    殖民者自动把 ITab 筛选允许的鱼食搬进鱼池鱼食槽
            WorkGiverDef pondFeedWork = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Binguin_PondFeedWork");
            if (pondFeedWork == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_PondFeedWork（奥德赛未激活？），鱼食投喂未挂载。");
            }
            else
            {
                pondFeedWork.giverClass = typeof(WorkGiver_BinguinPondFeed);
                FieldInfo workerF2 = WorkGiverWorkerIntField;
                if (workerF2 != null)
                {
                    workerF2.SetValue(pondFeedWork, null);
                }
                Log.Message("[冰鹅族] 鱼食自动投喂工作已挂载：WorkGiver_BinguinPondFeed（同打窝用具，Hauling 工作）");
            }
        }

        // 高级钓鱼学（2026-08-19 用户设计文档）：钓竿装配台 + 配件 + 合成系统。
        // ★ 全部动态机制：StatPart 注册到原版 stat（伤害/冷却/移速/毒性/免疫/自愈/耐久），
        //   自定义 Comp 由静态构造挂载，ThoughtDef workerClass 替换。
        private static void PatchMasterFishing()
        {
            try
            {
                PatchMasterFishingInner();
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 高级钓鱼学挂载失败：" + ex);
            }
        }

        private static void PatchMasterFishingInner()
        {
            // 1) 装配台：合成/制饵 comp（gizmo 打开自定义对话框）
            ThingDef table = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_RodAssemblyTable");
            if (table == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_RodAssemblyTable（奥德赛未激活？），装配台未挂载。");
            }
            else
            {
                if (table.comps == null)
                {
                    table.comps = new List<CompProperties>();
                }
                table.comps.Add(new CompProperties_BinguinRodAssembly());
                Log.Message("[冰鹅族] 钓竿装配台 Comp 已挂载：CompBinguinRodAssembly（合成钓竿 / 制作鱼饵对话框）");
            }

            // 2) 鱼饵：效果 comp（合成时读取）
            ThingDef bait = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_RodBait");
            if (bait == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_RodBait（奥德赛未激活？），鱼饵未挂载。");
            }
            else
            {
                if (bait.comps == null)
                {
                    bait.comps = new List<CompProperties>();
                }
                bait.comps.Add(new CompProperties_BinguinRodBait());
                Log.Message("[冰鹅族] 鱼饵 Comp 已挂载：CompBinguinRodBait（1~2 种食材效果）");
            }

            // 3) 两种高级钓竿：把 BaseWeapon 继承来的 CompEquippable 替换成
            //    CompBinguinRod（继承 CompEquippable → 装备 gizmo 收集链生效）
            //    ★ 不能再 Add 一个独立 comp——装备 gizmo 只调主武器的
            //      CompEquippable.CompGetEquippedGizmosExtra（1.6 反编译确认）
            string[] rodDefs = new string[] { "Binguin_AdvancedRod_Blade", "Binguin_AdvancedRod_Hammer" };
            int rodCount = 0;
            for (int i = 0; i < rodDefs.Length; i++)
            {
                ThingDef rod = DefDatabase<ThingDef>.GetNamedSilentFail(rodDefs[i]);
                if (rod == null)
                {
                    continue;
                }
                // ★ 2026-09 修复+清理：XML 没写 <comps> 时 ThingDef.comps 默认是
                //   null，而下面的循环会直接读 rod.comps.Count → NRE 被外层 catch
                //   吞掉，整个钓竿 comp 替换会静默失败。原来的判空写在循环【之后】
                //   等于死代码；现在提到循环之前（当前 XML 有 <comps>，行为不变）。
                if (rod.comps == null)
                {
                    rod.comps = new List<CompProperties>();
                }
                bool replaced = false;
                for (int j = 0; j < rod.comps.Count; j++)
                {
                    CompProperties cp = rod.comps[j];
                    if (cp != null && cp.compClass != null && cp.compClass == typeof(CompEquippable))
                    {
                        cp.compClass = typeof(CompBinguinRod);
                        replaced = true;
                        break;
                    }
                }
                if (!replaced)
                {
                    rod.comps.Add(new CompProperties_BinguinRod());
                }
                rodCount++;
            }
            if (rodCount > 0)
            {
                Log.Message("[冰鹅族] 高级钓竿 Comp 已挂载：CompBinguinRod（" + rodCount + " 种，配件属性动态计算）");
            }

            // 3.5) 技能（Ability 系统，Bladelink 式）：★ 不能用武器 verb
            //    （ThingDef.verbs 会让鱼竿变成远程武器——用户反馈！），
            //    技能 = AbilityDef + CompAbilityEffect（装备时 Notify_Equipped
            //    → pawn.abilities.GainAbility，命令栏显示能力按钮）
            AbilityDef pierceAbility = DefDatabase<AbilityDef>.GetNamedSilentFail("Binguin_AbilityPierce");
            if (pierceAbility == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_AbilityPierce（奥德赛未激活？），穿刺技能未挂载。");
            }
            else
            {
                if (pierceAbility.comps == null)
                {
                    pierceAbility.comps = new List<AbilityCompProperties>();
                }
                bool hasPierce = false;
                for (int i = 0; i < pierceAbility.comps.Count; i++)
                {
                    if (pierceAbility.comps[i] != null && pierceAbility.comps[i].compClass == typeof(CompAbilityEffect_BinguinPierce))
                    {
                        hasPierce = true;
                        break;
                    }
                }
                if (!hasPierce)
                {
                    pierceAbility.comps.Add(new AbilityCompProperties { compClass = typeof(CompAbilityEffect_BinguinPierce) });
                }
            }

            AbilityDef hookAbility = DefDatabase<AbilityDef>.GetNamedSilentFail("Binguin_AbilityHook");
            if (hookAbility == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_AbilityHook（奥德赛未激活？），钩鱼技能未挂载。");
            }
            else
            {
                if (hookAbility.comps == null)
                {
                    hookAbility.comps = new List<AbilityCompProperties>();
                }
                bool hasHook = false;
                for (int i = 0; i < hookAbility.comps.Count; i++)
                {
                    if (hookAbility.comps[i] != null && hookAbility.comps[i].compClass == typeof(CompAbilityEffect_BinguinHook))
                    {
                        hasHook = true;
                        break;
                    }
                }
                if (!hasHook)
                {
                    hookAbility.comps.Add(new AbilityCompProperties { compClass = typeof(CompAbilityEffect_BinguinHook) });
                }
            }
            Log.Message("[冰鹅族] 高级钓竿技能已挂载：穿刺（直钩 Ability）/ 钩鱼（弯钩 Ability），装备时加入命令栏");

            // 4) StatPart 注册：伤害/穿甲、冷却、移速、毒性、免疫、自愈、耐久
            string[] statNames = new string[]
            {
                "MeleeWeapon_DamageMultiplier",
                "MeleeWeapon_CooldownMultiplier",
                "MoveSpeed",
                "ToxicResistance",
                "ImmunityGainSpeed",
                "InjuryHealingFactor",
                "MaxHitPoints"
            };
            int statCount = 0;
            for (int i = 0; i < statNames.Length; i++)
            {
                StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(statNames[i]);
                if (stat == null)
                {
                    Log.Warning("[冰鹅族] 找不到 stat " + statNames[i] + "，StatPart 未注册。");
                    continue;
                }
                if (stat.parts == null)
                {
                    stat.parts = new List<StatPart>();
                }
                bool exists = false;
                for (int j = 0; j < stat.parts.Count; j++)
                {
                    if (stat.parts[j] is StatPart_BinguinRod)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    stat.parts.Add(new StatPart_BinguinRod(stat));
                    statCount++;
                }
            }
            if (statCount > 0)
            {
                Log.Message("[冰鹅族] 高级钓竿 StatPart 已注册（" + statCount + " 个 stat：伤害/穿甲/冷却/移速/毒性/免疫/自愈/耐久）");
            }

            // 5) 虫胶饵心情 Thought：workerClass 替换为自定义 ThoughtWorker
            ThoughtDef jellyThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Binguin_RodBaitJellyMood");
            if (jellyThought == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_RodBaitJellyMood（奥德赛未激活？），虫胶心情未挂载。");
            }
            else
            {
                jellyThought.workerClass = typeof(ThoughtWorker_BinguinRodBait);
                // ★ ThoughtDef.Worker 属性会把首个 worker 实例缓存进非 public 字段 workerInt。
                // 若游戏在本次挂载前已访问过该属性，缓存里仍是占位类 ThoughtWorker_AlwaysActive 的
                // 恒真实例，只改 workerClass 不会生效（导致不拿虫胶饵也 +3）。必须清空缓存强制重建。
                System.Reflection.FieldInfo workerCacheField = AccessTools.Field(typeof(ThoughtDef), "workerInt");
                if (workerCacheField != null)
                {
                    workerCacheField.SetValue(jellyThought, null);
                }
                Log.Message("[冰鹅族] 虫胶饵心情 Thought 已挂载：ThoughtWorker_BinguinRodBait（+3）");
            }

            // 6) 装配 job：driverClass 挂载（殖民者装配高级钓竿）
            JobDef assembleJob = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_AssembleRod");
            if (assembleJob == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_AssembleRod（奥德赛未激活？），装配 job 未挂载。");
            }
            else
            {
                assembleJob.driverClass = typeof(JobDriver_BinguinAssembleRod);
                Log.Message("[冰鹅族] 装配高级钓竿 job 已挂载：JobDriver_BinguinAssembleRod");
            }

            // 7) 【钓竿（未完成）】· 半成品（2026-10-06 用户需求）
            //    ① ThingDef `Binguin_UnfinishedRod` 挂容器 comp（XML 零自定义类型原则）
            //    ② JobDef `Binguin_WorkOnUnfinishedRod` 挂 driverClass
            ThingDef unfinishedRod = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_UnfinishedRod");
            if (unfinishedRod == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_UnfinishedRod，半成品功能未加载。");
            }
            else
            {
                if (unfinishedRod.comps == null)
                {
                    // ★ 判空必须在**读 comps 之前**（本文件踩过这个坑：写在后面是死代码，
                    //   XML 没写 <comps> 时会 NRE 被外层 catch 吞掉、整个挂载静默失败）
                    unfinishedRod.comps = new List<CompProperties>();
                }
                bool already = false;
                for (int i = 0; i < unfinishedRod.comps.Count; i++)
                {
                    if (unfinishedRod.comps[i] is CompProperties_BinguinUnfinishedRod)
                    {
                        already = true;
                        break;
                    }
                }
                if (!already)
                {
                    unfinishedRod.comps.Add(new CompProperties_BinguinUnfinishedRod());
                }
                Log.Message("[冰鹅族] 【钓竿（未完成）】已挂载："
                    + unfinishedRod.defName + " += CompBinguinUnfinishedRod"
                    + "（容器保管 4 配件 + 进度跟着物品走）");
            }

            JobDef workJob = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_WorkOnUnfinishedRod");
            if (workJob == null)
            {
                Log.Warning("[冰鹅族] 未找到 Binguin_WorkOnUnfinishedRod（Defs/13_WorkAndRecipes/JobDefs_Binguin.xml 没加载？），"
                    + "半成品无法继续装配！");
            }
            else
            {
                workJob.driverClass = typeof(JobDriver_BinguinWorkOnUnfinishedRod);
                Log.Message("[冰鹅族] 半成品工作 job 已挂载：JobDriver_BinguinWorkOnUnfinishedRod");
            }
        }

        // ====================================================================
        // 尚方宝剑（2026-10-06 用户需求）
        //   · 武器 comp：CompBinguinVoidSword（继承 CompEquippable，负责
        //     "14 条鱼轮流"的循环指针 + 装备时按 mod 选项授予远程技能）
        //   · 技能效果：CompAbilityEffect_BinguinVoidStrike（椭圆范围 14 连斩）
        //   · 斩击损伤：DamageDef Binguin_FishBluntDamage 的 workerClass
        //   ★ 全部走"XML 用原版占位 + 静态构造替换"的既有模式
        //     （XML 零自定义类型引用是本 mod 的铁律）。
        // ====================================================================
        private static void PatchVoidSword()
        {
            try
            {
                PatchVoidSwordInner();
            }
            catch (Exception ex)
            {
                Log.Error("[冰鹅族] 尚方宝剑挂载失败：" + ex);
            }
        }

        private static void PatchVoidSwordInner()
        {
            // 1) 武器 def：把 BaseWeapon 继承来的 CompEquippable 换成
            //    CompBinguinVoidSword（★ 不能另 Add 一个独立 comp —— 装备 gizmo
            //    只调主武器的 CompEquippable.CompGetEquippedGizmosExtra）
            ThingDef sword = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_VoidSwordFish");
            if (sword == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_VoidSwordFish（奥德赛未激活？），尚方宝剑未挂载。");
            }
            else
            {
                if (sword.comps == null)
                {
                    // ★ 判空必须在读 comps 之前（本文件踩过这个坑）
                    sword.comps = new List<CompProperties>();
                }
                bool replaced = false;
                for (int i = 0; i < sword.comps.Count; i++)
                {
                    CompProperties cp = sword.comps[i];
                    if (cp != null && cp.compClass != null && cp.compClass == typeof(CompEquippable))
                    {
                        cp.compClass = typeof(CompBinguinVoidSword);
                        replaced = true;
                        break;
                    }
                }
                if (!replaced)
                {
                    sword.comps.Add(new CompProperties_BinguinVoidSword());
                }
                Log.Message("[冰鹅族] 尚方宝剑 Comp 已挂载：CompBinguinVoidSword"
                    + "（14 条鱼轮流指针 + 装备时按 mod 选项授予远程技能）");
            }

            // 2) 技能效果：AbilityDef comps 里的占位（原版 CompAbilityEffect_Teleport）
            //    替换成 CompAbilityEffect_BinguinVoidStrike
            AbilityDef strike = DefDatabase<AbilityDef>.GetNamedSilentFail("Binguin_AbilityVoidStrike");
            if (strike == null)
            {
                Log.Message("[冰鹅族] 未找到 Binguin_AbilityVoidStrike（奥德赛未激活？），天顶鱼斩未挂载。");
            }
            else
            {
                if (strike.comps == null)
                {
                    strike.comps = new List<AbilityCompProperties>();
                }
                bool hasEffect = false;
                for (int i = 0; i < strike.comps.Count; i++)
                {
                    AbilityCompProperties acp = strike.comps[i];
                    if (acp == null)
                    {
                        continue;
                    }
                    // ★ 直接找到那个占位项并换成我们的效果类（保留同一条
                    //   CompProperties，这样数值参数用我们类的默认值）
                    if (acp.compClass == typeof(CompAbilityEffect_Teleport)
                        || acp.compClass == typeof(CompAbilityEffect_BinguinVoidStrike))
                    {
                        acp.compClass = typeof(CompAbilityEffect_BinguinVoidStrike);
                        hasEffect = true;
                        break;
                    }
                }
                if (!hasEffect)
                {
                    strike.comps.Add(new AbilityCompProperties
                    {
                        compClass = typeof(CompAbilityEffect_BinguinVoidStrike)
                    });
                }
                Log.Message("[冰鹅族] 天顶鱼斩技能已挂载：CompAbilityEffect_BinguinVoidStrike"
                    + "（14 连斩 / 椭圆范围 / 100% 钝器穿透；默认关闭，见 mod 选项）");
            }

            // 3) 斩击损伤 worker：Binguin_FishBluntDamage
            //    （XML 里 workerClass 用原版 DamageWorker_Blunt 占位）
            MountIceWorker("Binguin_FishBluntDamage", typeof(DamageWorker_BinguinFishBlunt),
                "尚方宝剑·鱼斩（钝击 + 面板伤害 1:1 累积冰爆）");
        }
    }
}
