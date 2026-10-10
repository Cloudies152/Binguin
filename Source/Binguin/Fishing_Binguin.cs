// ============================================================================
// 冰鹅族【钓到企鹅！】—— 钓鱼时有极小概率钓上一名冰鹅族难民（2026-09-24 用户需求）
//
// 用户需求原文：
//   「钓鱼有 0.78% 概率钓出冰鹅族（拥有麻痹症 2 天）。同时可以钓出冰鹅族的冷却为 60 天。
//     这个冰鹅族属于难民，可以选择帮助进行招募，也可以单救援不招募。
//     除了领袖外都有概率钓出来。」
//
// ★★ 挂点（1.6 IL 实证，别重新考证）：
//   · 原版钓鱼：`RimWorld.FishingUtility.GetCatchesFor(Pawn, IntVec3, bool, out bool)`
//     —— 原版自己的"稀有渔获"就在这个方法里判定（`lastRareCatchTick` + Rand.Chance(0.01f)
//         + 300000 tick 冷却 + 出参 isRareCatch）。我们【完全照抄这个形态】，
//        只是把概率换成 0.78%、冷却换成 60 天、产出换成一个倒地冰鹅难民。
//     · 本 mod 鱼池：`JobDriver_BinguinPondFish` 的收尾动作（原来直接 comp.TryCatchOne()）。
//   ⇒ 两处都走同一个 `BinguinFishingCatchUtility.TryReplaceCatch(...)`，
//     共用同一套 60 天冷却（全局，存 GameComponent）。
//
// ★★ 为什么【故意不挂蟹笼】`Comp_BinguinCrabTrap`：
//   蟹笼是"每 catchIntervalTicks（15000 tick ≈ 5 小时）自动捕一次"的被动产出，
//   玩家完全不用操作。如果它也参与 0.78% 判定，就等于把一次"钓到企鹅"的惊喜
//   摊成一台挂机刷冰鹅的机器（概率上平均约 5 天多就出一次，远快于玩家手动钓鱼），
//   而且蟹笼常常放在野外冰面上没人看着 —— 这不符合"钓鱼钓上来"的设定。
//   ⇒ 只保留玩家主动的两条路径：原版钓鱼区 + 本 mod 鱼池。
//
// ★★ 为什么冷却和"待生成队列"必须放在 GameComponent 里：
//   ① GameComponent 会被 `Game.FillComponents()` 用
//      `GenTypes.AllSubclassesNonAbstract(typeof(GameComponent))` + `Activator.CreateInstance(type, game)`
//      **自动实例化**（IL 实证）⇒ 不需要在 XML 里注册，只要 public 且构造函数是 (Game)。
//   ② 生成 pawn 不能在 GetCatchesFor 里做（那时还在原版方法内部，池子/地图状态都不该动），
//      所以只入队，等 GameComponentUpdate 时再生成。
//   ③ 60 天冷却是"全局"的：换地图/换水域都不重置（用户要的是"钓出冰鹅族的冷却"，不是"每片水域各一次"）。
//
// ★ 难民形态 = 照抄原版「坠机难民」的做法：
//   `DownedRefugeeQuestUtility.GenerateRefugee` 也是"一个随机派系的 pawn + allowDowned"。
//   我们这里更直接：和平派系（Binguin）的平民 + 麻痹症 ⇒ 小人会倒地（爬都爬不了），
//   玩家把他抬回去救起来就是原版那套「救援/招募」，不自己造 UI。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Binguin
{
    /// <summary>钓到冰鹅：概率 / 冷却 / 兵种池 / 生成难民，全在这里。</summary>
    public static class BinguinFishingCatchUtility
    {
        /// <summary>用户指定：0.78%。</summary>
        public const float CatchChance = 0.0078f;

        /// <summary>用户指定：60 天。1 天 = 60000 tick。</summary>
        public const int CooldownDays = 60;
        public const int CooldownTicks = CooldownDays * 60000;

        /// <summary>用户指定：麻痹症 2 天。</summary>
        public const int AbasiaDays = 2;
        public const int AbasiaTicks = AbasiaDays * 60000;

        /// <summary>
        /// 可钓出的兵种池 = 和平派系（Binguin）的平民，照抄 `Defs/03_PawnKinds/PawnKindDefs_Binguin.xml`
        /// 「和平类」那一节的 defName。
        /// ★ 刻意排除两个领袖：`Binguin_Leader`（&lt;factionLeader&gt;true&lt;/factionLeader&gt;）
        ///   与 `Binguin_CaravanLeader`（商队领袖）—— 用户要求"除了领袖外都有概率"。
        ///   另外在 PickKind() 里还会用 `kind.factionLeader` 兜一道，
        ///   以后谁给某个 kind 打上 factionLeader 也会自动被排除。
        /// </summary>
        public static readonly string[] CatchableKinds =
        {
            "Binguin_Settler",        // 定居者
            "Binguin_Diplomat",       // 外交官
            "Binguin_Merchant",       // 商人
            "Binguin_CoserNine",      // 冰鹅Coser·九
            "Binguin_CoserDaiyousei", // 冰鹅Coser·大妖精
        };

        // ---------------------------------------------------------------- 冷却

        /// <summary>本局游戏的"钓到冰鹅"记录组件（没有就顺手建一个）。</summary>
        public static GameComponent_BinguinFishing Comp
        {
            get
            {
                try
                {
                    if (Current.Game == null) return null;
                    GameComponent_BinguinFishing c = Current.Game.GetComponent<GameComponent_BinguinFishing>();
                    if (c == null)
                    {
                        // 正常由 Game.FillComponents() 自动建；这里只是兜底
                        //（例如存档是旧版本、或组件列表被别的 mod 动过）。
                        c = new GameComponent_BinguinFishing(Current.Game);
                        Current.Game.components.Add(c);
                    }
                    return c;
                }
                catch (Exception e)
                {
                    Log.Warning("[冰鹅族] 取钓鱼组件失败：" + e.Message);
                    return null;
                }
            }
        }

        /// <summary>60 天冷却是否已过（从没见过 = 立刻可触发）。</summary>
        public static bool CanCatchBinguin
        {
            get
            {
                GameComponent_BinguinFishing c = Comp;
                if (c == null) return false;
                if (c.lastBinguinCatchTick < 0) return true;      // 从没钓到过
                return GenTicks.TicksGame - c.lastBinguinCatchTick >= CooldownTicks;
            }
        }

        /// <summary>距离冷却结束还有几天（供提示用；已过则返回 0）。</summary>
        public static int DaysUntilReady()
        {
            GameComponent_BinguinFishing c = Comp;
            if (c == null || c.lastBinguinCatchTick < 0) return 0;
            int left = CooldownTicks - (GenTicks.TicksGame - c.lastBinguinCatchTick);
            return left <= 0 ? 0 : (left / 60000) + 1;
        }

        // ------------------------------------------------- 概率判定 + 入队生成

        /// <summary>
        /// ★ 三处钓鱼共用的入口。返回 true = 这次渔获被"换成了一只冰鹅"，
        /// 调用方就【不要】再产原本的鱼了。
        ///
        /// 判定顺序：先看 60 天冷却（照原版稀有渔获的做法，冷却没过就根本不抽），
        /// 再抽 0.78%。抽中就此刻就记冷却 + 入队 + 弹提示（pawn 稍后生成）。
        /// </summary>
        public static bool TryReplaceCatch(Pawn fisher, Map map, string sourceLabel)
        {
            try
            {
                if (fisher == null || map == null || !fisher.Spawned) return false;
                if (!CanCatchBinguin) return false;
                if (!Rand.Chance(CatchChance)) return false;

                GameComponent_BinguinFishing c = Comp;
                if (c == null) return false;

                // 冷却在这一刻就记上：即使后面生成失败（找不着格子等），也不该让人反复抽。
                c.lastBinguinCatchTick = GenTicks.TicksGame;
                c.Enqueue(fisher, map);

                Messages.Message("Binguin_FishingCatch_01".Translate(),
                    new TargetInfo(fisher.Position, map, false), MessageTypeDefOf.PositiveEvent, false);
                Log.Message("[冰鹅族] 钓到冰鹅！来源=" + sourceLabel
                    + "，钓手=" + (fisher.Name != null ? fisher.Name.ToStringShort : fisher.def.defName)
                    + "，冷却进入 " + CooldownDays + " 天。");
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 钓到冰鹅判定异常：" + e.Message);
                return false;
            }
        }

        // ------------------------------------------------------------ 生成难民

        /// <summary>在钓手旁边生成那名"被钓上来的"冰鹅难民（由组件延迟调用）。</summary>
        public static void SpawnRefugee(Pawn fisher, Map map)
        {
            if (fisher == null || map == null || !fisher.Spawned) return;

            PawnKindDef kind = PickKind();
            if (kind == null)
            {
                Log.Warning("[冰鹅族] 钓到冰鹅：找不到可用的兵种模板，本次跳过。");
                return;
            }
            Faction faction = FindPeacefulFaction();

            Pawn pawn = null;
            try
            {
                pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction));
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 钓到冰鹅：生成 pawn 异常：" + e);
                return;
            }
            if (pawn == null)
            {
                Log.Error("[冰鹅族] 钓到冰鹅：生成 pawn 返回 null。");
                return;
            }

            // ★ 冰鹅只允许 Female / Thin 两种体型，否则贴图找不到会红字 + 花屏
            //   （同 BandVisitFlow_Binguin.FixBinguinBodyType 的坑）。
            FixBodyType(pawn);

            // ★ 麻痹症 2 天（defName 是 Abasia，label "paralytic abasia"）。
            //   Abasia 自带 HediffCompProperties_Disappears(1800000~2400000 tick)，会随机提前消失，
            //   所以必须把它的 ticksToDisappear 显式压成 2 天，否则做不到用户要的"正好 2 天"。
            ApplyAbasia(pawn);

            IntVec3 cell = FindSpawnCell(fisher, map);
            try
            {
                GenSpawn.Spawn(pawn, cell, map, Rot4.Random);
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 钓到冰鹅：落地失败：" + e);
                return;
            }

            // 麻痹症本身就会让人倒地（preventsCrawling）；这里再显式压一次，保证一定是"待救援"状态。
            if (pawn.health != null) pawn.health.forceDowned = true;

            Messages.Message("Binguin_FishingCatch_02".Translate(
                    pawn.LabelShortCap, kind.label, AbasiaDays.ToString()),
                pawn, MessageTypeDefOf.PositiveEvent, false);
            Log.Message("[冰鹅族] 钓上来的冰鹅难民已落地：" + kind.defName
                + " @ " + cell + "（麻痹症 " + AbasiaDays + " 天，倒地待救援）");
        }

        /// <summary>从兵种池里随机挑一个；顺手排除任何 kind.factionLeader 的模板。</summary>
        private static PawnKindDef PickKind()
        {
            List<PawnKindDef> pool = new List<PawnKindDef>();
            for (int i = 0; i < CatchableKinds.Length; i++)
            {
                PawnKindDef k = DefDatabase<PawnKindDef>.GetNamedSilentFail(CatchableKinds[i]);
                if (k == null)
                {
                    Log.Warning("[冰鹅族] 钓到冰鹅：兵种模板不存在 → " + CatchableKinds[i]);
                    continue;
                }
                if (k.factionLeader) continue;            // 兜底：领袖一律不钓
                pool.Add(k);
            }
            if (pool.Count == 0) return null;
            return pool[Rand.Range(0, pool.Count)];
        }

        /// <summary>和平派系「冰鹅」；找不到就交给 PawnGenerator 自己决定（传 null）。</summary>
        private static Faction FindPeacefulFaction()
        {
            try
            {
                List<Faction> all = Find.FactionManager.AllFactionsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    if (BinguinFactions.IsPeaceful(all[i])) return all[i];
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static void FixBodyType(Pawn p)
        {
            try
            {
                if (p == null || p.story == null) return;
                // ★ 2026-09-26 种族判定收口：见 BinguinRaceUtility
                if (!BinguinRaceUtility.IsBinguin(p)) return;
                BodyTypeDef bt = p.story.bodyType;
                if (bt != null && (bt.defName == "Female" || bt.defName == "Thin")) return;
                p.story.bodyType = (p.gender == Gender.Male) ? BodyTypeDefOf.Thin : BodyTypeDefOf.Female;
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 钓到冰鹅：纠正体型失败：" + e.Message);
            }
        }

        /// <summary>麻痹症 2 天。Abasia 的 Disappears comp 会把时长压回 2 天。</summary>
        private static void ApplyAbasia(Pawn pawn)
        {
            try
            {
                HediffDef def = HediffDefOf.Abasia;
                if (def == null)
                {
                    Log.Warning("[冰鹅族] 钓到冰鹅：HediffDefOf.Abasia 为空，本次不带麻痹症。");
                    return;
                }
                Hediff h = HediffMaker.MakeHediff(def, pawn);
                if (h == null) return;
                h.Severity = 1f;
                h.ageTicks = 0;

                // ★ 关键：把"随机 30~45 天自愈"的 comp 改成固定的 2 天。
                //   comps 字段在 HediffWithComps 上（不是 Hediff 上），所以要先转类型；
                //   转不了也不影响主流程，只是时长会回到原版的 30~45 天。
                HediffWithComps hwc = h as HediffWithComps;
                if (hwc != null && hwc.comps != null)
                {
                    for (int i = 0; i < hwc.comps.Count; i++)
                    {
                        HediffComp_Disappears dis = hwc.comps[i] as HediffComp_Disappears;
                        if (dis != null) dis.ticksToDisappear = AbasiaTicks;
                    }
                }
                pawn.health.AddHediff(h, null, null, null);
                Log.Message("[冰鹅族] 钓到冰鹅：已施加麻痹症 " + AbasiaDays + " 天（Abasia）。");
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 钓到冰鹅：施加麻痹症失败：" + e.Message);
            }
        }

        /// <summary>在钓手附近找一格能站人的位置（跟乐队事件 SpawnOne 同一套找法）。</summary>
        private static IntVec3 FindSpawnCell(Pawn fisher, Map map)
        {
            IntVec3 cell;
            if (CellFinder.TryFindRandomCellNear(fisher.Position, map, 4,
                    c => c.Standable(map) && !c.Fogged(map), out cell))
            {
                return cell;
            }
            if (CellFinder.TryFindRandomCellNear(fisher.Position, map, 12,
                    c => c.Standable(map), out cell))
            {
                return cell;
            }
            return fisher.Position;
        }
    }

    /// <summary>
    /// 存"钓到冰鹅"的 60 天冷却 + 待生成队列。
    /// ★ 会被 Game.FillComponents() 自动实例化（IL 实证），不需要 XML 注册；
    ///   构造函数必须是 public 且签名 (Game)，1.6 用 Activator.CreateInstance(type, game)。
    /// </summary>
    public class GameComponent_BinguinFishing : GameComponent
    {
        /// <summary>上次钓到冰鹅的 tick；-1 = 从没钓到过。</summary>
        public int lastBinguinCatchTick = -1;

        // 待生成队列（不存档：跨存档时把没落地的冰鹅丢掉，好过在旧地图坐标上乱生成）
        private readonly List<Pawn> pendingFishers = new List<Pawn>();
        private readonly List<Map> pendingMaps = new List<Map>();

        public GameComponent_BinguinFishing(Game game) : base()
        {
        }

        public void Enqueue(Pawn fisher, Map map)
        {
            pendingFishers.Add(fisher);
            pendingMaps.Add(map);
        }

        public override void GameComponentUpdate()
        {
            if (pendingFishers.Count == 0) return;

            // 只处理第一个（正常情况下队列里也就一个）
            Pawn fisher = pendingFishers[0];
            Map map = pendingMaps[0];
            pendingFishers.RemoveAt(0);
            pendingMaps.RemoveAt(0);

            try
            {
                BinguinFishingCatchUtility.SpawnRefugee(fisher, map);
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 钓到冰鹅：延迟生成失败：" + e);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref lastBinguinCatchTick, "binguinLastFishingCatchTick", -1, false);
        }
    }

    /// <summary>
    /// 原版钓鱼的挂点：`FishingUtility.GetCatchesFor(Pawn, IntVec3, bool, out bool)`。
    ///
    /// ★ 用 Postfix 而不是 Prefix：原版这个方法内部有一整套鱼群/稀有渔获判定，
    ///   我们只是"在它已经决定这次有渔获之后，把渔获换成一只冰鹅"，
    ///   所以等它跑完再动最省事、也最不容易和原版行为打架。
    ///
    /// ★ 判定"这次确实钓到东西了"用的是 `__result` 既非 null 又非空 ——
    ///   原版在鱼群枯竭 / 没有可捕鱼种 / 随机没抽中时都会返回【空表】，
    ///   所以这一条同时把所有"没钓到"的情况排除掉了，不必去猜 out 参数名。
    ///   （IL 实证：空表分支在 IL_0132 / IL_0032 / IL_0019 等直接 ret。）
    /// </summary>
    public static class Patch_BinguinFishingCatch
    {
        // ★★ 形参名必须【逐字】等于原版形参名：Harmony 默认按名字注入，
        //   原版签名是 `GetCatchesFor(Pawn pawn, IntVec3 cell, bool animalFishing, out bool rare)`
        //   ⇒ 第一个参数必须叫 `pawn`。
        //   2026-10-01 修：原来写成 `fisher` → 启动日志报
        //   「Parameter "fisher" not found in method …」→ 整个"钓到企鹅"补丁没挂上。
        //   （Mono.Cecil 读出的实际参数名见 AdvancedFishing 相关记录，别再用意译的名字。）
        public static void Postfix(Pawn pawn, ref List<Thing> __result)
        {
            try
            {
                if (__result == null || __result.Count == 0) return;   // 这次没渔获
                if (pawn == null || pawn.Map == null) return;
                if (BinguinFishingCatchUtility.TryReplaceCatch(pawn, pawn.Map, "原版钓鱼区"))
                {
                    __result = new List<Thing>();     // 渔获换成冰鹅了，别再给鱼
                }
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 原版钓鱼挂钩异常：" + e.Message);
            }
        }
    }
}
