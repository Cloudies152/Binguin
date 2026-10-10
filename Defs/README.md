# Defs — 功能目录说明

基础内容位于 `Defs/`；需要 Odyssey 的内容位于 `AdvancedFishing/Defs/`，
由根目录 `LoadFolders.xml` 条件加载。两者均采用 `Feature/<功能>` 分类。
功能内部保留相关 ThingDef、JobDef、RecipeDef、HediffDef 等，不按 Def 类型拆散玩法。

共享科技树放在 `Core/Research`。Def XML 没有 C# 命名空间，子目录不改变定义身份；
`defName`、抽象父定义 `Name`、`ParentName`、程序集类型引用和资源逻辑路径均需保持一致。
大型混合文件已按功能拆分，文件名可在不同功能目录中重复；定位内容优先搜索 `defName`。

## Defs

### Core/Research

- [ResearchProjectDefs_Binguin.xml](Core/Research/ResearchProjectDefs_Binguin.xml)：**12 个科技**（含「增冰鹅鹅」「霰雪无垠」等）+ 研究标签。本文件 13 个定义。

### Feature/Apparel

- [Apparel_Coats.xml](Feature/Apparel/Apparel_Coats.xml)：风尘大衣、风雪大衣。本文件 2 个定义。
- [Apparel_Cosplay.xml](Feature/Apparel/Apparel_Cosplay.xml)：⑨服、大酱服。本文件 2 个定义。
- [Apparel_Headwear.xml](Feature/Apparel/Apparel_Headwear.xml)：雪花绒帽、绒羽头冠、红围巾。本文件 3 个定义。
- [Apparel_Jumpsuit.xml](Feature/Apparel/Apparel_Jumpsuit.xml)：企鹅连体衣。本文件 1 个定义。
- [Apparel_Uniforms.xml](Feature/Apparel/Apparel_Uniforms.xml)：JK服、机工装、医生服、研究服、军服、常服。本文件 5 个定义。
- [Armor_ScoutAndMilitary.xml](Feature/Apparel/Armor_ScoutAndMilitary.xml)：哨骑装甲/头盔、军用头盔。本文件 5 个定义。
- [ThingDefs_MiscAndUnfinished.xml](Feature/Apparel/ThingDefs_MiscAndUnfinished.xml)：防弹背心与制作配方。本文件 2 个定义。

### Feature/Appearance

- [AlienRace_Binguin.xml](Feature/Appearance/AlienRace_Binguin.xml)：★ **种族本体**（HAR `AlienRace.ThingDef_AlienRace`）：体型、肤色通道、舒适温度、身体/头部贴图路径、`graphicPaths`。本文件 1 个定义。
- [HairDefs_Binguin.xml](Feature/Appearance/HairDefs_Binguin.xml)：2 个 HairDef（绒羽短发 `Mop`、羽冠 `Crest`）。本文件 2 个定义。

### Feature/Band

- [BandVisit_Binguin.xml](Feature/Band/BandVisit_Binguin.xml)：演唱会地标、5 件乐器、JobDef、ThoughtDef。本文件 12 个定义。

### Feature/Diplomacy

- [Endgame_Binguin.xml](Feature/Diplomacy/Endgame_Binguin.xml)：飞天决战线（发射舱、飞船零件）+ 科技。本文件 4 个定义。
- [FactionDefs_Binguin.xml](Feature/Diplomacy/FactionDefs_Binguin.xml)：中立派系「冰鹅」：科技等级、`pawnGroupMakers`、商队配置。本文件 1 个定义。
- [FactionDefs_PlayerBinguin.xml](Feature/Diplomacy/FactionDefs_PlayerBinguin.xml)：玩家可玩的冰鹅族派系。本文件 1 个定义。
- [JobDefs_Binguin.xml](Feature/Diplomacy/JobDefs_Binguin.xml)：外交官交谈任务。本文件 1 个定义。
- [LetterDefs_Binguin.xml](Feature/Diplomacy/LetterDefs_Binguin.xml)：3 个自定义信件样式。本文件 3 个定义。
- [PawnKindDefs_Binguin.xml](Feature/Diplomacy/PawnKindDefs_Binguin.xml)：定居者、外交官、商人、coser 与商队领袖。本文件 6 个定义。
- [PawnKindDefs_Colonists.xml](Feature/Diplomacy/PawnKindDefs_Colonists.xml)：玩家开局可选的殖民者模板。本文件 2 个定义。
- [RulePackDefs_SettlementNames.xml](Feature/Diplomacy/RulePackDefs_SettlementNames.xml)：据点名规则（`XX观察站`，XX 取自屈原诗歌名词）。本文件 2 个定义。
- [Scenarios_Binguin.xml](Feature/Diplomacy/Scenarios_Binguin.xml)：开局剧本。本文件 1 个定义。

