# 项目协作规范

适用于整个仓库。项目为 RimWorld 1.6 冰鹅族模组，C# 源码位于 `Source/Binguin`。
详细说明见 [源码结构](Source/STRUCTURE.md) 和 [构建说明](Source/BUILDING.md)。

## 目录与命名空间

- 优先按功能归类：`Feature/<功能>` 包含该功能的 Patch、Comp、JobDriver、窗口及专用工具，不按类名前缀拆散功能。
- `Util/` 放跨功能复用的类型、组件，例如 `DefLookup<T>`。
- `Helper/` 放跨功能辅助函数入口，例如种族、派系判定和日志工具。
- 根级 `Patch/`、`Comps/` 放跨功能或无法归属具体功能的代码；其他通用类别按需增加，不提前建空目录。
- `Core/` 放模组入口与设置。功能专用工具留在功能目录，例如 `Feature/Rods` 中的 `BinguinRodUtility`。
- 命名空间必须对应相对 `Source/Binguin` 的目录：`Feature/Fishing` → `Binguin.Feature.Fishing`，`Helper` → `Binguin.Helper`。
- 移动、重命名类型时同步更新跨目录 `using`、XML 完整类型名、反射字符串及补丁注册入口。
- 当前无需旧存档兼容；不添加旧命名空间或类型兼容层。程序集名称仍为 `Binguin`。

## 通用逻辑与工具类

- 新增逻辑前搜索已有实现；种族判定使用 `BinguinRaceUtility`，派系判定使用 `BinguinFactions`。
- 相同职责、相同行为的重复逻辑应提取复用；单一功能内部复用的代码留在该功能目录。
- 不因为代码形状相似就合并不同业务规则；核对空值、缓存生命周期、缺失重试、异常处理和存档行为。
- 按名称懒加载 Def 可使用 `Binguin.Util.DefLookup<T>`，明确指定 `retryIfMissing`，保留原有缺失重试策略。
- 不为了目录整理改动玩法数值或业务行为。

## 日志

- 所有业务日志必须使用 `Binguin.Helper.BinguinLogUtility`；只有该工具类可直接调用 `Verse.Log`。
- 工具类统一添加 `[冰鹅族] ` 前缀，调用方只传正文，不重复写模组前缀。
- `BinguinLogUtility.Log(message)`：默认调试消息，仅 Debug 构建输出。
- `BinguinLogUtility.Log(message, isDebug: false)`：需要在 Release 保留的普通消息。
- `BinguinLogUtility.Log(message, severity: 1, isDebug: false)`：警告，两种构建均输出。
- `BinguinLogUtility.Log(message, severity: 2, isDebug: false)`：错误，两种构建均输出。
- `BinguinLogUtility.WarningOnce(message, key)`：保留 Verse 的按 key 去重行为，默认两种构建均输出；不要随意改变已有 key。
- 不将实际异常、缺失依赖或补丁失败仅作为调试消息输出。

## 构建与多人协作

- 保留当前 `net48` 和 C# 5 设置；本机 HAR 1.6 面向 net48。调整框架或语言版本须有明确需求和验证。
- 每人通过被 Git 忽略的 `Source/Binguin/Binguin.local.props` 配置 `RimWorldDir`、`HarDir`，特殊布局用 `ManagedDir`、`HarAssemblyPath`。
- 公共 `.csproj`、模板和 task 不写个人绝对路径；不提交本机 props、游戏/HAR DLL、`bin/`、`obj/` 或编译产物。
- 从仓库根目录执行 `dotnet build Source/Binguin/Binguin.Race.sln -c Release`；日志条件编译相关改动同时验证 `-c Debug`。
- VS Code 使用 `build: Release`、`build: Debug`；Release 为默认任务。`Source/build.ps1` 调用同一项目。
- 构建仅复制本模组 DLL 到 `Assemblies`；两种配置都更新该文件，以最后一次构建为准。
- 编译成功不等于游戏内验证通过；报告实际做过的验证，明确尚未验证的加载或运行行为。

## 注释与文档

- 保留协作者需要的注释和 Markdown 文档，不为压缩文件随意删除。
- 错误或过时的说明可以用简洁、准确的中文重写；配置与行为变更同步更新相关说明。
- 避免顺手修改无关文件；提交前检查差异，只提交本次任务相关文件。
