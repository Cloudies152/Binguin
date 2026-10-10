// ============================================================================
// 冰冠技能「投掷冰块」（2026-09 用户需求）
//
//   · 装备冰冠 → CompBinguinIceCrown（运行时不挂载）→ pawn.abilities.GainAbility
//     → 命令栏出现技能按钮（与高级钓竿那套 Bladelink 式做法同构）。
//   · AbilityDef Binguin_AbilityThrowIce：charges=3（可储存 3 次）、
//     cooldownTicksRange=36000（1 小时）、cooldownPerCharge=true（每次充能各冷却
//     1 小时，原版 VoidTerror 同款组合）。
//   · 施放：以目标格为中心投出 5 块「灵造冰岩」（十字分布：中心 + 四正邻）；
//     若某些格不可用（狭窄区域/有人站着/是墙），依次退到斜角与半径 2 的格位，
//     尽量把 5 块都放下（挤在一起），实在放不下就少放几块。
//     ★ 只检查"能不能放"，【绝不改动地形】（用户要求：不会破坏地形）。
//   · 灵造冰岩 Binguin_SpiritIceRock：440 耐久、不可通行、6 小时后自行消融
//     （CompBinguinSpiritIce 倒计时，随存档保存；6h = 36000 tick）。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.IceCombat
{
    // ==================== 技能效果：投掷 5 块灵造冰岩 ====================
    public class CompProperties_BinguinThrowIce : CompProperties_AbilityEffect
    {
        public int count = 5;
        public string rockDefName = "Binguin_SpiritIceRock";

        public CompProperties_BinguinThrowIce()
        {
            compClass = typeof(CompAbilityEffect_BinguinThrowIce);
        }
    }

    public class CompAbilityEffect_BinguinThrowIce : CompAbilityEffect
    {
        public new CompProperties_BinguinThrowIce Props
        {
            get { return (CompProperties_BinguinThrowIce)props; }
        }

        // ★ 基类 Valid/CanApplyOn 内部会对特定子类型硬 cast（InvalidCastException），
        //   自定义效果必须 override 返回 true（目标合法性由 verbProperties 限制）
        public override bool Valid(LocalTargetInfo target, bool throwMessages)
        {
            return true;
        }

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            try
            {
                Pawn caster = parent.pawn;
                if (caster == null || caster.Map == null)
                {
                    return;
                }
                Map map = caster.Map;
                IntVec3 center = target.Cell;
                ThingDef rockDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.rockDefName);
                if (rockDef == null)
                {
                    BinguinLogUtility.Log("找不到灵造冰岩 def：" + Props.rockDefName, severity: 1, isDebug: false);
                    return;
                }

                List<IntVec3> cells = BuildCellOrder(center);
                int placed = 0;
                for (int i = 0; i < cells.Count && placed < Props.count; i++)
                {
                    if (TryPlaceOne(cells[i], map, rockDef))
                    {
                        placed++;
                    }
                }

                // 特效：中心冰屑 + 尘土
                FleckDef iceFleck = DefDatabase<FleckDef>.GetNamedSilentFail("Binguin_IceDust");
                if (iceFleck != null)
                {
                    FleckMaker.Static(center, map, iceFleck, 1.5f);
                }
                FleckMaker.ThrowDustPuff(center.ToVector3Shifted(), map, 1.2f);
                if (placed < Props.count)
                {
                    Messages.Message("Binguin_AbilityIceThrow_01".Translate() + placed + "Binguin_AbilityIceThrow_02".Translate(),
                        MessageTypeDefOf.NeutralEvent, false);
                }
                BinguinLogUtility.Log("投掷冰块：投出 " + placed + " 块（目标格 " + center + "）");
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("投掷冰块异常：" + e.Message, severity: 1, isDebug: false);
            }
        }

        // 候选格顺序：十字（中心+四正邻）→ 四斜角 → 半径 2 的环
        private static List<IntVec3> BuildCellOrder(IntVec3 c)
        {
            List<IntVec3> list = new List<IntVec3>();
            list.Add(c);
            list.Add(c + IntVec3.North);
            list.Add(c + IntVec3.South);
            list.Add(c + IntVec3.East);
            list.Add(c + IntVec3.West);
            list.Add(c + new IntVec3(1, 0, 1));
            list.Add(c + new IntVec3(1, 0, -1));
            list.Add(c + new IntVec3(-1, 0, 1));
            list.Add(c + new IntVec3(-1, 0, -1));
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (Math.Abs(dx) != 2 && Math.Abs(dz) != 2)
                    {
                        continue; // 只取半径 2 的外圈
                    }
                    list.Add(new IntVec3(c.x + dx, 0, c.z + dz));
                }
            }
            return list;
        }

        // 能放就放：越界/地形不可通行/已有建筑/有人站着 → 跳过（不改地形）
        private static bool TryPlaceOne(IntVec3 c, Map map, ThingDef rockDef)
        {
            if (!c.InBounds(map))
            {
                return false;
            }
            if (!c.Walkable(map))
            {
                return false;
            }
            if (c.GetEdifice(map) != null)
            {
                return false;
            }
            if (c.GetFirstPawn(map) != null)
            {
                return false;
            }
            try
            {
                Thing rock = ThingMaker.MakeThing(rockDef, null);
                if (rock == null)
                {
                    return false;
                }
                GenSpawn.Spawn(rock, c, map, Rot4.North);
                return true;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("冰岩放置失败 " + c + "：" + e.Message, severity: 1, isDebug: false);
                return false;
            }
        }
    }

    // ==================== 灵造冰岩：6 小时后消融 ====================
    public class CompProperties_BinguinSpiritIce : CompProperties
    {
        public int lifetimeTicks = 36000; // 6 小时

        public CompProperties_BinguinSpiritIce()
        {
            compClass = typeof(CompBinguinSpiritIce);
        }
    }

    public class CompBinguinSpiritIce : ThingComp
    {
        private int ticksLeft = -1;

        public CompProperties_BinguinSpiritIce Props
        {
            get { return (CompProperties_BinguinSpiritIce)props; }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (ticksLeft < 0)
            {
                ticksLeft = Props.lifetimeTicks;
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (ticksLeft < 0)
            {
                return;
            }
            ticksLeft -= 250; // Rare tick = 250 tick
            if (ticksLeft <= 0)
            {
                Melt();
            }
        }

        private void Melt()
        {
            try
            {
                if (parent == null || parent.Destroyed)
                {
                    return;
                }
                FleckDef iceFleck = DefDatabase<FleckDef>.GetNamedSilentFail("Binguin_IceDust");
                if (iceFleck != null && parent.Spawned)
                {
                    FleckMaker.Static(parent.Position, parent.Map, iceFleck, 1f);
                }
                parent.Destroy(DestroyMode.Vanish);
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("冰岩消融异常：" + e.Message, severity: 1, isDebug: false);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (ticksLeft < 0)
            {
                return null;
            }
            // 1 小时 = 2500 rare tick
            return "Binguin_AbilityIceThrow_03".Translate() + (ticksLeft / 2500f).ToString("0.0") + "Binguin_AbilityIceThrow_04".Translate();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<int>(ref ticksLeft, "ticksLeft", -1, false);
        }
    }

    // ==================== 冰冠：装备即获得「投掷冰块」 ====================
    public class CompProperties_BinguinIceCrown : CompProperties
    {
        public string abilityDefName = "Binguin_AbilityThrowIce";

        public CompProperties_BinguinIceCrown()
        {
            compClass = typeof(CompBinguinIceCrown);
        }
    }

    public class CompBinguinIceCrown : ThingComp
    {
        public CompProperties_BinguinIceCrown Props
        {
            get { return (CompProperties_BinguinIceCrown)props; }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            SetAbility(pawn, true);
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            SetAbility(pawn, false);
        }

        // 读档兜底：穿上冰冠但技能丢失时补发（GameComponent 每 6000 tick 也会扫）
        public void EnsureAbility()
        {
            Pawn wearer = (parent as Apparel) != null ? ((Apparel)parent).Wearer : null;
            if (wearer != null)
            {
                SetAbility(wearer, true);
            }
        }

        private void SetAbility(Pawn pawn, bool gain)
        {
            try
            {
                if (pawn == null || pawn.abilities == null)
                {
                    return;
                }
                AbilityDef def = DefDatabase<AbilityDef>.GetNamedSilentFail(Props.abilityDefName);
                if (def == null)
                {
                    return;
                }
                if (gain)
                {
                    if (pawn.abilities.GetAbility(def) == null)
                    {
                        pawn.abilities.GainAbility(def);
                    }
                }
                else
                {
                    pawn.abilities.RemoveAbility(def);
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("冰冠技能授予异常：" + e.Message, severity: 1, isDebug: false);
            }
        }
    }
}
