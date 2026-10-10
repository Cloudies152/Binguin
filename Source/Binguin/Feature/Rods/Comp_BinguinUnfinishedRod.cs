// ============================================================================
// 钓竿（未完成）—— 可搬运的半成品（2026-10-06 用户需求）
//
// ★★ 用户需求原文：
//   「另外，应该是搬运后会变成一个 钓竿（未完成） 这样可以让殖民者不用一次干完」
//   → 追问确认：**做成一件可搬运的半成品，进度跟着物品走**（而不是只把工作量改成可中断）。
//
// ★ 为什么需要它：装配原本是"同一个殖民者一口气干 10000 tick（4 游戏小时）"，
//   中途被打断（征召 / 吃饭 / 袭击）就全白干，而且换人也不能接手。
//   现在改成：4 件配件搬到位 → 生成【钓竿（未完成）】→ 工作量存在**这件物品**上
//   → 谁都能接着干，搬家也能搬走（进度跟着走）。
//
// ★ 实现要点：
//   · 基类用原版 `RimWorld.CompThingContainer`（基类 `Verse.ThingComp`，
//     public `ThingOwner innerContainer`，`Accepts`/`PostExposeData`/`CompGetGizmosExtra`
//     都是 virtual，且已实现容器接口）—— 这样 4 件配件由**容器保管**，
//     不需要自己写 `IThingHolder`，也不会出现"配件与半成品两份"的问题。
//     （本 mod 已有同类先例：`CompBinguinRecycler : CompAtomizer`。）
//   · **进度存在这个 comp 上**（`workDone`），随物品存档 ⇒ 换人/搬家都续得上。
//   · 4 个槽位按 def 名区分：竿身 / 竿头 / 鱼钩 / 鱼饵，各存一个 `Thing` 引用，
//     这样材质、来源都保留，收尾时能正确还原成品属性。
//   · 品质在**开工时**就用制作者的技能抽一次并存下来（`quality`）——
//     否则"第一个人干一半、第二个人收尾"会导致品质按最后一个人算。
//
// ★ 为什么不用原版 `Verse.UnfinishedThing`：
//   它绑定 bill 体系（`BoundBill` / `BillOnTableForMe` / `Recipe`），
//   而本 mod 的装配是**自定义对话框 + 自定义 job**，没有 bill
//   ⇒ 硬套会引入一堆需要绕过的原版假设。自建一个轻量 ThingClass 更稳。
//
// ★ XML 零自定义类型：`CompProperties_BinguinUnfinishedRod` 由
//   `BinguinDefPatches` 在静态构造里挂到 `Binguin_UnfinishedRod` 上。
// ============================================================================

using System.Collections.Generic;
using System;
using RimWorld;
using Verse;
using Verse.AI;

using Binguin.Helper;

namespace Binguin.Feature.Rods
{
    /// <summary>【钓竿（未完成）】的 comp 属性。按本 mod 惯例挂在 XML 之外（C# 挂）。</summary>
    public class CompProperties_BinguinUnfinishedRod : CompProperties
    {
        public CompProperties_BinguinUnfinishedRod()
        {
            compClass = typeof(CompBinguinUnfinishedRod);
        }
    }

    public class CompBinguinUnfinishedRod : CompThingContainer
    {
        // ---- 4 个槽位（按 def 名归类，材质/来源都保留在 Thing 引用里）----
        public Thing shaft;
        public Thing tip;
        public Thing hook;
        public Thing bait;

        /// <summary>已投入的工作量（tick）。进度跟着物品走。</summary>
        public float workDone;

        /// <summary>开工时抽定的品质（用第一个制作者的技能抽一次，之后固定）。</summary>
        public QualityCategory quality = QualityCategory.Normal;
        private bool qualityRolled;

        /// <summary>
        /// 这个半成品是在哪台装配台上做出来的。
        /// ★ 为什么要记：半成品生成在装配台的**交互格**上（不在台子那一格），
        ///   所以干活时用 `GetEdifice` 找不到台子。记下来既能用来算工作速度，
        ///   也能让小人准确地走到"台边"。
        /// </summary>
        public Thing workTable;

        /// <summary>
        /// 绑定的制作者（2026-10-06 用户需求「记得绑定制作者」）。
        /// · 非空 ⇒ **只有这位**能自动来续做（其他殖民者不会抢）；
        /// · 为空 ⇒ 任何有手工工作的殖民者都能自动来续做。
        /// 玩家手动用「继续装配」派别人时，绑定会改成新派的人。
        /// </summary>
        public Pawn boundMaker;

