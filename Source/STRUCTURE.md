# C# 代码目录约定

代码位于 `Source/Binguin`。优先按功能归类，功能内部可以同时包含 Patch、Comp、
JobDriver、窗口与工具类；不根据类名前缀把同一功能拆散。

## 目录职责

- `Feature/<功能>`：具体玩法及其配套代码。当前包括 Band（乐队）、Diplomacy
  （外交与母舰）、Fishing（鱼池与捕鱼设施）、Rods（组合鱼竿）、Raids（袭击）、
  Trading（交易）、Sliding（滑行）、IceCombat（冰系战斗）、VoidSword（虚空剑）、
  WillingCannon（自愿炮）、GateShield（护盾）、Production（生产建筑）、Weather
  （天气控制）、Appearance（外观）和 RaceTraits（种族特性）。
- `Util`：跨功能可复用的类型或组件，例如 `DefLookup<T>`。
- `Helper`：跨功能的辅助函数入口，例如种族与派系判定。
- `Patch`：跨功能的补丁注册或暂时无法归属单一功能的补丁。
  `HarmonyPatches_Binguin` 保留跨功能的 Harmony 注册。Def 的组件与类型直接在 XML 声明，原版共享 Def 的调整放在按功能分类的 XML Patch 中。
- `Comps`：不属于特定玩法的通用 Comp，例如装备时附加 Hediff。
- `Core`：模组入口与设置。

功能专用工具仍放在功能目录，例如 `BinguinRodUtility` 留在 `Feature/Rods`。
无需提前建立空目录；以后出现通用 JobDriver、窗口等代码，再增加对应目录。
`Util` 与 `Helper` 的区别是主要职责，不强求每个类完全没有状态。

## 重构约束

命名空间与相对 `Source/Binguin` 的目录对应，例如 `Feature/Fishing` 使用
`Binguin.Feature.Fishing`，`Helper` 使用 `Binguin.Helper`。保留原有类型短名。
XML 的完整类型名和跨目录 using 必须同步更新；本项目不保留旧存档兼容层。
XML 的类型引用、存档与 Harmony 注册依赖类型身份；移动文件改变命名空间时，需检查字符串类型引用和注册入口。
SDK 自动收集子目录源码，构建入口和输出位置不变。
Def 声明、程序集限定类型名和 DLC 门控见 [XML 类型与依赖](XML_DEFS.md)。

提取公共逻辑前先核对行为，而不是只看代码形状相似。首次提取的 `DefLookup<T>`
保留了两个鱼池 WorkGiver 的缺失重试策略，以及 JoyGiver 只查询一次的策略。
不合并不同的鱼类判定、派系条件、缓存生命周期或异常处理，除非确认语义一致。

本轮保留各源码文件的功能说明和历史注释；注释里旧文件名仍可按文件名搜索。
大型补丁注册文件和单文件多类型的进一步拆分应独立处理，避免目录移动与行为修改混在一起。

## 日志约定

统一通过 `Binguin.Helper.BinguinLogUtility` 输出日志，业务代码不直接调用 `Verse.Log`。
工具类统一添加 `[冰鹅族] ` 前缀，调用方只传消息正文。
`Log(message)` 默认是调试消息，只在 Debug 构建输出。
警告使用 `Log(message, severity: 1, isDebug: false)`，错误使用
`Log(message, severity: 2, isDebug: false)`，两者在 Release 中也输出。
需要保留的普通消息可指定 `isDebug: false`。
`WarningOnce(message, key)` 转发 Verse 的去重机制，默认在两种构建中均输出。
