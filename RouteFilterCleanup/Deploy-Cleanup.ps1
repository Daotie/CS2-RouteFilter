param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$game = 'C:\Users\Wty_M\AppData\LocalLow\Colossal Order\Cities Skylines II'
$dest = Join-Path $game 'Mods\RouteFilterCleanup'
$configPath = Join-Path $game '.cache\Mods\playset_config.json'
if (Get-Process Cities2 -ErrorAction SilentlyContinue) { throw 'Close Cities: Skylines II before deploying cleanup.' }
dotnet build (Join-Path $root 'RouteFilterCleanup\RouteFilterCleanup.csproj') -c $Configuration
$sourceDll = Join-Path $root "RouteFilterCleanup\bin\$Configuration\net48\RouteFilterCleanup.dll"
New-Item -ItemType Directory -Path $dest -Force | Out-Null
Copy-Item -LiteralPath $sourceDll -Destination (Join-Path $dest 'RouteFilterCleanup.dll') -Force
Copy-Item -LiteralPath (Join-Path $root 'RouteFilterCleanup\mod.json') -Destination (Join-Path $dest 'mod.json') -Force
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$active = @($config.playsets | Where-Object id -eq $config.activePlaysetId)
if ($active.Count -ne 1) { throw 'Cannot resolve active playset.' }
foreach ($playset in $config.playsets) {
    $playset.mods = @($playset.mods | Where-Object {
        !(($_.source -eq 'local' -and $_.sourceId -in @('RouteFilter', '.RouteFilter', 'RouteFilterCleanup')) -or
          ($_.source -eq 'pdx_mods' -and $_.sourceId -eq '155839'))
    })
}
$active[0].mods = @($active[0].mods) + @([pscustomobject]@{ source='local'; sourceId='RouteFilterCleanup'; isEnabled=$true })
$config | ConvertTo-Json -Depth 100 -Compress | Set-Content -LiteralPath $configPath -Encoding utf8
"Deployed RouteFilterCleanup; RouteFilter is absent from every playset; build RFCLEAN-20260925-NETWORK-REFRESH-01"
