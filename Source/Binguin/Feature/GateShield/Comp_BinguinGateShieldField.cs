// ============================================================================
// 寒门盾外层护盾场（2026-08-21 v3 → v18 事件驱动）
//   根因：CompProperties_ProjectileInterceptor 只对【地图上的生成物】生效
//   （投射物经 ThingRequestGroup.ProjectileInterceptor 查询地图物列表；
//   穿在身上的 apparel 永远查不到 → 前两轮罩子不绘制也不拦截）。
//   方案：穿戴者脚下生成一个隐形实体 Binguin_GateShieldField（地图物，
//   带百夫长同款拦截罩 comps），跟随穿戴者移动。
//   ★ v18（事件驱动，替代轮询）：
//     · CompBinguinGateShieldBearer 挂在寒门盾 apparel 上——原版 comp
//       事件 Notify_Equipped/Notify_Unequipped（CompBiocodable 同款机制，
//       反射确认 apparel 穿戴/脱下会触发）：穿上即时生成场实体、
//       脱下即时销毁；
//     · 场实体本身 CompTick 每 15 tick 校验穿戴者仍穿盾（兜底自毁）+
//       位置同步；
//     · 读档补发 + 意外丢失兜底由 GameComponent 在加载后首个 tick 与
//       每 60000 tick（1 天）执行一次（原 10 秒轮询取消）。
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.GateShield
{
    public class CompProperties_BinguinGateShieldField : CompProperties
    {
        public CompProperties_BinguinGateShieldField()
        {
            compClass = typeof(CompBinguinGateShieldField);
        }
    }

    public class CompBinguinGateShieldField : ThingComp
    {
        // 绑定的穿戴者
        public Pawn wearer;

        // ★ v15 性能：字段节流（每 15 tick 处理一次，避免每 tick 取模判断）
        private int nextProcessTick = -1;

        // 寒门盾 apparel 的 def（懒加载缓存，供自毁检查做引用比较）
        private static ThingDef cachedShieldDef;
        private static bool shieldDefLookedUp;

        private static ThingDef ShieldDef
        {
            get
            {
                if (!shieldDefLookedUp)
                {
                    shieldDefLookedUp = true;
                    cachedShieldDef = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_GateShield");
                }
                return cachedShieldDef;
            }
        }

        // ================= 静态工具（v18：事件与兜底共用） =================

        // ★ 2026-09 性能：def 名是常量，而 FieldDef 在每帧的护盾条路径上被读取
        //   → 懒加载缓存（null 也记住，避免反复查不存在的 def）。
        private static ThingDef cachedFieldDef;
        private static bool fieldDefLookedUp;

        public static ThingDef FieldDef
        {
            get
            {
                if (!fieldDefLookedUp)
                {
                    fieldDefLookedUp = true;
                    cachedFieldDef = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_GateShieldField");
                }
                return cachedFieldDef;
            }
        }

        // 找到绑定某穿戴者的场实体（同地图内）
        public static CompBinguinGateShieldField FindFieldFor(Pawn wearer)
        {
            if (wearer == null || !wearer.Spawned || wearer.Map == null)
            {
                return null;
            }
            ThingDef def = FieldDef;
            if (def == null)
            {
                return null;
            }
            List<Thing> fields = wearer.Map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < fields.Count; i++)
            {
                CompBinguinGateShieldField c = fields[i]
                    .TryGetComp<CompBinguinGateShieldField>();
                if (c != null && c.wearer == wearer)
                {
                    return c;
                }
            }
            return null;
        }

        // 确保穿戴者脚下有场实体（没有则生成）；返回 comp（可能为 null）
        public static CompBinguinGateShieldField EnsureFieldFor(Pawn wearer)
        {
            try
            {
                if (wearer == null || wearer.Destroyed || !wearer.Spawned || wearer.Map == null)
                {
                    return null;
                }
                CompBinguinGateShieldField existing = FindFieldFor(wearer);
                if (existing != null)
                {
                    return existing;
                }
                ThingDef def = FieldDef;
                if (def == null)
                {
                    return null;
                }
                Thing field = ThingMaker.MakeThing(def);
                GenSpawn.Spawn(field, wearer.Position, wearer.Map);
                // ★ v20：thingClass = 原版 MechShield——绑定穿戴者为跟随目标
                //   （SetTarget 后护盾泡泡绘制跟随 target，DrawPos=target 位置）
                Verse.MechShield ms = field as Verse.MechShield;
                if (ms != null)
                {
                    ms.SetTarget(wearer);
                }
                CompBinguinGateShieldField cc = field
                    .TryGetComp<CompBinguinGateShieldField>();
                if (cc != null)
                {
                    cc.wearer = wearer;
                }
                BinguinLogUtility.Log("寒门盾场已生成，跟随 " + wearer.LabelShort);
                return cc;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("寒门盾场生成异常：" + e.Message, severity: 1, isDebug: false);
                return null;
            }
        }

        // 销毁绑定某穿戴者的场实体（脱下时即时调用）
        public static void DeleteFieldFor(Pawn wearer)
        {
            try
            {
                if (wearer == null || wearer.Map == null)
                {
                    return;
                }
                ThingDef def = FieldDef;
                if (def == null)
                {
                    return;
                }
                List<Thing> fields = wearer.Map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < fields.Count; i++)
                {
                    CompBinguinGateShieldField c = fields[i]
                        .TryGetComp<CompBinguinGateShieldField>();
                    if (c != null && c.wearer == wearer)
                    {
                        Thing f = c.parent;
                        if (f != null && !f.Destroyed)
                        {
                            f.Destroy();
                        }
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("寒门盾场销毁异常：" + e.Message, severity: 1, isDebug: false);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look<Pawn>(ref wearer, "wearer", false);
        }

        public override void CompTick()
        {
            base.CompTick();
            try
            {
                Thing p = parent;
                if (p == null || p.Destroyed)
                {
                    return;
                }
                // 每 15 tick 处理一次（60 tick/秒 → 每秒 4 次，足够平滑）
                if (GenTicks.TicksGame < nextProcessTick)
                {
                    return;
                }
                nextProcessTick = GenTicks.TicksGame + 15;
                // 穿戴者失效/脱下/离图 → 自毁
                if (wearer == null || wearer.Destroyed || !wearer.Spawned
                    || wearer.Map == null || wearer.apparel == null)
                {
                    if (!p.Destroyed)
                    {
                        p.Destroy();
                    }
                    return;
                }
                // ★ 2026-09 性能：改比缓存的 ThingDef 引用（原为每 15 tick 逐件
                //   字符串比较 defName）
                ThingDef shieldDef = ShieldDef;
                bool stillWearing = false;
                List<Apparel> worn = wearer.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                {
                    if (worn[i] != null && worn[i].def == shieldDef)
                    {
                        stillWearing = true;
                        break;
                    }
                }
                if (!stillWearing)
                {
                    if (!p.Destroyed)
                    {
                        p.Destroy();
                    }
                    return;
                }
                // ★ v21：thingClass = 原版 MechShield（SetTarget 原生跟随，
                //   DrawPos=target.DrawPos）→ 位置同步交给原生类；
                //   仅当实体不是 MechShield（如旧档 ThingWithComps 实例）
                //   时才自行同步位置。
                if (!(p is Verse.MechShield)
                    && p.Spawned && p.Map == wearer.Map && p.Position != wearer.Position)
                {
                    IntVec3 dest = wearer.Position;
                    p.DeSpawn();
                    GenSpawn.Spawn(p, dest, wearer.Map);
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("寒门盾场跟随异常：" + e.Message, severity: 1, isDebug: false);
            }
        }
    }

    // ============================================================================
    // 寒门盾 apparel 承载 comp（v18 事件驱动）
    //   挂在 Binguin_GateShield 上（BinguinDefPatches 静态构造挂载）。
    //   · Notify_Equipped（穿上）→ 立即在脚下生成护盾场实体；
    //   · Notify_Unequipped（脱下）→ 立即销毁护盾场实体；
    //   · Notify_WearerDied（穿戴者死亡/离图）→ 立即销毁。
    //   （1.6 会在穿戴/脱下 apparel 时调用这些 comp 事件——与
    //     CompBiocodable 生物编码同机制，反射确认。）
    // ============================================================================
    public class CompProperties_BinguinGateShieldBearer : CompProperties
    {
        public CompProperties_BinguinGateShieldBearer()
        {
            compClass = typeof(CompBinguinGateShieldBearer);
        }
    }

    public class CompBinguinGateShieldBearer : ThingComp
    {
        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            // 场是新生成的 → 清掉上一轮的缓存（下次取 Gizmo 时重建）
            InvalidateFieldCache();
            CompBinguinGateShieldField.EnsureFieldFor(pawn);
        }

        private void InvalidateFieldCache()
        {
            cachedField = null;
            cachedIc = null;
            cachedGizmo = null;
        }

        // ★ v22：穿戴者被选中时，命令栏显示外层护盾剩余能量条
        //（拦截罩盾值在护盾场实体的 CompProjectileInterceptor 上，读它）
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            // 迭代器内不能 try/catch（CS1626），创建逻辑提到普通方法
            Gizmo g = TryCreateOuterShieldGizmo();
            if (g != null)
            {
                yield return g;
            }
        }

        // ★ 2026-09 性能：命令栏每帧重建本 gizmo，而原实现每帧都要
        //   ① 查一次 FieldDef ② 全图扫一遍护盾场 ③ 对每个场做 TryGetComp
        //   ④ new 一个 Gizmo。现在缓存"我的场 comp + 它的拦截器 + Gizmo"，
        //   只在穿上/脱下时失效；场实体本身不存档，读档后缓存为空 → 自动回落到扫描。
        private CompBinguinGateShieldField cachedField;
        private CompProjectileInterceptor cachedIc;
        private Gizmo_BinguinOuterShield cachedGizmo;

        private Gizmo TryCreateOuterShieldGizmo()
        {
            try
            {
                Pawn wearer = (parent as Apparel) != null ? ((Apparel)parent).Wearer : null;
                if (wearer == null || wearer.Destroyed || !wearer.Spawned)
                {
                    return null;
                }
                CompBinguinGateShieldField field = cachedField;
                if (field == null || field.parent == null || field.parent.Destroyed
                    || field.wearer != wearer)
                {
                    field = CompBinguinGateShieldField.FindFieldFor(wearer);
                    cachedField = field;
                    cachedIc = null;
                    cachedGizmo = null;
                }
                if (field == null || field.parent == null)
                {
                    return null;
                }
                CompProjectileInterceptor ic = cachedIc;
                if (ic == null || ic.parent != field.parent)
                {
                    ic = field.parent.TryGetComp<CompProjectileInterceptor>();
                    cachedIc = ic;
                    cachedGizmo = null;
                }
                if (ic == null)
                {
                    return null;
                }
                if (cachedGizmo == null)
                {
                    cachedGizmo = new Gizmo_BinguinOuterShield(ic);
                }
                return cachedGizmo;
            }
            catch (Exception ex)
            {
                BinguinLogUtility.Log("外层护盾能量条异常：" + ex.Message, severity: 1, isDebug: false);
                return null;
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            InvalidateFieldCache();
            CompBinguinGateShieldField.DeleteFieldFor(pawn);
        }

        public override void Notify_WearerDied()
        {
            base.Notify_WearerDied();
            Pawn wearer = (parent as Apparel) != null ? ((Apparel)parent).Wearer : null;
            if (wearer == null && parent != null && parent.ParentHolder is Pawn_ApparelTracker)
            {
                // 兜底：从 holder 拿穿戴者
                Pawn_ApparelTracker tr = (Pawn_ApparelTracker)parent.ParentHolder;
                if (tr != null && tr.pawn != null)
                {
                    wearer = tr.pawn;
                }
            }
            CompBinguinGateShieldField.DeleteFieldFor(wearer);
        }
    }

    // ============================================================================
    // 外层护盾能量条（v22 用户需求：选中穿戴者可见外层护盾剩余能量）
    //   ★ 2026-09：显示条照抄原版 Gizmo_EnergyShieldStatus（个人护盾/
    //     百夫长同款）——布局/字体/贴图完全一致（贴图取原版私有静态纹理）。
    //   数据源 = 护盾场实体上的 CompProjectileInterceptor（currentHitPoints/
    //   HitPointsMax，均为 public）。
    // ============================================================================
    // ★ 2026-09：本类有 static 资源字段（Texture2D / Graphic[]），RimWorld 启动时会警告
    //   「probably needs a StaticConstructorOnStartup attribute ... must be loaded in the main thread」。
    //   加上该特性 → 静态构造在主线程启动时执行，警告消除、资源加载时机也正确。
    [StaticConstructorOnStartup]
    public class Gizmo_BinguinOuterShield : Gizmo
    {
        private readonly CompProjectileInterceptor interceptor;

        // ★ 2026-09（用户：旧自绘条太丑）：显示条照抄原版
        //   Gizmo_EnergyShieldStatus（个人护盾/百夫长同款样式）——
        //   布局、字体、贴图全部一致；填充贴图直接复用原版私有静态纹理
        //   （反射取一次缓存，GUI 线程加载）。
        private static Texture2D vanillaFullBar;
        private static Texture2D vanillaEmptyBar;
        private static bool texturesLoaded;

        private static int lastGizmoErrorLogTick = -100000;

        public Gizmo_BinguinOuterShield(CompProjectileInterceptor ic)
        {
            interceptor = ic;
        }

        private static void EnsureVanillaTextures()
        {
            if (texturesLoaded)
            {
                return;
            }
            texturesLoaded = true;
            try
            {
                System.Reflection.FieldInfo ff = HarmonyLib.AccessTools.Field(
                    typeof(Gizmo_EnergyShieldStatus), "FullShieldBarTex");
                System.Reflection.FieldInfo fe = HarmonyLib.AccessTools.Field(
                    typeof(Gizmo_EnergyShieldStatus), "EmptyShieldBarTex");
                if (ff != null)
                {
                    vanillaFullBar = ff.GetValue(null) as Texture2D;
                }
                if (fe != null)
                {
                    vanillaEmptyBar = fe.GetValue(null) as Texture2D;
                }
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("读取原版护盾条贴图失败：" + e.Message, severity: 1, isDebug: false);
            }
        }

        public override float GetWidth(float maxWidth)
        {
            return 136f; // 原版 Gizmo_EnergyShieldStatus 同宽
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            try
            {
                EnsureVanillaTextures();

                // —— 原版 Gizmo_EnergyShieldStatus 逐行复刻 ——
                Rect bgRect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 73f);
                Widgets.DrawWindowBackground(bgRect);
                Rect inner = bgRect.ContractedBy(6f);

                // 上半：标题（Tiny 字）
                Rect labelRect = inner;
                labelRect.height = inner.height / 2f;
                Text.Font = GameFont.Tiny;
                Widgets.Label(labelRect, "Binguin_CompGateShieldField_01".Translate());

                // 下半：原版护盾条（同款填充贴图）
                Rect barRect = inner;
                barRect.y = inner.y + inner.height / 2f;
                barRect.height = inner.height / 2f;
                float cur = interceptor != null ? interceptor.currentHitPoints : 0;
                float max = interceptor != null ? interceptor.HitPointsMax : 0;
                float pct = max > 0f ? Mathf.Clamp01(cur / max) : 0f;
                Widgets.FillableBar(barRect, pct,
                    vanillaFullBar != null ? vanillaFullBar : BaseContent.BlackTex,
                    vanillaEmptyBar != null ? vanillaEmptyBar : BaseContent.BlackTex,
                    false);

                // 数值（小字，叠条中央）——★ 2026-09 用户要求：显示【实际护盾点数】
                //   （如 300 / 300）；原版显示百分比是因为它的护盾上限恒为 1.0
                // ★ 2026-09 性能：本方法每帧绘制，原来把 (int)cur/(int)max 各转换
                //   两次、并且【无条件】拼一段长 tooltip 字符串（鼠标不悬停时是
                //   纯浪费）。现在只转一次，且只在鼠标位于本 gizmo 内时才拼字符串
                //   （TipRegion 本身也只在悬停时显示，行为不变）。
                int curInt = (int)cur;
                int maxInt = (int)max;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(barRect, curInt.ToString() + " / " + maxInt.ToString());
                Text.Anchor = TextAnchor.UpperLeft;

                if (Mouse.IsOver(inner))
                {
                    TooltipHandler.TipRegion(inner,
                        "Binguin_CompGateShieldField_02".Translate() + curInt.ToString()
                        + " / " + maxInt.ToString()
                        + "Binguin_CompGateShieldField_03".Translate());
                }
            }
            catch (Exception e)
            {
                // ★ 2026-09：本方法每帧绘制——异常时若不节流，日志会被瞬间刷爆
                if (GenTicks.TicksGame - lastGizmoErrorLogTick >= 600)
                {
                    lastGizmoErrorLogTick = GenTicks.TicksGame;
                    BinguinLogUtility.Log("外层护盾能量条绘制异常：" + e.Message, severity: 1, isDebug: false);
                }
            }
            return new GizmoResult(GizmoState.Clear, null);
        }
    }
}
