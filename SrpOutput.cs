#nullable disable
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace GreenScreen
{
    internal static class SrpOutput
    {
        private static Type _system;
        private static Func<bool> _running, _hdr;
        private static Func<XRDisplaySubsystem> _display;
        private static MethodInfo _cameraData;

        // Resolve only on the cold Update path. The IL2CPP wrappers and Mono
        // assemblies expose the same public XRSystem API in these Unity 6 games.
        internal static void Discover()
        {
            if (_system != null && _cameraData != null) return;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type data = assembly.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData")
                    ?? assembly.GetType("Il2CppUnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
                if (data != null && _cameraData == null)
                    foreach (MethodInfo method in typeof(Component).GetMethods())
                        if (method.Name == "GetComponent" && method.IsGenericMethodDefinition
                            && method.GetParameters().Length == 0)
                        { _cameraData = method.MakeGenericMethod(data); break; }
                if (_system != null) continue;
                Type type = assembly.GetType("UnityEngine.Experimental.Rendering.XRSystem")
                    ?? assembly.GetType("Il2CppUnityEngine.Experimental.Rendering.XRSystem");
                if (type == null) continue;
                try
                {
                    _running = (Func<bool>)type.GetProperty("displayActive").GetGetMethod().CreateDelegate(typeof(Func<bool>));
                    _hdr = (Func<bool>)type.GetProperty("isHDRDisplayOutputActive").GetGetMethod().CreateDelegate(typeof(Func<bool>));
                    _display = (Func<XRDisplaySubsystem>)type.GetMethod("GetActiveDisplay").CreateDelegate(typeof(Func<XRDisplaySubsystem>));
                    _system = type;
                }
                catch { _running = null; _hdr = null; _display = null; }
            }
        }

        internal static bool Running => _running != null && _running();
        internal static object CameraData(Camera camera)
        {
            if (_cameraData == null) throw new InvalidOperationException("URP camera API is unavailable.");
            return _cameraData.Invoke(camera, null);
        }
        internal static bool AllowsCamera(Camera camera)
        {
            if (!Running) return false;
            object data = CameraData(camera);
            if (data == null) return true; // URP default: Base camera, XR allowed.
            Type type = data.GetType();
            object role = type.GetProperty("renderType")?.GetValue(data);
            return type.GetProperty("allowXRRendering")?.GetValue(data) is bool allowed && allowed
                && role != null && Convert.ToInt32(role) == 0;
        }

        internal static string ValidateTargets()
        {
            if (!Running || _display == null) return "The URP XR display is not ready.";
            if (_hdr()) return "HDR headset output is outside the companion's SDR profile.";
            XRDisplaySubsystem display = _display();
            if (display == null) return "The URP XR display was replaced.";
            int passes = display.GetRenderPassCount();
            if (passes < 1 || passes > 2) return "The current XR display has an unsupported render-pass layout.";
            int views = 0;
            for (int i = 0; i < passes; i++)
            {
                display.GetRenderPass(i, out XRDisplaySubsystem.XRRenderPass pass);
                int parameters = pass.GetRenderParameterCount();
                if (parameters < 1 || parameters > 2 || !NativePassthrough.ValidTarget(pass.renderTargetDesc, true))
                    return "The current XR pass must retain SDR RGBA with one slice per eye.";
                views += parameters;
            }
            return views == 2 ? null : "The current XR source does not contain exactly two eye views.";
        }

        // URP's XRSystem always requests an array target, then creates a pass per
        // eye when this policy is false. Changing XRSettings' legacy stereo flag
        // or the display texture layout alone does not control that renderer.
        // Native DirectMode/encoder admission still independently requires the
        // real submitted single-slice resources; an array never bypasses it.
        internal static bool TryMultiPass(out RenderSettingLease lease) => _system != null
            ? RenderSettingLease.TryAcquire(_system, "singlePassAllowed", false, out lease)
            : NoLease(out lease);

        private static bool NoLease(out RenderSettingLease lease) { lease = null; return false; }
    }
}
