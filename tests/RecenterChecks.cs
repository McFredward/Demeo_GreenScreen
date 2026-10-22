// Owned game/OpenVR ports. Actual Harmony constructor dispatch and production
// identity classification; no Unity native runtime, device or game code.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using GreenScreen;
using HarmonyLib;
using Valve.VR;

internal static class RecenterChecks
{
    private static int checks;
    private static int observations;
    private static void Observe() { observations++; }
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    private static void Isolated(SteamLinkConnection.Snapshot snapshot)
    {
        // Publish the owned reader result at the production monitor boundary;
        // never launch an SDK helper or grant renderer/source permission here.
        object monitor = typeof(SteamLinkConnection).GetField("State", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        typeof(SteamLinkConnection.Monitor).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(monitor, snapshot);
    }
    private static void Construct(bool resetExpected, string why)
    {
        int before = CVRChaperone.Resets;
        var input = new Boardgame.SteamVR.SteamVRInput();
        Check(CVRChaperone.Resets == before + (resetExpected ? 1 : 0), why);
        Check(input.Initialized == 42 && input.Buttons.Count == 3 && input.AfterReset == 7,
            "Constructor field initializers and both sides of the reset execute normally: " + why);
    }

    public static void Main()
    {
        Construct(true, "Unpatched baseline automatically recenters");
        var constructor = typeof(Boardgame.SteamVR.SteamVRInput).GetConstructor(Type.EmptyTypes);
        var foreign = new Harmony("OwnedTest.OtherConstructorObserver");
        foreign.Patch(constructor, postfix: new HarmonyMethod(typeof(RecenterChecks).GetMethod(nameof(Observe), BindingFlags.NonPublic | BindingFlags.Static)));
        DemeoAutomaticRecenter.Initialize();
        Check(Utils.Messages.Exists(s => s.Contains("guard ready")), "Real constructor hook installed");
        Check(!SteamLinkConnection.Available, "Isolated reader has no result during the early startup test");
        Construct(false, "Verified Frame startup reset suppressed before cached identity or camera admission");
        Construct(false, "Reinitialization/lobby reset also suppressed");
        Check(observations == 2, "Another constructor observer runs exactly once per initialization");
        var movement = new Boardgame.CameraMoveAndScaleControl();
        movement.AutoTeleportAvatar();
        Check(movement.Moves == 0 && movement.Fades == 0, "Automatic game viewpoint cannot fade or move the avatar in native mode");
        var avatar = new Boardgame.AvatarController();
        avatar.Initialize();
        Check(UnityEngine.XR.InputTracking.Resets == 0 && avatar.Parents == 1 && avatar.Hands == 1 && avatar.Callbacks == 1,
            "Avatar setup retains parenting, hands and callbacks while avoiding its legacy automatic recenter");
        movement.Notify(new UnityEngine.XR.XRInputSubsystem());
        Check(movement.Placements == 0, "Automatic tracking notification cannot schedule a local-avatar reset");
        movement.Notify(null);
        Check(movement.Placements == 1 && UnityEngine.XR.InputTracking.Resets == 1, "Explicit controller/keyboard recenter remains available");
        movement.Notify(new UnityEngine.XR.XRInputSubsystem { subsystemDescriptor = { id = "OpenXR Input" } });
        Check(movement.Placements == 2, "Other runtime's tracking callback retains original behavior");
        UnityEngine.XR.InputTracking.Recenter();
        Check(UnityEngine.XR.InputTracking.Resets == 3, "Direct legacy recenter including bounds recovery is unchanged");
        Check(Harmony.GetPatchInfo(typeof(UnityEngine.XR.InputTracking).GetMethod("Recenter")) == null,
            "The shared legacy InputTracking API is never patched");

        // Real1.4.14 failure: the game emulates Quest2, while the isolated SDK
        // reader correctly identifies Frame. Do not admit Quest2 as a Frame.
        CVRSystem.Tracking = "oculus"; CVRSystem.Model = "Oculus Quest2";
        Construct(true, "Emulated Quest2 before independent confirmation is not Frame permission");
        movement.AutoTeleportAvatar();
        Check(movement.Moves == 1, "Pending/unverified identity retains automatic game movement");
        Isolated(SteamLinkConnection.Classify(true, 1, "cv", "Deckard MP", ""));
        Construct(false, "Independent confirmed Frame suppresses reset despite game Quest2 emulation");
        var verifiedRigInput = new UnityEngine.XR.XRInputSubsystem();
        Check(new Unity.XR.CoreUtils.XROrigin().Initialize(verifiedRigInput) && verifiedRigInput.Resets == 0,
            "Independent Frame confirmation also protects automatic rig initialization with game emulation");
        avatar.Initialize();
        Check(UnityEngine.XR.InputTracking.Resets == 3 && avatar.Parents == 2 && avatar.Hands == 2 && avatar.Callbacks == 2,
            "Independent Frame confirmation protects avatar reset and still runs all callbacks");
        avatar.ResetAvatarPositionRotationScaleTilt(2, true);
        Check(avatar.Placements == 1 && Utils.Messages.Exists(s => s.Contains("avatar placement retained; slot=2; offset=True")),
            "Other game placements execute and produce an event diagnostic");
        movement.AutoTeleportAvatar();
        Check(movement.Moves == 1, "Verified Frame prevents game-space automatic reframe despite emulation");
        SteamLinkConnectionLifecycle.UsesOpenVr = false;
        movement.AutoTeleportAvatar();
        avatar.Initialize();
        Check(movement.Moves == 2 && UnityEngine.XR.InputTracking.Resets == 4,
            "A separate OpenVR reader cannot suppress another active game transport");
        SteamLinkConnectionLifecycle.UsesOpenVr = true;
        ConfigurationStore.Current.SteamLinkNativePassthrough = false;
        movement.AutoTeleportAvatar();
        Check(movement.Moves == 3, "Opt-out restores automatic game viewpoint");
        ConfigurationStore.Current.SteamLinkNativePassthrough = true;
        Isolated(new SteamLinkConnection.Snapshot(SteamLinkConnection.Reason.Pending));
        movement.AutoTeleportAvatar();
        Check(movement.Moves == 4, "Expired/lost independent proof cannot authorize suppression with a Quest2 interface");
        CVRSystem.Frame();
        Isolated(SteamLinkConnection.Classify(true, 1, "oculus", "Oculus Quest2", ""));
        Construct(true, "A verified different independent headset takes priority over game Frame emulation");
        SteamLinkConnection.Reset();
        var rig = new Unity.XR.CoreUtils.XROrigin();
        var subsystem = new UnityEngine.XR.XRInputSubsystem();
        Check(rig.Initialize(subsystem) && subsystem.Resets == 0, "Device-mode rig initialization retains the current origin and reports success");
        Check(subsystem.ModeUpdates == 1 && rig.Offsets == 1 && rig.Completed == 1,
            "Rig mode selection, height offsets and completion still execute exactly once");
        Check(rig.Initialize(subsystem) && subsystem.Resets == 0 && rig.Completed == 2,
            "A second scene's rig initialization also avoids automatic recenter");
        Check(subsystem.TryRecenter() && subsystem.Resets == 1, "Direct/manual Unity subsystem recenter remains available");
        Check(Harmony.GetPatchInfo(typeof(UnityEngine.XR.XRInputSubsystem).GetMethod("TryRecenter")) == null,
            "The shared Unity recenter API is never patched");
        rig.DeviceMode = false;
        Check(rig.Initialize(subsystem) && subsystem.Resets == 1, "Floor-mode setup retains its original branch without a reset");
        rig.DeviceMode = true;
        var otherSubsystem = new UnityEngine.XR.XRInputSubsystem();
        otherSubsystem.subsystemDescriptor.id = "OpenXR Input";
        Check(rig.Initialize(otherSubsystem) && otherSubsystem.Resets == 1,
            "Another runtime's subsystem is not suppressed by a separately initialized OpenVR interface");

        int before = CVRChaperone.Resets;
        OpenVR.Chaperone.ResetZeroPose(ETrackingUniverseOrigin.TrackingUniverseStanding);
        OpenVR.Chaperone.ResetZeroPose(ETrackingUniverseOrigin.TrackingUniverseSeated);
        Check(CVRChaperone.Resets == before + 2, "Direct/manual Standing and Seated resets remain available");
        Check(Harmony.GetPatchInfo(typeof(CVRChaperone).GetMethod("ResetZeroPose")) == null,
            "The shared OpenVR reset API is never patched");
        before = CVRSystem.Reads;
        ConfigurationStore.Current.SteamLinkNativePassthrough = false;
        Construct(true, "INI opt-out preserves game reset");
        Check(rig.Initialize(subsystem) && subsystem.Resets == 2, "INI opt-out restores the original rig setup reset");
        Check(CVRSystem.Reads == before, "Opt-out performs no OpenVR identity query");
        ConfigurationStore.Current.SteamLinkNativePassthrough = true;
        CVRSystem.Connected = false;
        Check(rig.Initialize(subsystem) && subsystem.Resets == 3, "Unverified connection retains original rig recenter");
        CVRSystem.Frame();
        before = CVRSystem.Reads;
        UnityEngine.Application.platform = UnityEngine.RuntimePlatform.LinuxPlayer;
        Construct(true, "Unsupported platform retains game reset");
        Check(CVRSystem.Reads == before, "Unsupported platform performs no identity query");
        UnityEngine.Application.platform = UnityEngine.RuntimePlatform.WindowsPlayer;

        var failure = new InvalidOperationException("Owned native reset failure");
        CVRChaperone.Failure = failure;
        ConfigurationStore.Current.SteamLinkNativePassthrough = false;
        bool forwarded = false;
        try { new Boardgame.SteamVR.SteamVRInput(); }
        catch (InvalidOperationException e) { forwarded = ReferenceEquals(e, failure); }
        Check(forwarded, "Original reset failures are forwarded without reflection wrapping");
        ConfigurationStore.Current.SteamLinkNativePassthrough = true;
        Construct(false, "A suppressed call does not enter the original native reset");
        CVRChaperone.Failure = null;

        foreach (var profile in new[] {
            new[] { "vrlink", "Quest 3", "vrlink/frame" },
            new[] { "VirtualDesktop", "Deckard MP", "" },
            new[] { "cv", "Steam Frame", "" },
            new[] { "lighthouse", "Valve Index", "" },
            new[] { "cv", "Deckard MP", "VirtualDesktop/frame" } })
        {
            CVRSystem.Tracking = profile[0]; CVRSystem.Model = profile[1]; CVRSystem.Registered = profile[2];
            Construct(true, "Other headset or transport retains game reset: " + string.Join("/", profile));
        }
        CVRSystem.Frame();
        CVRSystem.Connected = false; Construct(true, "Disconnected device is not permission"); CVRSystem.Frame();
        CVRSystem.DeviceClass = ETrackedDeviceClass.Controller; Construct(true, "Controller cannot establish HMD identity"); CVRSystem.Frame();
        CVRSystem.FailProperty = true; Construct(true, "Property error retains game reset"); CVRSystem.Frame();
        CVRSystem.BadLength = true; Construct(true, "Truncated/malformed property retains game reset"); CVRSystem.Frame();
        CVRSystem.Model = new string('x', 97); Construct(true, "Oversized property is rejected by the shared classifier"); CVRSystem.Frame();
        CVRSystem.Model = "Deckard MP\n"; Construct(true, "Control characters cannot grant permission"); CVRSystem.Frame();
        CVRSystem.ChangeOnSecondRead = true; Construct(true, "Device change between snapshots retains game reset"); CVRSystem.Frame();
        CVRSystem.MissingOptional = true; Construct(false, "Absent optional properties match the observed Frame profile"); CVRSystem.Frame();
        CVRSystem.MissingOptional = CVRSystem.UnknownWithLength = true;
        Construct(false, "Unknown optional property length is ignored just like the isolated reader"); CVRSystem.Frame();
        CVRSystem.Tracking = "vrlink"; CVRSystem.Model = "Steam Frame"; CVRSystem.Registered = "vrlink/frame";
        Construct(false, "Canonical Frame/Steam Link identity suppresses only automatic reset"); CVRSystem.Frame();
        OpenVR.MissingSystem = true; Construct(true, "Missing initialized game interface retains game reset"); OpenVR.MissingSystem = false;
        Boardgame.SteamVR.SteamVRInput.HasXr = false;
        before = CVRSystem.Reads; Construct(false, "Original no-XR branch remains intact");
        Check(CVRSystem.Reads == before, "No-XR constructor does not call the guard");
        Boardgame.SteamVR.SteamVRInput.HasXr = true;

        // The transpiler must refuse unfamiliar or ambiguous game code instead
        // of silently broadening the suppression to another reset path.
        var rewrite = typeof(DemeoAutomaticRecenter).GetMethod("Transpile", BindingFlags.Static | BindingFlags.NonPublic);
        var reset = typeof(CVRChaperone).GetMethod("ResetZeroPose");
        foreach (var code in new[] {
            new List<CodeInstruction>(),
            new List<CodeInstruction> { new CodeInstruction(OpCodes.Ldc_I4_0), new CodeInstruction(OpCodes.Callvirt, reset) },
            new List<CodeInstruction> { new CodeInstruction(OpCodes.Ldc_I4_1), new CodeInstruction(OpCodes.Callvirt, reset),
                new CodeInstruction(OpCodes.Ldc_I4_1), new CodeInstruction(OpCodes.Callvirt, reset) } })
        {
            bool refused = false;
            try { rewrite.Invoke(null, new object[] { code }); }
            catch (TargetInvocationException e) when (e.InnerException is NotSupportedException) { refused = true; }
            Check(refused, "Missing, Seated or duplicate reset layout is refused");
        }
        var avatarRewrite = typeof(DemeoAutomaticRecenter).GetMethod("TranspileAvatarReset", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (var code in new[] {
            new List<CodeInstruction>(),
            new List<CodeInstruction> { new CodeInstruction(OpCodes.Call, typeof(UnityEngine.XR.InputTracking).GetMethod("Recenter")),
                new CodeInstruction(OpCodes.Call, typeof(UnityEngine.XR.InputTracking).GetMethod("Recenter")) } })
        {
            bool refused = false;
            try { avatarRewrite.Invoke(null, new object[] { code }); }
            catch (TargetInvocationException e) when (e.InnerException is NotSupportedException) { refused = true; }
            Check(refused, "Missing or ambiguous avatar legacy recenter layout is refused");
        }
        DemeoAutomaticRecenter.Shutdown();
        before = movement.Moves;
        movement.AutoTeleportAvatar();
        Check(movement.Moves == before + 1, "Unload restores game automatic viewpoint");
        foreach (var target in new[] {
            typeof(Boardgame.CameraMoveAndScaleControl).GetMethod("AutoTeleportAvatar"),
            typeof(Boardgame.CameraMoveAndScaleControl).GetMethod("RecenterDefault", BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(Boardgame.AvatarController).GetMethod("ResetAvatar", BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(Boardgame.AvatarController).GetMethod("ResetAvatarPositionRotationScaleTilt") })
            Check(Harmony.GetPatchInfo(target) == null || Harmony.GetPatchInfo(target).Owners.Count == 0,
                "Unload removes each owned game hook: " + target.Name);
        before = UnityEngine.XR.InputTracking.Resets;
        avatar.Initialize();
        Check(UnityEngine.XR.InputTracking.Resets == before + 1 && avatar.Parents == avatar.Callbacks,
            "Unload restores avatar native recenter and retains all initialization");
        Check(rig.Initialize(subsystem) && subsystem.Resets == 4, "Unload restores the real rig setup method");
        var rigPatch = Harmony.GetPatchInfo(typeof(Unity.XR.CoreUtils.XROrigin).GetMethod("SetupCamera", BindingFlags.NonPublic | BindingFlags.Instance));
        Check(rigPatch == null || rigPatch.Transpilers.Count == 0, "Unload removes the owned rig transpiler");
        var remaining = Harmony.GetPatchInfo(typeof(Boardgame.SteamVR.SteamVRInput).GetConstructor(Type.EmptyTypes));
        Check(remaining != null && remaining.Transpilers.Count == 0 && remaining.Postfixes.Count == 1,
            "Unload removes only the owned constructor hook");
        before = observations;
        Construct(true, "Unload restores the game's original automatic reset");
        Check(observations == before + 1, "Unload preserves the other owner's actual observer");
        DemeoAutomaticRecenter.Initialize();
        Construct(false, "Reload installs one functioning guard");
        DemeoAutomaticRecenter.Initialize();
        Construct(false, "Repeated initialization does not duplicate or skip constructor work");
        DemeoAutomaticRecenter.Shutdown();
        foreign.Unpatch(constructor, typeof(RecenterChecks).GetMethod(nameof(Observe), BindingFlags.NonPublic | BindingFlags.Static));
        Console.WriteLine("Automatic recenter checks passed: " + checks);
    }
}

namespace GreenScreen
{
    internal static class Utils
    {
        internal static readonly List<string> Messages = new List<string>();
         internal static void LogDebug(string m) => LogInfo(m); internal static void LogWarning(string m) => LogInfo(m); internal static void LogError(string m) => LogInfo(m); internal static void LogInfo(string text) { Messages.Add(text); Console.WriteLine(text); }
    }
    internal static class ConfigurationStore { internal static readonly Settings Current = new Settings(); }
    internal sealed class Settings { internal bool SteamLinkNativePassthrough = true; }
    internal static class SteamLinkConnectionLifecycle { internal static bool UsesOpenVr = true; }
}
namespace UnityEngine
{
    public enum RuntimePlatform { WindowsPlayer, LinuxPlayer }
    public static class Application { public static RuntimePlatform platform = RuntimePlatform.WindowsPlayer; }
}
namespace UnityEngine.XR
{
    public static class InputTracking
    {
        public static int Resets;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Recenter() { Resets++; }
    }
    public sealed class XRInputSubsystem
    {
        public int Resets, ModeUpdates;
        public readonly Descriptor subsystemDescriptor = new Descriptor();
        public sealed class Descriptor { public string id = "OpenVR Input"; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool TryRecenter() { Resets++; return true; }
        public void SetMode() { ModeUpdates++; }
    }
}
namespace Boardgame
{
    public sealed class CameraMoveAndScaleControl
    {
        public int Moves, Fades, Placements;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void AutoTeleportAvatar() { Fades++; Moves++; }
        public void Notify(UnityEngine.XR.XRInputSubsystem input) => RecenterDefault(input);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RecenterDefault(UnityEngine.XR.XRInputSubsystem subsystem = null)
        { Fades++; UnityEngine.XR.InputTracking.Recenter(); Placements++; }
    }
    public sealed class AvatarController
    {
        public int Parents, Hands, Callbacks, Placements;
        public void Initialize() => ResetAvatar();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ResetAvatar()
        { Parents++; UnityEngine.XR.InputTracking.Recenter(); Hands++; Callbacks++; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ResetAvatarPositionRotationScaleTilt(int slot, bool checkForOffset = false) { Placements++; }
    }
}
namespace Unity.XR.CoreUtils
{
    public sealed class XROrigin
    {
        public bool DeviceMode = true;
        public int Offsets, Completed;
        public bool Initialize(UnityEngine.XR.XRInputSubsystem input) => SetupCamera(input);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool SetupCamera(UnityEngine.XR.XRInputSubsystem input)
        {
            input.SetMode();
            Offsets++;
            bool ready = !DeviceMode || input.TryRecenter();
            Completed++;
            return ready;
        }
    }
}
namespace Boardgame.SteamVR
{
    public sealed class SteamVRInput
    {
        public static bool HasXr = true;
        public readonly HashSet<int> Buttons = new HashSet<int> { 1, 2, 3 };
        public int Initialized, AfterReset;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public SteamVRInput()
        {
            Initialized = 42;
            if (HasXr) OpenVR.Chaperone.ResetZeroPose(ETrackingUniverseOrigin.TrackingUniverseStanding);
            AfterReset = 7;
        }
    }
}
namespace Valve.VR
{
    public enum ETrackingUniverseOrigin { TrackingUniverseSeated, TrackingUniverseStanding }
    public enum ETrackedDeviceClass { Invalid, HMD, Controller }
    public enum ETrackedDeviceProperty { Tracking = 1000, Model = 1001, Registered = 1036 }
    public enum ETrackedPropertyError { Success, WrongDataType = 1, UnknownProperty = 4 }
    public static class OpenVR
    {
        private static readonly CVRSystem system = new CVRSystem();
        public static readonly CVRChaperone Chaperone = new CVRChaperone();
        public static bool MissingSystem;
        public static CVRSystem System => MissingSystem ? null : system;
    }
    public sealed class CVRChaperone
    {
        public static int Resets;
        public static Exception Failure;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ResetZeroPose(ETrackingUniverseOrigin origin) { if (Failure != null) throw Failure; Resets++; }
    }
    public sealed class CVRSystem
    {
        public static int Reads, SnapshotReads;
        public static string Tracking = "cv", Model = "Deckard MP", Registered = "";
        public static bool Connected = true, FailProperty, BadLength, ChangeOnSecondRead, MissingOptional, UnknownWithLength;
        public static ETrackedDeviceClass DeviceClass = ETrackedDeviceClass.HMD;
        public static void Frame()
        {
            Tracking = "cv"; Model = "Deckard MP"; Registered = ""; Connected = true; DeviceClass = ETrackedDeviceClass.HMD;
            SnapshotReads = 0; FailProperty = BadLength = ChangeOnSecondRead = MissingOptional = UnknownWithLength = false;
        }
        public bool IsTrackedDeviceConnected(uint device) { Reads++; SnapshotReads++; return device == 0 && Connected; }
        public ETrackedDeviceClass GetTrackedDeviceClass(uint device) => DeviceClass;
        public uint GetStringTrackedDeviceProperty(uint device, ETrackedDeviceProperty property, StringBuilder text, uint size, ref ETrackedPropertyError error)
        {
            error = ETrackedPropertyError.Success;
            if (FailProperty) { error = ETrackedPropertyError.WrongDataType; return 0; }
            if (MissingOptional && property == ETrackedDeviceProperty.Registered) { error = ETrackedPropertyError.UnknownProperty; return UnknownWithLength ? 1u : 0u; }
            string value = property == ETrackedDeviceProperty.Tracking ? Tracking : property == ETrackedDeviceProperty.Model ? Model : Registered;
            if (ChangeOnSecondRead && SnapshotReads >= 2 && property == ETrackedDeviceProperty.Model) value = "Quest 3";
            text.Append(value);
            return BadLength ? 1024 : (uint)text.Length + 1;
        }
    }
}
