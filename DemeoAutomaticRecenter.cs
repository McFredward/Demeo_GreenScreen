#nullable disable
using System;
#if DEMEO
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;
#endif

namespace GreenScreen
{
    // Guard verified automatic initialization calls in Demeo's input constructor
    // and XR rig, never the shared chaperone/subsystem API or user input.
    internal static class DemeoAutomaticRecenter
    {
#if DEMEO
        private static HarmonyLib.Harmony _patch;
        private static ConstructorInfo _constructor;
        private static MethodInfo _setup;
        private static HeadsetReader _headset;
        private static Action<object, int> _originalReset;
        private static bool _running, _failed;
        private static float _nextBind;
        private static int _suppressed, _readFailures;
        private static MethodInfo Guard => typeof(DemeoAutomaticRecenter).GetMethod(nameof(ResetUnlessNative), BindingFlags.Static | BindingFlags.NonPublic);
        private static MethodInfo Rewrite => typeof(DemeoAutomaticRecenter).GetMethod(nameof(Transpile), BindingFlags.Static | BindingFlags.NonPublic);
        private static MethodInfo RewriteSetup => typeof(DemeoAutomaticRecenter).GetMethod(nameof(TranspileSetup), BindingFlags.Static | BindingFlags.NonPublic);

        internal static void Initialize()
        {
            if (_patch != null || _failed) return;
            try
            {
                Type input = Assembly.Load("Assembly-CSharp").GetType("Boardgame.SteamVR.SteamVRInput");
                if (input == null) return;
                _constructor = input.GetConstructor(Type.EmptyTypes);
                if (_constructor == null || _constructor.GetMethodBody() == null)
                    throw new MissingMethodException("Demeo SteamVRInput constructor");
                Type origin = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if ((origin = assembly.GetType("Unity.XR.CoreUtils.XROrigin")) != null) break;
                origin ??= Assembly.Load("Unity.XR.CoreUtils").GetType("Unity.XR.CoreUtils.XROrigin");
                _setup = origin?.GetMethod("SetupCamera", BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(XRInputSubsystem) }, null);
                if (_setup == null || _setup.ReturnType != typeof(bool))
                    throw new MissingMethodException("XROrigin.SetupCamera(XRInputSubsystem)");
                _patch = new HarmonyLib.Harmony("McFredward.DemeoGreenScreen.AutomaticRecenter");
                _patch.Patch(_constructor, transpiler: new HarmonyMethod(Rewrite));
                _patch.Patch(_setup, transpiler: new HarmonyMethod(RewriteSetup));
                _running = true;
                Utils.LogInfo("Demeo automatic recenter guard ready: SteamVRInput constructor and XROrigin.SetupCamera. Manual recenter remains available.");
            }
            catch (Exception error)
            {
                Shutdown();
                _failed = true;
                Utils.LogInfo("Demeo automatic recenter guard unavailable; retaining game behavior: " + error.GetType().Name + ": " + error.Message);
            }
        }

        internal static void Tick(float now)
        {
            if (_patch != null || _failed || now < _nextBind) return;
            _nextBind = now + 1f;
            Initialize();
        }

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int match = -1, calls = 0;
            for (int i = 0; i < code.Count; i++)
            {
                // Refuse an unfamiliar exception layout instead of moving its boundaries.
                if (code[i].blocks.Count != 0) throw new NotSupportedException("Input constructor exception layout");
                if (code[i].operand is MethodInfo method && method.Name == "ResetZeroPose")
                {
                    calls++;
                    var parameters = method.GetParameters();
                    if (code[i].opcode == OpCodes.Callvirt && !method.IsStatic && !method.DeclaringType.IsValueType && method.ReturnType == typeof(void)
                        && method.DeclaringType.FullName == "Valve.VR.CVRChaperone" && parameters.Length == 1
                        && parameters[0].ParameterType.FullName == "Valve.VR.ETrackingUniverseOrigin"
                        && parameters[0].ParameterType.IsEnum && Enum.GetUnderlyingType(parameters[0].ParameterType) == typeof(int)
                        && i > 0 && code[i - 1].opcode == OpCodes.Ldc_I4_1)
                    {
                        match = i;
                        _headset = new HeadsetReader(method.DeclaringType.Assembly);
                    }
                }
            }
            if (calls != 1 || match < 0) throw new NotSupportedException("Expected exactly one constructor Standing ResetZeroPose call");