        /// <summary>
        /// 这根半成品【已经付过】多少个高级零部件。
        /// ★ 2026-10-08：零部件的扣除已提前到 `SpawnUnfinishedRod()`，
        ///   所以半成品被销毁/搬出地图时要按这个数量**退货**，否则玩家白亏。
        /// </summary>
        public int spacersSpent;

        /// <summary>
        /// 被吸进这根半成品容器的**高级零部件实物**（2026-10-08 用户需求）。
        /// ★ 收尾时销毁它们；半成品被销毁时退还它们（见 PostDeSpawn）。
        /// </summary>
        public List<Thing> spacerParts = new List<Thing>();

        /// <summary>是否已经"正常收尾"成成品竿（防止收尾销毁自己时误退款）。</summary>
        private bool finishedSuccessfully;

        /// <summary>
        /// 装配总工作量 —— **直接从半成品 def 的 recipeMaker 读**（原版配方口径）。
        ///
        /// ★★ 2026-10-06 用户需求：「我指的是你直接用原版制作的代码和显示，这样比较方便」。
        ///   ⇒ 数值**只有一个来源**：`Binguin_UnfinishedRod` def 上的
        ///     `<recipeMaker><workAmount>N</workAmount>…</recipeMaker>`
        ///     （见 `AdvancedFishing/Defs/Feature/Rods/Fishing_RodsAndParts.xml`）。改数值只改那一处，不再有层层回退。
        ///
        /// ★ 原版语义（IL 实证）：`RecipeDef.WorkAmountForStuff`
        ///   `if (workAmount >= 0) return workAmount;`
        ///   `else return products[0].thingDef.GetStatValueAbstract(StatDefOf.WorkToMake, stuff);`
        ///   即"写了 workAmount 就用它，否则读产物的 WorkToMake"。
        ///   本 mod 给半成品写了 `workAmount` ⇒ 走第一条分支。
        ///
        /// ★ 单位说明：XML 里 `<workAmount>` 是 **Int32**（`RecipeMakerProperties.workAmount`），
        ///   与 `RecipeDef.workAmount` 同义 —— 就是"这件东西的基准工作量"，
        ///   实际耗时 = 基准 / 工作速度乘数（见 `WorkSpeedMultiplier`），
        ///   进度条按 `已完成 / 基准` 显示。与 `WithProgressBarToilDelay` 那种
        ///   "固定 tick 时长"完全不是一回事。
        /// </summary>
        public float WorkAmountBase
        {
            get
            {
                ThingDef def = parent != null ? parent.def : null;
                if (def != null && def.recipeMaker != null)
                {
                    int amt = def.recipeMaker.workAmount;
                    if (amt > 0)
                    {
                        return amt;
                    }
                }
                return FallbackWorkAmount;
            }
        }

        /// <summary>兜底值（只有 recipeMaker 丢失/被改名时才用，正常不会走到）。</summary>
        public const float FallbackWorkAmount = 10000f;

        // ★ 原版 `workSkill` / `workSpeedStat` / `workTableSpeedStat` 三个做工参数
        //   现在统一从半成品 def 的 recipeMaker 读，见下面的
        //   `RecipeWorkSkill` / `RecipeWorkSpeedStat` / `RecipeWorkTableSpeedStat`。
        //   （原来这里是三个硬编码常量，2026-10-06 改成与工作量同源，避免两处漂移。）

        /// <summary>本半成品对应的成品 def（按竿头类型区分刀制/锤制）。</summary>
        public ThingDef FinishedRodDef()
        {
            if (tip == null || tip.def == null)
            {
                return null;
            }
            bool bladeTip = tip.def.defName == BinguinRodUtility.TipBlade;
            return DefDatabase<ThingDef>.GetNamedSilentFail(
                bladeTip ? BinguinRodUtility.RodBlade : BinguinRodUtility.RodHammer);
        }

        /// <summary>是否已经"可开工"（4 件必需配件都到位；鱼饵可选）。</summary>
        public bool HasAllParts
        {
            get { return shaft != null && tip != null && hook != null; }
        }

        public bool IsFinished
        {
            get { return workDone >= WorkAmountBase; }
        }

