// ============================================================================
// 装配钓竿 · 调试工具（2026-10-06 用户需求）
//
// 用户需求原文：「能不能做个debug用的快速生成四种配件各一个和4个高级零部件，
//   这样测试快一点」
//
// 提供两处入口：
//   ① 开发者模式菜单：`[DebugAction("冰鹅族", ...)]`（在 Dev 模式顶部工具栏里）
//   ② **装配台的按钮**（`CompBinguinRodAssembly.CompGetGizmosExtra` 里追加，
//      而且**只在开发者模式显示** —— 正常游玩不会看到，不污染界面）
//
// ★ 「四种配件」= 竿身 / 竿头 / 鱼钩 / 鱼饵：
//   竿身/竿头/鱼钩是 **stuff 物品**（XML `<stuffCategories>Metallic</stuffCategories>`，
//   材质在制作时选）⇒ `ThingMaker.MakeThing(def, stuff)` **必须给材质**，
//   否则原版会报 `MakeThing error: ... is madeFromStuff but stuff=null`。
//   这里默认给钢铁（可选项还能给玻璃钢/铀，方便测不同材质的属性差异）。
//   鱼饵不是 stuff 物品，直接给 null。
//
// ★ 高级零部件 = `ComponentSpacer` ×4（装配收尾时要扣的那 4 个）。
//
// ★ 所有生成都落在 `DropCellFinder.TradeDropSpot(map)`（和原版"空投物资"落点
//   一致，保证是可站立、可达、可堆放的格子），生成失败只记日志不影响游戏。
// ============================================================================

using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;

namespace Binguin
{
    public static class DebugActions_RodAssembly
    {
        /// <summary>竿身/竿头/鱼钩默认用的材质（可换成 Plasteel / Uranium 等）。</summary>
        private static ThingDef DefaultMetal
        {
            get { return ThingDefOf.Steel; }
        }

        // ------------------------------------------------------------------
        // 开发者菜单入口
        // ------------------------------------------------------------------
        [DebugAction("冰鹅族", "钓竿装配：生成测试套件（4 配件 + 4 高级零部件）")]
        public static void SpawnFullKit()
        {
            SpawnKit(DefaultMetal, false);
        }

        [DebugAction("冰鹅族", "钓竿装配：生成测试套件（玻璃钢材质）")]
        public static void SpawnFullKitPlasteel()
        {
            SpawnKit(ThingDef.Named("Plasteel"), false);
        }

        [DebugAction("冰鹅族", "钓竿装配：只生成 4 个高级零部件")]
        public static void SpawnComponentsOnly()
        {
            SpawnKit(null, true);
        }

        // ------------------------------------------------------------------
        // 实际生成
        // ------------------------------------------------------------------
        /// <summary>
        /// 生成测试套件。
        /// ★ `onlyComponents=true` 时只给 4 个高级零部件（配件那部分跳过）。
        /// </summary>
        public static bool SpawnKit(ThingDef metal, bool onlyComponents)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("（调试）当前没有地图，无法生成。", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            IntVec3 pos;
            try
            {
                pos = DropCellFinder.TradeDropSpot(map);
            }
            catch
            {
                pos = map.Center;
            }
            return SpawnKitAt(map, pos, metal, onlyComponents);
        }

        /// <summary>在指定位置生成测试套件（装配台按钮用这个，直接落在装配台旁）。</summary>
        public static bool SpawnKitAt(Map map, IntVec3 pos, ThingDef metal, bool onlyComponents)
        {
            if (map == null || !pos.IsValid || !pos.InBounds(map))
            {
                return false;
            }
            if (metal == null)
            {
                metal = DefaultMetal;
            }

            List<Thing> spawned = new List<Thing>();

            if (!onlyComponents)
            {
                // ---- 四种配件：竿身 / 竿头 / 鱼钩 / 鱼饵 ----
                //   ① 竿身（长制：伤害 ×120%）
                spawned.Add(MakePart(BinguinRodUtility.ShaftLong, metal));
                //   ② 竿头（刀制：割+刺）
                spawned.Add(MakePart(BinguinRodUtility.TipBlade, metal));
                //   ③ 鱼钩（直钩）
                spawned.Add(MakePart(BinguinRodUtility.HookStraight, metal));
                //   ④ 鱼饵（非 stuff 物品，材质传 null）
                spawned.Add(MakePart("Binguin_RodBait", null));
            }

            // ---- 4 个高级零部件 ----
            ThingDef spacer = ThingDef.Named("ComponentSpacer");
            if (spacer != null)
            {
                Thing comp = ThingMaker.MakeThing(spacer, null);
                if (comp != null)
                {
                    comp.stackCount = 4;
                    spawned.Add(comp);
                }
            }
            else
            {
                Log.Warning("[冰鹅族] （调试）找不到 ComponentSpacer（高级零部件），未生成。");
            }

            // ---- 落地 ----
            int ok = 0;
            for (int i = 0; i < spawned.Count; i++)
            {
                Thing t = spawned[i];
                if (t == null)
                {
                    continue;
                }
                try
                {
                    GenSpawn.Spawn(t, pos, map, WipeMode.Vanish);
                    ok++;
                }
                catch (System.Exception e)
                {
                    Log.Warning("[冰鹅族] （调试）生成 " + t.def.defName + " 失败：" + e.Message);
                }
            }

            Log.Message("[冰鹅族] （调试）已生成装配测试套件于 " + pos
                + "：成功 " + ok + " 件（材质=" + (metal != null ? metal.label : "无") + "）");
            Messages.Message("（调试）已生成装配测试套件（" + ok + " 件）于 " + pos + "。",
                MessageTypeDefOf.TaskCompletion, false);
            return ok > 0;
        }

        /// <summary>
        /// 生成一件配件。`stuff` 为 null 且该 def 需要材质时，用默认金属兜底。
        /// ★ 必须给材质：竿身/竿头/鱼钩的 XML 里有 `<stuffCategories>Metallic</stuffCategories>`，
        ///   不给就会报 `MakeThing error: ... is madeFromStuff but stuff=null`。
        /// </summary>
        private static Thing MakePart(string defName, ThingDef stuff)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Log.Warning("[冰鹅族] （调试）找不到配件 def：" + defName);
                return null;
            }
            // 该 def 需要材质但没给 → 用默认金属
            ThingDef useStuff = stuff;
            if (useStuff == null && def.MadeFromStuff)
            {
                useStuff = DefaultMetal;
            }
            return ThingMaker.MakeThing(def, useStuff);
        }
    }
}
