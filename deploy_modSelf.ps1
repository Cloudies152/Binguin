param(
    [string]$GameDir = '',
    [string]$DestinationRoot = '',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Source/BuildPaths.ps1')
if (-not $DestinationRoot) {
    $game = Find-BinguinGame -GameDir $GameDir
    $DestinationRoot = Join-Path $game 'Mods/Binguin'
}
$destination = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\','/')
$source = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\','/')
if ($destination -eq $source -or -not (Split-Path -Leaf $destination)) { throw '部署目标必须是独立的模组目录。' }
if ((Test-Path -LiteralPath (Join-Path $destination 'Assembly-CSharp.dll')) -or (Test-Path -LiteralPath (Join-Path $destination 'Source'))) { throw '禁止把游戏根目录或开发目录当作部署目标。' }
& (Join-Path $PSScriptRoot 'package_modSelf.ps1') -OutputRoot $destination -Configuration $Configuration
Write-Host "部署完成：$destination"
