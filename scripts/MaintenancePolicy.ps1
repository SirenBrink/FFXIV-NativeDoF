function Get-MaintenanceDecision($Latest, $Manifest, $State) {
    if (!$Latest.assemblyVersion -or !$Latest.supportedGameVer -or !$Latest.runtimeVersion) {
        throw 'Stable Dalamud metadata is incomplete.'
    }
    if ($Latest.isApplicableForCurrentGameVer -ne $true) { return 'game-patch' }
    if ($Latest.supportedGameVer -ne $Manifest.ApplicableVersion -or $State.gameVersion -ne $Manifest.ApplicableVersion) { return 'game-patch' }
    if (([version]$Latest.assemblyVersion).Major -ne $Manifest.DalamudApiLevel -or ([version]$Latest.runtimeVersion).Major -ne 10) { return 'api-change' }
    if ([version]$Latest.assemblyVersion -lt [version]$State.dalamudVersion) { return 'rollback' }
    if ($Latest.assemblyVersion -eq $State.dalamudVersion) { return 'unchanged' }
    return 'rebuild'
}

function Get-MaintenanceWork($Latest, $Manifest, $State, $Feed, [switch]$ForceCheck) {
    $decision = Get-MaintenanceDecision $Latest $Manifest $State
    $pending = [version]$Manifest.AssemblyVersion -gt [version]$Feed.AssemblyVersion
    [pscustomobject]@{
        Decision = $decision
        Needed = [bool]($ForceCheck -or $pending -or $decision -ne 'unchanged')
        BuildCandidate = [bool]($decision -in @('unchanged', 'rebuild'))
    }
}
