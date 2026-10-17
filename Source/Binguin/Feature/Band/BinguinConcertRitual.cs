// ============================================================================
// 冰鹅族【自办演唱会】· 原版仪式界面接线（2026-10-17）
//
// ★★ 作者要求：「和文化 UI 一样最好」。
//   ⇒ 直接使用文化 DLC 的 `RimWorld.Dialog_BeginRitual` —— 它就是那个窗口本体，
//     所以界面**完全一致**：左列角色槽、右侧头像网格、灰掉原因、预计质量、
//     一人只能占一个位置（由 `RitualRoleAssignments` 原生保证）。
//
// ★★ 依赖的三件套 XML：`Defs/Feature/Band/Concerts_Ritual_Binguin.xml`
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

                // ★★ 2026-10-17 关键：必须 `Init` 过，否则 `behavior` / `sourcePattern`
                //   这些字段是空的，后面任何 ShouldShowGizmo / 窗口逻辑都会 NRE。
                //   `Precept_Ritual.Init(Ideo ideo, FactionDef generatingFor)` 是 public virtual。
                //   ⇒ 用玩家派系的 ideo 初始化；没有 ideo（未装 Ideology）就传 null。
                Ideo ideo = null;
                Faction player = Faction.OfPlayer;
                if (player != null && player.ideos != null && player.ideos.PrimaryIdeo != null)
                {
                    ideo = player.ideos.PrimaryIdeo;
                }
                try
                {
                    ritual.Init(ideo, null);
                }
                catch (System.Exception e1)
                {
                    BinguinLogUtility.Log("Precept_Ritual.Init 失败（回退到自建窗口）：" + e1,
                        severity: 1, isDebug: false);
                    return false;
                }
                if (ritual.behavior == null)
                {
                    BinguinLogUtility.Log("仪式 behavior 仍为空（RitualBehaviorDef 的 workerClass 未被实例化），"
                        + "回退到自建窗口。", severity: 1, isDebug: false);
                    return false;
                }

                Dialog_BeginRitual dlg = new Dialog_BeginRitual(
                    "Binguin_ConcertSetup_Title".Translate(),   // ritualLabel
                    ritual,
                    new TargetInfo(micCell, map),
                    map,
                    delegate (RitualRoleAssignments assignments)
                    {
                        OnAssignmentsConfirmed(assignments, map, micCell);
                        return true;
                    },
                    null,      // organizer：让玩家自己在角色槽里选主唱
                    null);     // obligation：非意识形态仪式没有义务
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
                // 曲目：仪式窗口里没地方选，先给默认第一首；
                // 玩家可以在通讯台/改期界面里调整（后续可做成仪式前选择）。
                List<Binguin_SongDef> songs = new List<Binguin_SongDef>();
                Binguin_SongDef d = BinguinConcertSongs.DefaultFor(lead);
                if (d != null) songs.Add(d);

                AskDaysThenBook(lead, musicians, songs, map, micCell);
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
            if (comp.HasBooking) comp.Cancel("重新预约");
            if (!comp.Book(lead, musicians, songs, days, map, micCell)) return;
            Messages.Message("Binguin_ConcertSetup_Booked".Translate(days.ToString(),
                songs.Count > 0 && songs[0] != null ? songs[0].label : ""),
                MessageTypeDefOf.PositiveEvent);
        }
    }
}
