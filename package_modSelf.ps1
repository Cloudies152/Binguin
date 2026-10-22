param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'dist/Binguin'),
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\','/')
$destination = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\','/')
if ($destination -eq $projectRoot -or $projectRoot.StartsWith($destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '打包目录不能是项目本身或项目的父目录。' }
$files = @('Assemblies/Binguin.dll', 'LoadFolders.xml')
foreach ($folder in @('About','Defs','Patches','Languages','Sounds','Textures')) {
    Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File -Recurse | ForEach-Object {
        $files += $_.FullName.Substring($projectRoot.Length + 1).Replace('\','/')
    }
}
$manifestPath = Join-Path $destination 'binguin-files.json'
# 只清理上一次清单拥有、且这次已移除的文件，绝不镜像删除整个目录。
$obsolete = @()
if (Test-Path -LiteralPath $manifestPath) {
    # PowerShell 5.1 的 ConvertFrom-Json 将数组作为单个管道对象返回；
    # 外包 @() 会产生嵌套数组，让 Join-Path 收到整份清单。直接赋值后逐项遍历。
    $previous = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $manifestPath -Raw)
    foreach ($relative in $previous) {
        if ($relative -isnot [string] -or [string]::IsNullOrWhiteSpace($relative)) { throw '旧清单包含无效文件名，已停止打包。' }
        $target = [IO.Path]::GetFullPath((Join-Path $destination $relative))
        if (-not $target.StartsWith($destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '旧清单含越界路径，已停止打包。' }
        if ($files -notcontains $relative) { $obsolete += $target }
    }
}
# 先校验旧清单，再生成或更新目标目录中的产物。
New-Item -ItemType Directory -Force -Path $destination | Out-Null
& (Join-Path $PSScriptRoot 'Source/build.ps1') -Configuration $Configuration -OutputRoot $destination
foreach ($target in $obsolete) { if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force } }
foreach ($relative in $files) {
    if ($relative -eq 'Assemblies/Binguin.dll') { continue }
    $target = Join-Path $destination $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot $relative) -Destination $target -Force
}
ConvertTo-Json -InputObject @($files | Sort-Object -Unique) | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "完整模组已打包：$destination"
