# 冰鹅族 1.7 构建入口：默认自动还原固定版本的编译引用。
# -GameDir 仅用于显式改用本地游戏引用；-OutputRoot 重定向完整产物位置。
param(
    [string]$GameDir = "",
    [string]$OutputRoot = "",
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoPause
)
$ErrorActionPreference = "Stop"
# 将参数传给同一项目；IDE、CLI、脚本使用相同引用与输出规则。
$buildArgs = @("build", (Join-Path $PSScriptRoot "Binguin\Binguin.Race.csproj"), "-c", $Configuration)
if ($GameDir) { $buildArgs += "-p:RimWorldDir=$GameDir" }
if ($OutputRoot) { $buildArgs += "-p:CustomOutputDir=$OutputRoot" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码：$LASTEXITCODE" }
