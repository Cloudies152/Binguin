// ============================================================================
// 冰鹅族【自办演唱会】· 准备窗口（仪式风格布局，2026-10-11）
//
// ★★ 作者要求：
//   「通过点击**麦克风**来准备演唱会」
//   「选择主唱和乐手，并**预定时间 1-7 天后**」
//   「每个人只能担任一个位置，不能重复」
//   「一次演唱会最多可以选择四首曲子」
//   「选人界面太粗糙」⇒ 要**像文化 DLC 的仪式**那样
//
// ★★ 为什么不用原版 `Dialog_BeginRitual`（已用 MetadataLoadContext 在 1.7 的
//   Assembly-CSharp.dll 上核实）：
//   `RitualRoleAssignments(Precept_Ritual ritual, TargetInfo target)` 的角色列表
//   **全部来自 `ritual.behavior.roles`**，`AllRolesForReading` 仅在 ritual 非空时非空。
//   原版授勋典礼能传 null，是因为它**不需要角色槽**（只选受勋者 + 观众）。
//   ⇒ 想用原版界面 + 自定义角色，必须注册 PreceptDef + RitualBehaviorDef，
//     并要求玩家有 ideoligion（Ideology）——限制太大。
//   ⇒ 这里**自建窗口，但按原版仪式的信息布局来排**：
//       左列 = 角色槽（名称 + 已选人 + 艺术等级）
//       右侧 = 候选人网格（头像 + 名字 + 艺术等级）
//       不能选的人**灰掉并给出原因**；一人只能占一个槽
//       顶部显示预计质量与总时长
//
// ★★ 一人一个位置：`RoleOf(pawn)` 判断该人已占哪个槽，占用则灰掉。
// ★★ 曲目：多选，**最多 4 首**，按选择顺序演出。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Binguin.Helper;

namespace Binguin.Feature.Band
{
    /// <summary>准备演唱会：仪式风格的角色分配 + 多曲目选择。</summary>
    public class Dialog_BinguinConcertSetup : Window
    {
        private readonly Map map;
        private readonly IntVec3 micCell;

        private readonly List<Pawn> candidates = new List<Pawn>();
        private readonly List<Building> instruments = new List<Building>();

        public const string SlotLeader = "__leader";
        /// <summary>角色槽 id → 已选的人（SlotLeader = 主唱）。</summary>
        private readonly Dictionary<string, Pawn> slots = new Dictionary<string, Pawn>();
        private string activeSlot = SlotLeader;

        private readonly List<Binguin_SongDef> chosenSongs = new List<Binguin_SongDef>();
        private int days = 1;
        private string invalidReason;
        private Vector2 scrollR;

        public const string MicDefName = "Binguin_Band_Microphone";
        public const int MaxSongs = 4;

        public Dialog_BinguinConcertSetup(Map map, IntVec3 micCell)
        {
            this.map = map;
            this.micCell = micCell;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            forcePause = true;

            if (map != null && map.mapPawns != null)
            {
                candidates.AddRange(map.mapPawns.FreeColonistsSpawned);
            }
            CollectInstruments();

            BinguinConcertBooking prev = GameComponent_BinguinConcertBooking.ActiveBooking;
            if (prev != null)
            {
                if (prev.musicians != null)
                {
                    foreach (KeyValuePair<string, Pawn> kv in prev.musicians)
                    {
                        if (kv.Value != null) slots[kv.Key] = kv.Value;
                    }
                }
                if (prev.leadSinger != null) slots[SlotLeader] = prev.leadSinger;
                if (prev.songs != null) chosenSongs.AddRange(prev.songs);
                if (prev.daysFromNow > 0) days = prev.daysFromNow;
            }
            if (chosenSongs.Count == 0)
            {
                Binguin_SongDef d = BinguinConcertSongs.DefaultFor(null);
                if (d != null) chosenSongs.Add(d);
            }
        }

        private void CollectInstruments()
        {
            instruments.Clear();
            if (map == null) return;
            List<Building> all = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < all.Count; i++)
            {
                Building bd = all[i];
                if (bd == null || bd.def == null || !(bd is Building_MusicalInstrument)) continue;
                if ((bd.Position - micCell).LengthHorizontalSquared > 144f) continue;
                instruments.Add(bd);
            }
        }

