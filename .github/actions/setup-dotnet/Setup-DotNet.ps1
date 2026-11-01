[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+$')][string]$Channel = '10.0',
    [string]$SdkVersion = '', [string]$AdditionalRuntimes = '',
    [string]$RepositoryRoot = (Get-Location).Path,
    [string]$InstallRoot = (Join-Path $env:RUNNER_TEMP 'duburl-dotnet')
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DotNetSetup.psm1') -Force
if ($SdkVersion) {
    if ($SdkVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'An exact SDK version must be stable.' }
    $Channel = ([Version]$SdkVersion).ToString(2)
}
$runtimes = @($AdditionalRuntimes -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($runtime in $runtimes) {
    if ($runtime -notmatch '^\d+\.\d+$') { throw 'Invalid additional runtime channel.' }
}
$command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$context = @{ Command = if ($command) { $command.Source } else { $null }; Installer = $null }

function Install-Component([string]$Kind, [string]$RequestedChannel, [string]$ExactVersion, [string]$Destination) {
    if (!$context.Installer) {
        $extension = if ($IsWindows) { 'ps1' } else { 'sh' }
        $context.Installer = Join-Path $env:RUNNER_TEMP "duburl-dotnet-install.$extension"
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.$extension" -OutFile $context.Installer -MaximumRetryCount 3
    }
    New-Item -ItemType Directory -Force $Destination | Out-Null
    Write-Host "Installing missing .NET $RequestedChannel $Kind."
    if ($IsWindows) {
        $arguments = @{ InstallDir = $Destination; NoPath = $true; SkipNonVersionedFiles = $true }
        if ($Kind -eq 'runtime') { $arguments.Runtime = 'dotnet'; $arguments.Channel = $RequestedChannel }
        elseif ($ExactVersion) { $arguments.Version = $ExactVersion }
        else { $arguments.Channel = $RequestedChannel; $arguments.Quality = 'GA' }
        & $context.Installer @arguments
    }
    else {
        $arguments = @('--install-dir', $Destination, '--no-path', '--skip-non-versioned-files')
        if ($Kind -eq 'runtime') { $arguments += @('--runtime', 'dotnet', '--channel', $RequestedChannel) }
        elseif ($ExactVersion) { $arguments += @('--version', $ExactVersion) }
        else { $arguments += @('--channel', $RequestedChannel, '--quality', 'GA') }
        & bash $context.Installer @arguments
    }
    if ($LASTEXITCODE -ne 0) { throw "Installing .NET $RequestedChannel $Kind failed." }
}

$inspect = {
    param($requestedChannel, $exactVersion)
    $sdkLines = @(); $runtimeLines = @()
    if ($context.Command) {
        $sdkLines = @(& $context.Command --list-sdks)
        if ($LASTEXITCODE) { throw 'Listing installed SDKs failed.' }
        $runtimeLines = @(& $context.Command --list-runtimes)
        if ($LASTEXITCODE) { throw 'Listing installed runtimes failed.' }
    }
    Select-DotNetInstallation -SdkLines $sdkLines -RuntimeLines $runtimeLines -Channel $requestedChannel -SdkVersion $exactVersion
}
$installSdk = {
    param($requestedChannel, $exactVersion)
    Install-Component 'SDK' $requestedChannel $exactVersion $InstallRoot
    $context.Command = Join-Path $InstallRoot $(if ($IsWindows) { 'dotnet.exe' } else { 'dotnet' })
}
$installRuntime = {
    param($requestedChannel, $destination)
    Install-Component 'runtime' $requestedChannel '' $destination
}
$state = Resolve-DotNetInstallation -Channel $Channel -SdkVersion $SdkVersion -AdditionalRuntimes $runtimes -Inspect $inspect -InstallSdk $installSdk -InstallRuntime $installRuntime
Write-DotNetSdkSelection -RepositoryRoot $RepositoryRoot -SdkVersion $state.SdkVersion
$env:PATH = $state.Root + [IO.Path]::PathSeparator + $env:PATH
$env:DOTNET_ROOT = $state.Root
$architectureVariable = 'DOTNET_ROOT_' + [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToUpperInvariant()
[Environment]::SetEnvironmentVariable($architectureVariable, $state.Root, 'Process')
if ($env:GITHUB_PATH) { $state.Root | Out-File -LiteralPath $env:GITHUB_PATH -Append -Encoding utf8NoBOM }
if ($env:GITHUB_ENV) {
    "DOTNET_ROOT=$($state.Root)" | Out-File -LiteralPath $env:GITHUB_ENV -Append -Encoding utf8NoBOM
    "$architectureVariable=$($state.Root)" | Out-File -LiteralPath $env:GITHUB_ENV -Append -Encoding utf8NoBOM
}
Push-Location $RepositoryRoot
try {
    $actual = & $context.Command --version
    if ($LASTEXITCODE -ne 0 -or $actual -ne $state.SdkVersion) { throw 'The selected SDK does not match the stable installed SDK.' }
}
finally { Pop-Location }
Write-Host "Using stable .NET SDK $($state.SdkVersion), runtime $($state.RuntimeVersion), from $($state.Root)."
