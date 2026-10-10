// ============================================================================
// 鱼池连接材质（2026-08-20 用户需求：相连池子"看上去是一起的"）
//
// ★ 2026-08-20 晚：v1 用 graphicClass 替换 Graphic_BinguinPondLink 失败——
//   放置时蓝图 ghost 管线（GhostDrawer→GhostGraphicFor）对自定义 Graphic
//   类实例抛 NRE（Player.log: NullReference in GhostGraphicFor），无法放置。
//   v2 改为【thingClass 替换 + override Print】：def.graphic 保持原版
//   Graphic_Single（ghost/蓝图/拆除预览全走原版，正常），放置后真正的地图
//   网格打印由自定义 Building_BinguinPond.Print 接管——按「连接 mask」
//   （四方向是否有【同组】相邻鱼池）选变体贴图打印：
//     - 与同组池相邻的边 → 该侧池沿"开口"（水面延伸至贴图边缘）
//     - 不与同组池相邻的边 → 保留原池沿
//   相连的一组池子水面连成一片，只有组的外圈有池沿 → 视觉上一个"大池"。
//
// ★ v3（2026-08-20 用户实测修复）：
//   a. 贴图上下反 → RimWorld 建筑贴图 v 轴向下=北：北邻(-z)映射贴图下沿、
//      南邻(+z)映射上沿（E/W 不变）——连接 mask 位：下沿=1 上沿=4 E=2 W=8；
//   b. 四面包围（mask=15）原本 fallback 成原图（四沿）→ 生成 FishPond_l15.png
//      （四面全开口=组内部池），变体数组扩到 16，comp 缺失才 fallback 原图。
//
// mesh 刷新：Print 只在 MapMeshDirty 时执行 → CompBinguinFishPond 在
//   PostSpawnSetup/PostDeSpawn 时对自身与四邻 dirty（见 Comp_BinguinFishPond.cs
//   的 DirtyConnectionVisuals；1.6 用 MapMeshFlagDef"Buildings"的 mask）。
//
// 贴图：FishPond_l0..l15.png（128x128），由 wiki-tools/Save-BinguinPondLink
//   Textures.ps1 从 FishPond.png 派生（l0 = 无连接=原图四沿）。
// ★ XML 零自定义类型：pond.thingClass 由 BinguinDefPatches 静态构造替换。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Fishing
{
    // ★ 2026-09：本类有 static 资源字段（Texture2D / Graphic[]），RimWorld 启动时会警告
    //   「probably needs a StaticConstructorOnStartup attribute ... must be loaded in the main thread」。
    //   加上该特性 → 静态构造在主线程启动时执行，警告消除、资源加载时机也正确。
    [StaticConstructorOnStartup]
    public class Building_BinguinPond : Building
    {
        private static Graphic[] cachedVariants; // mask 0..15 -> 变体贴图
        private static ThingDef cachedPondDef;

        private static ThingDef PondDef
        {
            get
            {
                if (cachedPondDef == null)
                {
                    cachedPondDef = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_FishPond");
                }
                return cachedPondDef;
            }
        }

        private static Graphic[] VariantGraphics
        {
            get
            {
                if (cachedVariants == null)
                {
                    cachedVariants = new Graphic[16];
                }
                return cachedVariants;
            }
        }

        // 建筑贴图打进地图网格时调用 → 按连接 mask 选变体贴图打印
        public override void Print(SectionLayer layer)
        {
            Graphic g = null;
            try
            {
                g = ChooseGraphic();
            }
            catch (Exception e)
            {
                BinguinLogUtility.WarningOnce("鱼池连接贴图选择失败：" + e.Message, 832612);
                g = null;
            }
            if (g == null || g == def.graphic)
            {
                base.Print(layer);
                return;
            }
            g.Print(layer, this, 0f);
        }

        private Graphic ChooseGraphic()
        {
            int mask = ConnectionMask();
            if (mask < 0)
            {
                return def.graphic;
            }
            Graphic g = VariantGraphics[mask];
            if (g == null)
            {
                string basePath = def.graphicData != null ? def.graphicData.texPath : "Things/Building/Binguin/FishPond";
                Vector2 size = def.graphicData != null ? def.graphicData.drawSize : new Vector2(2f, 2f);
                Shader sh = def.graphicData != null && def.graphicData.shaderType != null
                    ? def.graphicData.shaderType.Shader
                    : ShaderDatabase.Cutout;
                Color col = def.graphicData != null ? def.graphicData.color : Color.white;
                Color col2 = def.graphicData != null ? def.graphicData.colorTwo : Color.white;
                g = GraphicDatabase.Get<Graphic_Single>(basePath + "_l" + mask, sh, size, col, col2);
                VariantGraphics[mask] = g;
            }
            return g;
        }

        // 计算连接 mask：四方向紧邻格（间距 2）是否有同组鱼池
        // ★ 贴图 v 轴与地图相反：贴图上沿 = 南(+z)、贴图下沿 = 北(-z)
        //   （2026-08-20 用户实测"贴图上下反"后对调；E/W 无镜像不变）。
        //   mask 位与生成脚本一致：bit1=贴图顶部开口 bit2=右 bit4=底部 bit8=左
        //   故：北邻(-z) → bit4（贴图底=世界北）；南邻(+z) → bit1。
        private int ConnectionMask()
        {
            if (Map == null || PondDef == null)
            {
                return -1;
            }
            CompBinguinFishPond comp = this.TryGetComp<CompBinguinFishPond>();
            if (comp == null)
            {
                return -1;
            }
            CompBinguinFishPond lead = comp.LeaderComp;
            if (lead == null || lead.parent == null)
            {
                return -1;
            }
            Thing leadThing = lead.parent;
            int mask = 0;
            if (HasGroupNeighbor(leadThing, new IntVec3(0, 0, -2))) mask |= 4; // 北邻 → 贴图下沿开口(bit4)
            if (HasGroupNeighbor(leadThing, new IntVec3(2, 0, 0))) mask |= 2;  // 东邻 → 贴图右沿开口(bit2)
            if (HasGroupNeighbor(leadThing, new IntVec3(0, 0, 2))) mask |= 1;  // 南邻 → 贴图上沿开口(bit1)
            if (HasGroupNeighbor(leadThing, new IntVec3(-2, 0, 0))) mask |= 8; // 西邻 → 贴图左沿开口(bit8)
            return mask;
        }

        private bool HasGroupNeighbor(Thing leadThing, IntVec3 offset)
        {
            IntVec3 cell = Position + offset;
            if (!cell.InBounds(Map))
            {
                return false;
            }
            List<Thing> things = Map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == this || t.Destroyed || t.def != PondDef)
                {
                    continue;
                }
                CompBinguinFishPond c = t.TryGetComp<CompBinguinFishPond>();
                if (c == null)
                {
                    continue;
                }
                CompBinguinFishPond lead = c.LeaderComp;
                if (lead != null && lead.parent == leadThing)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
