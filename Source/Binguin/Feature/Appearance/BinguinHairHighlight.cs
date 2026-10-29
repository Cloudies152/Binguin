using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Appearance
{
    /// <summary>单个人的挑染设置。</summary>
    public class BinguinHairHighlightEntry : IExposable
    {
        public bool enabled;
        public float r = 0.93f;
        public float g = 0.36f;
        public float b = 0.36f;

        public Color Color
        {
            get { return new Color(r, g, b); }
        }

        public void ExposeData()
        {
            Scribe_Values.Look<bool>(ref enabled, "on", false, false);
            Scribe_Values.Look<float>(ref r, "r", 0.93f, false);
            Scribe_Values.Look<float>(ref g, "g", 0.36f, false);
            Scribe_Values.Look<float>(ref b, "b", 0.36f, false);
        }
    }

    /// <summary>
    /// 挑染数据（2026-10-29）。
    ///
    /// 为什么不挂 ThingComp：往种族 ThingDef 里加 &lt;comps&gt; 会覆盖掉继承自 Human 的
    /// comps（Anomaly 的收容/研究组件），风险大。这里改成 GameComponent + 字典存 Pawn。
    ///
    /// 渲染方式：发型贴图旁边放一张 `_hl` 叠加贴图（只保留要挑染的那几缕头发），
    /// 在头发节点之后再画一遍、用挑染颜色着色 —— 不依赖 CutoutComplex 的两色蒙版，
    /// 也不改原发型贴图。贴图由 Tools\Make-BinguinHairHighlightTextures.py 生成。
    /// </summary>
    public class GameComponent_BinguinHairHighlight : GameComponent
    {
        private Dictionary<Pawn, BinguinHairHighlightEntry> entries =
            new Dictionary<Pawn, BinguinHairHighlightEntry>();

        public GameComponent_BinguinHairHighlight(Game game) : base()
        {
        }

        public static GameComponent_BinguinHairHighlight Instance
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }
                return Current.Game.GetComponent<GameComponent_BinguinHairHighlight>();
            }
        }

        /// <summary>取某个人的挑染设置；create=true 时没有就新建。</summary>
        public BinguinHairHighlightEntry Get(Pawn p, bool create)
        {
            if (p == null)
            {
                return null;
            }
            BinguinHairHighlightEntry e;
            if (entries.TryGetValue(p, out e))
            {
                return e;
            }
            if (!create)
            {
                return null;
            }
            e = new BinguinHairHighlightEntry();
            entries[p] = e;
            return e;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look<Pawn, BinguinHairHighlightEntry>(
                ref entries, "hairHighlights", LookMode.Reference, LookMode.Deep);
            if (entries == null)
            {
                entries = new Dictionary<Pawn, BinguinHairHighlightEntry>();
            }
            // 读档后清掉已经不存在/已经死亡的 pawn 引用
            List<Pawn> dead = null;
            foreach (KeyValuePair<Pawn, BinguinHairHighlightEntry> kv in entries)
            {
                if (kv.Key == null || kv.Key.Destroyed)
                {
                    if (dead == null) dead = new List<Pawn>();
                    dead.Add(kv.Key);
                }
            }
            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                {
                    entries.Remove(dead[i]);
                }
            }
        }
    }

    /// <summary>挑染叠加贴图的材质缓存（按 贴图路径 + 朝向 + 颜色 缓存）。</summary>
    public static class BinguinHairHighlightCache
    {
        private static readonly Dictionary<string, Material> cache =
            new Dictionary<string, Material>();

        public static Material Get(string texPath, Rot4 facing, Color color)
        {
            if (texPath.NullOrEmpty())
            {
                return null;
            }
            string dir = facing == Rot4.North ? "north" : (facing == Rot4.South ? "south" : "east");
            string path = texPath + "_" + dir + "_hl";
            int cr = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int cg = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int cb = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            string key = path + "|" + cr + "_" + cg + "_" + cb;

            Material mat;
            if (cache.TryGetValue(key, out mat) && mat != null)
            {
                return mat;
            }
            Texture2D tex = ContentFinder<Texture2D>.Get(path, false);
            if (tex == null)
            {
                return null;
            }
            mat = MaterialPool.MatFrom(new MaterialRequest(
                tex, ShaderDatabase.Cutout, new Color(cr / 255f, cg / 255f, cb / 255f)));
            if (cache.Count > 256)
            {
                cache.Clear();
            }
            cache[key] = mat;
            return mat;
        }
    }

    /// <summary>在头发节点之后追加一遍"挑染层"。只对冰鹅族、且开着挑染的人生效。</summary>
    [HarmonyPatch(typeof(PawnRenderNodeWorker), "AppendDrawRequests")]
    public static class Patch_BinguinHairHighlightDraw
    {
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms,
            List<PawnGraphicDrawRequest> requests)
        {
            try
            {
                if (!(node is PawnRenderNode_Hair))
                {
                    return;
                }
                Pawn p = parms.pawn;
                if (p == null || p.def == null || !BinguinRaceUtility.IsBinguin(p))
                {
                    return;
                }
                GameComponent_BinguinHairHighlight gc = GameComponent_BinguinHairHighlight.Instance;
                if (gc == null)
                {
                    return;
                }
                BinguinHairHighlightEntry e = gc.Get(p, false);
                if (e == null || !e.enabled)
                {
                    return;
                }
                Graphic g = node.GraphicFor(p);
                if (g == null)
                {
                    return;
                }
                Material mat = BinguinHairHighlightCache.Get(g.path, parms.facing, e.Color);
                if (mat == null)
                {
                    return;
                }
                Mesh mesh = node.GetMesh(parms);
                if (mesh == null)
                {
                    return;
                }
                requests.Add(new PawnGraphicDrawRequest(node, mesh, mat));
            }
            catch (Exception)
            {
                // 纯绘制，出错不影响游戏
            }
        }
    }

    /// <summary>
    /// 梳妆台（Building_StylingStation）的右键菜单里加一条「挑染头发…」。
    /// ★ 2026-10-29 按作者要求：挑染入口放在梳妆台，而不是小人身上的 gizmo。
    /// </summary>
    [HarmonyPatch(typeof(Building_StylingStation), "GetFloatMenuOptions")]
    public static class Patch_BinguinHairHighlightStation
    {
        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> __result,
            Building_StylingStation __instance, Pawn selPawn)
        {
            if (__result != null)
            {
                foreach (FloatMenuOption o in __result)
                {
                    yield return o;
                }
            }
            if (__instance == null || selPawn == null || selPawn.Dead || selPawn.def == null
                || !BinguinRaceUtility.IsBinguin(selPawn))
            {
                yield break;
            }
            Pawn actor = selPawn;
            yield return new FloatMenuOption("Binguin_HairHighlight_StationMenu".Translate(),
                delegate
                {
                    Find.WindowStack.Add(new Dialog_BinguinHairHighlight(actor));
                });
        }
    }
}
