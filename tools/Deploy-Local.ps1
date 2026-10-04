param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$gameData = 'C:\Users\Wty_M\AppData\LocalLow\Colossal Order\Cities Skylines II'
$destination = Join-Path $gameData 'Mods\RouteFilter'
$configPath = Join-Path $gameData '.cache\Mods\playset_config.json'
if (Get-Process Cities2 -ErrorAction SilentlyContinue) { throw 'Close Cities: Skylines II before deployment.' }
$pairs = @(
    @{ Source = Join-Path $workspace "bin\$Configuration\net48\RouteFilter.dll"; Name = 'RouteFilter.dll' },
    @{ Source = Join-Path $workspace "bin\$Configuration\net48\RouteFilter.pdb"; Name = 'RouteFilter.pdb' },
    @{ Source = Join-Path $workspace 'UI\build\RouteFilter.mjs'; Name = 'RouteFilter.mjs' },
    @{ Source = Join-Path $workspace 'UI\build\RouteFilter.css'; Name = 'RouteFilter.css' },
    @{ Source = Join-Path $workspace 'UI\mod.json'; Name = 'mod.json' }
)
foreach ($pair in $pairs) { if (!(Test-Path -LiteralPath $pair.Source)) { throw "Missing $($pair.Source)" } }
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$active = @($config.playsets | Where-Object id -eq $config.activePlaysetId)
if ($active.Count -ne 1) { throw 'Cannot resolve exactly one active Playset.' }
$backupRoot = Join-Path $workspace 'deployment-backups'
$backup = Join-Path $backupRoot ('RouteFilter-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath $configPath -Destination (Join-Path $backup 'playset_config.json')
foreach ($pair in $pairs) {
    $target = Join-Path $destination $pair.Name
    if (Test-Path -LiteralPath $target) {
        $old = Join-Path $backup $pair.Name
        New-Item -ItemType Directory -Path (Split-Path $old -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $target -Destination $old
    }
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $pair.Source -Destination $target
    if ((Get-FileHash -LiteralPath $pair.Source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw "Hash mismatch: $target" }
}
$imageSource = Join-Path $workspace 'UI\build\images'
$imageDestination = Join-Path $destination 'images'
if (!(Test-Path -LiteralPath $imageSource)) { throw "Missing $imageSource" }
$resolvedDestination = [System.IO.Path]::GetFullPath($destination)
$resolvedImages = [System.IO.Path]::GetFullPath($imageDestination)
if (!$resolvedImages.StartsWith($resolvedDestination + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace images outside RouteFilter: $resolvedImages"
}
if (Test-Path -LiteralPath $resolvedImages) { Remove-Item -LiteralPath $resolvedImages -Recurse -Force }
New-Item -ItemType Directory -Path $imageDestination -Force | Out-Null
Copy-Item -Path (Join-Path $imageSource '*') -Destination $imageDestination -Recurse -Force
# Remove the published 1.x package and stale local aliases from every playset so
# the launcher cannot select another RouteFilter build later.
foreach ($playset in $config.playsets) {
    $playset.mods = @($playset.mods | Where-Object {
        !(($_.source -eq 'local' -and $_.sourceId -in @('.RouteFilter','RouteFilter')) -or
          ($_.source -eq 'pdx_mods' -and $_.sourceId -eq '155839'))
    })
}
$active[0].mods = @($active[0].mods) +
    @([pscustomobject]@{ source='local'; sourceId='RouteFilter'; isEnabled=$true })
$config | ConvertTo-Json -Depth 100 -Compress | Set-Content -LiteralPath $configPath -Encoding utf8
$verified = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$verified.playsets | Where-Object id -eq $verified.activePlaysetId | ForEach-Object {
    $_.mods | Where-Object { $_.source -eq 'local' -and $_.sourceId -eq 'RouteFilter' }
}
"Deployed $Configuration to $destination; all file hashes match. Backup: $backup"
