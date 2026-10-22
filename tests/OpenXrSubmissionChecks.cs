#if DEMEOBATTLEMARKED
using System;
using GreenScreen;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

internal static class OpenXrSubmissionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var pipeline=GraphicsSettings.currentRenderPipeline;
        var loader=UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.activeLoader;
        var descriptor=XRSettings.eyeTextureDesc;
        var display=SrpOutput.ActiveDisplay;
        int stops=display.Stops, starts=display.Starts;
        var settings=OpenXRSettings.Instance;
        try
        {
            GraphicsSettings.currentRenderPipeline=new UniversalRenderPipelineAsset();
            UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.activeLoader=new OpenXRLoader();
            OpenXRSettings.ApplyLayout=true;
            settings.renderMode=OpenXRSettings.RenderMode.SinglePassInstanced;
            Time.unscaledTime+=2;
            SteamLinkConnectionLifecycle.Tick(true); SrpOutput.Discover();
            SteamLinkConnection.Reset(); int writes=OpenXRSettings.Writes;
            check(!OpenXrSubmission.Ensure(out _) && OpenXRSettings.Writes==writes,"unconfirmed headset cannot change the OpenXR eye layout");
            SteamLinkConnection.Frame();
            SteamLinkCoverageBridge.Grant();
            check(!OpenXrSubmission.Ensure(out _) && settings.renderMode==OpenXRSettings.RenderMode.MultiPass,
                "Battlemarked selects the actual runtime MultiPass property before source admission");
            check(OpenXrSubmission.Ensure(out _) && display.GetRenderPassCount()==2,
                "only confirmed independent 2D OpenXR eyes can request source permission");
            check(display.Stops==stops && display.Starts==starts,"OpenXR eye preparation never stops or restarts XR");
            SteamLinkConnection.Invalidate();
            check(!OpenXrSubmission.Ensure(out _) && settings.renderMode==OpenXRSettings.RenderMode.MultiPass,
                "an identity refresh revokes permission without repeatedly rebuilding the eye layout");
            OpenXrSubmission.Reset();
            check(settings.renderMode==OpenXRSettings.RenderMode.SinglePassInstanced,"releasing the owned runtime setting restores the original mode");
            SteamLinkConnection.Frame();
            OpenXRSettings.ApplyLayout=false;
            check(!OpenXrSubmission.Ensure(out _) && !OpenXrSubmission.Ensure(out _),
                "property readback alone cannot admit an old array layout");
            OpenXrSubmission.Reset();
            OpenXRSettings.Reject=true;
            check(!OpenXrSubmission.Ensure(out _) && settings.renderMode==OpenXRSettings.RenderMode.SinglePassInstanced,
                "a rejected runtime mode remains opaque and retains the original setting");
            OpenXrSubmission.Reset(); OpenXRSettings.Reject=false; OpenXRSettings.ApplyLayout=true;
            OpenXrSubmission.Ensure(out _);
            settings.renderMode=OpenXRSettings.RenderMode.SinglePassInstanced;
            writes=OpenXRSettings.Writes;
            check(!OpenXrSubmission.Ensure(out _) && OpenXRSettings.Writes==writes,
                "an external eye-mode change is refused without fighting the game");
            OpenXrSubmission.Reset();
            check(OpenXRSettings.Writes==writes,"release preserves a setting changed by the game");
        }
        finally
        {
            OpenXrSubmission.Reset(); OpenXRSettings.ApplyLayout=false; OpenXRSettings.Reject=false;
            settings.renderMode=OpenXRSettings.RenderMode.MultiPass;
            GraphicsSettings.currentRenderPipeline=pipeline;
            UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.activeLoader=loader;
            XRSettings.eyeTextureDesc=descriptor;
            SteamLinkConnectionLifecycle.Reset();
        }
    }
}
#endif
