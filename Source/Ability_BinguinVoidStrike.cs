// ============================================================================
// 尚方宝剑 · 技能「天顶鱼斩」效果（2026-10-06 用户需求）
//
// 用户规格：「会类似泰拉瑞亚的天顶剑一样远程进行 14 条鱼的轮流斩击，
//            对椭圆形范围造成范围伤害，每次斩击 10 钝器伤害（附带冰爆），
//            100% 钝器穿透，每秒斩击 14 次」
//
// 实现拆解：
//   ① **14 条鱼轮流**：每次斩击从 `BinguinVoidSwordUtility.FishDefs` 里取一条鱼，
//      下标存在武器 comp 的 `nextFishIndex` 上 ⇒ 跨施放连续、跨存档连续
//      （连打两次不会"每次都是鲑鱼"）。
//   ② **椭圆范围**：以瞄准点为中心，**长轴沿"施法者→瞄准点"方向**的椭圆。
//      判定公式：把目标相对中心的偏移旋转到长轴/短轴坐标系，
//      `(u/长半轴)² + (v/短半轴)² ≤ 1` 即命中。
//      半轴长度、最大距离等全部来自 `CompProperties_BinguinVoidSword`（XML 可调）。
//   ③ **每次 10 钝击 + 冰爆**：走自定义 DamageDef `Binguin_FishBluntDamage`
//      （worker = `DamageWorker_BinguinFishBlunt`，面板伤害 1:1 累积冰爆）。
//   ④ **100% 钝器穿透**：`armorPenetration = 1`（DamageInfo 的第 3 个参数）。
//      另外**技能伤害不吃目标的钝器护甲减伤**是不必要的 —— 原版穿透 1.0 已经
//      足以让绝大多数护甲完全失效（原版 armorRating 上限约 1.x）。
//   ⑤ **每秒 14 次**：一次施放把 14 次斩击**一口气结算**（表现层再逐条放音效/浮字）。
//      这样既满足"每秒 14 次"的观感，也不引入每 tick 状态机（不会污染存档、
//      不会因为读档/中断而卡在半途）。
//   ⑥ **只打敌人**：复用高级钓竿技能的目标筛选规则（不打自己、不打友军、不打盟友），
//      额外要求**视线通畅**（不隔墙斩人）。
//
// ★ 表现层：每次斩击在目标位置放冰屑 Fleck + 播放"某种鱼被甩出去"的视觉，
//   并在目标身上掷出伤害浮字。所有 Fleck 都有 null 兜底，缺贴图也不会崩。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin
{
    public class CompAbilityEffect_BinguinVoidStrike : CompAbilityEffect
    {
        // ★ 基类 Valid/CanApplyOn 对特定子类型做硬 cast（会 InvalidCastException，
        //   表现为日志里满屏 Root level exception in OnGUI）⇒ 自定义效果必须
        //   override 返回 true（与 CompAbilityEffect_BinguinPierce 同一处理）。
        public override bool Valid(LocalTargetInfo target, bool throwMessages)
        {
            return true;
        }

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            return true;
        }

        /// <summary>
        /// 从代码拿到本次施放的调参。
        /// ★ `CompAbilityEffect.Props` 的类型是 `CompProperties_AbilityEffect`，
        ///   本 mod 的数值参数放在子类 `CompProperties_BinguinVoidStrike` 上
        ///   （由 `BinguinDefPatches` 把那一条 comp 的 compClass 换成本效果类，
        ///   数值直接用子类字段默认值）⇒ 这里 `as` 转型 + null 兜底。
        /// </summary>
        private IBinguinVoidSwordCfg Cfg
        {
            get
            {
                IBinguinVoidSwordCfg p = Props as IBinguinVoidSwordCfg;
                if (p == null)
                {
                    p = new CompProperties_BinguinVoidStrike();
                }
                return p;
            }
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn caster = parent.pawn;
            if (caster == null || caster.Map == null || !target.IsValid)
            {
                return;
            }
            // ★ 双保险：即使玩家用别的途径触发了这个技能，也要尊重 mod 选项
            if (BinguinMod.Settings == null || !BinguinMod.Settings.enableVoidStrike)
            {
                Messages.Message("Binguin_VoidStrike_Disabled".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            Map map = caster.Map;
            IBinguinVoidSwordCfg cfg = Cfg;
            IntVec3 center = target.Cell;
            if (!center.InBounds(map))
            {
                return;
            }

            // ---- ① 收集椭圆范围内的目标（一次扫描，14 次斩击复用）----
            List<Pawn> victims = CollectTargets(caster, map, center, cfg);
            if (victims.Count == 0)
            {
                // 空放也要有效果，否则玩家不知道技能放了
                FleckMaker.ThrowDustPuff(center.ToVector3Shifted(), map, 1.4f);
                MoteMaker.ThrowText(center.ToVector3Shifted(), map,
                    "Binguin_VoidSword_01".Translate(), 1.2f);
                return;
            }

            // ---- ② 单次斩击伤害（用户规格：10 钝击）----
            // ★ 伤害仍按原版近战公式吃「近战伤害乘数」（技能等级/特质/仿生件都算），
            //   基准值来自 XML 的 damagePerStrike。
            float meleeMult = caster.GetStatValue(StatDefOf.MeleeDamageFactor);
            float perHit = cfg.DamagePerStrike * meleeMult;
            DamageDef dmgDef = BinguinVoidSwordUtility.FishDamageDef;
            ThingDef weaponDef = caster.equipment != null && caster.equipment.Primary != null
                ? caster.equipment.Primary.def : null;
            List<ThingDef> fish = BinguinVoidSwordUtility.FishDefs;
            CompBinguinVoidSword swordComp = caster.equipment != null
                ? caster.equipment.Primary.TryGetComp<CompBinguinVoidSword>() : null;

            // ---- ③ 14 次轮流斩击，一口气结算 ----
            int strikes = Mathf.Max(1, cfg.Strikes);
            int fishTotal = fish.Count;
            for (int i = 0; i < strikes; i++)
            {
                // 轮流取鱼：下标存在武器 comp 上 ⇒ 跨施放/跨存档连续
                int fishIndex;
                if (swordComp != null)
                {
                    fishIndex = swordComp.nextFishIndex;
                    swordComp.nextFishIndex = fishTotal > 0
                        ? (swordComp.nextFishIndex + 1) % fishTotal
                        : 0;
                }
                else
                {
                    fishIndex = fishTotal > 0 ? i % fishTotal : 0;
                }
                ThingDef fishDef = (fishTotal > 0 && fishIndex < fishTotal) ? fish[fishIndex] : null;

                for (int v = 0; v < victims.Count; v++)
                {
                    Pawn victim = victims[v];
                    if (victim == null || victim.Dead || !victim.Spawned)
                    {
                        continue;
                    }
                    DamageInfo dinfo = new DamageInfo(
                        dmgDef,
                        perHit,
                        cfg.ArmorPenetration,     // ★ 100% 钝器穿透
                        0f,
                        caster,
                        null,
                        weaponDef,
                        DamageInfo.SourceCategory.ThingOrUnknown,
                        null,
                        false, false,
                        QualityCategory.Normal,
                        false, false);
                    victim.TakeDamage(dinfo);
                }
            }

            // ---- ④ 表现层：一次性的冰屑爆发 + 浮字（不逐条放，免得刷屏）----
            FleckDef iceFleck = DefDatabase<FleckDef>.GetNamedSilentFail("Binguin_IceDust");
            for (int v = 0; v < victims.Count; v++)
            {
                Pawn victim = victims[v];
                if (victim == null || !victim.Spawned)
                {
                    continue;
                }
                if (iceFleck != null)
                {
                    FleckMaker.Static(victim.Position, map, iceFleck, 1.2f);
                }
                else
                {
                    FleckMaker.ThrowDustPuff(victim.Position.ToVector3Shifted(), map, 0.8f);
                }
            }
            FleckMaker.ThrowDustPuff(center.ToVector3Shifted(), map, 1.6f);
            // ★ 参数全部先转成 string 再传给 Translate：本 mod 的翻译串里用的是
            //   {0} 这种**无格式说明符**的占位；如果传 float 进去，
            //   string.Format 会按当前区域性把它变成 "3,5" 之类，
            //   而且传错类型会抛 FormatException。统一传 string 最稳。
            MoteMaker.ThrowText(center.ToVector3Shifted(), map,
                "Binguin_VoidSword_02".Translate(
                    strikes.ToString(),
                    victims.Count.ToString(),
                    fishTotal.ToString()),
                1.4f);

            Log.Message("[冰鹅族] 天顶鱼斩：" + strikes + " 次斩击 × " + victims.Count
                + " 个目标（共 " + fishTotal + " 种鱼轮流），单次 " + perHit.ToString("0.0")
                + " 钝击 / 穿透 " + cfg.ArmorPenetration.ToString("0.00"));
        }

        /// <summary>
        /// 收集椭圆范围内的合法目标。
        /// ★ 椭圆：长轴沿"施法者 → 瞄准点"方向；用旋转到长轴坐标系的
        ///   `(u/a)² + (v/b)² ≤ 1` 判定。
        /// </summary>
        private List<Pawn> CollectTargets(Pawn caster, Map map, IntVec3 center,
            IBinguinVoidSwordCfg cfg)
        {
            List<Pawn> result = new List<Pawn>();
            Faction casterFaction = caster.Faction;

            // 长轴单位向量（施法者 → 瞄准点）；两者同格时退化为 +X
            Vector3 axis = (center - caster.Position).ToVector3();
            if (axis.sqrMagnitude < 0.0001f)
            {
                axis = new Vector3(1f, 0f, 0f);
            }
            axis.Normalize();
            float ax = axis.x;
            float az = axis.z;

            float a = Mathf.Max(0.5f, cfg.OvalRadiusLong);    // 半长轴
            float b = Mathf.Max(0.5f, cfg.OvalRadiusShort);   // 半短轴
            float maxRange = Mathf.Max(a, cfg.MaxStrikeRange);
            int range = Mathf.CeilToInt(maxRange);
            int a2 = Mathf.CeilToInt(a);
            int b2 = Mathf.CeilToInt(b);
            int radius = Mathf.Max(a2, b2) + 1;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    IntVec3 cell = new IntVec3(center.x + dx, 0, center.z + dz);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }
                    // 椭圆判定
                    float rx = dx;
                    float rz = dz;
                    float u = rx * ax + rz * az;                 // 沿长轴
                    float v = -rx * az + rz * ax;                // 垂直方向
                    if ((u * u) / (a * a) + (v * v) / (b * b) > 1f)
                    {
                        continue;
                    }
                    // 距离上限（防止瞄准点被拉到天边时斩到超远目标）
                    if ((cell - caster.Position).LengthHorizontalSquared
                        > cfg.MaxStrikeRange * cfg.MaxStrikeRange)
                    {
                        continue;
                    }
                    // 视线：不隔墙斩人
                    if (!GenSight.LineOfSight(caster.Position, cell, map, true))
                    {
                        continue;
                    }
                    List<Thing> things = map.thingGrid.ThingsListAt(cell);
                    for (int j = 0; j < things.Count; j++)
                    {
                        Pawn p = things[j] as Pawn;
                        if (p == null || p == caster || p.Dead || !p.Spawned)
                        {
                            continue;
                        }
                        // 不打友军 / 盟友（与高级钓竿技能同一套筛选规则）
                        if (p.Faction == casterFaction)
                        {
                            continue;
                        }
                        if (p.Faction != null && casterFaction != null
                            && p.Faction.RelationKindWith(casterFaction) == FactionRelationKind.Ally)
                        {
                            continue;
                        }
                        if (!result.Contains(p))
                        {
                            result.Add(p);
                        }
                    }
                }
            }
            return result;
        }
    }
}
