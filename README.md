# 冰鹅族 Binguin Race — 环世界 1.6 种族 MOD 设计与实现

> ## 🙋 是来帮忙的？
>
> **先看 → [CONTRIBUTING.md](CONTRIBUTING.md)（协作者指南）**
>
> 里面按 **美术 / 程序 / 翻译** 三块列了**能直接上手的任务**：
> - 怎么把 Mod 跑起来（clone 即玩，已自带编译好的 DLL）
> - 每张贴图要放哪、多大、什么名字
> - 两个程序员任务：**重置钓竿穿刺技能** + **重置制作钓竿 UI**
> - 交东西的方式（GitHub 网页改 / PR / 直接发压缩包）
> - ⛔ **会弄坏 Mod 的 5 个坑**（不看会白干）
>
> 贴图缺口清单（自动生成，**别手抄**）：[贴图需求总表.md](贴图需求总表.md)
>
> ⚠️ 本 README 是**设计与实现文档**（28 KB，给改代码的人看）。只想帮忙做点事的话，看协作者指南就够。
>
> ---

一个基于 **Humanoid Alien Races 2.0（外星人框架）** 的环世界（RimWorld）1.6 种族 MOD：
企鹅娘种族「冰鹅族」，中立派系「冰鹅」，据点名为「XX观察站」（XX 取自屈原诗歌中的名词），
并带有开局三天内上门询问「共存意向」的外交事件。

> **结构依据**：本 MOD 的种族定义已按 HAR 官方 Wiki（Getting-Started / General-Settings /
> Body-Addons / Graphic-Paths / 1.4/1.5/1.6 迁移指南）核对重写，关键结论见 §10。

---

## 1. 需求 → 实现 对照表

