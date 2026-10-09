// ============================================================================
// 鱼池鱼食容器（2026-08-20 用户需求）
// 每个鱼池带一个"鱼食槽"：玩家选中鱼池 → ITab_Storage（存储）标签 →
// 手动筛选允许投入的食物（默认 Foods = 一切食物），WorkGiver 自动投喂。
//   - 营养上限 5 / 池（超出由 EjectExcess 丢回地面，同打窝用具）
//   - 实际"消耗 0.5/天 给繁殖 +50%"由组长 CompBinguinFishPond 的 Rare tick
//     统一执行：扫描组内容器营养，>0 → 繁殖 +50%，并每 60000 tick 从
//     任一有食池容器扣 0.5 营养（模拟鱼吃掉）
// ★ XML 用原版 CompProperties_ThingContainer 占位，静态构造替换
//   compClass → CompBinguinPondFeed（继承 CompThingContainer）。
// ============================================================================

using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin
{
    public class CompBinguinPondFeed : CompThingContainer, IStoreSettingsParent
    {
        public const float MaxNutrition = 5f;

        // 玩家可编辑的食物筛选（ITab_Storage）；默认 = fixedStorageSettings（Foods）
        public StorageSettings allowedFoodSettings;

        public float NutritionLeft
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < innerContainer.Count; i++)
                {
                    Thing food = innerContainer[i];
                    if (food != null && food.def != null)
                    {
                        total += food.def.GetStatValueAbstract(StatDefOf.Nutrition, food.Stuff) * food.stackCount;
                    }
                }
                return total;
            }
        }

        public float NutritionCapacityLeft
        {
            get { return Mathf.Max(0f, MaxNutrition - NutritionLeft); }
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            allowedFoodSettings = new StorageSettings(this);
            if (parent != null && parent.def != null && parent.def.building != null
                && parent.def.building.fixedStorageSettings != null)
            {
                allowedFoodSettings.CopyFrom(parent.def.building.fixedStorageSettings);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look<StorageSettings>(ref allowedFoodSettings, "pondFeedSettings");
        }

        // 从容器中扣除 need 营养（拆堆销毁，模拟被鱼吃掉）；返回实际扣除营养
        public float ConsumeNutrition(float need)
        {
            float consumed = 0f;
            while (need > 0.001f && innerContainer.Count > 0)
            {
                Thing food = innerContainer[0];
                if (food == null || food.def == null)
                {
                    break;
                }
                float nut = food.def.GetStatValueAbstract(StatDefOf.Nutrition, food.Stuff);
                if (nut <= 0.001f)
                {
                    innerContainer.Remove(food);
                    food.Destroy();
                    continue;
                }
                int take = Mathf.CeilToInt(Mathf.Min(need / nut, (float)food.stackCount));
                if (take <= 0)
                {
                    take = 1;
                }
                Thing eaten = food.SplitOff(take);
                if (eaten != null)
                {
                    eaten.Destroy();
                }
                float taken = nut * take;
                consumed += taken;
                need -= taken;
            }
            return consumed;
        }

        // 容量保险（Rare tick 调用，鱼池建筑是 Rare ticker）
        public override void CompTickRare()
        {
            base.CompTickRare();
            if (NutritionLeft > MaxNutrition + 0.01f)
            {
                EjectExcess();
            }
        }

        private void EjectExcess()
        {
            // ★ 2026-09 性能：原实现循环条件里每次都重算 NutritionLeft（O(堆数)
            //   的营养值查询），整体是 O(n²)。现在用局部变量自己维护剩余值，
            //   每轮只做一次减法 → O(n)。
            float left = NutritionLeft;
            while (left > MaxNutrition + 0.01f && innerContainer.Count > 0)
            {
                Thing food = innerContainer[innerContainer.Count - 1];
                if (food == null)
                {
                    break;
                }
                float nut = food.def.GetStatValueAbstract(StatDefOf.Nutrition, food.Stuff);
                if (nut <= 0.001f)
                {
                    innerContainer.Remove(food);
                    food.Destroy();
                    continue;
                }
                int dropCount = Mathf.CeilToInt((left - MaxNutrition) / nut);
                dropCount = Mathf.Clamp(dropCount, 1, food.stackCount);
                Thing dropped = food.SplitOff(dropCount);
                if (dropped != null)
                {
                    GenPlace.TryPlaceThing(dropped, parent.Position, parent.Map, ThingPlaceMode.Near, null);
                }
                // SplitOff 成功移出的就是 dropCount 份 → 剩余营养同步减少
                left -= nut * dropCount;
            }
        }

        // ---------- IStoreSettingsParent（ITab_Storage） ----------
        public bool StorageTabVisible
        {
            get { return true; }
        }

        public StorageSettings GetStoreSettings()
        {
            return allowedFoodSettings;
        }

        public StorageSettings GetParentStoreSettings()
        {
            if (parent != null && parent.def != null && parent.def.building != null)
            {
                return parent.def.building.fixedStorageSettings;
            }
            return null;
        }

        public void Notify_SettingsChanged()
        {
        }
    }
}
