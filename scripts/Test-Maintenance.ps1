$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/MaintenancePolicy.ps1"
$manifest = [pscustomobject]@{ ApplicableVersion='2026.10.16.0000.0000'; DalamudApiLevel=15 }
$state = [pscustomobject]@{ gameVersion=$manifest.ApplicableVersion; dalamudVersion='15.0.3.9' }
$latest = [pscustomobject]@{ supportedGameVer=$manifest.ApplicableVersion; assemblyVersion='15.0.3.9'; runtimeVersion='10.0.0'; isApplicableForCurrentGameVer=$true }
function Check($Expected) {
    if ((Get-MaintenanceDecision $latest $manifest $state) -ne $Expected) { throw "Expected $Expected" }
}
Check 'unchanged'
$latest.assemblyVersion='15.0.3.10'; Check 'rebuild'
$latest.isApplicableForCurrentGameVer=$false; Check 'game-patch'
$latest.isApplicableForCurrentGameVer=$true
$latest.supportedGameVer='2026.11.01.0000.0000'; Check 'game-patch'
$latest.supportedGameVer=$manifest.ApplicableVersion
$latest.assemblyVersion='16.0.0.0'; Check 'api-change'
$latest.assemblyVersion='15.0.3.8'; Check 'rollback'
$latest.assemblyVersion='15.0.3.10'; $latest.runtimeVersion='11.0.0'; Check 'api-change'
$latest.runtimeVersion=$null
$rejected=$false
try { Get-MaintenanceDecision $latest $manifest $state | Out-Null } catch { $rejected=$true }
if (!$rejected) { throw 'Incomplete metadata was accepted' }
Write-Output 'PASS: 8 maintenance decision checks'