| 需求 | 实现 |
|---|---|
| 环世界 **1.6** 版本 | `About.xml` 声明 `<supportedVersions><li>1.6</li></supportedVersions>`；全部逻辑使用 1.6 原生 API |
| **全部 DLC** | 无任何 DLC 硬依赖；与 Royalty / Ideology / Biotech / Anomaly / **Odyssey** 兼容（DLC 内容一律 MayRequire 门控，未装则不加载，见 §8） |
| 外星人框架 | 依赖 **Humanoid Alien Races 2.0**（HAR2），种族定义使用 `AlienRace.ThingDef_AlienRace` |
| 种族名 **冰鹅族** | ThingDef `Binguin`，label「冰鹅族」，谐《楚辞·招魂》「增冰峨峨，飞雪千里些」之音（双关：增冰鹅鹅）；首项科技即名「增冰鹅鹅」 |
| 原型为 **企鹅娘**（不是企鹅） | 继承 Human 基础 + 体型 0.9、企鹅白肤色通道、耐寒（舒适温度下限 -45℃）、性别比 9:1（女:男）。★ **鸟喙 bodyAddons 已按用户要求移除**（节点与 beak_*.png 贴图均已删除） |
| 派系名 **冰鹅** | FactionDef `Binguin`，中立定居型，科技等级 Industrial，开局必生成据点（`requiredCountAtGameStart=1`） |
| 据点名 **XX观察站** | `FactionDef.settlementNameMaker` → `BinguinSettlementNameMaker`（NameRule_Tabular：`XX` + `观察站`），XX 来自 44 个屈原诗歌名词（见 §5） |
| 开局 **三天内**派外交官 | `GameComponent_BinguinDiplomacy` 计时，第 3 天（180000 tick）触发，沿玩家殖民地边缘生成一名 `Binguin_Diplomat` 外交官（只发**到达通知信**，不自动弹选项） |
| **毒垃圾报复事件**（2026-09） | 玩家把毒垃圾丢在世界地图上害到冰鹅族时：**50%** 冰鹅特制报复 —— 运输舱空投 **200~300 袋毒垃圾 + 10 只发狂爆炸羊**到玩家**财富最高的房间**正中央；另外 **50%** 走原版 `PollutionRaid`（普通袭击）。爆炸羊 = 落地即永久发狂、1.5 小时内失血致死、有皮外伤但**绝不打腿**（保证能移动）。挂点：前缀拦原版 `CompDissolutionEffect_Goodwill.TriggerRetaliationEvent` |
| 点击外交官**进入事件** | 选中一名殖民者 → **右键点击外交官** → 殖民者走到她面前（`FloatMenuOptionProvider_BinguinDiplomacy` + 交谈 Job）→ 才弹出共存意向选择信 |
| 愿意共存 **+50** | 选择「我们愿意与冰鹅族共存」→ 立即 `TryAffectGoodwillWith(玩家, +50)`，外交官逗留一天后**走回地图边缘再离场** |
| 保持中立 **±0** | 选择「我们保持中立」→ **好感不变**，外交官稍作停留后离场 |
| 无意共存 **-75** | 选择「很抱歉，我们无意与贵方共存」→ 好感**不立即扣**：等外交官**走出地图边缘**离场时才结算 `-75`（`DepartDiplomat` 内触发） |
| 霰雪无垠**蓝图**（2026-09） | 该研究 techprintCount=1 → 原版自动生成科技图纸 Techprint_Binguin_ResearchEndlessSnow（市价 3000）；稀有贸易商 30%（稀有品商队/轨道稀有品商）、**冰鹅族据点 70%** 概率出售（StockGenerator_BinguinBlueprint，运行时挂载；需 Royalty） |
| 野外作战仿生眼（2026-09） | 植入体：**射击精度 +2**（ShootingAccuracyPawn，等同射击技能+2）＋ **开枪无视天气精度减益**；物品 玻璃钢20+高级零部件6+零部件8、工作量 30000；装配台制作（需原版【仿生体】+ 冰鹅族【霰雪无垠】双研究），医疗床手术装到【眼睛】部位（Defs/BionicEye_Binguin.xml + Source/Patch_BinguinEye*.cs） |
| 大气控制仪改版（2026-09） | 改为 **充能 24 小时才能操控一次天气**；充能中 700W，**充满自动进入低耗能模式 10W**；用掉后立刻重新充能；停电/关开关则充能暂停。旧版"用后 12 小时耗 4000W"已删除（Source/Comp_BinguinWeather.cs + Defs/Buildings2_Binguin.xml） |
| 单位模板（2026-09） | 9 个 PawnKindDef：外交官/商人/coser×2/商队领袖/民兵/士兵/哨骑/领袖护卫/领袖，各自固定服装（apparelRequired，士兵用本 mod 的冰鹅族防弹背心 Binguin_FlakVest）+ 指定武器（专属 weaponTags）；派系 pawnGroupMakers 同步重排；通讯台招募默认冰鹅族殖民者（Defs/PawnKinds_Binguin.xml + Faction_Binguin.xml + Source/Patch_BinguinPlayerRecruit.cs） |
| 冰鹅族头盔（2026-09） | 军用头盔：原版防弹头盔 ×1.1（装甲倍率 0.77、耐久 132、工作量 8800；材料与原版防弹头盔相同：40 金属+玻璃钢10+零部件2）+ 寒抗 20 / 热抗 10（绝对值）；覆盖 FullHead；冰鹅机工台制作（机加工+服装双研究）；士兵标配（Defs/Armor_Binguin.xml） |
| 袭击编成（2026-09） | 单位点数 民兵60/士兵100/哨骑140/护卫180/领袖250；门槛按**策略计算前**点数：哨骑≥2000、护卫≥6000、领袖≥8000；每场护卫≤2、领袖≤1；民兵/士兵/哨骑 30% 背包（护盾背包每场≤2~4）、士兵小概率火箭筒、民兵/士兵 50% 原版工业武器；**领袖必带护盾背包**（护卫腰带位被寒门盾占用故不发）、**毒环境士兵脱头盔改戴防毒面具**；全员携带 **1~3 份食物**（普通单位简单/精致随机，领袖与商队领袖奢侈食物）（Source/RaidComposition_Binguin.cs） |
| 冰鹅族工兵（2026-09） | 特殊士兵：**建造技能必定 11~20**；只在**破墙袭击 / 工兵袭击 / 特殊围攻（待做）**出现，其它袭击自动降级成士兵；破墙时装备**手榴弹**，工兵袭击时装备**护盾腰带 + 量产型钓竿**；服装同士兵（Defs/PawnKinds_Binguin.xml + Source/RaidComposition_Binguin.cs） |
| 低温围攻（2026-09） | 新建筑**大气冷冻器**（2×2，75钢+2高级零部件+20玻璃钢，工作量 7500≈3小时，运转后全图 −30℃）；新策略**低温围攻**（原版围攻 AI + 我们的蓝图：冷冻器+2台自动加农炮+沙袋；**袭击点数 ×0.5**；减员30%或工兵全灭→冲锋；仅冰鹅族可用）（Defs/CryoSiege_Binguin.xml + Source/CryoSiege_Binguin.cs） |
| cos服抗寒（2026-09） | ⑨服 / 大酱服 额外 **+30℃** 绝对耐寒（叠加材质保暖：布料制 = 36+30 = 66℃）（Defs/Cosplay_Binguin.xml） |
| 威灵套**真空抗性**（2026-09） | 威灵装甲 +31%、威灵头盔 +68%（equippedStatOffsets/VacuumResistance，MayRequire Odyssey 门控） |
| 企鹅连体衣（2026-09） | 外套（Shell 层）· 120 纤维或皮革 · 覆盖照抄原版布卡罩袍（FullHead/UpperHead/Neck/Shoulders/Torso/Legs/Arms）· 材质系数：护甲×30%、隔冷×200%、隔热×100% · 冰鹅缝纫台 + 原版缝纫台，解锁=冰鹅族服装（Defs/Jumpsuit_Binguin.xml） |
| 头饰两件（2026-09） | **冰鹅族雪花绒帽**（25 纤维/皮革、HP88）· **冰鹅族绒羽头冠**（40 纤维/皮革、HP110）：Overhead 层、覆盖 UpperHead；属性 = 各自贴图来源的原版帽子 ×1.1（绒帽 护甲0.22/隔冷0.55/隔热0.11；羽冠 护甲0.22/隔冷0.11/隔热0.165）；冰鹅缝纫台制作、冰鹅族服装解锁（Defs/Headwear_Binguin.xml） |
| 职业装五件（2026-09） | 冰鹅族**JK服**（+1 魅力 PawnBeauty）· **机工装**（+10% 手工 GeneralLaborSpeed、+10% 机械维修 MechRepairSpeed[Biotech]）· **医生服**（+10% 治疗速度、+10% 治疗质量）· **研究服**（+10% 研究速度、+10% 骇入 HackingSpeed）· **军服**（护甲材料系数 30%、额外 25 钢铁）；另**冰鹅族常服 +5% 全局工作效率**（WorkSpeedGlobal）；除军服外数值全部照抄常服（60 纤维/皮革、OnSkin、HP110、2000 工作量）；均在冰鹅缝纫台制作、冰鹅族服装解锁（Defs/Uniforms_Binguin.xml） |
| 冰冠技能「投掷冰块」（2026-09） | 装备冰冠即获得：投出 **5 块灵造冰岩**（空地十字分布/狭窄处挤在一起，不改地形），冰岩 **440 耐久、6 小时**消融；**3 次充能、每 1 小时恢复 1 次**；冰冠另给 **+20 心灵熵阈值 / +0.1 心灵熵消散速率**（需 Royalty） |
| 灵质法袍 + 冰冠（2026-09） | 同属【冰鹅族护甲】研究、冰鹅族机工台制作：**灵质法袍**（Shell 层，70 纤维/皮革 + 15 玻璃钢 + 2 高级零部件，覆盖 躯干/颈/双肩/双臂/双腿，工作量 10000，护甲×48%/隔冷×400%/隔热×155%）；**冰冠**（Overhead 头饰层，45 金属 + 2 高级零部件，覆盖 UpperHead=头/双耳，护甲×89%/隔冷×200%/隔热×100%，社交效果 +20%） |
| 冰鹅族cos服（2026-09） | 支线研究（前置冰鹅族服装，100 点，坐标 (3.0,0.8)）→ 解锁 企鹅连体衣 + ⑨服 + 大酱服 三件 cos 装；⑨服/大酱服数值与覆盖全部参考连体衣（120 纤维或皮革 · Shell · 护甲×30%、隔冷×200%、隔热×100%） |
| 冰鹅族风尘/风雪大衣（2026-09） | 两件外套（Shell）· 各 85 纤维或皮革 · 风尘大衣覆盖 躯干/颈/双肩/双臂/双腿、工作量 10000、护甲×42%/隔冷×200%/隔热×200%；风雪大衣覆盖 躯干/颈/双肩/双臂、工作量 8000、护甲×42%/隔冷×350%/隔热×50%；均装备 3 秒（Defs/Coats_Binguin.xml） |
| **钓到企鹅！**（2026-09-24） | 钓鱼时有 **0.78%** 概率把渔获换成一名**冰鹅族难民**（**麻痹症 2 天**，倒地待救援）；**60 天冷却**。难民是和平派系平民（定居者/外交官/商人/Coser 两姐妹），**排除领袖**（`Binguin_Leader` 与 `Binguin_CaravanLeader`）。抬回床上救起来就是原版那套救援/招募流程——**可以选择帮助并招募，也可以只救援不招募**。生效范围：**原版钓鱼区 + 本 mod 鱼池**（**蟹笼故意不挂**：它自动产出，挂了会变成挂机刷冰鹅）。挂点 = `FishingUtility.GetCatchesFor`（照原版"稀有渔获"形态）+ `JobDriver_BinguinPondFish`（Source/Fishing_Binguin.cs） |
| **作战商卖冰鹅族武器**（2026-09-26） | 战斗补给商队另带 **2~5 种不同的**冰鹅族枪械，每种 **1~2 把**。只卖**冷冻武器及以下**：冲锋枪 / 霰弹枪 / 杠杆步枪 / 突击步枪 / 精确步枪；**霰雪无垠层级（企鹅飞踢！！、极激急击机枪）不卖**，钓竿等近战工具也不卖。排除规则**数据驱动**（按配方研究前置过滤），以后新增的高阶武器自动不卖。需 Odyssey 无依赖，自定义 `StockGenerator_BinguinWeapons`（Source/TraderStock_BinguinWeapons.cs，运行时挂到 `Binguin_Caravan_CombatSupplier`） |
| **冰鹅商人卖鱼**（2026-09-26） | 冰鹅派系的**据点商人**（每次补货）与**商队**（大宗杂货 + 战斗补给）都会带鱼：**2~4 种鱼、每种 10~20 个**（`categoryDef=Fish`，自动涵盖全部鱼种）。**稀有品贸易商按用户要求不带鱼**。做法：新建 3 个 `TraderKindDef`（照抄原版 stockGenerators 整段 + 追加鱼生成器），**原版 def 一字未改** ⇒ 别的派系完全不受影响。需 Odyssey；未装时鱼生成器按 `MayRequire` 跳过、退化成原版行为（Defs/TraderKinds_Binguin.xml，由 Tools\Make-BinguinTraderKinds.py 生成） |
| **女性身形 4/5 游戏内切换**（2026-09） | 两套女身形贴图都打进 mod，在 **选项 → Mod 设置 → 冰鹅族** 里一键切换（默认身形 4「瘦一点」，身形 5「肚子更圆」）。切换时**身体和自家服饰一起换**（两套宽度差 11%，不换会撑破衣服），**立刻生效、不用重开游戏**。只影响【女性】体型的冰鹅；纤细体型与婴儿/小孩不受影响；**原版服饰完全不受影响**。（Source/BinguinMod.cs + Source/BinguinBodySwitch.cs；贴图 BinguinBody_Female5_* / Tribalwear5_* / FlakVest5_*） |

