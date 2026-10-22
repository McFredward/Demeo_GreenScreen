#nullable disable
#if DEMEOBATTLEMARKED
using System;
using System.Reflection;
using UnityEngine.Rendering;

namespace GreenScreen
{
    // Battlemarked's instanced OpenXR array is resolved by SteamVR into
    // compositor-owned textures. Those cannot satisfy the game's PID-bound
    // source declaration. Ask Unity for independent eyes through its public
    // runtime setting, before requesting source authority. No XR restart or
    // compositor-process declaration is involved.
    internal static class OpenXrSubmission
    {
        private static RenderSettingLease _mode;
        private static object _settings;
        private static PropertyInfo _property;
        private static string _failure;
        private static bool _reported;

        // Called from Update only, after this game's SteamVR runtime and the
        // connected Frame/Steam Link headset have both been confirmed.
        internal static bool Ensure(out string reason)
        {
            if (!ConfigurationStore.Current.SteamLinkNativePassthrough || !SteamLinkConnection.Available
                || !SteamLinkConnectionLifecycle.UsesOpenXr)
            { reason = "Battlemarked OpenXR eye preparation requires confirmed Frame/Steam Link."; return false; }
            reason = _failure;
            if (reason != null) return false;
            try
            {
                if (_property == null)
                {
                    Type type = SteamLinkConnectionLifecycle.FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
                    _settings = type?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    _property = type?.GetProperty("renderMode", BindingFlags.Public | BindingFlags.Instance);
                    Type mode = _property?.PropertyType;
                    if (_settings == null || _property?.CanRead != true || !_property.CanWrite || mode?.IsEnum != true
                        || Enum.GetUnderlyingType(mode) != typeof(int)
                        || !Enum.IsDefined(mode, "MultiPass") || Convert.ToInt32(Enum.Parse(mode, "MultiPass")) != 0
                        || !Enum.IsDefined(mode, "SinglePassInstanced") || Convert.ToInt32(Enum.Parse(mode, "SinglePassInstanced")) != 1)
                        throw new NotSupportedException("The public OpenXR renderMode API has changed.");
                }
                int current = Convert.ToInt32(_property.GetValue(_settings));
                if (_mode != null && !_mode.Current)
                    throw new NotSupportedException("The game changed the owned OpenXR eye mode.");
                if (_mode == null && current != 0)
                {
                    if (current != 1 || !RenderSettingLease.TryAcquire(_settings, "renderMode", 0, out _mode))
                        throw new NotSupportedException("Unity did not accept independent OpenXR eyes.");
                    Utils.LogInfo("Battlemarked OpenXR: selected MultiPass through the runtime renderMode setting for Frame/Steam Link. Waiting for independent eye textures.");
                    // Never admit textures from the previous array layout in
                    // the frame in which the setting was changed.
                    reason = "Waiting for Battlemarked's independent OpenXR eye textures.";
                    return false;
                }
                var display = SrpOutput.ActiveDisplay;
                if (display == null || !display.running || display.GetRenderPassCount() != 2)
                { reason = "Waiting for Battlemarked's independent OpenXR eye textures."; return false; }
                for (int eye = 0; eye < 2; eye++)
                {
                    display.GetRenderPass(eye, out var pass);
                    if (pass.GetRenderParameterCount() != 1 || pass.renderTargetDesc.dimension != TextureDimension.Tex2D
                        || pass.renderTargetDesc.volumeDepth != 1)
                    { reason = "Battlemarked OpenXR has not supplied two independent 2D eyes."; return false; }
                }
                if (!_reported)
                {
                    _reported = true;
                    Utils.LogDebug("Battlemarked OpenXR: independent 2D eye passes confirmed; source permission still requires the game's exact PID and both companions.");
                }
                reason = null; return true;
            }
            catch (Exception error)
            {
                _failure = reason = "Battlemarked OpenXR eye layout unavailable: " + error.GetType().Name + ": " + error.Message;
                Utils.LogWarning(reason);
                return false;
            }
        }

        internal static void Reset()
        {
            _mode?.Dispose(); _mode = null;
            _settings = null; _property = null; _failure = null; _reported = false;
        }
    }
}
#endif