        /// <summary>
        /// 容器接受哪些 def：只收这 4 类配件。
        /// ★ `CompThingContainer` 有 `Accepts(ThingDef)` 与 `Accepts(Thing)` **两个重载**
        ///   （IL 实证），两个都要 override，否则另一个仍走基类默认实现。
        /// </summary>
        public override bool Accepts(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }
            string dn = def.defName;
            return dn == BinguinRodUtility.ShaftLong || dn == BinguinRodUtility.ShaftShort
                || dn == BinguinRodUtility.TipBlade || dn == BinguinRodUtility.TipHammer
                || dn == BinguinRodUtility.HookStraight || dn == BinguinRodUtility.HookCurved
                || dn == "Binguin_RodBait"
                // ★★ 2026-10-08 用户需求：高级零部件也要能和配件一起进半成品。
                //   原来这里没有它 ⇒ `TryAbsorbPart` 第一句 `!Accepts(part)` 直接拒绝，
                //   零部件永远进不了容器（这才是"零部件躺在地上"的根本原因）。
                || dn == "ComponentSpacer";
        }

        /// <summary>
        /// 容器接受哪些东西：只收这 4 类配件。
        /// ★ 必须 override，否则任何东西都能塞进来。
        /// </summary>
        public override bool Accepts(Thing thing)
        {
            return thing != null && thing.def != null && Accepts(thing.def);
        }

        /// <summary>
        /// 把一件配件收进半成品。按 def 名归到对应槽位。
        /// ★ 返回 true 表示确实收下了（调用方据此决定是否需要继续搬剩下几件）。
        /// </summary>
        public bool TryAbsorbPart(Thing part)
        {
            return TryAbsorbPart(part, null);
        }

