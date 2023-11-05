using Mono.Cecil.Rocks;

namespace GreenScreen
{
    using Boardgame;
    using Boardgame.NonVR;
    using Boardgame.NonVR.Ui;
    using Boardgame.Ui.LobbyMenu;
    using HarmonyLib;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using System.IO;
    using System;
    using RGConfig;

    internal static class ModPatcher
    {

        internal static void Patch(Harmony harmony)
        {
            harmony.Patch(
            original: typeof(Lobby).GetMethod("Init"),
            prefix: new HarmonyMethod(typeof(ModPatcher), nameof(Init_Prefix)));
        }

        private static void ChangeCamBackground()
        {
            Camera[] camerasInScene = GameObject.FindObjectsOfType<Camera>();
            UnityEngine.Color col = LoadColorFromFile();
            foreach (Camera camera in camerasInScene)
            {
                camera.backgroundColor = col;
            }
        }

        private static Color LoadColorFromFile()
        {
            string filePath = Path.Combine("Mods", "custom_color.ini");

            if (File.Exists(filePath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(filePath);
                    int r = -1, g = -1, b = -1;

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("R="))
                        {
                            if (int.TryParse(line.Substring(2), out r))
                                continue;
                        }
                        if (line.StartsWith("G="))
                        {
                            if (int.TryParse(line.Substring(2), out g))
                                continue;
                        }
                        if (line.StartsWith("B="))
                        {
                            if (int.TryParse(line.Substring(2), out b))
                                continue;
                        }
                    }

                    if (IsValidRGBValue(r) && IsValidRGBValue(g) && IsValidRGBValue(b))
                    {
                        return new Color(r / 255f, g / 255f, b / 255f);
                    }
                    else
                    {
                        GreenScreenBase.LogDebug("Invalid RGB values in the file. Using default color (Green).");
                    }
                }
                catch (Exception ex)
                {
                    GreenScreenBase.LogDebug("Error reading the file: " + ex.Message);
                }
            }
            else
            {
                GreenScreenBase.LogDebug("Custom color file not found. Using default color (Green).");
            }

            return new Color(0f, 1f, 0f); // Default color
        }


        static bool IsValidRGBValue(int value)
    {
        return value >= 0 && value <= 255;
    }

    //Harmony Patches

    private static void Init_Prefix(
            GameContext gameContext,
            GameObject lobbyGUI,
            BehindBackLobbyGUI behindBackLobbyGUI,
            LobbyMenuController lobbyMenuController)
        {
            ChangeCamBackground();
            GreenScreenBase.LogDebug("Changed Background");
        }
    }
}
