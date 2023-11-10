using MelonLoader;
using GreenScreen;

[assembly: MelonInfo(
    typeof(MelonLoaderMod),
    "GreenScreen",
    "1.0.2",
    "McFredward",
    "https://github.com/McFredward/Demeo_GreenScreen")]
#if DEMEO
[assembly: MelonGame("Resolution Games", "Demeo")]
[assembly: MelonID("566783")]
[assembly: VerifyLoaderVersion("0.5.7", true)]
#elif DEMEOBATTLES
[assembly: MelonGame("Resolution Games", "Demeo Battles")]
[assembly: MelonID("566784")]
[assembly: VerifyLoaderVersion("0.6.1", true)]
#endif


namespace GreenScreen
{
    using MelonLoader;
    using HarmonyLib;
    using UnityEngine;
    using System.IO;
    using System;
    #if DEMEO
        using Boardgame;
    #elif DEMEOBATTLES
        using Il2Cpp;
        using Il2CppBoardgame;
        using Il2CppBoardgame.Ui.LobbyMenu;

#endif

    internal class MelonLoaderMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
        }
    }

    [HarmonyPatch(typeof(GameStartup), "InitializeGame", new Type[] { })]
    public static class InitializeGame_Prefix_Patch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            Utils.ChangeCamBackground();
            MelonLogger.Msg("Changed Background");
        }
    }

    public static class Utils
    {
        public static void ChangeCamBackground()
        {
            Camera[] camerasInScene = UnityEngine.GameObject.FindObjectsOfType<Camera>();
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
                        MelonLogger.Msg("Invalid RGB values in the file. Using default color (Green).");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg("Error reading the file: " + ex.Message);
                }
            }
            else
            {
                MelonLogger.Msg("Custom color file not found. Using default color (Green).");
            }

            return new Color(0f, 1f, 0f); // Default color
        }


        static bool IsValidRGBValue(int value)
        {
            return value >= 0 && value <= 255;
        }
    }

}