## 2. 种族设定：冰鹅族（企鹅娘）

- **外观**：继承人类基础体型（BodySize 0.9）；肤色通道改为企鹅白（90% 亮白 / 10% 灰白）；
  头戴鸟喙附加件（`bodyAddons`，1.5+ 语法，贴图待美术）；性别比 9:1 女:男（`maleGenderProbability=0.1`）。
- **耐性**：舒适温度 -45℃ ~ 25℃，极耐寒、略怕热——寒原观测者的设定。
- **食性**：杂食（企鹅娘是"带企鹅特征的人类"，不必像真企鹅那样只吃肉）。
- **生物特性**：`bloodDef=Filth_Blood`（1.5 起必须显式指定才能流血/治伤）、`renderTree=Humanlike`、
  `gestationPeriodDays=18`（与人类一致的妊娠期）、`humanRecipeImport=true`（继承全部人类手术配方）。
- **特长**：定居者/领袖/外交官模板偏重 `Social / Intellectual / Research`（观测、记录、交涉的职业气质）。

## 3. 派系设定：冰鹅

- 中立定居型派系，科技等级 **Industrial**（观测站科技），开局必生成据点（1~3 个）。
- 领袖头衔「首席观察员」，据点显示名「观察站」。
- 成员：共 **13** 个 PawnKindDef（Defs/PawnKinds_Binguin.xml）——定居者 / 外交官 / 商人 / coser×2（⑨服·大酱服）/ 商队领袖 / 民兵 / 士兵 / **工兵** / 哨骑 / 领袖护卫 / 领袖 / 巡护员；
  全部通过 `<race>Binguin</race>` 指定为冰鹅族，含 1.4+ 必填字段 `initialResistanceRange`；武器用专属 weaponTags 精确指定（见 §1 表格）。

