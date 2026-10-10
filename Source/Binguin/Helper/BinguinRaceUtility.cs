// 加载方式已迁至 XML；下述占位替换描述仅记录旧实现。
// ============================================================================
// 冰鹅族【种族判定】统一入口 —— 2026-09-26（借鉴 vivi/RPEF 的"规则收口"做法）
//
// ★ 为什么要这个东西：
//   在此之前，全项目有 12 处各自硬编码 `pawn.def.defName == "Binguin"`：
//     BandVisitFlow_Binguin.cs:542 / BinguinBodySwitch.cs:131 / Hair_Binguin.cs:79
//     Compat_BinguinSkinAndFace.cs:117 / Fishing_Binguin.cs:267 / Mothership_Binguin.cs:210
//     GameComponent_BinguinDiplomacy.cs:647 / 1311 / 1324 / 1354 / 1430
//     Patch_BinguinPlayerRecruit.cs:60
//   问题不是"多写几遍"，而是：
//     · 判断口径会漂：有的查 `pawn.def`（种族），有的查 `kindDef.race`，
//       第 83 条就踩过一次（`IsLeadSinger` 误用 `pawn.def`，应为 `kindDef`）。
//     · 以后加"冰鹅族亚种/变体"要改十几处，必漏。
//   ⇒ 全部收口到这里，只保留一个判据。
//
// ★★ 判据定义（别搞混，这是本文件存在的唯一理由）：
//     · 【种族】= `ThingDef.defName == "Binguin"`
//       → 本体是 HAR 的 `AlienRace.ThingDef_AlienRace`，继承 `ThingDef`。
//       → 判断"这个小人的种族是不是冰鹅"一律用这个。
//     · 【派系】= `FactionDef.defName`，冰鹅族有两个：Binguin（和平）/ BinguinHostile（敌对）
//       → 那是派系，**不属于本文件**，归 `BinguinFactions` 管。
//     · 【兵种】= `PawnKindDef.defName`，如 Binguin_Leader / Binguin_Settler
//       → 要判断"这个兵种是不是冰鹅族的"看 `PawnKindDef.race`（本文件提供了重载）。
//
// ★ 关于缓存（照本项目一贯的写法，别写成哨兵 bug）：
//   用【独立的 bool lookedUp 标记】而不是 `if (cachedDef == null)` ——
//   第 54 条记过教训：拿 null 当"还没查过"，一旦 def 缺失（改名/未加载）
//   就会每次调用都去查 DefDatabase。这里 def 缺失是永久性的，
//   所以查一次就够，查到空也照样标记"查过了"。
//
// ★ 以后要不要升级成 DefModExtension？
//   可以，而且更灵活（能把"哪些种族算冰鹅"写成 XML 数据）。
//   但 `<li Class="...">` 在 XML 里解析依然要在加载期做类型解析，
//   本 mod 正因为 GenTypes 缓存早期固化才全走 C# 挂载（见 BinguinDefPatches.cs 顶部）。
//   所以要升级也应该是在 C# 里 `def.modExtensions.Add(...)`，
//   而不是往 XML 里写 Class。现在这版是最小、最安全的收口。
// ============================================================================

using RimWorld;
using Verse;

namespace Binguin.Helper
{
    public static class BinguinRaceUtility
    {
        /// <summary>冰鹅族的种族 defName（ThingDef）。</summary>
        public const string RaceDefName = "Binguin";

        private static ThingDef cachedRace;
        private static bool lookedUp;

        /// <summary>冰鹅族的种族 ThingDef（没加载出来时为 null）。</summary>
        public static ThingDef RaceDef
        {
            get
            {
                if (!lookedUp)
                {
                    lookedUp = true;
                    cachedRace = DefDatabase<ThingDef>.GetNamedSilentFail(RaceDefName);
                }
                return cachedRace;
            }
        }

        /// <summary>
        /// 这个小人的种族是不是冰鹅族。
        /// ★ 这是全项目唯一的"是不是冰鹅"判据 —— 别的文件不要再自己比字符串。
        /// </summary>
        public static bool IsBinguin(Pawn pawn)
        {
            return pawn != null && pawn.def != null && IsBinguinRace(pawn.def);
        }

        /// <summary>这个种族 ThingDef 是不是冰鹅族。</summary>
        public static bool IsBinguinRace(ThingDef race)
        {
            if (race == null) return false;
            // 优先比对缓存的 def 引用（最快）；def 没加载出来时退回比 defName，
            // 免得因为缓存拿不到就整体失效。
            ThingDef cached = RaceDef;
            if (cached != null) return race == cached;
            return race.defName == RaceDefName;
        }

        /// <summary>
        /// 这个兵种模板是不是冰鹅族的（看它的 race）。
        /// ★ 判断"兵种"请用这个，不要用 `pawn.def`（第 83 条踩过）。
        /// </summary>
        public static bool IsBinguinKind(PawnKindDef kind)
        {
            return kind != null && IsBinguinRace(kind.race);
        }
    }
}
