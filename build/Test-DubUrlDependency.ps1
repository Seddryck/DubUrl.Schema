$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DubUrlDependency.psm1') -Force
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('duburl-dependency-' + [Guid]::NewGuid().ToString('N'))
New-Item $fixture -ItemType Directory | Out-Null
$state = @{ Version = '0.60.0'; Missing = $false; Fail = $false; Calls = [Collections.Generic.List[string]]::new() }
$request = {
    param($Uri)
    $state.Calls.Add($Uri)
    if ($state.Fail) { throw 'Simulated NuGet outage.' }
    if ($Uri -eq 'https://api.nuget.org/v3/index.json') {
        return @{ resources = @(
            @{ '@type' = 'SearchQueryService/3.5.0'; '@id' = 'https://example.test/query' },
            @{ '@type' = 'PackageBaseAddress/3.0.0'; '@id' = 'https://example.test/content/' }
        ) }
    }
    if ($Uri -like 'https://example.test/query?*') {
        if ($Uri -notlike '*prerelease=false*') { throw 'Search must exclude prereleases.' }
        return @{ data = @(@{ id = 'DubUrl'; version = $state.Version }) }
    }
    if ($Uri -match '/(duburl|duburl\.provider\.[a-z]+)/index.json$') {
        if ($state.Missing -and $Uri -match 'provider.future') { return @{ versions = @('0.55.1') } }
        return @{ versions = @('0.55.1', '0.60.0', '0.61.0-beta.1') }
    }
    throw "Unexpected URI: $Uri"
}.GetNewClosure()
function Assert-Rejected([scriptblock]$Action, [string]$Pattern) {
    try { & $Action; throw 'Expected rejection did not occur.' }
    catch { if ($_.Exception.Message -notlike $Pattern) { throw } }
}
try {
    Set-Content (Join-Path $fixture 'Core.csproj') '<Project><ItemGroup><PackageReference Include="DubUrl"/><PackageReference Include="Other"/></ItemGroup></Project>'
    Set-Content (Join-Path $fixture 'QA.csproj') '<Project><ItemGroup><PackageReference Include="DubUrl.Provider.Sqlite"/><PackageReference Include="DubUrl.Provider.Future"/></ItemGroup></Project>'
    $resolveParams = @{ RepositoryRoot = $fixture; Request = $request }
    if ((Resolve-DubUrlVersion @resolveParams) -ne '0.60.0') { throw 'Latest stable release was not selected.' }
    if ($state.Calls.Count -ne 5) { throw 'Expected one search and checks for all three unique packages.' }
    $state.Calls.Clear()
    if ((Resolve-DubUrlVersion @resolveParams -Candidate '0.55.1') -ne '0.55.1') { throw 'Stable override was ignored.' }
    if ($state.Calls -like '*query*') { throw 'An explicit override must bypass latest-version search.' }
    if ((Resolve-DubUrlVersion @resolveParams -Candidate '0.61.0-beta.1') -ne '0.61.0-beta.1') { throw 'Prerelease override was ignored.' }
    $state.Calls.Clear()
    foreach ($candidate in @('*', '1.2.3;bad', "1.2.3`ninjected", '1.2.3-', "1.2.3`n", "1.2.3`r")) {
        Assert-Rejected { Resolve-DubUrlVersion @resolveParams -Candidate $candidate } '*Invalid DubUrl package version*'
    }
    if ($state.Calls.Count) { throw 'Invalid overrides must fail before network access.' }
    $state.Version = '0.61.0-beta.1'
    Assert-Rejected { Resolve-DubUrlVersion @resolveParams } '*did not return a stable DubUrl version*'
    $state.Version = ''
    Assert-Rejected { Resolve-DubUrlVersion @resolveParams } '*did not return a stable DubUrl version*'
    $state.Version = '0.60.0'; $state.Missing = $true
    Assert-Rejected { Resolve-DubUrlVersion @resolveParams } '*DubUrl.Provider.Future 0.60.0 is not published*'
    Assert-Rejected { Resolve-DubUrlVersion @resolveParams -Candidate '0.60.0' } '*DubUrl.Provider.Future 0.60.0 is not published*'
    $state.Missing = $false; $state.Fail = $true
    Assert-Rejected { Resolve-DubUrlVersion @resolveParams } '*Simulated NuGet outage*'
    Write-Host 'Passed 9 dependency-selection scenarios (including six invalid override forms).'
}
finally {
    Remove-Item -LiteralPath (Join-Path $fixture 'Core.csproj') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $fixture 'QA.csproj') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $fixture -Force
}
