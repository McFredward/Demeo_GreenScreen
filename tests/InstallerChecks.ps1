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
function global:dotnet {
    if ($args[0] -eq '--version') { $global:LASTEXITCODE=0; '8.0.423'; return }
    $configuration=$args[([array]::IndexOf($args,'-c')+1)]
    $output=$args[([array]::IndexOf($args,'--output')+1)]
    $global:DemeoInstallerBuilds.Add($configuration)
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
        if ($spec.Id -eq 'DEMEO') {
            File (Join-Path $directory ($spec.Data+'/Managed/UnityEngine.CoreModule.dll'))
            File (Join-Path $directory 'BepInEx/core/BepInEx.dll')
            File (Join-Path $directory ('BepInEx/plugins/Nested/'+$spec.Dll)) 'old'
            File (Join-Path $directory 'BepInEx/plugins/greenscreen.ini') '[COLOR] preserve-me'
        } elseif ($spec.Id -eq 'DEMEOBATTLES') {
            File (Join-Path $directory 'BepInEx/interop/UnityEngine.CoreModule.dll')
            File (Join-Path $directory 'BepInEx/core/BepInEx.Unity.IL2CPP.dll')
        } else {
            File (Join-Path $directory 'MelonLoader/Il2CppAssemblies/UnityEngine.CoreModule.dll')
            File (Join-Path $directory 'MelonLoader/net6/MelonLoader.dll')
            File (Join-Path $directory 'Mods/custom_color.ini') 'R=123'
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
    Check (Test-Path -LiteralPath (Join-Path $paths.DemeoBattles 'BepInEx/plugins/greenscreen.ini')) 'Create default opt-in-disabled config for new install'
    Check ([IO.File]::ReadAllText((Join-Path $paths.DemeoBattles 'BepInEx/plugins/greenscreen.ini')).Contains('NativePassthrough=false')) 'New installation keeps native passthrough opt-in'
    Check ([IO.File]::ReadAllText((Join-Path $paths.DemeoBattlemarked 'Mods/custom_color.ini')) -eq 'R=123') 'Preserve custom_color.ini'
    Check (-not (Test-Path -LiteralPath (Join-Path $paths.DemeoBattlemarked 'Mods/greenscreen.ini'))) 'Do not shadow legacy filename'
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
    Remove-Item -LiteralPath (Join-Path $paths.DemeoBattlemarked 'MelonLoader/net6/MelonLoader.dll')
    $failed=$false
    try { & (Join-Path $repo 'scripts/install.ps1') -SteamLibrary $steam } catch { $failed=$true }
    Check ($failed -and $global:DemeoInstallerBuilds.Count -eq $before) 'Missing loader fails all-game preflight before first build'
    Write-Host "Installer checks passed: $script:checks"
} finally {
    Remove-Item Function:\global:dotnet -ErrorAction SilentlyContinue
    Remove-Item Function:\global:Get-Process -ErrorAction SilentlyContinue
    Remove-Variable DemeoInstallerBuilds,DemeoInstallerFail -Scope Global -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
