# Defs — 目录说明

游戏内容的**定义**（物品、建筑、武器、服装、科技…）。RimWorld 用 XML 描述游戏内容，
每个文件里装着一批 `<defName>` 条目。

> **命名惯例参考**：原版 `Core/Defs/` 用 `ThingDefs_Races` / `PawnKindDefs` / `Apparel` /
> `ResearchProjectDefs` / `FactionDefs` 这种 **Def 类型名**做目录；
> 本目录沿用同一套（NewRatkinPlus、Miho 等主流种族 mod 也是这个做法）。

---

## ⚠️ 两条规则

1. **目录名和文件名可以随便改** —— 游戏**递归扫描**这个目录，
   原版和全部 DLC 都用了子目录（Royalty 46 个、Odyssey 70 个）。
2. **`<defName>` 绝对不能改** —— 那才是真正的唯一 ID，存档、其他 def、C# 代码都靠它引用。

---

## 目录一览

| 目录 | 装什么 | 文件数 |
|---|---|---|
| [`01_RaceAndAppearance/`](#01_raceandappearance) | 种族本体、头型、发型、面部动画、基因 | 5 |
| [`02_Factions/`](#02_factions) | 三个派系、据点命名、信件样式 | 5 |
| [`03_PawnKinds/`](#03_pawnkinds) | 单位模板（外交官、士兵、商人…） | 2 |
| [`04_Apparel/`](#04_apparel) | 职业装、大衣、头饰、连体衣、cos 服 | 5 |
| [`05_Weapons/`](#05_weapons) | 4 把远程武器 + 企鹅飞踢 | 4 |
| [`06_ArmorAndImplants/`](#06_armorandimplants) | 护甲、威灵套、寒门盾、冰冠技能、仿生眼 | 5 |
| [`07_Buildings/`](#07_buildings) | 建筑、家具、地板、冰岩石料、陨石引导器 | 6 |
| [`08_Security/`](#08_security) | 自动炮塔 | 1 |
| [`09_Research/`](#09_research) | 完整科技树 | 1 |
| [`10_FoodAndJoy/`](#10_foodandjoy) | 食物、乐队来访与演唱会 | 2 |
| [`11_World/`](#11_world) | 开局剧本、叙事者、商队货单 | 3 |
| [`12_Incidents/`](#12_incidents) | 袭击策略、低温围攻、毒垃圾报复、飞天决战线 | 4 |
| [`13_WorkAndRecipes/`](#13_workandrecipes) | 工作定义、工作分配、杂物 | 3 |

**合计 46 个 xml**

---

### 01_RaceAndAppearance

| 文件 | 里面是什么 |
|---|---|
| `AlienRace_Binguin.xml` | ★ **种族本体**（HAR `AlienRace.ThingDef_AlienRace`）：体型、肤色通道、舒适温度、身体/头部贴图路径、`graphicPaths` |
| `HeadTypeDefs_Binguin.xml` | 自定义头型 `Binguin_Head_Average` |
| `HairDefs_Binguin.xml` | 2 个 HairDef（绒羽短发 `Mop`、羽冠 `Crest`） |
| `FacialAnimation_Binguin.xml` | Nals.FacialAnimation 分层（头 / 眼 / 眉 / 眼皮 / 嘴） |
| `GeneDefs_Binguin.xml` | 6 个基因 + 1 个异种（冰鹅种）+ 相关心情 |

### 02_Factions

| 文件 | 里面是什么 |
|---|---|
| `FactionDefs_Binguin.xml` | 中立派系「冰鹅」：科技等级、`pawnGroupMakers`、商队配置 |
| `FactionDefs_BinguinHostile.xml` | 敌对版本 + 好感情境（`GoodwillSituationDef`） |
| `FactionDefs_PlayerBinguin.xml` | 玩家可玩的冰鹅族派系 |
| `RulePackDefs_SettlementNames.xml` | 据点名规则（`XX观察站`，XX 取自屈原诗歌名词） |
| `LetterDefs_Binguin.xml` | 3 个自定义信件样式 |

### 03_PawnKinds

| 文件 | 里面是什么 |
|---|---|
| `PawnKindDefs_Binguin.xml` | **13 个 PawnKindDef**：定居者 / 外交官 / 商人 / coser×2 / 商队领袖 / 民兵 / 士兵 / 工兵 / 哨骑 / 领袖护卫 / 领袖 / 巡护员 |
| `PawnKindDefs_Colonists.xml` | 玩家开局可选的殖民者模板 |

### 04_Apparel

| 文件 | 里面是什么 |
|---|---|
| `Apparel_Uniforms.xml` | JK服、机工装、医生服、研究服、军服、常服 |
| `Apparel_Coats.xml` | 风尘大衣、风雪大衣 |
| `Apparel_Headwear.xml` | 雪花绒帽、绒羽头冠、红围巾 |
| `Apparel_Jumpsuit.xml` | 企鹅连体衣 |
| `Apparel_Cosplay.xml` | ⑨服、大酱服 |

### 05_Weapons

| 文件 | 里面是什么 |
|---|---|
| `Weapons_Basic.xml` | 冲锋枪、霰弹枪、杠杆步枪 |
| `Weapons_Advanced.xml` | 突击步枪、精确步枪 |
| `Weapons_PenguinKick.xml` | 企鹅飞踢！！（含音效定义） |
| `Weapons_LaserGatling.xml` | 冰鹅级极激急击机枪（**英文名 GGGGGG**） |

### 06_ArmorAndImplants

| 文件 | 里面是什么 |
|---|---|
| `Armor_ScoutAndMilitary.xml` | 哨骑装甲/头盔、军用头盔 |
| `Armor_Willing.xml` | 威灵装甲、威灵头盔（含真空抗性，需 Odyssey） |
| `Apparel_GateShield.xml` | 寒门盾（护盾腰带） |
| `AbilityDefs_IceCrown.xml` | 冰冠 +「投掷冰块」技能 |
| `HediffDefs_BionicEye.xml` | 野外作战仿生眼 + 手术 |

### 07_Buildings

| 文件 | 里面是什么 |
|---|---|
| `ThingDefs_Buildings.xml` | 高级排污泵、采切石一体机、垃圾回收器、缝纫台、机工台、大型信号发射器 |
| `ThingDefs_AtmosphereController.xml` | 大气控制仪、大气冷冻器 |
| `ThingDefs_Furniture.xml` | 冰鹅床、椅、凳、桌、矮柜、床头柜 |
| `TerrainDefs_Floors.xml` | 冰面 / 雪花 / 精致冰雪三种地板 + 建筑分类标签 |
| `ThingDefs_IceRockAndStone.xml` | 灵造冰岩、冰岩砖 |
| `ThingDefs_MeteorGuide.xml` | 陨石引导器 + 相关工作 |

### 08_Security

| 文件 | 里面是什么 |
|---|---|
| `ThingDefs_Turrets.xml` | 自动加特林塔、小型自动霰弹发射塔 |

### 09_Research

| 文件 | 里面是什么 |
|---|---|
| `ResearchProjectDefs_Binguin.xml` | **12 个科技**（含「增冰鹅鹅」「霰雪无垠」等）+ 研究标签 |

### 10_FoodAndJoy

| 文件 | 里面是什么 |
|---|---|
| `ThingDefs_Foods.xml` | 芝士条、鱼干 |
| `BandVisit_Binguin.xml` | 演唱会地标、5 件乐器、JobDef、ThoughtDef |

### 11_World

| 文件 | 里面是什么 |
|---|---|
| `Scenarios_Binguin.xml` | 开局剧本 |
| `Storytellers_Binguin.xml` | 自定义叙事者 + 立绘 |
| `TraderKindDefs_Binguin.xml` | 3 个商队类型（冰鹅据点 / 大宗杂货 / 战斗补给）+ 鱼与武器生成器 |

### 12_Incidents

| 文件 | 里面是什么 |
|---|---|
| `RaidStrategyDefs_Binguin.xml` | 2 个袭击策略 |
| `CryoSiege_Binguin.xml` | 低温围攻策略 + 大气冷冻器 + 游戏状态 |
| `WasteRetaliation_Binguin.xml` | 毒垃圾报复事件的 Hediff（爆炸羊发狂） |
| `Endgame_Binguin.xml` | 飞天决战线（发射舱、飞船零件）+ 科技 |

### 13_WorkAndRecipes

| 文件 | 里面是什么 |
|---|---|
| `JobDefs_Binguin.xml` | 4 个 JobDef + FleckDef |
| `WorkGiverDefs_Binguin.xml` | 3 个 WorkGiverDef |
| `ThingDefs_MiscAndUnfinished.xml` | 未完成品、Hediff、配方等零散内容 |

---

## 钓鱼子模块

`AdvancedFishing/Defs/` —— **只有装了 Odyssey DLC 才加载**（由根目录 `LoadFolders.xml` 门控）：

| 目录 | 文件 | 装什么 |
|---|---|---|
| `01_GettingStarted/` | `Fishing_GettingStarted.xml` | 入门钓鱼学、蟹笼、打窝用具、量产型钓竿 |
| `02_RodsAndParts/` | `Fishing_RodsAndParts.xml` | 进阶/终极钓鱼学、装配台、8 种配件、高级钓竿、尚方宝剑 |
| `03_FishPond/` | `Fishing_FishPond.xml` | 鱼池系统 + 钓鱼作为娱乐 |
| `04_StinkFishCan/` | `Fishing_StinkFishCan.xml` | 臭鱼罐头（迫击炮弹 + 陷阱 + 臭气心情） |

---

## 想找某个东西在哪？

| 你要找… | 怎么做 |
|---|---|
| **某个物品/建筑的数值** | 在这个目录里搜它的 **`defName`**（比如 `Binguin_GateShield`），或搜中文名 |
| **某个东西的贴图路径** | 找到它的 def，看 `<texPath>` / `<graphicPath>` |
| **它的中文名 / 说明** | 大多直接写在 def 的 `<label>` / `<description>` 里；界面文字在 `Languages/` |
| **它的科技前置** | 看 def 里的 `<researchPrerequisites>` |
| **哪些东西用它** | 全局搜 `<defName>`（比如 `Binguin_FlakVest`），或搜 C# 里的字符串 |

> 💡 也可以在 `Languages/ChineseSimplified/Keyed/Binguin.xml` 里搜中文，拿到键名，
> 再回 `Source/` 搜键名，就知道是哪段代码在用它。
