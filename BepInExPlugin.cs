#if BEPINEX
namespace GreenScreen
{
    using BepInEx;
    using BepInEx.Logging;
    using HarmonyLib;

    [BepInPlugin(GreenScreenBase.ModId, GreenScreenBase.ModName, GreenScreenBase.ModVersion)]
    internal class BepInExPlugin : BaseUnityPlugin
    {
        internal ManualLogSource? Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            GreenScreenBase.Init(this);

            var harmony = new Harmony(GreenScreenBase.ModId);
            ModPatcher.Patch(harmony);
        }
    }
}
#endif
