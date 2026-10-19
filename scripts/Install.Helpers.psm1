Set-StrictMode -Version Latest

function Get-DemeoGames {
    @(
        [pscustomobject]@{ Id='DEMEO'; Name='Demeo'; Data='Demeo_Data'; Dll='Demeo_GreenScreen.dll'; Framework='netstandard2.1' }
        [pscustomobject]@{ Id='DEMEOBATTLES'; Name='DemeoBattles'; Data='DemeoBattles_Data'; Dll='DemeoBattles_GreenScreen.dll'; Framework='net6.0' }
        [pscustomobject]@{ Id='DEMEOBATTLEMARKED'; Name='DemeoBattlemarked'; Data='battlemarked_Data'; Dll='DemeoBattlemarked_GreenScreen.dll'; Framework='net6.0' }
    )
}

function Get-SteamLibraries {
    param([string[]]$Roots)
    if (-not $Roots) {
        $Roots = @()
        foreach ($key in @('HKCU:\Software\Valve\Steam','HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
            try {
                $settings = Get-ItemProperty -LiteralPath $key -ErrorAction Stop
                foreach ($name in @('SteamPath','InstallPath')) {
                    if ($settings.PSObject.Properties[$name]) { $Roots += $settings.$name }
                }
            } catch { }
        }
        if (${env:ProgramFiles(x86)}) { $Roots += Join-Path ${env:ProgramFiles(x86)} 'Steam' }
    }
    $libraries = @($Roots)
    foreach ($steamRoot in $Roots) {
        $vdf = Join-Path $steamRoot 'steamapps/libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf -PathType Leaf) {
            foreach ($match in [regex]::Matches([IO.File]::ReadAllText($vdf), '"path"\s+"((?:\\.|[^"\\])*)"')) {
                $libraries += $match.Groups[1].Value.Replace('\\','\').Replace('\"','"')
            }
        }
    }
    @($libraries | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) } |
        ForEach-Object { (Resolve-Path -LiteralPath $_).Path } | Sort-Object -Unique)
}

