using System;
using GreenScreen;
using UnityEngine;
using UnityEngine.XR;

internal static class OpenVrOriginChecks
{
    internal static void Run(Action<bool, string> check)
    {
#if DEMEO
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
            SteamLinkConnection.Frame(); input.running = false; Time.unscaledTime += 2; OpenVrOrigin.Tick(true);
            check(input.Listeners == 0, "stopped input subsystem relinquishes its origin subscription");
            input.running = true; Time.unscaledTime += 2; OpenVrOrigin.Tick(true);
            check(input.Listeners == 1, "restarted input subsystem is observed again");
            OpenVrOrigin.Tick(false); input.NotifyOrigin();
            check(input.Listeners == 0 && OpenVrOrigin.Revision == revision + 1, "opt-out removes observer without replaying old notifications");
        }
        finally
        {
            OpenVrOrigin.Reset(); SubsystemManager.Inputs.Clear();
            manager.activeLoader = previous; SteamLinkConnectionLifecycle.Reset(); SteamLinkCoverageBridge.Reset();
        }
#else
        OpenVrOrigin.Tick(true); OpenVrOrigin.Reset();
        check(true, "unvalidated IL2CPP origin callback remains inactive");
#endif
    }
}
