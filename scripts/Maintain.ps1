param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/MaintenancePolicy.ps1"
Set-Location (Split-Path $PSScriptRoot -Parent)

function Summary([string]$Text) {
    Write-Output $Text
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $Text }
}
function GitHub([string]$Path, [string]$Method='Get', $Body=$null) {
    $parameters = @{ Uri="https://api.github.com/repos/$env:GITHUB_REPOSITORY/$Path"; Method=$Method; Headers=@{
        Authorization="Bearer $env:GH_TOKEN"; Accept='application/vnd.github+json'; 'X-GitHub-Api-Version'='2022-11-28' } }
    if ($null -ne $Body) { $parameters.ContentType='application/json'; $parameters.Body=ConvertTo-Json $Body -Depth 10 }
    Invoke-RestMethod @parameters
}
function Blocked([string]$Reason) {
    Summary $Reason
    if ($ValidateOnly) { throw $Reason }
    $title = 'Native DoF compatibility maintenance needs attention'
    $body = "Automated maintenance cannot publish an update.`n`n$Reason`n`nThe existing release and plugin feed are retained. Review native compatibility or resolve the build failure, then run the workflow again."
    $issues = @(GitHub 'issues?state=open&per_page=100')
    $issue = $issues | Where-Object { $_.title -eq $title } | Select-Object -First 1
    if ($issue) {
        if ($issue.body -ne $body) { GitHub "issues/$($issue.number)" Patch @{body=$body} | Out-Null }
    } else { GitHub 'issues' Post @{title=$title;body=$body} | Out-Null }
}
function CheckedGit([string[]]$Arguments) {
    & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git operation failed: $($Arguments[0])" }
}
function SaveJson($Value, [string]$Path) { ConvertTo-Json -InputObject $Value -Depth 10 | Set-Content -LiteralPath $Path -Encoding utf8 }
function AssertPackage([string]$Path, [string]$Version) {
    $archive=[IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path))
    try {
        foreach($name in @('NativeDof.dll','NativeDof.json','NativeDof.deps.json')) {
            $entry=$archive.GetEntry($name)
            if (!$entry) { throw "Missing packaged file: $name" }
            $stream=$entry.Open()
            try { $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            if ($hash -ne (Get-FileHash "release/NativeDof/$name").Hash) { throw "Published file differs: $name" }
        }
    } finally { $archive.Dispose() }
    $manifest=Get-Content release/NativeDof/NativeDof.json -Raw | ConvertFrom-Json
    if ($manifest.AssemblyVersion -ne "$Version.0") { throw 'Package version mismatch' }
}

