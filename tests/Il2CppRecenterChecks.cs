// Owned method ports with the supplied interop signatures. Exercises real
// Harmony prefix dispatch, not execution or detouring of native IL2CPP code.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using GreenScreen;
using HarmonyLib;
using UnityEngine.XR;
#if DEMEOBATTLES
using Controller = Boardgame.CameraMoveAndScaleControl;
#else
using Controller = Jade.XRRecentering.XRRecentering;
#endif

internal static class Il2CppRecenterChecks
{
    private static int checks, observed;
    private static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    private static void ObserveOtherOwner() => observed++;
    private static void Identity(string tracking = "cv", string model = "Deckard MP", string registered = "")
    {
        object monitor = typeof(SteamLinkConnection).GetField("State", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        typeof(SteamLinkConnection.Monitor).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(monitor, SteamLinkConnection.Classify(true, 1, tracking, model, registered));
    }
    private static void Invoke(Controller controller, XRInputSubsystem input)
    {
#if DEMEOBATTLES
        controller.Notify(input);
#else
        controller.OnTrackingOriginUpdated(input);
#endif
    }

    public static void Main()
    {
        var controller = new Controller();
        var input = new XRInputSubsystem();
#if DEMEOBATTLES
        MethodInfo target = typeof(Controller).GetMethod("AutoTeleportAvatar");
#else
        MethodInfo target = typeof(Controller).GetMethod("OnTrackingOriginUpdated");
#endif
        var other = new Harmony("OwnedTest.OtherRecenterObserver");
        MethodInfo observer = typeof(Il2CppRecenterChecks).GetMethod(nameof(ObserveOtherOwner), BindingFlags.NonPublic | BindingFlags.Static);
        other.Patch(target, postfix: new HarmonyMethod(observer));
        DemeoAutomaticRecenter.Initialize();
        Check(Utils.Messages.Exists(s => s.Contains("ready:")), "Actual profile hooks installed");
        Check(Harmony.GetPatchInfo(target).Transpilers.Count == 0, "No generated-wrapper transpiler is installed");
        Invoke(controller, input);
        Check(controller.Calls == 1, "Unverified device retains original game behavior");
        Identity();
#if DEMEOBATTLES
        Invoke(controller, input);
        Check(controller.Calls == 1, "OpenVR automatic origin notification cannot schedule another local reset");
        controller.AutoTeleportAvatar();
        Check(controller.Moves == 0 && controller.Fades == 1, "Verified Frame automatic viewpoint cannot move/tween/fade");
        Check(observed == 1, "Another owner's postfix still runs for suppressed automatic viewpoint");
        Invoke(controller, null);
        Check(controller.Calls == 2, "Explicit controller/keyboard recenter retains original callback");
        Invoke(controller, new XRInputSubsystem { subsystemDescriptor = { id = "OpenXR Input" } });
        Check(controller.Calls == 3, "Other input transport is not suppressed");
        var avatar = new Boardgame.AvatarController();
        avatar.ResetAvatarPositionRotationScaleTilt(1, true);
        Check(avatar.Placements == 1 && Utils.Messages.Exists(s => s.Contains("placement retained; slot=1; offset=True")),
            "Game-owned scene/team/bounds placement runs and is observed");
        int before = InputTracking.Resets;
        avatar.ResetAvatar();
        Check(avatar.Setups == 1 && avatar.Callbacks == 1 && InputTracking.Resets==before,
            "Scoped native legacy reset is suppressed while every avatar setup/callback still runs");
        avatar.DuringReset=()=>controller.Notify(null);
        avatar.ResetAvatar();
        Check(InputTracking.Resets==before+1 && avatar.Callbacks==2,
            "Explicit manual recenter nested inside an avatar callback remains available");
        avatar.DuringReset=()=>throw new InvalidOperationException("owned setup failure");
        try { avatar.ResetAvatar(); } catch(InvalidOperationException) { }
        avatar.DuringReset=null;
        InputTracking.Before=()=>throw new InvalidOperationException("owned manual failure");
        avatar.DuringReset=()=>controller.Notify(null);
        try { avatar.ResetAvatar(); } catch(InvalidOperationException) { }
        InputTracking.Before=null;avatar.DuringReset=null;
        before=InputTracking.Resets;avatar.ResetAvatar();
        Check(InputTracking.Resets==before,"An exceptional manual request cannot leak manual permission into a later automatic reset");
        before=InputTracking.Resets;InputTracking.Recenter();
        Check(InputTracking.Resets==before+1,"An exception clears the automatic scope before any later manual call");
        avatar.DuringReset=()=>System.Threading.Tasks.Task.Run(()=>InputTracking.Recenter()).GetAwaiter().GetResult();
        before=InputTracking.Resets;avatar.ResetAvatar();
        Check(InputTracking.Resets==before+1,"Other native threads are never covered by the avatar call's scope");
        avatar.DuringReset=()=>{avatar.DuringReset=null;avatar.ResetAvatar();};
        before=InputTracking.Resets;int callbacks=avatar.Callbacks;avatar.ResetAvatar();
        Check(InputTracking.Resets==before && avatar.Callbacks==callbacks+2,"Nested automatic avatar resets balance their scopes and retain callbacks");
        before = InputTracking.Resets;
        InputTracking.Recenter();
        Check(InputTracking.Resets == before + 1, "Direct/manual legacy API remains available");
        foreach (var profile in new[] {
            new[] { "oculus", "Oculus Quest2", "" },
            new[] { "VirtualDesktop", "Deckard MP", "" },
            new[] { "lighthouse", "Valve Index", "" },
            new[] { "VirtualDesktop", "Steam Frame", "VirtualDesktop/frame" } })
        {
            Identity(profile[0], profile[1], profile[2]);
            before = controller.Moves; controller.AutoTeleportAvatar();
            Check(controller.Moves == before + 1, "Wrong headset/transport retains automatic movement");
            int resets=InputTracking.Resets;avatar.ResetAvatar();
            Check(InputTracking.Resets==resets+1,"An avatar on another headset/transport keeps its legacy tracking reset");
        }
        SteamLinkConnection.Reset();
        before = controller.Moves; controller.AutoTeleportAvatar();
        Check(controller.Moves == before + 1, "Revoked independent identity restores game behavior");
        Identity(); SteamLinkConnectionLifecycle.UsesOpenVr = false;
        before = controller.Moves; controller.AutoTeleportAvatar();
        Check(controller.Moves == before + 1, "A separate OpenVR reader cannot authorize a different game transport");
        int transportResets=InputTracking.Resets;avatar.ResetAvatar();
        Check(InputTracking.Resets==transportResets+1,"An OpenXR game cannot enter the OpenVR avatar suppression scope");
        SteamLinkConnectionLifecycle.UsesOpenVr = true;
        ConfigurationStore.Current.SteamLinkNativePassthrough = false;
        before = controller.Moves; controller.AutoTeleportAvatar();
        Check(controller.Moves == before + 1, "INI opt-out restores automatic viewpoint");
        int optOutResets=InputTracking.Resets;avatar.ResetAvatar();
        Check(InputTracking.Resets==optOutResets+1,"INI opt-out restores the original avatar tracking reset");
        ConfigurationStore.Current.SteamLinkNativePassthrough = true;
        UnityEngine.Application.platform = UnityEngine.RuntimePlatform.LinuxPlayer;
        before = controller.Moves; controller.AutoTeleportAvatar();
        Check(controller.Moves == before + 1, "Unsupported platform preserves game behavior");
        UnityEngine.Application.platform = UnityEngine.RuntimePlatform.WindowsPlayer;
        before = controller.Moves;
        for (int i = 0; i < 100; i++) controller.AutoTeleportAvatar();
        Check(controller.Moves == before && Utils.Messages.FindAll(s => s.StartsWith("Suppressed ")).Count == 64,
            "Suppression remains active after its bounded diagnostics budget");
        before = avatar.Placements;
        for (int i = 0; i < 100; i++) avatar.ResetAvatarPositionRotationScaleTilt(i, false);
        Check(avatar.Placements == before + 100 && Utils.Messages.FindAll(s => s.Contains("avatar placement retained")).Count == 32,
            "Diagnostic budget never blocks game placements");
#else
        Invoke(controller, input);
        Check(controller.Calls == 2 && controller.ListenerCalls == 2, "Frame user origin notification retains every game listener");
        Check(Utils.Messages.Exists(s => s.Contains("tracking-origin notification retained")), "Observed origin event is timestamped");
        Invoke(controller, null);
        Check(controller.Calls == 3, "A null/unknown subsystem does not suppress user or game callbacks");
        ConfigurationStore.Current.SteamLinkNativePassthrough = false;
        int before = Utils.Messages.Count;
        Invoke(controller, input);
        Check(controller.Calls == 4 && Utils.Messages.Count == before, "Opt-out stops diagnostics while preserving native callbacks");
        ConfigurationStore.Current.SteamLinkNativePassthrough = true;
        SteamLinkConnection.Reset(); before = Utils.Messages.Count;
        Invoke(controller, input);
        Check(controller.Calls == 5 && Utils.Messages.Count == before, "Lost identity is never permission to alter callbacks");
        Identity();
        for (int i = 0; i < 100; i++) Invoke(controller, input);
        Check(controller.Calls == 105 && controller.ListenerCalls == 105
            && Utils.Messages.FindAll(s => s.Contains("tracking-origin notification retained")).Count == 32,
            "Repeated origin notifications keep callbacks and bounded diagnostics");
#endif
#if DEMEOBATTLES
        Check(Harmony.GetPatchInfo(typeof(InputTracking).GetMethod("Recenter")).Prefixes.Count==1,
            "Exactly one native legacy API guard exists, forwarding outside the verified avatar scope");
#else
        Check(Harmony.GetPatchInfo(typeof(InputTracking).GetMethod("Recenter")) == null,
            "Battlemarked's shared legacy recenter API is unpatched");
#endif
        Check(Harmony.GetPatchInfo(typeof(XRInputSubsystem).GetMethod("TryRecenter")) == null,
            "Shared subsystem recenter API is unpatched");
#if DEMEOBATTLEMARKED
        CheckMovementObservers();
#endif
        DemeoAutomaticRecenter.Shutdown();
        Check(Harmony.GetPatchInfo(typeof(InputTracking).GetMethod("Recenter"))?.Prefixes.Count is null or 0,
            "Unload removes the scoped legacy guard");
        Check(Harmony.GetPatchInfo(target).Prefixes.Count == 0 && Harmony.GetPatchInfo(target).Postfixes.Count == 1,
            "Unload removes only owned prefix and retains another owner");
        int calls = controller.Calls;
        Invoke(controller, input);
        Check(controller.Calls == calls + 1, "Unload restores original callback dispatch");
        DemeoAutomaticRecenter.Initialize(); DemeoAutomaticRecenter.Initialize();
        Check(Harmony.GetPatchInfo(target).Prefixes.Count == 1, "Reload/repeated init installs exactly one owned prefix");
        DemeoAutomaticRecenter.Shutdown();
        other.Unpatch(target, observer);
        Console.WriteLine("IL2CPP recenter method-port checks passed: " + checks + "; " + typeof(Controller).FullName);
    }

#if DEMEOBATTLEMARKED
    private static void CheckMovementObservers()
    {
        typeof(Il2CppAutomaticRecenter).GetMethod("TryMovementObservers",BindingFlags.Static|BindingFlags.NonPublic)
            .Invoke(null,new object[]{typeof(Jade.GameView.CameraControlSystemVR),typeof(Jade.GameView.Avatar.AvatarVR)});
        var avatar=new Jade.GameView.Avatar.AvatarVR();
        var movement=new Jade.GameView.CameraControlSystemVR(avatar);
        movement.OnMenuSceneLoaded(null);
        Check(avatar.Calls==1 && movement.Calls==1,"Movement observers preserve menu setup and its original avatar placement");
        Check(Utils.Messages.Exists(s=>s.Contains("avatar placement: source=SetAvatarPositionByCameraWaypoint")),
            "The actual nested camera-waypoint source is recorded for an avatar placement");
        movement.Throw=true;
        try { movement.OnLevelLoaded(null,null); } catch(InvalidOperationException) { }
        avatar.SetAvatarPosition(default,default,1);
        Check(Utils.Messages[Utils.Messages.Count-1].Contains("source=direct/unknown"),
            "Placement source scopes are restored by finalizers after an original game exception");
        int messages=Utils.Messages.Count, calls=avatar.Calls;
        ConfigurationStore.Current.SteamLinkNativePassthrough=false;
        avatar.SetAvatarPosition(default,default,1);
        Check(Utils.Messages.Count==messages && avatar.Calls==calls+1,"Opt-out preserves placement without diagnostic output");
        ConfigurationStore.Current.SteamLinkNativePassthrough=true;
        calls=avatar.Calls;
        for(int i=0;i<100;i++)avatar.SetAvatarPosition(default,default,1);
        Check(avatar.Calls==calls+100 && Utils.Messages.FindAll(s=>s.Contains("Battlemarked avatar placement:")||s.Contains("Battlemarked camera event:")).Count==64,
            "Bounded important movement diagnostics never suppress original game or manual placements");
    }
#endif
}

namespace GreenScreen
{
    internal static class Utils { internal static readonly List<string> Messages = new();  internal static void LogDebug(string m) => LogInfo(m); internal static void LogWarning(string m) => LogInfo(m); internal static void LogError(string m) => LogInfo(m); internal static void LogInfo(string message) => Messages.Add(message); }
    internal static class ConfigurationStore { internal static readonly Settings Current = new(); }
    internal sealed class Settings { internal bool SteamLinkNativePassthrough = true; }
    internal static class SteamLinkConnectionLifecycle { internal static bool UsesOpenVr = true; }
}
namespace UnityEngine
{
    public struct Vector3 { public float x,y,z; }
    public enum RuntimePlatform { WindowsPlayer, LinuxPlayer }
    public static class Application { public static RuntimePlatform platform = RuntimePlatform.WindowsPlayer; }
}

#if DEMEOBATTLEMARKED
namespace Jade.GameView.Avatar
{
    public sealed class AvatarVR
    {
        public int Calls;
        [MethodImpl(MethodImplOptions.NoInlining)] public void SetAvatarPosition(UnityEngine.Vector3 position,UnityEngine.Vector3 rotation,float scale) { Calls++; }
    }
}
namespace Jade.GameView
{
    public sealed class CameraControlSystemVR
    {
        public readonly Avatar.AvatarVR Avatar; public int Calls; public bool Throw;
        public CameraControlSystemVR(Avatar.AvatarVR avatar) { Avatar=avatar; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnMenuSceneLoaded(object waypoint) {Calls++;SetAvatarPositionByCameraWaypoint(waypoint);}
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnLevelLoaded(object level,object step) {Calls++;SetAvatarPositionByCameraWaypoint(null);if(Throw)throw new InvalidOperationException();}
        [MethodImpl(MethodImplOptions.NoInlining)] public void SetAvatarPositionByCameraWaypoint(object waypoint) {Avatar.SetAvatarPosition(default,default,1);}
        [MethodImpl(MethodImplOptions.NoInlining)] public void SetAvatarPositionByCameraWaypointId(string id) {SetAvatarPositionByCameraWaypoint(id);}
    }
}
#endif
namespace UnityEngine.XR
{
    public class XRInputSubsystem
    {
        public readonly Descriptor subsystemDescriptor = new();
        public sealed class Descriptor { public string id = "OpenVR Input"; }
        [MethodImpl(MethodImplOptions.NoInlining)] public bool TryRecenter() => true;
    }
    public static class InputTracking
    {
        public static int Resets;public static Action Before;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Recenter() {Before?.Invoke();Resets++;}
    }
}
namespace Boardgame
{
    public class CameraMoveAndScaleControl
    {
        public int Calls, Moves, Fades;
        [MethodImpl(MethodImplOptions.NoInlining)] public void AutoTeleportAvatar() { Fades++; Moves++; }
        public void Notify(XRInputSubsystem input) => RecenterDefault(input);
        [MethodImpl(MethodImplOptions.NoInlining)] public void RecenterDefault(XRInputSubsystem subsystem = null) { Calls++; Fades++; InputTracking.Recenter(); }
    }
    public class AvatarController
    {
        public int Placements, Setups, Callbacks;
        public Action DuringReset;
        [MethodImpl(MethodImplOptions.NoInlining)] public void ResetAvatarPositionRotationScaleTilt(int slot, bool checkForOffset = false) => Placements++;
        [MethodImpl(MethodImplOptions.NoInlining)] public void ResetAvatar() { Setups++; DuringReset?.Invoke(); InputTracking.Recenter(); Callbacks++; }
    }
}
namespace Jade.XRRecentering
{
    public class XRRecentering
    {
        public int Calls, ListenerCalls;
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnTrackingOriginUpdated(XRInputSubsystem input) { Calls++; ListenerCalls++; }
    }
}
