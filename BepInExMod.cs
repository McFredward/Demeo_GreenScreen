using GreenScreen;

using BepInEx;

namespace GreenScreen
{
    using HarmonyLib;
    using UnityEngine;
    using System.IO;
    using System;
    using System.Reflection;

    using Boardgame;
    using BepInEx.Logging;
    using System.Collections.Generic;

#if DEMEO
    [BepInPlugin("566784", "GreenScreen", "1.1")]
    internal class BepInExPlugin : BaseUnityPlugin { 
        internal static new ManualLogSource Log;
        private void Awake()
        {
            BepInExPlugin.Log = base.Logger;
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            Debug.Log("Finished patching.");
        }
    }
#elif DEMEOBATTLES
    using BepInEx.Unity.IL2CPP;

    [BepInPlugin("566784", "GreenScreen", "1.1")]
    internal class BepInExPlugin : BasePlugin
    {
        internal static new ManualLogSource Log;
        public override void Load()
        {
            BepInExPlugin.Log = base.Log;
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            BepInExPlugin.Log.LogInfo("Finished patching.");
        }
    }
#endif

[HarmonyPatch(typeof(GameStartup), "InitializeGame", new Type[] { })]
    public static class InitializeGame_Prefix_Patch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            Utils.ChangeCamBackground();
            BepInExPlugin.Log.LogInfo("Changed Background");
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
            string filePath = Path.Combine("BepInEx", "plugins", "custom_color.ini");

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
                        BepInExPlugin.Log.LogInfo("Invalid RGB values in the file. Using default color (Green).");
                    }
                }
                catch (Exception ex)
                {
                    BepInExPlugin.Log.LogInfo("Error reading the file: " + ex.Message);
                }
            }
            else
            {
                BepInExPlugin.Log.LogInfo("Custom color file not found. Using default color (Green).");
            }

            return new Color(0f, 1f, 0f); // Default color
        }


        static bool IsValidRGBValue(int value)
        {
            return value >= 0 && value <= 255;
        }
    }

}
