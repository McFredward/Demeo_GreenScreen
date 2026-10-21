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

        int before = CVRChaperone.Resets;
        OpenVR.Chaperone.ResetZeroPose(ETrackingUniverseOrigin.TrackingUniverseStanding);
        OpenVR.Chaperone.ResetZeroPose(ETrackingUniverseOrigin.TrackingUniverseSeated);
        Check(CVRChaperone.Resets == before + 2, "Direct/manual Standing and Seated resets remain available");
        Check(Harmony.GetPatchInfo(typeof(CVRChaperone).GetMethod("ResetZeroPose")) == null,
            "The shared OpenVR reset API is never patched");
        before = CVRSystem.Reads;
        ConfigurationStore.Current.SteamLinkNativePassthrough = false;
        Construct(true, "INI opt-out preserves game reset");
        Check(CVRSystem.Reads == before, "Opt-out performs no OpenVR identity query");
        ConfigurationStore.Current.SteamLinkNativePassthrough = true;
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
        DemeoAutomaticRecenter.Shutdown();
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
        internal static void LogInfo(string text) { Messages.Add(text); Console.WriteLine(text); }
    }
    internal static class ConfigurationStore { internal static readonly Settings Current = new Settings(); }
    internal sealed class Settings { internal bool SteamLinkNativePassthrough = true; }
}
namespace UnityEngine
{
    public enum RuntimePlatform { WindowsPlayer, LinuxPlayer }
    public static class Application { public static RuntimePlatform platform = RuntimePlatform.WindowsPlayer; }
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
