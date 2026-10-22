// ============================================================================
// 冰鹅族【自办演唱会】· 原版仪式界面接线（2026-10-17）
//
// ★★ 作者要求：「和文化 UI 一样最好」。
//   ⇒ 直接使用文化 DLC 的 `RimWorld.Dialog_BeginRitual` —— 它就是那个窗口本体，
//     所以界面**完全一致**：左列角色槽、右侧头像网格、灰掉原因、预计质量、
//     一人只能占一个位置（由 `RitualRoleAssignments` 原生保证）。
//
// ★★ 依赖的三件套 XML：`Defs/Features/Band/Concerts_Ritual_Binguin.xml`
//     `RitualBehaviorDef BinguinConcert`（角色槽）
//     `RitualPatternDef  BinguinConcert`（随时可开）
//     `PreceptDef        BinguinConcert`（isNonIdeoRitual = 不绑意识形态）
//
// ★★ 拿到 `Precept_Ritual` 实例的办法（已用 MetadataLoadContext 在 1.7 上核实）：
//     `PreceptMaker.MakePrecept(PreceptDef)` → `Precept` → 转 `Precept_Ritual`
//     `Precept.ideo` 是 `public Ideo ideo;`（引用类型，可为 null）
//     ⇒ `isNonIdeoRitual` 的仪式允许 ideo 为空；为稳妥这里仍尝试填玩家派系的 ideo。
//
// ★★ 日期怎么选：
//     原版仪式窗口里没有"几天后"的字段，所以在仪式确认后
//     弹一个原版 `FloatMenu` 让玩家挑 1~7 天 —— 轻量且风格统一。
//
// ★★ 回调里怎么取"谁演什么"：
//     `RitualRoleAssignments.AssignedPawns(string roleId)`
//     roleId 就是 XML 里写的 lead / keyboard / drumkit / guitar / bass
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;
using Binguin.Helper;

namespace Binguin.Feature.Band
{
    /// <summary>把「麦克风 gizmo」接到原版仪式窗口上。</summary>
    public static class BinguinConcertRitual
    {
        public const string PreceptDefName = "BinguinConcert";

        /// <summary>角色 id → 乐器 defName（主唱用麦克风，不在这里）。</summary>
        private static readonly string[][] RoleToDef = new string[][]
        {
            new string[] { "keyboard", "Binguin_Band_Keyboard" },
            new string[] { "drumkit",  "Binguin_Band_DrumKit"  },
            new string[] { "guitar",   "Binguin_Band_Guitar"   },
            new string[] { "bass",     "Binguin_Band_Bass"     },
        };

        /// <summary>
        /// 打开原版仪式窗口。返回 false 表示仪式 Def 缺失/创建失败
        /// （调用方应回退到自建窗口）。
        /// </summary>
        public static bool TryOpen(Map map, IntVec3 micCell)
        {
            return TryOpen(map, micCell, null);
        }

