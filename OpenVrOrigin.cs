#nullable disable
using System;
using System.Threading;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.XR;

namespace GreenScreen
{
    // Observe Unity's real origin notifications. Never recenter, consume OpenVR
    // events, move a camera or suppress an intentional game/user reset.
    internal static class OpenVrOrigin
    {
        private static long _revision;
        internal static long Revision => Interlocked.Read(ref _revision);
        internal static void Mark() => Interlocked.Increment(ref _revision);
        private static readonly List<XRInputSubsystem> Watched = new List<XRInputSubsystem>();
#if DEMEO
        private static readonly List<XRInputSubsystem> Current = new List<XRInputSubsystem>();
#else
        private static readonly Il2CppSystem.Collections.Generic.List<XRInputSubsystem> Current = new();
        private static readonly Il2CppSystem.Action<XRInputSubsystem> ChangedCallback =
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<XRInputSubsystem>>(
                (Action<XRInputSubsystem>)Changed);
#endif
        private static float _next;
        private static int _reports;

        internal static void Tick(bool requested)
        {
            if (!requested) { Reset(); return; }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            try
            {
                SubsystemManager.GetSubsystems(Current);
                for (int i = Watched.Count - 1; i >= 0; --i)
                    if (!Current.Contains(Watched[i]) || !Watched[i].running)
                    { Subscribe(Watched[i], false); Watched.RemoveAt(i); }
                foreach (var input in Current)
                    if (input.running && !Watched.Contains(input))
                    { Subscribe(input, true); Watched.Add(input); }
                Current.Clear();
            }
            catch (Exception)
            {
                // Origin evidence is optional. Failure must not disable a
                // valid eye image or create a synthetic discontinuity.
                Reset();
                _next = Time.unscaledTime + 5f;
            }
        }

        private static void Subscribe(XRInputSubsystem input, bool add)
        {
#if DEMEO
            if (add) input.trackingOriginUpdated += Changed;
            else input.trackingOriginUpdated -= Changed;
#else
            if (add) input.add_trackingOriginUpdated(ChangedCallback);
            else input.remove_trackingOriginUpdated(ChangedCallback);
#endif
        }

        private static void Changed(XRInputSubsystem input)
        {
            if (!Watched.Contains(input) || !input.running
                || !SteamLinkConnection.Available || !SteamLinkConnectionLifecycle.UsesOpenVr
                || !SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba
                || !ConfigurationStore.Current.SteamLinkNativePassthrough) return;
            Mark();
            if (_reports++ < 12)
                Utils.LogInfo("OpenVR tracking origin changed: notification=" + Revision
                    + "; Unity frame=" + Time.frameCount
                    + ". Marking two eye pairs as discontinuous for motion smoothing.");
        }

        internal static void Reset()
        {
            foreach (var input in Watched)
                try { Subscribe(input, false); }
                catch (Exception) { } // A retired native subsystem may already be disposed.
            Watched.Clear(); Current.Clear(); _next = 0;
            // Keep the monotonic revision across opt-out. A reactivated submit
            // establishes its own baseline rather than replaying old events.
        }
    }
}
