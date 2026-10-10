# 构建与多人协作

项目面向 RimWorld 1.6，使用 .NET Framework 4.8（本机 HAR 1.6 DLL 的目标框架）。安装 .NET SDK 后，
Visual Studio、Rider、dotnet CLI 与 `build.ps1` 使用同一份项目配置。

## 每位协作者只需配置一次

将 `Binguin/Binguin.local.props.example` 复制为同目录的
`Binguin.local.props`，填写自己的 `RimWorldDir` 和 `HarDir`。

- `RimWorldDir`：游戏根目录，包含 `RimWorldWin64_Data/Managed`。
- `HarDir`：HAR 模组根目录，例如创意工坊的 `294100/839005762`。
  项目引用其 `1.6/Assemblies/AlienRace.dll`，请勿选择旧版子目录。
- 如果目录结构不同，可以直接指定 `ManagedDir` 和 `HarAssemblyPath`。
- 本机配置由 `.gitignore` 排除。提交 `.csproj` 和 `.props.example`，
  不提交个人绝对路径、游戏 DLL 或 HAR DLL。

从模组根目录执行：

```powershell
dotnet build Source/Binguin/Binguin.Race.sln -c Release
```

也可以执行 `./Source/build.ps1 -NoPause`。脚本需要 .NET SDK，
不再使用 Windows 自带的旧版 csc。`-NoPause` 为兼容旧调用保留，脚本不等待按键。

## VS Code 构建任务

在 VS Code 中打开模组根目录，完成上述本机配置后，按 `Ctrl+Shift+B`，
执行默认的 `build: Release` 任务。通过“终端 → 运行任务”可选择
`build: Debug` 或 `build: Release`；“终端 → 配置默认生成任务”可更改默认项。
两个任务均使用 PATH 中的 `dotnet`，并将编译诊断显示在“问题”面板。
两种配置都会更新模组的 `Assemblies/Binguin.dll`，以最后一次构建为准。
任务不包含个人绝对路径；协作者共用 `.vscode/tasks.json`，各自维护本机 props。

临时覆盖路径（命令行属性优先于本机配置）：

```powershell
dotnet build Source/Binguin/Binguin.Race.sln -c Release `
  -p:RimWorldDir="D:/Games/RimWorld" `
  -p:HarDir="E:/SteamLibrary/steamapps/workshop/content/294100/839005762"
```

产物先生成在项目的 `bin/Release/net48`，再仅复制 `Binguin.dll` 到模组的
`Assemblies`。依赖引用均不随产物复制；运行游戏仍需安装 Harmony 和 HAR。
若仓库在游戏之外，先将完整模组安装到游戏，再通过本机配置的 `GameModsDir`
及 `-p:DeployToGame=true` 更新该安装中的 DLL。

## CI 与版本一致性

CI 使用固定的 RimWorld 参考包 `Krafs.Rimworld.Ref 1.6.4871`，
以及固定提交 `ae8412cdb4a047cb1a5d3945bebca44d061da569` 的官方 HAR。
CI 通过命令行传入临时依赖路径，不需要个人配置。
协作者应使用同一 RimWorld 版本和对应的 HAR 1.6；更新依赖时同步更新 CI。
Steam 工坊可能自动更新 HAR，本机路径一致并不意味着 DLL 版本完全一致。

## 源码结构

C# 文件按功能组织，目录职责与重构约束见 [STRUCTURE.md](STRUCTURE.md)。
