// ============================================================================
// Mod 入口类 + Mod 选项（设置）界面
//   · ★★★ 2026-10-09【原版身形/脸版 v0.0.3】：
//     "女性身形 4 / 5 切换"这一整套已经**去掉** —— 因为本版的身体贴图
//     直接指向原版（Naked_Female / Naked_Thin），不存在自绘的两套身形。
//     对应的 `Source/BinguinBodySwitch.cs` 已删除，所以这里不能再引用
//     `BinguinBodySwitch.RefreshAll()`（否则编译不过）。
// ============================================================================

using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace Binguin
{
    public class BinguinSettings : ModSettings
    {
        /// <summary>
        /// ★★ 尚方宝剑【远程斩击】测试开关（2026-10-06 用户需求）。
        ///
        /// 用户原文：「注意，远程斩击是测试玩法，要在 mod 选项打开，常态只能用于近战」。
        ///   · false（默认）⇒ 尚方宝剑是**纯近战**武器（面板 30 钝击 / 冷却 0.7s）
        ///   · true          ⇒ 额外获得技能「天顶鱼斩」：14 条鱼轮流远程斩击
        ///
        /// ★ 为什么做成选项而不是直接给：远程斩击每秒 14 次、带范围伤害 + 冰爆，
        ///   属于测试性质的超规格玩法，会让普通存档失衡。
        /// ★ 开关是**即时生效**的：`CompBinguinVoidSword.CompGetEquippedGizmosExtra`
        ///   每帧都重新判断，所以场上已装备的剑也会立刻加上/去掉技能按钮。
        /// </summary>
        public bool enableVoidStrike = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<bool>(ref enableVoidStrike, "enableVoidStrike", false, false);
        }
    }

    public class BinguinMod : Mod
    {
        public static BinguinSettings Settings;

        public BinguinMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<BinguinSettings>();
            Log.Message("[冰鹅族·原版身形版] DLL 已加载：" + GetType().Assembly.FullName);
        }

        public override string SettingsCategory()
        {
            return "冰鹅族（原版身形·脸）";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard ls = new Listing_Standard();
            ls.Begin(inRect);
            ls.Label("★★ 这是【原版身形 / 头部 / 面部】版 ★★");
            ls.Gap(2f);
            ls.Label("冰鹅族的身体、头、脸全部使用原版贴图。");
            ls.Label("其余内容（基因、技能、武器、建筑、科技、事件、乐队）与正式版完全一致。");

            ls.GapLine();
            ls.Gap(6f);
            ls.Label("尚方宝剑 · 远程斩击（测试玩法）");
            ls.Gap(4f);
            // ★ 用户需求：远程斩击属于测试玩法，默认关闭，常态只能近战
            bool beforeVoid = Settings.enableVoidStrike;
            ls.CheckboxLabeled("启用「天顶鱼斩」（14 条鱼轮流远程斩击）",
                ref Settings.enableVoidStrike,
                "关闭时尚方宝剑是纯近战武器（面板 30 钝击 / 冷却 0.7s）。\n"
                + "开启后额外获得技能「天顶鱼斩」：向目标区域倾泻 14 条鱼的轮流斩击，\n"
                + "每次 10 钝击 + 冰爆、100% 钝器穿透，对椭圆形范围造成范围伤害。\n"
                + "★ 这是测试玩法，会显著影响平衡。");
            if (beforeVoid != Settings.enableVoidStrike)
            {
                // 即时生效：不需要重开存档。场上已装备的剑会在下一帧刷新技能按钮。
                Log.Message("[冰鹅族] 尚方宝剑远程斩击 = " + (Settings.enableVoidStrike ? "开" : "关"));
            }
            ls.End();

        }
    }
}