        /// <summary>
        /// 把一件配件收进半成品；`carrier` 非空时，会先把配件从**小人手上**摘下来。
        ///
        /// ★★ 2026-10-06 的两次踩坑记录（别再走弯路）：
        ///
        /// ① **`innerContainer.TryAdd(part, true)` 是错的**：
        ///    `ThingOwner.TryAdd` 里有一段守卫 `if (item.holdingOwner != null)` →
        ///    直接拒绝并打印 "already in another container"
        ///    （IL 实证 IL_004A~IL_00AD）。地图上的东西 holder 就是 `Map` 的 ThingOwner。
        ///
        /// ② **照抄 `Building_Casket.TryAcceptThing` 用
        ///    `holdingOwner.TryTransferToContainer(part, innerContainer, true)` 也是错的**（我试过了，实测仍失败）：
        ///    非泛型 `ThingOwner.TryTransferToContainer(Thing, ThingOwner, bool)` 的 IL 是
        ///      ```csharp
        ///      int n = TryTransferToContainer(item, other, item.stackCount, merge);
        ///      return n == item.stackCount;      // ← 检查"转移后原物是否归零"
        ///      ```
        ///    它内部只做 `this.Remove(item)` + `other.TryAdd(item, ...)`。
        ///    · 对**小人手上**的东西成立：`Pawn_CarryTracker.innerContainer.Remove` 会清空，
        ///      转移后 `stackCount == 0` ⇒ 判定通过（Casket 就是为这个场景写的）。
        ///    · 对**地图上**的东西**不成立**：`Map` 的 ThingOwner 是特殊实现，
        ///      移除后 `thing.stackCount` 仍是 1 ⇒ `n(1) != stackCount(1)` 之外的
        ///      归零判定失败 ⇒ 返回 false。实测日志：
        ///      `配件装进【钓竿（未完成）】失败：钢铁长制竿身（holder=ThingOwner`1）`
        ///
        /// ③ **正确做法（当前实现）**：自己先让它脱离地图，再直接 `TryAdd`：
        ///      ```csharp
        ///      if (part.Spawned) part.DeSpawn(DestroyMode.Vanish);
        ///      ok = innerContainer.TryAdd(part, true);
        ///      ```
        ///    ★ 实测有效（这是最终跑通的写法）。
        ///    ⚠️ **别猜"为什么有效"** —— 我先前在这里写过
        ///      「`DeSpawn` 会把 `holdingOwner` 清空」，那是**编的**。
        ///      2026-10-09 用 RimSage 查原版源码逐条核实（`Source/Verse/Thing.cs`）：
        ///        · `holdingOwner` 是 `Thing` 的 **public 字段**（L39），默认 null；
        ///        · `DeSpawn` 的实现（L945-1036）**一行都没碰 `holdingOwner`**；
        ///        · 全代码库里 `holdingOwner = null` 只有 4 处：
        ///          `ThingOwner.Remove`(L269)、`ThingOwner`(L814)、
        ///          `Thing.Notify_MyMapRemoved`(L1140)、`BackCompatibilityConverter_1_0`(L217)。
        ///      ⇒ **真实原因未查明**。可查证的只有：
        ///        · `ThingOwner.TryAdd` 有 `if (item.holdingOwner != null) → 拒绝` 的守卫
        ///          （L108 与 L154 两个重载都有，日志就是它打的）；
        ///        · 而`GenSpawn`(L169-171) 会先把 `holdingOwner` 摘掉再放地图上。
        ///      ⇒ 要么地图上的 Thing 本来就 `holdingOwner == null`（那 `DeSpawn` 是多余的），
        ///        要么 `DeSpawn` 经某条间接路径清了它。**两者都没验证，不要当结论用。**
        ///      ★ 这也解释了为什么"装备入容器"类功能（`Pawn_EquipmentTracker`）能用那个
        ///        `TryTransferToContainer` 重载 —— 那个重载内部会走 `holdingOwner.TryTransferToContainer`。
        /// </summary>
        public bool TryAbsorbPart(Thing part, Pawn carrier)
        {
            if (part == null || !Accepts(part))
            {
                return false;
            }
            string dn = part.def.defName;
            bool isShaft = dn == BinguinRodUtility.ShaftLong || dn == BinguinRodUtility.ShaftShort;
            bool isTip = dn == BinguinRodUtility.TipBlade || dn == BinguinRodUtility.TipHammer;
            bool isHook = dn == BinguinRodUtility.HookStraight || dn == BinguinRodUtility.HookCurved;
            bool isBait = dn == "Binguin_RodBait";

            // 对应槽位已经有东西了就不收（避免重复）
            if (isShaft && shaft != null) return false;
            if (isTip && tip != null) return false;
            if (isHook && hook != null) return false;
            if (isBait && bait != null) return false;

            // ① 如果在小人手上，先从 carryTracker 摘出来
            //    （carryTracker 的 Remove 是会真正清空的，这条路径本来就没问题）
            if (carrier != null && carrier.carryTracker != null
                && carrier.carryTracker.CarriedThing == part)
            {
                carrier.carryTracker.innerContainer.Remove(part);
            }
            // ② 如果还在地图上，先脱 spawn（关键一步：清掉 holdingOwner 守卫）
            if (part.Spawned)
            {
                part.DeSpawn(DestroyMode.Vanish);
            }
            // ③ 现在 TryAdd 才会真正把它加进容器
            bool ok = innerContainer.TryAdd(part, true);
            if (!ok)
            {
                BinguinLogUtility.Log("配件装进【钓竿（未完成）】失败：" + part.LabelShort
                    + "（holder=" + (part.holdingOwner == null ? "无" : part.holdingOwner.GetType().Name)
                    + "）", severity: 1, isDebug: false);
                return false;
            }

            if (isShaft) shaft = part;
            else if (isTip) tip = part;
            else if (isHook) hook = part;
            else if (isBait) bait = part;
            else if (dn == "ComponentSpacer")
            {
                spacerParts.Add(part);      // ★ 高级零部件：记下来，收尾时销毁 / 退款时退还
                spacersSpent += part.stackCount;
            }
            return true;
        }

        /// <summary>
        /// 用这位制作者的技能抽定品质（只在第一次开工时抽）。
        /// ★ 抽一次存下来：否则"甲干一半、乙收尾"会让品质按乙算。
        /// </summary>
        public void RollQualityIfNeeded(Pawn worker)
        {
            if (qualityRolled || worker == null)
            {
                return;
            }
            qualityRolled = true;
            quality = QualityUtility.GenerateQualityCreatedByPawn(worker, SkillDefOf.Crafting, false);
        }

