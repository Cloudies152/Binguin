# XML 类型与可选依赖

自定义组件和工作驱动直接在 Def 中声明。类型使用完整命名空间和程序集名：

```xml
<driverClass>Binguin.Feature.Rods.JobDriver_BinguinAssembleRod, Binguin</driverClass>
<li Class="Binguin.Feature.Rods.CompProperties_BinguinRodAssembly, Binguin" />
```

本机 RimWorld 1.6 的 `DirectXmlToObject.ClassTypeOf` 使用 `GenTypes.GetTypeInAnyAssembly` 解析 `Class`。该解析器只自动补充原版命名空间，无法把短类名推断为 `Binguin.Feature.*`；程序集限定名还可以通过 `Type.GetType` 解析，不依赖自定义类型是否出现在 `AllTypes` 缓存中。

游戏在 XML 加载前创建 Mod 实例并加载程序集。因此不要用原版类型占位，再在启动静态构造中更换类型；也不要在加载中清空类型缓存。历史注释中“缓存固化，所以 XML 不得引用自定义类型”的结论已废弃。过去每一次类型错误的具体原因无法仅凭历史注释确定；实际排查应检查类型名称、DLL 构建产物、缺失程序集及游戏日志中的加载异常。

## DLC 门控

- `AdvancedFishing` 加载根继续由 `LoadFolders.xml` 的 Odyssey 条件控制，其中的 Def、Patch 和贴图随功能一起加载。
- 根目录中引用钓竿功能的 JobDef、WorkGiverDef 使用 `MayRequire="Ludeon.RimWorld.Odyssey"`。
- 垃圾回收器依赖 Biotech 的 Wastepack；基因定义和 HAR 的 `raceGenes` 同样用 Biotech 门控。
- 科技图纸库存生成器使用 Royalty 门控，运行时按名称查找游戏生成的 Techprint，避免 XML 引用尚未生成的 Def。
- `MayRequire` 控制内容是否加载，不能用于隐藏本模组自身类型的拼写或编译错误。

## 共享 Def 与继承

本模组自有 Def 的组件、技能效果和参数直接写入 `Defs/Feature`。原版 StatPart、商人科技图纸库存和派系袭击权重分别放在 `AdvancedFishing/Patches/Feature/Rods`、`Patches/Feature/Trading`、`Patches/Feature/Raids`，由原版 XML Patch 在对象创建前处理。

高级钓竿和尚方宝剑使用 `comps Inherit="false"`，显式保留 BaseWeapon 的 Forbiddable、Styleable 和本定义的品质组件，用自定义 Equippable 子类替代原版 Equippable，避免出现两个装备组件。

钓竿 StatPart 使用原版 `parentStat`。游戏创建 StatWorker 时绑定该字段，首次属性查询再缓存对应分支；不能在 XML 默认构造函数里读取尚未绑定的属性。

验证应包含实际程序集类型解析、组件属性读取、XML Patch 和继承结果。构建通过仍须重启游戏检查加载及实际功能，尤其是 DLC 开关组合。