### Feature/Floors

- [TerrainDefs_Floors.xml](Feature/Floors/TerrainDefs_Floors.xml)：冰面 / 雪花 / 精致冰雪三种地板 + 建筑分类标签。本文件 4 个定义。

### Feature/Food

- [ThingDefs_Foods.xml](Feature/Food/ThingDefs_Foods.xml)：芝士条、鱼干。本文件 4 个定义。

### Feature/Furniture

- [ThingDefs_Furniture.xml](Feature/Furniture/ThingDefs_Furniture.xml)：冰鹅床、椅、凳、桌、矮柜、床头柜。本文件 6 个定义。

### Feature/GateShield

- [Apparel_GateShield.xml](Feature/GateShield/Apparel_GateShield.xml)：寒门盾（护盾腰带）。本文件 3 个定义。

### Feature/IceCombat

- [AbilityDefs_IceCrown.xml](Feature/IceCombat/AbilityDefs_IceCrown.xml)：冰冠 +「投掷冰块」技能。本文件 2 个定义。
- [Armor_ScoutAndMilitary.xml](Feature/IceCombat/Armor_ScoutAndMilitary.xml)：灵袍与冰冠装备。本文件 2 个定义。
- [Weapons_Advanced.xml](Feature/IceCombat/Weapons_Advanced.xml)：突击步枪、精确步枪。本文件 8 个定义。
- [Weapons_LaserGatling.xml](Feature/IceCombat/Weapons_LaserGatling.xml)：冰鹅级极激急击机枪（**英文名 GGGGGG**）。本文件 5 个定义。
- [Weapons_PenguinKick.xml](Feature/IceCombat/Weapons_PenguinKick.xml)：企鹅飞踢！！（含音效定义）。本文件 5 个定义。

### Feature/Production

- [ThingDefs_AtmosphereController.xml](Feature/Production/ThingDefs_AtmosphereController.xml)：垃圾回收器。本文件 1 个定义。
- [ThingDefs_Buildings.xml](Feature/Production/ThingDefs_Buildings.xml)：高级排污泵、采切石一体机、垃圾回收器、缝纫台、机工台、大型信号发射器。本文件 4 个定义。
- [ThingDefs_IceRockAndStone.xml](Feature/Production/ThingDefs_IceRockAndStone.xml)：灵造冰岩、冰岩砖。本文件 2 个定义。
- [ThingDefs_MeteorGuide.xml](Feature/Production/ThingDefs_MeteorGuide.xml)：陨石引导器 + 相关工作。本文件 3 个定义。
- [WorkGiverDefs_Binguin.xml](Feature/Production/WorkGiverDefs_Binguin.xml)：机工台和裁缝台的账单工作。本文件 2 个定义。

### Feature/RaceTraits

- [GeneDefs_Binguin.xml](Feature/RaceTraits/GeneDefs_Binguin.xml)：6 个基因 + 1 个异种（冰鹅种）+ 相关心情。本文件 8 个定义。
- [HediffDefs_BionicEye.xml](Feature/RaceTraits/HediffDefs_BionicEye.xml)：野外作战仿生眼 + 手术。本文件 3 个定义。

### Feature/Raids

- [CryoSiege_Binguin.xml](Feature/Raids/CryoSiege_Binguin.xml)：低温围攻策略 + 大气冷冻器 + 游戏状态。本文件 3 个定义。
- [FactionDefs_BinguinHostile.xml](Feature/Raids/FactionDefs_BinguinHostile.xml)：敌对版本 + 好感情境（`GoodwillSituationDef`）。本文件 3 个定义。
- [PawnKindDefs_Binguin.xml](Feature/Raids/PawnKindDefs_Binguin.xml)：民兵、士兵、工兵、哨骑、领袖护卫、领袖与巡护员。本文件 7 个定义。
- [RaidStrategyDefs_Binguin.xml](Feature/Raids/RaidStrategyDefs_Binguin.xml)：2 个袭击策略。本文件 2 个定义。
- [WasteRetaliation_Binguin.xml](Feature/Raids/WasteRetaliation_Binguin.xml)：毒垃圾报复事件的 Hediff（爆炸羊发狂）。本文件 1 个定义。

### Feature/Rods

- [JobDefs_Binguin.xml](Feature/Rods/JobDefs_Binguin.xml)：鱼竿装配与半成品续做任务。本文件 2 个定义。
- [WorkGiverDefs_Binguin.xml](Feature/Rods/WorkGiverDefs_Binguin.xml)：半成品鱼竿自动续做。本文件 1 个定义。

### Feature/Security

- [ThingDefs_Turrets.xml](Feature/Security/ThingDefs_Turrets.xml)：自动加特林塔、小型自动霰弹发射塔。本文件 6 个定义。

### Feature/Sliding

