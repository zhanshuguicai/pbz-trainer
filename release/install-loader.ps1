# Installs or removes the UE4SS loader (UE4SS + PR #1484 configs for PBZ) shipped in .\loader of this package.
# Install refuses to overwrite anything and records every file it writes; Rollback removes only those files.
# Run with the game closed:  powershell -ExecutionPolicy Bypass -File install-loader.ps1 -Mode Install
param(
    [Parameter(Mandatory)][ValidateSet('Install', 'Rollback')][string]$Mode,
    [string]$GameDir = ''
)
$ErrorActionPreference = 'Stop'
$stageRoot = Join-Path $PSScriptRoot 'loader'
$manifestPath = Join-Path $PSScriptRoot 'loader-install-manifest.json'
# Game builds this loader was verified on (Steam build id: PBZ.exe SHA-256). Keep in sync with GameCompatibility.Verified.
$verified = @{
    '26266538' = '7B4FCD286A350780FAF0824D73AA150B21CBF1BCCDE883E6A96386CBE09973B4'
    '26367565' = '4415933CE33476F14C837ABB61A1B60AF8B78CD81783896501913A7AAC69E423'
}
if (Get-Process -Name PBZ -ErrorAction SilentlyContinue) { throw '请先正常退出游戏。' }

# Without -GameDir: the default Steam folder, then every Steam library listed in libraryfolders.vdf.
function Find-GameDir {
    $suffix = 'steamapps\common\PBZ\platform\steam'
    $roots = @('C:\Program Files (x86)\Steam')
    $steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $roots += $steam.Replace('/', '\')
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($m in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) { $roots += $m.Groups[1].Value.Replace('\\', '\') }
        }
    }
    foreach ($r in $roots | Select-Object -Unique) {
        $candidate = Join-Path $r $suffix
        if (Test-Path -LiteralPath (Join-Path $candidate 'PBZ.exe')) { return $candidate }
    }
    throw '找不到游戏。请用 -GameDir 指定 ...\steamapps\common\PBZ\platform\steam（见使用教程第 9 节）。'
}
if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'PBZ.exe'))) { throw "这个目录里没有 PBZ.exe：$GameDir" }
$targetRoot = [IO.Path]::GetFullPath($GameDir).TrimEnd('\')
Write-Output "游戏目录：$targetRoot"

if ($Mode -eq 'Install') {
    if (Test-Path -LiteralPath $manifestPath) { throw '这个包已经安装过加载器（有 loader-install-manifest.json）。要重装请先 Rollback。' }
    $exeHash = (Get-FileHash -LiteralPath (Join-Path $targetRoot 'PBZ.exe') -Algorithm SHA256).Hash
    if ($verified.Values -notcontains $exeHash) { throw "PBZ.exe 不是验证过的版本（$exeHash）。游戏更新后要重新验证再安装。" }
    $stageFull = (Resolve-Path -LiteralPath $stageRoot).Path
    $files = @(Get-ChildItem -LiteralPath $stageFull -File -Recurse | ForEach-Object {
        [pscustomobject]@{ relativePath = $_.FullName.Substring($stageFull.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; bytes = $_.Length }
    })
    # Check every target before copying anything, so a conflict leaves the game directory untouched.
    foreach ($entry in $files) { if (Test-Path -LiteralPath (Join-Path $targetRoot $entry.relativePath)) { throw "目标文件已存在，请人工检查：$($entry.relativePath)" } }
    $manifest = [ordered]@{ destination = $targetRoot; exeSha256 = $exeHash; status = 'PLANNED'; files = $files }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    foreach ($entry in $files) {
        $target = Join-Path $targetRoot $entry.relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $stageFull $entry.relativePath) -Destination $target
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.sha256) { throw "复制后校验失败：$($entry.relativePath)" }
    }
    $manifest.status = 'INSTALLED'
    $manifest.installedAt = (Get-Date).ToString('o')
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Output "已安装 $($files.Count) 个加载器文件。"
    return
}

# Rollback: remove only files this package installed and that are unchanged; stop on anything unexpected.
if (-not (Test-Path -LiteralPath $manifestPath)) { throw '没有安装清单，不能自动卸载。' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([IO.Path]::GetFullPath($manifest.destination).TrimEnd('\') -ne $targetRoot) { throw '安装位置和清单不一致。' }
foreach ($entry in $manifest.files) {
    $target = Join-Path $targetRoot $entry.relativePath
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "文件已被修改，停止卸载：$($entry.relativePath)"
    }
}
if (Test-Path -LiteralPath (Join-Path $targetRoot 'ue4ss\Mods\PBZSuite')) { throw '请先在修改器里卸载 PBZSuite。' }
foreach ($entry in $manifest.files) { Remove-Item -LiteralPath (Join-Path $targetRoot $entry.relativePath) -ErrorAction SilentlyContinue }
# Remove directories the loader created, deepest first, only if empty (UE4SS logs and generated files stay for inspection).
Get-ChildItem -LiteralPath (Join-Path $targetRoot 'ue4ss') -Directory -Recurse -ErrorAction SilentlyContinue |
    Sort-Object { $_.FullName.Length } -Descending |
    Where-Object { -not (Get-ChildItem -LiteralPath $_.FullName -Force) } | Remove-Item
Remove-Item -LiteralPath $manifestPath
Write-Output '加载器已移除（UE4SS 自己生成的日志等文件保留在 ue4ss 目录里，可手动删除）。'
