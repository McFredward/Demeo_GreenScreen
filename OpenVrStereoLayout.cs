#nullable disable
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GreenScreen
{
    // A real provider-level MultiPass transition. Never split a native two-view
    // pass with XRSystem.singlePassAllowed: ReconfigurePass needs native IDs.
    internal static class OpenVrStereoLayout
    {
        internal interface ISettings
        {
            bool Current { get; }
            ushort Mode { get; }
            bool ProviderSinglePass { get; }
            void Rebuild(ushort expected, ushort mode);
        }
        internal static Func<ISettings> SettingsFactory = OpenVrNativeSettings.Open;
        private static XRDisplaySubsystem _display;
        private static ISettings _settings;
        private static ushort _original;
        private static bool _changed, _ready, _resumeNeeded;
        private static float _deadline;
        internal static string Failure { get; private set; }
        internal static bool Owned => _settings != null;

        internal static bool Ensure(out string reason)
        {
            reason = Failure;
            if (Failure != null) return false;
            try
            {
                XRDisplaySubsystem display = SrpOutput.ActiveDisplay;
                if (display == null || !display.running)
                {
                    if (_settings != null && Time.unscaledTime >= _deadline)
                        throw new InvalidOperationException("The OpenVR display did not become ready.");
                    reason = "Waiting for the OpenVR display."; return false;
                }
                if (_settings != null && (!ReferenceEquals(display, _display) || !_settings.Current))
                    throw new InvalidOperationException("The OpenVR display changed during stereo ownership.");
                if (_settings == null)
                {
                    _settings = SettingsFactory(); _display = display;
                    _original = _settings.Mode;
                    if (_original > 1) throw new NotSupportedException("Unknown OpenVR stereo mode.");
                    _deadline = Time.unscaledTime + 5f;
                    if (_original != 0)
                    {
                        Switch(_original, 0);
                        Utils.LogInfo("OpenVR provider stereo changed to MultiPass. Waiting for two native single-view 2D passes; URP's pass policy is retained.");
                    }
                }
                if (_settings.Mode != 0) throw new InvalidOperationException("Another owner changed the OpenVR stereo setting.");
                if (!_settings.ProviderSinglePass && SeparateViews(_display))
                {
                    if (!_ready) Utils.LogInfo("OpenVR native MultiPass verified: two native passes, one view per pass, separate 2D eye targets. Resolution, MSAA and game camera flow are retained.");
                    _ready = true; _deadline = Time.unscaledTime + 5f;
                    reason = null; return true;
                }
                _ready = false;
                if (Time.unscaledTime >= _deadline)
                    throw new InvalidOperationException("OpenVR did not retain two native single-view 2D passes.");
                reason = "Waiting for OpenVR's separate eye targets; native coverage remains disabled.";
                return false;
            }
            catch (Exception error)
            {
                Release();
                Failure = reason = "OpenVR native MultiPass unavailable: " + error.Message;
                return false;
            }
        }

        internal static bool SeparateViews(XRDisplaySubsystem display)
        {
            if (display.GetRenderPassCount() != 2) return false;
            for (int index = 0; index < 2; index++)
            {
                display.GetRenderPass(index, out XRDisplaySubsystem.XRRenderPass pass);
                var descriptor = pass.renderTargetDesc;
                if (pass.GetRenderParameterCount() != 1 || descriptor.dimension != TextureDimension.Tex2D
                    || descriptor.volumeDepth != 1 || !NativePassthrough.ValidTarget(descriptor, false)) return false;
            }
            return true;
        }

        private static void Switch(ushort expected, ushort value, bool resume = false)
        {
            NativePassthrough.BeforeProviderStereoChange();
            bool running = _display.running;
            if (running) _resumeNeeded = true;
            try
            {
                if (running) _display.Stop();
                if (_display.running) throw new InvalidOperationException("OpenVR display did not stop before its eye-target rebuild.");
                _settings.Rebuild(expected, value);
                _changed = value != _original;
            }
            finally
            {
                // Retain the running original display even if preparation fails.
                // The input subsystem and the SteamVR/Frame processes are untouched.
                if (running || resume)
                {
                    _display.Start();
                    if (_display.running) _resumeNeeded = false;
                }
            }
            if ((running || resume) && !_display.running) throw new InvalidOperationException("OpenVR display could not resume.");
        }

        internal static void Release()
        {
            try
            {
                if (_changed && _settings != null && _settings.Current && _settings.Mode == 0)
                {
                    Switch(0, _original, _resumeNeeded);
                    Utils.LogInfo("OpenVR provider stereo restored to its original mode.");
                }
            }
            catch (Exception error) { Utils.LogInfo("OpenVR stereo restore failed: " + error.Message); }
            finally { _display = null; _settings = null; _changed = _ready = _resumeNeeded = false; }
        }

        internal static void Reset() { Release(); Failure = null; }
    }
}
