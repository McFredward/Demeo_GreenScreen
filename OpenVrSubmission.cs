#nullable disable
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GreenScreen
{
    // Keep Unity/URP's native two-view pass. The OpenVR submit adapter copies
    // completed slices into separate 2D eyes under a live coverage grant.
    internal static class OpenVrSubmission
    {
        internal interface IBackend
        {
            bool Refresh();
            void Activate(SteamLinkCoverageBridge.Snapshot grant);
            void Deactivate();
            void Detach();
            string Statistics { get; }
        }
        internal static Func<IBackend> BackendFactory = OpenVrEyeSubmission.Open;
        private static IBackend _backend;
        private static float _nextReport;
        private static int _reports;
        internal static bool Owned => _backend != null;
        internal static string Failure { get; private set; }

        internal static bool Ensure(out string reason)
        {
            reason = Failure;
            if (reason != null) return false;
            try
            {
                var display = SrpOutput.ActiveDisplay;
                if (display == null || !display.running) { reason = "Waiting for the OpenVR display."; return false; }
                if (display.GetRenderPassCount() == 2)
                {
                    // A game originally using native MultiPass needs no array
                    // adapter. Never change its configured rendering mode.
                    for (int i = 0; i < 2; i++)
                    {
                        display.GetRenderPass(i, out var view);
                        if (view.GetRenderParameterCount() != 1 || view.renderTargetDesc.dimension != TextureDimension.Tex2D)
                            throw new NotSupportedException("Unknown OpenVR eye layout.");
                    }
                    Release(); return true;
                }
                if (display.GetRenderPassCount() != 1) throw new NotSupportedException("Unknown OpenVR pass count.");
                display.GetRenderPass(0, out var pass);
                if (pass.GetRenderParameterCount() != 2 || pass.renderTargetDesc.dimension != TextureDimension.Tex2DArray
                    || pass.renderTargetDesc.volumeDepth != 2)
                    throw new NotSupportedException("OpenVR single-pass output must contain two eye slices.");
                _backend ??= BackendFactory();
                if (!_backend.Refresh()) { reason = "Waiting for OpenVR's current eye textures."; return false; }
                if (_reports < 20 && Time.unscaledTime >= _nextReport)
                {
                    Utils.LogInfo(_backend.Statistics);
                    // Cover a long intro and the later menu without a
                    // permanent per-frame/per-second diagnostic stream.
                    _reports++; _nextReport = Time.unscaledTime + (_reports < 6 ? 5f : 30f);
                }
                reason = null; return true;
            }
            catch (Exception error)
            {
                Release(); Failure = reason = "OpenVR 2D eye submission unavailable: " + error.Message;
                return false;
            }
        }
        internal static void Activate(SteamLinkCoverageBridge.Snapshot grant) => _backend?.Activate(grant);
        internal static void Deactivate() => _backend?.Deactivate();
        private static void Release() { _backend?.Detach(); _backend = null; }
        internal static void Reset() { Release(); Failure = null; _reports = 0; _nextReport = 0; }
    }
}
