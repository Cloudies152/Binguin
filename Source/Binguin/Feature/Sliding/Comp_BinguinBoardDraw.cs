// ============================================================================
// 企鹅滑板 —— 穿戴时脚下滑板绘制（装备体现在人物上）
//
// 2026-08-19 用户要求：滑板装备后要体现在人物上（装备时出现在人物脚下）。
// 实现：ThingComp.CompDrawWornExtras —— Apparel.DrawWornExtras（virtual）
// 基类会遍历装备 comps 调用它，PawnRenderer 渲染人物时每帧执行。
// 在人物脚下的地面绘制一张俯视滑板贴图（PenguinBoardGround.png）。
// ============================================================================

using RimWorld;
using UnityEngine;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Sliding
{
    public class CompProperties_BinguinBoardDraw : CompProperties
    {
        public CompProperties_BinguinBoardDraw()
        {
            compClass = typeof(CompBinguinBoardDraw);
        }
    }

    public class CompBinguinBoardDraw : ThingComp
    {
        private Graphic cachedGroundGraphic;

        // 穿戴时每帧：在人物脚下地面画滑板
        public override void CompDrawWornExtras()
        {
            try
            {
                Apparel apparel = parent as Apparel;
                Pawn wearer = apparel != null ? apparel.Wearer : null;
                if (wearer == null || wearer.Destroyed || !wearer.Spawned || wearer.Map == null)
                {
                    return;
                }
                if (cachedGroundGraphic == null)
                {
                    cachedGroundGraphic = GraphicDatabase.Get<Graphic_Single>(
                        "Things/Apparel/Binguin/PenguinBoardGround",
                        ShaderDatabase.Transparent,
                        new Vector2(1f, 1f),
                        Color.white);
                }
                // 人物脚下贴地（y 极低，避免 z-fighting）
                Vector3 pos = wearer.DrawPos;
                pos.y = 0.01f;
                // ★ 用水平面（plane10）绘制：俯视滑板平铺地面（Graphic.Draw 是垂直面会"立起来"）
                //   滑板长轴随人物朝向（Rot4.AsQuat），板沿前后方向（z 长）
                Matrix4x4 m = Matrix4x4.TRS(
                    pos,
                    wearer.Rotation.AsQuat,
                    new Vector3(0.7f, 1f, 1.3f));
                Graphics.DrawMesh(MeshPool.plane10, m, cachedGroundGraphic.MatSingle, 0);
            }
            catch (System.Exception e)
            {
                BinguinLogUtility.Log("脚下滑板绘制失败: " + e.Message, severity: 1, isDebug: false);
            }
        }
    }
}
