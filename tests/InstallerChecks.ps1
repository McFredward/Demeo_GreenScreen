$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repo 'scripts/Install.Helpers.psm1') -Force
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('demeo-installer-'+[Guid]::NewGuid().ToString('N'))
$script:checks=0
function Check([bool]$Condition,[string]$Name) {
    if (-not $Condition) { throw $Name }
    $script:checks++
}
function File([string]$Path,[string]$Text='test') {
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [IO.File]::WriteAllText($Path,$Text)
}
$global:DemeoInstallerBuilds=[Collections.Generic.List[string]]::new()
$global:DemeoInstallerFail=$false
$global:DemeoInstallerBackends=[Collections.Generic.List[string]]::new()
function global:dotnet {
    if ($args[0] -eq '--version') { $global:LASTEXITCODE=0; '8.0.423'; return }
    $configuration=$args[([array]::IndexOf($args,'-c')+1)]
    $output=$args[([array]::IndexOf($args,'--output')+1)]
    $global:DemeoInstallerBuilds.Add($configuration)
    $backend=@($args | Where-Object { $_ -like '-p:LoaderBackend=*' })
    if ($backend.Count -ne 1) { throw 'Installer must select the detected loader backend' }
    $global:DemeoInstallerBackends.Add(($backend[0] -split '=',2)[1])
    foreach ($property in @('LoaderDirectory','UnityAssemblyDirectory')) {
        $argument=@($args | Where-Object { $_ -like "-p:$property=*" })
        if ($argument.Count -ne 1) { throw "Missing explicit $property" }
        $directory=[Uri]::UnescapeDataString(($argument[0] -split '=',2)[1])
        $required=if ($property -eq 'UnityAssemblyDirectory') { 'UnityEngine.CoreModule.dll' }
            elseif ($backend[0] -like '*=MelonLoader') { 'MelonLoader.dll' }
            elseif ($configuration -eq 'DEMEO') { 'BepInEx.dll' } else { 'BepInEx.Unity.IL2CPP.dll' }
        if (-not (Test-Path -LiteralPath (Join-Path $directory $required))) { throw "Wrong $property for selected loader" }
    }
    if ($global:DemeoInstallerFail) { $global:LASTEXITCODE=1; return }
    if ($args -notcontains '-p:UseReferenceAssemblies=false') { throw 'Installer must use actual installed references' }
    $spec=Get-DemeoGames | Where-Object Id -eq $configuration
    [void][IO.Directory]::CreateDirectory($output)
    [IO.File]::WriteAllText((Join-Path $output $spec.Dll),'fresh-'+$configuration)
    $global:LASTEXITCODE=0
}
try {
    $steam=Join-Path $temporary 'Steam'
    $extra=Join-Path $temporary 'Second Library'
    File (Join-Path $steam 'steamapps/libraryfolders.vdf') ('"libraryfolders" { "1" { "path" "'+$extra.Replace('\','\\')+'" } }')
    $specs=@(Get-DemeoGames)
    $paths=@{}
    $number=0
    foreach ($spec in $specs) {
        $library=if ($number -eq 0) { $steam } else { $extra }
        $directory=Join-Path $library ('steamapps/common/Custom '+$spec.Name)
        $paths[$spec.Name]=$directory
        File (Join-Path $directory (($spec.Data -replace '_Data$','')+'.exe'))
        [void][IO.Directory]::CreateDirectory((Join-Path $directory $spec.Data))
        File (Join-Path $library ('steamapps/appmanifest_'+$number+'.acf')) ('"AppState" { "installdir" "Custom '+$spec.Name+'" }')
        File (Join-Path $directory 'BepInEx/core/0Harmony.dll')
        if ($spec.Id -ne 'DEMEO') {
            foreach ($dll in @('BepInEx.Core.dll','BepInEx.Unity.Common.dll','BepInEx.Unity.IL2CPP.dll','Il2CppInterop.Runtime.dll')) {
                File (Join-Path $directory ('BepInEx/core/'+$dll))
            }
            File (Join-Path $directory 'BepInEx/interop/Il2Cppmscorlib.dll')
        }
        if ($spec.Id -eq 'DEMEO') {
            File (Join-Path $directory ($spec.Data+'/Managed/UnityEngine.CoreModule.dll'))
            File (Join-Path $directory 'BepInEx/core/BepInEx.dll')
            File (Join-Path $directory ('BepInEx/plugins/Nested/'+$spec.Dll)) 'old'
            File (Join-Path $directory 'BepInEx/plugins/greenscreen.ini') '[COLOR] preserve-me'
        } elseif ($spec.Id -eq 'DEMEOBATTLES') {
            File (Join-Path $directory 'BepInEx/interop/UnityEngine.CoreModule.dll')
            File (Join-Path $directory 'BepInEx/core/BepInEx.Unity.IL2CPP.dll')
        } else {
            File (Join-Path $directory 'BepInEx/interop/UnityEngine.CoreModule.dll')
            File (Join-Path $directory 'BepInEx/plugins/custom_color.ini') 'R=123'
        }
        $number++
    }
    $found=@(Find-DemeoInstallations -SteamLibrary $steam)
    Check ($found.Count -eq 3) 'Discover three games across libraryfolders.vdf'
    Check (@(Find-DemeoInstallations -SteamLibrary $steam -Game DemeoBattles).Count -eq 1) 'Game filter'
    Check (@(Find-DemeoInstallations -GamePath $paths.Demeo).Count -eq 1) 'Explicit game folder'
    Check ((ConvertTo-MSBuildLiteral 'C:\Steam; $Games, 100%') -eq 'C:\Steam%3B %24Games%2C 100%25') 'Literal MSBuild path escaping'
    & (Join-Path $repo 'scripts/install.ps1') -SteamLibrary $steam -WhatIf
    Check ($global:DemeoInstallerBuilds.Count -eq 0) 'WhatIf performs no build or deployment'
    & (Join-Path $repo 'scripts/install.ps1') -SteamLibrary $steam
    Check ($global:DemeoInstallerBuilds.Count -eq 3) 'Build each selected installed game'
    $demeo=Join-Path $paths.Demeo 'BepInEx/plugins/Nested/Demeo_GreenScreen.dll'
    Check ([IO.File]::ReadAllText($demeo) -eq 'fresh-DEMEO') 'Update existing nested plugin instead of duplicating it'
    Check ([IO.File]::ReadAllText($demeo+'.bak') -eq 'old') 'Keep previous DLL backup'
    Check ([IO.File]::ReadAllText((Join-Path $paths.Demeo 'BepInEx/plugins/greenscreen.ini')) -eq '[COLOR] preserve-me') 'Preserve existing root config'
    Check (-not (Test-Path -LiteralPath (Join-Path $paths.Demeo 'BepInEx/plugins/Nested/greenscreen.ini'))) 'Do not shadow legacy root configuration'
    Check (-not (Test-Path -LiteralPath (Join-Path $paths.DemeoBattles 'BepInEx/plugins/greenscreen.ini'))) 'New BepInEx install does not create a plugin INI'
    Check (-not (Test-Path -LiteralPath (Join-Path $paths.DemeoBattles 'BepInEx/config/566784.cfg'))) 'Plugin creates the normal config on first launch so legacy migration is possible'
    Check ([IO.File]::ReadAllText((Join-Path $paths.DemeoBattlemarked 'BepInEx/plugins/custom_color.ini')) -eq 'R=123') 'Preserve custom_color.ini'
    Check (-not (Test-Path -LiteralPath (Join-Path $paths.DemeoBattlemarked 'BepInEx/plugins/greenscreen.ini'))) 'Do not shadow legacy filename'
    Check (($global:DemeoInstallerBackends -join ',') -eq 'BepInEx,BepInEx,BepInEx') 'Battlemarked without MelonLoader builds for installed BepInEx'
    # Add MelonLoader beside BepInEx; Auto must keep preferring BepInEx.
    foreach ($spec in $specs) {
        $directory=$paths[$spec.Name]
        $runtime=if ($spec.Id -eq 'DEMEO') { 'net35' } else { 'net6' }
        foreach ($dll in @('MelonLoader.dll','0Harmony.dll')) { File (Join-Path $directory ('MelonLoader/'+$runtime+'/'+$dll)) }
        if ($spec.Id -ne 'DEMEO') {
            File (Join-Path $directory 'MelonLoader/net6/Il2CppInterop.Runtime.dll')
            File (Join-Path $directory 'MelonLoader/Il2CppAssemblies/UnityEngine.CoreModule.dll')
            File (Join-Path $directory 'MelonLoader/Il2CppAssemblies/Il2Cppmscorlib.dll')
        }
    }
    $marked=$found | Where-Object { $_.Spec.Id -eq 'DEMEOBATTLEMARKED' }
    Check ((Resolve-DemeoLoaderPaths $marked).Backend -eq 'BepInEx') 'BepInEx folder takes priority when both loaders exist'
    & (Join-Path $repo 'scripts/install.ps1') -SteamLibrary $steam -Loader MelonLoader
    Check (($global:DemeoInstallerBackends | Select-Object -Last 3) -join ',' -eq 'MelonLoader,MelonLoader,MelonLoader') 'Explicit MelonLoader compiles all three matching entry points'
    Check (Test-Path -LiteralPath (Join-Path $paths.Demeo 'Mods/Demeo_GreenScreen.dll')) 'MelonLoader deploys Mono mod to Mods'
    Check (Test-Path -LiteralPath (Join-Path $paths.DemeoBattles 'Mods/DemeoBattles_GreenScreen.dll')) 'MelonLoader deploys IL2CPP mod to Mods'
    File (Join-Path $paths.Demeo 'MelonLoader/net472/MelonLoader.dll')
    File (Join-Path $paths.Demeo 'MelonLoader/net472/0Harmony.dll')
    $mono=$found | Where-Object { $_.Spec.Id -eq 'DEMEO' }
    Check ((Resolve-DemeoLoaderPaths $mono -Loader MelonLoader).Loader.EndsWith('net472')) 'Recognize current Mono net472 layout'
    foreach ($spec in $specs) {
        Move-Item -LiteralPath (Join-Path $paths[$spec.Name] 'BepInEx') -Destination (Join-Path $paths[$spec.Name] 'BepInEx.saved')
    }
    & (Join-Path $repo 'scripts/install.ps1') -SteamLibrary $steam
    Check (($global:DemeoInstallerBackends | Select-Object -Last 3) -join ',' -eq 'MelonLoader,MelonLoader,MelonLoader') 'Auto falls back to MelonLoader for all three games'
    foreach ($spec in $specs) {
        Move-Item -LiteralPath (Join-Path $paths[$spec.Name] 'BepInEx.saved') -Destination (Join-Path $paths[$spec.Name] 'BepInEx')
    }
    $global:DemeoInstallerFail=$true
    $failed=$false
    try { & (Join-Path $repo 'scripts/install.ps1') -Game Demeo -GamePath $paths.Demeo } catch { $failed=$true }
    Check $failed 'Propagate build failure'
    Check ([IO.File]::ReadAllText($demeo) -eq 'fresh-DEMEO') 'Failed build cannot replace installed DLL'
    $global:DemeoInstallerFail=$false
    $before=$global:DemeoInstallerBuilds.Count
    $duplicate=Join-Path $paths.Demeo 'BepInEx/plugins/Duplicate/Demeo_GreenScreen.dll'
    File $duplicate 'duplicate'
    $failed=$false
    try { & (Join-Path $repo 'scripts/install.ps1') -Game Demeo -GamePath $paths.Demeo } catch { $failed=$true }
    Check ($failed -and $global:DemeoInstallerBuilds.Count -eq $before) 'Duplicate plugin copies fail preflight'
    Remove-Item -LiteralPath $duplicate
    $global:DemeoInstallerRunningPath=Join-Path $paths.Demeo 'Demeo.exe'
    function global:Get-Process { [pscustomobject]@{ Path=$global:DemeoInstallerRunningPath } }
    $failed=$false
    try { & (Join-Path $repo 'scripts/install.ps1') -Game Demeo -GamePath $paths.Demeo } catch { $failed=$true }
    Check ($failed -and $global:DemeoInstallerBuilds.Count -eq $before) 'Running game fails preflight'
    Remove-Item Function:\global:Get-Process
    Remove-Variable DemeoInstallerRunningPath -Scope Global
    Remove-Item -LiteralPath (Join-Path $paths.DemeoBattlemarked 'BepInEx/core/BepInEx.Unity.IL2CPP.dll')
    $failed=$false
    try { & (Join-Path $repo 'scripts/install.ps1') -SteamLibrary $steam } catch { $failed=$true }
    Check ($failed -and $global:DemeoInstallerBuilds.Count -eq $before) 'Missing loader fails all-game preflight before first build'
    Check ($Error[0].ToString().Contains('BepInEx.Unity.IL2CPP.dll')) 'Incomplete BepInEx diagnostic identifies the missing file'
    Remove-Item -LiteralPath (Join-Path $paths.DemeoBattlemarked 'BepInEx') -Recurse
    Remove-Item -LiteralPath (Join-Path $paths.DemeoBattlemarked 'MelonLoader') -Recurse
    $message=''
    try { Resolve-DemeoLoaderPaths $marked } catch { $message=$_.ToString() }
    Check ($message.Contains('neither BepInEx nor MelonLoader') -and $message.Contains('battlemarked.exe')) 'Missing-loader diagnostic includes actual game executable'
    File (Join-Path $paths.DemeoBattlemarked 'MelonLoader/net35/MelonLoader.dll')
    $message=''
    try { Resolve-DemeoLoaderPaths $marked } catch { $message=$_.ToString() }
    Check ($message.Contains('net6') -and $message.Contains('net35')) 'Wrong-runtime diagnostic lists checked and found MelonLoader paths'
    Write-Host "Installer checks passed: $script:checks"
} finally {
    Remove-Item Function:\global:dotnet -ErrorAction SilentlyContinue
    Remove-Item Function:\global:Get-Process -ErrorAction SilentlyContinue
    Remove-Variable DemeoInstallerBuilds,DemeoInstallerFail,DemeoInstallerBackends -Scope Global -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