            // Keep the original arguments, labels and branches. The enum has a
            // verified Int32 representation; its receiver is a reference type.
            // Cache a typed native-wrapper call, preserving virtual dispatch and
            // exceptions without reflection Invoke on the original reset path.
            var original = (MethodInfo)code[match].operand;
            var forward = new DynamicMethod("DemeoOriginalStandingReset", typeof(void),
                new[] { typeof(object), typeof(int) }, typeof(DemeoAutomaticRecenter), true);
            ILGenerator il = forward.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, original.DeclaringType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, original);
            il.Emit(OpCodes.Ret);
            _originalReset = (Action<object, int>)forward.CreateDelegate(typeof(Action<object, int>));
            code[match] = new CodeInstruction(code[match]) { opcode = OpCodes.Call, operand = Guard };
            return code;
        }

        private static void ResetUnlessNative(object chaperone, int origin)
        {
            Action<object, int> original = _originalReset;
            if (origin != 1 || !Suppress("SteamVRInput constructor")) original(chaperone, origin);
        }

        private static IEnumerable<CodeInstruction> TranspileSetup(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            MethodInfo reset = typeof(XRInputSubsystem).GetMethod("TryRecenter", Type.EmptyTypes);
            int count = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Callvirt && Equals(code[i].operand, reset))
                {
                    count++;
                    code[i] = new CodeInstruction(code[i]) { opcode = OpCodes.Call,
                        operand = typeof(DemeoAutomaticRecenter).GetMethod(nameof(InitializeWithoutRecenter), BindingFlags.Static | BindingFlags.NonPublic) };
                }
            if (reset?.ReturnType != typeof(bool) || count != 1)
                throw new NotSupportedException("Expected one XROrigin.SetupCamera TryRecenter call");
            return code;
        }

        private static bool InitializeWithoutRecenter(XRInputSubsystem input)
        {
            // Preserve mode selection, offsets and callbacks. Success prevents
            // Unity from retrying initialization simply because we kept the origin.
            bool openvr = false;
            try { openvr = input != null && input.subsystemDescriptor?.id == "OpenVR Input"; }
            catch (Exception) { }
            if (openvr && Suppress("XROrigin.SetupCamera automatic initialization")) return true;
            return input.TryRecenter();
        }

        private static bool Suppress(string source)
        {
            if (!_running || !ConfigurationStore.Current.SteamLinkNativePassthrough
                || Application.platform != RuntimePlatform.WindowsPlayer || IntPtr.Size != 8) return false;
            try
            {
                // Startup can precede the isolated reader's first result. Read only
                // the game's already initialized OpenVR interface, without VR_Init,
                // loading native libraries, child processes or waiting for a grant.
                var headset = _headset.Read();
                if (!headset.Available)
                {
                    if (Interlocked.Increment(ref _readFailures) <= 3)
                        Utils.LogInfo("Demeo automatic recenter retained in " + source + "; headset=" + headset.State
                            + "; tracking=" + headset.TrackingSystem + "; model=" + headset.Model + ".");
                    return false;
                }
                int count = Interlocked.Increment(ref _suppressed);
                Utils.LogInfo("Suppressed Demeo automatic recenter in " + source + "; count=" + count
                    + "; at=" + DateTime.Now.ToString("HH:mm:ss.fff")
                    + "; Frame/Steam Link confirmed from the current game interface. Manual recenter is unchanged.");
                return true;
            }
            catch (Exception error)
            {
                if (Interlocked.Increment(ref _readFailures) <= 3)
                    Utils.LogInfo("Demeo automatic recenter: current headset could not be verified; retaining game reset: " + error.GetType().Name + ".");
                return false;
            }
        }

        private sealed class HeadsetReader
        {
            private readonly MethodInfo _system, _connected, _class, _property;
            private readonly Type _propertyEnum, _errorEnum;

            internal HeadsetReader(Assembly api)
            {
                Type openvr = api.GetType("Valve.VR.OpenVR", true), system = api.GetType("Valve.VR.CVRSystem", true);
                _propertyEnum = api.GetType("Valve.VR.ETrackedDeviceProperty", true);
                _errorEnum = api.GetType("Valve.VR.ETrackedPropertyError", true);
                _system = openvr.GetProperty("System", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
                _connected = system.GetMethod("IsTrackedDeviceConnected", new[] { typeof(uint) });
                _class = system.GetMethod("GetTrackedDeviceClass", new[] { typeof(uint) });
                _property = system.GetMethod("GetStringTrackedDeviceProperty", new[] { typeof(uint), _propertyEnum,
                    typeof(StringBuilder), typeof(uint), _errorEnum.MakeByRefType() });
                if (_system == null || _system.ReturnType != system || _connected?.ReturnType != typeof(bool)
                    || _class == null || !_class.ReturnType.IsEnum || _property?.ReturnType != typeof(uint)
                    || !_propertyEnum.IsEnum || !_errorEnum.IsEnum)
                    throw new NotSupportedException("Game OpenVR connected-headset property contract");
            }

            internal SteamLinkConnection.Snapshot Read()
            {
                object system = _system.Invoke(null, null);
                if (system == null) throw new InvalidOperationException("No current game OpenVR system");
                var first = Read(system);
                var second = Read(system);
                if (first.State != second.State || first.TrackingSystem != second.TrackingSystem || first.Model != second.Model
                    || first.RegisteredDeviceType != second.RegisteredDeviceType)
                    return new SteamLinkConnection.Snapshot(SteamLinkConnection.Reason.Changed);
                return first;
            }

            private SteamLinkConnection.Snapshot Read(object system)
            {
                if (!(bool)_connected.Invoke(system, new object[] { 0u })
                    || Convert.ToInt32(_class.Invoke(system, new object[] { 0u })) != 1)
                    return new SteamLinkConnection.Snapshot(SteamLinkConnection.Reason.NativeUnavailable);
                return SteamLinkConnection.Classify(true, 1, Property(system, 1000, true),
                    Property(system, 1001, false), Property(system, 1036, true));
            }

            private string Property(object system, int key, bool optional)
            {
                var text = new StringBuilder(128);
                object[] args = { 0u, Enum.ToObject(_propertyEnum, key), text, 128u, Enum.ToObject(_errorEnum, 0) };
                uint length = (uint)_property.Invoke(system, args);
                int error = Convert.ToInt32(args[4]);
                if (optional && error == 4) return ""; // TrackedProp_UnknownProperty, same rule as the isolated reader
                if (error != 0 || length > 128 || length != text.Length + 1u)
                    throw new InvalidOperationException("Invalid connected-headset property " + key);
                return text.ToString();
            }
        }

        internal static void Shutdown()
        {
            _running = false;
            if (_patch != null && _constructor != null) _patch.Unpatch(_constructor, Rewrite);
            if (_patch != null && _setup != null) _patch.Unpatch(_setup, RewriteSetup);
            _patch = null; _constructor = null; _setup = null; _headset = null;
            _failed = false; _nextBind = 0; _suppressed = _readFailures = 0;
        }
#else
        internal static void Initialize() { }
        internal static void Tick(float now) { }
        internal static void Shutdown() { }
#endif
    }
}
