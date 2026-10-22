#nullable disable
#if !MELONLOADER
using System;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace GreenScreen
{
    internal sealed class BepInExConfiguration : IConfigurationBackend
    {
        private readonly ConfigFile _config;
        private readonly ConfigEntry<int> _r, _g, _b;
        private readonly ConfigEntry<bool> _disableTable, _native, _budget;
        private readonly ConfigEntry<string> _logging;
        private DateTime _modified;
        private long _length = -1;
        private bool _dirty = true;
        public string Path => _config.ConfigFilePath;

        internal static void Attach(ConfigFile config) => ConfigurationStore.SetBackend(new BepInExConfiguration(config));

        internal BepInExConfiguration(ConfigFile config)
        {
            _config = config;
            bool created = !File.Exists(Path);
            string legacy = created ? ConfigurationStore.Resolve(System.IO.Path.GetDirectoryName(typeof(ConfigurationStore).Assembly.Location), BepInEx.Paths.PluginPath) : "";
            // Read before binding/saving: a failed migration must leave the old
            // file intact and must not create a misleading default config.
            ModConfiguration imported = legacy.Length == 0 ? null : ModConfiguration.Parse(File.ReadAllLines(legacy));
            bool save = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                var colour = new ConfigDescription("Colour-key background (0–255). Used without native passthrough.", new AcceptableValueRange<int>(0, 255));
                _r = config.Bind("COLOR", "R", 0, colour);
                _g = config.Bind("COLOR", "G", 255, colour);
                _b = config.Bind("COLOR", "B", 0, colour);
                _disableTable = config.Bind("TABLE", "Disable", false, "Hide the game table.");
                _native = config.Bind("STEAMLINK", "NativePassthrough", true, "Enable native passthrough when Steam Frame over Steam Link is detected.");
                _budget = config.Bind("STEAMLINK", "AccountMaskBandwidth", true, "Include lossless mask traffic in the video bandwidth budget.");
                _logging = config.Bind("LOGGING", "Level", "Auto", new ConfigDescription("Auto: dev diagnostics; main important messages and errors.",
                    new AcceptableValueList<string>("Auto", "Debug", "Important", "Warning", "Error")));
                if (created && imported != null)
                {
                    _r.Value = (int)Math.Round(imported.BackgroundColor.r * 255);
                    _g.Value = (int)Math.Round(imported.BackgroundColor.g * 255);
                    _b.Value = (int)Math.Round(imported.BackgroundColor.b * 255);
                    _disableTable.Value = imported.DisableTable;
                    _native.Value = imported.SteamLinkNativePassthrough;
                    _budget.Value = imported.SteamLinkAccountMaskBandwidth;
                    _logging.Value = imported.LogLevel.ToString();
                }
                if (created) config.Save();
            }
            finally { config.SaveOnConfigSet = save; }
            config.SettingChanged += (_, _) => _dirty = true;
            if (created && imported != null) Utils.LogInfo("Imported legacy configuration once from " + legacy + " into " + Path + ". The legacy file is preserved.");
        }

        public ModConfiguration ReadIfChanged()
        {
            var file = new FileInfo(Path);
            DateTime modified = file.Exists ? file.LastWriteTimeUtc : DateTime.MinValue;
            long length = file.Exists ? file.Length : 0;
            if (!_dirty && modified == _modified && length == _length) return null;
            if (_length >= 0 && file.Exists && (modified != _modified || length != _length)) _config.Reload();
            var result = new ModConfiguration
            {
                BackgroundColor = new Color(_r.Value / 255f, _g.Value / 255f, _b.Value / 255f, 1),
                DisableTable = _disableTable.Value,
                SteamLinkNativePassthrough = _native.Value,
                SteamLinkAccountMaskBandwidth = _budget.Value,
                LogLevel = ModLogging.Parse(_logging.Value)
            };
            _dirty = false; _modified = modified; _length = length;
            return result;
        }
    }
}
#endif
