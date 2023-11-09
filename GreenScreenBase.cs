namespace GreenScreen
{
    using System;

    internal static class GreenScreenBase
    {
        internal const string ModId = "com.mcfredward.demeomods.greenscreen";
        internal const string ModName = "GreenScreen";
        internal const string ModVersion = "1.0.2";
        internal const string ModAuthor = "McFredward";

        private static Action<object>? _logDebug;

        internal static void LogDebug(object data) => _logDebug?.Invoke(data);

        internal static void Init(object loader)
        {
            #if BEPINEX
            if (loader is BepInExPlugin plugin)
            {
                if (plugin.Log == null)
                {
                    return;
                }

                _logDebug = plugin.Log.LogDebug;
            }
            #endif

            #if MELONLOADER
            if (loader is MelonLoaderMod mod)
            {
                _logDebug = mod.LoggerInstance.Msg;
            }
            #endif
        }
    }
}
