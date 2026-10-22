# 只查询安装信息；显式参数优先，不保存个人路径。
function Find-BinguinGame {
    param([string]$GameDir)
    if ($GameDir) {
        $resolved = (Resolve-Path -LiteralPath $GameDir).Path
        if (-not (Test-Path -LiteralPath (Join-Path $resolved 'Assembly-CSharp.dll'))) { throw 'GameDir 不是 RimWorld 1.7 游戏根目录。' }
        return $resolved
    }
    $steamRoots = @()
    if ($env:STEAM_PATH) { $steamRoots += $env:STEAM_PATH }
    foreach ($key in @('HKCU:\Software\Valve\Steam','HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        $entry = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        if ($entry.SteamPath) { $steamRoots += $entry.SteamPath }
        if ($entry.InstallPath) { $steamRoots += $entry.InstallPath }
    }
    if (${env:ProgramFiles(x86)}) { $steamRoots += Join-Path ${env:ProgramFiles(x86)} 'Steam' }
    $libraries = @($steamRoots)
    foreach ($steam in $steamRoots) {
        $vdf = Join-Path $steam 'steamapps/libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            $text = Get-Content -LiteralPath $vdf -Raw
            foreach ($match in [regex]::Matches($text, '"path"\s+"([^"]+)"')) {
                $libraries += $match.Groups[1].Value.Replace('\\','\')
            }
        }
    }
    $games = @($libraries | Select-Object -Unique | ForEach-Object {
        $candidate = Join-Path $_ 'steamapps/common/RimWorld'
        if ((Test-Path -LiteralPath (Join-Path $candidate 'Assembly-CSharp.dll')) -and (Test-Path -LiteralPath (Join-Path $candidate 'UnityStubs.dll'))) { [IO.Path]::GetFullPath($candidate) }
    } | Sort-Object -Unique)
    if ($games.Count -ne 1) { throw '未找到唯一的 RimWorld 1.7 安装。请传入 -GameDir；普通编译不需要游戏路径。' }
    return $games[0]
}