## 4. 外交事件机制

```
游戏开始 (startTick = TicksGame)
   └─ 第 3 天 (3 × 60000 tick) ──► TrySendDiplomat()
         ├─ 找到冰鹅派系（不存在/已被灭则静默跳过）
         ├─ 在地图边缘生成外交官（Binguin_Diplomat，Social 8）
         └─ 只发【到达通知信】（不自动弹共存选项——2026-08-22 改）
玩家操作：选中一名殖民者 → 右键点击外交官 → 走近交谈（JobDriver_BinguinDiplomatTalk）
         └─ 到面前才弹出选择信件「冰鹅族外交使节」
               ├─ [我们愿意与冰鹅族共存] → 好感立即 +50，逗留 1 天 → 走出地图离场
               ├─ [我们保持中立]         → 好感不变，稍作停留 → 走出地图离场
               └─ [很抱歉，我们无意与贵方共存] → 好感先不扣，等外交官走出地图边缘时
                  才结算 -75（DepartDiplomat 触发）→ 走出地图离场
```

- 用 **GameComponent** 实现（而非随机事件），保证**必定在三天内**触发、且每局只触发一次。
- 状态（开始 tick、是否已触发、答复选项、离场倒计时）全部通过 `ExposeData` 存档，读档不会重复触发。
- 好感变化调用原生的 `Faction.TryAffectGoodwillWith`，与世界好感系统完全兼容：
  **愿意共存 +50 在答复时立即生效；保持中立 0；无意共存 -75 延迟到外交官
  走出地图边缘离场时才结算**（`DepartDiplomat`，带防重复标记）。
