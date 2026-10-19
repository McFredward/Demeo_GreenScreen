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
        if ($spec.Id -eq 'DEMEO') {
            $assemblies = Join-Path $install.Data 'Managed'
            $loader = Join-Path $install.Path 'BepInEx/core'
            $requiredLoader = 'BepInEx.dll'
            $pluginRoot = Join-Path $install.Path 'BepInEx/plugins'
        } elseif ($spec.Id -eq 'DEMEOBATTLES') {
            $assemblies = Join-Path $install.Path 'BepInEx/interop'
            $loader = Join-Path $install.Path 'BepInEx/core'
            $requiredLoader = 'BepInEx.Unity.IL2CPP.dll'
            $pluginRoot = Join-Path $install.Path 'BepInEx/plugins'
        } else {
            $assemblies = Join-Path $install.Path 'MelonLoader/Il2CppAssemblies'
            $loader = Join-Path $install.Path 'MelonLoader/net6'
            $requiredLoader = 'MelonLoader.dll'
            $pluginRoot = Join-Path $install.Path 'Mods'
        }
        if (-not (Test-Path -LiteralPath (Join-Path $loader $requiredLoader) -PathType Leaf)) {
            throw "$($spec.Name): install its BepInEx/MelonLoader version first (see README.md). Missing: $loader/$requiredLoader"
        }
        if (-not (Test-Path -LiteralPath (Join-Path $assemblies 'UnityEngine.CoreModule.dll') -PathType Leaf)) {
            throw "$($spec.Name): game/interop references missing in $assemblies. Start the game once with its loader, exit it, then run this script again."
        }
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
        $plans += [pscustomobject]@{ Install=$install; Assemblies=$assemblies; Loader=$loader; Destination=$destination; PluginRoot=$pluginRoot }
    }
    foreach ($plan in $plans) {
        $spec = $plan.Install.Spec
        if (-not $PSCmdlet.ShouldProcess($plan.Install.Path, "Build and install $($spec.Dll)")) { continue }
        $output = Join-Path $repo ('.build/install/' + $spec.Id)
        Write-Host "Building $($spec.Name) against its installed game/loader files..."
        & dotnet build (Join-Path $repo 'GreenScreen.csproj') -c $spec.Id --nologo --output $output `
            '-p:UseReferenceAssemblies=false' '-p:Optimize=true' `
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
        Copy-Item -LiteralPath (Join-Path $repo 'greenscreen.ini.example') -Destination (Join-Path $plan.Destination 'greenscreen.ini.example') -Force
        $hasConfig = $false
        foreach ($directory in @($plan.Destination,$plan.PluginRoot)) {
            foreach ($name in @('greenscreen.ini','custom_color.ini')) {
                if (Test-Path -LiteralPath (Join-Path $directory $name) -PathType Leaf) { $hasConfig = $true }
            }
        }
        if (-not $hasConfig) {
            Copy-Item -LiteralPath (Join-Path $repo 'greenscreen.ini.example') -Destination (Join-Path $plan.Destination 'greenscreen.ini')
        }
        Write-Host "Installed $target (SHA-256 $((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash))"
    }
} finally { Pop-Location }
