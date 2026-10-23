param([switch]$ForceCheck)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/MaintenancePolicy.ps1"
Set-Location (Split-Path $PSScriptRoot -Parent)
$latest = Invoke-RestMethod 'https://kamori.goats.dev/Dalamud/Release/VersionInfo'
$manifest = Get-Content src/NativeDof.json -Raw | ConvertFrom-Json
$state = Get-Content maintenance.json -Raw | ConvertFrom-Json
$feed = @(Get-Content repo.json -Raw | ConvertFrom-Json)
$result = Get-MaintenanceWork $latest $manifest $state $feed[0] -ForceCheck:$ForceCheck
Write-Output "Stable Dalamud: $($latest.assemblyVersion); last processed: $($state.dalamudVersion); decision: $($result.Decision); run maintenance: $($result.Needed)."
if ($env:GITHUB_OUTPUT) {
    "needed=$($result.Needed.ToString().ToLowerInvariant())" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
    "build_candidate=$($result.BuildCandidate.ToString().ToLowerInvariant())" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
}
