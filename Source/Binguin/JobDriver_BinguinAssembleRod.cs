// ============================================================================
// 装配高级钓竿 JobDriver（第一次装配）—— 搬配件 → 生成【钓竿（未完成）】→ 交给工作 job
//
// JobDef = Binguin_AssembleRod（targetA = 装配台）。
// driverClass 由 BinguinDefPatches 静态构造挂载（XML 零自定义类型）。
//
// ★★ 2026-10-06 流程大改（用户需求：「应该是搬运后会变成一个 钓竿（未完成）
//   这样可以让殖民者不用一次干完」）：
//   旧流程：搬配件（最多 2 件）→ 走到装配台 → **一口气干 10000 tick** → 出成品。
//          问题：中途被打断/换人 = 全白干。
//   新流程：
//     ① 把 4 件配件逐个搬到装配台
//     ② 生成【钓竿（未完成）】(ThingDef `Binguin_UnfinishedRod`)，
//        4 件配件被**装进它内部的容器**（`CompBinguinUnfinishedRod`，不会有两份）
//     ③ 自动排上 `Binguin_WorkOnUnfinishedRod` 开始干活
//        —— 那个 job 的进度记在**半成品身上**，所以：
//           征召 / 吃饭 / 袭击 / 换人 / 把半成品搬去别处，进度都不丢。
//
// ★ 搬运目标格仍是装配台的 `InteractionCell`，但放下时改为**装进半成品**。
//
// ★★ `Verse.AI.TargetIndex` 只有 None/A/B/C（枚举打印实证）⇒ 3 个槽：
//      A = 装配台，B/C = 第 1、2 件配件。
//      所以**这一版仍然只自动搬 2 件**（竿身、竿头），
//      剩下 2 件（鱼钩、鱼饵）由 `SpawnUnfinishedRod()` 直接从地图装进半成品容器
//      —— 功能完整（成品属性一个不少），只是少了那两趟搬运动画。
//      ★ 想要 4 件都真搬，需要给搬运 job 另开 target 体系（或分批派 job），
//        属于后续可选优化，不影响功能正确性。
//
// ★ 关于本 job 里不能再用 `Toils_Haul.PlaceHauledThingInCell` 的原因（见旧注释）：
//   它的 IL 是 `curJob.GetTarget(cellInd).Cell`（传装配台会得到**建筑自己那格**），
//   且 `storageMode=false` 时放失败会走到 `Incomplete haul ... Destroying` 分支
//   —— **直接销毁搬来的配件**。现在改为"装进半成品容器"，绕开了整个问题。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace Binguin
{
    public class JobDriver_BinguinAssembleRod : JobDriver
    {
        /// <summary>能参与自动搬运的配件槽位（A 被装配台占用，故只剩 B、C）。</summary>
        private static readonly TargetIndex[] PartSlots = { TargetIndex.B, TargetIndex.C };

        /// <summary>与 PartSlots 一一对应的配件（在 TryMakePreToilReservations 里填好）。</summary>
        private readonly List<Thing> partsToHaul = new List<Thing>();
        /// <summary>本次要搬到装配台的高级零部件（在 TryMakePreToilReservations 里预定好）。</summary>
        private readonly List<Thing> spacersToHaul = new List<Thing>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 装配台必须能预定
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }

            partsToHaul.Clear();

            CompBinguinRodAssembly comp = AssemblyComp();
            if (comp == null)
            {
                return true;
            }
            // 顺序与 BinguinRodPartPicker 一致：竿身 / 竿头 / 鱼钩 / 鱼饵
            Thing[] parts = { comp.pendingShaft, comp.pendingTip, comp.pendingHook, comp.pendingBait };
            //int slot = 0;
            //for (int i = 0; i < parts.Length && slot < PartSlots.Length; i++)
            //{
            //    Thing part = parts[i];
            //    if (part == null || part.Destroyed)
            //    {
            //        continue;
            //    }
            //    job.SetTarget(PartSlots[slot], part);
            //    // 能预定就预定；被别人占着就不预定（toil 会 FailOn，不会整个派不下来）
            //    pawn.Reserve(part, job, 1, -1, null, false);
            //    partsToHaul.Add(part);
            //    slot++;
            //}
            // 不再固定绑定 PartSlots（B/C 只有两个），4 件全部进 partsToHaul，
            //   搬运循环里用 Toils_General.Do 在运行时把 job.targetB 指向当前那件。
            for (int i = 0; i < parts.Length; i++)
            {
                Thing part = parts[i];
                if (part == null || part.Destroyed)
                {
                    continue;
                }
                // 能预定就预定；被别人占着就不预定（toil 会 FailOn，不会整个派不下来）
                pawn.Reserve(part, job, 1, -1, null, false);
                partsToHaul.Add(part);
            }

            // ★ 合并时补的修正（程序员原版没有这一步）：
            //   把要搬的 4 个高级零部件【也预定掉】。
            //   不预定的话，多个人同时装配时会互抢同一堆零部件 ——
            //   toil 里的 FailOnDespawnedOrNull 会让这个 job 直接失败。
            //   与配件那边同规则：maxPawns=1、stackCount=-1（预定整堆）。
            // ★★ 2026-10-08（用户要求"回到程序员那版，只改零部件一起搬"）：
            //   配件照旧在这里预定；零部件**不在这里预定** ——
            //   它们跟着配件走同一个搬运循环，由 `Toils_Haul.StartCarryThing`
            //   自己在拿的时候预定（这是程序员原版 4 件配件能跑通的做法）。
            //   ★ 我之前在这里额外预定零部件 + 另写一个搬运段，实测会把 job 卡死。
            spacersToHaul.Clear();
            const int SpacersNeeded = 4;
            // ★★★ 2026-10-08 根因修复（诊断日志实测）：
            //   原来按"地图扫描顺序"取零部件 ⇒ 实测先取到了仓库里那堆
            //   在 **(62, 0, 154)** 的旧货，而装配台在 **(158, 0, 143)** —— 相隔约 97 格，
            //   小人走到一半就卡住（日志停在 `零件部[0]/走过去`，没有"拿起"、没有异常）。
            //   ⇒ 改成：
            //     ① 按**离装配台的距离**排序，先搬最近的那堆
            //     ② 距离超过 `MaxHaulDist`（40 格）的**干脆不搬** ——
            //        真正扣料时是 `ConsumeComponents(4)` 从**整张地图**找的，
            //        搬不搬都能扣到，没必要让小人跑半个地图。
            const float MaxHaulDist = 40f;
            IntVec3 tablePos = comp.parent != null ? comp.parent.Position : IntVec3.Invalid;
            List<Thing> spAll = FindAllSpacers(comp.parent != null ? comp.parent.Map : null);
            if (tablePos.IsValid)
            {
                spAll.Sort(delegate (Thing a, Thing b)
                {
                    float da = (a.Position - tablePos).LengthHorizontalSquared;
                    float db = (b.Position - tablePos).LengthHorizontalSquared;
                    return da.CompareTo(db);
                });
            }
            int reserved = 0;
            for (int i = 0; i < spAll.Count && reserved < SpacersNeeded; i++)
            {
                Thing sp = spAll[i];
                if (sp == null || sp.Destroyed) continue;
                if (tablePos.IsValid
                    && (sp.Position - tablePos).LengthHorizontalSquared > MaxHaulDist * MaxHaulDist)
                {
                    Log.Message("[冰鹅族] 零部件 " + sp.LabelShort + " @ " + sp.Position
                        + " 离装配台太远（>" + MaxHaulDist + " 格），不搬 —— 扣料时仍会从地图上扣。");
                    continue;
                }
                spacersToHaul.Add(sp);
                reserved += Mathf.Min(SpacersNeeded - reserved, sp.stackCount);
            }
            return true;
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);

            CompBinguinRodAssembly comp = AssemblyComp();
            if (comp == null)
            {
                Log.Warning("[冰鹅族] 装配 job：装配台上没有 CompBinguinRodAssembly，直接结束。");
                yield break;
            }

            Log.Message("[冰鹅族] 装配钓竿开始（第一次）：待搬配件 " + partsToHaul.Count + " 件（"
                + pawn.LabelShort + "）");

            // ---- ① 逐个把配件搬到装配台 ----
            //   ★★ 2026-10-06 关键修复（实测红字定位）：
            //      `Toils_Haul.StartCarryThing` 的 `initAction` 会先
            //      `ErrorCheckForCarry`，其中 `availableStackSpace` 是按
            //      `maximum - carrying` 算的（maximum 传的是 job.count = 1）。
            //      ⇒ **小人手上还拿着第 1 件时去拿第 2 件，availableStackSpace = 0**，
            //        直接抛 `System.Exception: StartCarryThing got availableStackSpace 0`
            //        ⇒ job 崩掉（这正是用户报的"搬了两件就卡住"）。
            //      ⇒ 每搬完一件必须**先把手里的放下**，再去拿下一件。
            //for (int i = 0; i < partsToHaul.Count; i++)
            //{
            //    Thing part = partsToHaul[i];
            //    if (part == null || part.Destroyed)
            //    {
            //        continue;
            //    }
            //    TargetIndex captured = PartSlots[i];
            //    //每次搬运前重置，防止被上一次搬运减到0
            //    job.count = 1;

            //    yield return Toils_Goto.GotoThing(captured, PathEndMode.ClosestTouch)
            //        .FailOnDespawnedOrNull(captured);
            //    yield return Toils_Haul.StartCarryThing(captured, false, false, true)//测试用第三个true改成false
            //        .FailOnDespawnedOrNull(captured);
            //    yield return Toils_Haul.CarryHauledThingToCell(TargetIndex.A);
            //    // ★ 放下（自定义，见 ToilDropAtTable：**绝不销毁**，绕开原版
            //    //   `PlaceHauledThingInCell` 的 storageMode=false 销毁分支）
            //    yield return ToilDropAtTable();
            //}

            for (int i = 0; i < partsToHaul.Count; i++)
            {
                Thing part = partsToHaul[i];
                if (part == null || part.Destroyed)
                {
                    continue;
                }
                Thing captured = part;    // 捕获当前这一件

                yield return Toils_General.Do(delegate
                {
                    if (captured == null || captured.Destroyed) return;
                    job.SetTarget(TargetIndex.B, captured);   //  运行时把 B 指向它
                    job.count = 1;
                });
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                    .FailOnDespawnedOrNull(TargetIndex.B);
                yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, false, true)
                    .FailOnDespawnedOrNull(TargetIndex.B);
                // ★★ 2026-10-09 用 RimSage 查原版源码后确认的一条**隐含前提**：
                //   `Toils_Haul.CarryHauledThingToCell`（Verse.AI/Toils_Haul.cs L254-294）
                //   有三个 failCondition，其中第二个是：
                //     if (job.haulMode == HaulMode.ToCellStorage
                //         && !cell.IsValidStorageFor(map, CarriedThing)) return true;
                //   ⇒ **若把 job.haulMode 设成 ToCellStorage，而装配台不是储存格
                //     ⇒ 每次搬运都会立刻失败 ⇒ 整个装配流程全挂！**
                //   当前之所以没事：`Job.haulMode` 是 `HaulMode` 枚举字段
                //   （`Verse.AI/Job.cs:57`，枚举 Undefined=0）
                //   ⇒ **默认值 Undefined**，永远不等于 ToCellStorage ⇒ 该条件不触发。
                //   ⚠️ **所以千万别给这个 job 设 haulMode = ToCellStorage**（新写 job 时同理）。
                yield return Toils_Haul.CarryHauledThingToCell(TargetIndex.A);
                yield return ToilDropAtTable();
            }
            //  把 4 个高级零部件**和配件一样**搬到装配台旁 ----
            // ★★ 2026-10-08 用户需求：「只改掉搬过来零部件时，和其他配件一起合成半成品」。
            //   ⇒ 零部件不再单独一段搬，而是**接在配件后面、用完全相同的模式**：
            //        Toils_General.Do 设 targetB → GotoThing → StartCarryThing
            //        → CarryHauledThingToCell → ToilDropAtTable
            //   搬完之后，`SpawnUnfinishedRod()` 会把它们**和 4 件配件一起**
            //   吸进半成品容器（见 `Comp_BinguinUnfinishedRod.Accepts` 的白名单）。
            //
            // ★★★ 2026-10-09 修复（用户报「搬高级零部件只搬一个，却消耗四个」）★★★
            //
            // 【旧代码的 bug】
            //   循环体里是 `job.count = 1;`（一次只搬 1 个），
            //   但累加写的是 `hauledSoFar += fromThis;`，而
            //     `fromThis = Mathf.Min(4 - hauledSoFar, 那一堆的数量)`
            //   ⇒ **只要那一堆本身有 ≥4 个**，第一轮 `fromThis` 就等于 4，
            //     循环条件 `hauledSoFar < 4` 立刻为假 ⇒ **直接跳出循环**
            //   ⇒ 结果：**实际只搬了 1 个（`job.count = 1` 的产物），
            //     但计数认为已经搬够 4 个**。这就是"只搬一个"的根因。
            //   （原来的注释还写着"job.count 用 1，和配件那条能跑通的路径完全一致"——
            //     配件那边没事是因为配件是 4 个**不同**的 def，每个循环只搬固定的一件；
            //     而零部件是**同一 def 的可堆叠物品**，一堆就有好几个，才会踩到这个坑。）
            //
            // 【修法】下面两处一起改，缺一不可：
            //   ① `job.count` 改成**这一轮真正需要的数量**（不再恒为 1）
            //      ⇒ `Toils_Haul.StartCarryThing` 会拿 min(需要, 那一堆有几个)，
            //        同一堆有 4 个就一次拿走 4 个 —— 这才是"四个一起搬"
            //   ② `hauledSoFar` 改成按**实际拿到手的数量**累加（在拿起后的 toil 里读
            //      `carryTracker.CarriedThing.stackCount`），
            //      不再用 `fromThis` 预估值 —— 预估与实际一旦不一致就会漏搬
            const int SpacersToHaul = 4;
            int hauledSoFar = 0;
            for (int si = 0; si < spacersToHaul.Count && hauledSoFar < SpacersToHaul; si++)
            {
                Thing sp = spacersToHaul[si];
                if (sp == null || sp.Destroyed)
                {
                    continue;
                }
                int need = SpacersToHaul - hauledSoFar;
                if (need <= 0)
                {
                    break;
                }
                if (sp.stackCount <= 0)
                {
                    continue;
                }

                Thing capturedSp = sp;
                yield return Toils_General.Do(delegate
                {
                    if (capturedSp == null || capturedSp.Destroyed) return;
                    int left = SpacersToHaul - hauledSoFar;
                    if (left <= 0) return;
                    job.SetTarget(TargetIndex.B, capturedSp);
                    // ★ 按"还需要几个"取，但仍受这一堆的数量限制
                    job.count = Mathf.Clamp(Mathf.Min(left, capturedSp.stackCount), 1, capturedSp.stackCount);
                });
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                    .FailOnDespawnedOrNull(TargetIndex.B);
                yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, false, true)
                    .FailOnDespawnedOrNull(TargetIndex.B);
                // ★ 用 toil 读"实际拿到几个"，回写到 hauledSoFar（按实际而非预估累加）
                yield return Toils_General.Do(delegate
                {
                    Thing c = pawn.carryTracker != null ? pawn.carryTracker.CarriedThing : null;
                    int got = (c != null && c.def != null && c.def.defName == "ComponentSpacer")
                        ? c.stackCount : 0;
                    hauledSoFar += got;
                });
                yield return Toils_Haul.CarryHauledThingToCell(TargetIndex.A);
                yield return ToilDropAtTable();
            }


            // ---- ② 生成【钓竿（未完成）】并把配件装进去 ----
            //   ★ 必须等搬运做完再生成：`SpawnUnfinishedRod()` 会把 pending 里
            //     **剩余的**配件（含没搬的那两件）一并从地图装进容器。
            //   ★ 放在一个 Instant toil 里做，失败也不至于把 job 卡死。
            yield return Toils_General.Do(delegate
            {
                CompBinguinRodAssembly c = AssemblyComp();
                if (c == null)
                {
                    return;
                }
                Thing rod = c.SpawnUnfinishedRod();
                if (rod == null)
                {
                    Messages.Message("Binguin_AssembleRod_SpawnFailed".Translate(),
                        job.targetA.Thing, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                // ③ 立刻排上"对半成品干活"的 job
                CompBinguinRodAssembly.StartWorkOnRod(pawn, rod);
            });
        }

        private CompBinguinRodAssembly AssemblyComp()
        {
            Thing t = job.targetA.Thing;
            return t != null ? t.TryGetComp<CompBinguinRodAssembly>() : null;
        }
        /// <summary>扫地图上所有高级零部件（ComponentSpacer）。</summary>
        private static List<Thing> FindAllSpacers(Map map)
        {
            List<Thing> result = new List<Thing>();
            if (map == null) return result;
            var all = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.def == null || t.Destroyed || !t.Spawned) continue;
                if (t.def.defName != "ComponentSpacer") continue;
                result.Add(t);
            }
            return result;
        }
        /// <summary>
        /// 把手上搬着的东西放到装配台旁边（**绝不销毁**）。
        ///
        /// ★ 三级回退（2026-10-06 实测 `ThingPlaceMode.Near` 在这个场景会失败：
        ///   小人正站在装配台 `InteractionCell` 上，`Near` 需要在目标格**周围**
        ///   另找一个能放东西的格子，常常找不到）：
        ///     ① `TryDropCarriedThing(preferred, ThingPlaceMode.Near, ...)`（原版做法）
        ///     ② 仍失败 → 直接放到 `preferred` 那一格（`ThingPlaceMode.Direct`）
        ///     ③ 再失败 → 放到小人脚下（`Direct`）
        ///   全都失败才保持拿着（不销毁）。
        ///
        /// ★★ 关于"原版会不会销毁东西"（我上一版注释写错了，这里更正，IL 实证）：
        ///   `Toils_Haul.PlaceHauledThingInCell` 的 `initAction` IL：
        ///     `IL_0114 TryDropCarriedThing` → `IL_011F brtrue` 成功即返回；
        ///     `IL_0124 storageMode == false` → 直接跳到结尾（**不销毁**）；
        ///     `IL_0267 ... Destroying` 只在 **storageMode == true** 且
        ///     连 `HaulAIUtility.CanHaulAside` 都找不到位置时才发生。
        ///   也就是说：storageMode=false 时它并不销毁东西。真正的问题是
        ///   "`Near` 模式找不到落点"，不是销毁。
        /// </summary>
        private Toil ToilDropAtTable()
        {
            Toil toil = new Toil();
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                if (actor == null)
                {
                    return;
                }
                Thing carried = actor.carryTracker != null ? actor.carryTracker.CarriedThing : null;
                if (carried == null)
                {
                    return;
                }
                Map map = actor.Map;
                if (map == null)
                {
                    return;
                }

                Building table = job.targetA.Thing as Building;
                IntVec3 preferred = table != null ? table.InteractionCell : job.targetA.Cell;

                Thing placed = null;
                string how = "";

                // ① 原版做法：目标格 + Near（会找旁边的空格）
                if (preferred.IsValid && preferred.InBounds(map))
                {
                    if (actor.carryTracker.TryDropCarriedThing(preferred, ThingPlaceMode.Near, out placed, null))
                    {
                        how = "Near@" + preferred;
                    }
                }
                // ② 直接放到目标格本身
                if (placed == null && preferred.IsValid && preferred.InBounds(map))
                {
                    if (actor.carryTracker.TryDropCarriedThing(preferred, ThingPlaceMode.Direct, out placed, null))
                    {
                        how = "Direct@" + preferred;
                    }
                }
                // ③ 放到小人脚下
                if (placed == null)
                {
                    if (actor.carryTracker.TryDropCarriedThing(actor.Position, ThingPlaceMode.Direct, out placed, null))
                    {
                        how = "Direct@" + actor.Position + "(脚下)";
                    }
                }

                if (placed != null)
                {
                    Log.Message("[冰鹅族] 配件已放下：" + placed.LabelShort + " → " + how);
                }
                else
                {
                    // ④ 全失败：保持拿着（绝不销毁）。后面的 SpawnUnfinishedRod 仍能
                    //    通过 holder 转移把配件从手上收进半成品，所以功能不受影响。
                    Log.Message("[冰鹅族] 配件暂时没找到落点，继续由 " + actor.LabelShort
                        + " 拿着：" + carried.LabelShort + "（稍后仍会被装进半成品）");
                }
            };
            return toil;
        }
    }
}
