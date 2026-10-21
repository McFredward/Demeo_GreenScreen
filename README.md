# Demeo_GreenScreen
Mixed reality for the PCVR versions of Demeo, Demeo Battles & Demeo Battlemarked. On a detected Steam Frame over Steam Link, native passthrough is enabled by default. Other headsets and transports use a background colour for [Virtual Desktop](https://www.meta.com/de-de/experiences/1261331807302580/). Native alpha integrates [Steam Link Native Passthrough](https://github.com/McFredward/steam-link-native-passthrough) for Steam Frame.

<p align="center">
  <img src="demeo_green.jpg" width="50%" height="50%">
</p>

## How To Install & Use

<ins>**Demeo**</ins> <br>
* Download [BepInEx for Mono](https://github.com/BepInEx/BepInEx/releases/download/v5.4.22/BepInEx_x64_5.4.22.0.zip) **version 5.4.22.0**, and extract the files into the game's Folder.
* Create a new file in `Demeo\BepInEx\config\BepInEx.cfg` (create that folder if necessary). Fill it with the content from [here](https://github.com/orendain/DemeoMods/blob/main/docs/BepInEx.cfg).
* Download `Demeo.zip` from [here](https://github.com/McFredward/Demeo_GreenScreen/releases) and unzip the file into `Demeo\BepInEx\plugins`.
* Start the game & turn off the basement environment in the setting ingame.

<ins>**Demeo Battles**</ins> <br>

* Download [BepInEx for Il2CPP](https://builds.bepinex.dev/projects/bepinex_be) **version 6 Bleeding Edge, IL2CPP Windows (x64)**, and extract the files into the game's Folder.
* Download `DemeoBattles.zip` from [here](https://github.com/McFredward/Demeo_GreenScreen/releases) and unzip the file into `Demeo Battles\BepInEx\plugins`.
* Start the game & turn off the basement environment in the setting ingame.

<ins>**Demeo Battlemarked**</ins> <br>
The prebuilt Battlemarked ZIP targets **MelonLoader**. The source installer below also supports an existing BepInEx IL2CPP installation.

* Download [MelonLoader Installer](https://lemonloader.com/melonloader/) and install it for Demeo Battlemarked. **Make sure to enable nightly builds!**
* Download `DemeoBattlemarked.zip` from [here](https://github.com/McFredward/Demeo_GreenScreen/releases) and unzip the file into `Battlemarked\Mods`.
* Start the game.
  
<ins>**Demeo, Demeo Battles, Demeo Battlemarked**</ins> <br>

The following colour-key settings apply to **Virtual Desktop**. For Steam Link, use the optional mode below.

* Turn on "VR Passthrough" under the "Streaming"-Tab in the Virtual Desktop Menu (within VR).
* **USE THE FOLLOWING CONFIGURATION:**:

Red: 0, Green: 255, Blue: 0, Similarity: 37, Smoothness: 5
<p align="center">
  <img src="passthrough_configuration.jpg" width="25%" height="25%">
</p>

## Additional Settings
* The ZIP includes `greenscreen.ini` beside the mod DLL. Edit it to customize the settings:

```ini
[COLOR]
R=0
G=255
B=0

[TABLE]
Disable=false

[STEAMLINK]
NativePassthrough=true
```

The `[TABLE]` section is only relevant for Battlemarked - this way you can re-enable the table mesh (per default its disabled).
Feel free to adjust the `[COLOR]` values as desired, ensuring they stay within the range of 0 to 255 for each component. <br>
Additionally, please update the color in the VirtualDesktop configuration accordingly. <br>
If you discover a color or configuration that, in your opinion, performs better than the default green, let me know :)

### Automatic native Steam Link passthrough

This integration is experimental and needs an in-headset test for each game. It sends the game's ordinary RGB image with independent alpha; green game objects are not removed by a colour key.

1. Install the mod for your game as above. Demeo and Battles still need the basement environment disabled in the game settings.
2. Install matching PC and Steam Frame companions using the [Steam Link Native Passthrough installer](https://github.com/McFredward/steam-link-native-passthrough#installation). Its supported hardware/runtime requirements apply.
3. Keep the included `greenscreen.ini` beside the mod DLL. Its shipped setting is:

   ```ini
   [STEAMLINK]
   NativePassthrough=true
   ```

4. Start the game in VR over Steam Link with Direct3D 11. Check both eyes, menus, loading transitions, translucent effects and reconnects on the headset.

Changing `NativePassthrough` or `[COLOR]` in the INI takes effect within about one second. Set `NativePassthrough=false` to return to Virtual Desktop's colour-key mode. `custom_color.ini`, unsectioned RGB values, and a config in the loader's plugins/Mods root remain supported; `greenscreen.ini` beside the DLL takes priority.

Native passthrough activates only when the running headset is identified as Steam Frame connected through Steam Link. While identity is still being checked, the background stays opaque black so green cannot flash during startup. A confirmed other headset, Virtual Desktop or unsupported platform uses the colour-key background even with `NativePassthrough=true`. Detection uses the same isolated public OpenVR background reader as GloomhavenVR, with periodic rechecks and immediate invalidation on HMD device changes.

After installation, the script prints the active INI path. New configs default to `NativePassthrough=true`; existing configs retain their setting. At game startup the mod logs the selected INI path, `NativePassthrough` value, and whether its Unity update callback is running. The loader's plugins/Mods root is resolved from the loader API rather than the process working directory.

The native mode waits for a verified, live sender connection before enabling transparency. Missing companions, unsupported eye buffers or lost permission leave an opaque black background, with the reason in the mod log. Installation and a successful source handshake do not prove a correct headset picture.

Version 1.4.11 retains Demeo and Battles' original **single-pass stereo rendering** and copies each completed eye slice into a separate **2D texture immediately before OpenVR Submit**. It bypasses SteamVR's optional supersample filter **only while the current thread executes an admitted native eye Submit**, preserving coverage through the intermediate OpenVR copy. It leases the public settings interface without calling any setting writer; other queries, threads and games keep their original behavior. The maintainer confirmed correct opacity and matching eyes in Demeo. The maintainer confirmed that PC patch 0.2.14 removes the intermittent flashes and that both eyes show correctly opaque objects. The remaining short black intro/loading transitions are separate game fades and source switches. The [latest continuity review](docs/STEAM-LINK-INTEGRATION.md#october21-confirmed-pc-continuity-and-native-game-fades-1412) records the evidence and limits. Rendering resolution, cameras, FOV and tracking remain unchanged. Battles and Battlemarked still require their own headset validation.

The current PC0.2.14 continuity candidate is available on `dev`. Keep the mod at1.4.11 and run the companion's development installer on the gaming PC:

```powershell
irm https://raw.githubusercontent.com/McFredward/steam-link-native-passthrough/dev/get.ps1 | iex
```

With the matching Frame0.2.12 already installed, this uses the PC-only update path and leaves Frame files, settings and services untouched. The maintainer confirmed the repaired Demeo intro/menu picture. This does not certify every loading/source switch or the other two games.

Version **1.4.12** changes the identified game black fades into fades toward passthrough in **all three games**, while native mode is active. The game keeps its original fade timing, loading state and completion callbacks. Opting out, losing permission or unloading restores the game materials. This is a mod update; matching PC0.2.14/Frame0.2.12 companions need no update. Actual pauses where the game submits no supported eye image remain a separate limitation. The new fades still need a headset check; test Demeo first, then Battles and Battlemarked.

In Demeo, actual Unity tracking-origin notifications now mark the following two eye pairs with OpenVR's `Submit_FrameDiscontinuity`, to prevent motion smoothing from extrapolating across a coordinate change. This observes the game and user recentering without changing it. Battles and Battlemarked retain their input behavior. The separate-eye adapter requires a confirmed Frame / Steam Link connection, a live source grant and the pinned SDK. The failed 1.4.6 MultiPass transition remains removed, and Battlemarked retains its OpenXR layout.

For Unity 6 URP Forward/Forward+, the mod temporarily enables alpha processing and preserves alpha in HDR intermediates when needed. An embedded stereo shader repairs opaque-object alpha using each eye's current depth before transparent effects are drawn. Game post-processing is temporarily bypassed because released games can have its alpha shader variants stripped. It keeps Linear project lighting and Unity's normal sRGB output. These settings are restored on opt-out or unload. Eye resolution, MSAA and field of view are retained. The separate-eye submission path has an actual-SDK/D3D11 regression test; visual confirmation on the headset is still required. See [integration details and validation limits](docs/STEAM-LINK-INTEGRATION.md).

## Build and install automatically (Windows)

Install BepInEx or MelonLoader first and start each IL2CPP game once so its interop assemblies are generated. Install the **.NET SDK 8 or newer**, clone this repository, close the games, and run from PowerShell:

```powershell
.\scripts\install.ps1
```

The script finds all three games through Steam's registry entry, `libraryfolders.vdf` and app manifests, builds against each game's actual local assemblies, and copies its mod DLL into `BepInEx/plugins` or `Mods`. It checks for a `BepInEx` folder first for every game, including Battlemarked, and otherwise detects MelonLoader. Both loaders have matching mod entry points for all three games. Existing nested plugin locations and config files are retained; the previous DLL is saved as `.dll.bak`.

```powershell
.\scripts\install.ps1 -ListOnly
.\scripts\install.ps1 -WhatIf
.\scripts\install.ps1 -Game Demeo
.\scripts\install.ps1 -Game DemeoBattles -GamePath "D:\SteamLibrary\steamapps\common\Demeo Battles"
.\scripts\install.ps1 -SteamLibrary "D:\SteamLibrary"
.\scripts\install.ps1 -Game DemeoBattlemarked -Loader BepInEx
.\scripts\install.ps1 -Loader MelonLoader
```

`-Loader` overrides automatic detection when needed. BepInEx uses `core` and, for IL2CPP, `interop`; MelonLoader uses `net35`/`net472` for Mono or `net6` plus `Il2CppAssemblies` for IL2CPP. An incomplete BepInEx folder reports the missing DLL instead of silently selecting another loader.

The installer preflights every selected game before installing anything. A missing loader/interop directory or a running game stops installation; a failed build leaves that game's installed DLL intact. It does not install the separate Steam Frame companion.

## Building

Build each configuration against that game's installed loader and managed/interop assemblies:

```sh
dotnet build GreenScreen.csproj -c DEMEO -p:GameDirectory="C:/Games/Demeo"
dotnet build GreenScreen.csproj -c DEMEOBATTLES -p:GameDirectory="C:/Games/Demeo Battles"
dotnet build GreenScreen.csproj -c DEMEOBATTLEMARKED -p:GameDirectory="C:/Games/Battlemarked"
```

Use `-p:LoaderBackend=BepInEx` or `-p:LoaderBackend=MelonLoader` to select a loader explicitly. `UnityAssemblyDirectory` and `LoaderDirectory` can override the reference locations independently. Loader and game assemblies are not copied into the output. The coverage shader is embedded in the mod DLL; rebuilding it requires Unity 2021.3 with Windows build support and the project's enabled AssetBundle module (see `unity/Assets/Editor/BuildCoverage.cs`). The build verifies native loading of the resulting bundle and its Shader asset.

### CI builds without an installed game

Development happens on `dev`. CI checks `dev`/`main` pushes and pull requests. Every push to `main` builds `Demeo.zip`, `DemeoBattles.zip` and `DemeoBattlemarked.zip`, available together with checksums in the workflow's **Demeo-mods** artifact. The workflow does not push commits or create releases.

CI uses committed [metadata-only references](libs/RefAsm/README.md). Every method body is removed, and CI verifies that all MethodDef RVAs are zero. Public loader dependencies are downloaded from SHA-256-pinned archives. CI compiles and checks both loader variants for each game; the three ZIPs retain their documented defaults (BepInEx for Demeo/Battles, MelonLoader for Battlemarked). The local installer builds the variant detected on your PC. ZIP packaging allows only the mod DLL, active `greenscreen.ini`, README and licences. The mod embeds a small owned identity reader and Valve's pinned public OpenVR SDK loader; game assemblies and mod loader assemblies are excluded.

CI also validates the coverage bundle's asset directory, its shader reference, D3D11 programs and named passes. It rejects the malformed 1.4.1 bundle as a regression fixture and checks that all six DLLs embed the exact validated bytes. Packaging requires the Python dependencies pinned in `scripts/coverage-requirements.txt`; these build tools are excluded from the ZIPs.

Run the same package build locally without a game installation:

```sh
python3 scripts/fetch-build-deps.py
python3 scripts/package.py
```

See [CI and installation details](docs/BUILD-AND-INSTALL.md) for reference regeneration and checks.

## Known issues

* Visible outlines around some objects (Depends on your passthrough configuration in Virtual Desktop)
* You can look through some parts of some objects which also have the same color (e.g. the Swiftness Potion Card for the default green background).

## Credits & Libs

* Code inspired by "SkipIntro" from [DemeoMods by orendain](https://github.com/orendain/DemeoMods) using the MIT License
* [HarmonyLib](https://github.com/pardeike/Harmony) using the MIT License
* [BepInEx](https://github.com/BepInEx/BepInEx) using the LGPL2.1 license
* [MelonLoader](https://github.com/LavaGang/MelonLoader) using the Apache-2.0
* [Steam Link Native Passthrough](https://github.com/McFredward/steam-link-native-passthrough) for the paired PC/Frame companion and application source protocol
* [GloomhavenVR](https://github.com/McFredward/GloomhavenVR) for the headset identity reader, source-negotiation client and opaque-depth coverage approach (GPL-3.0)
* [Valve OpenVR SDK](https://github.com/ValveSoftware/openvr) 1.26.7 (BSD-3-Clause); bundled licence in `licenses/OpenVR-LICENSE.txt`

If you have any suggestions, feel free to open an Issue. Or contact me (McFredward) in via Discord.