        public override Vector2 InitialSize { get { return new Vector2(880f, 650f); } }

        /// <summary>场地合格性（null = 合格）。</summary>
        public static string CheckVenue(Map map, IntVec3 micCell)
        {
            if (map == null) return "Binguin_ConcertVenue_Unreachable".Translate();
            int mic = 0, other = 0;
            List<Building> all = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < all.Count; i++)
            {
                Building bd = all[i];
                if (bd == null || bd.def == null || !(bd is Building_MusicalInstrument)) continue;
                if ((bd.Position - micCell).LengthHorizontalSquared > 144f) continue;
                if (bd.def.defName == MicDefName) mic++; else other++;
            }
            if (mic <= 0) return "Binguin_ConcertVenue_NoMic".Translate();
            if (other < 2) return "Binguin_ConcertVenue_TooFewInstruments".Translate();
            return null;
        }

        private List<string> SlotIds()
        {
            List<string> ids = new List<string>();
            ids.Add(SlotLeader);
            for (int i = 0; i < instruments.Count; i++)
            {
                Building bd = instruments[i];
                if (bd.def.defName == MicDefName) continue;
                if (!ids.Contains(bd.def.defName)) ids.Add(bd.def.defName);
            }
            return ids;
        }

        private string SlotLabel(string id)
        {
            if (id == SlotLeader) return "Binguin_ConcertSetup_LeadSinger".Translate();
            ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(id);
            return d != null ? d.label : id;
        }

        /// <summary>此人已占的槽（null = 未占）。用于「一人只能一个位置」。</summary>
        private string RoleOf(Pawn p)
        {
            foreach (KeyValuePair<string, Pawn> kv in slots)
            {
                if (kv.Value == p) return kv.Key;
            }
            return null;
        }

        private static int ArtisticOf(Pawn p)
        {
            if (p == null || p.skills == null) return 0;
            SkillRecord s = p.skills.GetSkill(SkillDefOf.Artistic);
            return s != null ? s.Level : 0;
        }

        private int MinArtisticNeeded()
        {
            int need = 0;
            for (int i = 0; i < chosenSongs.Count; i++)
            {
                if (chosenSongs[i] != null && chosenSongs[i].requiredArtisticLevel > need)
                    need = chosenSongs[i].requiredArtisticLevel;
            }
            return need;
        }

        /// <summary>不能选的原因（null = 可选）。</summary>
        private bool CanPick(Pawn p, string slot, out string why)
        {
            why = null;
            if (p == null) { why = ""; return false; }
            if (p.Dead || !p.Spawned) { why = "Binguin_ConcertVenue_Unreachable".Translate(); return false; }
            string occ = RoleOf(p);
            if (occ != null && occ != slot) { why = "已在：" + SlotLabel(occ); return false; }
            int need = MinArtisticNeeded();
            if (need > 0 && ArtisticOf(p) < need)
            {
                why = "Binguin_ConcertSong_LevelTooLow".Translate(need.ToString(), ArtisticOf(p).ToString());
                return false;
            }
            return true;
        }

        private float EstimateQuality()
        {
            float sum = 0f; int n = 0;
            foreach (KeyValuePair<string, Pawn> kv in slots)
            {
                if (kv.Value == null) continue;
                sum += Mathf.Clamp01(ArtisticOf(kv.Value) / 20f); n++;
            }
            return n > 0 ? sum / (float)n : 0f;
        }

        private string QualityLabel(float q)
        {
            if (q < 0.2f) return "Binguin_ConcertSelf_Quality_Terrible".Translate();
            if (q < 0.4f) return "Binguin_ConcertSelf_Quality_Boring".Translate();
            if (q < 0.7f) return "Binguin_ConcertSelf_Quality_Fun".Translate();
            return "Binguin_ConcertSelf_Quality_Unforgettable".Translate();
        }

        private int TotalTicks()
        {
            int t = 0;
            for (int i = 0; i < chosenSongs.Count; i++)
            {
                if (chosenSongs[i] != null) t += chosenSongs[i].EffectiveDurationTicks;
            }
            return t > 0 ? t : BinguinConcert.DurationTicks;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "Binguin_ConcertSetup_Title".Translate());
            Text.Font = GameFont.Small;

            invalidReason = CheckVenue(map, micCell);
            float y = 34f;
            if (invalidReason != null)
            {
                GUI.color = ColorLibrary.RedReadable;
                Widgets.Label(new Rect(0f, y, inRect.width, 22f), invalidReason);
                GUI.color = Color.white;
            }
            else
            {
                Widgets.Label(new Rect(0f, y, inRect.width, 22f),
                    "预计质量：" + QualityLabel(EstimateQuality())
                    + "    ·    总时长：" + (TotalTicks() / 2500f).ToString("0.#") + " 小时");
            }
            y += 28f;

            // ═════ 左列：角色槽 ═════
            float leftW = 272f;
            Rect leftRect = new Rect(0f, y, leftW, inRect.height - y - 132f);
            Widgets.DrawMenuSection(leftRect);
            List<string> ids = SlotIds();
            float ly = leftRect.y + 6f;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                Pawn cur;
                slots.TryGetValue(id, out cur);
                Rect row = new Rect(leftRect.x + 6f, ly, leftW - 12f, 50f);
                bool isActive = id == activeSlot;
                if (isActive) Widgets.DrawHighlightSelected(row);
                else Widgets.DrawHighlightIfMouseover(row);

                Widgets.Label(new Rect(row.x + 4f, row.y + 2f, row.width - 8f, 20f), SlotLabel(id));
                string who = cur != null ? cur.LabelShort + "（艺术 " + ArtisticOf(cur) + "）" : "— 空缺 —";
                GUI.color = cur != null ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                Widgets.Label(new Rect(row.x + 12f, row.y + 22f, row.width - 16f, 20f), who);
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(row))
                {
                    if (cur != null && !isActive) slots.Remove(id);
                    activeSlot = id;
                }
                ly += 54f;
            }

            // ═════ 右侧：候选人网格 ═════
            Rect rightRect = new Rect(leftW + 8f, y, inRect.width - leftW - 8f, inRect.height - y - 132f);
            Widgets.DrawMenuSection(rightRect);
            int rows = Mathf.CeilToInt(candidates.Count / 5f);
            Rect viewRect = new Rect(0f, 0f, rightRect.width - 22f, Mathf.Max(rightRect.height - 8f, rows * 74f));
            Widgets.BeginScrollView(
                new Rect(rightRect.x + 4f, rightRect.y + 4f, rightRect.width - 8f, rightRect.height - 8f),
                ref scrollR, viewRect);
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn p = candidates[i];
                int cx = i % 5, cy = i / 5;
                Rect cell = new Rect(cx * (viewRect.width / 5f) + 2f, cy * 74f + 2f,
                    viewRect.width / 5f - 4f, 70f);
                string why;
                bool selectable = CanPick(p, activeSlot, out why);
                if (!selectable) GUI.color = new Color(1f, 1f, 1f, 0.32f);

                Widgets.DrawHighlightIfMouseover(cell);
                Rect av = new Rect(cell.x + 4f, cell.y + 2f, 38f, 44f);
                try { GUI.DrawTexture(av, PortraitsCache.Get(p, new Vector2(38f, 44f), Rot4.South)); }
                catch (System.Exception) { Widgets.DrawBoxSolid(av, new Color(0.3f, 0.35f, 0.4f)); }
                Widgets.Label(new Rect(cell.x + 46f, cell.y + 3f, cell.width - 48f, 20f), p.LabelShort);
                Widgets.Label(new Rect(cell.x + 46f, cell.y + 23f, cell.width - 48f, 20f), "艺术 " + ArtisticOf(p));
                if (!selectable)
                {
                    GUI.color = ColorLibrary.RedReadable;
                    // ★ 2026-10-17 作者要求：技能不足时**提示要多少级**。
                    //   原版 `Binguin_ConcertSong_LevelTooLow` 已含「需要 X，当前 Y」，
                    //   但格子窄放不下，这里压缩成简短文案。
                    string shortWhy;
                    if (why != null && why.StartsWith("已在"))
                    {
                        shortWhy = "已占用";
                    }
                    else
                    {
                        int need = MinArtisticNeeded();
                        shortWhy = "需艺术 " + need + " 级";
                    }
                    Widgets.Label(new Rect(cell.x + 4f, cell.y + 48f, cell.width - 8f, 20f), shortWhy);
                    GUI.color = new Color(1f, 1f, 1f, 0.32f);
                }
                else if (RoleOf(p) == activeSlot)
                {
                    Widgets.DrawBox(cell, 2);
                }
                GUI.color = Color.white;

                if (selectable && Widgets.ButtonInvisible(cell)) slots[activeSlot] = p;
            }
            Widgets.EndScrollView();

            // ═════ 底部：曲目 + 天数 ═════
            float by = inRect.height - 126f;
            Widgets.Label(new Rect(0f, by, inRect.width, 20f),
                "Binguin_ConcertSong_Label".Translate() + "（最多 " + MaxSongs + " 首，按选择顺序演出）");
            by += 22f;
            List<Binguin_SongDef> all = BinguinConcertSongs.All;
            float sx = 0f;
            for (int i = 0; i < all.Count; i++)
            {
                Binguin_SongDef s = all[i];
                bool on = chosenSongs.Contains(s);
                if (Widgets.ButtonText(new Rect(sx, by, 152f, 26f), (on ? "☑ " : "☐ ") + s.label))
                {
                    if (on) chosenSongs.Remove(s);
                    else if (chosenSongs.Count < MaxSongs) chosenSongs.Add(s);
                }
                sx += 156f;
                if (sx > inRect.width - 152f) { sx = 0f; by += 28f; }
            }
            by += 34f;

            Widgets.Label(new Rect(0f, by, 100f, 22f), "Binguin_ConcertSetup_Days".Translate());
            days = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(105f, by, 190f, 22f),
                days, 1f, 7f, true, null, "1", "7", 1f));
            Widgets.Label(new Rect(300f, by, 150f, 22f),
                "Binguin_ConcertSetup_DaysValue".Translate(days.ToString()));

            float cbx = inRect.width - 340f;
            bool canConfirm = invalidReason == null && slots.ContainsKey(SlotLeader) && slots[SlotLeader] != null
                && chosenSongs.Count > 0;
            if (!canConfirm) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(cbx, by - 4f, 200f, 32f),
                "Binguin_ConcertSetup_Confirm".Translate()) && canConfirm)
            {
                Confirm();
            }
            GUI.color = Color.white;
            if (Widgets.ButtonText(new Rect(cbx + 208f, by - 4f, 120f, 32f),
                "Binguin_ConcertSetup_Cancel".Translate()))
            {
                Close();
            }
        }

        private void Confirm()
        {
            GameComponent_BinguinConcertBooking comp = GameComponent_BinguinConcertBooking.Instance;
            if (comp == null)
            {
                BinguinLogUtility.Log("找不到预约 GameComponent。", severity: 2, isDebug: false);
                return;
            }
            Pawn leader;
            slots.TryGetValue(SlotLeader, out leader);
            Dictionary<string, Pawn> music = new Dictionary<string, Pawn>();
            foreach (KeyValuePair<string, Pawn> kv in slots)
            {
                if (kv.Key != SlotLeader && kv.Value != null) music[kv.Key] = kv.Value;
            }
            if (comp.HasBooking) comp.Cancel("重新预约");
            if (!comp.Book(leader, music, chosenSongs, days, map, micCell)) return;
            Messages.Message("Binguin_ConcertSetup_Booked".Translate(days.ToString(),
                chosenSongs[0] != null ? chosenSongs[0].label : ""), MessageTypeDefOf.PositiveEvent);
            Close();
        }
    }

    /// <summary>麦克风上的「准备演唱会」gizmo。</summary>
    [HarmonyLib.HarmonyPatch(typeof(Building_MusicalInstrument), "GetGizmos")]
    public static class Patch_BinguinConcertMicGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Building_MusicalInstrument __instance)
        {
            if (__result != null)
            {
                foreach (Gizmo g in __result) yield return g;
            }
            if (__instance == null || __instance.def == null) yield break;
            if (__instance.def.defName != Dialog_BinguinConcertSetup.MicDefName) yield break;
            if (__instance.Faction != Faction.OfPlayer) yield break;
            Map map = __instance.Map;
            if (map == null) yield break;

            Command_Action cmd = new Command_Action();
            // ★ 2026-10-11 作者要求：人员到场后，**由玩家主动开演**。
            //   麦克风上的按钮按状态切换：
            //     ① 已到场（awaitingStart）⇒ 「开始演唱会」
            //     ② 有待办预约但还没到日子 ⇒ 「取消演唱会」+「改期/改人」
            //     ③ 没有预约 ⇒ 「准备演唱会」
            BinguinConcertBooking waiting = GameComponent_BinguinConcertBooking.ActiveBooking;
            if (waiting != null && waiting.awaitingStart)
            {
                cmd.defaultLabel = "Binguin_ConcertSetup_StartNow".Translate();
                cmd.defaultDesc = "Binguin_ConcertSetup_StartNowDesc".Translate(
                    waiting.AssembledHours.ToString("0.#"));
                cmd.action = delegate
                {
                    GameComponent_BinguinConcertBooking comp = GameComponent_BinguinConcertBooking.Instance;
                    if (comp != null) comp.StartNow(waiting);
                };
                yield return cmd;

                // ★ 到场后也能取消（作者要求「准备演唱会完成后点击话筒可以取消」）
                Command_Action cancel1 = new Command_Action();
                cancel1.defaultLabel = "Binguin_ConcertSetup_CancelBooking".Translate();
                cancel1.defaultDesc = "Binguin_ConcertSetup_CancelBookingDesc".Translate();
                cancel1.action = delegate
                {
                    GameComponent_BinguinConcertBooking comp = GameComponent_BinguinConcertBooking.Instance;
                    if (comp != null) comp.Cancel("玩家取消");
                };
                yield return cancel1;
                yield break;
            }

            if (waiting != null)
            {
                // 还没到日子：可以取消，也可以改期/改人
                cmd.defaultLabel = "Binguin_ConcertSetup_CancelBooking".Translate();
                cmd.defaultDesc = "Binguin_ConcertSetup_CancelBookingDesc".Translate();
                cmd.action = delegate
                {
                    GameComponent_BinguinConcertBooking comp = GameComponent_BinguinConcertBooking.Instance;
                    if (comp != null) comp.Cancel("玩家取消");
                };
                yield return cmd;

                Command_Action edit = new Command_Action();
                edit.defaultLabel = "Binguin_ConcertSetup_Edit".Translate();
                edit.defaultDesc = "Binguin_ConcertSetup_EditDesc".Translate(
                    waiting.DaysUntilStart.ToString());
                edit.action = delegate
                {
                    // ★ 2026-10-17 作者要求：改期/改人也用**仪式界面**
                    //   并把已有的主唱/乐手预先填回角色槽
                    if (!BinguinConcertRitual.TryOpen(map, __instance.Position, waiting))
                    {
                        Find.WindowStack.Add(new Dialog_BinguinConcertSetup(map, __instance.Position));
                    }
                };
                yield return edit;
                yield break;
            }

            cmd.defaultLabel = "Binguin_ConcertSetup_Gizmo".Translate();
            cmd.defaultDesc = "Binguin_ConcertSetup_GizmoDesc".Translate();
            string why = Dialog_BinguinConcertSetup.CheckVenue(map, __instance.Position);
            int cool = GameComponent_BinguinConcertBooking.Instance != null
                ? Mathf.CeilToInt(GameComponent_BinguinConcertBooking.Instance.CooldownDaysLeft) : 0;
            if (why != null) cmd.Disable("Binguin_ConcertSetup_Disabled".Translate(why));
            else if (cool > 0) cmd.Disable("Binguin_ConcertSetup_Cooldown".Translate(cool.ToString()));
            cmd.action = delegate
            {
                // ★ 2026-10-17 作者要求「和文化 UI 一样」：
                //   优先走**原版仪式窗口**（`Dialog_BeginRitual`，界面完全一致）；
                //   仪式 Def 缺失或创建失败时才回退到自建窗口。
                if (!BinguinConcertRitual.TryOpen(map, __instance.Position))
                {
                    Find.WindowStack.Add(new Dialog_BinguinConcertSetup(map, __instance.Position));
                }
            };
            yield return cmd;
        }
    }
}
