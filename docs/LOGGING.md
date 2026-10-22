# Logging

Mod 2.0.0 adds four levels: Error, Warning, Important and Debug. Important
includes warnings/errors and brief configuration/native-mode transitions.
Debug also includes the existing eye/alpha counters, camera/fade discovery,
identity checks and automatic-recenter diagnostic details. These are our logs;
Unity, BepInEx/MelonLoader and SteamVR control their own logs independently.

The build embeds Debug on the dev branch and Important on main. This is resolved
by the actual MSBuild target for local source installation and by the package
builder for all six game/loader DLLs. Downloaded ZIPs retain their default without
Git installed on the gaming PC. build-info.json records the selected channel and
default. CI checks both defaults and an incremental dev/main/dev switch.

Existing INIs without a logging section use Auto. Explicit settings survive a
source installation like other existing INI options:

```ini
[LOGGING]
Level=Auto
```

Use Debug, Important, Warning or Error to override Auto. Case is ignored; unknown
and numeric values fall back to the build default. Config refresh runs on the
existing cold maintenance tick. Changing the level does not change rendering,
source proof, stereo submissions, bitrate, recenter or fade behavior. Shader
fallbacks and failures are retained at Warning/Error, not hidden as diagnostics.

The native PC/Frame companions have their own [logging controls](https://github.com/McFredward/steam-link-native-passthrough/blob/dev/docs/development/LOGGING.md).

The public mod name is Demeo Mixed Reality, version 2.0.0. Existing assembly
filenames, plugin GUID, namespaces, embedded resource IDs and greenscreen.ini
remain stable so source/ZIP upgrades replace the previous mod and retain user
settings rather than loading two copies. The GitHub repository URL is unchanged.
