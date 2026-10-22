// Exercise real BepInEx 5/6 ConfigFile, binding, serialization and reload.
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using GreenScreen;
using UnityEngine;

internal static class ConfigChecks
{
    private static int checks;
    private static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    public static void Main()
    {
        string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "demeo-config-" + Guid.NewGuid().ToString("N"));
        string legacy = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(ConfigChecks).Assembly.Location), "greenscreen.ini");
        Directory.CreateDirectory(temp);
        try
        {
            string path = System.IO.Path.Combine(temp, "DemeoMixedReality.cfg");
            var config = BepInExConfiguration.Open(temp);
            var backend = new BepInExConfiguration(config);
            var defaults = backend.ReadIfChanged();
            Check(File.Exists(path) && config.Count == 7, "Real ConfigFile creates the seven normal entries.");
            Check(backend.Path == path && !File.Exists(System.IO.Path.Combine(temp, "566784.cfg")),
                "New installs use the readable filename without creating a numeric config.");
            Check(defaults.BackgroundColor.g == 1 && defaults.BackgroundColor.r == 0 && defaults.BackgroundColor.b == 0
                && defaults.DisableTable && defaults.SteamLinkNativePassthrough, "Requested new-install defaults are persisted.");
            Check(backend.ReadIfChanged() == null, "Unchanged config is not reread/logged per tick.");
            config.Bind("TABLE", "Disable", true).Value = false; // Explicit existing choice must survive the new default.
            File.WriteAllText(legacy, "[COLOR]\nR=12\nG=34\nB=56\n[TABLE]\nDisable=true\n[STEAMLINK]\nNativePassthrough=false\nAccountMaskBandwidth=false\n[LOGGING]\nLevel=Debug\n");
            var existing = new BepInExConfiguration(new ConfigFile(path, false));
            Check(!existing.ReadIfChanged().DisableTable, "Existing normal config wins over legacy values.");
            string importedPath = System.IO.Path.Combine(temp, "imported.cfg");
            var importedFile = new ConfigFile(importedPath, false);
            var importedBackend = new BepInExConfiguration(importedFile);
            var imported = importedBackend.ReadIfChanged();
            Check(imported.BackgroundColor.r == 12 / 255f && imported.BackgroundColor.g == 34 / 255f && imported.BackgroundColor.b == 56 / 255f,
                "Legacy RGB is imported without byte rounding loss.");
            Check(imported.DisableTable && !imported.SteamLinkNativePassthrough && !imported.SteamLinkAccountMaskBandwidth
                && imported.LogLevel == ModLogLevel.Debug, "All legacy preferences are imported, including opt-out and logging.");
            Check(File.Exists(legacy) && Utils.Messages.FindAll(s => s.StartsWith("Imported legacy")).Count == 1, "Migration is reported once and preserves the INI.");
            File.WriteAllText(legacy, "[STEAMLINK]\nNativePassthrough=true\n");
            var restarted = new BepInExConfiguration(new ConfigFile(importedPath, false));
            Check(!restarted.ReadIfChanged().SteamLinkNativePassthrough, "Restart does not reimport changed legacy INI.");
            importedFile.Bind("STEAMLINK", "NativePassthrough", true).Value = true;
            Check(importedBackend.ReadIfChanged().SteamLinkNativePassthrough, "Normal BepInEx setting changes apply live.");
            string changed = File.ReadAllText(importedPath).Replace("NativePassthrough = true", "NativePassthrough = false");
            File.WriteAllText(importedPath, changed + "\n# changed\n");
            Check(!importedBackend.ReadIfChanged().SteamLinkNativePassthrough, "External config edits reload through real BepInEx.");
            BepInExConfiguration.Attach(importedFile);
            Check(ConfigurationStore.Refresh() && ConfigurationStore.ActivePath == importedPath
                && !ConfigurationStore.Current.SteamLinkNativePassthrough, "Shared store reads the real loader config backend.");
            Check(!ConfigurationStore.Refresh(), "Backend caches unchanged settings at the shared maintenance boundary.");

            string upgradeDirectory = System.IO.Path.Combine(temp, "upgrade");
            Directory.CreateDirectory(upgradeDirectory);
            string numericPath = System.IO.Path.Combine(upgradeDirectory, "566784.cfg");
            string namedPath = System.IO.Path.Combine(upgradeDirectory, "DemeoMixedReality.cfg");
            File.WriteAllText(numericPath, "[COLOR]\nR=21\nG=43\nB=65\n[TABLE]\nDisable=false\n[STEAMLINK]\nNativePassthrough=false\nAccountMaskBandwidth=false\n[LOGGING]\nLevel=Important\n[OTHER]\nPreserved=custom\n");
            var renamedFile = BepInExConfiguration.Open(upgradeDirectory);
            var renamed = new BepInExConfiguration(renamedFile).ReadIfChanged();
            Check(File.Exists(namedPath) && !File.Exists(numericPath), "Existing numeric config is renamed once.");
            Check(renamed.BackgroundColor.r == 21 / 255f && renamed.BackgroundColor.g == 43 / 255f
                && renamed.BackgroundColor.b == 65 / 255f && !renamed.DisableTable
                && !renamed.SteamLinkNativePassthrough && !renamed.SteamLinkAccountMaskBandwidth
                && renamed.LogLevel == ModLogLevel.Important, "Renaming preserves all settings and takes precedence over the legacy INI.");
            Check(renamedFile.Bind("OTHER", "Preserved", "default").Value == "custom", "Unrecognized settings survive the rename too.");
            File.WriteAllText(numericPath, "[STEAMLINK]\nNativePassthrough=true\n");
            var namedWins = new BepInExConfiguration(BepInExConfiguration.Open(upgradeDirectory));
            Check(!namedWins.ReadIfChanged().SteamLinkNativePassthrough && File.Exists(numericPath),
                "An existing named config wins; subsequent numeric files are not imported or deleted.");
            BepInExConfiguration.Attach(upgradeDirectory);
            Check(ConfigurationStore.Refresh() && ConfigurationStore.ActivePath == namedPath
                && !ConfigurationStore.Current.SteamLinkNativePassthrough, "Both loader entry points attach the readable config through the shared directory path.");
            File.Delete(legacy);
            string custom = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(legacy), "custom_color.ini");
            File.WriteAllText(custom, "R=1\nG=2\nB=3\nDisableTable=1\n");
            try
            {
                var customBackend = new BepInExConfiguration(new ConfigFile(System.IO.Path.Combine(temp, "custom.cfg"), false));
                var value = customBackend.ReadIfChanged();
                Check(value.BackgroundColor.b == 3 / 255f && value.DisableTable, "Older unsectioned custom_color.ini migrates too.");
            }
            finally { File.Delete(custom); }
            Console.WriteLine($"Real BepInEx config checks passed: {checks}; {typeof(ConfigFile).Assembly.GetName().Name}");
        }
        finally { File.Delete(legacy); Directory.Delete(temp, true); }
    }
}
namespace GreenScreen
{
    internal enum ModLogLevel { Auto, Debug, Important, Warning, Error }
    internal static class ModLogging
    {
        internal static ModLogLevel Level, BuildDefault = ModLogLevel.Debug;
        internal static void Configure(ModLogLevel value) => Level = value;
        internal static ModLogLevel Parse(string value) => Enum.TryParse(value, true, out ModLogLevel result) ? result : ModLogLevel.Auto;
    }
    internal static class Utils
    {
        internal static readonly List<string> Messages = new();
        internal static void LogInfo(string message) => Messages.Add(message);
        internal static void LogError(string message) => Messages.Add(message);
    }
}
namespace UnityEngine
{
    internal struct Color
    {
        internal float r, g, b, a;
        internal Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
}