try {
    if (!$ValidateOnly -and ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REF -ne 'refs/heads/main')) {
        throw 'Publishing is only allowed in the main-branch GitHub Actions workflow. Use -ValidateOnly locally.'
    }
    $latest=Invoke-RestMethod 'https://kamori.goats.dev/Dalamud/Release/VersionInfo'
    $manifest=Get-Content src/NativeDof.json -Raw | ConvertFrom-Json
    $state=Get-Content maintenance.json -Raw | ConvertFrom-Json
    $feed=@(Get-Content repo.json -Raw | ConvertFrom-Json)
    $decision=Get-MaintenanceDecision $latest $manifest $state
    if ($decision -in @('game-patch','api-change','rollback')) {
        Blocked "Reason: $decision. Stable Dalamud $($latest.assemblyVersion) targets game $($latest.supportedGameVer) (current-game applicability: $($latest.isApplicableForCurrentGameVer)); this plugin targets $($manifest.ApplicableVersion), API $($manifest.DalamudApiLevel). Game/API changes require independently verified native addresses and layouts. GitHub-hosted runners do not have a licensed game installation."
        exit 0
    }
    $pending=[version]$manifest.AssemblyVersion -gt [version]$feed[0].AssemblyVersion
    if ($decision -eq 'unchanged' -and !$pending -and !$ValidateOnly) { Summary 'No compatibility changes; no release needed.'; exit 0 }
    if ($pending -and $decision -ne 'unchanged') { throw 'An unfinished release exists alongside a newer Dalamud revision. Reconcile it before publishing.' }
    $download=[uri]$latest.downloadUrl
    if ($download.Scheme -ne 'https' -or $download.Host -ne 'kamori.goats.dev' -or !$download.AbsolutePath.StartsWith('/File/Get/')) {
        throw 'Unexpected Dalamud download URL'
    }
    New-Item -ItemType Directory -Path '.build/maintenance' -Force | Out-Null
    Invoke-WebRequest $download.AbsoluteUri -OutFile '.build/maintenance/dalamud.zip'
    Expand-Archive '.build/maintenance/dalamud.zip' '.build/maintenance/dalamud' -Force
    $dlls=@(Get-ChildItem '.build/maintenance/dalamud' -Filter Dalamud.dll -Recurse)
    if ($dlls.Count -ne 1) { throw 'Cannot identify downloaded Dalamud reference directory' }
    $references=$dlls[0].DirectoryName
    $actual=[Reflection.AssemblyName]::GetAssemblyName($dlls[0].FullName).Version.ToString()
    if ($actual -ne $latest.assemblyVersion) { throw 'Downloaded Dalamud version differs from metadata' }
    & dotnet restore scripts/VerifyLayouts/VerifyLayouts.csproj --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Layout verifier restore failed' }
    & dotnet run --project scripts/VerifyLayouts/VerifyLayouts.csproj -c Release --no-restore -- "$references/FFXIVClientStructs.dll"
    if ($LASTEXITCODE -ne 0) { throw 'Native reference layout verification failed' }

    $version=([version]$manifest.AssemblyVersion).ToString(3)
    if (!$ValidateOnly -and !$pending) {
        $allReleases=@()
        for ($page=1; ; $page++) {
            $batch=@(GitHub "releases?per_page=100&page=$page")
            $allReleases += $batch
            if ($batch.Count -lt 100) { break }
        }
        $highest=[version]$manifest.AssemblyVersion
        foreach($release in $allReleases) {
            $parsed=$null
            if ([version]::TryParse(($release.tag_name -replace '^v',''), [ref]$parsed) -and $parsed -gt $highest) { $highest=$parsed }
        }
        if ([version]$feed[0].AssemblyVersion -gt $highest) { $highest=[version]$feed[0].AssemblyVersion }
        $version="$($highest.Major).$($highest.Minor).$($highest.Build + 1)"
        $oldVersion=([version]$manifest.AssemblyVersion).ToString(3)
        $projectPath=Join-Path $PWD 'src/NativeDof.csproj'
        $project=[IO.File]::ReadAllText($projectPath) -replace '<Version>[^<]+</Version>',"<Version>$version</Version>"
        [IO.File]::WriteAllText($projectPath,$project)
        foreach($path in @('src/Plugin.cs','src/NativeBackend.cs')) {
            $full=Join-Path $PWD $path
            [IO.File]::WriteAllText($full,[IO.File]::ReadAllText($full).Replace("Native DoF Prototype $oldVersion","Native DoF Prototype $version").Replace("Native DoF $oldVersion`:","Native DoF $version`:"))
        }
        $manifest.AssemblyVersion="$version.0"
        SaveJson $manifest 'src/NativeDof.json'
        $state.dalamudVersion=$latest.assemblyVersion
        SaveJson $state 'maintenance.json'
        $readme=[IO.File]::ReadAllText((Join-Path $PWD 'README.md'))
        $readme=$readme -replace 'Version \*\*\d+\.\d+\.\d+\*\*,',"Version **$version**,"
        $readme=$readme -replace 'Built against Dalamud [\d.]+ and',"Built against Dalamud $($latest.assemblyVersion) and"
        $readme=$readme -replace '`v[\d.]+` release''s `NativeDof-[\d.]+\.zip`', ('`v'+$version+'` release''s `NativeDof-'+$version+'.zip`')
        [IO.File]::WriteAllText((Join-Path $PWD 'README.md'),$readme)
    }
    & ./build.ps1 -DalamudLibPath $references
    if ($LASTEXITCODE -ne 0) { throw 'Build or regression tests failed' }
    $package="release/NativeDof-$version.zip"
    AssertPackage $package $version
    if ($ValidateOnly) { Summary "Validation passed against stable Dalamud $actual. No release, repository or issue changes made."; exit 0 }

    # Stop on concurrent source changes; never rebase and publish a package built from different code.
    CheckedGit @('fetch','origin','main')
    if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { throw 'Remote main changed during this run; rerun maintenance.' }
    CheckedGit @('config','user.name','github-actions[bot]')
    CheckedGit @('config','user.email','41898282+github-actions[bot]@users.noreply.github.com')
    if (!$pending) {
        CheckedGit @('add','--','src/NativeDof.csproj','src/NativeDof.json','src/Plugin.cs','src/NativeBackend.cs','maintenance.json','README.md')
        CheckedGit @('commit','-m',"Build Native DoF $version against Dalamud $actual")
        CheckedGit @('push','origin','HEAD:main')
    }
    $tag="v$version"
    $sha=(git rev-parse HEAD).Trim()
    $releases=@(GitHub 'releases?per_page=100')
    $release=$releases | Where-Object tag_name -eq $tag | Select-Object -First 1
    if (!$release) {
        $notes="Rebuilt against stable Dalamud $actual for verified game build $($manifest.ApplicableVersion).`n`nNative reference layouts, release build and regression tests passed. Native hooks and the exact executable guard are unchanged. This automated release has not been visually tested in-game. Update through /xlplugins using the existing custom repository URL."
        $release=GitHub 'releases' Post @{tag_name=$tag;target_commitish=$sha;name="Native DoF $version";body=$notes;draft=$true;prerelease=$true}
    }
    $asset=$release.assets | Where-Object name -eq "NativeDof-$version.zip" | Select-Object -First 1
    if (!$asset) {
        if (!$release.draft) { throw 'Published release has no matching asset; refusing to modify it' }
        $upload=($release.upload_url -replace '\{.*\}$','')+"?name=NativeDof-$version.zip"
        $asset=Invoke-RestMethod -Method Post -Uri $upload -Headers @{Authorization="Bearer $env:GH_TOKEN"} -ContentType 'application/zip' -InFile $package
        if ($asset.state -ne 'uploaded') { throw 'Release asset upload incomplete' }
    }
    if ($release.draft) { GitHub "releases/$($release.id)" Patch @{draft=$false} | Out-Null }
    $url="https://github.com/$env:GITHUB_REPOSITORY/releases/download/$tag/NativeDof-$version.zip"
    Invoke-WebRequest $url -OutFile '.build/maintenance/published.zip'
    AssertPackage '.build/maintenance/published.zip' $version
    foreach($prop in $manifest.PSObject.Properties) { $feed[0].($prop.Name)=$prop.Value }
    foreach($key in @('DownloadLinkInstall','DownloadLinkUpdate','DownloadLinkTesting')) { $feed[0].$key=$url }
    $feed[0].LastUpdate=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    SaveJson $feed 'repo.json'
    CheckedGit @('add','--','repo.json')
    CheckedGit @('commit','-m',"Publish Native DoF $version to plugin feed")
    CheckedGit @('push','origin','HEAD:main')
    $publishedFeed=Invoke-RestMethod "https://raw.githubusercontent.com/$env:GITHUB_REPOSITORY/$((git rev-parse HEAD).Trim())/repo.json"
    if ($publishedFeed[0].AssemblyVersion -ne $manifest.AssemblyVersion) { throw 'Published feed verification failed' }
    Summary "Published [Native DoF $version](https://github.com/$env:GITHUB_REPOSITORY/releases/tag/$tag) and updated the plugin feed."
} catch {
    if (!$ValidateOnly -and $env:GITHUB_ACTIONS -eq 'true') {
        try { Blocked "Maintenance failed: $($_.Exception.Message) See the workflow logs for details. If source or a release was already pushed, rerun to complete the feed update." } catch { Write-Warning 'Could not open the maintenance issue.' }
    }
    throw
}
