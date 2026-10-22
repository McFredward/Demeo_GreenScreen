using System;
using System.IO;
using UnityEngine;

namespace GreenScreen
{
    internal sealed class ModConfiguration
    {
        internal Color BackgroundColor = new Color(0f, 1f, 0f, 1f);
        internal bool DisableTable = true;
        internal bool SteamLinkNativePassthrough = true;
        internal bool SteamLinkAccountMaskBandwidth = true;
        internal ModLogLevel LogLevel = ModLogLevel.Auto;

        internal static ModConfiguration Parse(string[] lines)
        {
            var settings = new ModConfiguration();
            int r = -1, g = -1, b = -1;
            string section = "";
            foreach (string line in lines)
            {
                string text = line.Trim();
                if (text.Length == 0 || text.StartsWith("#") || text.StartsWith(";")) continue;
                if (text.StartsWith("[") && text.EndsWith("]"))
                {
                    section = text.Substring(1, text.Length - 2).Trim().ToUpperInvariant();
                    continue;
                }
                int equals = text.IndexOf('=');
                if (equals <= 0) continue;
                string key = text.Substring(0, equals).Trim().ToUpperInvariant();
                string value = text.Substring(equals + 1).Trim();
                if (section == "LOGGING" && key == "LEVEL") settings.LogLevel = ModLogging.Parse(value);
                if (section == "COLOR" || section == "")
                {
                    if (key == "R") r = Rgb(value);
                    else if (key == "G") g = Rgb(value);
                    else if (key == "B") b = Rgb(value);
                }
                if (((section == "TABLE") && (key == "DISABLE" || key == "DISABLETABLE"))
                    || ((section == "SETTINGS" || section == "") && key == "DISABLETABLE"))
                {
                    if (TryBoolean(value, out bool disable)) settings.DisableTable = disable;
                }
                if (section == "STEAMLINK" && key == "ACCOUNTMASKBANDWIDTH")
                    settings.SteamLinkAccountMaskBandwidth = TryBoolean(value, out bool budget) && budget;
                if (section == "STEAMLINK" && key == "NATIVEPASSTHROUGH")
                    settings.SteamLinkNativePassthrough = TryBoolean(value, out bool native) && native;
            }
            if (r >= 0 && g >= 0 && b >= 0)
                settings.BackgroundColor = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return settings;
        }

        private static int Rgb(string value) => int.TryParse(value, out int number)
            && number >= 0 && number <= 255 ? number : -1;

        private static bool TryBoolean(string value, out bool result)
        {
            if (value == "1") { result = true; return true; }
            if (value == "0") { result = false; return true; }
            return bool.TryParse(value, out result);
        }
    }

    internal static class ConfigurationStore
    {
        internal static ModConfiguration Current { get; private set; } = new ModConfiguration();
        internal static string ActivePath { get; private set; } = "";
        private static bool _loaded;
        private static string _path = "";
        private static DateTime _modified;
        private static long _length = -1;

        // Called only on startup and on the cold maintenance tick, never per eye.
        internal static bool Refresh()
        {
            try
            {
                string assemblyDirectory = Path.GetDirectoryName(typeof(ConfigurationStore).Assembly.Location);
#if MELONLOADER
                string legacyDirectory = MelonLoader.Utils.MelonEnvironment.ModsDirectory;
#else
                string legacyDirectory = BepInEx.Paths.PluginPath;
#endif
                if (string.IsNullOrEmpty(assemblyDirectory)) assemblyDirectory = legacyDirectory;
                string path = Resolve(assemblyDirectory, legacyDirectory);
                var file = path.Length == 0 ? null : new FileInfo(path);
                DateTime modified = file == null ? DateTime.MinValue : file.LastWriteTimeUtc;
                long length = file == null ? 0 : file.Length;
                if (_loaded && path == _path && modified == _modified && length == _length) return false;
                ModConfiguration next = file == null ? new ModConfiguration()
                    : ModConfiguration.Parse(File.ReadAllLines(path));
                Current = next;
                ModLogging.Configure(next.LogLevel);
                ActivePath = path.Length == 0 ? "" : Path.GetFullPath(path);
                _path = path; _modified = modified; _length = length;
                _loaded = true;
                Utils.LogInfo("Demeo Mixed Reality configuration: "
                    + (ActivePath.Length == 0 ? "no INI found; expected " + Path.Combine(assemblyDirectory, "greenscreen.ini") : ActivePath)
                    + "; LogLevel=" + ModLogging.Level + "; Steam Link NativePassthrough=" + next.SteamLinkNativePassthrough + "."
                    + (next.SteamLinkNativePassthrough ? " Enabled only after Frame/Steam Link identity is confirmed."
                        : " Virtual Desktop colour-key mode; Steam Link native passthrough is disabled. Set [STEAMLINK] NativePassthrough=true in this INI to enable it."));
                return true;
            }
            catch (Exception error)
            {
                // A partially saved or unreadable INI must revoke opt-in.
                Current = new ModConfiguration { SteamLinkNativePassthrough = false };
                ActivePath = "";
                if (_length != -2) Utils.LogError("Could not read greenscreen.ini: " + error.Message);
                _length = -2;
                return true;
            }
        }

        internal static string Resolve(string assemblyDirectory, string legacyDirectory)
        {
            // Prefer the documented location beside the DLL, including subfolders.
            foreach (string name in new[] { "greenscreen.ini", "custom_color.ini" })
                foreach (string directory in new[] { assemblyDirectory, legacyDirectory })
                    if (!string.IsNullOrEmpty(directory))
                    {
                        string candidate = Path.Combine(directory, name);
                        if (File.Exists(candidate)) return candidate;
                    }
            return "";
        }
    }
}
