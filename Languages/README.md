# 翻译目录规范

`Languages/<语言>/Keyed` 保存界面翻译键；`DefInjected/<Def类型>` 保存定义字段翻译。
这是游戏的翻译定位格式，不采用 `Feature` 分类，也不跟随 C# 命名空间改名。

例如 `DefInjected/ThingDef` 中的 `Binguin.label` 指的是 `defName=Binguin`
的 ThingDef 字段，不是 C# 类型 `Binguin`；不要改成 `Binguin.Feature.*.label`。
HAR 的 `AlienRace.ThingDef_AlienRace` 继承 ThingDef，对应翻译仍放 `ThingDef`。

源码命名空间调整时，检查真正的完整类型引用（Class 属性或 *Class 字段），
不批量替换 Def 翻译键。本轮未发现 Languages 中残留的旧源码完整类型名。

同一语言、翻译类别和键只能定义一次。已清理英文 DefInjected 中的重复键，
保留文件中首次出现的文案；后续修改该条目，不在末尾追加同名条目。
游戏自动生成的 Make_* 配方不一定在 XML 中显式声明，校验引用时需区别对待。

已移除 ThingDef 翻译目录中误放的两条 Recipe_Binguin_LaserGatling 翻译；
正确的 RecipeDef 目录已包含这些字段。
