// ============================================================================
// 【钓竿（未完成）】的工作 job —— **完整照抄原版制作机制**
//
// JobDef = Binguin_WorkOnUnfinishedRod（targetA = 半成品物品）
//   driverClass 由 `BinguinDefPatches` 静态挂载（XML 零自定义类型）。
//
// ★★ 2026-10-06 用户需求（两轮澄清后的最终理解）：
//   「应该是搬运后会变成一个 钓竿（未完成） 这样可以让殖民者不用一次干完」
//   「10000是我填的，这没错，但是我指的是工作量应该是参考原版的制作，
//     下面显示进度条，并且会受到手工技能和工作速度加成的原版制作机制」
//   ⇒ 不是"我拍一个数字、自己发明一套算法"，而是**复刻原版
//     `Verse.AI.Toils_Recipe.DoRecipeWork`**（那是原版每一次 bill 制作都在跑的方法）：
//
//   原版 tickIntervalAction 的 IL（`<DoRecipeWork>b__2`，逐条抄下来）：
//   ```csharp
//   // ① 技能成长
//   if (recipe.workSkill != null && recipe.UsesUnfinishedThing && pawn.skills != null)
//       pawn.skills.Learn(recipe.workSkill,
//                         0.1f * recipe.workSkillLearnFactor * delta, false, false);
//   // ② 速度乘数（技能加成通过 workSpeedStat 上的 StatPart 自动进来）
//   float num = recipe.workSpeedStat != null ? pawn.GetStatValue(recipe.workSpeedStat, true) : 1f;
//   if (recipe.workTableSpeedStat != null && BillGiver is Building_WorkTable)
//       num *= table.GetStatValue(recipe.workTableSpeedStat, true);
//   // ③ 扣工作量
//   workLeft -= num * delta;
//   if (workLeft <= 0f) ReadyForNextToil();
//   ```
//
//   原版进度条（`<DoRecipeWork>b__5`）：`1f - workLeft / recipe.WorkAmountTotal(thing)`，
//   通过 `WithProgressBar(targetIndex, func, ...)` 挂上去（**注意不是 `WithProgressBarToilDelay`**
//   —— 那个按固定 toilDuration 递减，和"工作量"不是一回事）。
//
//   原版 toil 其余部分也照抄：
//     · `defaultCompleteMode = ToilCompleteMode.Never`（完成与否由 workLeft 判定）
//     · `WithEffect(配方 effectWorking)` / `PlaySustainerOrSound(配方 soundWorking)`
//     · `FailOn(...)`（本 mod 没有 bill，所以只保留"目标被销毁就结束"）
//     · `activeSkill = 配方 workSkill`（影响技能经验浮字/学习速度统计）
//
// ★ 与旧 `JobDriver_BinguinAssembleRod` 的分工：
//   · 那个负责"第一次装配"：搬 4 件配件 → 生成半成品 → 排上本 job
//   · 本 job 负责"对半成品干活"：进度记在**半成品身上** ⇒ 可反复中断、任何人续接
// ============================================================================

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class JobDriver_BinguinWorkOnUnfinishedRod : JobDriver
    {
        private CompBinguinUnfinishedRod RodComp
        {
            get
            {
                Thing t = job.targetA.Thing;
                return t != null ? t.TryGetComp<CompBinguinUnfinishedRod>() : null;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 半成品本体必须能预定（别人同时在干就排队）
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);

            CompBinguinUnfinishedRod comp = RodComp;
            if (comp == null)
            {
                Log.Warning("[冰鹅族] 半成品工作 job：目标没有 CompBinguinUnfinishedRod ⇒ 直接结束。");
                yield break;
            }
            if (!comp.HasAllParts)
            {
                Log.Warning("[冰鹅族] 半成品工作 job：配件不齐（" + comp.PartsSummary() + "）⇒ 直接结束。");
                Messages.Message("Binguin_WorkOnRod_PartsIncomplete".Translate(),
                    job.targetA.Thing, MessageTypeDefOf.RejectInput, false);
                yield break;
            }

            // 品质在第一次开工时抽定（之后固定，避免"甲干一半乙收尾"改品质）
            comp.RollQualityIfNeeded(pawn);
            // ★ 绑定制作者（2026-10-06 用户需求「记得绑定制作者」）：
            //   走到这一步说明已经通过了 WorkGiver / 手动派活的筛选
            //   ⇒ 把绑定确认为当前这个人（第一个来做的人成为制作者）。
            if (comp.boundMaker == null)
            {
                comp.boundMaker = pawn;
                Log.Message("[冰鹅族] 半成品已绑定制作者：" + pawn.LabelShort);
            }

            // ---- 走到干活位置 ----
            Thing workTable = WorkTable();
            IntVec3 goal;
            bool goalIsCell;
            if (workTable != null && workTable.Spawned)
            {
                goal = workTable.InteractionCell;
                goalIsCell = true;
            }
            else
            {
                goal = job.targetA.Thing != null ? job.targetA.Thing.Position : pawn.Position;
                goalIsCell = false;
            }

            Toil go = goalIsCell
                ? Toils_Goto.GotoCell(goal, PathEndMode.OnCell)
                : Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            // ★★ 2026-10-06 踩坑（写在这里防止再犯）：**不要**在真实 toil 前面插一个
            //    自制的 `ToilCompleteMode.Instant` toil 当"轨迹"。`DriverTick` 对 Instant 的
            //    处理是"本 tick 立刻 ReadyForNextToil()"，那个包装 toil 会**抢在 goto
            //    开始寻路之前结束整个 job** ⇒ 一个 tick 内 job 被反复重建、
            //    小人一步不动（实测 95 次重建 + 日志 `Happy started 10 jobs in one tick`）。
            yield return go;

            // ================================================================
            // 干活 toil —— 复刻原版 `Toils_Recipe.DoRecipeWork`
            // ================================================================
            Toil work = new Toil();

            // `workLeft` 用闭包局部变量（原版存在 JobDriver_DoBill 的字段里；
            //  本 mod 的进度存在**半成品 comp** 上，见下面的同步逻辑）
            float[] workLeftBox = new float[1];

            work.initAction = delegate
            {
                CompBinguinUnfinishedRod c = RodComp;
                if (c == null)
                {
                    return;
                }
                // 原版：`workLeft = bill.GetWorkAmount(thing)`；
                //      若已有 UnfinishedThing 且已初始化，则从它的 `workLeft` 续上
                // ⇒ 这里等价于"从半成品身上记着的进度续上"
                workLeftBox[0] = c.WorkAmountBase - c.workDone;
                Log.Message("[冰鹅族] 开始装配半成品：已有进度 " + c.workDone.ToString("0")
                    + " / " + c.WorkAmountBase.ToString("0")
                    + "，本次剩余 " + workLeftBox[0].ToString("0")
                    + "（" + pawn.LabelShort + "，速度 ×"
                    + c.WorkSpeedMultiplier(pawn, WorkTable()).ToString("0.00") + "）");
            };

            // ★ 原版是 `tickIntervalAction`（每 N tick 一次、带 delta）；
            //   而**进度条每 tick 都要刷新**，所以放在 `tickAction` 里、
            //   自己用 `Gen.IsHashIntervalTick` 控制"真正扣工作量/学技能"的节奏，
            //   这样进度条平滑、机制与原版一致。
            const int Interval = 10;
            work.tickAction = delegate
            {
                CompBinguinUnfinishedRod c = RodComp;
                if (c == null || c.parent == null || c.parent.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (!Gen.IsHashIntervalTick(pawn, Interval))
                {
                    return;
                }
                int delta = Interval;

                // ① 技能成长（原版：0.1 * workSkillLearnFactor * delta）
                if (CompBinguinUnfinishedRod.RecipeWorkSkill != null && pawn.skills != null)
                {
                    pawn.skills.Learn(CompBinguinUnfinishedRod.RecipeWorkSkill,
                        0.1f * JobDriver_BinguinWorkOnUnfinishedRod.WorkSkillLearnFactor * delta,
                        false, false);
                }

                // ② 速度乘数（技能加成通过 workSpeedStat 的 StatPart 自动进来）
                float speed = c.WorkSpeedMultiplier(pawn, WorkTable());

                // ③ 扣工作量
                workLeftBox[0] -= speed * delta;

                // ④ 同步到半成品（进度跟着物品走 ⇒ 中断/换人/搬走都不丢）
                float done = c.WorkAmountBase - workLeftBox[0];
                if (done < 0f) done = 0f;
                c.workDone = done;

                if (workLeftBox[0] <= 0f)
                {
                    c.workDone = c.WorkAmountBase;   // 确保收尾判定通过
                    Log.Message("[冰鹅族] 半成品装配完成，进入收尾（" + pawn.LabelShort + "）");
                    ReadyForNextToil();
                }
            };

            work.defaultCompleteMode = ToilCompleteMode.Never;
            // 原版：`WithEffect(recipe.effectWorking)`；本 mod 沿用金属加工特效
            work.WithEffect(EffecterDefOf.ConstructMetal, TargetIndex.A);
            work.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            // 原版进度条：`1f - workLeft / WorkAmountTotal(thing)`
            work.WithProgressBar(TargetIndex.A, delegate
            {
                CompBinguinUnfinishedRod c = RodComp;
                if (c == null)
                {
                    return 0f;
                }
                float total = c.WorkAmountBase;
                if (total <= 0f)
                {
                    return 1f;
                }
                float p = 1f - workLeftBox[0] / total;
                if (p < 0f) p = 0f;
                if (p > 1f) p = 1f;
                return p;
            }, false, -0.5f, false);
            // 原版：`activeSkill = recipe.workSkill`（影响技能相关表现）
            work.activeSkill = delegate
            {
                return CompBinguinUnfinishedRod.RecipeWorkSkill;
            };
            yield return work;

            // ---- 收尾：把半成品变成真正的钓竿 ----
            yield return Toils_General.Do(delegate
            {
                CompBinguinUnfinishedRod c = RodComp;
                if (c != null)
                {
                    c.FinishIntoRod(pawn);
                }
            });
        }

        /// <summary>
        /// 原版 `RecipeDef.workSkillLearnFactor` 的默认值。
        /// ★ 本 mod 的配件 `recipeMaker` 里没写这一项 ⇒ 用原版字段默认值。
        ///   （`Verse.RecipeDef.workSkillLearnFactor` 的 XML 默认就是 0.1，
        ///     与 `DoRecipeWork` 里 `0.1f * factor * delta` 的 0.1 是两个不同的系数。）
        /// </summary>
        public const float WorkSkillLearnFactor = 1f;

        /// <summary>
        /// 干活时所在的装配台。
        /// ★★ 顺序（2026-10-06 定稿）：半成品生成在装配台的**交互格**上
        ///   （不在台子那一格），所以 `GetEdifice` 在它自己那格**找不到台子**。
        ///   ⇒ 优先用 comp 里记下的 `workTable` 引用（生成时写进去、进存档），
        ///     找不到再退回"扫自己格 + 邻近 8 格"。
        /// </summary>
        private Thing WorkTable()
        {
            // ① comp 里记着的（最可靠）
            CompBinguinUnfinishedRod c = RodComp;
            if (c != null && c.workTable != null && c.workTable.Spawned
                && c.workTable.Destroyed == false)
            {
                return c.workTable;
            }

            Thing rod = job.targetA.Thing;
            if (rod == null || !rod.Spawned || rod.Map == null)
            {
                return null;
            }
            Map map = rod.Map;
            Building here = rod.Position.GetEdifice(map);
            if (here != null && here.TryGetComp<CompBinguinRodAssembly>() != null)
            {
                return here;
            }
            // ② 邻近 8 格
            for (int i = 0; i < 8; i++)
            {
                IntVec3 cc = rod.Position + GenAdj.AdjacentCells[i];
                if (!cc.InBounds(map)) continue;
                Building b = cc.GetEdifice(map);
                if (b != null && b.TryGetComp<CompBinguinRodAssembly>() != null)
                {
                    return b;
                }
            }
            return here;
        }
    }
}
