using System;
using GreenScreen;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

internal static class OpenVrStereoChecks
{
    // Model the SDK's actual contract: mode is cached at Start, Stop does not
    // retire targets, and only a rebuild creates new per-eye texture storage.
    private sealed class Provider : OpenVrStereoLayout.ISettings
    {
        internal readonly XRDisplaySubsystem Display = SrpOutput.ActiveDisplay;
        internal ushort Value = 1;
        internal bool Valid = true, CachedSingle = true, Created = true, Deferred, FailFirstStart, FailRebuild;
        internal int Rebuilds, Retirements;
        internal Provider() { Display.Started = Started; Complete(); }
        public bool Current => Valid;
        public ushort Mode => Value;
        public bool ProviderSinglePass => CachedSingle;
        public void Rebuild(ushort expected, ushort mode)
        {
            if (Display.running || SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba || Value != expected)
                throw new Exception("A live XR/source owner must be stopped and revoked before changing native stereo.");
            if (FailRebuild) throw new Exception("Injected rebuild failure.");
            Rebuilds++; if (Created) Retirements++;
            Created = false; Value = mode;
        }
        private void Started()
        {
            if (FailFirstStart) { FailFirstStart = false; throw new Exception("Injected first Start failure."); }
            if (!Deferred) Complete();
        }
        internal void Complete()
        {
            CachedSingle = Value == 1; Created = true;
            var descriptor = XRSettings.eyeTextureDesc;
            descriptor.volumeDepth = CachedSingle ? 2 : 1;
            descriptor.dimension = CachedSingle ? TextureDimension.Tex2DArray : TextureDimension.Tex2D;
            XRSettings.eyeTextureDesc = descriptor;
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        var oldFactory = OpenVrStereoLayout.SettingsFactory;
        var oldDescriptor = XRSettings.eyeTextureDesc;
        var display = SrpOutput.ActiveDisplay;
        Action oldStarted = display.Started;
        bool oldPolicy = UnityEngine.Experimental.Rendering.XRSystem.singlePassAllowed;
        try
        {
            var provider = new Provider();
            OpenVrStereoLayout.SettingsFactory = () => provider;
            int stops = display.Stops, starts = display.Starts;
            SteamLinkCoverageBridge.Grant();
            check(OpenVrStereoLayout.Ensure(out _), "provider transition reaches two actual single-view native passes");
            check(provider.Value == 0 && !provider.CachedSingle && provider.Rebuilds == 1 && provider.Retirements == 1,
                "native mode and cached target storage both change, with old array textures retired");
            check(display.Stops == stops + 1 && display.Starts == starts + 1 && display.running,
                "only the XR display stops and resumes once");
            check(!SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,
                "provider transition withdraws the old source grant before touching native settings");
            check(XRSettings.eyeTextureDesc.width == oldDescriptor.width && XRSettings.eyeTextureDesc.height == oldDescriptor.height
                && XRSettings.eyeTextureDesc.msaaSamples == oldDescriptor.msaaSamples
                && UnityEngine.Experimental.Rendering.XRSystem.singlePassAllowed == oldPolicy,
                "provider MultiPass retains resolution, MSAA and URP's native-pass policy");
            for (int i = 0; i < 12; i++) { Time.frameCount++; Time.unscaledTime += .1f; check(OpenVrStereoLayout.Ensure(out _), "stable provider remains ready"); }
            check(provider.Rebuilds == 1 && display.Stops == stops + 1, "normal frames never restart XR or rebuild targets");
            SteamLinkCoverageBridge.Grant();
            OpenVrStereoLayout.Reset();
            check(provider.Value == 1 && provider.CachedSingle && provider.Rebuilds == 2 && provider.Retirements == 2 && display.running,
                "opt-out restores the original native mode and rebuilds original eye targets");

            provider = new Provider { Deferred = true };
            OpenVrStereoLayout.SettingsFactory = () => provider;
            check(!OpenVrStereoLayout.Ensure(out _) && OpenVrStereoLayout.Failure == null && provider.Value == 0,
                "a stale two-view array after Start is waiting, never source permission");
            check(!OpenVrStereoLayout.SeparateViews(display), "the old one-native-pass/two-view layout is not a MultiPass result");
            provider.Complete();
            check(OpenVrStereoLayout.Ensure(out _), "fresh per-eye targets complete asynchronous provider startup");
            OpenVrStereoLayout.Reset();

            provider = new Provider { Deferred = true };
            OpenVrStereoLayout.SettingsFactory = () => provider;
            check(!OpenVrStereoLayout.Ensure(out _), "deferred provider begins a bounded transition");
            Time.unscaledTime += 5.1f;
            check(!OpenVrStereoLayout.Ensure(out _) && OpenVrStereoLayout.Failure != null && provider.Value == 1 && display.running,
                "a provider which never changes layout rolls back to a running original display");
            int failedRebuilds = provider.Rebuilds;
            check(!OpenVrStereoLayout.Ensure(out _) && provider.Rebuilds == failedRebuilds,
                "a permanent layout failure never causes repeated restart churn");
            OpenVrStereoLayout.Reset();

            provider = new Provider { FailFirstStart = true };
            OpenVrStereoLayout.SettingsFactory = () => provider;
            check(!OpenVrStereoLayout.Ensure(out _) && provider.Value == 1 && display.running,
                "failure to start the new layout restores and starts the original layout");
            OpenVrStereoLayout.Reset();

            provider = new Provider { FailRebuild = true };
            OpenVrStereoLayout.SettingsFactory = () => provider;
            check(!OpenVrStereoLayout.Ensure(out _) && provider.Value == 1 && display.running,
                "a failed target retirement retains native mode and resumes the display");
            OpenVrStereoLayout.Reset();

            provider = new Provider();
            OpenVrStereoLayout.SettingsFactory = () => provider;
            check(OpenVrStereoLayout.Ensure(out _), "ownership test acquires native MultiPass");
            provider.Value = 1; int beforeForeign = display.Stops;
            check(!OpenVrStereoLayout.Ensure(out _) && provider.Value == 1 && display.Stops == beforeForeign,
                "foreign stereo changes are preserved without another display restart");
            OpenVrStereoLayout.Reset();

            provider = new Provider();
            OpenVrStereoLayout.SettingsFactory = () => provider;
            check(OpenVrStereoLayout.Ensure(out _), "provider replacement test acquires native MultiPass");
            provider.Valid = false; beforeForeign = display.Stops;
            check(!OpenVrStereoLayout.Ensure(out _) && display.Stops == beforeForeign,
                "a replaced native context is never called through stale provider pointers");
            OpenVrStereoLayout.Reset();

            provider = new Provider { Value = 0 }; provider.Complete();
            OpenVrStereoLayout.SettingsFactory = () => provider;
            beforeForeign = display.Stops;
            check(OpenVrStereoLayout.Ensure(out _) && provider.Rebuilds == 0, "an already-native-MultiPass game requires no stereo writes");
            OpenVrStereoLayout.Reset();
            check(provider.Value == 0 && display.Stops == beforeForeign, "an original MultiPass layout remains owned by the game");
            provider = new Provider();
            OpenVrStereoLayout.SettingsFactory = () => provider;
            check(OpenVrStereoLayout.Ensure(out _), "external-stop test acquires native MultiPass");
            display.Stop(); int beforeExternal = display.Starts;
            OpenVrStereoLayout.Reset();
            check(provider.Value == 1 && !display.running && display.Starts == beforeExternal,
                "cleanup never restarts an XR display stopped by the game or user");
            display.Start();
            check(!OpenVrNativeSettings.Pin(null) && !OpenVrNativeSettings.Pin(new byte[OpenVrNativeSettings.FileBytes]),
                "unknown provider files never authorize native memory access");

            provider = new Provider();
            UnityEngine.Experimental.Rendering.XRSystem.singlePassAllowed = false;
            check(!OpenVrStereoLayout.SeparateViews(display),
                "negative control: splitting URP alone does not change the provider's one native array pass");
        }
        finally
        {
            OpenVrStereoLayout.Reset();
            OpenVrStereoLayout.SettingsFactory = oldFactory;
            display.Started = oldStarted;
            XRSettings.eyeTextureDesc = oldDescriptor;
            UnityEngine.Experimental.Rendering.XRSystem.singlePassAllowed = oldPolicy;
        }
    }
}
