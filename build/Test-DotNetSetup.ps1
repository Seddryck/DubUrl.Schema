$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../.github/actions/setup-dotnet/DotNetSetup.psm1') -Force
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('dotnet-setup-tests-' + [Guid]::NewGuid().ToString('N'))
$resolvedRoot = [IO.Path]::GetFullPath($fixtureRoot)
if ((Split-Path $resolvedRoot -Parent).TrimEnd('\','/') -ne ([IO.Path]::GetTempPath()).TrimEnd('\','/') -or !(Split-Path $resolvedRoot -Leaf).StartsWith('dotnet-setup-tests-')) {
    throw 'Unexpected SDK test fixture path.'
}
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null

function Assert-Equal($Actual, $Expected, [string]$Name) {
    if ($Actual -ne $Expected) { throw "$Name expected '$Expected', got '$Actual'." }
}
function Assert-Throws([scriptblock]$Action, [string]$Name) {
    $caught = $false
    try { & $Action | Out-Null } catch { $caught = $true }
    if (!$caught) { throw "$Name did not fail." }
}
function New-Inventory([string[]]$Sdks = @(), [string[]]$Runtimes = @(), [bool]$BundleRuntime = $true, [bool]$BrokenSdk = $false) {
    $state = @{ Sdks = $Sdks; Runtimes = $Runtimes; Calls = [Collections.Generic.List[string]]::new(); BundleRuntime = $BundleRuntime; BrokenSdk = $BrokenSdk }
    $sdkPath = Join-Path $fixtureRoot 'installation/sdk'
    $runtimePath = Join-Path $fixtureRoot 'installation/shared/Microsoft.NETCore.App'
    $inspect = {
        param($channel, $exact)
        Select-DotNetInstallation -SdkLines @($state.Sdks | ForEach-Object { "$_ [$sdkPath]" }) -RuntimeLines @($state.Runtimes | ForEach-Object { "Microsoft.NETCore.App $_ [$runtimePath]" }) -Channel $channel -SdkVersion $exact
    }.GetNewClosure()
    $installSdk = {
        param($channel, $exact)
        $state.Calls.Add("sdk:$channel")
        if (!$state.BrokenSdk) {
            $state.Sdks += $(if ($exact) { $exact } else { "$channel.401" })
            if ($state.BundleRuntime) { $state.Runtimes += "$channel.99" }
        }
    }.GetNewClosure()
    $installRuntime = {
        param($channel, $destination)
        $state.Calls.Add("runtime:$channel")
        $state.Runtimes += "$channel.99"
    }.GetNewClosure()
    @{ State = $state; Inspect = $inspect; InstallSdk = $installSdk; InstallRuntime = $installRuntime }
}
function Resolve-Inventory($Inventory, [string[]]$Additional = @(), [string]$Channel = '10.0', [string]$Exact = '') {
    Resolve-DotNetInstallation -Inspect $Inventory.Inspect -InstallSdk $Inventory.InstallSdk -InstallRuntime $Inventory.InstallRuntime -AdditionalRuntimes $Additional -Channel $Channel -SdkVersion $Exact
}

try {
    $inventory = New-Inventory -Sdks @('10.0.100','10.0.401','11.0.100','10.0.500-preview.1') -Runtimes @('10.0.13','10.0.14','11.0.0','10.0.99-preview.1')
    $selection = Resolve-Inventory $inventory
    Assert-Equal $selection.SdkVersion '10.0.401' 'Highest stable .NET 10 SDK'
    Assert-Equal $selection.RuntimeVersion '10.0.14' 'Highest stable .NET 10 runtime'
    Assert-Equal $inventory.State.Calls.Count 0 'Installed components need no downloads'

    $inventory = New-Inventory
    $null = Resolve-Inventory $inventory
    Assert-Equal ($inventory.State.Calls -join ',') 'sdk:10.0' 'SDK bundle supplies a missing runtime'

    $inventory = New-Inventory -Runtimes @('10.0.14')
    $null = Resolve-Inventory $inventory
    Assert-Equal ($inventory.State.Calls -join ',') 'sdk:10.0' 'Only SDK missing'

    $inventory = New-Inventory -Sdks @('10.0.401')
    $null = Resolve-Inventory $inventory
    Assert-Equal ($inventory.State.Calls -join ',') 'runtime:10.0' 'Only runtime missing'

    $inventory = New-Inventory -Sdks @('10.0.500-preview.1') -Runtimes @('10.0.99-preview.1')
    $selection = Resolve-Inventory $inventory
    Assert-Equal $selection.SdkVersion '10.0.401' 'Preview SDK rejected'
    Assert-Equal ($inventory.State.Calls -join ',') 'sdk:10.0' 'Preview runtime rejected'

    $inventory = New-Inventory -Sdks @('10.0.401') -Runtimes @('10.0.14','9.0.12')
    $null = Resolve-Inventory $inventory -Additional @('8.0','9.0')
    Assert-Equal ($inventory.State.Calls -join ',') 'runtime:8.0' 'Only missing consumer runtime installed'

    $inventory = New-Inventory -Sdks @('8.0.416','9.0.308') -Runtimes @('8.0.22','9.0.12')
    $selection = Resolve-Inventory $inventory -Channel '8.0' -Exact '8.0.416'
    Assert-Equal $selection.SdkVersion '8.0.416' 'Pinned older SDK retained'
    Assert-Equal $inventory.State.Calls.Count 0 'Older job does not request an LTS runtime'

    $inventory = New-Inventory -BrokenSdk $true
    Assert-Throws { Resolve-Inventory $inventory } 'Failed SDK installation'

    $inventory = New-Inventory -BundleRuntime $false
    $null = Resolve-Inventory $inventory
    Assert-Equal ($inventory.State.Calls -join ',') 'sdk:10.0,runtime:10.0' 'Incomplete SDK bundle repaired'

    $selectionRoot = Join-Path $fixtureRoot 'selection'
    New-Item -ItemType Directory -Path $selectionRoot | Out-Null
    '{"msbuild-sdks":{"Custom.Sdk":"1.0.0"}}' | Set-Content (Join-Path $selectionRoot 'global.json')
    Write-DotNetSdkSelection -RepositoryRoot $selectionRoot -SdkVersion '10.0.401'
    $configuration = Get-Content (Join-Path $selectionRoot 'global.json') -Raw | ConvertFrom-Json
    Assert-Equal $configuration.sdk.version '10.0.401' 'SDK selection'
    Assert-Equal $configuration.sdk.rollForward 'disable' 'SDK roll-forward disabled'
    Assert-Equal $configuration.sdk.allowPrerelease $false 'SDK previews disabled'
    Assert-Equal $configuration.'msbuild-sdks'.'Custom.Sdk' '1.0.0' 'Other global.json settings preserved'

    function Test-InstalledWrapper {
        $wrapperRoot = Join-Path $fixtureRoot 'wrapper'
        $installedRoot = Join-Path $fixtureRoot 'fake-installed'
        New-Item -ItemType Directory -Path $wrapperRoot,$installedRoot | Out-Null
        $fake = Join-Path $installedRoot 'dotnet.ps1'
        $sdkPath = Join-Path $installedRoot 'sdk'
        $runtimePath = Join-Path $installedRoot 'shared/Microsoft.NETCore.App'
        $fakeCode = @'
switch ($args[0]) {
  '--list-sdks' { '10.0.401 [SDK_PATH]'; '11.0.100-preview.1 [SDK_PATH]' }
  '--list-runtimes' { 'Microsoft.NETCore.App 10.0.14 [RUNTIME_PATH]' }
  '--version' { '10.0.401' }
  default { throw 'Unexpected dotnet command' }
}
$global:LASTEXITCODE = 0
'@
        $fakeCode.Replace('SDK_PATH',$sdkPath).Replace('RUNTIME_PATH',$runtimePath) | Set-Content -LiteralPath $fake -Encoding utf8NoBOM
        function Get-Command {
            param($Name, $CommandType, $ErrorAction)
            if ($Name -eq 'dotnet') { [pscustomobject]@{ Source = $fake } }
            else { Microsoft.PowerShell.Core\Get-Command @PSBoundParameters }
        }
        function Invoke-WebRequest { throw 'Installed SDK/runtime must not trigger a download.' }
        $architectureVariable = 'DOTNET_ROOT_' + [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToUpperInvariant()
        $names = @('PATH','DOTNET_ROOT','GITHUB_ENV','GITHUB_PATH','RUNNER_TEMP',$architectureVariable)
        $previous = @{}
        foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
        try {
            $env:GITHUB_ENV = Join-Path $fixtureRoot 'github-env'
            $env:GITHUB_PATH = Join-Path $fixtureRoot 'github-path'
            $env:RUNNER_TEMP = $fixtureRoot
            & (Join-Path $PSScriptRoot '../.github/actions/setup-dotnet/Setup-DotNet.ps1') -RepositoryRoot $wrapperRoot -InstallRoot (Join-Path $fixtureRoot 'unused-installation')
            $configuration = Get-Content (Join-Path $wrapperRoot 'global.json') -Raw | ConvertFrom-Json
            Assert-Equal $configuration.sdk.version '10.0.401' 'Wrapper selects installed stable SDK'
            Assert-Equal ((Get-Content $env:GITHUB_PATH -Raw).Trim()) $installedRoot 'Wrapper exports installed dotnet path'
            if ((Get-Content $env:GITHUB_ENV -Raw) -notmatch 'DOTNET_ROOT=') { throw 'Wrapper did not export DOTNET_ROOT.' }
        }
        finally {
            foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
        }
    }
    Test-InstalledWrapper
    Write-Host 'Passed 11 SDK/runtime selection scenarios, including the real no-download wrapper path.'
}
finally {
    if ([IO.Path]::GetFullPath($fixtureRoot) -ne $resolvedRoot) { throw 'SDK fixture cleanup target changed.' }
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}
