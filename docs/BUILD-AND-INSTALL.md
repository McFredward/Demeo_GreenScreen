# Build, CI and installation

Use `dev` for development. `.github/workflows/build.yml` runs the protocol,
render-lifecycle and installer checks, verifies reference assemblies, downloads
pinned public loader dependencies, then compiles both loader variants for all
three games. Packaging retains the three default ZIPs: BepInEx Demeo/Battles and MelonLoader Battlemarked.
On `main` pushes the three ZIPs, `SHA256SUMS` and source commit receipt are
uploaded as one Actions artifact. Pull requests and `dev` builds run the same
checks without uploading release artifacts. No secret or write token is needed.

The ZIPs have an explicit five-file allowlist: the game's mod DLL,
`greenscreen.ini`, `README.md`, `LICENSE`, and `licenses/OpenVR-LICENSE.txt`.
The shipped INI enables native passthrough automatically on confirmed Frame /
Steam Link sessions. Game, interop, reference and BepInEx/MelonLoader DLLs
are excluded. The mod embeds an owned PE identity reader and a pinned public
OpenVR application SDK loader with its BSD licence; neither is game code. Dependencies use
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
default config is created only when none exists. New configs set
`NativePassthrough=true`; an independent runtime identity gate always disables
native mode outside Steam Frame over Steam Link. Existing `false` settings
remain explicit opt-outs.

## Checks and limits

```sh
dotnet run --project tests/ProtocolChecks.csproj
dotnet run --project tests/IdentityChecks.csproj
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


## Building and checking native game fades

The1.4.12 fade uses its own `assets/nativefade.bundle`; do not rebuild or replace
`nativecoverage.bundle` when changing only game fades. With the licensed Unity
2021.3 Editor and Windows build support, use `BuildFade.Build` on the `unity`
project. `FadeGpuChecks.Run` additionally needs an actual graphics device and
checks both eyes, separate/instanced rendering and1x/4x MSAA. Its source-over
black control must make an empty background opaque; the owned fade must keep
that background transparent throughout.

`python3 scripts/check-fade.py` validates the compiled owned Windows bundle with
UnityPy. `python3 scripts/check-fade-lifecycle.py` runs actual Harmony dispatch
for all three adapters using owned game/Unity ports and the locked modern CLR
test dependency. It does not run IL2CPP machine code or replace the mod loader's
Harmony. Packaging and CI include both gates, and each of the six mod DLLs must
embed the exact validated fade resource. No game assemblies enter the ZIPs.

Successful startup logs `Passthrough fade observer ready:` with the game's
controller type. `Game black fades now fade into passthrough` confirms a known
renderer was admitted under native permission. The bindings accommodate lazy
interop assembly loading on both loaders. Missing/changed contracts or an
unsupported shader retain original game fades and report a bounded reason.
Headset validation remains separate, starting with Demeo and then the two
IL2CPP games. Native PC0.2.14/Frame0.2.12 components require no update for this
mod-only change.
