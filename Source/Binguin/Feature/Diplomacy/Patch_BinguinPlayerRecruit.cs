// ============================================================================
// 通讯台招募 → 默认冰鹅族殖民者（2026-09 用户需求）
//   「玩家使用通讯台招募的时候默认为冰鹅族殖民者，即为开始游戏可选的池里挑」
//
// 做法：给 Verse.PawnGenerator.GeneratePawn(PawnGenerationRequest) 挂 prefix，
//   当【请求属于玩家派系】且【玩家派系就是冰鹅族玩家派系】且【请求的 kind 是人形】时，
//   把 request.KindDef 直接换成开局池里的 Binguin_Colonist
//   （= 剧本开局可选的冰鹅族殖民者模板：冰鹅族 + 冰鹅种异种 + 常服 + 随机行囊）。
//   → 流浪者加入 / 任务奖励 / 通讯台招募 / 救援等一切"给玩家送人"的路径都会是冰鹅族，
//     而动物、机械体、其它派系的 pawn 完全不受影响。
//
// ★ 判定玩家派系用两种方式（任一成立即生效），避免剧本/派系换 def 后失效：
//     1) 派系 defName == Binguin_PlayerColony（本 mod 的玩家派系）
//     2) 玩家派系 basicMemberKind 的 race 就是 Binguin
// ★ PawnGenerationRequest.KindDef 有 setter（Mono.Cecil 反射确认）；
//   Harmony 对值类型参数用 ref 传递，prefix 里改写 struct 是官方支持用法。
// ============================================================================

using System;
using HarmonyLib;
using RimWorld;
using Verse;

using Binguin.Helper;

namespace Binguin.Feature.Diplomacy
{
    public static class BinguinPlayerPawnUtility
    {
        public const string PlayerFactionDefName = "Binguin_PlayerColony";
        public const string ColonistKindDefName = "Binguin_Colonist";
        // ★ 2026-09-26：种族 defName 的唯一出处改到 BinguinRaceUtility.RaceDefName，
        //   这里保留同名常量只是为了不破坏外部引用（const 可以在编译期折叠，不会多出运行时开销）。
        public const string BinguinRaceDefName = BinguinRaceUtility.RaceDefName;

        private static PawnKindDef colonistKind;
        private static bool lookedUp;

        public static PawnKindDef ColonistKind
        {
            get
            {
                if (!lookedUp)
                {
                    lookedUp = true;
                    colonistKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(ColonistKindDefName);
                }
                return colonistKind;
            }
        }

        // 该派系是不是"冰鹅族玩家派系"
        public static bool IsBinguinPlayerFaction(Faction faction)
        {
            if (faction == null || faction.def == null || !faction.IsPlayer)
            {
                return false;
            }
            if (faction.def.defName == PlayerFactionDefName)
            {
                return true;
            }
            PawnKindDef basic = faction.def.basicMemberKind;
            // ★ 2026-09-26 种族判定收口：见 BinguinRaceUtility
            return BinguinRaceUtility.IsBinguinKind(basic);
        }
    }

    public static class Patch_BinguinPlayerRecruit
    {
        public static void GeneratePawnPrefix(ref PawnGenerationRequest request)
        {
            try
            {
                if (!BinguinPlayerPawnUtility.IsBinguinPlayerFaction(request.Faction))
                {
                    return;
                }
                PawnKindDef current = request.KindDef;
                if (current == null)
                {
                    return;
                }
                PawnKindDef colonist = BinguinPlayerPawnUtility.ColonistKind;
                if (colonist == null || current == colonist)
                {
                    return;
                }
                // 只改人形（动物、机械体、异象实体等一律不动）
                if (current.race == null || current.race.race == null || !current.race.race.Humanlike)
                {
                    return;
                }
                request.KindDef = colonist;
            }
            catch (Exception e)
            {
                BinguinLogUtility.Log("招募默认冰鹅族殖民者补丁异常: " + e.Message, severity: 1, isDebug: false);
            }
        }
    }
}
