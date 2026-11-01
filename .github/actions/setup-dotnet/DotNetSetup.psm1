Set-StrictMode -Version Latest

function Select-DotNetInstallation {
    param([string[]]$SdkLines = @(), [string[]]$RuntimeLines = @(), [string]$Channel = '10.0', [string]$SdkVersion = '')
    $sdkEntry = @($SdkLines | ForEach-Object {
        if ($_ -match '^(?<version>\d+\.\d+\.\d+)\s+\[(?<path>.+)\]\s*$') {
            $candidate = [Version]$Matches.version
            if ($candidate.ToString(2) -eq $Channel -and (!$SdkVersion -or $candidate.ToString() -eq $SdkVersion)) {
                [pscustomobject]@{ Version = $candidate; Path = $Matches.path }
            }
        }
    } | Sort-Object Version -Descending | Select-Object -First 1)
    $runtimeEntry = @($RuntimeLines | ForEach-Object {
        if ($_ -match '^Microsoft\.NETCore\.App\s+(?<version>\d+\.\d+\.\d+)\s+\[.+\]\s*$') {
            $candidate = [Version]$Matches.version
            if ($candidate.ToString(2) -eq $Channel) { $candidate }
        }
    } | Sort-Object -Descending | Select-Object -First 1)
    [pscustomobject]@{
        SdkVersion = if ($sdkEntry.Count) { $sdkEntry[0].Version.ToString() } else { $null }
        RuntimeVersion = if ($runtimeEntry.Count) { $runtimeEntry[0].ToString() } else { $null }
        Root = if ($sdkEntry.Count) { Split-Path $sdkEntry[0].Path -Parent } else { $null }
    }
}

function Resolve-DotNetInstallation {
    param(
        [string]$Channel = '10.0', [string]$SdkVersion = '', [string[]]$AdditionalRuntimes = @(),
        [Parameter(Mandatory)][scriptblock]$Inspect,
        [Parameter(Mandatory)][scriptblock]$InstallSdk,
        [Parameter(Mandatory)][scriptblock]$InstallRuntime
    )
    $state = & $Inspect $Channel $SdkVersion
    if (!$state.SdkVersion) {
        & $InstallSdk $Channel $SdkVersion | Out-Host
        $state = & $Inspect $Channel $SdkVersion
    }
    if (!$state.SdkVersion) { throw "No stable .NET $Channel SDK is available after installation." }
    # The SDK includes its runtime. Reinspect before considering a runtime-only
    # installation, so a missing SDK never causes a redundant runtime download.
    foreach ($runtimeChannel in @(@($Channel) + $AdditionalRuntimes | Select-Object -Unique)) {
        $runtime = & $Inspect $runtimeChannel ''
        if (!$runtime.RuntimeVersion) {
            & $InstallRuntime $runtimeChannel $state.Root | Out-Host
            $runtime = & $Inspect $runtimeChannel ''
        }
        if (!$runtime.RuntimeVersion) { throw "No stable .NET $runtimeChannel runtime is available after installation." }
    }
    & $Inspect $Channel $SdkVersion
}

function Write-DotNetSdkSelection {
    param([Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$SdkVersion)
    $path = Join-Path $RepositoryRoot 'global.json'
    $configuration = if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
    if (!$configuration.ContainsKey('sdk')) { $configuration.sdk = @{} }
    $configuration.sdk.version = $SdkVersion
    $configuration.sdk.rollForward = 'disable'
    $configuration.sdk.allowPrerelease = $false
    $configuration | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
}

Export-ModuleMember -Function Select-DotNetInstallation, Resolve-DotNetInstallation, Write-DotNetSdkSelection
