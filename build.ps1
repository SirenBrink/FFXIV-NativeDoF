param(
    [Parameter(Mandatory=$true)][string]$DalamudLibPath,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$DalamudLibPath = (Resolve-Path -LiteralPath $DalamudLibPath).Path
foreach ($library in @('Dalamud.dll','Dalamud.Bindings.ImGui.dll','FFXIVClientStructs.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $DalamudLibPath $library))) { throw "Missing reference: $library" }
}
$env:DOTNET_CLI_HOME = "$PSScriptRoot\.build\dotnet-home"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $Dotnet build "$PSScriptRoot\src\NativeDof.csproj" -c Release "-p:DalamudLibPath=$DalamudLibPath" --configfile "$PSScriptRoot\NuGet.Config"
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed' }
& $Dotnet restore "$PSScriptRoot\tests\NativeDof.Tests.csproj" --configfile "$PSScriptRoot\NuGet.Config"
if ($LASTEXITCODE -ne 0) { throw 'Test restore failed' }
& $Dotnet run --project "$PSScriptRoot\tests\NativeDof.Tests.csproj" -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
$release = "$PSScriptRoot\release\NativeDof"
New-Item -ItemType Directory -Path $release -Force | Out-Null
foreach ($name in @('NativeDof.dll','NativeDof.json','NativeDof.deps.json')) {
    Copy-Item -LiteralPath "$PSScriptRoot\src\bin\Release\net10.0-windows\$name" -Destination $release -Force
}
Copy-Item -LiteralPath "$PSScriptRoot\README.md" -Destination $release -Force
Compress-Archive -Path "$release\*" -DestinationPath "$PSScriptRoot\release\NativeDof-0.1.3.zip" -Force
Write-Output "Built and tested: $release\NativeDof.dll"
