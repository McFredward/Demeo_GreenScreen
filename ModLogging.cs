using System;

namespace GreenScreen
{
    internal enum ModLogLevel { Auto = -1, Error, Warning, Important, Debug }

    internal static class ModLogging
    {
#if DEMEO_DIAGNOSTIC_LOGS
        internal const ModLogLevel BuildDefault = ModLogLevel.Debug;
#else
        internal const ModLogLevel BuildDefault = ModLogLevel.Important;
#endif
        internal static ModLogLevel Level { get; private set; } = BuildDefault;
        internal static bool Enabled(ModLogLevel level) => level <= Level;
        internal static void Configure(ModLogLevel level) => Level = level == ModLogLevel.Auto ? BuildDefault : level;
        internal static ModLogLevel Parse(string value)
        {
            foreach (ModLogLevel level in new[] { ModLogLevel.Auto, ModLogLevel.Error,
                ModLogLevel.Warning, ModLogLevel.Important, ModLogLevel.Debug })
                if (string.Equals(value, level.ToString(), StringComparison.OrdinalIgnoreCase)) return level;
            return ModLogLevel.Auto;
        }
    }
}
