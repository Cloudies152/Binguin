// ============================================================================
// Mod 入口类 + Mod 选项（设置）界面
//   · ★★★ 2026-10-09【改用原版身形 / 脸版】：
//     "女性身形 4 / 5 切换"这一整套已经**去掉** —— 因为本版的身体贴图
//     直接指向原版（Naked_Female / Naked_Thin），不存在自绘的两套身形。
//     对应的 `Source/BinguinBodySwitch.cs` 已删除，所以这里不能再引用
//     `BinguinBodySwitch.RefreshAll()`（否则编译不过）。
// ============================================================================

using System;
using UnityEngine;
using Verse;
using RimWorld;

using Binguin.Helper;
using Binguin.Feature.Band;

namespace Binguin.Core
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
            BinguinLogUtility.Log("原版身形版：DLL 已加载：" + GetType().Assembly.FullName);
        }

        public override string SettingsCategory()
        {
            return "Binguin_Interface_001".Translate().ToString();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard ls = new Listing_Standard();
            ls.Begin(inRect);
            ls.Label("Binguin_Interface_002".Translate().ToString());
            ls.Gap(2f);
            ls.Label("Binguin_Interface_003".Translate().ToString());
            ls.Label("Binguin_Interface_004".Translate().ToString());

            ls.GapLine();
            ls.Gap(6f);
            ls.Label("Binguin_Interface_005".Translate().ToString());
            ls.Gap(4f);
            // ★ 用户需求：远程斩击属于测试玩法，默认关闭，常态只能近战
            bool beforeVoid = Settings.enableVoidStrike;
            ls.CheckboxLabeled("Binguin_Interface_006".Translate().ToString(),
                ref Settings.enableVoidStrike,
                "Binguin_Interface_007".Translate().ToString()
                + "Binguin_Interface_008".Translate().ToString()
                + "Binguin_Interface_009".Translate().ToString()
                + "Binguin_Interface_010".Translate().ToString());
            if (beforeVoid != Settings.enableVoidStrike)
            {
                // 即时生效：不需要重开存档。场上已装备的剑会在下一帧刷新技能按钮。
                BinguinLogUtility.Log("尚方宝剑远程斩击 = " + (Settings.enableVoidStrike ? "开" : "关"));
            }
            ls.GapLine();
            ls.Gap(6f);
            ls.Label("Binguin_Interface_011".Translate().ToString());
            ls.Gap(4f);
            ls.Label("Binguin_Interface_012".Translate().ToString());
            ls.Label("Binguin_Interface_013".Translate().ToString());
            ls.Gap(4f);

            if (ls.ButtonText("Binguin_Interface_014".Translate().ToString()))
            {
                BinguinUserSongs.OpenFolder();
            }
            if (ls.ButtonText("Binguin_Interface_015".Translate().ToString()))
            {
                BinguinUserSongs.Rescan();
            }

            ls.Gap(4f);
            ls.Label("Binguin_Interface_016".Translate().ToString() + BinguinUserSongs.LoadedNames.Count + "Binguin_Interface_017".Translate().ToString());
            ls.Label("Binguin_Interface_018".Translate().ToString() + BinguinUserSongs.LastResult);
            ls.Label("Binguin_Interface_019".Translate().ToString() + (BinguinUserSongs.FolderPath.NullOrEmpty()
                ? "Binguin_Interface_020".Translate().ToString() : BinguinUserSongs.FolderPath));

            ls.End();

        }
    }
}
