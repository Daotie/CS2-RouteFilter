param([string]$OutputDirectory = 'dist')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
Push-Location $workspace
try {
    foreach ($configuration in @('Debug','Release')) {
        dotnet build RouteFilter.csproj -c $configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw "$configuration build failed" }
    }
    foreach ($test in @('RoadCoverage','LeaseRules','SafetyRules','SaveFormatTests','RoadSigns','UxRules')) {
        dotnet run --project "Tests/$test" -c Release
        if ($LASTEXITCODE -ne 0) { throw "$test failed" }
    }
    $previousOutput = $env:ROUTEFILTER_OUTPUT_DIR
    $env:ROUTEFILTER_OUTPUT_DIR = Join-Path $workspace 'UI/build'
    Push-Location UI
    try {
        foreach ($script in @('typecheck','test','build')) {
            npm run $script
            if ($LASTEXITCODE -ne 0) { throw "UI $script failed" }
        }
    } finally { Pop-Location; $env:ROUTEFILTER_OUTPUT_DIR = $previousOutput }
    python tools/Verify-PlateMesh.py
    if ($LASTEXITCODE -ne 0) { throw 'RF-Plate resource verification failed' }
    git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace check failed' }
    $version = (Get-Content VERSION -Raw).Trim()
    $build = [regex]::Match((Get-Content Mod.cs -Raw),'BuildId = "([^"]+)"').Groups[1].Value
    $destination = Join-Path $workspace "$OutputDirectory/RouteFilter-$version-$build"
    if (Test-Path -LiteralPath $destination) { throw "Output already exists; use a different directory: $destination" }
    $payload = Join-Path $destination 'RouteFilter'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    Get-ChildItem -LiteralPath 'bin/Release/net48' -File | Where-Object Extension -in @('.dll','.pdb','.config','.so','.bundle') |
        Copy-Item -Destination $payload
    Copy-Item -LiteralPath 'UI/build/RouteFilter.mjs','UI/build/RouteFilter.css','UI/mod.json' -Destination $payload
    Copy-Item -LiteralPath 'UI/build/images' -Destination $payload -Recurse
    Copy-Item -LiteralPath 'UNIFIED_TEST_REPORT.md','RELEASE_NOTES.md' -Destination $destination
    $sourceHead = (git rev-parse HEAD).Trim()
    @("version=$version", "build=$build", "sourceHead=$sourceHead", "branch=$((git branch --show-current).Trim())") | Set-Content -LiteralPath (Join-Path $destination 'BUILD.txt') -Encoding utf8
    Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($destination.Length+1).Replace('\','/')
        "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLower())  $relative"
    } | Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Encoding utf8
    Compress-Archive -Path "$destination/*" -DestinationPath "$destination.zip"
    Write-Output "Unified test package: $destination.zip"
} finally { Pop-Location }
