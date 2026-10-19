using GreenScreen;
#nullable disable

#if DEMEOBATTLEMARKED
using MelonLoader;

[assembly: MelonInfo(
    typeof(BattlemarkedMod),
    "GreenScreen",
    "1.3.0",
    "McFredward",
    "https://github.com/McFredward/Demeo_GreenScreen")]
[assembly: MelonGame("Resolution Games", "Battlemarked")]
[assembly: MelonID("566783")]
#endif

namespace GreenScreen
{
    using HarmonyLib;
    using UnityEngine;
    using System;
    using System.Reflection;

#if DEMEO
    using BepInEx;
    using BepInEx.Logging;

    [BepInPlugin("566784", "GreenScreen", "1.3.0")]
    internal class BepInExPlugin : BaseUnityPlugin { 
        internal static ManualLogSource Log;
        private void Awake()
        {
            BepInExPlugin.Log = base.Logger;
            NativePassthrough.Initialize();
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            Debug.Log("Finished patching.");
        }
        private void Update() => NativePassthrough.Tick();
        private void OnDestroy() => NativePassthrough.Shutdown();
    }
#elif DEMEOBATTLES
    using BepInEx.Unity.IL2CPP;
    using BepInEx;
    using BepInEx.Logging;

    [BepInPlugin("566784", "GreenScreen", "1.3.0")]
    internal class BepInExPlugin : BasePlugin
    {
        internal static new ManualLogSource Log;
        public override void Load()
        {
            BepInExPlugin.Log = base.Log;
            AddComponent<PassthroughDriver>();
            NativePassthrough.Initialize();
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            BepInExPlugin.Log.LogInfo("Finished patching.");
        }
        public override bool Unload()
        {
            NativePassthrough.Shutdown();
            return true;
        }
    }
    public sealed class PassthroughDriver : MonoBehaviour
    {
        public PassthroughDriver(IntPtr pointer) : base(pointer) { }
        private void Update() => NativePassthrough.Tick();
        private void OnDestroy() => NativePassthrough.Shutdown();
    }
#elif DEMEOBATTLEMARKED
    using MelonLoader;

    internal class BattlemarkedMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            NativePassthrough.Initialize();
            HarmonyLib.Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            Melon<BattlemarkedMod>.Logger.Msg("Finished patching.");
        }

        public override void OnUpdate() => NativePassthrough.Tick();
        public override void OnDeinitializeMelon() => NativePassthrough.Shutdown();

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            NativePassthrough.InvalidateCameras();
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

            // Refresh the configured Virtual Desktop / Steam Link background
            Utils.ChangeCamBackground();
            MelonLogger.Msg("Changed camera background");
        }
    }

#endif


    // Harmony Patches
#if DEMEO || DEMEOBATTLES
    [HarmonyPatch]
    public static class InitializeGame_Prefix_Patch
    {
        private static MethodBase _initializeGame;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            Type startup = AccessTools.TypeByName("Boardgame.GameStartup");
            _initializeGame = startup?.GetMethod("InitializeGame", BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            return _initializeGame != null;
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() => _initializeGame;

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
            NativePassthrough.InvalidateCameras();
        }

        internal static ModConfiguration LoadSettings() => ConfigurationStore.Current;
    }
}
