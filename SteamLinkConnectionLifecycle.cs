#nullable disable
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace GreenScreen
{
    // Identity is independent of a scene camera. Device changes revoke the
    // cached identity immediately; the isolated reader also refreshes it cold.
    internal static class SteamLinkConnectionLifecycle
    {
        private static bool _watching, _hasHead;
        private static InputDevice _head;
        private static float _nextRuntimeCheck;
        private static bool _steamVrRuntime;
        private static bool _runtimeKnown;
        private static bool _openVrRuntime;
        internal static bool UsesOpenVr => _runtimeKnown && _openVrRuntime;
        internal static bool LegacyTransport => _runtimeKnown && !_steamVrRuntime;
        private static Type _generalSettings;
        private static string _runtimeStatus;
#if !DEMEO
        private static readonly Il2CppSystem.Action<InputDevice> ChangedCallback =
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<InputDevice>>(
                (Action<InputDevice>)Changed);
#endif
        internal static void Tick(bool requested)
        {
            bool running = GraphicsRunning();
            if (!running) { _runtimeKnown = false; _steamVrRuntime = _openVrRuntime = false; _nextRuntimeCheck = 0; }
            bool eligible = requested && running && Application.platform == RuntimePlatform.WindowsPlayer
                && SteamVrRuntime();
            if (!eligible) { Reset(); return; }
            if (!_watching)
            {
#if DEMEO
                InputDevices.deviceConnected += Changed;
                InputDevices.deviceDisconnected += Changed;
                InputDevices.deviceConfigChanged += Changed;
#else
                InputDevices.add_deviceConnected(ChangedCallback);
                InputDevices.add_deviceDisconnected(ChangedCallback);
                InputDevices.add_deviceConfigChanged(ChangedCallback);
#endif
                _head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                _hasHead = _head.isValid;
                _watching = true;
            }
            SteamLinkConnection.Tick(true, Time.unscaledTime);
        }

        private static bool GraphicsRunning() => UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
            ? SrpOutput.Running : XRSettings.enabled;

        // The helper must describe this game's transport. A separately running
        // SteamVR session is insufficient when the game uses another OpenXR runtime.
        private static bool SteamVrRuntime()
        {
            if (Time.unscaledTime < _nextRuntimeCheck) return _steamVrRuntime;
            _nextRuntimeCheck = Time.unscaledTime + 1f;
            _steamVrRuntime = false;
            _openVrRuntime = false;
            try
            {
                _generalSettings ??= FindType("UnityEngine.XR.Management.XRGeneralSettings");
                object settings = _generalSettings?.GetProperty("Instance")?.GetValue(null);
                object manager = settings?.GetType().GetProperty("Manager")?.GetValue(settings);
                var loader = manager?.GetType().GetProperty("activeLoader")?.GetValue(manager) as UnityEngine.Object;
                if (loader == null) return RuntimeResult("no active XR loader", false, false);
                if (NativePassthrough.RuntimeObject(loader, "Unity.XR.OpenVR.OpenVRLoader") != null)
                {
                    _openVrRuntime = true;
                    return RuntimeResult("OpenVR", true);
                }
                if (NativePassthrough.RuntimeObject(loader, "UnityEngine.XR.OpenXR.OpenXRLoader") == null)
                    return RuntimeResult("unsupported loader " + loader.GetType().FullName, false);
                object name = FindType("UnityEngine.XR.OpenXR.OpenXRRuntime")?.GetProperty("name")?.GetValue(null);
                return RuntimeResult("OpenXR runtime " + (name as string ?? "unavailable"),
                    string.Equals(name as string, "SteamVR/OpenXR", StringComparison.OrdinalIgnoreCase), name is string);
            }
            catch (Exception error) { return RuntimeResult("unavailable: " + error.GetType().Name, false, false); }
        }

        private static bool RuntimeResult(string status, bool supported, bool known = true)
        {
            _runtimeKnown = known;
            if (_runtimeStatus != status)
            {
                _runtimeStatus = status;
                Utils.LogDebug("Steam Link game XR transport: " + status + "; SteamVR=" + supported + ".");
            }
            return _steamVrRuntime = supported;
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name) ?? assembly.GetType("Il2Cpp" + name);
                if (type != null) return type;
            }
            return null;
        }

        private static void Changed(InputDevice device)
        {
            bool hmd = (device.characteristics & InputDeviceCharacteristics.HeadMounted) != 0;
            if (!hmd && (!_hasHead || !device.Equals(_head))) return;
            if (hmd) { _head = device; _hasHead = true; }
            SteamLinkConnection.Invalidate();
            SteamLinkCoverageBridge.Reset();
        }

        internal static void Reset()
        {
            if (_watching)
            {
#if DEMEO
                InputDevices.deviceConnected -= Changed;
                InputDevices.deviceDisconnected -= Changed;
                InputDevices.deviceConfigChanged -= Changed;
#else
                InputDevices.remove_deviceConnected(ChangedCallback);
                InputDevices.remove_deviceDisconnected(ChangedCallback);
                InputDevices.remove_deviceConfigChanged(ChangedCallback);
#endif
                SteamLinkConnection.Reset();
            }
            _watching = false; _hasHead = false; _head = default;
            SteamLinkConnection.Tick(false, Time.unscaledTime);
        }
    }
}
