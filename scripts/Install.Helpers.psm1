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

Export-ModuleMember -Function Get-DemeoGames, Get-SteamLibraries, Find-DemeoInstallations, ConvertTo-MSBuildLiteral
