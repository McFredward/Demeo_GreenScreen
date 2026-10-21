#nullable disable
#if !DEMEO
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace GreenScreen
{
    // Native IL2CPP methods need loader-supported detour prefixes. Rewriting
    // generated wrapper IL would not rewrite the game's native implementation.
    internal static class Il2CppAutomaticRecenter
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly List<(MethodInfo Target, MethodInfo Prefix, MethodInfo Finalizer)> Hooks = new();
        private static HarmonyLib.Harmony _patch;
        private static bool _failed;
        private static float _next;
        private static int _suppressed, _observed;
#if DEMEOBATTLES
        [ThreadStatic] private static int _avatarResetDepth, _manualDepth;
        private static bool _legacyReady;
#endif
#if DEMEOBATTLES
        private const string Game = "Demeo Battles";
#else
        private const string Game = "Demeo Battlemarked";
#endif

        internal static void Initialize()
        {
            if (_patch != null || _failed) return;
            try
            {
#if DEMEOBATTLES
                Type movement = FindType("Assembly-CSharp", "Boardgame.CameraMoveAndScaleControl");
                Type avatar = FindType("Assembly-CSharp", "Boardgame.AvatarController");
                if (movement == null || avatar == null) return;
                Add(movement, "AutoTeleportAvatar", Type.EmptyTypes, nameof(AutomaticViewpoint));
                Add(movement, "RecenterDefault", new[] { typeof(XRInputSubsystem) }, nameof(TrackingNotification), nameof(AfterTrackingNotification));
                Add(avatar, "ResetAvatarPositionRotationScaleTilt", new[] { typeof(int), typeof(bool) }, nameof(ObservePlacement));
#else
                Type recenter = FindType("Jade.XRRecentering", "Jade.XRRecentering.XRRecentering");
                if (recenter == null) return;
                // This notification can result from an intentional user reset.
                // Observe it; it is not Demeo's automatic reframe method.
                Add(recenter, "OnTrackingOriginUpdated", new[] { typeof(XRInputSubsystem) }, nameof(ObserveOrigin));
#endif
                _patch = new HarmonyLib.Harmony("McFredward.DemeoGreenScreen.Il2CppAutomaticRecenter");
                foreach (var hook in Hooks) _patch.Patch(hook.Target, prefix: new HarmonyMethod(hook.Prefix),
                    finalizer: hook.Finalizer == null ? null : new HarmonyMethod(hook.Finalizer));
#if DEMEOBATTLES
                TryLegacyGuard(avatar);
#endif
                Utils.LogInfo(Game + " IL2CPP recenter " +
#if DEMEOBATTLES
                    "guard ready: automatic viewpoint and OpenVR notification; scoped legacy avatar reset=" + _legacyReady + "."
#else
                    "observer ready: OpenXR origin notifications remain game/user owned."
#endif
                    + " Manual recenter and scene/bounds setup remain available.");
            }
            catch (Exception error)
            {
                Shutdown();
                _failed = true;
                Utils.LogInfo(Game + " IL2CPP recenter binding unavailable; retaining game behavior: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private static Type FindType(string assemblyName, string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == assemblyName || assembly.GetName().Name == "Il2Cpp" + assemblyName)
                {
                    Type type = assembly.GetType(name) ?? assembly.GetType("Il2Cpp" + name);
                    if (type != null) return type;
                }
            foreach (string candidate in new[] { assemblyName, "Il2Cpp" + assemblyName })
            {
                try
                {
                    Assembly assembly = Assembly.Load(new AssemblyName(candidate));
                    Type type = assembly.GetType(name) ?? assembly.GetType("Il2Cpp" + name);
                    if (type != null) return type;
                }
                catch (FileNotFoundException) { }
                catch (FileLoadException) { }
            }
            return null;
        }

        private static void Add(Type type, string name, Type[] arguments, string prefix, string finalizer = null, bool isStatic = false)
        {
            MethodInfo target = type.GetMethod(name, Members | BindingFlags.Static, null, arguments, null);
            if (target == null || target.IsStatic != isStatic || target.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, name);
            Hooks.Add((target, typeof(Il2CppAutomaticRecenter).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static),
                finalizer == null ? null : typeof(Il2CppAutomaticRecenter).GetMethod(finalizer, BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static bool Verified => _patch != null && ConfigurationStore.Current.SteamLinkNativePassthrough
            && Application.platform == RuntimePlatform.WindowsPlayer && IntPtr.Size == 8 && SteamLinkConnection.Available;

#if DEMEOBATTLES
        private static void TryLegacyGuard(Type avatar)
        {
            int first = Hooks.Count;
            try
            {
                Add(avatar, "ResetAvatar", Type.EmptyTypes, nameof(BeforeAvatarReset), nameof(AfterAvatarReset));
                Add(typeof(InputTracking), "Recenter", Type.EmptyTypes, nameof(LegacyAvatarRecenter), isStatic: true);
                for (int i = first; i < Hooks.Count; i++)
                    _patch.Patch(Hooks[i].Target, prefix: new HarmonyMethod(Hooks[i].Prefix),
                        finalizer: Hooks[i].Finalizer == null ? null : new HarmonyMethod(Hooks[i].Finalizer));
                _legacyReady = true;
            }
            catch (Exception error)
            {
                for (int i = first; i < Hooks.Count; i++)
                {
                    _patch.Unpatch(Hooks[i].Target, Hooks[i].Prefix);
                    if (Hooks[i].Finalizer != null) _patch.Unpatch(Hooks[i].Target, Hooks[i].Finalizer);
                }
                Hooks.RemoveRange(first, Hooks.Count - first);
                Utils.LogInfo("Scoped Battles legacy avatar reset unavailable; existing viewpoint guards remain active: "
                    + error.GetType().Name + ": " + error.Message);
            }
        }

        private static bool AutomaticViewpoint() => !Suppress("CameraMoveAndScaleControl.AutoTeleportAvatar");

        private static bool TrackingNotification(XRInputSubsystem __0, out bool __state)
        {
            __state = __0 == null;
            if (__state) _manualDepth++;
            // A null argument is the explicit controller/keyboard recenter path.
            try
            {
                return __0 == null || __0.subsystemDescriptor?.id != "OpenVR Input"
                    || !Suppress("CameraMoveAndScaleControl tracking-origin notification");
            }
            catch (Exception) { return true; }
        }

        private static void AfterTrackingNotification(bool __state)
        { if (__state && _manualDepth > 0) _manualDepth--; }

        private static void BeforeAvatarReset(out bool __state)
        {
            __state = _legacyReady && Verified && SteamLinkConnectionLifecycle.UsesOpenVr;
            if (__state) _avatarResetDepth++;
        }
        private static void AfterAvatarReset(bool __state)
        { if (__state && _avatarResetDepth > 0) _avatarResetDepth--; }

        // The supplied generated Recenter wrapper has a native MethodInfo;
        // Harmony's IL2CPP backend detours that native method, not wrapper IL.
        // The shared API forwards normally outside this exact game scope,
        // including other threads and explicit controller/keyboard requests.
        private static bool LegacyAvatarRecenter() => _avatarResetDepth == 0 || _manualDepth != 0
            || !Suppress("AvatarController.ResetAvatar legacy tracking-origin reset");

        private static bool Suppress(string source)
        {
            if (!Verified || !SteamLinkConnectionLifecycle.UsesOpenVr) return false;
            if (_suppressed++ < 64) Utils.LogInfo("Suppressed " + Game + " automatic recenter in " + source
                + "; count=" + _suppressed + "; at=" + DateTime.Now.ToString("HH:mm:ss.fff")
                + "; Frame/Steam Link confirmed by isolated reader. Manual recenter is unchanged.");
            return true;
        }

        private static void ObservePlacement(int __0, bool __1)
        {
            if (Verified && SteamLinkConnectionLifecycle.UsesOpenVr && _observed++ < 32)
                Utils.LogInfo(Game + " avatar placement retained; slot=" + __0 + "; offset=" + __1
                    + "; at=" + DateTime.Now.ToString("HH:mm:ss.fff") + ".");
        }
#else
        private static void ObserveOrigin(XRInputSubsystem __0)
        {
            if (Verified && _observed++ < 32)
                Utils.LogInfo(Game + " tracking-origin notification retained; input=" + (__0 == null ? "none" : "present")
                    + "; at=" + DateTime.Now.ToString("HH:mm:ss.fff") + ". User recenter/listener callbacks remain available.");
        }
#endif

        internal static void Tick(float now)
        {
            if (_patch != null || _failed || now < _next) return;
            _next = now + 1f;
            Initialize();
        }

        internal static void Shutdown()
        {
            if (_patch != null) foreach (var hook in Hooks)
            {
                _patch.Unpatch(hook.Target, hook.Prefix);
                if (hook.Finalizer != null) _patch.Unpatch(hook.Target, hook.Finalizer);
            }
#if DEMEOBATTLES
            _avatarResetDepth = _manualDepth = 0; _legacyReady = false;
#endif
            Hooks.Clear(); _patch = null; _failed = false; _next = 0; _suppressed = _observed = 0;
        }
    }
}
#endif
