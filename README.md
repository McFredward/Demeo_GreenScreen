# Demeo Mixed Reality

Formerly **Demeo Green Screen**.

**Version 2.0.0** enables mixed reality in the PCVR versions of Demeo,
Demeo Battles and Demeo Battlemarked.
Steam Frame over Steam Link uses native passthrough automatically. Other headsets
can use the coloured background with Virtual Desktop.

<p align="center">
  <a href="#steam-frame-passthrough">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="docs/img/steam-frame-logo-dark.svg">
      <img src="docs/img/steam-frame-logo.svg" width="240" alt="Steam Frame">
    </picture><br>
    <strong>Native passthrough over Steam Link</strong>
  </a>
</p>

<p align="center">
  <img src="docs/img/demeo-mixed-reality.jpg" width="720" alt="Demeo on a virtual table in a real living room using Steam Frame passthrough">
</p>

## Installation

Install the loader for your game in its game folder:

| Game | Loader | Extract the mod ZIP into |
|---|---|---|
| Demeo | [BepInEx 5, x64 Mono](https://github.com/BepInEx/BepInEx/releases/download/v5.4.22/BepInEx_x64_5.4.22.0.zip) | `BepInEx/plugins` |
| Demeo Battles | [BepInEx 6, IL2CPP Windows x64](https://builds.bepinex.dev/projects/bepinex_be) | `BepInEx/plugins` |
| Demeo Battlemarked | [BepInEx 6, IL2CPP Windows x64](https://builds.bepinex.dev/projects/bepinex_be) | `BepInEx/plugins` |

1. Start the game once with its loader, then close it.
2. Download your game's ZIP from [Releases](https://github.com/McFredward/Demeo_GreenScreen/releases) and extract it into the folder above.
3. Start the game in VR. In Demeo and Demeo Battles, turn off the basement environment in the game settings.

For automatic installation from this download, install [.NET SDK 8+](https://dotnet.microsoft.com/download/dotnet/8.0), then run PowerShell in the extracted project folder:

```powershell
.\scripts\install.ps1
```

The installer finds installed games and their loaders, builds the matching mods
and installs them. Existing configuration is preserved. It also supports
MelonLoader for all three games.

## Steam Frame passthrough

Install [Steam Link Native Passthrough](https://github.com/McFredward/steam-link-native-passthrough)
on the PC and Frame. The mod enables native passthrough only when it detects
Steam Frame over Steam Link. All three games have been tested on the headset.

Steam Link can use **Auto or a fixed bitrate**. Close **OVR Advanced Settings**
while playing Demeo; it can cause unwanted recentering.

## Settings

BepInEx uses `BepInEx/config/DemeoMixedReality.cfg` (previously `566784.cfg`). Existing
`greenscreen.ini` / `custom_color.ini` settings are imported once; your old files
are kept. MelonLoader continues to use `greenscreen.ini` beside the mod DLL.
Changes apply while the game runs.

```ini
[COLOR]
R=0
G=255
B=0

[TABLE]
Disable=true

[STEAMLINK]
NativePassthrough=true

[LOGGING]
Level=Auto
```

Set `NativePassthrough=false` for Virtual Desktop colour-key mode. Use `[COLOR]`
with `R`, `G`, `B` values from 0–255 to change its background colour.

Logging defaults to full diagnostics on **dev** and important messages, warnings
and errors on **main**. `Level=Debug` enables diagnostics manually;
`Important`, `Warning` and `Error` are also available.

Existing DLL filenames and `greenscreen.ini` remain compatible with earlier
GreenScreen installations.
