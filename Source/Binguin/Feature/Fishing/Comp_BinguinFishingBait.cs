// ============================================================================
// 打窝用具 —— 投入食物，加快所在水域鱼群增长速度（进阶钓鱼学）
//
// 机制（2026-08-19 用户需求）：
//   - 容器（继承原版 CompThingContainer）：可放入任意食物，
//     营养值上限 5.0；殖民者用自定义 WorkGiver（Binguin_FillFishingBait）
//     自动投喂（每次搬入不超过剩余营养容量的食物）。
//   - 每天（60000 tick）消耗 0.5 营养值（拆堆取走，模拟被鱼吃掉；2026-08-20 用户改：1 → 0.5），
//     然后给所在水域 population += MaxPopulation × 100% ×（实际消耗营养）= 0.5 营养 → +50%，
//     且不超过 MaxPopulation（clamp）。
//   - 若当天容器为空（消耗 0），只记日志不加速。
//   - PlaceWorker（必须水域 + 每水域最多 2 个）由 BinguinDefPatches 挂载。
//
// ★ 食物筛选（2026-08-19 用户要求，抄 Ideology 塑形仓 BiosculpterPod）：
//   实现 IStoreSettingsParent 接口 → 选中建筑时出现 ITab_Storage（存储）标签，
//   玩家可筛选允许投入的食物（默认 = building.fixedStorageSettings 的拷贝，
//   XML 里默认 Foods 类目 = 一切食物含鱼肉）；WorkGiver 投喂时用
//   GetStoreSettings().AllowedToAccept(food) 尊重筛选。
//
// ★ 与陨石引导器同款教训：XML 里用原版 CompProperties_ThingContainer 占位，
//   静态构造只换 compClass → props 运行时是原版类型，
//   自定义数值一律用常量（本类顶部 const），不建自定义属性类。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Fishing
{
    public class CompBinguinFishingBait : CompThingContainer, IStoreSettingsParent
    {
        private const float MaxNutrition = 5f;        // 营养值上限
        private const float NutritionPerDay = 0.5f;   // ★ 每天消耗 0.5 营养值（2026-08-20 用户改：1 → 0.5）
        private const float GrowthPerDay = 1.0f;      // 每消耗 0.5 营养/天 → +50% MaxPopulation 增速
                                                       //   （2026-08-20 同步调整：耗 0.5 → 0.5×1.0 = +50%，效率翻倍）

        private int nextConsumeTick = -1;

        // 玩家可编辑的食物筛选（ITab_Storage 编辑它；默认拷贝 fixedStorageSettings）
        public StorageSettings allowedFoodSettings;

        // 当前容器内食物总营养
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

        // 剩余可容纳营养（WorkGiver 投喂时用）
        public float NutritionCapacityLeft
        {
            get { return Mathf.Max(0f, MaxNutrition - NutritionLeft); }
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            allowedFoodSettings = new StorageSettings(this);
            // ★ fixedStorageSettings 挂在 ThingDef.building（BuildingProperties）上
            if (parent != null && parent.def != null && parent.def.building != null && parent.def.building.fixedStorageSettings != null)
            {
                allowedFoodSettings.CopyFrom(parent.def.building.fixedStorageSettings);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<int>(ref nextConsumeTick, "nextConsumeTick", -1, false);
            Scribe_Deep.Look<StorageSettings>(ref allowedFoodSettings, "allowedFoodSettings");
        }

        // ---------- IStoreSettingsParent（ITab_Storage 存储筛选 UI） ----------
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

        public override void CompTick()
        {
            base.CompTick();
            // ★ 2026-09 性能：本建筑 tickerType=Normal（每 tick 调用），而
            //   NutritionLeft 要遍历容器每一堆并逐堆查营养 stat。超容检查
            //   改为每 250 tick（4 秒，与 Rare 同频）做一次——最多晚 4 秒
            //   把多余饵料丢回地面，玩家不可感知。
            if (parent.IsHashIntervalTick(250) && NutritionLeft > MaxNutrition + 0.01f)
            {
                EjectExcess();
            }
            if (nextConsumeTick < 0)
            {
                nextConsumeTick = GenTicks.TicksGame + 60000;
            }
            if (GenTicks.TicksGame >= nextConsumeTick)
            {
                nextConsumeTick += 60000;
                ConsumeAndBoost();
            }
        }

        // 把超过营养上限的食物丢回地上
        private void EjectExcess()
        {
            // ★ 2026-09 性能：循环条件里原来每轮重算 NutritionLeft（O(n²)），
            //   现在用局部变量维护剩余营养 → O(n)。
            float left = NutritionLeft;
            bool dropped = false;
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
                Thing droppedThing = food.SplitOff(dropCount);
                if (droppedThing != null)
                {
                    GenPlace.TryPlaceThing(droppedThing, parent.Position, parent.Map, ThingPlaceMode.Near, null);
                }
                left -= nut * dropCount;
                dropped = true;
            }
            // ★ 2026-09：只有真的丢出东西才记日志（原实现每 tick 都可能刷一条）
            if (dropped)
            {
                BinguinLogUtility.Log("打窝用具：饵料超出营养上限，已把多余部分放回地面。");
            }
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            WaterBody body;
            string waterInfo = "Binguin_CompFishingBait_01".Translate();
            if (parent.Map.waterBodyTracker.TryGetWaterBodyAt(parent.Position, out body))
            {
                waterInfo = "Binguin_CompFishingBait_02".Translate() + body.Population.ToString("0") + " / " + body.MaxPopulation.ToString("0");
            }
            return "Binguin_CompFishingBait_03".Translate() + NutritionLeft.ToString("0.0") + " / " + MaxNutrition.ToString("0.0")
                + "\n" + waterInfo;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            if (Prefs.DevMode)
            {
                Command_Action consume = new Command_Action();
                consume.defaultLabel = "Binguin_CompFishingBait_04".Translate();
                consume.defaultDesc = "Binguin_CompFishingBait_05".Translate();
                consume.action = delegate { ConsumeAndBoost(); };
                yield return consume;
            }
        }

        // 每天消耗 1 营养值 → 该水域鱼群 +50% MaxPopulation / 天
        private void ConsumeAndBoost()
        {
            if (parent.Map == null)
            {
                return;
            }
            float consumed = 0f;
            float need = NutritionPerDay;
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
                    // 无营养的物品（不该存在，但防御）：直接清出
                    innerContainer.Remove(food);
                    food.Destroy();
                    continue;
                }
                int takeCount = Mathf.CeilToInt(Mathf.Min(need / nut, (float)food.stackCount));
                if (takeCount <= 0)
                {
                    takeCount = 1;
                }
                Thing eaten = food.SplitOff(takeCount);   // 从堆中拆出 takeCount 个
                if (eaten != null)
                {
                    eaten.Destroy();                       // 被鱼吃掉
                }
                float taken = nut * takeCount;
                consumed += taken;
                need -= taken;
            }

            if (consumed <= 0.001f)
            {
                BinguinLogUtility.Log("打窝用具：容器为空，今天未撒饵（无增速）。");
                return;
            }

            WaterBody body;
            if (!parent.Map.waterBodyTracker.TryGetWaterBodyAt(parent.Position, out body))
            {
                BinguinLogUtility.Log("打窝用具：不在水域中，饵料效果无效。");
                return;
            }

            // 增速 = MaxPopulation × GrowthPerDay × 实际消耗营养（消耗 0.5 营养 = +50%/天）
            float boost = body.MaxPopulation * GrowthPerDay * Mathf.Min(consumed, MaxNutrition);
            body.Population = Mathf.Min(body.Population + boost, body.MaxPopulation);
            BinguinLogUtility.Log("打窝用具撒饵：消耗 " + consumed.ToString("0.00")
                + " 营养，鱼群 +" + boost.ToString("0") + "（" + body.Population.ToString("0")
                + " / " + body.MaxPopulation.ToString("0") + "）");
        }
    }
}