- [JobDefs_Binguin.xml](Feature/Sliding/JobDefs_Binguin.xml)：滑行任务与冰尘 Fleck。本文件 2 个定义。
- [ThingDefs_MiscAndUnfinished.xml](Feature/Sliding/ThingDefs_MiscAndUnfinished.xml)：企鹅滑板、移动与免伤效果、滑行飞行器。本文件 4 个定义。

### Feature/Storytelling

- [Storytellers_Binguin.xml](Feature/Storytelling/Storytellers_Binguin.xml)：自定义叙事者 + 立绘。本文件 1 个定义。

### Feature/Trading

- [TraderKindDefs_Binguin.xml](Feature/Trading/TraderKindDefs_Binguin.xml)：3 个商队类型（冰鹅据点 / 大宗杂货 / 战斗补给）+ 鱼与武器生成器。本文件 3 个定义。

### Feature/Weapons

- [Weapons_Basic.xml](Feature/Weapons/Weapons_Basic.xml)：冲锋枪、霰弹枪、杠杆步枪。本文件 9 个定义。

### Feature/Weather

- [ThingDefs_AtmosphereController.xml](Feature/Weather/ThingDefs_AtmosphereController.xml)：大气控制仪。本文件 1 个定义。

### Feature/WillingCannon

- [Armor_Willing.xml](Feature/WillingCannon/Armor_Willing.xml)：威灵装甲、威灵头盔（含真空抗性，需 Odyssey）。本文件 4 个定义。

## AdvancedFishing/Defs

### Feature/Fishing

- [Fishing_FishPond.xml](../AdvancedFishing/Defs/Feature/Fishing/Fishing_FishPond.xml)：Binguin_FishPond、Binguin_PondFish、Binguin_PondFishWork、Binguin_PondStock、Binguin_PondFeedWork、Binguin_PondFishingJoy。本文件 6 个定义。
- [Fishing_GettingStarted.xml](../AdvancedFishing/Defs/Feature/Fishing/Fishing_GettingStarted.xml)：Binguin_ResearchAdvancedFishing、Binguin_CrabTrap、Binguin_FishingBait、Binguin_FillFishingBait。本文件 4 个定义。

### Feature/RaceTraits

- [Fishing_StinkFishCan.xml](../AdvancedFishing/Defs/Feature/RaceTraits/Fishing_StinkFishCan.xml)：Binguin_StinkGas、Binguin_StinkFishCan、Bullet_Binguin_StinkFishCan、Binguin_StinkFishCan_Releasing_Base、Binguin_StinkFishCan_Releasing、Make_Binguin_StinkFishCan、Binguin_StinkFishCanTrap、Binguin_StinkFishSmell。本文件 8 个定义。

### Feature/Rods

- [Fishing_GettingStarted.xml](../AdvancedFishing/Defs/Feature/Rods/Fishing_GettingStarted.xml)：Binguin_FishingRod、Recipe_Binguin_FishingRod。本文件 2 个定义。
- [Fishing_RodsAndParts.xml](../AdvancedFishing/Defs/Feature/Rods/Fishing_RodsAndParts.xml)：Binguin_ResearchMasterFishing、Binguin_ResearchUltimateFishing、Binguin_RodAssemblyTable、Binguin_UnfinishedRod、Binguin_RodPartBase、Binguin_RodShaft_Long、Binguin_RodShaft_Short、Binguin_RodTip_Blade、Binguin_RodTip_Hammer、Binguin_RodHook_Straight、Binguin_RodHook_Curved、Binguin_RodBait、Binguin_AdvancedRod_Blade、Binguin_AdvancedRod_Hammer、Binguin_RodBaitJellyMood、Binguin_RodBaitHumanKill、Binguin_AbilityPierce、Binguin_AbilityHook、DoBillsBinguinRodAssembly。本文件 19 个定义。

### Feature/VoidSword

- [Fishing_RodsAndParts.xml](../AdvancedFishing/Defs/Feature/VoidSword/Fishing_RodsAndParts.xml)：Binguin_VoidSwordFish、Recipe_Binguin_VoidSwordFish、Binguin_FishBluntDamage、Binguin_AbilityVoidStrike。本文件 4 个定义。

## 查找与维护

- 查物品、建筑和数值：在两个 Defs 根目录搜索 `defName` 或中文名称。
- 查贴图：查看 `texPath`、`graphicPath`，实际资源位于对应加载根的 `Textures`。
- 查文字：Def 的 `label`、`description` 或 `Languages`；界面文本使用 Keyed 键。
- 查科技前置：查看 `researchPrerequisites` 或 `researchPrerequisite`。
- 移动文件后同步代码诊断与文档链接，但不要因为移动 XML 改变 C# 命名空间。
- 翻译目录继续按 Def 类型分类，规则见 [Languages/README.md](../Languages/README.md)。