        /// <summary>
        /// 打开原版仪式窗口（可预先把现有预约的人填回角色槽）。
        /// ★ 2026-10-17 作者要求：「改期界面最好也用仪式的」
        ///   ⇒ 重新选人走同一个仪式窗口，并用 forcedForRole 把已有的人填好。
        /// </summary>
        public static bool TryOpen(Map map, IntVec3 micCell, BinguinConcertBooking prefill)
        {
            if (map == null) return false;
            try
            {
                PreceptDef def = DefDatabase<PreceptDef>.GetNamedSilentFail(PreceptDefName);
                if (def == null)
                {
                    BinguinLogUtility.Log("找不到 PreceptDef " + PreceptDefName
                        + "（Concerts_Ritual_Binguin.xml 没加载？），回退到自建窗口。",
                        severity: 1, isDebug: false);
                    return false;
                }
                Precept raw = PreceptMaker.MakePrecept(def);
                Precept_Ritual ritual = raw as Precept_Ritual;
                if (ritual == null)
                {
                    BinguinLogUtility.Log("PreceptMaker 造出来的不是 Precept_Ritual，回退。",
                        severity: 1, isDebug: false);
                    return false;
                }

                // ★★ 2026-10-17 关键：`PreceptMaker.MakePrecept` 只造了个空壳，
                //   三个字段必须按原版顺序手动补齐，否则行为空、窗口打不开：
                //
                //   ① `Init(Ideo, FactionDef)`          —— 只做 base.Init + RegenerateName
                //   ② `PostMake()`                      —— **sourcePattern = def.ritualPatternBase**
                //      （源码 Precept_Ritual.cs:421-424 实证）
                //   ③ `sourcePattern.ritualBehavior.GetInstance()` —— **创建 behavior**
                //      （源码 RitualBehaviorDef.cs:79 GetInstance() =
                //        (RitualBehaviorWorker)Activator.CreateInstance(workerClass, this)）
                //
                //   ★ 玩家实测日志曾出现：
                //     「[冰鹅族] 仪式 behavior 仍为空（RitualBehaviorDef 的 workerClass
                //       未被实例化），回退到自建窗口。」
                //     原因就是漏了第 ②③ 步 ⇒ sourcePattern 与 behavior 都是 null。
                Ideo ideo = null;
                Faction player = Faction.OfPlayer;
                if (player != null && player.ideos != null && player.ideos.PrimaryIdeo != null)
                {
                    ideo = player.ideos.PrimaryIdeo;
                }
                try
                {
                    ritual.Init(ideo, null);
                    ritual.PostMake();                       // ② 设 sourcePattern

                    // ③ ★★ 关键：`PreceptMaker.MakePrecept` 只造空壳，
                    //   `Precept_Ritual` 有 **4 个实例字段**全都要从
                    //   `sourcePattern` 里手动造出来，一个不建就 NRE：
                    //     behavior               → RitualBehaviorDef.GetInstance()
                    //     outcomeEffect          → RitualOutcomeEffectDef.GetInstance()
                    //     obligationTargetFilter → RitualObligationTargetFilterDef.GetInstance()
                    //     targetFilter           → RitualTargetFilterDef.GetInstance()
                    //   历史崩溃对应：
                    //     obligationTargetFilter 为空 ⇒ ShouldShowGizmo NRE ⇒ 全图物体无按键
                    //     outcomeEffect 为空          ⇒ Dialog_BeginRitual.PredictedQuality NRE
                    //     behavior 为空               ⇒ 仪式窗口无法初始化
                    RitualPatternDef pat = ritual.sourcePattern;
                    if (pat != null)
                    {
                        if (ritual.behavior == null && pat.ritualBehavior != null)
                            ritual.behavior = pat.ritualBehavior.GetInstance();
                        if (ritual.outcomeEffect == null && pat.ritualOutcomeEffect != null)
                            ritual.outcomeEffect = pat.ritualOutcomeEffect.GetInstance();
                        if (ritual.obligationTargetFilter == null
                            && pat.ritualObligationTargetFilter != null)
                            ritual.obligationTargetFilter =
                                pat.ritualObligationTargetFilter.GetInstance();
                        if (ritual.targetFilter == null && pat.ritualTargetFilter != null)
                            ritual.targetFilter = pat.ritualTargetFilter.GetInstance();
                    }
                }
                catch (System.Exception e1)
                {
                    BinguinLogUtility.Log("Precept_Ritual 初始化失败（回退到自建窗口）：" + e1,
                        severity: 1, isDebug: false);
                    return false;
                }
                if (ritual.sourcePattern == null)
                {
                    BinguinLogUtility.Log("仪式的 sourcePattern 为空（PostMake 没生效？），回退到自建窗口。",
                        severity: 1, isDebug: false);
                    return false;
                }
                if (ritual.behavior == null)
                {
                    BinguinLogUtility.Log("仪式 behavior 仍为空（"
                        + (ritual.sourcePattern.ritualBehavior == null
                            ? "RitualPatternDef.ritualBehavior 未指向 RitualBehaviorDef"
                            : "GetInstance 未返回实例")
                        + "），回退到自建窗口。", severity: 1, isDebug: false);
                    return false;
                }

                // ★★ ★ 2026-10-17：回调里要把仪式窗口**关掉**。
                //    之前只弹 FloatMenu 选天数，窗口一直挂在上面，
                //    玩家感觉“点完了还在同一页”。
                Dialog_BeginRitual dlg = null;
                RimWorld.Dialog_BeginRitual.ActionCallback onConfirm =
                    delegate (RitualRoleAssignments assignments)
                {
                    OnAssignmentsConfirmed(assignments, map, micCell);
                    if (dlg != null) Find.WindowStack.TryRemove(dlg, true);
                    return false;
                };
                dlg = new Dialog_BeginRitual(
                    "Binguin_ConcertSetup_Title".Translate(),   // ritualLabel
                    ritual,
                    new TargetInfo(micCell, map),
                    map,
                    onConfirm
                    /*
                    delegate (RitualRoleAssignments assignments)
                    {
                        OnAssignmentsConfirmed(assignments, map, micCell);
                        // ★★ 返回 **false** 是刻意的：
                        //   `Dialog_BeginRitual.TryStartRitual()` 里的写法是
                        //     if (actionCallback != null && actionCallback(assignments)) { ...启动仪式... }
                        //   （源码 Dialog_BeginRitual.cs:292-293 实证）
                        //   ⇒ 返回 true 会真的启动一个原版仪式 LordJob，
                        //     而我们的演唱会由 `BinguinConcert` / `BinguinConcertDirector`
                        //     自己驱动 —— 两套并行会互相干扰。
                        //   ⇒ 我们只把这个窗口当**选人界面**用，返回 false 阻止仪式启动。
                        return false;
                    }*/,
                    null,      // organizer：让玩家自己在角色槽里选主唱
                    null,      // obligation：非意识形态仪式没有义务
                    // ★★★ 2026-10-17 最后一个坑：**必须传 outcome**。
                    //   构造函数签名（Dialog_BeginRitual.cs:245）：
                    //     (ritualLabel, ritual, target, map, action, organizer, obligation,
                    //      filter=null, okButtonText=null, requiredPawns=null,
                    //      forcedForRole=null, outcome=null, extraInfoText=null, selectedPawn=null)
                    //   `outcome` 没传 ⇒ 窗口字段 `outcome` 为 null ⇒
                    //   `PredictedQuality()` 第一行 `outcome.startingQuality` 直接 NRE
                    //   （Dialog_BeginRitual.cs:301）⇒ 每帧报错、界面画不出来。
                    //   历史日志：PredictedQuality 的 NRE 刷了 1441+400 次。
                    // filter / okButtonText / requiredPawns / forcedForRole
                    null, null, null, BuildForced(prefill),
                    ritual.sourcePattern != null ? ritual.sourcePattern.ritualOutcomeEffect : null);
                Find.WindowStack.Add(dlg);
                return true;
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("打开原版仪式窗口失败（回退到自建窗口）：" + e,
                    severity: 1, isDebug: false);
                return false;
            }
        }

