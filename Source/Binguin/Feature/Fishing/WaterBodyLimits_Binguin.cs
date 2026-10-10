// ============================================================================
// 水域【捕捞下限】存储 —— 2026-10-06 用户需求修正
//
// 用户原话：「我发现了一个问题，蟹笼的管理鱼群应该是**限制捕捞数量**
//            （控制仓库鱼类上限或者水域内数量），你写成直接改变鱼群数量了。」
// 进一步澄清：「我指的是**限制整个水域的鱼类下限**，比如到某个数字就不捕捞，
//            **默认为中值**」/ 用滑条加减调 / 原来那套直接改鱼群保留成高级功能。
//
// ⇒ 本文件负责"**整片水域共享一个捕捞下限**"的存储：
//     水域鱼群 ≤ 下限时，该水域里的**所有蟹笼**都停止捕捞（鱼群能重新长起来）。
//     —— 与鱼池那边的 `CompBinguinFishPond.keepMinFish`（鱼群 ≤ 保留数停捕）
//        是同一个设计思路，只是这里按"水域"而不是"鱼池组"来记。
//
// ★★ 为什么需要独立的 GameComponent（而不是存在蟹笼 comp 上）：
//   用户要的是"**整个水域**的鱼类下限" ⇒ 同一片水域里的多个蟹笼必须**共享**一个值。
//   存在单个蟹笼身上就变成"每个蟹笼各管各的"，达不到目的。
//
// ★★ 为什么键是 (Map.uniqueID, WaterBody.rootCell)：
//   IL 实证 `Verse.WaterBody` **没有 ID 字段**（只有 map / rootCell / waterBodyType /
//   cells / population 等），而且它在 `WaterBodyTracker.ConstructBodies()` 里
//   **每次读档都会重建** ⇒ 不能直接拿对象当键（读档后就是新对象了）。
//   但 `rootCell` 是构造时定下来的稳定格子，配合地图 ID 就能唯一标识一片水域。
//
// ★★ 为什么继承 `MapComponent` 而不是 `GameComponent`（编译期实证）：
//   `Map.GetComponent<T>()` 的泛型约束是 **`where T : MapComponent`**
//   ⇒ 写成 `GameComponent` 会 **CS0311**（"没有从 X 到 Verse.MapComponent 的隐式转换"）。
//   而且按地图存本来就是对的：一片水域只属于一张地图。
//   自动实例化：`MapComponent` 只要满足 `public` + 构造函数接收 `Map`，
//   就会被 `Map.FillComponents()` 自动创建，**不需要在 XML 里注册**。
// ============================================================================

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Binguin.Feature.Fishing
{
    /// <summary>水域捕捞下限的存储（每张地图一份，按 水域根格 索引）。</summary>
    public class MapComponent_BinguinWaterBodyLimits : MapComponent
    {
        /// <summary>默认下限 = 水域上限的百分比（用户要求"默认为中值"）。</summary>
        public const float DefaultLimitFraction = 0.3f;

        /// <summary>下限允许的最小值（0 = 允许捕到枯竭）。</summary>
        public const float MinLimit = 0f;

        /// <summary>键：地图 ID * 1000000 + 水域根格索引。</summary>
        private Dictionary<long, float> limits = new Dictionary<long, float>();

        /// <summary>可选的显式下限（-1 表示"用默认值"）。存下来是为了区分
        /// "玩家还没调过"（跟着默认值走）和"玩家调成了 0"（合法值）。</summary>
        private Dictionary<long, float> explicitLimits = new Dictionary<long, float>();

        public MapComponent_BinguinWaterBodyLimits(Map map) : base(map)
        {
        }

        // ==================== 存档 ====================
        // ★ 用两个并行的 List 存（Dictionary 不能直接 Scribe）。
        private List<long> scribeKeys = new List<long>();
        private List<float> scribeValues = new List<float>();

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                scribeKeys.Clear();
                scribeValues.Clear();
                foreach (KeyValuePair<long, float> kv in explicitLimits)
                {
                    scribeKeys.Add(kv.Key);
                    scribeValues.Add(kv.Value);
                }
            }
            Scribe_Collections.Look<long>(ref scribeKeys, "binguinWaterLimitKeys", LookMode.Value);
            Scribe_Collections.Look<float>(ref scribeValues, "binguinWaterLimitValues", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                limits.Clear();
                explicitLimits.Clear();
                if (scribeKeys != null && scribeValues != null)
                {
                    int n = Mathf.Min(scribeKeys.Count, scribeValues.Count);
                    for (int i = 0; i < n; i++)
                    {
                        explicitLimits[scribeKeys[i]] = scribeValues[i];
                        limits[scribeKeys[i]] = scribeValues[i];
                    }
                }
            }
        }

        // ==================== 键 ====================

        private static long KeyOf(WaterBody body)
        {
            if (body == null || body.map == null)
            {
                return long.MinValue;
            }
            IntVec3 rc = body.rootCell;
            // ★ 地图 ID 乘 1e6 再叠根格索引 —— 同一张地图的格索引 < 1e6
            //   （最大地图 350×350 = 122500），不会撞车。
            return (long)body.map.uniqueID * 1000000L + (rc.z * 1000 + rc.x);
        }

        /// <summary>
        /// 默认下限（用户要的"中值"）：水域上限 × `DefaultLimitFraction`。
        /// ★ 为什么不写 0.5：上限通常是几百，中值太高会导致"钓不了几条就停"。
        ///   0.3 是"钓到三成就该让它恢复"的折中值，玩家可在窗口里滑条调整。
        /// </summary>
        public static float DefaultLimitFor(WaterBody body)
        {
            if (body == null)
            {
                return 0f;
            }
            float max = body.MaxPopulation;
            if (max < 1f)
            {
                max = 1f;
            }
            return Mathf.Round(max * DefaultLimitFraction);
        }

        // ==================== 读写 ====================

        /// <summary>取这片水域的捕捞下限（玩家没设过就返回默认值）。</summary>
        public static float GetLimit(WaterBody body)
        {
            if (body == null || body.map == null)
            {
                return 0f;
            }
            MapComponent_BinguinWaterBodyLimits comp =
                body.map.GetComponent<MapComponent_BinguinWaterBodyLimits>();
            long key = KeyOf(body);
            if (comp != null && comp.limits.ContainsKey(key))
            {
                return comp.limits[key];
            }
            return DefaultLimitFor(body);
        }

        /// <summary>设置这片水域的捕捞下限（会 clamp 到 [MinLimit, MaxPopulation]）。</summary>
        public static void SetLimit(WaterBody body, float value)
        {
            if (body == null || body.map == null)
            {
                return;
            }
            MapComponent_BinguinWaterBodyLimits comp =
                body.map.GetComponent<MapComponent_BinguinWaterBodyLimits>();
            if (comp == null)
            {
                return;
            }
            float max = body.MaxPopulation;
            if (max < 1f)
            {
                max = 1f;
            }
            float v = Mathf.Clamp(value, MinLimit, max);
            long key = KeyOf(body);
            comp.limits[key] = v;
            comp.explicitLimits[key] = v;
        }

        /// <summary>玩家是否显式设过（用于窗口上区分"默认"和"已自定义"）。</summary>
        public static bool HasExplicitLimit(WaterBody body)
        {
            if (body == null || body.map == null)
            {
                return false;
            }
            MapComponent_BinguinWaterBodyLimits comp =
                body.map.GetComponent<MapComponent_BinguinWaterBodyLimits>();
            return comp != null && comp.explicitLimits.ContainsKey(KeyOf(body));
        }

        /// <summary>恢复默认（"重置为中值"按钮用）。</summary>
        public static void ResetLimit(WaterBody body)
        {
            if (body == null || body.map == null)
            {
                return;
            }
            MapComponent_BinguinWaterBodyLimits comp =
                body.map.GetComponent<MapComponent_BinguinWaterBodyLimits>();
            if (comp == null)
            {
                return;
            }
            long key = KeyOf(body);
            comp.limits.Remove(key);
            comp.explicitLimits.Remove(key);
        }

        /// <summary>
        /// 这片水域现在还能不能捕鱼 —— **蟹笼捕捞的判定入口**。
        /// ★ 与原版钓鱼一致：鱼群必须还在下限**之上**才能捕。
        ///   （用 +0.01 容差避免浮点边界抖动，与鱼池那边 `keepMinFish + 0.01f` 一致。）
        /// </summary>
        public static bool CanCatchFrom(WaterBody body)
        {
            if (body == null)
            {
                return false;
            }
            if (!body.HasFish || body.Population <= 0.01f)
            {
                return false;
            }
            return body.Population > GetLimit(body) + 0.01f;
        }
    }
}
