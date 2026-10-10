# 冰鹅族构建入口：统一调用 .csproj，需要 .NET SDK。
# 默认使用 Binguin/Binguin.local.props 中的个人路径。
# 示例：./Source/build.ps1 -NoPause
# 临时覆盖：./Source/build.ps1 -GameDir "D:/RimWorld" -HarDir "E:/Mods/HAR"
# 特殊 HAR 布局可使用 -HarAssemblyPath 直接指定 AlienRace.dll。
# 默认 Release；产物为模组根目录 Assemblies/Binguin.dll。
# -NoPause 为兼容旧调用保留；当前脚本不会等待按键。
param(
    [string]$GameDir = "",
    [string]$HarDir = "",
    [string]$HarAssemblyPath = "",
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoPause
)
$ErrorActionPreference = "Stop"
# 将参数传给同一项目；IDE、CLI、脚本使用相同引用与输出规则。
$buildArgs = @("build", (Join-Path $PSScriptRoot "Binguin\Binguin.Race.csproj"), "-c", $Configuration)
if ($GameDir) { $buildArgs += "-p:RimWorldDir=$GameDir" }
if ($HarDir) { $buildArgs += "-p:HarDir=$HarDir" }
if ($HarAssemblyPath) { $buildArgs += "-p:HarAssemblyPath=$HarAssemblyPath" }
& dotnet @buildArgs
exit $LASTEXITCODE
