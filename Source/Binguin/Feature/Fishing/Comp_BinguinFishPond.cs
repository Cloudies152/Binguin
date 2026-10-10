// ============================================================================
// 鱼池（2026-08-20 用户需求 v4）—— 进阶钓鱼学解锁，2x2 养殖池
//
// 功能：
//   ① 放苗：Gizmo → Dialog 选鱼种 → 殖民者搬运 1 条该鱼放入池中（JobDriver
//      Binguin_PondStock，非凭空消耗）；同种再放 = +1 条
//   ② 钓鱼：殖民者启用原版「钓鱼」工作自动来钓（WorkGiver_BinguinPondFishing）；
//      Gizmo 手动指派亦可。低于「保留鱼数」时不钓
//   ③ 鱼食：ITab_Storage 手动筛选食物（同打窝用具）+ WorkGiver 自动投喂进
//      鱼食容器（每池容器营养上限 5）；组内容器总营养 > 0 → 繁殖 +50%
//      （每池每天自然 4 / 鱼食 6），每 60000 tick 组内消耗 0.5 营养
//   ④ 蟹笼模块：Gizmo 开关，每 15000 tick 捕 池数 条；低于「保留鱼数」停捕
//   ⑤ 保留下限（keepMin）：Gizmo「保留鱼数」设置——鱼群 ≤ 保留数时停止
//      捕捞（钓鱼/蟹笼模块），让鱼繁殖恢复
//
// 连通模型（v4 改进，放置即连接）：
//   - 相邻（2x2 共享边，间距 2）的池在放置时即并入同一组（无论是否放苗）
//   - 同组判定 = 相邻 且 LeaderComp 相同（leaderThing 归并）
//   - 数据（鱼种/鱼群/开关/计时/保留下限）只存组长；组容量 = 组池数 × 20
//   - 拆组长：数据移交相邻同组员；无则鱼掉落地面
// ============================================================================

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Binguin.Feature.Fishing
{
    public class CompProperties_BinguinFishPond : CompProperties
    {
        public float catchIntervalTicks = 15000f;
        // ★ 2026-09：删除无人使用的 dayTicks 字段（tick 里用的是局部常量 rarePerDay）。

        public CompProperties_BinguinFishPond()
        {
            compClass = typeof(CompBinguinFishPond);
        }
    }

    public class CompBinguinFishPond : ThingComp
    {
        public const float MaxFishPerPond = 20f;

        // ---------- 组状态（组长持有；组员 fishDefName 同步存档） ----------
        public string fishDefName = "";    // 组鱼种（"" = 未放苗）
        public float fishCount;            // 组鱼群
        public bool autoCatchOn;           // 蟹笼模块
        public float reproAccum;           // 繁殖计时
        public float catchAccum;           // 蟹笼计时
        public float feedTickAccum;        // 鱼食消耗计时（每 dayTicks 组内耗 0.5）
        public float keepMinFish;          // ★ 保留下限：鱼群 ≤ 此值停捕（2026-08-20）
        public int cachedGroupCount = -1;

        public Thing leaderThing;          // 组长 Thing；null/self = 组长
        private CompBinguinFishPond leaderCompCache;
        private static ThingDef cachedPondDef;

        // ★ 2026-09 性能：鱼池垂钓 job def 同样是常量 → 懒加载缓存（null 不缓存，
        //   保持 def 尚未加载时可重试的语义）。
        private static JobDef cachedPondFishJobDef;

        public static JobDef PondFishJobDef
        {
            get
            {
                if (cachedPondFishJobDef == null)
                {
                    cachedPondFishJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Binguin_PondFish");
                }
                return cachedPondFishJobDef;
            }
        }

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

        public CompBinguinFishPond LeaderComp
        {
            get
            {
                if (leaderThing == null || leaderThing == parent || leaderThing.Destroyed)
                {
                    return this;
                }
                if (leaderCompCache == null || leaderCompCache.parent != leaderThing)
                {
                    leaderCompCache = leaderThing.TryGetComp<CompBinguinFishPond>();
                }
                if (leaderCompCache == null)
                {
                    leaderThing = null;
                    return this;
                }
                return leaderCompCache;
            }
        }

        public bool IsLeaderComp { get { return leaderThing == null || leaderThing == parent; } }

        public CompProperties_BinguinFishPond Props
        {
            get { return (CompProperties_BinguinFishPond)props; }
        }

        // ★ 2026-09 性能：检视面板每帧读它 → 按 fishDefName 记忆化，
        //   不再每帧查一次 DefDatabase（鱼种名放苗后基本不变）。
        private string cachedFishLabelName;
        private string cachedFishLabel;

        public string FishLabel
        {
            get
            {
                CompBinguinFishPond lead = LeaderComp;
                string name = lead.fishDefName;
                if (string.IsNullOrEmpty(name))
                {
                    cachedFishLabelName = name;
                    cachedFishLabel = "Binguin_CompFishPond_01".Translate();
                    return cachedFishLabel;
                }
                if (cachedFishLabel == null || cachedFishLabelName != name)
                {
                    cachedFishLabelName = name;
                    ThingDef f = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    cachedFishLabel = f != null ? f.label : name;
                }
                return cachedFishLabel;
            }
        }

        public static bool AreAdjacent(Thing a, Thing b)
        {
            int dx = Mathf.Abs(a.Position.x - b.Position.x);
            int dz = Mathf.Abs(a.Position.z - b.Position.z);
            return (dx == 2 && dz == 0) || (dx == 0 && dz == 2);
        }

        // ================= 放置 / 拆毁 / 加载 =================

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (respawningAfterLoad)
            {
                if (leaderThing != null && leaderThing != parent && !leaderThing.Destroyed)
                {
                    CompBinguinFishPond c = leaderThing.TryGetComp<CompBinguinFishPond>();
                    if (c == null)
                    {
                        leaderThing = null;
                    }
                }
                return;
            }
            // ★ 放置即连接：并入任意相邻池所在组（无论是否放苗）
            CompBinguinFishPond neighborLead = FindAdjacentLeader();
            if (neighborLead != null)
            {
                fishDefName = neighborLead.fishDefName;
                leaderThing = neighborLead.parent;
            }
            else
            {
                leaderThing = null;
            }
            // ★ 2026-08-20 连接贴图：新池放进地图 → 自己与四邻的连接 mask
            //   都变了 → 重打地图网格（MapMeshOnly 建筑贴图在 mesh 里）
            DirtyConnectionVisuals(parent.Map);
        }

        // 连接贴图刷新：dirty 自己 2x2 与四邻格上鱼池的 2x2（放/拆时调用）
        // ★ 1.6 的 mesh 脏标记改成了 def 制（MapMeshFlagDef.Buildings.mask），
        //   不再是 MapMeshFlag 枚举 —— 用 def 查询拿 ulong mask
        private static ulong buildingsMeshMask;
        private static bool buildingsMeshMaskCached;

        private static ulong BuildingsMeshFlag()
        {
            // ★ 2026-09 性能：这个值在 def 加载完成后就是常量，但原实现每次
            //   放置/拆池都要做一遍 DefDatabase 查询 + AccessTools.Field + 反射
            //   取值。改成只算一次并缓存（拿不到值时同旧行为按第 3 位兜底）。
            if (buildingsMeshMaskCached)
            {
                return buildingsMeshMask;
            }
            // mask 是 MapMeshFlagDef 的私有字段（由 def 解析时按 index 赋值），
            // 用反射读；拿不到就按 Core 默认顺序 Buildings 在第 3 位兜底
            MapMeshFlagDef f = DefDatabase<MapMeshFlagDef>.GetNamedSilentFail("Buildings");
            if (f != null)
            {
                FieldInfo fi = AccessTools.Field(typeof(MapMeshFlagDef), "mask");
                if (fi != null)
                {
                    object v = fi.GetValue(f);
                    if (v != null && v is ulong)
                    {
                        buildingsMeshMask = (ulong)v;
                        buildingsMeshMaskCached = true;
                        return buildingsMeshMask;
                    }
                }
            }
            buildingsMeshMask = (ulong)1 << 3;
            buildingsMeshMaskCached = true;
            return buildingsMeshMask;
        }

        private void DirtyConnectionVisuals(Map map)
        {
            if (map == null || map.mapDrawer == null || parent == null)
            {
                return;
            }
            ulong flag = BuildingsMeshFlag();
            IntVec3 pos = parent.Position;
            // 自己 2x2 的四个格
            for (int dx = 0; dx < 2; dx++)
            {
                for (int dz = 0; dz < 2; dz++)
                {
                    map.mapDrawer.MapMeshDirty(pos + new IntVec3(dx, 0, dz), flag);
                }
            }
            // 四邻（间距 2）可能贴边的池：整池 2x2 dirty
            IntVec3[] offsets = new IntVec3[]
            {
                new IntVec3(0, 0, -2), new IntVec3(2, 0, 0),
                new IntVec3(0, 0, 2), new IntVec3(-2, 0, 0)
            };
            for (int i = 0; i < offsets.Length; i++)
            {
                IntVec3 cell = pos + offsets[i];
                if (!cell.InBounds(map) || PondDef == null)
                {
                    continue;
                }
                List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing t = things[j];
                    if (t != null && !t.Destroyed && t.def == PondDef)
                    {
                        for (int dx = 0; dx < 2; dx++)
                        {
                            for (int dz = 0; dz < 2; dz++)
                            {
                                map.mapDrawer.MapMeshDirty(t.Position + new IntVec3(dx, 0, dz), flag);
                            }
                        }
                    }
                }
            }
        }

        private CompBinguinFishPond FindAdjacentLeader()
        {
            if (parent == null || parent.Map == null || PondDef == null)
            {
                return null;
            }
            List<Thing> ponds = parent.Map.listerThings.ThingsOfDef(PondDef);
            for (int i = 0; i < ponds.Count; i++)
            {
                Thing t = ponds[i];
                if (t == parent || t.Destroyed)
                {
                    continue;
                }
                CompBinguinFishPond c = t.TryGetComp<CompBinguinFishPond>();
                if (c == null)
                {
                    continue;
                }
                CompBinguinFishPond lead = c.LeaderComp;
                if (lead != null && lead.parent != null && AreAdjacent(parent, t))
                {
                    return lead;
                }
            }
            return null;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            base.PostDeSpawn(map, mode);
            // ★ 2026-08-20 连接贴图：拆除后四邻的连接 mask 都变了 → 重打网格
            DirtyConnectionVisuals(map);
            if (IsLeaderComp)
            {
                if (!string.IsNullOrEmpty(fishDefName) && fishCount >= 0.5f)
                {
                    CompBinguinFishPond heir = FindHeir(map);
                    if (heir != null)
                    {
                        heir.fishDefName = fishDefName;
                        heir.fishCount = fishCount;
                        heir.autoCatchOn = autoCatchOn;
                        heir.reproAccum = reproAccum;
                        heir.catchAccum = catchAccum;
                        heir.feedTickAccum = feedTickAccum;
                        heir.keepMinFish = keepMinFish;
                        heir.leaderThing = null;
                        // 原组员重挂到新组长（同组相邻者）
                        if (heir.parent != null && heir.parent.Map != null)
                        {
                            List<Thing> ponds = heir.parent.Map.listerThings.ThingsOfDef(PondDef);
                            for (int i = 0; i < ponds.Count; i++)
                            {
                                Thing t = ponds[i];
                                if (t == heir.parent || t.Destroyed)
                                {
                                    continue;
                                }
                                CompBinguinFishPond c = t.TryGetComp<CompBinguinFishPond>();
                                if (c != null && AreAdjacent(heir.parent, t)
                                    && (c.leaderThing == parent || c.leaderThing == null))
                                {
                                    c.fishDefName = fishDefName;
                                    c.leaderThing = heir.parent;
                                }
                            }
                        }
                    }
                    else if (map != null && parent != null)
                    {
                        int drop = Mathf.Clamp((int)fishCount, 0, 1 + (int)MaxFishPerPond);
                        if (drop > 0)
                        {
                            ThingDef f = DefDatabase<ThingDef>.GetNamedSilentFail(fishDefName);
                            if (f != null)
                            {
                                Thing fish = ThingMaker.MakeThing(f);
                                fish.stackCount = Mathf.Min(drop, f.stackLimit);
                                GenPlace.TryPlaceThing(fish, parent.Position, map, ThingPlaceMode.Near, null);
                            }
                        }
                    }
                }
            }
            leaderThing = null;
        }

        private CompBinguinFishPond FindHeir(Map map)
        {
            if (map == null || PondDef == null)
            {
                return null;
            }
            List<Thing> ponds = map.listerThings.ThingsOfDef(PondDef);
            for (int i = 0; i < ponds.Count; i++)
            {
                Thing t = ponds[i];
                if (t == parent || t.Destroyed)
                {
                    continue;
                }
                CompBinguinFishPond c = t.TryGetComp<CompBinguinFishPond>();
                if (c != null && c.LeaderComp != null && c.LeaderComp.parent == parent
                    && AreAdjacent(parent, t))
                {
                    return c;
                }
            }
            return null;
        }

        // ================= 组信息（BFS：相邻 且 LeaderComp 相同） =================

        public List<Thing> GetGroupThings()
        {
            List<Thing> result = new List<Thing>();
            if (parent == null || parent.Map == null || PondDef == null)
            {
                result.Add(parent);
                return result;
            }
            Thing myLead = LeaderComp != null ? LeaderComp.parent : parent;
            HashSet<Thing> visited = new HashSet<Thing>();
            Queue<Thing> queue = new Queue<Thing>();
            visited.Add(parent);
            queue.Enqueue(parent);
            // ★ 2026-09 性能：组内所有池必在同一张地图上（按相邻 2 格连接），
            //   原来每个出队节点都重新取一次 ThingsOfDef 列表（O(池数) 次），
            //   现在整轮 BFS 只取一次。
            List<Thing> ponds = parent.Map.listerThings.ThingsOfDef(PondDef);
            while (queue.Count > 0)
            {
                Thing cur = queue.Dequeue();
                result.Add(cur);
                for (int i = 0; i < ponds.Count; i++)
                {
                    Thing t = ponds[i];
                    if (t.Destroyed || visited.Contains(t))
                    {
                        continue;
                    }
                    CompBinguinFishPond c = t.TryGetComp<CompBinguinFishPond>();
                    if (c == null)
                    {
                        continue;
                    }
                    CompBinguinFishPond lead = c.LeaderComp;
                    if (lead == null || lead.parent == null || lead.parent != myLead)
                    {
                        continue;
                    }
                    if (AreAdjacent(cur, t))
                    {
                        visited.Add(t);
                        queue.Enqueue(t);
                    }
                }
            }
            return result;
        }

        public int GroupPondCount()
        {
            List<Thing> g = GetGroupThings();
            return g != null && g.Count > 0 ? g.Count : 1;
        }

        public int CachedGroupPondCount()
        {
            if (cachedGroupCount < 0)
            {
                cachedGroupCount = GroupPondCount();
            }
            return cachedGroupCount;
        }

        public float GroupCapacity()
        {
            return GroupPondCount() * MaxFishPerPond;
        }

        public float CachedGroupCapacity()
        {
            return CachedGroupPondCount() * MaxFishPerPond;
        }

        // ★ 2026-09 性能：检视面板每帧调用 → 用 CompTickRare 里算好的缓存值
        //   （最多滞后一个 Rare tick = 4 秒，面板显示无感）。
        public float cachedGroupFeed;

        // 组内鱼食总营养（扫描各池 CompBinguinPondFeed 容器）
        public float GroupFeedNutrition()
        {
            return GroupFeedNutritionOf(GetGroupThings());
        }

        private static float GroupFeedNutritionOf(List<Thing> group)
        {
            float total = 0f;
            if (group == null)
            {
                return 0f;
            }
            for (int i = 0; i < group.Count; i++)
            {
                CompBinguinPondFeed feed = group[i].TryGetComp<CompBinguinPondFeed>();
                if (feed != null)
                {
                    total += feed.NutritionLeft;
                }
            }
            return total;
        }

        // 组内容器营养上限 = 池数 × 5
        public float GroupFeedCapacity()
        {
            return GroupPondCount() * CompBinguinPondFeed.MaxNutrition;
        }

        // ================= Tick（组长；Rare） =================

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (parent == null || parent.Map == null || !IsLeaderComp)
            {
                return;
            }
            cachedGroupCount = -1;
            if (string.IsNullOrEmpty(fishDefName))
            {
                return;
            }

            const float rarePerDay = 60000f / 250f; // 240 次/天
            // ★ 2026-09 性能：这一轮 tick 原来最多跑 3 次组 BFS（GetGroupThings：
            //   一次算鱼食、CachedGroupPondCount 再跑两次）。现在整轮只跑一次，
            //   池数/鱼食/消耗全部复用同一份结果。
            List<Thing> group = GetGroupThings();
            int ponds = group.Count > 0 ? group.Count : 1;
            cachedGroupCount = ponds;
            cachedGroupFeed = GroupFeedNutritionOf(group);
            float groupFeed = cachedGroupFeed;

            // 鱼食消耗：有营养时每 dayTicks 从组内容器耗 0.5
            if (groupFeed > 0f)
            {
                feedTickAccum += 1f;
                if (feedTickAccum >= rarePerDay)
                {
                    feedTickAccum = 0f;
                    ConsumeGroupFeedOf(group, 0.5f);
                }
            }

            // 繁殖：每池每天自然 4 / 鱼食 6（+50%）
            reproAccum += 1f;
            if (reproAccum >= rarePerDay)
            {
                reproAccum = 0f;
                float cap = ponds * MaxFishPerPond;
                if (fishCount < cap)
                {
                    float gain = (groupFeed > 0f ? 6f : 4f) * ponds;
                    fishCount = Mathf.Min(cap, fishCount + gain);
                }
            }

            // 蟹笼模块：每 catchIntervalTicks 捕 池数 条；保留下限保护
            if (autoCatchOn)
            {
                catchAccum += 1f;
                float rarePerCatch = Props.catchIntervalTicks / 250f;
                if (catchAccum >= rarePerCatch)
                {
                    catchAccum = 0f;
                    int n = ponds;
                    // 不捕到低于保留数
                    int allowed = Mathf.FloorToInt(Mathf.Max(0f, fishCount - keepMinFish));
                    int take = Mathf.Min(n, allowed);
                    for (int i = 0; i < take; i++)
                    {
                        fishCount -= 1f;
                        SpawnOneFish();
                    }
                }
            }
        }

        private void ConsumeGroupFeed(float need)
        {
            ConsumeGroupFeedOf(GetGroupThings(), need);
        }

        private void ConsumeGroupFeedOf(List<Thing> group, float need)
        {
            float left = need;
            if (group == null)
            {
                return;
            }
            for (int i = 0; i < group.Count && left > 0.001f; i++)
            {
                CompBinguinPondFeed feed = group[i].TryGetComp<CompBinguinPondFeed>();
                if (feed != null && feed.NutritionLeft > 0f)
                {
                    left -= feed.ConsumeNutrition(left);
                }
            }
        }

        // ================= 放苗（殖民者搬运由 JobDriver 完成，成功后调用） =================

        // 把一条鱼放入池中：同种 +1，空池定种 +1。返回是否成功
        public bool TryStock(string defName)
        {
            CompBinguinFishPond lead = LeaderComp;
            if (lead == null)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(lead.fishDefName) && lead.fishDefName != defName)
            {
                return false; // 相邻已养不同鱼种，需先拆开
            }
            if (lead.fishCount >= lead.GroupCapacity() - 0.01f)
            {
                return false; // 组容量已满（池数 × 20）
            }
            lead.fishDefName = defName;
            lead.fishCount += 1f;
            return true;
        }

        // 钓鱼扣 1（受保留数保护：<= keepMin 时不钓）
        public bool TryCatchOne()
        {
            CompBinguinFishPond lead = LeaderComp;
            if (lead == null || string.IsNullOrEmpty(lead.fishDefName))
            {
                return false;
            }
            if (lead.fishCount <= lead.keepMinFish + 0.01f)
            {
                return false;
            }
            lead.fishCount -= 1f;
            lead.SpawnOneFish();
            return true;
        }

        // 自动钓鱼工作是否可以钓（供 WorkGiver 判断）
        public bool CanFishNow()
        {
            CompBinguinFishPond lead = LeaderComp;
            return lead != null
                && !string.IsNullOrEmpty(lead.fishDefName)
                && lead.fishCount > lead.keepMinFish + 0.01f;
        }

        private void SpawnOneFish()
        {
            CompBinguinFishPond lead = LeaderComp;
            if (lead == null)
            {
                return;
            }
            ThingDef f = DefDatabase<ThingDef>.GetNamedSilentFail(lead.fishDefName);
            if (f == null || lead.parent == null || lead.parent.Map == null)
            {
                return;
            }
            Thing fish = ThingMaker.MakeThing(f);
            fish.stackCount = 1;
            GenPlace.TryPlaceThing(fish, lead.parent.Position, lead.parent.Map, ThingPlaceMode.Near, null);
        }

        // ================= Gizmo =================

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            CompBinguinFishPond lead = LeaderComp;
            if (lead == null || lead.parent == null || lead.parent.Destroyed)
            {
                yield break;
            }

            // ★ 2026-09 性能：三个命令按钮原本各查一次 ContentFinder（字典+路径
            //   解析），一次取好复用（不能做静态缓存：贴图重载会失效）。
            Texture2D pondIcon = ContentFinder<Texture2D>.Get("Things/Building/Binguin/FishPond", false);
            // 放苗（有鱼种时也可继续放同种 +1；异种由对话框提示拆池）
            Command_Action stock = new Command_Action();
            stock.defaultLabel = string.IsNullOrEmpty(lead.fishDefName) ? "Binguin_CompFishPond_02".Translate() : "Binguin_CompFishPond_03".Translate();
            stock.defaultDesc = string.IsNullOrEmpty(lead.fishDefName)
                ? "Binguin_CompFishPond_04".Translate()
                : "Binguin_CompFishPond_05".Translate() + lead.FishLabel + "Binguin_CompFishPond_06".Translate();
            stock.icon = pondIcon;
            stock.action = delegate
            {
                Find.WindowStack.Add(new Dialog_BinguinStockFish(lead));
            };
            yield return stock;

            if (!string.IsNullOrEmpty(lead.fishDefName))
            {
                // 手动钓鱼
                Command_Action fishCmd = new Command_Action();
                fishCmd.defaultLabel = "Binguin_CompFishPond_07".Translate();
                fishCmd.defaultDesc = "Binguin_CompFishPond_08".Translate()
                    + "Binguin_CompFishPond_09".Translate() + lead.keepMinFish.ToString("0") + "Binguin_CompFishPond_10".Translate()
                    + "Binguin_CompFishPond_11".Translate();
                fishCmd.icon = pondIcon;
                fishCmd.action = delegate { StartFishing(lead); };
                yield return fishCmd;

                // 保留下限
                Command_Action keep = new Command_Action();
                keep.defaultLabel = "Binguin_CompFishPond_12".Translate();
                keep.defaultDesc = "Binguin_CompFishPond_13".Translate()
                    + lead.keepMinFish.ToString("0") + "Binguin_CompFishPond_14".Translate() + lead.CachedGroupCapacity().ToString("0");
                keep.icon = pondIcon;
                keep.action = delegate
                {
                    Find.WindowStack.Add(new Dialog_BinguinPondKeep(lead));
                };
                yield return keep;

                // 蟹笼模块
                Command_Toggle toggle = new Command_Toggle();
                toggle.defaultLabel = "Binguin_CompFishPond_15".Translate();
                toggle.defaultDesc = "Binguin_CompFishPond_16".Translate();
                toggle.icon = ContentFinder<Texture2D>.Get("Things/Building/Binguin/CrabTrap", false);
                toggle.isActive = delegate { return lead.autoCatchOn; };
                toggle.toggleAction = delegate { lead.autoCatchOn = !lead.autoCatchOn; };
                yield return toggle;
            }
        }

        private void StartFishing(CompBinguinFishPond lead)
        {
            Map map = lead.parent != null ? lead.parent.Map : null;
            if (map == null)
            {
                return;
            }
            if (!lead.CanFishNow())
            {
                Messages.Message("Binguin_CompFishPond_17".Translate() + lead.keepMinFish.ToString("0")
                    + "Binguin_CompFishPond_18".Translate(), lead.parent, MessageTypeDefOf.RejectInput, false);
                return;
            }
            Pawn fisher = null;
            float best = float.MaxValue;
            for (int i = 0; i < map.mapPawns.FreeColonists.Count; i++)
            {
                Pawn p = map.mapPawns.FreeColonists[i];
                if (p.Downed || p.InMentalState || p.workSettings == null)
                {
                    continue;
                }
                float d = p.Position.DistanceTo(lead.parent.Position);
                if (d < best)
                {
                    best = d;
                    fisher = p;
                }
            }
            if (fisher == null)
            {
                Messages.Message("Binguin_CompFishPond_19".Translate(), lead.parent, MessageTypeDefOf.RejectInput, false);
                return;
            }
            JobDef jd = PondFishJobDef;
            if (jd == null)
            {
                return;
            }
            Job job = new Job(jd, lead.parent);
            job.count = 1;
            fisher.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public override string CompInspectStringExtra()
        {
            CompBinguinFishPond lead = LeaderComp;
            if (lead == null)
            {
                return null;
            }
            if (string.IsNullOrEmpty(lead.fishDefName))
            {
                return "Binguin_CompFishPond_20".Translate()
                    + "Binguin_CompFishPond_21".Translate() + lead.CachedGroupPondCount().ToString("0");
            }
            int n = lead.CachedGroupPondCount();
            float cap = n * MaxFishPerPond;
            // ★ 2026-09 性能：面板每帧调用——鱼食读 Rare tick 缓存，容量直接由
            //   n 换算（原来又跑一次 BFS：GroupFeedNutrition + GroupFeedCapacity）。
            float feed = lead.cachedGroupFeed;
            return "Binguin_CompFishPond_22".Translate() + lead.FishLabel + "（" + n.ToString("0") + "Binguin_CompFishPond_23".Translate()
                + "Binguin_CompFishPond_24".Translate() + lead.fishCount.ToString("0") + " / " + cap.ToString("0")
                + "Binguin_CompFishPond_25".Translate() + lead.keepMinFish.ToString("0") + "）\n"
                + "Binguin_CompFishPond_26".Translate() + feed.ToString("0.0") + " / " + (n * CompBinguinPondFeed.MaxNutrition).ToString("0")
                + (feed > 0f ? (string)"Binguin_CompFishPond_27".Translate() : "") + "\n"
                + "Binguin_CompFishPond_28".Translate() + (lead.autoCatchOn ? "Binguin_CompFishPond_29".Translate() : "Binguin_CompFishPond_30".Translate());
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<string>(ref fishDefName, "pondFishDef", "", false);
            Scribe_Values.Look<float>(ref fishCount, "pondFishCount", 0f, false);
            Scribe_Values.Look<bool>(ref autoCatchOn, "pondAutoCatch", false, false);
            Scribe_Values.Look<float>(ref reproAccum, "pondReproAccum", 0f, false);
            Scribe_Values.Look<float>(ref catchAccum, "pondCatchAccum", 0f, false);
            Scribe_Values.Look<float>(ref feedTickAccum, "pondFeedTickAccum", 0f, false);
            Scribe_Values.Look<float>(ref keepMinFish, "pondKeepMinFish", 0f, false);
            Scribe_References.Look<Thing>(ref leaderThing, "pondLeaderThing", false);
            cachedGroupCount = -1;
        }
    }
}