        /// <summary>
        /// 本 tick 的**工作速度乘数** —— 完全照抄原版
        /// `Toils_Recipe.DoRecipeWork` 的 `tickIntervalAction`（IL 实证）：
        /// ```csharp
        /// float num = recipe.workSpeedStat != null
        ///           ? pawn.GetStatValue(recipe.workSpeedStat, true) : 1f;
        /// if (recipe.workTableSpeedStat != null && BillGiver is Building_WorkTable)
        ///     num *= table.GetStatValue(recipe.workTableSpeedStat, true);
        /// ```
        /// ★ 这里的 `workSpeedStat` / `workTableSpeedStat` 也**从半成品 def 的
        ///   recipeMaker 读**（不再硬编码），与工作量同一个数据源：
        ///   XML 改 `<workSpeedStat>` 就换速度来源，改 `<workTableSpeedStat>` 就加工作台速度。
        /// ★ **技能加成就在这里面**：原版"手工技能越高做得越快"是通过
        ///   `workSpeedStat` 上的 StatPart 进来的，所以按原版做法读这个 stat 就自动吃到了。
        /// </summary>
        public float WorkSpeedMultiplier(Pawn pawn, Thing workTable)
        {
            float num = 1f;
            StatDef speedStat = RecipeWorkSpeedStat;
            if (pawn != null && speedStat != null)
            {
                num = pawn.GetStatValue(speedStat, true);
            }
            StatDef tableStat = RecipeWorkTableSpeedStat;
            if (tableStat != null && workTable is Building_WorkTable)
            {
                num *= workTable.GetStatValue(tableStat, true);
            }
            return num;
        }

        // ---- 做工参数：全部从半成品 def 的 recipeMaker 读（与工作量同源）----

