# Build, CI and installation

Use `dev` for development. `.github/workflows/build.yml` runs the protocol,
render-lifecycle and installer checks, verifies reference assemblies, downloads
pinned public loader dependencies, then compiles both loader variants for all
three games. Packaging retains the three default ZIPs: BepInEx Demeo/Battles and MelonLoader Battlemarked.
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

The installer requires .NET SDK 8+. For every game it checks whether a
`BepInEx` folder exists first, then tries `MelonLoader`. `-Loader BepInEx` or
`-Loader MelonLoader` overrides this choice. Each loader has its own compiled
entry point, logger and Unity update callbacks; changing the destination alone
would not produce a compatible mod.

| Loader / game backend | Loader DLL directory | Game references | Destination |
| --- | --- | --- | --- |
| BepInEx / Demeo Mono | `BepInEx/core` | `<game>/*emeo_Data/Managed` | `BepInEx/plugins` |
| BepInEx / Battles or Battlemarked IL2CPP | `BepInEx/core` | `BepInEx/interop` | `BepInEx/plugins` |
| MelonLoader / Demeo Mono | `MelonLoader/net472` or `net35` | `<game>/*emeo_Data/Managed` | `Mods` |
| MelonLoader / Battles or Battlemarked IL2CPP | `MelonLoader/net6` | `MelonLoader/Il2CppAssemblies` | `Mods` |

An existing BepInEx folder with missing core DLLs reports those missing files;
it does not silently fall back to MelonLoader. Missing-loader errors identify
the detected game executable so an installation into another game folder can
be diagnosed. MelonLoader runtime errors list expected and discovered DLL paths.

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
python3 scripts/check-integration.py
pwsh -NoProfile -File tests/InstallerChecks.ps1
python3 scripts/fetch-build-deps.py
python3 scripts/package.py
```

Installer tests execute production discovery/deployment against temporary Steam
libraries, with a controlled SDK/build boundary. They cover three titles across
two libraries, custom install folders, filtering, explicit paths, dry runs,
config preservation, nested updates, backup, missing prerequisites and failed
builds. They also cover BepInEx Battlemarked without MelonLoader, both-loader
priority, explicit selection, MelonLoader for all three games, Mono runtime
layouts, and missing/wrong-runtime diagnostics. They do not replace an actual
Windows Steam installation run.

Native passthrough additionally needs the per-game headset checks described in
[the source integration notes](STEAM-LINK-INTEGRATION.md).
