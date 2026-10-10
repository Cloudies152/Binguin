// ============================================================================
// 打窝投喂 WorkGiver —— 殖民者自动把任意食物搬进打窝用具
//
// 机制（2026-08-19）：
//   - PotentialWorkThingsGlobal：地图上所有未满的打窝用具
//   - HasJobOnThing：还能装下营养 且 能找到可搬运的食物
//   - JobOnThing：JobDefOf.HaulToContainer（targetA=食物，targetB=打窝用具），
//     job.count = 不超过剩余营养容量的数量（超出的部分留在原地不搬，
//     避免把整堆全搬进去超容量）。
//   - 找食物：全图 HaulableEver 中可食用的（IsNutritionGivingIngestible）
//     且小人能自动搬运的；食物来源优先级不高（本 WorkGiver 的
//     priorityInType=50，低于原版搬运 90，只在空闲时投喂）。
//
// ★ giverClass 由 BinguinDefPatches 静态构造替换（XML 零自定义类型）。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Binguin
{
    public class WorkGiver_BinguinFillBait : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode
        {
            get { return PathEndMode.Touch; }
        }

        // ★ 2026-09 性能：def 名是常量 → 懒加载缓存 + 引用比较（原为逐建筑字符串比较）
        private static ThingDef cachedBaitDef;
        private static bool baitDefLookedUp;

        private static ThingDef BaitDef
        {
            get
            {
                if (!baitDefLookedUp)
                {
                    baitDefLookedUp = true;
                    cachedBaitDef = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_FishingBait");
                }
                return cachedBaitDef;
            }
        }

        // ★ 2026-09 性能：同一 tick 内 HasJobOnThing → JobOnThing 会对同一个目标
        //   各扫一遍全图可搬运物（HaulableEver）。这里记住本次找食物用的
        //   (目标, tick, 结果)，JobOnThing 直接复用；目标被拿走/禁止时会自动失效。
        private Thing lastFood;
        private Thing lastFoodFor;
        private Pawn lastFoodPawn;
        private int lastFoodTick = -1;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            ThingDef baitDef = BaitDef;
            foreach (Thing t in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (t.def == baitDef
                    && !t.IsForbidden(pawn)
                    && t.TryGetComp<CompBinguinFishingBait>() != null)
                {
                    yield return t;
                }
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompBinguinFishingBait comp = t.TryGetComp<CompBinguinFishingBait>();
            if (comp == null)
            {
                return false;
            }
            // ★ 2026-09 性能：NutritionCapacityLeft 是 O(容器堆数) 属性，原来
            //   HasJobOnThing / JobOnThing / FindFood 各算一次 → 现在只算一次。
            float left = comp.NutritionCapacityLeft;
            if (left <= 0.01f)
            {
                return false;
            }
            if (!pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Deadly))
            {
                return false;
            }
            return FindFood(pawn, comp, left, t) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompBinguinFishingBait comp = t.TryGetComp<CompBinguinFishingBait>();
            if (comp == null)
            {
                return null;
            }
            float left = comp.NutritionCapacityLeft;
            Thing food = FindFood(pawn, comp, left, t);
            if (food == null)
            {
                return null;
            }
            float nut = food.def.GetStatValueAbstract(StatDefOf.Nutrition, food.Stuff);
            if (nut <= 0.001f)
            {
                return null;
            }
            // 只搬不超过剩余营养容量的数量
            int take = Mathf.Min(food.stackCount, Mathf.CeilToInt(left / nut));
            if (take <= 0)
            {
                return null;
            }
            Job job = JobMaker.MakeJob(JobDefOf.HaulToContainer, food, t);
            job.count = take;
            return job;
        }

        // 全图找一个可自动搬运的可食用物品（尊重玩家的食物筛选）
        private Thing FindFood(Pawn pawn, CompBinguinFishingBait comp, float left, Thing forThing)
        {
            // 同一 tick 内已经为这个目标找过 → 直接复用（前提是它还在、还能搬）
            if (lastFoodFor == forThing && lastFoodPawn == pawn
                && lastFoodTick == GenTicks.TicksGame
                && lastFood != null && !lastFood.Destroyed && !lastFood.IsForbidden(pawn))
            {
                return lastFood;
            }
            StorageSettings storeSettings = comp.GetStoreSettings();
            foreach (Thing food in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (food == null || food.def == null || food.IsForbidden(pawn))
                {
                    continue;
                }
                if (!food.def.IsNutritionGivingIngestible)
                {
                    continue;
                }
                // ★ 玩家在 ITab_Storage 里筛选允许投入的食物（默认 Foods 类目）
                if (storeSettings != null && !storeSettings.AllowedToAccept(food))
                {
                    continue;
                }
                if (!HaulAIUtility.PawnCanAutomaticallyHaul(pawn, food, false))
                {
                    continue;
                }
                float nut = food.def.GetStatValueAbstract(StatDefOf.Nutrition, food.Stuff);
                if (nut <= 0.001f)
                {
                    continue;
                }
                // 该堆中至少能放得下 1 个
                if (nut <= left + 0.01f)
                {
                    // 记住本次结果：同一 tick 的 JobOnThing 可直接复用（省一次全图扫描）
                    lastFood = food;
                    lastFoodFor = forThing;
                    lastFoodPawn = pawn;
                    lastFoodTick = GenTicks.TicksGame;
                    return food;
                }
            }
            return null;
        }
    }
}
