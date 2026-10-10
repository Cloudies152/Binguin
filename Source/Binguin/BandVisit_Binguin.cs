// ============================================================================
// 冰鹅族【乐队来访】—— 场地蓝图 / 地标 / 建完判定（代码批 1/2，2026-09）
//
// 流程：接任务 → 地图上落下地标 → 地标按 sketch 铺蓝图 → 玩家建造
//       → MonumentMarker.AllDone 为真 → 触发乐队来访（下一批接）
//
// ★★ 为什么能白拿这么多东西：RimWorld.MonumentMarker 这个【类】在基础游戏
//    程序集里（只有它的 ThingDef 定义在 Royalty）。我们自己定义了一个
//    Defs/BandVisit_Binguin.xml 里的 Binguin_BandMarker 挂上去，于是：
//      · 按 sketch 自动铺蓝图          ✔ 白拿
//      · AllDone 建完判定              ✔ 白拿
//      · 缺蓝图 / 圈内违建的警报        ✔ 白拿
//      · 蓝图放置合法性校验             ✔ 白拿
//    且【不依赖任何 DLC】。
//
// ★ 场地布局（用户定稿·最终版）：【只有 5 件乐器】。
//   木地板、物品架、凳子、长桌全部去掉（2026-09 用户：桌子删了也行）。
//   原因见 BuildSketch() 上方注释：地标"放置蓝图"的 gizmo 对不选材的构件会空引用。
//   椅子桌子由玩家自己摆，观众会自动找能坐的地方坐下。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace Binguin
{
    public static class BinguinBandVisit
    {
        public const string MarkerDefName = "Binguin_BandMarker";

        // ---------------- 场地布局（相对坐标） ----------------
        // ★★★ 2026-09 用户定稿（最终版）：桌凳全部删掉，只剩舞台。
        // 舞台：以麦克风为水平中心。麦克风居中，吉他 +2，键盘 -3，
        // 架子鼓 +2 且后退 3 行，贝斯 -3 且后退 3 行。
        // ★ 键盘宽 2 格：放在 -3 → 占 x=2,3，右边缘 3 与麦克风(5) 之间空出 1 格。
        //   （用户实测：放 -2 时占 x=3,4，第 4 格紧贴麦克风，所以往外挪一格。）
        private const int VenueCenterX = 5;                  // 麦克风所在的 x
        private static readonly IntVec3 MicPos = new IntVec3(VenueCenterX, 0, -8);
        private static readonly IntVec3 GuitarPos = new IntVec3(VenueCenterX + 2, 0, -8);
        private static readonly IntVec3 KeyboardPos = new IntVec3(VenueCenterX - 3, 0, -8);
        private static readonly IntVec3 DrumPos = new IntVec3(VenueCenterX + 2, 0, -11);
        private static readonly IntVec3 BassPos = new IntVec3(VenueCenterX - 3, 0, -11);

        // ---------------- 工具 ----------------
        private static ThingDef Named(string defName)
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        }

        /// <summary>按用户定稿的布局构建 sketch。返回 null 表示有 def 缺失。</summary>
        /// <remarks>
        /// ★★★ 2026-09 用户实测修复（桌子全部删除）：
        ///   原来场地里有 2 张原版 2×4 长桌（要选材）。地标选中后点"放置蓝图"的 gizmo 会走
        ///       MonumentMarker.GetPlaceBlueprintsCommand 的闭包 b__0
        ///         → AllowedStuffsFor(buildable)      // 不选材的构件返回 **null**（IL_009A: ldnull; ret）
        ///         → GenCollection.Any(list)          // 实现是 list.Count > 0，**没有判空**
        ///   结果：NullReferenceException（用户 Player.log 实证：
        ///   at RimWorld.MonumentMarker+&lt;&gt;c__DisplayClass30_0.&lt;GetPlaceBlueprintsCommand&gt;b__0 [0x00012]）。
        ///   两处一起修：
        ///     ① 场地里【不再放任何"要选材"的构件】——桌凳全删，只剩 5 件固定造价的乐器；
        ///     ② Patch_BandVisitFreeStuffs 把 AllowedStuffsFor 的 null 兜成空表
        ///        （空表 → Any 为 false → 原版直接走 PlaceBlueprintsSimilarTo(buildable, null) 铺蓝图）。
        /// </remarks>
        public static Sketch BuildSketch()
        {
            Sketch sk = new Sketch();

            // ---- 5 件乐器（固定造价：20 铁 + 1 零部件，不选材，stuff 传 null） ----
            AddInstrument(sk, "Binguin_Band_Microphone", MicPos);
            AddInstrument(sk, "Binguin_Band_Guitar", GuitarPos);
            AddInstrument(sk, "Binguin_Band_Keyboard", KeyboardPos);
            AddInstrument(sk, "Binguin_Band_DrumKit", DrumPos);
            AddInstrument(sk, "Binguin_Band_Bass", BassPos);

            sk.MoveOccupiedCenterToZero();   // 让地标落在场地正中（与原版纪念碑一致）
            return sk;
        }

        private static void AddInstrument(Sketch sk, string defName, IntVec3 pos)
        {
            ThingDef def = Named(defName);
            if (def == null)
            {
                Log.Warning("[冰鹅族] 乐队场地：找不到乐器 " + defName + "，已跳过。");
                return;
            }
            sk.AddThing(def, pos, Rot4.North, null, 1, null, null, true, 0f);
        }

        // ---------------- 找地方落标 ----------------
        /// <summary>在殖民地附近找一块能放下整个场地的空地。找不到返回 false。</summary>
        public static bool TryFindSpot(Map map, IntVec2 size, out IntVec3 center)
        {
            center = IntVec3.Invalid;
            if (map == null) return false;

            IntVec3 anchor = map.Center;
            if (map.mapPawns.FreeColonistsSpawnedCount > 0)
            {
                anchor = map.mapPawns.FreeColonistsSpawned[0].Position;
            }

            int halfX = size.x / 2, halfZ = size.z / 2;
            // 从近到远螺旋找（半径上限跟着地图走）
            for (int r = 6; r < 60; r += 2)
            {
                for (int i = 0; i < 40; i++)
                {
                    IntVec3 c = anchor + new IntVec3(Rand.RangeInclusive(-r, r), 0, Rand.RangeInclusive(-r, r));
                    if (!c.InBounds(map)) continue;
                    if (RectClear(map, c, halfX, halfZ))
                    {
                        center = c;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool RectClear(Map map, IntVec3 center, int halfX, int halfZ)
        {
            for (int dx = -halfX; dx <= halfX; dx++)
            {
                for (int dz = -halfZ; dz <= halfZ; dz++)
                {
                    IntVec3 c = center + new IntVec3(dx, 0, dz);
                    if (!c.InBounds(map)) return false;
                    if (c.Fogged(map)) return false;
                    if (c.GetEdifice(map) != null) return false;
                    if (!c.Standable(map)) return false;
                }
            }
            return true;
        }

        /// <summary>生成地标。已有地标则不重复生成。</summary>
        public static MonumentMarker SpawnMarker(Map map)
        {
            try
            {
                if (map == null) return null;
                if (FindMarker(map) != null)
                {
                    Log.Message("[冰鹅族] 地图上已有演唱会地标，跳过生成。");
                    return FindMarker(map);
                }
                Sketch sk = BuildSketch();
                if (sk == null) return null;

                IntVec3 center;
                if (!TryFindSpot(map, sk.OccupiedSize, out center))
                {
                    Log.Warning("[冰鹅族] 乐队场地：附近找不到足够空地（需要 " + sk.OccupiedSize.x + "×" + sk.OccupiedSize.z + " 格）。");
                    return null;
                }

                ThingDef def = Named(MarkerDefName);
                if (def == null)
                {
                    Log.Error("[冰鹅族] 找不到地标 def " + MarkerDefName);
                    return null;
                }
                MonumentMarker marker = ThingMaker.MakeThing(def, null) as MonumentMarker;
                if (marker == null)
                {
                    Log.Error("[冰鹅族] " + MarkerDefName + " 的 thingClass 不是 MonumentMarker。");
                    return null;
                }
                marker.sketch = sk;
                GenSpawn.Spawn(marker, center, map, Rot4.North);
                Log.Message("[冰鹅族] 演唱会地标已生成于 " + center + "（场地 " + sk.OccupiedSize.x + "×" + sk.OccupiedSize.z + "）。");
                return marker;
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 生成演唱会地标失败：" + e);
                return null;
            }
        }

        /// <summary>接受任务时【空投】地标（用户定稿）。</summary>
        public static MonumentMarker DropMarkerByPod(Map map)
        {
            try
            {
                if (map == null) return null;
                Sketch sk = BuildSketch();
                if (sk == null) return null;
                IntVec3 center;
                if (!TryFindSpot(map, sk.OccupiedSize, out center))
                {
                    Log.Warning("[冰鹅族] 空投地标：附近找不到空地。");
                    return null;
                }
                ThingDef def = Named(MarkerDefName);
                if (def == null) return null;
                MonumentMarker marker = ThingMaker.MakeThing(def, null) as MonumentMarker;
                if (marker == null) return null;
                marker.sketch = sk;
                // ★★ 2026-09 修复（用户反馈）：原来直接把地标本体丢下来，玩家既不知道
                //    落在哪、也没法挑位置和朝向。改成【小型化后空投】——
                //    落下来的是一个可安装的地标（和原版纪念碑一样），
                //    玩家自己选地方放、可以按 R 旋转。
                // ★★ 2026-09 修正：之前以为"安装小型化物品时看不到场地"是原版限制 ——
                //    错了。原版是靠 Binguin_BandMarker 上挂的 PlaceWorker_MonumentMarker
                //    调 MonumentMarker.DrawGhost → Sketch.DrawGhost 画整套幽灵图的。
                //    现在 PlaceWorker 已补上（见 Defs/BandVisit_Binguin.xml），
                //    所以恢复【小型化空投】，玩家自己挑位置、按 R 旋转，且能看到整个场地。
                MinifiedThing mini = marker.MakeMinified();
                List<Thing> payload = new List<Thing>();
                payload.Add(mini);
                DropPodUtility.DropThingsNear(center, map, payload, 110, false, false, true, false);
                // 信件带"跳转"目标，玩家点一下就能看到落在哪
                Find.LetterStack.ReceiveLetter(
                    (string)"Binguin_BandMarkerDropped_Label".Translate(),
                    (string)"Binguin_BandMarkerDropped_Text".Translate(),
                    LetterDefOf.PositiveEvent,
                    new LookTargets(center, map));
                Log.Message("[冰鹅族] 已空投（小型化）演唱会地标到 " + center + "。");
                return marker;
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 空投地标失败：" + e);
                return null;
            }
        }

        public static MonumentMarker FindMarker(Map map)
        {
            if (map == null || map.listerThings == null) return null;
            List<Thing> list = map.listerThings.ThingsOfDef(Named(MarkerDefName));
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                MonumentMarker m = list[i] as MonumentMarker;
                if (m != null && !m.Destroyed) return m;
            }
            return null;
        }
    }

    /// <summary>地标存在时检查是否建完；建完就抛一个事件给后面的演出流程接。</summary>
    public class GameComponent_BinguinBandVisit : GameComponent
    {
        // 已经处理过的"建完"状态（避免每 tick 重复触发）
        private bool announcedDone;

        public GameComponent_BinguinBandVisit(Game game) : base()
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<bool>(ref announcedDone, "binguinBandAnnouncedDone", false, false);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager == null || Find.TickManager.TicksGame % 250 != 0) return;
            try
            {
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    MonumentMarker m = BinguinBandVisit.FindMarker(maps[i]);
                    if (m == null) continue;
                    if (m.AllDone && !announcedDone)
                    {
                        announcedDone = true;
                        OnVenueBuilt(maps[i], m);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 检查演唱会场地失败：" + e);
            }
        }

        private void OnVenueBuilt(Map map, MonumentMarker marker)
        {
            // ★★ 2026-09 修复：原来这里只写了日志，忘了真正把乐队叫来
            //    （用户实测「建造完后没有乐队来」就是这个原因）。
            Log.Message("[冰鹅族] 演唱会场地已建完，乐队正在赶来。");
            BinguinBandConcert.Arrive(map, marker);

            // ★★★ 2026-09 用户要求：「演唱会地标建造完后不会自动删除，还是会被点到」。
            //   原版纪念碑地标是任务结束时由任务清理的；我们这里是"建完就完事"，
            //   所以建完当场把地标销毁掉（Arrive 里已经先把 marker.Position 存进 venueCenter 了，
            //   销毁不影响场地中心）。
            try
            {
                if (marker != null && !marker.Destroyed)
                {
                    marker.Destroy(DestroyMode.Vanish);
                    Log.Message("[冰鹅族] 场地已建完，地标已自动移除。");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 移除演唱会地标失败：" + e.Message);
            }
        }
    }

    /// <summary>调试命令：立刻在殖民地附近落下演唱会地标。</summary>
    public static class DebugActions_BandVisit
    {
        [DebugAction("冰鹅族", "乐队来访：立刻生成演唱会地标")]
        public static void SpawnMarkerNow()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Message("[冰鹅族] 没有当前地图。");
                return;
            }
            BinguinBandVisit.SpawnMarker(map);
        }

        [DebugAction("冰鹅族", "乐队来访：一键建完整个场地")]
        public static void BuildAllNow()
        {
            MonumentMarker m = BinguinBandVisit.FindMarker(Find.CurrentMap);
            if (m == null)
            {
                Messages.Message("地图上没有演唱会地标，先生成一个。", MessageTypeDefOf.RejectInput);
                return;
            }
            m.DebugBuildAll();
            Messages.Message("已一键建完演唱会场地。", MessageTypeDefOf.TaskCompletion);
        }
    }

    /// <summary>
    /// 修补 MonumentMarker.AllowedStuffsFor：
    ///   ① 【判空兜底 —— 必须的】原版对"不选材"的构件会 return null，
    ///      而唯一调用方 MonumentMarker.&lt;GetPlaceBlueprintsCommand&gt;b__0 紧接着就是
    ///      GenCollection.Any(list)（实现 = list.Count > 0，不判空）→ 点"放置蓝图"直接空引用。
    ///      我们场地里的乐器全是固定造价、不选材 → 每一件都会踩到。
    ///      这里把 null 换成【空表】：Any 为 false → 原版走
    ///      PlaceBlueprintsSimilarTo(buildable, null)，照常铺蓝图、不弹选材菜单。
    ///   ② 【放行全部材料】原来给长桌用的（用户要求"桌子随意材料"）。
    ///      现在场地里已经没有要选材的构件了，保留着不吃亏：
    ///      将来若再加需选材的构件，玩家就能从全部材料里挑。
    /// </summary>
    [HarmonyPatch(typeof(MonumentMarker), "AllowedStuffsFor")]
    public static class Patch_BandVisitFreeStuffs
    {
        public static void Postfix(ref List<ThingDef> __result)
        {
            try
            {
                if (__result == null)
                {
                    __result = new List<ThingDef>();   // ★ 关键：null → 空表，避免 Any(null) 空引用
                    return;
                }
                if (__result.Count == 0) return;       // 本来就是空表 → 不动

                // 放行【全部可用材料】
                List<ThingDef> all = new List<ThingDef>();
                foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
                {
                    if (d.stuffProps != null)
                    {
                        all.Add(d);
                    }
                }
                if (all.Count > 0)
                {
                    __result = all;
                }
            }
            catch (Exception)
            {
                // 出问题就保留原版结果（至少要保证不是 null）
                if (__result == null) __result = new List<ThingDef>();
            }
        }
    }
}
