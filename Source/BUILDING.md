# 构建、打包和部署

项目仅支持 RimWorld 1.7，使用 .NET 10 SDK。安装 SDK 并克隆仓库后即可构建；无需准备游戏 DLL、HAR DLL 或本机配置文件。首次构建需要网络以还原 NuGet 包。

## 构建

从仓库根目录执行：

```powershell
dotnet build Source/Binguin/Binguin.Race.sln -c Release
# 或
.\compile_modSelf.bat Release
# 可选第二参数：将产物复制到指定目录（支持中文及空格）。
.\compile_modSelf.bat Release '<产物目录>'
.\Source\build.ps1 -Configuration Debug
```

引用固定为 `Krafs.Rimworld.Ref 1.7.4985-unstable` 和 `Lib.Harmony 2.4.1`。参考包版本与当前游戏构建对应；它只用于编译，不能用来启动游戏。HAR 是运行依赖，源码不直接引用其类型，因此编译不要求安装 HAR。

IDE、VS Code task 和脚本调用同一项目。C# 语言版本保留为 5。产物位于 `Source/Binguin/bin/<配置>/net10.0`，仅将 `Binguin.dll` 复制到根 `Assemblies`。游戏、Harmony 和参考包 DLL 不随模组发布。

`compile_modSelf.bat` 和 `deploy_modSelf.bat` 成功、失败都会显示结果并暂停，按键后才结束；失败时保留非零退出码。批处理会先切换到仓库根目录，支持从其他工作目录启动。自动化请直接调用对应 `.ps1`，不要使用带暂停的 `.bat`。

```powershell
# 可选：改用本地 RimWorld 1.7 根目录的引用。
.\Source\build.ps1 -GameDir '<游戏根目录>'
# 可选：产物重定向到独立目录。
.\Source\build.ps1 -OutputRoot '<产物目录>'
```

通常不需要 `Binguin.local.props`；模板仅用于个人显式覆盖，文件被 Git 忽略。公共项目不包含个人安装路径。

## 打包

```powershell
.\package_modSelf.ps1
.\package_modSelf.ps1 -OutputRoot '<打包目录>'
```

默认输出 `dist/Binguin`，包含 About、Assemblies、Defs、Patches、Languages、Sounds、Textures 和 LoadFolders.xml。不会包含源码、IDE 配置或开发任务文档。清单 `binguin-files.json` 记录本次拥有的文件；更新时只清理旧清单中已被移除的文件，保留用户自加歌曲等未登记文件。

旧清单在写入构建产物之前校验；清单读取兼容 Windows PowerShell 5.1 和 PowerShell 7，支持对同一目标重复打包、更新部署。

## 部署

```powershell
.\deploy_modSelf.bat
# 或
.\deploy_modSelf.ps1 -GameDir '<游戏根目录>'
.\deploy_modSelf.ps1 -DestinationRoot '<独立部署目录>'
```

默认读取 Steam 注册表及 libraryfolders.vdf 查找唯一的 1.7 安装，将完整模组部署到 `Mods/Binguin`。非 Steam 安装或存在多个安装时显式传参。部署会重新构建完整包，不只是复制 DLL；拒绝以游戏根目录或开发目录作为目标。不自动启动游戏。

## 验证边界

CI 自动还原依赖，验证 Debug、构建 Release 并提供完整模组 Artifact。编译、XML MCP 诊断、重定向部署和游戏实际加载是不同证据；发布前仍需游戏内检查相关玩法及正常存读档。
