// ============================================================================
// 冰鹅族大气控制仪 —— Comp
//
// 2026-08-19 初版：操控天气（晴/雨/暴风雨/雪/暴风雪）。
// 2026-09 用户修订（两次）：
//   ① 「改为充电 24 小时才能使用一次，而不是使用后需要消耗电量」
//   ② 「充电 24 小时后自动进入低耗能模式」
//   → 最终机制：
//      · 充能阶段：通电时每 tick 累积，满 24 小时（60000 tick）才能用一次；
//        充能功耗 = 700W（与 XML basePowerConsumption 一致）
//      · 充满 → 自动进入【低耗能模式】：功耗降到 10W，期间随时可用
//      · 使用一次 → 立即清空充能，回到 700W 重新充能 24 小时
//      · 停电 / 关电源开关 → 充能暂停（PowerOn=false 时不累积）
//      · 旧版"启动后 12 小时消耗 4000W"的爆发耗电已彻底删除
//
// ★ 功耗实现（1.6 反射确认）：CompPowerTrader.PowerOutput 正值=发电、负值=消耗。
//   ★ 2026-09 性能：不再每 tick 写 PowerOutput（那只是重复写同一个值）。
//   功耗只在三个状态跃迁处更新：PostSpawnSetup（含读档）、刚充满、用掉一次充能
//   （TryChangeWeather）——这三处覆盖了值会变化的全部情形。
// ★ XML 零自定义类型：compClass 由 BinguinDefPatches 代码挂载
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin
{
    public class CompProperties_BinguinWeather : CompProperties
    {
        // 充能时间 tick：60000 = 24 小时（2026-09 用户定稿）
        public int rechargeTicks = 60000;
        // 充能功耗（W，负值 = 消耗；对应 XML basePowerConsumption=700）
        public float chargePower = -700f;
        // 充满后低耗能待机功耗（W，负值 = 消耗）
        public float chargedIdlePower = -10f;

        public CompProperties_BinguinWeather()
        {
            compClass = typeof(CompBinguinWeather);
        }
    }

    public class CompBinguinWeather : ThingComp
    {
        // 已积累的充能 tick
        private int chargeProgress;
        private CompPowerTrader powerComp;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.TryGetComp<CompPowerTrader>();
            ApplyPowerOutput();
        }

        // 是否已充能完毕（可以切换天气 = 低耗能模式）
        public bool IsCharged
        {
            get { return chargeProgress >= Props.rechargeTicks; }
        }

        // 剩余充能小时数（1 小时 = 2500 tick）
        public float HoursLeft
        {
            get
            {
                int left = Props.rechargeTicks - chargeProgress;
                if (left < 0)
                {
                    left = 0;
                }
                return left / 2500f;
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!IsCharged)
            {
                // ★ 只有通电时才充能（没电 / 关了电源开关 → 充能暂停）
                if (powerComp != null && powerComp.PowerOn)
                {
                    chargeProgress++;
                    if (IsCharged)
                    {
                        Messages.Message("Binguin_CompWeather_01".Translate(),
                            MessageTypeDefOf.PositiveEvent, false);
                        Log.Message("[冰鹅族] 大气控制仪：充能完毕，进入低耗能模式（"
                            + Props.chargedIdlePower + "W）");
                        // ★ 2026-09：充满是唯一会改变功耗的状态跃迁 → 只在
                        //   跃迁处写一次 PowerOutput（其余时刻值不变）。
                        ApplyPowerOutput();
                    }
                }
            }
            // ★ 2026-09 性能：原来【每 tick】都调一次 ApplyPowerOutput（内部读
            //   Props/IsCharged 若干次）。功耗只由"是否充满"决定，而它只在这里
            //   与 TryChangeWeather（用掉一次充能）发生变化，故不再逐 tick 校正。
        }

        // 按当前状态设置功耗（负值 = 消耗）
        private void ApplyPowerOutput()
        {
            if (powerComp == null)
            {
                return;
            }
            float want = IsCharged ? Props.chargedIdlePower : Props.chargePower;
            if (powerComp.PowerOutput != want)
            {
                powerComp.PowerOutput = want;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<int>(ref chargeProgress, "chargeProgress", 0, false);
        }

        public override string CompInspectStringExtra()
        {
            if (IsCharged)
            {
                return "Binguin_CompWeather_02".Translate() + (-Props.chargedIdlePower).ToString("0")
                    + "Binguin_CompWeather_03".Translate();
            }
            return "Binguin_CompWeather_04".Translate() + HoursLeft.ToString("0.0") + "Binguin_CompWeather_05".Translate()
                + (-Props.chargePower).ToString("0") + "W）";
        }

        // 选中建筑的天气按钮
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            // ★ 未充满时给一个灰掉的按钮显示充能进度（原版没有进度条 API，用按钮代替）
            if (!IsCharged)
            {
                Command_Action status = new Command_Action
                {
                    defaultLabel = "Binguin_CompWeather_06".Translate() + HoursLeft.ToString("0.0") + "h",
                    defaultDesc = "Binguin_CompWeather_07".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Things/Building/Binguin/AtmosphereController", false),
                    action = delegate { }
                };
                status.Disable("Binguin_CompWeather_08".Translate() + HoursLeft.ToString("0.0") + "Binguin_CompWeather_09".Translate());
                yield return status;
            }

            // ★ 天气按钮图标统一用大气控制仪贴图（原版天气无独立图标文件，
            //   Weather/* 路径不存在会报错；按钮靠文字区分）
            yield return MakeWeatherCommand("Binguin_Weather_Clear".Translate(), "Binguin_CompWeather_10".Translate());
            yield return MakeWeatherCommand("Binguin_Weather_Rain".Translate(), "Binguin_CompWeather_11".Translate());
            yield return MakeWeatherCommand("Binguin_Weather_Thunderstorm".Translate(), "Binguin_CompWeather_12".Translate());
            yield return MakeWeatherCommand("Binguin_Weather_Snow".Translate(), "Binguin_CompWeather_13".Translate());
            yield return MakeWeatherCommand("Binguin_Weather_Snowstorm".Translate(), "Binguin_CompWeather_14".Translate());
        }

        private Gizmo MakeWeatherCommand(string label, string desc)
        {
            Command_Action cmd = new Command_Action
            {
                defaultLabel = label,
                defaultDesc = desc,
                icon = ContentFinder<Texture2D>.Get("Things/Building/Binguin/AtmosphereController", false),
                action = delegate
                {
                    TryChangeWeather(label);
                }
            };
            // 充能没满 → 禁用（2026-09 新机制：充能 24 小时才能用一次）
            if (!IsCharged)
            {
                cmd.Disable("Binguin_CompWeather_15".Translate() + HoursLeft.ToString("0.0") + "Binguin_CompWeather_16".Translate());
            }
            else if (!HasPowerNow())
            {
                // ★ 2026-08-19 v4：无电时禁用天气按钮（PowerOn 为 false =
                //   未通电/断电/开关关闭），修复"不通电也能改天气"的 bug
                cmd.Disable("Binguin_CompWeather_17".Translate());
            }
            return cmd;
        }

        // 当前是否有电（CompPowerTrader.PowerOn：通电且开关打开）
        private bool HasPowerNow()
        {
            return powerComp != null && powerComp.PowerOn;
        }

        private void TryChangeWeather(string label)
        {
            try
            {
                Map map = parent.Map;
                if (map == null)
                {
                    return;
                }
                // ★ 防御检查：无电不能启动（按钮已禁用，这里双保险）
                if (!HasPowerNow())
                {
                    Messages.Message("Binguin_CompWeather_18".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                // ★ 2026-09 新机制：必须充能满 24 小时
                if (!IsCharged)
                {
                    Messages.Message("Binguin_CompWeather_19".Translate() + HoursLeft.ToString("0.0") + "Binguin_CompWeather_20".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return;
                }
                WeatherDef target = GetWeatherFor(label);
                if (target == null)
                {
                    Log.Warning("[冰鹅族] 大气控制仪：找不到天气 " + label);
                    return;
                }
                map.weatherManager.TransitionTo(target);
                // 用掉这次充能 → 回到充能状态（100W，24 小时）
                chargeProgress = 0;
                ApplyPowerOutput();
                Messages.Message("Binguin_CompWeather_21".Translate() + label + "Binguin_CompWeather_22".Translate(),
                    MessageTypeDefOf.NeutralEvent, false);
                Log.Message("[冰鹅族] 大气控制仪：天气切换为 " + label + "（" + target.defName
                    + "），重新充能 24 小时");
            }
            catch (System.Exception e)
            {
                Log.Warning("[冰鹅族] 大气控制仪切换天气异常: " + e.Message);
            }
        }

        private CompProperties_BinguinWeather Props
        {
            get { return (CompProperties_BinguinWeather)props; }
        }

        private static WeatherDef GetWeatherFor(string label)
        {
            switch (label)
            {
                case "晴": return DefDatabase<WeatherDef>.GetNamedSilentFail("Clear");
                case "雨": return DefDatabase<WeatherDef>.GetNamedSilentFail("Rain");
                case "暴风雨": return DefDatabase<WeatherDef>.GetNamedSilentFail("RainyThunderstorm");
                case "雪": return DefDatabase<WeatherDef>.GetNamedSilentFail("SnowGentle");
                case "暴风雪": return DefDatabase<WeatherDef>.GetNamedSilentFail("SnowHard");
                default: return null;
            }
        }
    }
}