function Find-DemeoInstallations {
    param([string[]]$SteamLibrary, [string]$GamePath, [string]$Game='All')
    $roots = @()
    if ($GamePath) {
        $roots += (Resolve-Path -LiteralPath $GamePath -ErrorAction Stop).Path
    } else {
        foreach ($library in @(Get-SteamLibraries -Roots $SteamLibrary)) {
            $apps = Join-Path $library 'steamapps'
            if (-not (Test-Path -LiteralPath $apps -PathType Container)) { continue }
            foreach ($manifest in @(Get-ChildItem -LiteralPath $apps -Filter 'appmanifest_*.acf' -File)) {
                $match = [regex]::Match([IO.File]::ReadAllText($manifest.FullName), '"installdir"\s+"((?:\\.|[^"\\])*)"')
                if (-not $match.Success) { continue }
                $folder = $match.Groups[1].Value.Replace('\\','\').Replace('\"','"')
                if ($folder -in @('.','..') -or $folder.IndexOfAny([char[]]'\/') -ge 0) { continue }
                $roots += Join-Path (Join-Path $apps 'common') $folder
            }
        }
    }
    foreach ($path in @($roots | Sort-Object -Unique)) {
        foreach ($spec in @(Get-DemeoGames)) {
            if ($Game -ne 'All' -and $Game -ne $spec.Name) { continue }
            $data = Join-Path $path $spec.Data
            # Windows filesystems are case-insensitive; handle lowercase Demeo
            # data folders on portable test/development filesystems as well.
            if (-not (Test-Path -LiteralPath $data -PathType Container) -and $spec.Id -eq 'DEMEO') {
                $data = Join-Path $path 'demeo_Data'
            }
            $exe = Join-Path $path (($spec.Data -replace '_Data$','') + '.exe')
            if (-not (Test-Path -LiteralPath $data -PathType Container) -or -not (Test-Path -LiteralPath $exe -PathType Leaf)) { continue }
            [pscustomobject]@{ Spec=$spec; Path=$path; Data=$data; Executable=$exe }
        }
    }
}

function ConvertTo-MSBuildLiteral {
    param([string]$Value)
    $result = $Value.Replace('%','%25')
    foreach ($character in @(';',',','$','@',"'",'(',')','?','*')) {
        $result = $result.Replace($character, ('%{0:X2}' -f [int][char]$character))
    }
    $result
}

function Resolve-DemeoLoaderPaths {
    param($Install, [ValidateSet('Auto','BepInEx','MelonLoader')][string]$Loader='Auto')
    $spec = $Install.Spec
    $bepRoot = Join-Path $Install.Path 'BepInEx'
    $melonRoot = Join-Path $Install.Path 'MelonLoader'
    if ($Loader -eq 'Auto') {
        # Prefer the existing BepInEx installation, including Battlemarked.
        if (Test-Path -LiteralPath $bepRoot -PathType Container) { $Loader = 'BepInEx' }
        elseif (Test-Path -LiteralPath $melonRoot -PathType Container) { $Loader = 'MelonLoader' }
        else {
            throw "$($spec.Name): neither BepInEx nor MelonLoader exists beside $($Install.Executable). Check the game path used by the loader installer, or use -GamePath with that game folder."
        }
    }
    $required = @('0Harmony.dll')
    if ($Loader -eq 'BepInEx') {
        $directory = Join-Path $bepRoot 'core'
        $destination = Join-Path $bepRoot 'plugins'
        if ($spec.Id -eq 'DEMEO') { $required += 'BepInEx.dll' }
        else { $required += @('BepInEx.Core.dll','BepInEx.Unity.Common.dll','BepInEx.Unity.IL2CPP.dll','Il2CppInterop.Runtime.dll') }
        $assemblies = Join-Path $bepRoot 'interop'
    } else {
        $destination = Join-Path $Install.Path 'Mods'
        $runtimes = if ($spec.Id -eq 'DEMEO') { @('net472','net35') } else { @('net6') }
        $directory = $null
        foreach ($runtime in $runtimes) {
            $candidate = Join-Path $melonRoot $runtime
            if (Test-Path -LiteralPath (Join-Path $candidate 'MelonLoader.dll') -PathType Leaf) {
                $directory = $candidate
                break
            }
        }
        if (-not $directory) {
            $checked = ($runtimes | ForEach-Object { Join-Path (Join-Path $melonRoot $_) 'MelonLoader.dll' }) -join ', '
            $found = if (Test-Path -LiteralPath $melonRoot -PathType Container) {
                @(Get-ChildItem -LiteralPath $melonRoot -Filter MelonLoader.dll -File -Recurse | ForEach-Object FullName) -join ', '
            } else { '' }
            throw "$($spec.Name): compatible MelonLoader runtime DLL not found. Checked: $checked. Found: $found. Install current x64 MelonLoader for $($Install.Executable), or select -Loader BepInEx if installed."
        }
        $required += 'MelonLoader.dll'
        if ($spec.Id -ne 'DEMEO') { $required += 'Il2CppInterop.Runtime.dll' }
        $assemblies = Join-Path $melonRoot 'Il2CppAssemblies'
    }
    foreach ($dll in $required) {
        $file = Join-Path $directory $dll
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "$($spec.Name): detected $Loader in $($Install.Path), but required DLL is missing: $file. Check that this loader installation is complete and matches the game's Mono/IL2CPP backend. Use -Loader to select another installed loader."
        }
    }
    if ($spec.Id -eq 'DEMEO') { $assemblies = Join-Path $Install.Data 'Managed' }
    $gameDlls = @('UnityEngine.CoreModule.dll')
    if ($spec.Id -ne 'DEMEO') { $gameDlls += 'Il2Cppmscorlib.dll' }
    foreach ($dll in $gameDlls) {
        if (-not (Test-Path -LiteralPath (Join-Path $assemblies $dll) -PathType Leaf)) {
            throw "$($spec.Name): $Loader game/interop references missing in $assemblies ($dll). Start this game once with $Loader, exit it, then run this script again."
        }
    }
    [pscustomobject]@{ Backend=$Loader; Loader=$directory; Assemblies=$assemblies; PluginRoot=$destination }
}

Export-ModuleMember -Function Get-DemeoGames, Get-SteamLibraries, Find-DemeoInstallations, ConvertTo-MSBuildLiteral, Resolve-DemeoLoaderPaths
