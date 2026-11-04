# P2 read-only probe mod install/rollback. Run Rollback before p1-loader.ps1 -Mode Rollback.
# watch.lua is edited during sampling, so it is recorded as mutable and removed without a hash check.
param([Parameter(Mandatory)][ValidateSet('Install', 'Rollback', 'SyncWatch')][string]$Mode)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$stageRoot = Join-Path $PSScriptRoot 'p2-stage'
$manifestPath = Join-Path $projectRoot 'evidence\p2-probe-manifest.json'
$targetRoot = 'C:\Program Files (x86)\Steam\steamapps\common\PBZ\platform\steam'
$modRoot = Join-Path $targetRoot 'ue4ss\Mods\PBZProbe'
$mutable = @('ue4ss\Mods\PBZProbe\watch.lua', 'ue4ss\Mods\PBZProbe\adhoc.lua')

if ($Mode -eq 'SyncWatch') {
    # Safe while the game runs: only replaces the watch list and adhoc script, which the mod rereads on F11/F8.
    foreach ($rel in $mutable) { Copy-Item -LiteralPath (Join-Path $stageRoot $rel) -Destination (Join-Path $targetRoot $rel) -Force }
    Write-Output 'watch.lua and adhoc.lua synced.'
    return
}
if (Get-Process -Name PBZ -ErrorAction SilentlyContinue) { throw 'Exit PBZ normally first.' }

if ($Mode -eq 'Install') {
    if (Test-Path -LiteralPath $manifestPath) { throw 'Manifest already exists; this round was installed before.' }
    if (-not (Test-Path -LiteralPath (Join-Path $targetRoot 'ue4ss\UE4SS.dll'))) { throw 'P1 loader is not installed.' }
    if (Test-Path -LiteralPath $modRoot) { throw 'PBZProbe already exists in game directory.' }
    $stageFull = (Resolve-Path -LiteralPath $stageRoot).Path
    $files = @(Get-ChildItem -LiteralPath $stageFull -File -Recurse | ForEach-Object {
        $rel = $_.FullName.Substring($stageFull.Length + 1)
        [pscustomobject]@{ relativePath = $rel; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; bytes = $_.Length; mutable = ($rel -in $mutable) }
    })
    foreach ($entry in $files) { if (Test-Path -LiteralPath (Join-Path $targetRoot $entry.relativePath)) { throw "Target exists: $($entry.relativePath)" } }
    $manifest = [ordered]@{ round = 'p2-probe'; destination = $targetRoot; status = 'PLANNED'; files = $files }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    foreach ($entry in $files) {
        $target = Join-Path $targetRoot $entry.relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $stageFull $entry.relativePath) -Destination $target
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Copy hash mismatch: $($entry.relativePath)" }
    }
    $manifest.status = 'INSTALLED'
    $manifest.installedAt = (Get-Date).ToString('o')
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Output "Installed $($files.Count) probe files."
    return
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$validated = @()
foreach ($entry in $manifest.files) {
    $target = [IO.Path]::GetFullPath((Join-Path $targetRoot $entry.relativePath))
    if (-not $target.StartsWith($modRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path outside probe mod directory.' }
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Missing recorded file: $($entry.relativePath)" }
    if (-not $entry.mutable -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Modified file; aborting: $($entry.relativePath)" }
    $validated += $target
}
$unknown = @(Get-ChildItem -LiteralPath $modRoot -File -Recurse | Where-Object { $_.FullName -notin $validated })
if ($unknown.Count) { throw "Unknown file in probe mod; inspect manually: $($unknown[0].Name)" }
foreach ($target in $validated) { Remove-Item -LiteralPath $target }
$directories = @(Get-ChildItem -LiteralPath $modRoot -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending)
foreach ($directory in $directories) { [IO.Directory]::Delete($directory.FullName, $false) }
[IO.Directory]::Delete($modRoot, $false)
@{ timestamp = (Get-Date).ToString('o'); removedRecordedFiles = $validated.Count } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'evidence\p2-probe-rollback-result.json') -Encoding utf8
# Keep the removed round's manifest as evidence so a new Install can write a fresh one.
Move-Item -LiteralPath $manifestPath -Destination (Join-Path $projectRoot ('evidence\p2-probe-manifest-removed-' + (Get-Date -Format yyyyMMdd-HHmmss) + '.json'))
Write-Output 'Probe rollback complete.'
