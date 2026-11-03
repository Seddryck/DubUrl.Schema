function Resolve-DubUrlVersion {
    [CmdletBinding()]
    param(
        [string]$Candidate,
        [string]$RepositoryRoot = (Split-Path $PSScriptRoot),
        [scriptblock]$Request = {
            param($Uri)
            Invoke-RestMethod -Uri $Uri -Headers @{ 'Cache-Control' = 'no-cache' } -TimeoutSec 60 -MaximumRetryCount 2
        }
    )
    $ErrorActionPreference = 'Stop'
    $versionPattern = '^\d+\.\d+\.\d+(-[A-Za-z0-9]+([.-][A-Za-z0-9]+)*)?\z'
    if ($Candidate -and $Candidate -notmatch $versionPattern) { throw 'Invalid DubUrl package version.' }

    $index = & $Request 'https://api.nuget.org/v3/index.json'
    $content = ($index.resources | Where-Object { $_.'@type' -eq 'PackageBaseAddress/3.0.0' } | Select-Object -First 1).'@id'
    if (-not $content) { throw 'NuGet package content service is unavailable.' }
    $version = $Candidate
    if (-not $version) {
        $search = ($index.resources | Where-Object { $_.'@type' -eq 'SearchQueryService/3.5.0' } | Select-Object -First 1).'@id'
        if (-not $search) { throw 'NuGet search service is unavailable.' }
        $response = & $Request "${search}?q=packageid%3ADubUrl&prerelease=false&semVerLevel=2.0.0&take=1"
        $version = ($response.data | Where-Object { $_.id -eq 'DubUrl' } | Select-Object -First 1).version
        if ($version -notmatch '^\d+\.\d+\.\d+\z') { throw 'NuGet did not return a stable DubUrl version.' }
    }

    # Discover the complete core/provider set, including QA and package consumers.
    $packages = @(Get-ChildItem $RepositoryRoot -Filter '*.csproj' -Recurse |
        Where-Object { [IO.Path]::GetRelativePath($RepositoryRoot, $_.FullName) -notmatch '(^|[\\/])(bin|obj|\.git|\.worktrees)[\\/]' } |
        ForEach-Object {
            [xml]$project = Get-Content $_.FullName -Raw
            $project.SelectNodes('//PackageReference') | ForEach-Object { $_.GetAttribute('Include') }
        } | Where-Object { $_ -eq 'DubUrl' -or $_ -like 'DubUrl.Provider.*' } | Sort-Object -Unique)
    if ('DubUrl' -notin $packages) { throw 'No DubUrl core package reference was found.' }
    foreach ($package in $packages) {
        $id = $package.ToLowerInvariant()
        $available = & $Request "$($content.TrimEnd('/'))/$id/index.json"
        if ($version -notin $available.versions) { throw "$package $version is not published on NuGet; refusing a mixed dependency set." }
    }
    Write-Host "Selected DubUrl $version for $($packages.Count) core/provider packages."
    return $version
}

Export-ModuleMember -Function Resolve-DubUrlVersion
