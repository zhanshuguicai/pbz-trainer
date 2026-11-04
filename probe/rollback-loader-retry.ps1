# Removes only the loader files recorded by the experiment after PBZ has exited.
# Modified or unknown files cause an abort rather than being deleted.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $projectRoot 'evidence\loader-retry-actual.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$expectedRoot = 'C:\Program Files (x86)\Steam\steamapps\common\PBZ\platform\steam'
$targetRoot = [IO.Path]::GetFullPath($manifest.destination).TrimEnd('\')
if ($targetRoot -ne $expectedRoot) { throw 'Unexpected install root.' }
if (Get-Process -Name PBZ -ErrorAction SilentlyContinue) { throw 'Exit PBZ normally before rollback.' }
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
$runtimeArchive = Join-Path $projectRoot ('evidence\rollback-runtime-' + (Get-Date -Format yyyyMMdd-HHmmss))
if ($unknown.Count) {
    New-Item -ItemType Directory -Path $runtimeArchive | Out-Null
    foreach ($file in $unknown) {
        if ($file.Name -notin @('UE4SS.log', 'UE4SS_Cache.bin', 'UE4SS_Signatures.json') -and $file.Extension -notin @('.log','.cache')) {
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
@{ timestamp=(Get-Date).ToString('o'); removedRecordedFiles=$validatedFiles.Count; archivedRuntimeFiles=$unknown.Count } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'evidence\loader-retry-rollback-result.json') -Encoding utf8
Write-Output 'Rollback complete. Game EXE and saves were not restored or modified.'
