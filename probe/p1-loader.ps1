# P1 loader install/rollback for UE4SS + PR #1484 configs.
# Install refuses to overwrite anything; Rollback removes only files recorded in this round's manifest.
param([Parameter(Mandatory)][ValidateSet('Install', 'Rollback')][string]$Mode)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$stageRoot = Join-Path $PSScriptRoot 'p1-stage'
$manifestPath = Join-Path $projectRoot 'evidence\p1-install-manifest.json'
$targetRoot = 'C:\Program Files (x86)\Steam\steamapps\common\PBZ\platform\steam'
$expectedExeSha256 = '7B4FCD286A350780FAF0824D73AA150B21CBF1BCCDE883E6A96386CBE09973B4'
if (Get-Process -Name PBZ -ErrorAction SilentlyContinue) { throw 'Exit PBZ normally first.' }

if ($Mode -eq 'Install') {
    if (Test-Path -LiteralPath $manifestPath) { throw 'Manifest already exists; this round was installed before.' }
    if ((Get-FileHash -LiteralPath (Join-Path $targetRoot 'PBZ.exe') -Algorithm SHA256).Hash -ne $expectedExeSha256) { throw 'PBZ.exe differs from the T0 baseline.' }
    $stageFull = (Resolve-Path -LiteralPath $stageRoot).Path
    $files = @(Get-ChildItem -LiteralPath $stageFull -File -Recurse | ForEach-Object {
        [pscustomobject]@{ relativePath = $_.FullName.Substring($stageFull.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; bytes = $_.Length }
    })
    # Check every target before copying anything, so a conflict leaves the game directory untouched.
    foreach ($entry in $files) { if (Test-Path -LiteralPath (Join-Path $targetRoot $entry.relativePath)) { throw "Target exists; inspect ownership: $($entry.relativePath)" } }
    if (Test-Path -LiteralPath (Join-Path $targetRoot 'ue4ss')) { throw 'ue4ss directory already exists.' }
    $manifest = [ordered]@{ round = 'p1'; destination = $targetRoot; exeSha256 = $expectedExeSha256; existingFilesOverwritten = @(); status = 'PLANNED'; files = $files }
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
    Write-Output "Installed $($files.Count) files."
    return
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([IO.Path]::GetFullPath($manifest.destination).TrimEnd('\') -ne $targetRoot) { throw 'Unexpected install root.' }
$validatedFiles = @()
foreach ($entry in $manifest.files) {
    $target = [IO.Path]::GetFullPath((Join-Path $targetRoot $entry.relativePath))
    if (-not $target.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path outside game executable directory.' }
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Missing recorded file: $($entry.relativePath)" }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Modified file; aborting: $($entry.relativePath)" }
    $validatedFiles += $target
}
$ue4ssRoot = Join-Path $targetRoot 'ue4ss'
$unknown = @(Get-ChildItem -LiteralPath $ue4ssRoot -File -Recurse | Where-Object { $_.FullName -notin $validatedFiles })
# Runtime output is preserved in the project before removing the loader tree.
$runtimeArchive = Join-Path $projectRoot ('evidence\p1-rollback-runtime-' + (Get-Date -Format yyyyMMdd-HHmmss))
if ($unknown.Count) {
    New-Item -ItemType Directory -Path $runtimeArchive | Out-Null
    foreach ($file in $unknown) {
        if ($file.Name -notin @('UE4SS.log', 'UE4SS_Cache.bin', 'UE4SS_Signatures.json') -and $file.Extension -notin @('.log', '.cache', '.dmp')) {
            throw "Unknown file found; inspect manually before rollback: $($file.Name)"
        }
        $relative = $file.FullName.Substring($ue4ssRoot.Length + 1)
        $archiveTarget = Join-Path $runtimeArchive $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $archiveTarget) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $archiveTarget
    }
}
foreach ($target in $validatedFiles) { Remove-Item -LiteralPath $target }
foreach ($file in $unknown) { Remove-Item -LiteralPath $file.FullName }
# Non-recursive deletion only: an unexpected surviving file prevents removal.
$directories = @(Get-ChildItem -LiteralPath $ue4ssRoot -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending)
foreach ($directory in $directories) { [IO.Directory]::Delete($directory.FullName, $false) }
[IO.Directory]::Delete($ue4ssRoot, $false)
@{ timestamp = (Get-Date).ToString('o'); removedRecordedFiles = $validatedFiles.Count; archivedRuntimeFiles = $unknown.Count } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'evidence\p1-rollback-result.json') -Encoding utf8
Write-Output 'Rollback complete. Game EXE and saves were not restored or modified.'