- ★ **2026-08-22 进入方式改版**：到达只发通知信；殖民者右键点击外交官（1.6
  `FloatMenuOptionProvider` 自动注册的子类）→ 交谈 Job（走近）→ 才弹共存选择信。
  相关文件：`Source/FloatMenuOptionProvider_BinguinDiplomacy.cs`、
  `Source/JobDriver_BinguinDiplomatTalk.cs`、Defs 里 `Binguin_DiplomatTalk` JobDef。
- 答复后外交官会沿路走回地图边缘再消失（`JobDefOf.Goto`）；未答复则原地等待；
  离开提示信在真正离场时才弹出。**注意：`leavingTick` 初始必须为 -1 且以 `>= 0` 判断答复状态**，
  否则外交官生成后下一 tick 就被误删（早期版本的严重 bug，已修复）。

## 5. 据点词汇表（XX观察站，均为屈原诗歌中的名词）

| 词汇 | 出处 | 词汇 | 出处 |
|---|---|---|---|
| 昆仑 | 离骚「邅吾道夫昆仑兮」 | 赤豹 | 九歌·山鬼「乘赤豹兮从文狸」 |
| 阆风 | 离骚「登阆风而绁马」 | 文狸 | 九歌·山鬼「乘赤豹兮从文狸」 |
| 县圃 | 离骚「夕余至乎县圃」 | 辛夷 | 九歌·山鬼「辛夷车兮结桂旗」 |
| 增城 | 天问「增城九重，其高几里」 | 桂旗 | 九歌·山鬼「辛夷车兮结桂旗」 |
| 不周 | 离骚「路不周以左转兮」 | 洞庭 | 九歌·湘夫人「洞庭波兮木叶下」 |
| 阊阖 | 离骚「倚阊阖而望予」 | 沅湘 | 九歌·湘君「令沅湘兮无波」 |
| 天津 | 离骚「朝发轫于天津兮」 | 芳洲 | 九歌·湘君「采芳洲兮杜若」 |
| 西极 | 离骚「夕余至乎西极」 | 杜若 | 九歌·湘君「采芳洲兮杜若」 |
| 扶桑 | 离骚「总余辔乎扶桑」 | 沧浪 | 楚辞·渔父「沧浪之水清兮」 |
| 若木 | 离骚「折若木以拂日」 | 烛龙 | 天问「日安不到？烛龙何照」 |
| 瑶台 | 离骚「望瑶台之偃蹇兮」 | 雷渊 | 招魂「旋入雷渊，爢散而不可止些」 |
| 白水 | 离骚「朝吾将济于白水兮」 | 幽都 | 招魂「君无下此幽都些」 |
| 赤水 | 离骚「遵赤水而容与」 | 飞雪 | 招魂「增冰峨峨，飞雪千里些」 |
| 流沙 | 离骚「忽吾行此流沙兮」 | 霰雪 | 九章·涉江「霰雪纷其无垠兮」 |
| 云霓 | 离骚「帅云霓而来御」 | 深林 | 九章·涉江「深林杳以冥冥兮」 |
| 九河 | 九歌·河伯「与女游兮九河」 | 玄武 | 远游「召玄武而奔属」 |
| 龙堂 | 九歌·河伯「鱼鳞屋兮龙堂」 | 玄冥 | 远游「历玄冥以邪径兮」 |
| 紫贝 | 九歌·河伯「紫贝阙兮珠宫」 | 湘灵 | 远游「使湘灵鼓瑟兮」 |
| 珠宫 | 九歌·河伯「紫贝阙兮珠宫」 | 冯夷 | 远游「令海若舞冯夷」 |
| 文鱼 | 九歌·河伯「乘白鼋兮逐文鱼」 | 北斗 | 九歌·东君「援北斗兮酌桂浆」 |
| 天狼 | 九歌·东君「举长矢兮射天狼」 | 彗星 | 九歌·少司命「登九天兮抚彗星」 |
| 九州 | 九歌·大司命「纷总总兮九州」 | 飘风 | 九歌·大司命「令飘风兮先驱」 |

