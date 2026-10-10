# 冰鹅族 Binguin Race - 编译脚本
# 用法：
#   （推荐）在 PowerShell 里执行：
#     cd W:\dsh\zengbing\Binguin_Race_Mod\Source
#     .\build.ps1
#   （或）直接双击 build.ps1（会尝试自动找游戏目录，结束前等你按键，不会闪退）
#   （或）指定游戏目录：
#     .\build.ps1 -GameDir "G:\SteamLibrary\steamapps\common\RimWorld"
#
# 产物输出到 ..\Assemblies\Binguin.dll
# 要求：Windows 自带 .NET Framework 4.x（csc.exe），无需额外安装 VS。

param(
    # 留空则自动探测（常见盘符/路径，含 G:\SteamLibrary）
    [string]$GameDir = "",
    # 加 -NoPause 可跳过结尾的"按回车关闭"（在已有终端里运行时用）
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

function Write-ErrorAndPause {
    param([string]$Msg)
    Write-Host ""
    Write-Host ("[错误] " + $Msg) -ForegroundColor Red
    if (-not $NoPause) {
        Write-Host ""
        Write-Host "按回车键关闭窗口……" -ForegroundColor Yellow
        [void][Console]::ReadLine()
    }
    exit 1
}

# ---------- 0. 自动探测游戏目录 ----------
if ([string]::IsNullOrEmpty($GameDir)) {
    $candidates = @(
        # 常见 Steam 库位置（覆盖各盘符下的 SteamLibrary / Steam 两种命名）
        (Join-Path $env:SystemDrive "SteamLibrary\steamapps\common\RimWorld"),
        (Join-Path $env:SystemDrive "Steam\steamapps\common\RimWorld"),
        "D:\RimWorld",
        "G:\SteamLibrary\steamapps\common\RimWorld",
        "D:\SteamLibrary\steamapps\common\RimWorld",
        "E:\SteamLibrary\steamapps\common\RimWorld",
        "F:\SteamLibrary\steamapps\common\RimWorld",
        "C:\Program Files (x86)\Steam\steamapps\common\RimWorld",
        "C:\Program Files\Steam\steamapps\common\RimWorld"
    )
    # 再扫一遍所有固定盘符下的两种常见命名
    foreach ($drive in (Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Free -ne $null })) {
        $candidates += (Join-Path ($drive.Root) "SteamLibrary\steamapps\common\RimWorld")
        $candidates += (Join-Path ($drive.Root) "Steam\steamapps\common\RimWorld")
    }
    foreach ($c in ($candidates | Select-Object -Unique)) {
        if (Test-Path (Join-Path $c "RimWorldWin64_Data\Managed")) {
            $GameDir = $c
            break
        }
    }
    if ([string]::IsNullOrEmpty($GameDir)) {
        Write-ErrorAndPause "未能自动找到 RimWorld 安装目录，请用 -GameDir 参数指定（例如 -GameDir `"G:\SteamLibrary\steamapps\common\RimWorld`"）。"
    }
    Write-Host ("检测到游戏目录：{0}" -f $GameDir)
}

# ---------- 1. 定位游戏托管程序集 ----------
$managed = Join-Path $GameDir "RimWorldWin64_Data\Managed"
if (-not (Test-Path $managed)) {
    Write-ErrorAndPause "找不到游戏程序集目录：$managed"
}

# ---------- 2. 定位 csc.exe ----------
$csc = $null
$candidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
foreach ($c in $candidates) {
    if (Test-Path $c) { $csc = $c; break }
}
if (-not $csc) {
    Write-ErrorAndPause "未找到 csc.exe（需要 .NET Framework 4.x 运行时，Windows 10/11 自带）。"
}

# ---------- 3. 引用（只引用游戏专属程序集；mscorlib/System/System.Core 由 csc 自动引用，重复引用会报 CS1703） ----------
$refs = @(
    (Join-Path $managed "Assembly-CSharp.dll"),
    (Join-Path $managed "UnityEngine.CoreModule.dll"),
    (Join-Path $managed "UnityEngine.dll"),
    (Join-Path $managed "UnityEngine.IMGUIModule.dll"),
    (Join-Path $managed "Unity.Mathematics.dll"),
    (Join-Path $managed "UnityEngine.TextRenderingModule.dll")
)

# netstandard facade（.NET Framework 自带）：csc 解析 UnityEngine 类型签名时
# 需要 netstandard 引用，否则报 CS0012（如 FleckMaker/Mathf 相关）
$netstandardDll = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
if (-not (Test-Path $netstandardDll)) {
    $netstandardDll = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\netstandard.dll"
}
if (Test-Path $netstandardDll) {
    $refs += $netstandardDll
    Write-Host ("netstandard 引用：{0}" -f $netstandardDll)
}

# Harmony 引用（官方 Harmony mod = brrainz.harmony，工坊 2009463077，0Harmony v2.x / HarmonyLib）
# ★ 切勿引用 818773962（那是 HugsLib 的工坊页）附带的 0Harmony v1.2.0.1（1.x，命名空间 Harmony）——
#   编译能过，但运行时游戏加载的是 2.x，1.x 类型（Harmony.HarmonyPatch 等）解析失败 →
#   游戏启动 TypeLoadException 黑屏（2026-08-17 实测踩坑记录）
$harmonyDll = $null
$harmonyCandidates = @(
    "$PSScriptRoot\0Harmony.dll",
    (Join-Path $GameDir '..\..\workshop\content\294100\2009463077\Current\Assemblies\0Harmony.dll'),
    (Join-Path $GameDir '..\..\workshop\content\294100\2009463077\1.6\Assemblies\0Harmony.dll'),
    (Join-Path $GameDir '..\..\workshop\content\294100\2009463077\1.5\Assemblies\0Harmony.dll'),
    (Join-Path $GameDir '..\..\workshop\content\294100\2009463077\1.4\Assemblies\0Harmony.dll')
)
foreach ($c in $harmonyCandidates) {
    if (Test-Path $c) { $harmonyDll = Get-Item $c; break }
}
if (-not $harmonyDll) {
    # 全盘兜底：反射挑选 HarmonyLib（2.x）版本，避免引用到 1.x 老 dll
    $all0Harmony = Get-ChildItem (Join-Path $GameDir '..\..\workshop\content\294100') -Recurse -Filter '0Harmony.dll' -ErrorAction SilentlyContinue
    foreach ($d in $all0Harmony) {
        try {
            $asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($d.FullName)
            $types = $null
            try { $types = $asm.GetTypes() } catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types }
            if (($types | Where-Object { $null -ne $_ -and $_.Namespace -eq 'HarmonyLib' }).Count -gt 0) {
                $harmonyDll = $d
                break
            }
        } catch { }
    }
    if (-not $harmonyDll) { $harmonyDll = $all0Harmony | Select-Object -First 1 }
}
if ($harmonyDll) {
    $refs += $harmonyDll.FullName
    Write-Host ("Harmony 引用：{0}" -f $harmonyDll.FullName)
} else {
    Write-Warning '未找到 Harmony dll（0Harmony.dll / HarmonyLib.dll），含 Harmony 补丁的 C# 将无法编译。'
}

$refArgs = $refs | ForEach-Object { "/reference:`"$_`"" }

# ---------- 4. 源码 ----------
# ★ 2026-10-10：.cs 挪进了子目录 Source\Binguin\（配合 Binguin.Race.csproj），
#   所以这里要 -Recurse，否则一个都找不到（报"Source 目录下没有 .cs 文件"）。
#   排除 obj/bin/\.idea，免得把中间产物或 IDE 缓存里的 .cs 也编进去。
$src = Get-ChildItem -Path $PSScriptRoot -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue |
       Where-Object { $_.FullName -notmatch '\\(obj|bin|\.idea|\.vs)\\' } |
       Sort-Object FullName |
       ForEach-Object { "`"$($_.FullName)`"" }
if (-not $src) { Write-ErrorAndPause "Source 目录（含子目录）下没有 .cs 文件。" }
Write-Host ("源码文件：{0} 个" -f $src.Count)

# ---------- 5. 编译 ----------
$outDir = Join-Path (Split-Path $PSScriptRoot -Parent) "Assemblies"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outDll = Join-Path $outDir "Binguin.dll"

& $csc "/nologo" "/target:library" "/out:`"$outDll`"" $refArgs $src
if ($LASTEXITCODE -ne 0) {
    Write-ErrorAndPause "编译失败（csc 退出码 $LASTEXITCODE），请把上方红字错误发给我。"
}

Write-Host ""
Write-Host "编译成功：$outDll" -ForegroundColor Green
Write-Host "把整个 Binguin_Race_Mod 文件夹覆盖到 RimWorld\Mods\ 后即可在游戏内启用。"

if (-not $NoPause) {
    Write-Host ""
    Write-Host "按回车键关闭窗口……" -ForegroundColor Yellow
    [void][Console]::ReadLine()
}