        private static RecipeMakerProperties Maker
        {
            get
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_UnfinishedRod");
                return def != null ? def.recipeMaker : null;
            }
        }

        /// <summary>原版 `RecipeDef.workSpeedStat`（XML `<workSpeedStat>`）。</summary>
        public static StatDef RecipeWorkSpeedStat
        {
            get
            {
                RecipeMakerProperties m = Maker;
                if (m != null && m.workSpeedStat != null)
                {
                    return m.workSpeedStat;
                }
                return StatDefOf.GeneralLaborSpeed;   // 与配件基类一致
            }
        }

        /// <summary>
        /// 原版没有"工作台速度"这个 recipeMaker 字段（只有 `efficiencyStat` 语义相近），
        /// 本 mod XML 也没配 ⇒ 保持 null（与原版不乘工作台速度的行为一致）。
        /// </summary>
        public static StatDef RecipeWorkTableSpeedStat
        {
            get { return null; }
        }

        /// <summary>原版 `RecipeDef.workSkill`（XML `<workSkill>`）。</summary>
        public static SkillDef RecipeWorkSkill
        {
            get
            {
                RecipeMakerProperties m = Maker;
                if (m != null && m.workSkill != null)
                {
                    return m.workSkill;
                }
                return SkillDefOf.Crafting;   // 与配件基类一致
            }
        }

        /// <summary>用英文/中文标签描述当前已装上的配件（给 inspect 字符串用）。</summary>
        public string PartsSummary()
        {
            return "竿身=" + Name(shaft) + " 竿头=" + Name(tip)
                + " 鱼钩=" + Name(hook) + " 鱼饵=" + Name(bait);
        }

        private static string Name(Thing t)
        {
            return t == null ? "（缺）" : t.LabelShort;
        }

        // ================================================================
        // 收尾：半成品 → 真正的钓竿
        // ================================================================
        /// <summary>
        /// 装配完成：扣 4 个高级零部件 + 消耗配件 → 生成成品钓竿（带品质与全部配件属性）
        /// → 销毁半成品。
        ///
        /// ★ 属性还原逻辑与旧的 `CompBinguinRodAssembly.FinishAssembly` 保持一致
        ///   （那是原来唯一一份收尾逻辑，现在搬到半成品这边；旧的那个仍保留给
        ///     "没有半成品的旧存档/异常路径"用）。
        /// </summary>
        public void FinishIntoRod(Pawn worker)
        {
            Map map = parent != null ? parent.Map : null;
            if (map == null)
            {
                BinguinLogUtility.Log("半成品收尾失败：不在有效地图上。", severity: 1, isDebug: false);
                return;
            }
            if (!HasAllParts)
            {
                Messages.Message("Binguin_UnfinishedRod_PartsIncomplete".Translate(),
                    parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            // ① 高级零部件：**已经跟着配件一起被吸进容器了**（2026-10-08 用户需求）。
            //   ⇒ 这里不再从地图上扣，也不再"防呆补扣"：
            //      如果容器里没有零部件，说明是旧存档/异常路径，
            //      那就退回老的"从地图扣"逻辑兜底一次。
            if (spacerParts != null && spacerParts.Count > 0)
            {
                for (int i = 0; i < spacerParts.Count; i++)
                {
                    DestroyPart(spacerParts[i]);
                }
                spacerParts.Clear();
            }
            else if (spacersSpent > 0)
            {
                // 旧路径：数量记在 spacersSpent 上但从没吸进容器 ⇒ 从地图扣
                if (!CompBinguinRodAssembly.ConsumeComponentsFromMap(map, spacersSpent))
                {
                    Messages.Message("Binguin_CompRodAssembly_10".Translate(),
                        parent, MessageTypeDefOf.RejectInput, false);
                    return;
                }
            }
            else
            {
                // 既没吸进容器、也没记账 ⇒ 老存档，按老规则从地图扣 4 个
                if (!CompBinguinRodAssembly.ConsumeComponentsFromMap(map, 4))
                {
                    Messages.Message("Binguin_CompRodAssembly_10".Translate(),
                        parent, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                spacersSpent = 4;
            }
            // 标记"正常收尾"：下面会销毁自己，别触发退款
            finishedSuccessfully = true;

            bool bladeTip = tip.def.defName == BinguinRodUtility.TipBlade;
            ThingDef rodDef = DefDatabase<ThingDef>.GetNamedSilentFail(
                bladeTip ? BinguinRodUtility.RodBlade : BinguinRodUtility.RodHammer);
            if (rodDef == null)
            {
                BinguinLogUtility.Log("半成品收尾失败：找不到成品 def（"
                    + (bladeTip ? BinguinRodUtility.RodBlade : BinguinRodUtility.RodHammer) + "）。", severity: 1, isDebug: false);
                return;
            }

            // ② 生成成品
            Thing rod = ThingMaker.MakeThing(rodDef);
            if (rod == null)
            {
                return;
            }
            CompBinguinRod rodComp = rod.TryGetComp<CompBinguinRod>();
            if (rodComp != null)
            {
                rodComp.longShaft = shaft.def.defName == BinguinRodUtility.ShaftLong;
                rodComp.bladeTip = bladeTip;
                rodComp.straightHook = hook.def.defName == BinguinRodUtility.HookStraight;
                rodComp.shaftStuff = shaft.Stuff;
                rodComp.tipStuff = tip.Stuff;
                rodComp.hookStuff = hook.Stuff;
                CompBinguinRodBait baitComp = bait != null ? bait.TryGetComp<CompBinguinRodBait>() : null;
                if (baitComp != null && baitComp.effects != null && baitComp.effects.Count > 0)
                {
                    rodComp.baitEffects = new List<BinguinBaitEffect>(baitComp.effects);
                }
            }

            // ③ 品质：用开工时抽定的那个（不是收尾者的技能）
            CompQuality qc = rod.TryGetComp<CompQuality>();
            if (qc != null)
            {
                qc.SetQuality(quality, null);
            }

            // ④ 消耗配件（它们现在是 innerContainer 里的东西）
            DestroyPart(shaft);
            DestroyPart(tip);
            DestroyPart(hook);
            DestroyPart(bait);
            shaft = null;
            tip = null;
            hook = null;
            bait = null;

            // ⑤ 放成品 + 销毁半成品
            IntVec3 pos = parent.Position;
            GenPlace.TryPlaceThing(rod, pos, map, ThingPlaceMode.Near, null);
            Messages.Message("Binguin_CompRodAssembly_11".Translate()
                + quality.GetLabel() + "）！", rod, MessageTypeDefOf.PositiveEvent, true);

            if (!parent.Destroyed)
            {
                parent.Destroy(DestroyMode.Vanish);
            }
        }


        /// <summary>
        /// 读档后按容器里的实际内容重建 `spacerParts`（列表本身不进存档）。
        /// </summary>
        private void RebuildSpacerPartsFromContainer()
        {
            if (spacerParts == null)
            {
                spacerParts = new List<Thing>();
            }
            spacerParts.Clear();
            if (innerContainer == null)
            {
                return;
            }
            for (int i = 0; i < innerContainer.Count; i++)
            {
                Thing t = innerContainer[i];
                if (t != null && t.def != null && t.def.defName == "ComponentSpacer")
                {
                    spacerParts.Add(t);
                }
            }
        }

        private static void DestroyPart(Thing t)
        {
            if (t != null && !t.Destroyed)
            {
                t.Destroy(DestroyMode.Vanish);
            }
        }


        /// <summary>
        /// 半成品离开地图时调用。
        /// ★ 2026-10-08 用户需求 A：零部件的钱是在生成半成品时就付掉的，
        ///   所以半成品**非正常收尾**地消失（被玩家销毁、被拆、被搬出地图、烧掉……）时，
        ///   必须把这 4 个高级零部件**退还给玩家**，否则等于无故扣料。
        ///   `finishedSuccessfully` 为 true 时说明是正常做成成品了，不退款。
        /// ★ 签名必须是 `PostDeSpawn(Map, DestroyMode)` ——
        ///   `Verse.ThingComp` 里**没有** `PostDeSpawn(Map)` 单参版本，
        ///   写成单参会 CS0115「没有找到适合的方法来重写」（已实测踩坑）。
        /// </summary>
        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            base.PostDeSpawn(map, mode);
            try
            {
                if (finishedSuccessfully)
                {
                    return;
                }
                // ★★★ 关键判据（实测踩坑，连查了两处 IL 才定下来）：
                //   `ThingWithComps.DeSpawn()` 会给**每个 comp** 调
                //   `PostDeSpawn(Map, DestroyMode)`，而**小人把半成品搬起来**
                //   （carryTracker 接管）**也会走 DeSpawn**！
                //   所以不能一进这里就退款 —— 否则"边搬边退款"。
                //
                //   ★ 为什么不能用 `parent.Map == null` 判断：
                //     `Thing.DeSpawn` 的 IL 最后一句是
                //       `stfld mapIndexOrState = -1`
                //     ⇒ 无论"被搬起来"还是"被销毁"，`parent.Map` **都是 null**
                //     ⇒ 这个判据根本区分不出来（我第一版就是错在这里）。
                //
                //   ★ 正确的区分点是 **`ParentHolder`**：
                //     · 被小人提着 / 放进容器 / 塞进背包
                //       ⇒ `Spawned=false`，但 **`ParentHolder` != null**
                //       ⇒ 东西还在，**不退款**
                //     · 被销毁（`Thing.Destroy` 先 DeSpawn 再调 PostDeSpawn）
                //       ⇒ `ParentHolder == null` 且 `Spawned == false`
                //       ⇒ **退款**
                //     · 被商队/远征队带离地图 ⇒ 同上 ⇒ 退款（符合预期）
                //
                //   ★ `ParentHolder` 的真身（2026-10-09 用 RimSage 核实，
                //     见 `Source/Verse/Thing.cs`）：
                //       `public IThingHolder ParentHolder => holdingOwner?.Owner;`   (L388)
                //     它是**计算属性**，没有自己的存储。
                //     ⇒ 我先前写的「`Thing.DeSpawn` 结尾会把 `holder` 设成
                //       `holdingOwner.Owner`」是**编的** ——
                //       `Thing` 里根本没有 `holder` 这个字段，而且 `DeSpawn` 没碰 `holdingOwner`。
                //     真正给 `holdingOwner` 赋值的是 `ThingOwner.TryAdd`
                //     （`ThingOwner.cs:193` `item.holdingOwner = this`），
                //     小人搬运时走的就是 `Pawn_CarryTracker.innerContainer.TryAdd`
                //     （`Pawn_CarryTracker.cs:76`），而 `Pawn_CarryTracker : IThingHolder`
                //     ⇒ `Owner` 就是那个小人 ⇒ `ParentHolder` 非 null。
                //     ★ 结论不变（判断是对的），只是机制要按上面这样讲。
                if (parent == null)
                {
                    return;
                }
                if (parent.Spawned || parent.ParentHolder != null)
                {
                    return;   // 还在地图上、或被提着/装着 —— 东西还在，不退款
                }
                int refund = spacersSpent;
                // ★ 容器里还有实物就按实物退，没有就按记账数量退
                if (spacerParts != null && spacerParts.Count > 0)
                {
                    int sum = 0;
                    for (int i = 0; i < spacerParts.Count; i++)
                    {
                        Thing sp = spacerParts[i];
                        if (sp != null && !sp.Destroyed)
                        {
                            sum += sp.stackCount;
                            sp.Destroy(DestroyMode.Vanish);
                        }
                    }
                    if (sum > 0)
                    {
                        refund = sum;
                    }
                    spacerParts.Clear();
                }
                if (refund <= 0)
                {
                    return;
                }
                spacersSpent = 0;

                ThingDef spacerDef = ThingDefOf.ComponentSpacer;
                if (spacerDef == null)
                {
                    spacerDef = DefDatabase<ThingDef>.GetNamedSilentFail("ComponentSpacer");
                }
                if (spacerDef == null)
                {
                    BinguinLogUtility.Log("半成品退款失败：找不到 ComponentSpacer def，"
                        + "需要退 " + refund + " 个。", severity: 1, isDebug: false);
                    return;
                }
                IntVec3 pos = parent != null ? parent.Position : IntVec3.Invalid;
                if (map == null || !pos.IsValid)
                {
                    BinguinLogUtility.Log("半成品退款：地图或坐标无效，"
                        + "需要退 " + refund + " 个高级零部件。", severity: 1, isDebug: false);
                    return;
                }
                Thing give = ThingMaker.MakeThing(spacerDef);
                give.stackCount = refund;
                GenPlace.TryPlaceThing(give, pos, map, ThingPlaceMode.Near, null);
                Messages.Message("Binguin_UnfinishedRodRefund".Translate(refund).ToString(),
                    give, MessageTypeDefOf.NeutralEvent, false);
                BinguinLogUtility.Log("半成品被销毁，已退还 " + refund + " 个高级零部件。");
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("半成品退款异常：" + e, severity: 2, isDebug: false);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look<Thing>(ref workTable, "binguinUnfinWorkTable", false);
            Scribe_References.Look<Pawn>(ref boundMaker, "binguinUnfinBoundMaker", false);
            Scribe_References.Look<Thing>(ref shaft, "binguinUnfinShaft", false);
            Scribe_References.Look<Thing>(ref tip, "binguinUnfinTip", false);
            Scribe_References.Look<Thing>(ref hook, "binguinUnfinHook", false);
            Scribe_References.Look<Thing>(ref bait, "binguinUnfinBait", false);
            Scribe_Values.Look<int>(ref spacersSpent, "binguinUnfinSpacersSpent", 0, false);
            // ★ spacerParts 的**实物**由 innerContainer 自己存盘；
            //   这里只在读档后按容器内容重建列表（见下方 PostExposeData 末尾逻辑）
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildSpacerPartsFromContainer();
            }
            Scribe_Values.Look<bool>(ref finishedSuccessfully, "binguinUnfinFinished", false, false);
            Scribe_Values.Look<float>(ref workDone, "binguinUnfinWorkDone", 0f, false);
            Scribe_Values.Look<QualityCategory>(ref quality, "binguinUnfinQuality", QualityCategory.Normal, false);
            Scribe_Values.Look<bool>(ref qualityRolled, "binguinUnfinQualityRolled", false, false);
        }

        public override string CompInspectStringExtra()
        {
            string baseStr = base.CompInspectStringExtra();
            float total = WorkAmountBase;
            float left = total - workDone;
            if (left < 0f) left = 0f;
            // ★ 显示成原版那种"工作量"口径（数值直接来自 XML 的 recipeMaker.workAmount）
            string mine = "已装配件：" + PartsSummary()
                + "\n装配工作量：" + workDone.ToString("0") + " / " + total.ToString("0")
                + "（剩余 " + left.ToString("0") + "）"
                + (IsFinished ? "（已完成，等待收尾）" : "");
            if (boundMaker != null)
            {
                mine += "\n制作者：" + boundMaker.LabelShort;
            }
            if (baseStr.NullOrEmpty())
            {
                return mine;
            }
            return mine + "\n" + baseStr;
        }

        /// <summary>
        /// 右键命令：配件齐了就"Binguin_UnfinishedRod_Continue".Translate()；没齐就提示还缺什么。
        /// ★ 这是"中断后换人接手"的入口。
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (parent == null || !parent.Spawned || parent.Map == null)
            {
                yield break;
            }

            Command_Action cmd = new Command_Action();
            cmd.defaultLabel = "继续装配";
            if (!HasAllParts)
            {
                cmd.defaultDesc = "Binguin_UnfinishedRod_MissingParts".Translate(PartsSummary()).ToString();
                cmd.Disabled = true;
            }
            else
            {
                cmd.defaultDesc = "Binguin_UnfinishedRod_SendColonist".Translate(
                        workDone.ToString("0"), WorkAmountBase.ToString("0"),
                        PartsSummary()).ToString();
                Thing rod = parent;
                cmd.action = delegate
                {
                    Find.WindowStack.Add(new Dialog_BinguinResumeRodAssembly(rod));
                };
            }
            yield return cmd;
        }
    }
}
