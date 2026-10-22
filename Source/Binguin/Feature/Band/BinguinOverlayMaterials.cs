using UnityEngine;
using Verse;

namespace Binguin.Feature.Band
{
    /// <summary>
    /// 覆盖层材质（问号等）。
    /// ★ 2026-10-22：原来问号材质是 `BinguinBandConcert` 的静态字段，加载时会刷一条
    ///   「Type BinguinBandConcert probably needs a StaticConstructorOnStartup attribute,
    ///    because it has a field questionMarkMat of type Material」的 WARN
    ///   —— 静态 Material 必须在主线程构造。放进这个带特性的类里就没有这条警告了，
    ///   也避免第一次画问号时才懒加载材质。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BinguinOverlayMaterials
    {
        public static readonly Material QuestionMark = MaterialPool.MatFrom(
            "UI/Overlays/QuestionMark", ShaderDatabase.MetaOverlay);
    }
}