> 说明：《招魂》《远游》《渔父》与屈原的关系虽有争议，但传统上均归于屈原名下；
> 「冰鹅」本身即出自《招魂》，与种族名完美呼应。名词风格兼顾「观测/天文」（北斗、天狼、彗星、天津、阊阖、玄武）
> 与「寒原/水域」（飞雪、霰雪、玄冥、龙堂、珠宫、冯夷），契合企鹅娘的观测者设定。

## 6. 文件结构

```
Binguin_Race_Mod/                     （共 39 个 Defs + 3 个 AdvancedFishing/Defs + 57 个 .cs）
├── About/            About.xml（元数据/依赖/描述）、Preview.png
├── Defs/             38 个 xml：种族 / 派系 / 13 单位 / 基因 / 科技 / 建筑 / 家具 / 地板 /
│                     武器（基础 3 把 + 冰爆突击/精确 + 企鹅飞踢 + 极激急击机枪）/
│                     服装（常服·连体衣·cos服·大衣·职业装·头饰·法袍·冰冠）/ 护甲（哨骑·威灵·寒门盾）/
│                     仿生眼 / 低温围攻 / 飞天决战线 / 叙述者 / 剧本 / 信件 / WorkGiver …
├── AdvancedFishing/  ★ 仅在装 Odyssey 时加载（LoadFolders.xml 门控）：蟹笼 / 打窝用具 /
│                     量产型钓竿 / 钓竿装配台 / 配件 / 鱼饵 / 鱼池（3 个 xml）
├── Source/           57 个 .cs（详见各文件头部注释）+ build.ps1（一键编译）
│                     核心：GameComponent_BinguinDiplomacy（外交+飞天状态机）、
│                     BinguinDefPatches（[StaticConstructorOnStartup] 运行时挂载全部自定义类型）、
│                     HarmonyPatches_Binguin（滑冰/免疫/贴地/仿生眼/招募/袭击/围攻补丁）、
│                     Comp_BinguinWillingCannon、Comp_BinguinGateShieldField、CryoSiege_Binguin、
│                     RaidComposition_Binguin、Mothership_Binguin、Comp_BinguinFishPond 等
├── Assemblies/       Binguin.dll（编译产物）
├── Textures/         全部贴图（Apparel/Building/Fleck/Floor/Item/Projectile/UI）
├── Sounds/           GuGuGaGa.mp3（企鹅飞踢发射音）
├── Languages/        ★ 界面/对话/信件/笑话的文案词条（Keyed）：
│                     ChineseSimplified/Keyed/Binguin.xml + English/Keyed/Binguin.xml（内容相同，
│                     中文原文）。Defs 里的 label/description 仍直写在各 Defs xml 内。
├── LoadFolders.xml   ★ Odyssey 门控（About.xml 不支持 loadFolders！）
└── README.md         本文件
```
> ★ **XML 零自定义类型原则**：所有 def 只引用原版类型，自定义 Comp/Verb/JobDriver/workerClass
>   一律由 `Source/BinguinDefPatches.cs`（[StaticConstructorOnStartup]）在运行时挂载 ——
>   详见《会话交接文档》§4.1。

