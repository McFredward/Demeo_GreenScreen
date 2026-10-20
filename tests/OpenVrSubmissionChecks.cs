using System;
using GreenScreen;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

internal static class OpenVrSubmissionChecks
{
    private sealed class Backend : OpenVrSubmission.IBackend
    {
        internal int Refreshes, Grants, Stops;
        internal bool Ready = true, Valid = true;
        public bool Refresh() { Refreshes++; if (!Valid) throw new Exception("Changed owner"); return Ready; }
        public void Activate(SteamLinkCoverageBridge.Snapshot grant) { if (grant.CanAcceptOrdinaryRgba) Grants++; }
        public void Deactivate() { }
        public void Detach() { Stops++; }
        public string Statistics => "Fake 2D submission";
    }
    internal static void Run(Action<bool, string> check)
    {
        var descriptor = XRSettings.eyeTextureDesc;
        var factory = OpenVrSubmission.BackendFactory;
        var display = SrpOutput.ActiveDisplay;
        int starts = display.Starts, stops = display.Stops;
        bool policy = UnityEngine.Experimental.Rendering.XRSystem.singlePassAllowed;
        try
        {
            var array = descriptor; array.dimension = TextureDimension.Tex2DArray; array.volumeDepth = 2;
            XRSettings.eyeTextureDesc = array;
            var backend = new Backend(); int opens = 0;
            OpenVrSubmission.BackendFactory = () => { opens++; return backend; };
            check(OpenVrSubmission.Ensure(out _), "single-pass native submission adapter becomes ready");
            SteamLinkCoverageBridge.Grant(); OpenVrSubmission.Activate(SteamLinkCoverageBridge.Current);
            check(backend.Grants == 1, "only a live prepared source activates the 2D boundary");
            for (int i = 0; i < 10; i++) check(OpenVrSubmission.Ensure(out _), "stable array submission remains ready");
            check(opens == 1 && display.Starts == starts && display.Stops == stops
                && XRSettings.eyeTextureDesc.volumeDepth == 2 && UnityEngine.Experimental.Rendering.XRSystem.singlePassAllowed == policy,
                "array submission never restarts XR, retires targets or changes stereo policy");
            backend.Valid = false;
            check(!OpenVrSubmission.Ensure(out _) && OpenVrSubmission.Failure != null && backend.Stops == 1,
                "replaced native ownership detaches and refuses source permission");
            check(!OpenVrSubmission.Ensure(out _) && opens == 1, "permanent native failure does not retry every frame");
            OpenVrSubmission.Reset(); backend = new Backend { Ready = false };
            check(!OpenVrSubmission.Ensure(out _) && OpenVrSubmission.Failure == null, "uncreated native textures remain pending");
            backend.Ready = true;
            check(OpenVrSubmission.Ensure(out _), "current native targets complete preparation without a restart");
            var separate = descriptor; separate.volumeDepth = 1; separate.dimension = TextureDimension.Tex2D;
            XRSettings.eyeTextureDesc = separate;
            check(OpenVrSubmission.Ensure(out _) && backend.Stops == 1 && !OpenVrSubmission.Owned,
                "original native two-pass games need no array adapter");
        }
        finally { OpenVrSubmission.Reset(); OpenVrSubmission.BackendFactory = factory; XRSettings.eyeTextureDesc = descriptor; }
    }
}
