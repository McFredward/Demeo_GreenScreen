# Demeo Mixed Reality

**Version 2.0.0** brings Demeo, Demeo Battles and Demeo Battlemarked into your room.
Steam Frame over Steam Link uses native passthrough automatically. Other headsets
can use the coloured background with Virtual Desktop.

<p align="center">
  <img src="demeo_green.jpg" width="60%" alt="Demeo in mixed reality">
</p>

## Installation

Install the loader for your game in its game folder:

| Game | Loader | Extract the mod ZIP into |
|---|---|---|
| Demeo | [BepInEx 5, x64 Mono](https://github.com/BepInEx/BepInEx/releases/download/v5.4.22/BepInEx_x64_5.4.22.0.zip) | `BepInEx/plugins` |
| Demeo Battles | [BepInEx 6, IL2CPP Windows x64](https://builds.bepinex.dev/projects/bepinex_be) | `BepInEx/plugins` |
| Demeo Battlemarked | [MelonLoader nightly](https://lemonloader.com/melonloader/) | `Mods` |

1. Start the game once with its loader, then close it.
2. Download your game's ZIP from [Releases](https://github.com/McFredward/Demeo_GreenScreen/releases) and extract it into the folder above.
3. Start the game in VR. In Demeo and Demeo Battles, turn off the basement environment in the game settings.

For automatic installation from this download, install [.NET SDK 8+](https://dotnet.microsoft.com/download/dotnet/8.0), then run PowerShell in the extracted project folder:

```powershell
.\scripts\install.ps1
```

The installer finds installed games and their loaders, builds the matching mods
and installs them. Existing configuration is preserved. It also supports
BepInEx for Battlemarked.

## Steam Frame passthrough

Install [Steam Link Native Passthrough](https://github.com/McFredward/steam-link-native-passthrough)
on the PC and Frame. The mod enables native passthrough only when it detects
Steam Frame over Steam Link. Demeo and Demeo Battles are headset-tested;
Battlemarked still awaits its headset test.

Steam Link can use **Auto or a fixed bitrate**. Close **OVR Advanced Settings**
while playing Demeo; it can cause unwanted recentering.

## Settings

Edit `greenscreen.ini` beside the mod DLL. Changes apply while the game runs.

```ini
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
