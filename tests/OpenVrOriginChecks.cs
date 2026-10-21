using System;
using GreenScreen;
using UnityEngine;
using UnityEngine.XR;

internal static class OpenVrOriginChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var manager = UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager;
        var previous = manager.activeLoader;
        var input = new XRInputSubsystem();
        try
        {
            OpenVrOrigin.Reset();
            SubsystemManager.Inputs.Add(input);
            manager.activeLoader = new Unity.XR.OpenVR.OpenVRLoader();
            Time.unscaledTime += 2;
            SteamLinkConnectionLifecycle.Tick(true);
            SteamLinkConnection.Frame(); SteamLinkCoverageBridge.Grant();
            long revision = OpenVrOrigin.Revision;
            OpenVrOrigin.Tick(true); OpenVrOrigin.Tick(true);
            check(input.Listeners == 1, "origin observer subscribes once without changing tracking state");
            input.NotifyOrigin();
            check(OpenVrOrigin.Revision == revision + 1, "actual origin notification records a discontinuity under a live Frame grant");
            SteamLinkCoverageBridge.Reset(); input.NotifyOrigin();
            check(OpenVrOrigin.Revision == revision + 1, "origin changes without source authority never synthesize submit flags");
            SteamLinkCoverageBridge.Grant(); SteamLinkConnection.Reset(); input.NotifyOrigin();
            check(OpenVrOrigin.Revision == revision + 1, "unknown headset cannot authorize an origin discontinuity");
            manager.activeLoader = new UnityEngine.XR.OpenXR.OpenXRLoader();
            Time.unscaledTime += 2; SteamLinkConnectionLifecycle.Tick(true); SteamLinkConnection.Frame();
            input.NotifyOrigin();
            check(OpenVrOrigin.Revision == revision + 1, "OpenXR origin notification never changes OpenVR submit flags");
            manager.activeLoader = new Unity.XR.OpenVR.OpenVRLoader();
            Time.unscaledTime += 2; SteamLinkConnectionLifecycle.Tick(true);
            SteamLinkConnection.Frame(); input.running = false; Time.unscaledTime += 2; OpenVrOrigin.Tick(true);
            check(input.Listeners == 0, "stopped input subsystem relinquishes its origin subscription");
            input.running = true; Time.unscaledTime += 2; OpenVrOrigin.Tick(true);
            check(input.Listeners == 1, "restarted input subsystem is observed again");
            int foreignCalls = 0;
#if DEMEO
            Action<XRInputSubsystem> foreign = _ => foreignCalls++;
            input.trackingOriginUpdated += foreign;
#else
            var foreign = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<XRInputSubsystem>>(
                (Action<XRInputSubsystem>)(_ => foreignCalls++));
            input.add_trackingOriginUpdated(foreign);
#endif
            OpenVrOrigin.Tick(false); input.NotifyOrigin();
            check(input.Listeners == 1 && foreignCalls == 1 && OpenVrOrigin.Revision == revision + 1,
                "opt-out removes only our callback and preserves another owner without replaying old notifications");
#if DEMEO
            input.trackingOriginUpdated -= foreign;
#else
            input.remove_trackingOriginUpdated(foreign);
            Time.unscaledTime += 2; OpenVrOrigin.Tick(true);
            input.RetiredOriginCallback = true;
            OpenVrOrigin.Reset(); input.NotifyOrigin();
            check(OpenVrOrigin.Revision == revision + 1,
                "Retired native callback cannot throw during cleanup or replay origin evidence after reset");
#endif
        }
        finally
        {
            OpenVrOrigin.Reset(); SubsystemManager.Inputs.Clear();
            manager.activeLoader = previous; SteamLinkConnectionLifecycle.Reset(); SteamLinkCoverageBridge.Reset();
        }
    }
}
