// ============================================================================
// 威灵装甲 —— 内置微型迫击炮（2026-08-20 用户需求）
//   弹舱：最多 3 发原版迫击炮弹（任意弹种混合装填，Shell_HighExplosive/
//     Incendiary/EMP/AntigrainWarhead）
//   燃料舱：60 化合燃料，每次发射消耗 10（由库存搬运补充）
//   ★ v8（2026-08-21）：放弃原版 ApparelReloadable + verb-owner 方案
//     —— v5/v6/v7 连续三轮用户实测无法发射；改为【滑板同款自绘命令】。
//   ★ v9（2026-08-21 用户实测 v8 可发射后要求）：
//     a) 射程 60 → 50 格；
//     b) 1 秒瞄准时间（像原版迫击炮）：选点后小人转向目标站定 1s，
//        GameComponent 到点执行真正抛射（瞄准中重新选点会覆盖落点）；
//     c) 自动装填：CompGetWornGizmosExtra 增加「自动装填」对话框
//        （勾选炮弹种类 + 自动装填炮弹开关 + 自动补充燃料开关），
//        每 4 秒轮询：弹舱空且库存有勾选弹种 → 自动派殖民者搬运装填；
//        燃料不足 → 自动派殖民者搬化合燃料。搬运用排队 job
//        （requestQueueing，不打断当前工作）。
//   ★ 命令按钮：装填炮弹 / 补充燃料 / 自动装填 / 发射迫击炮
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class CompProperties_BinguinWillingCannon : CompProperties
    {
        public float fuelMax = 60f;
        public float fuelPerShot = 10f;
        public int shellMax = 3;
        public float range = 50f;   // v9：60 → 50
        public float impactSpread = 2f;  // v12：落点 0-2 格随机偏移（v11 先做 0-3，用户嫌大改小）

        public CompProperties_BinguinWillingCannon()
        {
            compClass = typeof(CompBinguinWillingCannon);
        }
    }

    // ★ 2026-09：本类有 static 资源字段（Texture2D / Graphic[]），RimWorld 启动时会警告
    //   「probably needs a StaticConstructorOnStartup attribute ... must be loaded in the main thread」。
    //   加上该特性 → 静态构造在主线程启动时执行，警告消除、资源加载时机也正确。
    [StaticConstructorOnStartup]
    public class CompBinguinWillingCannon : ThingComp
    {
        // v8：燃料自管（不再读原版 CompApparelReloadable）
        public float fuelNow = 60f;

        public List<string> shells = new List<string>(); // 弹种 defName，最多 shellMax

        // ---- v9 自动装填设置（「自动装填」对话框勾选）----
        public bool autoReload;            // 自动装填炮弹开关
        public string autoShellDefName = ""; // 勾选的炮弹种类 defName
        public bool autoFuel = true;       // 自动补充燃料开关（默认开）

        // 炮击冷却（2026-08-20 用户定稿）：30 秒
        private const int FireCooldownTicks = 1800;   // 30s
        private int nextFireTick = -1;

        // ★ v15 性能：命令图标静态缓存（gizmo 每帧重建，ContentFinder 每帧
        //   查纹理有字典开销，改为只查一次）
        private static Texture2D iconShell;
        private static Texture2D ShellIcon
        {
            get
            {
                if (iconShell == null)
                {
                    iconShell = ContentFinder<Texture2D>.Get(
                        "Things/Item/Resource/Shell/Shell_HighExplosive", false);
                }
                return iconShell;
            }
        }

        // ====================================================================
        // ★★★ 2026-09 用户定稿：发射命令的图标 = 【下个准备射出的那发炮弹】的图标。
        //
        //   弹舱是个【栈】：装填走 TryLoadOneShell → shells.Add(defName)（加到末尾），
        //   发射走 shells[shells.Count - 1] 再 RemoveAt(Count - 1)（也从末尾拿）。
        //   ⇒ 下一发 = shells[shells.Count - 1]，即最后一个装进去的。
        //   空仓时退回原来的高爆弹图标。
        //
        //   ★ 性能：命令栏每帧重建，所以这里【按弹种 defName 缓存 Texture2D】
        //     （含"查不到"的 null 也缓存，避免每帧查 DefDatabase）。
        // ====================================================================
        private static readonly Dictionary<string, Texture2D> shellIconCache =
            new Dictionary<string, Texture2D>();

        /// <summary>下一发要射出的炮弹的图标；空仓/查不到时退回默认高爆弹图标。</summary>
        private Texture2D NextShellIcon()
        {
            try
            {
                if (shells == null || shells.Count == 0) return ShellIcon;
                string defName = shells[shells.Count - 1];
                if (defName == null || defName.Length == 0) return ShellIcon;

                Texture2D cached;
                if (shellIconCache.TryGetValue(defName, out cached))
                {
                    return cached != null ? cached : ShellIcon;
                }

                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                Texture2D tex = def != null ? def.uiIcon : null;
                shellIconCache[defName] = tex;
                return tex != null ? tex : ShellIcon;
            }
            catch (Exception)
            {
                return ShellIcon;
            }
        }

        // ---- v10 站定瞄准视觉（JobDriver 执行瞄准期间由 GameComponent 每帧绘制）----
        public static Pawn aimPawn;
        public static IntVec3 aimDest = IntVec3.Invalid;
        public static int aimStartTick = -1;

        public static void StartAimVisual(Pawn pawn, IntVec3 dest)
        {
            aimPawn = pawn;
            aimDest = dest;
            aimStartTick = GenTicks.TicksGame;
        }

        private static int lastAimErrorLogTick = -100000;

        public static void ClearAimVisual()
        {
            aimPawn = null;
            aimDest = IntVec3.Invalid;
            aimStartTick = -1;
        }

        // 每帧绘制：瞄准扇形（随进度张开）+ 落点标记（GameComponentUpdate 调用）
        public static void DrawAimVisualTick()
        {
            try
            {
                if (aimStartTick < 0 || aimPawn == null || aimPawn.Destroyed || !aimPawn.Spawned)
                {
                    return;
                }
                // 若 job 已被打断（不再是炮击 job），清掉视觉
                // ★ 2026-09 性能：改比 JobDef 引用（缓存）而不是每帧比较字符串 defName
                JobDef fireDef = FireJobDef;
                if (fireDef == null || aimPawn.jobs == null || aimPawn.jobs.curJob == null
                    || aimPawn.jobs.curJob.def != fireDef)
                {
                    ClearAimVisual();
                    return;
                }
                if (!aimDest.IsValid)
                {
                    return;
                }
                float t = (GenTicks.TicksGame - aimStartTick) / 60f;
                if (t > 1f)
                {
                    t = 1f;
                }
                // ★ 2026-09 用户修正：扇形方向反了——正确的是【随瞄准进度收拢】
                //   （从 ~150° 收小到 8°：像瞄准镜一样逐渐聚拢到目标），
                //   而不是原来的由小张大。
                int deg = Mathf.RoundToInt(Mathf.Lerp(150f, 8f, t));
                GenDraw.DrawAimPie(aimPawn, new LocalTargetInfo(aimDest), deg, 0.6f);
                // 落点圈：橙色随进度变白
                Color c = Color.Lerp(new Color(1f, 0.55f, 0.2f), Color.white, t);
                GenDraw.DrawRadiusRing(aimDest, 1.5f, c);
            }
            catch (Exception e)
            {
                // ★ 2026-09：本方法是每帧调用（60 次/秒）——异常时若不节流，
                //   日志会被瞬间刷爆。每 600 tick 最多记一条。
                if (GenTicks.TicksGame - lastAimErrorLogTick >= 600)
                {
                    lastAimErrorLogTick = GenTicks.TicksGame;
                    Log.Warning("[冰鹅族] 威灵瞄准视觉异常：" + e.Message);
                }
                ClearAimVisual();
            }
        }

        public CompProperties_BinguinWillingCannon Props
        {
            get { return (CompProperties_BinguinWillingCannon)props; }
        }

        public Pawn Wearer
        {
            get { return (parent as Apparel) != null ? ((Apparel)parent).Wearer : null; }
        }

        public float FuelNow { get { return fuelNow; } }
        public float FuelMaxNow { get { return Props.fuelMax; } }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<float>(ref fuelNow, "willingFuel", 60f, false);
            Scribe_Collections.Look<string>(ref shells, "willingShells", LookMode.Value);
            if (shells == null)
            {
                shells = new List<string>();
            }
            Scribe_Values.Look<int>(ref nextFireTick, "willingNextFireTick", -1, false);
            Scribe_Values.Look<bool>(ref autoReload, "willingAutoReload", false, false);
            Scribe_Values.Look<string>(ref autoShellDefName, "willingAutoShell", "", false);
            Scribe_Values.Look<bool>(ref autoFuel, "willingAutoFuel", true, false);
        }

        // ---- ★ v19：穿上/脱下注册（替代全地图衣物扫描）----
        // 1.6 在穿戴/脱下 apparel 时会调用 comp 事件（同 CompBiocodable），
        // 这里把"当前穿在身上的威灵"登记到 GameComponent 的注册表；
        // 自动装填轮询只遍历注册表（通常 0~N 个），不再每 10 秒全图扫衣物。
        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            try
            {
                GameComponent_BinguinDiplomacy gc = Current.Game != null
                    ? Current.Game.GetComponent<GameComponent_BinguinDiplomacy>() : null;
                if (gc != null)
                {
                    gc.RegisterWillingComp(this);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[冰鹅族] 威灵注册异常：" + ex.Message);
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            try
            {
                GameComponent_BinguinDiplomacy gc = Current.Game != null
                    ? Current.Game.GetComponent<GameComponent_BinguinDiplomacy>() : null;
                if (gc != null)
                {
                    gc.UnregisterWillingComp(this);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[冰鹅族] 威灵注销异常：" + ex.Message);
            }
        }

        public override void Notify_WearerDied()
        {
            base.Notify_WearerDied();
            try
            {
                GameComponent_BinguinDiplomacy gc = Current.Game != null
                    ? Current.Game.GetComponent<GameComponent_BinguinDiplomacy>() : null;
                if (gc != null)
                {
                    gc.UnregisterWillingComp(this);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[冰鹅族] 威灵注销异常：" + ex.Message);
            }
        }

        // ★ 2026-09 性能：下面这些是"每帧都会被调用"的路径共用的静态/实例缓存。
        //   已删除的 NextShellLabel（无引用死代码，且每次调用查 DefDatabase）
        //   与本缓存同处，故在此说明。
        //
        //   JobDef 缓存：装填/开火 job def 名是常量，原实现每次派发（含每帧
        //   绘制的瞄准视觉）都查一次 DefDatabase；DefDatabase 是字典查找，
        //   但仍是无谓开销。null 时不缓存（保持"def 未加载时重试"的语义）。
        private static JobDef cachedLoadJobDef;
        private static JobDef cachedFireJobDef;

        private static JobDef LoadJobDef
        {
            get
            {
                if (cachedLoadJobDef == null)
                {
                    cachedLoadJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_WillingLoad");
                }
                return cachedLoadJobDef;
            }
        }

        private static JobDef FireJobDef
        {
            get
            {
                if (cachedFireJobDef == null)
                {
                    cachedFireJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_WillingFire");
                }
                return cachedFireJobDef;
            }
        }

        // 自动装填弹种 def 缓存（autoShellDefName 不变则不重查）
        private string cachedAutoShellName;
        private ThingDef cachedAutoShellDef;

        private ThingDef AutoShellDef
        {
            get
            {
                if (autoShellDefName == null || autoShellDefName.Length == 0)
                {
                    cachedAutoShellName = autoShellDefName;
                    cachedAutoShellDef = null;
                    return null;
                }
                if (cachedAutoShellDef == null || cachedAutoShellName != autoShellDefName)
                {
                    cachedAutoShellName = autoShellDefName;
                    cachedAutoShellDef = DefDatabase<ThingDef>.GetNamedSilentFail(autoShellDefName);
                }
                return cachedAutoShellDef;
            }
        }

        // ★ 2026-09 性能：检视面板每帧调用本方法（选中时 60 次/秒）→ 缓存结果，
        //   仅当弹舱数 / 燃料值 / 自动设置发生变化时重建，文案逐字符不变。
        private string cachedInspect;
        private int inspectShellCount = -1;
        private float inspectFuelKey = float.MinValue;
        private bool inspectAutoReload;
        private bool inspectAutoFuel;
        private string inspectAutoShellName;

        public override string CompInspectStringExtra()
        {
            Pawn wearer = Wearer;
            if (wearer == null)
            {
                return null;
            }
            if (cachedInspect != null && inspectShellCount == shells.Count
                && inspectFuelKey == fuelNow && inspectAutoReload == autoReload
                && inspectAutoFuel == autoFuel && inspectAutoShellName == autoShellDefName)
            {
                return cachedInspect;
            }
            inspectShellCount = shells.Count;
            inspectFuelKey = fuelNow;
            inspectAutoReload = autoReload;
            inspectAutoFuel = autoFuel;
            inspectAutoShellName = autoShellDefName;

            string auto = "";
            if (autoReload && autoShellDefName.Length > 0)
            {
                ThingDef d = AutoShellDef;
                auto = "Binguin_CompWillingCannon_01".Translate() + (d != null ? d.label : autoShellDefName);
            }
            if (autoFuel)
            {
                auto += "Binguin_CompWillingCannon_02".Translate();
            }
            cachedInspect = "Binguin_CompWillingCannon_03".Translate() + shells.Count + " / " + Props.shellMax + "Binguin_CompWillingCannon_04".Translate()
                + FuelNow.ToString("0") + " / " + FuelMaxNow.ToString("0")
                + "Binguin_CompWillingCannon_05".Translate() + Props.fuelPerShot.ToString("0") + "）" + auto;
            return cachedInspect;
        }

        // ---- v13 自绘命令：装填炮弹（含自动装填设置）/ 发射迫击炮 ----
        // ★ v13（2026-08-21 用户要求）：删除「补充燃料」「自动装填」两个
        //   命令按钮——手动点按钮补充已取消；自动装填/自动补燃料每 4 秒
        //   轮询保留，且【只派穿甲者本人】搬运（他人不得替他装填）。
        //   弹种筛选（勾选自动装填种类）与开关并入「装填炮弹」对话框。
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            Pawn wearer = Wearer;
            if (wearer == null || wearer.Destroyed || !wearer.Spawned)
            {
                yield break;
            }

            // 1) 装填炮弹 + 自动装填设置（对话框，穿甲者本人搬运装填）
            // ★ 2026-09 性能：命令栏每帧重建（选中穿戴者时 60 次/秒）——
            //   desc 字符串与 action 委托改为缓存：desc 只在弹舱数/燃料整数位
            //   变化时重建（文案逐字符不变），委托只创建一次。
            if (loadGizmoAction == null)
            {
                loadGizmoAction = delegate { Find.WindowStack.Add(new Dialog_BinguinLoadShell(this)); };
            }
            yield return new Command_Action
            {
                defaultLabel = "Binguin_CompWillingCannon_06".Translate(),
                defaultDesc = LoadGizmoDesc(),
                icon = ShellIcon,
                action = loadGizmoAction
            };

            // 2) 发射迫击炮：滑板同款自绘 Targeter（v8）+ 1s 瞄准（v9）
            Gizmo fireCmd = TryCreateFireCommand();
            if (fireCmd != null)
            {
                yield return fireCmd;
            }
        }

        private System.Action loadGizmoAction;
        private string cachedLoadGizmoDesc;
        private int loadDescKey = int.MinValue;

        // 装填命令说明文案（缓存版；键 = 弹舱数 + 燃料整数值）
        private string LoadGizmoDesc()
        {
            int key = shells.Count * 100000 + (int)FuelNow;
            if (cachedLoadGizmoDesc == null || key != loadDescKey)
            {
                loadDescKey = key;
                cachedLoadGizmoDesc = "Binguin_CompWillingCannon_07".Translate()
                    + shells.Count + "/" + Props.shellMax + "Binguin_CompWillingCannon_08".Translate()
                    + FuelNow.ToString("0") + "/" + FuelMaxNow.ToString("0")
                    + "Binguin_CompWillingCannon_09".Translate();
            }
            return cachedLoadGizmoDesc;
        }

        private Gizmo TryCreateFireCommand()
        {
            try
            {
                Pawn wearer = Wearer;
                if (wearer == null || wearer.Destroyed || !wearer.Spawned || wearer.Map == null)
                {
                    return null;
                }
                Command_BinguinWillingFire cmd = new Command_BinguinWillingFire
                {
                    defaultLabel = "Binguin_CompWillingCannon_10".Translate(),
                    defaultDesc = FireGizmoDesc(),
                    // ★ 2026-09 用户定稿：图标跟着【下一发炮弹】走（见 NextShellIcon）
                    icon = NextShellIcon(),
                    caster = wearer,
                    range = Props.range,
                    cannon = this
                };
                // 不可发射状态直接置灰（理由文案同样走缓存）
                string disabledReason = DisabledReasonTextCached();
                if (disabledReason != null)
                {
                    cmd.Disable(disabledReason);
                }
                return cmd;
            }
            catch (Exception e)
            {
                // ★ 2026-09：命令栏每帧重建 → 异常日志同样需要节流
                if (GenTicks.TicksGame - lastAimErrorLogTick >= 600)
                {
                    lastAimErrorLogTick = GenTicks.TicksGame;
                    Log.Error("[冰鹅族] 威灵炮发射命令创建异常（已拦截）: " + e);
                }
                return null;
            }
        }

        // ★ 2026-09 性能：命令栏每帧重建发射按钮 → desc 与"不可发射理由"两段
        //   字符串全部缓存（键 = 弹舱数 + 燃料整数 + 冷却剩余秒数），
        //   文案逐字符不变；冷却倒数只在秒数变化时才重建。
        private string cachedFireDesc;
        private int fireDescKey = int.MinValue;
        private string cachedDisabledReason;
        private int disabledReasonKey = int.MinValue;

        private int FireGizmoKey()
        {
            int cdLeft = GenTicks.TicksGame < nextFireTick
                ? (int)Mathf.Ceil((nextFireTick - GenTicks.TicksGame) / 60f) : 0;
            if (cdLeft > 999)
            {
                cdLeft = 999;
            }
            return shells.Count * 1000000 + (int)FuelNow * 1000 + cdLeft;
        }

        private string FireGizmoDesc()
        {
            int key = FireGizmoKey();
            if (cachedFireDesc == null || key != fireDescKey)
            {
                fireDescKey = key;
                cachedFireDesc = "Binguin_CompWillingCannon_11".Translate() + Props.range.ToString("0")
                    + "Binguin_CompWillingCannon_12".Translate() + Props.impactSpread.ToString("0")
                    + "Binguin_CompWillingCannon_13".Translate() + shells.Count + "/" + Props.shellMax
                    + "Binguin_CompWillingCannon_14".Translate() + FuelNow.ToString("0") + "/" + FuelMaxNow.ToString("0")
                    + "Binguin_CompWillingCannon_15".Translate();
            }
            return cachedFireDesc;
        }

        // 注意：DisabledReasonText() 正常时返回 null（= 可以发射），
        // 所以"是否已算过"必须靠 key（FireGizmoKey 恒 ≥ 0，int.MinValue 不会误命中）。
        private string DisabledReasonTextCached()
        {
            int key = FireGizmoKey();
            if (key != disabledReasonKey)
            {
                disabledReasonKey = key;
                cachedDisabledReason = DisabledReasonText();
            }
            return cachedDisabledReason;
        }

        private string DisabledReasonText()
        {
            if (GenTicks.TicksGame < nextFireTick)
            {
                int left = (int)Mathf.Ceil((nextFireTick - GenTicks.TicksGame) / 60f);
                return "Binguin_CompWillingCannon_16".Translate() + left + "Binguin_CompWillingCannon_17".Translate();
            }
            if (shells.Count <= 0)
            {
                return "Binguin_CompWillingCannon_18".Translate();
            }
            if (fuelNow < Props.fuelPerShot)
            {
                return "Binguin_CompWillingCannon_19".Translate();
            }
            return null;
        }

        // ================= 自动装填轮询（v17：性能优化） =================
        // GameComponent 每 600 tick 调一次；comp 内不再额外节流（外部已
        // 10 秒一次），但保留"需求早退"——弹舱满且燃料满时几乎零成本
        // 返回（不扫库存/jobs）。
        private int nextAutoTick = -1;

        public void AutoMaintainTick()
        {
            try
            {
                if (GenTicks.TicksGame < nextAutoTick)
                {
                    return;
                }
                nextAutoTick = GenTicks.TicksGame + 600;
                Pawn wearer = Wearer;
                if (wearer == null || wearer.Destroyed || !wearer.Spawned || wearer.Map == null
                    || wearer.Downed || wearer.InMentalState || wearer.IsPrisonerOfColony)
                {
                    return;
                }
                // ★ 需求早退：没有任何需求就不做任何查询（最大头优化——
                //   绝大多数轮次弹舱/燃料都是满的，直接 0 成本返回）
                bool needShell = autoReload && autoShellDefName.Length > 0
                    && shells.Count < Props.shellMax;
                bool needFuel = autoFuel && fuelNow < FuelMaxNow - 0.5f;
                if (!needShell && !needFuel)
                {
                    return;
                }
                // 本人已有装填 job（当前或排队）→ 跳过本轮（只查本人，v13 只派本人）
                if (HasPendingLoadJobFor(wearer))
                {
                    return;
                }
                // 1) 自动炮弹：弹舱空且勾选了种类且有库存
                if (needShell)
                {
                    Thing shell = FindStockOf(wearer.Map, autoShellDefName);
                    if (shell != null)
                    {
                        QueueLoadJobInternal(wearer, shell, 1, true);
                    }
                }
                // 2) 自动燃料：燃料不足且库存有化合燃料（v13：删手动按钮，仅自动）
                if (needFuel)
                {
                    AutoFuelTick();
                }
            }
            catch (Exception e)
            {
                Log.Warning("[冰鹅族] 威灵自动装填异常（已拦截）: " + e.Message);
            }
        }

        // 穿甲者本人是否已有装填 job（当前或排队）→ 跳过本轮防重复派活。
        // ★ v15 性能：v13 起只派穿甲者本人搬运，故只需查本人 jobs，
        //   不再全图遍历所有殖民者的 jobs（旧实现 O(全殖民者×jobs)）。
        private static bool HasPendingLoadJobFor(Pawn wearer)
        {
            if (wearer == null || wearer.jobs == null)
            {
                return false;
            }
            try
            {
                JobDef loadDef = LoadJobDef;
                if (loadDef == null)
                {
                    return false;
                }
                // ★ 2026-09 性能：原实现用 jobs.AllJobs()——那是编译器生成的
                //   迭代器（每次调用都分配状态机对象）。1.6 的 AllJobs() 语义就是
                //   "当前 job + 队列里的 job"，这里直接按同样顺序遍历，零分配。
                if (IsLoadJobFor(wearer.jobs.curJob, wearer, loadDef))
                {
                    return true;
                }
                JobQueue queue = wearer.jobs.jobQueue;
                if (queue != null)
                {
                    for (int i = 0; i < queue.Count; i++)
                    {
                        if (IsLoadJobFor(queue[i].job, wearer, loadDef))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static bool IsLoadJobFor(Job j, Pawn wearer, JobDef loadDef)
        {
            if (j == null || j.def != loadDef)
            {
                return false;
            }
            Pawn t = j.GetTarget(TargetIndex.B).Thing as Pawn;
            return t == wearer;
        }

        // ================= 装填 job 派发（v13：只派穿甲者本人） =================

        // 手动装填炮弹（对话框按钮）：穿甲者本人执行搬运（玩家直接命令）
        public bool QueueLoadJob(string defName)
        {
            Pawn wearer = Wearer;
            if (wearer == null || wearer.Map == null)
            {
                return false;
            }
            if (shells.Count >= Props.shellMax)
            {
                Messages.Message("Binguin_CompWillingCannon_20".Translate() + Props.shellMax + "Binguin_CompWillingCannon_21".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            Thing item = FindStockOf(wearer.Map, defName);
            if (item == null)
            {
                Messages.Message("Binguin_CompWillingCannon_22".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return QueueLoadJobInternal(wearer, item, 1, false);
        }

        // 实际派发装炮弹 job（穿甲者本人执行；queueIfBusy：自动模式排队不打断）
        private static bool QueueLoadJobInternal(Pawn wearer, Thing item, int count, bool queueIfBusy)
        {
            JobDef jd = LoadJobDef;
            if (jd == null || item == null || item.Destroyed)
            {
                return false;
            }
            Pawn carrier = SelfCarrier(wearer, queueIfBusy);
            if (carrier == null)
            {
                return false;
            }
            Job job = new Job(jd, item, wearer);
            job.count = count;
            return carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc, queueIfBusy);
        }

        // 自动燃料检查（每 4 秒轮询）：燃料不足且库存有化合燃料
        private bool AutoFuelTick()
        {
            Pawn wearer = Wearer;
            if (wearer == null || wearer.Map == null)
            {
                return false;
            }
            if (fuelNow >= FuelMaxNow - 0.5f)
            {
                return false;
            }
            int need = Mathf.CeilToInt(FuelMaxNow - fuelNow);
            Thing chemfuel = FindStockOf(wearer.Map, "Chemfuel");
            if (chemfuel == null)
            {
                return false;
            }
            return QueueFuelJobInternal(wearer, chemfuel,
                Mathf.Min(need, chemfuel.stackCount), true);
        }

        private static bool QueueFuelJobInternal(Pawn wearer, Thing chemfuel, int amount, bool queueIfBusy)
        {
            JobDef jd = LoadJobDef;
            if (jd == null || chemfuel == null || chemfuel.Destroyed || amount <= 0)
            {
                return false;
            }
            Pawn carrier = SelfCarrier(wearer, queueIfBusy);
            if (carrier == null)
            {
                return false;
            }
            Job job = new Job(jd, chemfuel, wearer);
            job.count = amount;
            return carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc, queueIfBusy);
        }

        // ★ v13：搬运者只能是穿甲者本人（他人不得替他装填）。
        //   自动模式（queueIfBusy=true）下征召中不派活（等脱战空闲再装）；
        //   手动模式（玩家点按钮）下即使征召也立即执行玩家命令。
        private static Pawn SelfCarrier(Pawn wearer, bool queueIfBusy)
        {
            if (wearer == null || wearer.Destroyed || !wearer.Spawned || wearer.Map == null
                || wearer.Downed || wearer.InMentalState || wearer.workSettings == null
                || wearer.IsPrisonerOfColony)
            {
                return null;
            }
            if (queueIfBusy && wearer.Drafted)
            {
                return null; // 自动装填：征召中的士兵不派（战斗优先）
            }
            return wearer;
        }

        public static Thing FindStockOf(Map map, string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return null;
            }
            List<Thing> things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t != null && !t.Destroyed)
                {
                    return t;
                }
            }
            return null;
        }

        public static int StockCountOf(Map map, string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null || map == null)
            {
                return 0;
            }
            int n = 0;
            List<Thing> things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t != null && !t.Destroyed)
                {
                    n += t.stackCount;
                }
            }
            return n;
        }

        // 库存中所有炮弹（def 有 projectileWhenLoaded = 可被发射的炮弹类）
        public static List<ThingDef> InStockShellDefs(Pawn wearer)
        {
            List<ThingDef> result = new List<ThingDef>();
            if (wearer == null || wearer.Map == null)
            {
                return result;
            }
            HashSet<string> seen = new HashSet<string>();
            List<Thing> things = wearer.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == null || t.Destroyed || t.def == null || seen.Contains(t.def.defName))
                {
                    continue;
                }
                if (t.def.projectileWhenLoaded != null)
                {
                    seen.Add(t.def.defName);
                    result.Add(t.def);
                }
            }
            return result;
        }

        // 装填 1 发炮弹（由搬运 JobDriver 在穿戴者身边调用；物资已在小人手上）
        public bool TryLoadOneShell(string shellDefName)
        {
            if (shells.Count >= Props.shellMax)
            {
                Messages.Message("Binguin_CompWillingCannon_23".Translate() + Props.shellMax + "Binguin_CompWillingCannon_24".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            shells.Add(shellDefName);
            return true;
        }

        // 加燃料（由搬运 JobDriver 在穿戴者身边调用；物资已在小人手上）
        public int TryAddFuelFromCarried(Thing carried)
        {
            if (carried == null || carried.Destroyed)
            {
                return 0;
            }
            int space = Mathf.CeilToInt(FuelMaxNow - fuelNow);
            if (space <= 0)
            {
                return 0;
            }
            int take = Mathf.Min(space, carried.stackCount);
            fuelNow += take;
            if (fuelNow > FuelMaxNow)
            {
                fuelNow = FuelMaxNow;
            }
            return take;
        }

        // ================= v10 站定瞄准（JobDriver 驱动） =================

        // 选点回调：预校验 → 派发炮击 job（小人站定瞄准 1s 后开火；
        // 期间移动/被打断 = 取消本次射击，与原版 warmup 手感一致）
        public bool TryBeginFire(IntVec3 dest)
        {
            try
            {
                Pawn wearer = Wearer;
                if (wearer == null || wearer.Destroyed || !wearer.Spawned || wearer.Map == null)
                {
                    return false;
                }
                string why = DisabledReasonText();
                if (why != null)
                {
                    Messages.Message(why, MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                if (!wearer.Position.InHorDistOf(dest, Props.range + 0.01f))
                {
                    Messages.Message("Binguin_CompWillingCannon_25".Translate() + Props.range.ToString("0") + "Binguin_CompWillingCannon_26".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                JobDef fireJobDef = FireJobDef;
                if (fireJobDef == null)
                {
                    Log.Warning("[冰鹅族] 未找到 Binguin_WillingFire job，无法开火。");
                    return false;
                }
                Job job = new Job(fireJobDef, new LocalTargetInfo(dest));
                if (!wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                {
                    Messages.Message("Binguin_CompWillingCannon_27".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                Log.Message("[冰鹅族] 威灵炮：炮击 job 已派发，站定瞄准 1s，落点 " + dest);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 威灵炮瞄准异常（已拦截）: " + e);
                return false;
            }
        }

        // ---- 真正发射（瞄准完成后调用）：扣 1 弹 + 10 燃料，30s 冷却，抛射 ----
        public bool ExecuteShot(IntVec3 dest)
        {
            try
            {
                Pawn wearer = Wearer;
                if (wearer == null || wearer.Destroyed || !wearer.Spawned || wearer.Map == null)
                {
                    return false;
                }
                if (GenTicks.TicksGame < nextFireTick)
                {
                    int left = (int)Mathf.Ceil((nextFireTick - GenTicks.TicksGame) / 60f);
                    Messages.Message("Binguin_CompWillingCannon_28".Translate() + left + "Binguin_CompWillingCannon_29".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                if (shells.Count <= 0)
                {
                    Messages.Message("Binguin_CompWillingCannon_30".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                if (fuelNow < Props.fuelPerShot)
                {
                    Messages.Message("Binguin_CompWillingCannon_31".Translate() + Props.fuelPerShot.ToString("0")
                        + "Binguin_CompWillingCannon_32".Translate() + FuelNow.ToString("0") + "Binguin_CompWillingCannon_33".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                // 检查目标在射程内
                if (!wearer.Position.InHorDistOf(dest, Props.range + 0.01f))
                {
                    Messages.Message("Binguin_CompWillingCannon_34".Translate() + Props.range.ToString("0") + "Binguin_CompWillingCannon_35".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                // 扣消耗：1 弹 + 10 燃料；冷却 30s
                string shellName = shells[shells.Count - 1];
                shells.RemoveAt(shells.Count - 1);
                fuelNow -= Props.fuelPerShot;
                if (fuelNow < 0f)
                {
                    fuelNow = 0f;
                }
                nextFireTick = GenTicks.TicksGame + FireCooldownTicks;

                // 抛射：炮弹的 projectileWhenLoaded（弹体自己飞向落点并爆炸）
                ThingDef shellDef = DefDatabase<ThingDef>.GetNamedSilentFail(shellName);
                ThingDef projDef = shellDef != null ? shellDef.projectileWhenLoaded : null;
                if (projDef == null)
                {
                    Log.Warning("[冰鹅族] 威灵炮：炮弹 " + shellName + " 无 projectileWhenLoaded");
                    return false;
                }
                Map map = wearer.Map;
                // ★ v11：0-3 格随机落点偏移（像原版迫击炮：瞄得准不代表落点准）
                //   在瞄准落点周围 0~impactSpread 格圆盘内取真实命中格
                IntVec3 actualDest = dest;
                if (Props.impactSpread > 0.01f)
                {
                    float dist = Rand.Range(0f, Props.impactSpread);
                    float ang = Rand.Range(0f, 360f);
                    Vector3 offsetVec = new Vector3(
                        Mathf.Cos(ang * Mathf.Deg2Rad) * dist, 0f,
                        Mathf.Sin(ang * Mathf.Deg2Rad) * dist);
                    IntVec3 candidate = (dest.ToVector3() + offsetVec).ToIntVec3();
                    if (candidate.InBounds(map))
                    {
                        actualDest = candidate;
                    }
                }
                Projectile proj = (Projectile)GenSpawn.Spawn(projDef, wearer.Position, map);
                proj.Launch(wearer, wearer.Position.ToVector3Shifted(),
                    new LocalTargetInfo(actualDest), new LocalTargetInfo(actualDest),
                    ProjectileHitFlags.IntendedTarget, false, null, null);
                if (actualDest != dest)
                {
                    Log.Message("[冰鹅族] 威灵炮发射 " + shellName + " → 瞄准 " + dest + "，落点偏移至 " + actualDest);
                }
                else
                {
                    Log.Message("[冰鹅族] 威灵炮发射 " + shellName + " → " + dest);
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[冰鹅族] 威灵炮发射异常（已拦截）: " + e);
                return false;
            }
        }
    }

    // ---- 发射迫击炮命令（v8，滑板 Command_BinguinSlide 同款；v9 走 1s 瞄准）----
    public class Command_BinguinWillingFire : Command
    {
        public Pawn caster;
        public float range = 50f;
        public CompBinguinWillingCannon cannon;

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            if (caster == null || caster.Destroyed || !caster.Spawned || caster.Map == null)
            {
                return;
            }
            if (cannon == null)
            {
                return;
            }
            // 开启 GameComponent 每帧画圈（targeting 全程可见，橙红色）
            GameComponent_BinguinDiplomacy.StartCannonAim(caster, range);
            RimWorld.TargetingParameters tp = new RimWorld.TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetSelf = false
            };
            Find.Targeter.BeginTargeting(tp,
                delegate (LocalTargetInfo target)
                {
                    Log.Message("[冰鹅族] 威灵炮：落点已选择 " + target);
                    GameComponent_BinguinDiplomacy.StopCannonAim();
                    if (cannon != null && target.IsValid && target.Cell.IsValid)
                    {
                        cannon.TryBeginFire(target.Cell);
                    }
                },
                caster,
                delegate
                {
                    GameComponent_BinguinDiplomacy.StopCannonAim();
                },
                null,
                false);
        }

        public override void GizmoUpdateOnMouseover()
        {
            if (caster != null && caster.Spawned && caster.Map != null)
            {
                GenDraw.DrawRadiusRing(caster.Position, range, new Color(1f, 0.55f, 0.2f));
            }
        }
    }

    // ---- v13 装填炮弹 + 自动装填设置 对话框（2026-08-21 用户要求）----
    //   删掉命令栏「补充燃料」「自动装填」按钮后，此对话框承载：
    //   ① 自动装填炮弹开关 + 自动补充燃料开关（自动轮询保留，只派穿甲者本人）
    //   ② 炮弹种类筛选（勾选 = 自动装填用弹种；行点击即装填 1 发）
    public class Dialog_BinguinLoadShell : Window
    {
        private CompBinguinWillingCannon comp;
        private Vector2 scrollPos;

        // ★ v15 性能：对话框 forcePause 每帧重绘，但库存炮弹扫描（全地图
        //   HaulableEver）与逐行库存统计很重 → 打开后缓存，每 60 tick 才重算。
        private List<ThingDef> cachedShellDefs = new List<ThingDef>();
        private List<int> cachedShellStocks = new List<int>();
        // ★ 2026-09 性能：对话框每帧重绘，逐行的名字/库存文案与标题文案
        //   改成随库存缓存一起算好（跟随 v15 的 60 tick 刷新节奏）。
        private List<string> cachedShellLabels = new List<string>();
        private List<string> cachedShellStockLabels = new List<string>();
        private string cachedHeader;
        private int cachedHeaderKey = int.MinValue;
        private int cacheTick = -1;
        private int cacheWearerTick = -1;

        public Dialog_BinguinLoadShell(CompBinguinWillingCannon cannon)
        {
            comp = cannon;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        // 缓存刷新（wearer 变化或 60 tick 到期才重扫库存）
        private void RefreshStockCacheIfNeeded()
        {
            try
            {
                Pawn wearer = comp != null ? comp.Wearer : null;
                int wt = wearer != null ? wearer.thingIDNumber : -1;
                if (wt == cacheWearerTick && cacheTick >= 0
                    && GenTicks.TicksGame - cacheTick < 60)
                {
                    return;
                }
                cacheWearerTick = wt;
                cacheTick = GenTicks.TicksGame;
                cachedShellDefs.Clear();
                cachedShellStocks.Clear();
                cachedShellLabels.Clear();
                cachedShellStockLabels.Clear();
                if (wearer == null || wearer.Map == null)
                {
                    return;
                }
                List<ThingDef> defs = CompBinguinWillingCannon.InStockShellDefs(wearer);
                for (int i = 0; i < defs.Count; i++)
                {
                    int stock = CompBinguinWillingCannon.StockCountOf(wearer.Map, defs[i].defName);
                    cachedShellDefs.Add(defs[i]);
                    cachedShellStocks.Add(stock);
                    cachedShellLabels.Add(defs[i].LabelCap);
                    cachedShellStockLabels.Add("Binguin_CompWillingCannon_36".Translate() + stock);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[冰鹅族] 装填对话框库存缓存异常：" + ex.Message);
            }
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(470f, 480f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (comp == null)
            {
                return;
            }
            float x = inRect.x;
            float y = inRect.y;
            // 标题文案缓存（键 = 弹舱数 + 燃料整数）
            int headerKey = comp.shells.Count * 100000 + (int)comp.FuelNow;
            if (cachedHeader == null || headerKey != cachedHeaderKey)
            {
                cachedHeaderKey = headerKey;
                cachedHeader = "Binguin_CompWillingCannon_37".Translate() + comp.shells.Count + "/" + comp.Props.shellMax
                    + "Binguin_CompWillingCannon_38".Translate() + comp.FuelNow.ToString("0") + "/" + comp.FuelMaxNow.ToString("0") + "）";
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, y, inRect.width, 28f), cachedHeader);
            y += 32f;
            Text.Font = GameFont.Small;

            // 1) 自动装填炮弹开关（v13：自动轮询每 4 秒，只派穿甲者本人）
            Widgets.CheckboxLabeled(new Rect(x, y, inRect.width - 20f, 26f),
                "Binguin_CompWillingCannon_39".Translate(), ref comp.autoReload);
            y += 28f;

            // 2) 自动补充燃料开关
            Widgets.CheckboxLabeled(new Rect(x, y, inRect.width - 20f, 26f),
                "Binguin_CompWillingCannon_40".Translate(), ref comp.autoFuel);
            y += 30f;

            // 3) 炮弹种类筛选/装填列表（滚动区；v15 用缓存防每帧全图扫描）
            RefreshStockCacheIfNeeded();
            float listH = inRect.height - (y - inRect.y) - 46f;
            if (listH < 60f)
            {
                listH = 60f;
            }
            Rect listRect = new Rect(x, y, inRect.width - 4f, listH);
            Widgets.Label(listRect, "");   // 占位防空
            Rect viewRect = new Rect(0f, 0f, listRect.width - 20f, 9999f);
            Widgets.BeginScrollView(listRect, ref scrollPos, viewRect);
            float ly = 0f;
            Text.Font = GameFont.Small;

            if (cachedShellDefs.Count == 0)
            {
                Widgets.Label(new Rect(0f, ly, viewRect.width, 40f),
                    "Binguin_CompWillingCannon_41".Translate());
                ly += 44f;
            }
            for (int i = 0; i < cachedShellDefs.Count; i++)
            {
                ThingDef def = cachedShellDefs[i];
                bool selected = comp.autoShellDefName == def.defName;
                int stock = i < cachedShellStocks.Count ? cachedShellStocks[i] : 0;

                // 行背景(选中高亮)
                Rect rowRect = new Rect(0f, ly, viewRect.width, 34f);
                if (selected)
                {
                    Widgets.DrawHighlight(rowRect);
                }
                // 左: 筛选圈(点击=设为自动弹种)
                string mark = selected ? "● " : "○ ";
                Rect selRect = new Rect(0f, ly + 4f, 130f, 26f);
                Widgets.Label(selRect, mark + (i < cachedShellLabels.Count
                    ? cachedShellLabels[i] : (string)def.LabelCap));
                if (Widgets.ButtonInvisible(selRect))
                {
                    if (selected)
                    {
                        comp.autoShellDefName = "";
                    }
                    else
                    {
                        comp.autoShellDefName = def.defName;
                        comp.autoReload = true;
                    }
                }
                // 中: 库存数（文案已随缓存算好）
                Widgets.Label(new Rect(136f, ly + 7f, 70f, 22f),
                    i < cachedShellStockLabels.Count ? cachedShellStockLabels[i] : "Binguin_CompWillingCannon_42".Translate() + stock);
                // 右: 手动装填 1 发按钮
                if (Widgets.ButtonText(new Rect(220f, ly + 1f, 120f, 32f),
                        comp.shells.Count >= comp.Props.shellMax ? "Binguin_CompWillingCannon_43".Translate() : "Binguin_CompWillingCannon_44".Translate()))
                {
                    if (comp.shells.Count >= comp.Props.shellMax)
                    {
                        Messages.Message("Binguin_CompWillingCannon_45".Translate() + comp.Props.shellMax + "Binguin_CompWillingCannon_46".Translate(),
                            MessageTypeDefOf.RejectInput, false);
                    }
                    else if (comp.QueueLoadJob(def.defName))
                    {
                        Close();
                    }
                }
                ly += 38f;
            }
            Widgets.EndScrollView();
        }
    }
}

