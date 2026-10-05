param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$gameData = 'C:\Users\Wty_M\AppData\LocalLow\Colossal Order\Cities Skylines II'
$destination = Join-Path $gameData 'Mods\RouteFilter'
$configPath = Join-Path $gameData '.cache\Mods\playset_config.json'
$modDirectoryPath = Join-Path $gameData '.cache\Mods\mod_directory.json'
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
if (Test-Path -LiteralPath $modDirectoryPath) {
    Copy-Item -LiteralPath $modDirectoryPath -Destination (Join-Path $backup 'mod_directory.json')
}
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
# Register the real local mod root before adding its playset entry. The launcher
# matches local sourceId against the basename of a registered dirPath; an entry
# with no discoverable directory aborts playset import and can erase its mods.
$modDirectory = Get-Content -LiteralPath $modDirectoryPath -Raw | ConvertFrom-Json
$localSources = @($modDirectory.modsSources | Where-Object source -eq 'local')
if ($localSources.Count -ne 1) { throw 'Cannot resolve exactly one local mod source.' }
$localRoot = Join-Path $gameData 'Mods'
$hasLocalRoot = @($localSources[0].rootPaths | Where-Object {
    [IO.Path]::GetFullPath($_).TrimEnd('\','/') -ieq [IO.Path]::GetFullPath($localRoot).TrimEnd('\','/')
}).Count -gt 0
if (!$hasLocalRoot) {
    $localSources[0].rootPaths = @($localSources[0].rootPaths) + @($localRoot)
    $directoryJson = ConvertTo-Json -InputObject $modDirectory -Depth 100 -Compress
    [IO.File]::WriteAllText($modDirectoryPath, $directoryJson, [Text.UTF8Encoding]::new($false))
}
# Only update RouteFilter in the active playset. Preserve every other mod,
# including its order and enabled state, and leave other playsets untouched.
function Test-RouteFilterEntry($mod) {
    return (($mod.source -eq 'local' -and $mod.sourceId -in @('.RouteFilter','RouteFilter')) -or
            ($mod.source -eq 'pdx_mods' -and $mod.sourceId -eq '155839'))
}
$otherPlaysetsBefore = ConvertTo-Json -InputObject @($config.playsets | Where-Object id -ne $config.activePlaysetId) -Depth 100 -Compress
$otherMods = @($active[0].mods | Where-Object { !(Test-RouteFilterEntry $_) })
$otherModsBefore = ConvertTo-Json -InputObject $otherMods -Depth 100 -Compress
$active[0].mods = $otherMods +
    @([pscustomobject]@{ source='local'; sourceId='RouteFilter'; isEnabled=$true })
# The PDX SDK compares this timestamp against playset_sync.json. Without a
# new timestamp it can consider the edit already synced and overwrite it.
$active[0].modifiedAt = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$active[0].modifiedWith = 'RouteFilter local deployment'
$serializedConfig = ConvertTo-Json -InputObject $config -Depth 100 -Compress
$verified = $serializedConfig | ConvertFrom-Json
$verifiedActive = @($verified.playsets | Where-Object id -eq $verified.activePlaysetId)
$otherModsAfter = ConvertTo-Json -InputObject @($verifiedActive[0].mods | Where-Object { !(Test-RouteFilterEntry $_) }) -Depth 100 -Compress
$otherPlaysetsAfter = ConvertTo-Json -InputObject @($verified.playsets | Where-Object id -ne $verified.activePlaysetId) -Depth 100 -Compress
if ($verifiedActive.Count -ne 1 -or $otherModsBefore -cne $otherModsAfter -or $otherPlaysetsBefore -cne $otherPlaysetsAfter -or
    @($verifiedActive[0].mods).Count -ne ($otherMods.Count + 1)) {
    throw 'Playset preservation check failed; original configuration has not been overwritten.'
}
$localEntry = @($verifiedActive[0].mods | Where-Object { $_.source -eq 'local' -and $_.sourceId -eq 'RouteFilter' -and $_.isEnabled })
if ($localEntry.Count -ne 1) { throw 'Expected exactly one enabled local RouteFilter entry.' }
# Do not overwrite a configuration changed by the launcher during deployment.
if ((Get-FileHash -LiteralPath $configPath).Hash -ne (Get-FileHash -LiteralPath (Join-Path $backup 'playset_config.json')).Hash) {
    throw 'Playset configuration changed during deployment; retry after closing the game and mod manager.'
}
[System.IO.File]::WriteAllText($configPath, $serializedConfig, [System.Text.UTF8Encoding]::new($false))
if ([System.IO.File]::ReadAllText($configPath) -cne $serializedConfig) { throw 'Playset write verification failed.' }
$localEntry
"Deployed $Configuration to $destination; all file hashes match. Backup: $backup"
