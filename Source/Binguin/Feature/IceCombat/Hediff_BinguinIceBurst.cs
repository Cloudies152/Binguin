// ============================================================================
// 冰爆状态的自定义显示与衰减（2026-08-20 用户需求 v4）：
//   1) 健康状态里冰爆后面实时显示百分比进度（如「冰爆 (36%)」）；
//   2) 状态名用【浅蓝色】显示；
//   3) ★ 随时间衰减：每游戏小时（2500 tick）下降 50% 进度
//      （severity -0.5/小时 → 每 tick -0.0002；从 100% 起 2 小时归零）。
//     衰减放在 Hediff.Tick（每 tick 必跑，不依赖 comp tick）。
// hediffClass 由 BinguinDefPatches 静态构造替换（XML 仍是原版 Hediff）。
// ============================================================================

using UnityEngine;
using Verse;

namespace Binguin.Feature.IceCombat
{
    public class Hediff_BinguinIceBurst : Hediff
    {
        // 每小时下降的严重度（0.5 = 50 个百分点/游戏小时）
        private const float DecayPerHour = 0.5f;
        private const float TicksPerHour = 2500f;

        public override void Tick()
        {
            base.Tick();
            if (Severity > 0f)
            {
                float s = Severity - DecayPerHour / TicksPerHour;
                Severity = s > 0f ? s : 0f;
            }
        }

        public override string LabelInBrackets
        {
            get { return (Severity * 100f).ToString("0") + "%"; }
        }

        public override Color LabelColor
        {
            get { return new Color(0.55f, 0.85f, 1f); } // 浅蓝
        }
    }
}
