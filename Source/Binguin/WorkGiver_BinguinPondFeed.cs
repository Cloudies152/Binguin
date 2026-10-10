// ============================================================================
// 鱼池鱼食投喂 WorkGiver —— 殖民者自动把筛选允许的食物搬进鱼池鱼食槽
// （2026-08-20 用户需求：鱼食放入像打窝用具一样手动选择）
//
// 机制（同打窝用具 WorkGiver_BinguinFillBait）：
//   - 玩家选中鱼池 → 「存储」标签（ITab_Storage）手动筛选允许投入的食物
//     （默认 Foods = 一切食物）—— 与打窝用具完全相同的操作方式
//   - PotentialWorkThingsGlobal：地图上所有鱼食槽未满的鱼池
//   - HasJobOnThing：该池鱼食槽还能装下营养 且 能找到符合筛选的食物
//   - JobOnThing：JobDefOf.HaulToContainer（targetA=食物，targetB=鱼池），
//     job.count = 不超过剩余营养容量的数量（原版 HaulToContainer 会自动
//     把食物放进建筑的 CompThingContainer 容器）
//   - 每个相连鱼池有自己的鱼食槽（营养上限 5）；组长统一从组内各槽消耗
//   - priorityInType=950 → 只在搬运工空闲时投喂，不抢占紧急搬运
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
    public class WorkGiver_BinguinPondFeed : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode
        {
            get { return PathEndMode.Touch; }
        }

        // ★ 2026-09 性能：RimWorld 对同一个目标会先调 HasJobOnThing 再调 JobOnThing，
        //   两者各自 FindFood 一次 = 同一 tick 扫两遍全图可搬运物。这里记住本次
        //   结果，仅当「同一 pawn + 同一目标 + 同一 tick + 物品仍可搬」时才复用。
        private Thing lastFood;
        private CompBinguinPondFeed lastFoodFeed;
        private Pawn lastFoodPawn;
        private int lastFoodTick = -1;

        // ★ 2026-09 性能：pond def 名是常量 → 懒加载缓存（本方法是迭代器，
        //   每次工作扫描重新枚举，原来每次都查一次 DefDatabase）。
        private static ThingDef cachedPondDef;

        private static ThingDef PondDef
        {
            get
            {
                if (cachedPondDef == null)
                {
                    cachedPondDef = DefDatabase<ThingDef>.GetNamedSilentFail("Binguin_FishPond");
                }
                return cachedPondDef;
            }
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            ThingDef pondDef = PondDef;
            if (pondDef == null || pawn.Map == null)
            {
                yield break;
            }
            foreach (Thing t in pawn.Map.listerThings.ThingsOfDef(pondDef))
            {
                if (t.Destroyed || t.IsForbidden(pawn))
                {
                    continue;
                }
                if (t.TryGetComp<CompBinguinPondFeed>() != null)
                {
                    yield return t;
                }
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompBinguinPondFeed feed = t.TryGetComp<CompBinguinPondFeed>();
            if (feed == null)
            {
                return false;
            }
            // ★ 2026-09 性能：NutritionCapacityLeft 是 O(容器堆数) 的属性，
            //   原来 HasJobOnThing / JobOnThing / FindFood 各算一次（同一 tick
            //   同一目标算三遍）→ 现在算一次往下传。
            float left = feed.NutritionCapacityLeft;
            if (left <= 0.01f)
            {
                return false;
            }
            if (!pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Deadly))
            {
                return false;
            }
            return FindFood(pawn, feed, left) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompBinguinPondFeed feed = t.TryGetComp<CompBinguinPondFeed>();
            if (feed == null)
            {
                return null;
            }
            float left = feed.NutritionCapacityLeft;
            Thing food = null;
            if (lastFoodFeed == feed && lastFoodPawn == pawn
                && lastFoodTick == GenTicks.TicksGame
                && lastFood != null && !lastFood.Destroyed && !lastFood.IsForbidden(pawn))
            {
                food = lastFood;
            }
            if (food == null)
            {
                food = FindFood(pawn, feed, left);
            }
            if (food == null)
            {
                return null;
            }
            float nut = food.def.GetStatValueAbstract(StatDefOf.Nutrition, food.Stuff);
            if (nut <= 0.001f)
            {
                return null;
            }
            // 只搬不超过剩余营养容量的数量（放不下那么多就留在原地）
            int take = Mathf.Min(food.stackCount, Mathf.CeilToInt(left / nut));
            if (take <= 0)
            {
                return null;
            }
            Job job = JobMaker.MakeJob(JobDefOf.HaulToContainer, food, t);
            job.count = take;
            return job;
        }

        // 全图找一个可自动搬运的可食用物品（尊重玩家在该鱼池「存储」标签里的筛选）
        private Thing FindFood(Pawn pawn, CompBinguinPondFeed feed, float left)
        {
            StorageSettings storeSettings = feed.GetStoreSettings();
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
                    lastFoodFeed = feed;
                    lastFoodPawn = pawn;
                    lastFoodTick = GenTicks.TicksGame;
                    return food;
                }
            }
            return null;
        }
    }
}