> ★ **文本与翻译（2026-09）**：C# 里**不再硬编码任何玩家可见文案** —— 界面按钮、对话框、
>   信件、心情提示、心情/检查面板、356 条冷笑话等共 **728 条**全部改成
>   `"Binguin_Xxx_NN".Translate()`，正文放在 `Languages/<语言>/Keyed/Binguin.xml`。
>   **要改文案或做英文翻译，只改词条文件，不用碰代码、不用重新编译。**
>   日志（`Log.Message` 等）、异常文本、Harmony 补丁标签、`[DebugAction]` 特性实参、
>   内部查表键（如天气名）**故意保留中文**——它们不是玩家可见正文。
>   自检脚本：`Tools/check_keyed_consistency.py`（键是否一一对应）、
>   `Tools/extract_text_to_keyed.py`（抽取，★ 已有词条时会拒绝二次执行以防键编号错乱）。

## 7. 安装与编译

### 安装（无需编译也可先测试 XML 部分）
1. 前置：Steam 创意工坊订阅 **Harmony**（官方页工坊 id 2009463077，0Harmony 2.x / HarmonyLib）与
   **Humanoid Alien Races 2.0**（工坊 id 839005762）。
2. 将整个 `Binguin_Race_Mod` 文件夹复制到 `RimWorld/Mods/` 下。
3. 游戏内「Mods」列表启用本 MOD（排在 HAR2 之后，`loadAfter` 已声明）。

### 编译 C#（外交事件需要）
```powershell
cd Binguin_Race_Mod\Source
.\build.ps1 -GameDir "你的 RimWorld 安装目录"
```
> 使用 Windows 自带的 .NET Framework csc.exe，无需 VS。产物为 `Assemblies\Binguin.dll`。
> 不编译也能进游戏看种族/派系，但三天外交事件不会触发。

### 测试要点
- 开局 3 天后地图边缘出现一名企鹅娘外交官 + 「冰鹅族外交官来访」**到达通知信**
  （不再自动弹共存选项）；
- 选中殖民者右键点击外交官 → 应出现「与冰鹅族外交官交谈」→ 殖民者走近后弹出
  「冰鹅族外交使节」选择信（读档不重复触发、外交官一直等到答复为止）；
- 选愿意共存：好感 +50（立即）；选保持中立：好感不变；选无意共存：好感不立即扣，
  等外交官**走出地图边缘**离场时才 -75；
- 答复后外交官应**走回地图边缘再消失**，并弹出「冰鹅族外交官离去了」信件；
- 世界生成时冰鹅派系拥有据点，名字为「XX观察站」；
- Architect 菜单有「冰鹅建筑」类别：冰面/雪花/精致冰雪三种地板
  （冰面 4铁/美观2/清洁0.6/工作量10、雪花 4铁/美观2/工作量10、精致 8铁/美观3/清洁0.2/工作量20；
  完整定义见 `Defs/Floors_Binguin.xml`），全部不减移速且可触发滑冰；
- 企鹅滑板（`Defs/ThingDefs_Binguin.xml`）：腰部装备，移速 +50%，装备命令「滑行冲刺」
  半径 10 格瞬移、CD 10 秒（TableMachining 制作：30 钢 + 2 工业组件）；
- 快速滑冰：冰/雪/水面/冰鹅地板 = **平地速度**（日志出现「[冰鹅族] 滑冰补丁已挂载」即生效）；
- 冰鹅族小人应显示为「冰鹅种」异种并携带 12 个基因（若看到重复基因，删除
  `Race_Binguin.xml` 中 `raceGenes` 节点后重测，见《会话交接文档》§4.3）。

## 8. 兼容性说明（全部 DLC）

| DLC | 说明 |
|---|---|
| Royalty | 种族继承 Human，可正常授予头衔；如需更多定制可查 HAR 的 Royalty-Compatibility 页 |
| Ideology | 无冲突；`bodyAddons` 默认可在造型台自定义（`userCustomizable` 默认 true） |
| Biotech | 已配置 `gestationPeriodDays`、`humanRecipeImport`、`maleGenderProbability`；可用 `raceGenes` 加种族基因 |
| Anomaly | 无冲突；事件使用原生好感 API，不会被异常体事件干扰（活铁 Bioferrite = 用户口中的「异铁」） |
| Odyssey | 钓鱼/鱼/水域/真空抗性等内容的载体：进阶与高级钓鱼学、鱼池、蟹笼、打窝用具全部放在
   `AdvancedFishing/` 并由 **LoadFolders.xml** 按 Odyssey 门控；威灵套真空抗性、企鹅飞踢音效等
   用 `MayRequire="Ludeon.RimWorld.Odyssey"` 就地门控，未装 DLC 不加载、不报错 |

