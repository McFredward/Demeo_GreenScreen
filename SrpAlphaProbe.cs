#nullable disable
using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace GreenScreen
{
    // Temporary bounded measurements for the reported per-eye divergence.
    // Only alpha summaries are logged; no images or RGB values are exported.
    internal static class SrpAlphaProbe
    {
        private static HarmonyLib.Harmony _patch;
        private static MethodInfo _target;
        private static Func<object, object> _camera, _texture, _alphaOutput, _scaling, _properties, _viewport;
        private static RenderTexture _source;
        private static Camera _owner;
        private static int _frame = -1, _remaining = 24, _epoch;
        private static bool _pending, _attempted, _alpha;
        private static float _next;
#if DEMEO
        private static readonly Action<AsyncGPUReadbackRequest> Completed = Complete;
#else
        private static readonly Il2CppSystem.Action<AsyncGPUReadbackRequest> Completed =
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<AsyncGPUReadbackRequest>>(
                (Action<AsyncGPUReadbackRequest>)Complete);
#endif
        private static string _description;
        private static int _requestEpoch;
        private static int _width, _height;

        internal static void Prepare()
        {
            if (_attempted) return;
            _attempted = true;
            try
            {
                Type pass = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    pass = assembly.GetType("UnityEngine.Rendering.Universal.Internal.FinalBlitPass")
                        ?? assembly.GetType("Il2CppUnityEngine.Rendering.Universal.Internal.FinalBlitPass");
                    if (pass != null) break;
                }
                _target = pass?.GetMethod("ExecutePass", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                var parameters = _target?.GetParameters();
                if (parameters == null || parameters.Length != 5) throw new MissingMethodException("URP FinalBlitPass.ExecutePass");
                _camera = SrpOpaqueCoverage.Reader(parameters[4].ParameterType, "camera", out _);
                _texture = SrpOpaqueCoverage.Reader(parameters[2].ParameterType, "rt", out _);
                _scaling = SrpOpaqueCoverage.Reader(parameters[2].ParameterType, "useScaling", out _);
                _properties = SrpOpaqueCoverage.Reader(parameters[2].ParameterType, "rtHandleProperties", out Type propertyType);
                _viewport = SrpOpaqueCoverage.Reader(propertyType, "currentViewportSize", out _);
                _alphaOutput = SrpOpaqueCoverage.Reader(parameters[1].ParameterType, "enableAlphaOutput", out _);
                _patch = new HarmonyLib.Harmony("McFredward.DemeoGreenScreen.AlphaProbe");
                _patch.Patch(_target, postfix: new HarmonyMethod(typeof(SrpAlphaProbe).GetMethod(nameof(AfterBlit), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception error) { Utils.LogInfo("URP alpha measurement unavailable: " + error.Message); }
        }

        private static void AfterBlit(object __1, object __2, object __4)
        {
            if (_remaining == 0 || _pending || Time.unscaledTime < _next) return;
            try
            {
                Camera camera = _camera(__4) as Camera;
                if (!NativePassthrough.OwnsSrpSource(camera)) return;
                _source = _texture(__2) as RenderTexture;
                if (_source == null) return;
                _width = _source.width; _height = _source.height;
                if (_scaling(__2) is bool scaling && scaling && _viewport(_properties(__2)) is Vector2Int viewport)
                { _width = Math.Min(_width, viewport.x); _height = Math.Min(_height, viewport.y); }
                _owner = camera;
                _frame = Time.frameCount;
                _alpha = _alphaOutput(__1) is bool enabled && enabled;
            }
            catch (Exception error) { ReportFailure(error); }
        }

        internal static void AfterCamera(Camera camera)
        {
            if (_remaining == 0 || _pending || camera != _owner || _frame != Time.frameCount || _source == null) return;
            if (!NativePassthrough.OwnsSrpSource(camera)) { _source = null; return; }
            try
            {
                if (!SystemInfo.supportsAsyncGPUReadback) throw new NotSupportedException("Async GPU readback is unavailable.");
                var source = _source; _source = null;
                int width = Math.Min(32, _width), height = Math.Min(32, _height);
                if (width <= 0 || height <= 0) return;
                int slices = Math.Min(2, source.volumeDepth);
                _description = "camera=" + camera.name + "; frame=" + _frame + "; source=" + source.width + "x" + source.height
                    + "/" + source.graphicsFormat + "; viewport=" + _width + "x" + _height
                    + "; MSAA=" + source.antiAliasing + "; slices=" + slices + "; finalAlpha=" + _alpha;
                _requestEpoch = _epoch; _pending = true; _remaining--; _next = Time.unscaledTime + 2f;
                AsyncGPUReadback.Request(source, 0, (_width - width) / 2, width,
                    (_height - height) / 2, height, 0, slices, TextureFormat.RGBA32, Completed);
            }
            catch (Exception error) { _pending = false; ReportFailure(error); }
        }

        private static void Complete(AsyncGPUReadbackRequest request)
        {
            _pending = false;
            if (_requestEpoch != _epoch) return;
            if (request.hasError) { Utils.LogInfo("URP alpha measurement failed: " + _description); return; }
            try
            {
                var text = new StringBuilder("URP alpha measurement: ").Append(_description);
                for (int eye = 0; eye < request.layerCount; eye++)
                {
                    var data = request.GetData<Color32>(eye);
                    int min = 255, max = 0, zero = 0, opaque = 0; long sum = 0;
                    for (int i = 0; i < data.Length; i++)
                    {
                        int alpha = data[i].a; min = Math.Min(min, alpha); max = Math.Max(max, alpha); sum += alpha;
                        if (alpha == 0) zero++; if (alpha == 255) opaque++;
                    }
                    text.Append("; slice ").Append(eye).Append(" alpha min/mean/max=").Append(min).Append('/')
                        .Append(data.Length == 0 ? 0 : sum / data.Length).Append('/').Append(max)
                        .Append(", zero/partial/opaque=").Append(zero).Append('/').Append(data.Length - zero - opaque).Append('/').Append(opaque);
                }
                Utils.LogInfo(text.ToString());
            }
            catch (Exception error) { ReportFailure(error); }
        }

        private static void ReportFailure(Exception error)
        {
            _remaining = 0;
            Utils.LogInfo("URP alpha measurement stopped: " + error.GetType().Name + ": " + error.Message);
        }

        internal static void Shutdown()
        {
            _epoch++; _remaining = 24; _source = null; _owner = null; _frame = -1; _next = 0;
            _patch?.Unpatch(_target, typeof(SrpAlphaProbe).GetMethod(nameof(AfterBlit), BindingFlags.Static | BindingFlags.NonPublic));
            _patch = null; _attempted = false;
        }
    }
}
