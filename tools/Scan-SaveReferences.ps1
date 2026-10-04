param([Parameter(Mandatory = $true)][string]$ModCache)

$ErrorActionPreference = 'Stop'
$cecil = 'D:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed\Colossal.Mono.Cecil.dll'
Add-Type -Path $cecil
$pattern = 'Game\.Serialization\.(SaveGameSystem|SerializerSystem|WriteSystem)|Game\.AutoSaveSystem|Colossal\.Serialization\.Entities\.(BinaryWriter|EntitySerializer|ComponentDataSerializer|SharedComponentDataSerializer)|Game\.Simulation\.(ServiceRequest|UpdateFrame)'

foreach ($file in (Get-ChildItem -LiteralPath $ModCache -Recurse -File -Filter '*.dll' -ErrorAction SilentlyContinue | Where-Object { $_.Name -notmatch '_win_x86_64\.dll$' })) {
    try {
        $asm = [Colossal.Mono.Cecil.AssemblyDefinition]::ReadAssembly($file.FullName)
        $hits = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($type in $asm.MainModule.GetTypes()) {
            foreach ($attribute in $type.CustomAttributes) {
                $attributeText = "$($attribute.AttributeType.FullName) $($attribute.ConstructorArguments | ForEach-Object { $_.Value } | Out-String)"
                if ($attributeText -match $pattern) {
                    [void]$hits.Add("$($type.FullName) [attribute] -> $attributeText")
                }
            }
            foreach ($method in $type.Methods) {
                foreach ($attribute in $method.CustomAttributes) {
                    $attributeText = "$($attribute.AttributeType.FullName) $($attribute.ConstructorArguments | ForEach-Object { $_.Value } | Out-String)"
                    if ($attributeText -match $pattern) {
                        [void]$hits.Add("$($type.FullName)::$($method.Name) [attribute] -> $attributeText")
                    }
                }
                if (-not $method.HasBody) { continue }
                foreach ($instruction in $method.Body.Instructions) {
                    if ($null -eq $instruction.Operand) { continue }
                    $target = $instruction.Operand.ToString()
                    if ($target -match $pattern) {
                        [void]$hits.Add("$($type.FullName)::$($method.Name) -> $target")
                    }
                }
            }
        }
        if ($hits.Count -gt 0) {
            [pscustomobject]@{
                Assembly = $asm.Name.Name
                Path = $file.FullName
                Matches = ($hits | Sort-Object) -join "`n"
            }
        }
        $asm.Dispose()
    } catch {
        Write-Warning "$($file.FullName): $($_.Exception.Message)"
    }
}
