# Demeo_GreenScreen
Replaces the black background color of Demeo and Demeo Battles, enabling mixed reality in the PCVR version of those games through [VirtualDesktop](https://www.meta.com/de-de/experiences/1261331807302580/)'s passthrough mode.

<p align="center">
  <img src="demeo_green.jpg" width="50%" height="50%">
</p>

## How To Install & Use

<ins>**Demeo**</ins> <br>
* Download [MelonLoader](https://github.com/LavaGang/MelonLoader/releases) **version 0.5.7**, open the downloaded Installer and Select the Game's .exe `Demeo\Demeo.exe`. Press `Install`.
* Download `Demeo.zip` from [here](https://github.com/McFredward/Demeo_GreenScreen/releases) and unzip the file into `Demeo\Mods`. 

<ins>**Demeo Battles**</ins> <br>

* Download [MelonLoader](https://github.com/LavaGang/MelonLoader/releases) **version 0.6.1 or later**, open the downloaded Installer and Select the Game's .exe `Demeo Battles\DemeoBattles.exe`. Press `Install`.
* Download `Demeo_Battles.zip` from [here](https://github.com/McFredward/Demeo_GreenScreen/releases) and unzip the file into `Demeo Battles\Mods`.
  
<ins>**Demeo & Demeo Battles**</ins> <br>

* Start the game & turn off the basement environment in the setting ingame.
* Turn on "VR Passthrough" under the "Streaming"-Tab in the Virtual Desktop Menu (within VR).
* **USE THE FOLLOWING CONFIGURATION:**:

Red: 0, Green: 255, Blue: 0, Similarity: 37, Smoothness: 5
<p align="center">
  <img src="passthrough_configuration.jpg" width="25%" height="25%">
</p>

## How To Define Your Own Background Color
* Create the file `custom_color.ini` in your `Mods` Folder (where the .dll is located) and insert the following:

```ini
[CustomColor]
R=0
G=0
B=255
```

Feel free to adjust the values as desired, ensuring they stay within the range of 0 to 255 for each component. <br>
Additionally, please update the color in the VirtualDesktop configuration accordingly. <br>
If you discover a color or configuration that, in your opinion, performs better than the default green, let me know :)

## Known issues

* Visible outlines around some objects (Depends on your passthrough configuration in Virtual Desktop)
* You can look through some parts of some objects which also have the same color (e.g. the Swiftness Potion Card for the default green background).

## Credits & Libs

* Code inspired by "SkipIntro" from [DemeoMods by orendain](https://github.com/orendain/DemeoMods) using the MIT License
* [HarmonyLib](https://github.com/pardeike/Harmony) using the MIT License
* [MelonLoader](https://github.com/LavaGang/MelonLoader) using the Apache-2.0 license

If you have any suggestions, feel free to open an Issue. Or contact me (McFredward) in via Discord.

