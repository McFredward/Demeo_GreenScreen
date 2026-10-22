<#
.SYNOPSIS
Detect installed Steam Demeo games, compile each mod against the local game and
loader assemblies, and install it. Requires .NET SDK 8+ and an installed loader.
.EXAMPLE
./scripts/install.ps1
.EXAMPLE
./scripts/install.ps1 -Game Demeo -GamePath 'D:\SteamLibrary\steamapps\common\Demeo'
.EXAMPLE
./scripts/install.ps1 -SteamLibrary 'D:\SteamLibrary' -ListOnly
#>
[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [ValidateSet('All','Demeo','DemeoBattles','DemeoBattlemarked')]
    [string]$Game='All',
    [string]$GamePath,
    [string[]]$SteamLibrary,
    [ValidateSet('Auto','BepInEx','MelonLoader')]
    [string]$Loader='Auto',
    [switch]$ListOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Install.Helpers.psm1') -Force
$repo = Split-Path -Parent $PSScriptRoot
$installed = @(Find-DemeoInstallations -SteamLibrary $SteamLibrary -GamePath $GamePath -Game $Game)
if ($installed.Count -eq 0) {
    throw 'No matching Steam game found. Use -GamePath with the game folder, or -SteamLibrary with a Steam library root.'
}
if ($ListOnly) {
    $installed | ForEach-Object { [pscustomobject]@{ Game=$_.Spec.Name; Path=$_.Path } }
    return
}
if ($WhatIfPreference) {
    foreach ($install in $installed) { [void]$PSCmdlet.ShouldProcess($install.Path, "Build and install $($install.Spec.Dll)") }
    return
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET SDK 8 or newer, then run this script again.' }
Push-Location -LiteralPath $repo
try {
    $sdk = @(& dotnet --version)
    if ($LASTEXITCODE -ne 0 -or ($sdk -join '').Trim() -notmatch '^(8|9|[1-9][0-9])\.') {
        throw 'A compatible .NET SDK 8+ is required; a runtime-only installation cannot build the mods.'
    }
    $plans = @()
    # Complete preflight for every selected game before replacing any DLL.
    foreach ($install in $installed) {
        $spec = $install.Spec
        $resolved = Resolve-DemeoLoaderPaths -Install $install -Loader $Loader
        $pluginRoot = $resolved.PluginRoot
        $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -and [IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($install.Executable) } catch { $false }
        })
        if ($running.Count -gt 0) { throw "Close $($spec.Name) before installing its mod." }
        $existing = @()
        if (Test-Path -LiteralPath $pluginRoot -PathType Container) {
            $existing = @(Get-ChildItem -LiteralPath $pluginRoot -Filter $spec.Dll -File -Recurse)
        }
        if ($existing.Count -gt 1) { throw "$($spec.Name): multiple copies of $($spec.Dll) exist. Remove duplicates before updating." }
        $destination = if ($existing.Count -eq 1) { $existing[0].DirectoryName } else { $pluginRoot }
        $plans += [pscustomobject]@{ Install=$install; Assemblies=$resolved.Assemblies; Loader=$resolved.Loader; Backend=$resolved.Backend; Destination=$destination; PluginRoot=$pluginRoot }
    }
    foreach ($plan in $plans) {
        $spec = $plan.Install.Spec
        if (-not $PSCmdlet.ShouldProcess($plan.Install.Path, "Build and install $($spec.Dll)")) { continue }
        $output = Join-Path $repo ('.build/install/' + $plan.Backend + '/' + $spec.Id)
        Write-Host "Building $($spec.Name) for detected $($plan.Backend) against $($plan.Loader)..."
        & dotnet build (Join-Path $repo 'GreenScreen.csproj') -c $spec.Id --nologo --output $output `
            '-p:UseReferenceAssemblies=false' '-p:Optimize=true' `
            "-p:LoaderBackend=$($plan.Backend)" `
            "-p:UnityAssemblyDirectory=$(ConvertTo-MSBuildLiteral $plan.Assemblies)" `
            "-p:LoaderDirectory=$(ConvertTo-MSBuildLiteral $plan.Loader)"
        if ($LASTEXITCODE -ne 0) { throw "$($spec.Name) build failed; its installed DLL was not replaced." }
        $binary = Join-Path $output $spec.Dll
        if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) { throw "Build did not produce $binary" }
        [void][IO.Directory]::CreateDirectory($plan.Destination)
        $target = Join-Path $plan.Destination $spec.Dll
        $stage = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        try {
            Copy-Item -LiteralPath $binary -Destination $stage
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                # Atomic replacement with one previous-DLL backup, same volume.
                [IO.File]::Replace($stage, $target, ($target + '.bak'))
            } else {
                [IO.File]::Move($stage, $target)
            }
        } finally { if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Force } }
        if ($plan.Backend -eq 'BepInEx') {
            # The plugin creates/binds its normal config on first launch, after
            # reading any legacy INI. Precreating it would suppress migration.
            $activeConfig = Join-Path $plan.Install.Path 'BepInEx/config/DemeoMixedReality.cfg'
        } else {
            Copy-Item -LiteralPath (Join-Path $repo 'greenscreen.ini.example') -Destination (Join-Path $plan.Destination 'greenscreen.ini.example') -Force
            $activeConfig = $null
            foreach ($name in @('greenscreen.ini','custom_color.ini')) {
                foreach ($directory in @($plan.Destination,$plan.PluginRoot)) {
                    $candidate = Join-Path $directory $name
                    if (-not $activeConfig -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { $activeConfig = $candidate }
                }
            }
            if (-not $activeConfig) {
                $activeConfig = Join-Path $plan.Destination 'greenscreen.ini'
                Copy-Item -LiteralPath (Join-Path $repo 'greenscreen.ini.example') -Destination $activeConfig
            }
        }
        Write-Host "Installed $target (SHA-256 $((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash))"
        Write-Host "Configuration: $activeConfig"
        Write-Host 'New configs enable native passthrough automatically for Frame over Steam Link. Other headsets/transports use the colour-key background. NativePassthrough=false opts out; existing configs are preserved.'
    }
} finally { Pop-Location }
