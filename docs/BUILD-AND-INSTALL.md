# Build, CI and installation

Use `dev` for development. `.github/workflows/build.yml` runs the protocol,
render-lifecycle and installer checks, verifies reference assemblies, downloads
pinned public loader dependencies, then builds and packages all three games.
On `main` pushes the three ZIPs, `SHA256SUMS` and source commit receipt are
uploaded as one Actions artifact. Pull requests and `dev` builds run the same
checks without uploading release artifacts. No secret or write token is needed.

The ZIPs have an explicit four-file allowlist: the game's mod DLL,
`greenscreen.ini.example`, `README.md`, and `LICENSE`. Game, interop, reference
and loader DLLs are never copied into the archives. Dependencies use
`Private=false` and packaging does not recursively collect a build directory.

## Reference generation

The scripts follow GloomhavenVR's Refasmer approach. `libs/RefAsm/` contains
separate metadata sets for DEMEO, DEMEOBATTLES and DEMEOBATTLEMARKED.
Generation requires the owner's actual Mono/interop files and the pinned
local Refasmer tool; it never falls back to copying complete DLLs.
See [the reference README](../libs/RefAsm/README.md) for commands and provenance.

`scripts/check-refasm.py` reads the PE/CLI metadata and rejects any nonzero
MethodDef RVA. CI also runs a negative control using a freshly compiled mod DLL:
the gate must reject its executable IL. The build's assembly search order
prioritizes explicit HintPaths so one game's same-named Unity references cannot
silently satisfy another game's build.

## Local installer

`scripts/install.ps1` supports Windows PowerShell 5.1 and PowerShell 7. It uses
Steam's registry/libraryfolders/app manifests to find actual install roots,
including custom folder names and additional drives. `-GamePath` bypasses Steam
discovery; `-Game` selects one title. `-ListOnly` and `-WhatIf` write nothing.

The installer requires .NET SDK 8+ and these existing loader locations:

| Game | Loader | Build references | Destination |
| --- | --- | --- | --- |
| Demeo | BepInEx 5 Mono | `<game>/*emeo_Data/Managed` | `BepInEx/plugins` |
| Demeo Battles | BepInEx 6 IL2CPP | `BepInEx/interop` | `BepInEx/plugins` |
| Battlemarked | MelonLoader | `MelonLoader/Il2CppAssemblies` | `Mods` |

Each selected installation is checked before the first build/deployment. A game
must be closed and its loader/interop files present. SDK resolution and every
build run from the repository directory. Literal path characters are escaped
for MSBuild; arguments are passed as arguments, not evaluated as shell code.
The installer explicitly disables the metadata fallback so local game API
changes are visible during compilation.

A single existing plugin DLL can be updated in its existing subfolder. Duplicate
copies are refused. A successful build is staged beside the destination and
atomically replaces the DLL with one `.dll.bak` backup. Config files are not
overwritten, including legacy root configs and `custom_color.ini`. A new
default config is created only when none exists. Native passthrough remains
disabled until the user enables its INI option.

## Checks and limits

```sh
dotnet run --project tests/ProtocolChecks.csproj
dotnet run --project tests/IntegrationChecks.csproj -p:Game=DEMEO
dotnet run --project tests/IntegrationChecks.csproj -p:Game=DEMEOBATTLES
dotnet run --project tests/IntegrationChecks.csproj -p:Game=DEMEOBATTLEMARKED
pwsh -NoProfile -File tests/InstallerChecks.ps1
python3 scripts/fetch-build-deps.py
python3 scripts/package.py
```

Installer tests execute production discovery/deployment against temporary Steam
libraries, with a controlled SDK/build boundary. They cover three titles across
two libraries, custom install folders, filtering, explicit paths, dry runs,
config preservation, nested updates, backup, missing prerequisites and failed
builds. They do not replace an actual Windows Steam installation run.

Native passthrough additionally needs the per-game headset checks described in
[the source integration notes](STEAM-LINK-INTEGRATION.md).