        /// <summary>仪式确认后：取出角色分配 → 让玩家选日期 → 登记预约。</summary>
        private static void OnAssignmentsConfirmed(RitualRoleAssignments assignments,
            Map map, IntVec3 micCell)
        {
            if (assignments == null || map == null) return;
            try
            {
                Pawn lead = FirstOf(assignments, "lead");
                if (lead == null)
                {
                    Messages.Message("Binguin_ConcertVenue_NoMic".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return;
                }
                Dictionary<string, Pawn> musicians = new Dictionary<string, Pawn>();
                for (int i = 0; i < RoleToDef.Length; i++)
                {
                    Pawn p = FirstOf(assignments, RoleToDef[i][0]);
                    if (p != null) musicians[RoleToDef[i][1]] = p;
                }
                // ★ 2026-10-17 作者要求：「选曲窗口在选人后」
                //   流程：仪式窗口选人 → **选曲（最多 4 首）** → 选 1~7 天 → 登记
                List<Binguin_SongDef> pre = new List<Binguin_SongDef>();
                Binguin_SongDef d = BinguinConcertSongs.DefaultFor(lead);
                if (d != null) pre.Add(d);

                Find.WindowStack.Add(new Dialog_BinguinSongPick(pre,
                    delegate (List<Binguin_SongDef> picked)
                    {
                        List<Binguin_SongDef> songs = new List<Binguin_SongDef>();
                        if (picked != null) songs.AddRange(picked);
                        if (songs.Count == 0)
                        {
                            Binguin_SongDef dd = BinguinConcertSongs.DefaultFor(lead);
                            if (dd != null) songs.Add(dd);
                        }
                        AskDaysThenBook(lead, musicians, songs, map, micCell);
                    }));
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("处理仪式分配结果失败：" + e, severity: 1, isDebug: false);
            }
        }

        /// <summary>取某角色分到的人（没有则 null）。</summary>
        private static Pawn FirstOf(RitualRoleAssignments assignments, string roleId)
        {
            // ★ `AssignedPawns(string roleId)` 返回 `IEnumerable<Pawn>`
            System.Collections.Generic.IEnumerable<Pawn> list = assignments.AssignedPawns(roleId);
            if (list == null) return null;
            foreach (Pawn p in list)
            {
                if (p != null) return p;
            }
            return null;
        }

        /// <summary>把已有预约的人变成“角色 id → 小人”，预先填好仪式角色槽。</summary>
        private static Dictionary<string, Pawn> BuildForced(BinguinConcertBooking b)
        {
            if (b == null) return null;
            Dictionary<string, Pawn> d = new Dictionary<string, Pawn>();
            if (b.leadSinger != null) d["lead"] = b.leadSinger;
            if (b.musicians != null)
            {
                foreach (KeyValuePair<string, Pawn> kv in b.musicians)
                {
                    if (kv.Value == null) continue;
                    string roleId = RoleIdOf(kv.Key);
                    if (!roleId.NullOrEmpty()) d[roleId] = kv.Value;
                }
            }
            return d.Count > 0 ? d : null;
        }

        /// <summary>乐器 defName → 仪式角色 id。</summary>
        private static string RoleIdOf(string instrumentDefName)
        {
            for (int i = 0; i < RoleToDef.Length; i++)
            {
                if (RoleToDef[i][1] == instrumentDefName) return RoleToDef[i][0];
            }
            return null;
        }

        /// <summary>弹原版 FloatMenu 让玩家挑 1~7 天后。</summary>
        private static void AskDaysThenBook(Pawn lead, Dictionary<string, Pawn> musicians,
            List<Binguin_SongDef> songs, Map map, IntVec3 micCell)
        {
            List<FloatMenuOption> opts = new List<FloatMenuOption>();
            for (int d = 1; d <= 7; d++)
            {
                int days = d;
                opts.Add(new FloatMenuOption(
                    "Binguin_ConcertSetup_DaysValue".Translate(days.ToString()),
                    delegate { DoBook(lead, musicians, songs, days, map, micCell); }));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static void DoBook(Pawn lead, Dictionary<string, Pawn> musicians,
            List<Binguin_SongDef> songs, int days, Map map, IntVec3 micCell)
        {
            GameComponent_BinguinConcertBooking comp = GameComponent_BinguinConcertBooking.Instance;
            if (comp == null)
            {
                BinguinLogUtility.Log("找不到预约 GameComponent。", severity: 2, isDebug: false);
                return;
            }
            if (comp.HasBooking) comp.Cancel("Binguin_Interface_035".Translate().ToString());
            if (!comp.Book(lead, musicians, songs, days, map, micCell)) return;
            Messages.Message("Binguin_ConcertSetup_Booked".Translate(days.ToString(),
                songs.Count > 0 && songs[0] != null ? songs[0].label : ""),
                MessageTypeDefOf.PositiveEvent);
        }
    }
}