## 9. 待核对 / 待办清单

1. **HAR 依赖的 packageId** ✅ 已确认：`erdelf.HumanoidAlienRaces`
   （依据你提供的 HAR 官方 `About.xml`，已写入本 MOD 的 `About.xml` 依赖声明与 loadAfter）。
2. **据点命名字段** ✅ 已确认：`settlementNameMaker` 存在且指向 **RulePackDef**
   （对照原版 `Factions_Misc.xml` 的 `NamerSettlementOutlander` 与 `RulePacks_Namers_Factions.xml`，
   命名规则关键词为 `r_name`，已按此重写 `NameMakers_Binguin.xml`）。
3. **美术贴图（最重要待办）**：★ 鸟喙附加件已按用户要求**移除**（节点与 `beak_*.png` 均已删除，
   如需恢复见《会话交接文档》§4.4 与 `Tools/Make-BinguinPlaceholderTextures.ps1`）。
   当前待补的是**企鹅娘专属外观**（身体/头贴图，现为占位）、连体衣/cos服/职业装/头饰的
   **专属 worn 贴图**（现借原版或常服贴图）——路径约定见 `Textures/README.txt`。
4. **体验打磨（部分完成）**：外交官离场已改为「走回地图边缘再消失」✅；剩余：
   同意共存后送一份鱼礼（Odyssey `Fish_*`）、英文翻译（目前文本为中文直写）、
   Ideology 专属 meme、About 预览图已生成占位版（`About/Preview.png`）。
5. **扩展方向**：Biotech `raceGenes`（耐寒/企鹅特征）、1.6 新字段 `forceGender`（如需纯女性种族）、
   Ideology 专属 meme、英文翻译（目前文本为中文直写）。

## 10. 官方参考核对结论（重要）

依据 HAR 官方 Wiki + 你提供的 Miho 1.6 种族 + 原版 Core 参考（`wiki-tools/_vanilla_refs/`）逐条核对：

- **种族结构**：`<AlienRace.ThingDef_AlienRace ParentName="Human">`，**不覆盖 `<race>` 节点**
  （完整继承人类的生命阶段/思维树/食谱/年龄曲线/手术配方），HAR 字段放
  `<alienRace><generalSettings>`；**AlienPartGenerator 在 1.5+ 已从 ThingComp 迁移到
  `<generalSettings><alienPartGenerator>`**（Player.log 确认旧 comp 写法无效）。
- **舒适温度**：用 ThingDef 级 `<statBases>` 的 `ComfyTemperatureMin/Max`（Miho/原版 Human 验证写法）；
  注意覆盖 statBases 会整体替换父类列表，已按原版 Human 列全。
- **颜色通道**：`<first Class="ColorGenerator_Options">` + RGBA 权重（Miho/Raven 验证写法），
  `<second>` 可省略。
- **身体附加件**：1.5+ 语法（`<path>` 必填、`<alignWithHead>` 头部附加件必填、
  `<conditions><BodyPart><bodyPart>Head</bodyPart></BodyPart>` 挂载部位），已按此写鸟喙。
- **PawnKindDef**：`<race>` 指定种族 ✓；`initialResistanceRange`/`minGenerationAge` 有效；
  `skillGains`/`wildSpawnChance` 在 1.6 已移除（Player.log 确认）。
- **FactionDef**：`pawnsPlural`（非 pawnPlural）、`settlementNameMaker`(RulePackDef)、
  `pawnGroupMakers` 内联 + 字典式 options（kindDef: Combat/Peaceful/Trader/Settlement）、
  `baseTraderKinds`/`visitorTraderKinds`（traderKinds 已移除）、`backstoryFilters`、
  `maxPawnCostPerTotalPointsCurve`/`raidLootValueFromPointsCurve`（配置检查必需）。
- **TraderKindDef**：`StockGenerator_SingleDef` 用 `<thingDef>`+`<countRange>`（非 def/totalPriceRange），
  买入用 `StockGenerator_BuyExpensiveSimple`/`StockGenerator_BuyTradeTag`（`StockGenerator_Buy` 已移除）。
- **LetterDef**：1.6 仅保留 `letterClass`/`color`/`flashColor`（canBeDismissed 等已移除）。
- **命名**：1.6 无 NameMakerDef；据点命名 = `settlementNameMaker` → RulePackDef，入口规则 `r_name`。
