using GreenScreen;
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
#nullable disable

#if MELONLOADER
using MelonLoader;
[assembly: MelonInfo(typeof(GreenScreenMod), ModInfo.Name, ModInfo.Version, "McFredward",
    "https://github.com/McFredward/Demeo_GreenScreen")]
#if DEMEO
[assembly: MelonGame("Resolution Games", "Demeo")]
#elif DEMEOBATTLES
[assembly: MelonGame("Resolution Games", "Demeo Battles")]
#else
[assembly: MelonGame("Resolution Games", "Battlemarked")]
#endif
#endif

namespace GreenScreen
{
    internal static class ModInfo { internal const string Name = "Demeo Mixed Reality"; internal const string Version = "2.0.0"; }

#if MELONLOADER
    public sealed class GreenScreenMod : MelonMod
    {
        // MelonLoader applies this assembly's Harmony patches automatically.
        public override void OnInitializeMelon() => ModLifecycle.Initialize();
        public override void OnUpdate() => ModLifecycle.Update();
        public override void OnDeinitializeMelon() => ModLifecycle.Shutdown();
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            NativePassthrough.InvalidateCameras();
            ModLifecycle.InvalidateEnvironment();
        }
    }
#elif DEMEO
    [BepInEx.BepInPlugin("566784", ModInfo.Name, ModInfo.Version)]
    public sealed class BepInExPlugin : BepInEx.BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log;
        private void Awake()
        {
            Log = base.Logger;
            BepInExConfiguration.Attach(base.Config);
            ModLifecycle.Initialize();
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        }
        private void Update() => ModLifecycle.Update();
        private void OnDestroy() => ModLifecycle.Shutdown();
    }
#else
    [BepInEx.BepInPlugin("566784", ModInfo.Name, ModInfo.Version)]
    public sealed class BepInExPlugin : BepInEx.Unity.IL2CPP.BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        public override void Load()
        {
            Log = base.Log;
            BepInExConfiguration.Attach(base.Config);
            AddComponent<PassthroughDriver>();
            ModLifecycle.Initialize();
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        }
        public override bool Unload()
        {
            ModLifecycle.Shutdown();
            return true;
        }
    }
    public sealed class PassthroughDriver : MonoBehaviour
    {
        public PassthroughDriver(IntPtr pointer) : base(pointer) { }
        private void Update() => ModLifecycle.Update();
        private void OnDestroy() => ModLifecycle.Shutdown();
    }
#endif

    internal static class ModLifecycle
    {
        private static bool _running;
        private static bool _updateReported;
#if DEMEOBATTLEMARKED
        private static float _nextEnvironmentScan;
#endif
        internal static void Initialize()
        {
            if (_running) return;
            _running = true;
            _updateReported = false;
            NativePassthrough.Initialize();
            InvalidateEnvironment();
        }
        internal static void InvalidateEnvironment()
        {
#if DEMEOBATTLEMARKED
            _nextEnvironmentScan = Time.unscaledTime + .5f;
#endif
        }
        internal static void Update()
        {
            if (!_running) return;
            if (!_updateReported)
            {
                _updateReported = true;
                Utils.LogDebug("Demeo Mixed Reality Unity Update is active.");
            }
            NativePassthrough.Tick();
#if DEMEOBATTLEMARKED
            if (Time.unscaledTime < _nextEnvironmentScan) return;
            _nextEnvironmentScan = Time.unscaledTime + 1f;
            // Polling also catches additive/late-created environments on either loader.
            foreach (string path in new[] {
                "BakeScale40x1/MainEnvironment/RoomBaked",
                "BakeScale40x1/Lighting_Main_Hanna",
                "BakeScale40x1/MainEnvironment/DistanceFogCatcher" })
                Disable(path);
            if (ConfigurationStore.Current.DisableTable)
                Disable("BakeScale40x1/MainEnvironment/Table/SM_GameTableModern");
#endif
        }
#if DEMEOBATTLEMARKED
        private static void Disable(string path)
        {
            var item = GameObject.Find(path);
            if (item != null) item.SetActive(false);
        }
#endif
        internal static void Shutdown()
        {
            _running = false;
            NativePassthrough.Shutdown();
        }
    }

    // Harmony Patches
#if DEMEO || DEMEOBATTLES
    [HarmonyPatch]
    public static class InitializeGame_Prefix_Patch
    {
        private static MethodBase _initializeGame;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            Type startup = PassthroughFade.FindType("Assembly-CSharp", "Boardgame.GameStartup");
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
            Utils.LogDebug("Changed Background");
        }
    }
#endif

    public static class Utils
    {
        internal static bool DebugLogging => ModLogging.Enabled(ModLogLevel.Debug);
        public static void LogInfo(string message) => Write(ModLogLevel.Important, message);
        public static void LogDebug(string message) => Write(ModLogLevel.Debug, message);
        public static void LogWarning(string message) => Write(ModLogLevel.Warning, message);
        public static void LogError(string message) => Write(ModLogLevel.Error, message);
        private static void Write(ModLogLevel level, string message)
        {
            if (!ModLogging.Enabled(level)) return;
#if MELONLOADER
            if (level == ModLogLevel.Error) Melon<GreenScreenMod>.Logger.Error(message);
            else if (level == ModLogLevel.Warning) Melon<GreenScreenMod>.Logger.Warning(message);
            else Melon<GreenScreenMod>.Logger.Msg(message);
#else
            if (level == ModLogLevel.Error) BepInExPlugin.Log.LogError(message);
            else if (level == ModLogLevel.Warning) BepInExPlugin.Log.LogWarning(message);
            else BepInExPlugin.Log.LogInfo(message);
#endif
        }

        public static void ChangeCamBackground()
        {
            NativePassthrough.InvalidateCameras();
        }

        internal static ModConfiguration LoadSettings() => ConfigurationStore.Current;
    }
}
