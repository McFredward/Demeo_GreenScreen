using GreenScreen;
#nullable disable

#if DEMEOBATTLEMARKED
using MelonLoader;
using System.Threading.Tasks;

[assembly: MelonInfo(
    typeof(BattlemarkedMod),
    "GreenScreen",
    "1.2",
    "McFredward",
    "https://github.com/McFredward/Demeo_GreenScreen")]
[assembly: MelonGame("Resolution Games", "Battlemarked")]
[assembly: MelonID("566783")]
#endif

namespace GreenScreen
{
    using HarmonyLib;
    using UnityEngine;
    using System.IO;
    using System;
    using System.Reflection;
    using System.Collections.Generic;

#if DEMEO
    using BepInEx;
    using Boardgame;    
    using BepInEx.Logging;

    [BepInPlugin("566784", "GreenScreen", "1.2")]
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
    using BepInEx;
    using Boardgame;    
    using BepInEx.Logging;

    [BepInPlugin("566784", "GreenScreen", "1.2")]
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
#elif DEMEOBATTLEMARKED
    using MelonLoader;
    using Il2CppJade.GameView.Avatar;
    using Il2CppJade.GameDefinitions;
    using Il2CppJade.GameView;
    using UnityEngine.SceneManagement;

    internal class BattlemarkedMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            HarmonyLib.Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            Melon<BattlemarkedMod>.Logger.Msg("Finished patching.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            MelonLogger.Msg($"Scene loaded: {sceneName}");
            MelonCoroutines.Start(RemoveEnvironment());
        }

        private static System.Collections.IEnumerator RemoveEnvironment()
        {
            yield return new WaitForSeconds(0.5f);
            var settings = Utils.LoadSettings();

            // Disable the room environment
            var roomBaked = GameObject.Find("BakeScale40x1/MainEnvironment/RoomBaked");
            if (roomBaked != null)
            {
                roomBaked.SetActive(false);
                MelonLogger.Msg("Disabled RoomBaked");
            }

            // Disable room lighting
            var lighting = GameObject.Find("BakeScale40x1/Lighting_Main_Hanna");
            if (lighting != null)
            {
                lighting.SetActive(false);
                MelonLogger.Msg("Disabled Lighting_Main_Hanna");
            }

            // Disable fog catcher
            var fogCatcher = GameObject.Find("BakeScale40x1/MainEnvironment/DistanceFogCatcher");
            if (fogCatcher != null)
            {
                fogCatcher.SetActive(false);
                MelonLogger.Msg("Disabled DistanceFogCatcher");
            }

            // Optionally disable the table mesh
            if (settings.DisableTable)
            {
                var tableMesh = GameObject.Find("BakeScale40x1/MainEnvironment/Table/SM_GameTableModern");
                if (tableMesh != null)
                {
                    tableMesh.SetActive(false);
                    MelonLogger.Msg("Disabled table mesh");
                }
            }

            // Set camera background to green (or custom color)
            Utils.ChangeCamBackground();
            MelonLogger.Msg("Changed camera background");
        }
    }

#endif


    // Harmony Patches
#if DEMEO || DEMEOBATTLES
    [HarmonyPatch(typeof(GameStartup), "InitializeGame", new Type[] { })]
    public static class InitializeGame_Prefix_Patch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            Utils.ChangeCamBackground();
            Utils.LogInfo("Changed Background");
        }
    }
#elif DEMEOBATTLEMARKED


#endif

    public static class Utils
    {
        public struct ModSettings
        {
            public Color BackgroundColor;
            public bool DisableTable;
        }

        public static void LogInfo(string message)
        {
#if DEMEOBATTLEMARKED
            Melon<BattlemarkedMod>.Logger.Msg(message);
#else
        BepInExPlugin.Log.LogInfo(message);
#endif
        }

        public static void ChangeCamBackground()
        {
            Camera[] camerasInScene = UnityEngine.GameObject.FindObjectsOfType<Camera>();
            UnityEngine.Color col = LoadSettings().BackgroundColor;
            foreach (Camera camera in camerasInScene)
            {
                camera.backgroundColor = col;
            }
        }

        public static ModSettings LoadSettings()
        {
            // Defaults
            ModSettings settings = new ModSettings
            {
                BackgroundColor = new Color(0f, 1f, 0f),
                DisableTable = true
            };

            string configDir = "BepInEx/plugins";
#if DEMEOBATTLEMARKED
            configDir = "Mods";
#endif

            // Try new name first, fall back to old name for backwards compatibility
            string filePath = Path.Combine(configDir, "greenscreen.ini");
            if (!File.Exists(filePath))
            {
                filePath = Path.Combine(configDir, "custom_color.ini");
            }

            if (!File.Exists(filePath))
            {
                return settings;
            }

            try
            {
                string[] lines = File.ReadAllLines(filePath);
                int r = -1, g = -1, b = -1;
                string currentSection = "";

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    // Skip empty lines and comments
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#") || trimmedLine.StartsWith(";"))
                        continue;

                    // Check for section header
                    if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                    {
                        currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2).ToUpper();
                        continue;
                    }

                    // Parse key=value
                    int equalsIndex = trimmedLine.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string key = trimmedLine.Substring(0, equalsIndex).Trim().ToUpper();
                    string value = trimmedLine.Substring(equalsIndex + 1).Trim();

                    // Handle based on section (or no section for backwards compatibility)
                    switch (currentSection)
                    {
                        case "COLOR":
                        case "": // Backwards compatibility - no section
                            if (key == "R") int.TryParse(value, out r);
                            else if (key == "G") int.TryParse(value, out g);
                            else if (key == "B") int.TryParse(value, out b);
                            break;

                        case "TABLE":
                            if (key == "DISABLE" || key == "DISABLETABLE")
                            {
                                settings.DisableTable = (value.ToLower() == "true" || value == "1");
                            }
                            break;

                        case "SETTINGS":
                            // For backwards compatibility, also check DisableTable here
                            if (key == "DISABLETABLE")
                            {
                                settings.DisableTable = (value.ToLower() == "true" || value == "1");
                            }
                            break;
                    }

                    // Backwards compatibility: DisableTable without section
                    if (currentSection == "" && key == "DISABLETABLE")
                    {
                        settings.DisableTable = (value.ToLower() == "true" || value == "1");
                    }
                }

                if (IsValidRGBValue(r) && IsValidRGBValue(g) && IsValidRGBValue(b))
                {
                    settings.BackgroundColor = new Color(r / 255f, g / 255f, b / 255f);
                    LogInfo($"Loaded custom color: RGB({r}, {g}, {b})");
                }

                if (settings.DisableTable)
                {
                    LogInfo("Table mesh will be disabled");
                }
            }
            catch (Exception ex)
            {
                LogInfo("Error reading config file: " + ex.Message);
            }

            return settings;
        }

        static bool IsValidRGBValue(int value)
        {
            return value >= 0 && value <= 255;
        }
    }
}