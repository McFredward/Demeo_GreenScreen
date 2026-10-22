#nullable disable
using System;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GreenScreen
{
#if DEMEO
    // Bounded alpha summaries of final XR targets; no image or RGB exports.
    // AsyncGPUReadback owns staging storage. Never read a pooled URP input.
    internal static class SrpAlphaProbe
    {
        private static HarmonyLib.Harmony _patch;
        private static MethodInfo _target;
        private static Func<object, object> _camera, _texture, _alphaOutput, _xr, _nativePass;
        private static Camera _owner;
        private static int _frame = -1, _remaining = 24, _epoch;
        private static int _failures;
        private static bool _attempted, _alpha;
        private static float _next;
        private static string _sourceInfo;
        private static Batch _active;
        private const int Width = 32;

        private sealed class Batch
        {
            internal readonly int Epoch = _epoch;
            internal readonly string[] Results = new string[2];
            internal readonly RenderTexture[] Targets = new RenderTexture[2];
            internal readonly int[] Slices = new int[2], X = new int[2], Y = new int[2], Heights = new int[2];
#if DEMEO
            internal readonly Action<AsyncGPUReadbackRequest>[] Callbacks = new Action<AsyncGPUReadbackRequest>[2];
#else
            internal readonly Il2CppSystem.Action<AsyncGPUReadbackRequest>[] Callbacks = new Il2CppSystem.Action<AsyncGPUReadbackRequest>[2];
#endif
            internal string Description;
            internal int Pending;
        }

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
                _alphaOutput = SrpOpaqueCoverage.Reader(parameters[1].ParameterType, "enableAlphaOutput", out _);
                _xr = SrpOpaqueCoverage.Reader(parameters[4].ParameterType, "xr", out Type xrType);
                _nativePass = SrpOpaqueCoverage.Reader(xrType, "multipassId", out _);
                _patch = new HarmonyLib.Harmony("McFredward.DemeoGreenScreen.AlphaProbe");
                _patch.Patch(_target, postfix: new HarmonyMethod(typeof(SrpAlphaProbe).GetMethod(nameof(AfterBlit), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception error) { Utils.LogWarning("XR output alpha measurement unavailable: " + error.Message); }
        }

        private static void AfterBlit(object __1, object __2, object __4)
        {
            if (_remaining == 0 || _active != null || Time.unscaledTime < _next) return;
            try
            {
                Camera camera = _camera(__4) as Camera;
                if (!NativePassthrough.OwnsSrpSource(camera)) return;
                // endCameraRendering runs once for EACH native MultiPass eye.
                // Wait for the last pass before reading a current-frame pair;
                // after the first pass the other target still holds an old eye.
                var display = SrpOutput.ActiveDisplay;
                if (display == null || display.GetRenderPassCount() == 2
                    && Convert.ToInt32(_nativePass(_xr(__4))) != 1) return;
                var source = _texture(__2) as RenderTexture;
                _sourceInfo = source == null ? "; blit-input=external" : "; blit-input=" + source.width + "x" + source.height
                    + "/" + source.graphicsFormat + "; input-MSAA=" + source.antiAliasing;
                _owner = camera; _frame = Time.frameCount;
                _alpha = _alphaOutput(__1) is bool enabled && enabled;
            }
            catch (Exception error) { ReportFailure(error); }
        }

        internal static void AfterCamera(Camera camera)
        {
            if (_remaining == 0 || _active != null || camera != _owner || _frame != Time.frameCount) return;
            if (!NativePassthrough.OwnsSrpSource(camera)) return;
            try
            {
                if (!SystemInfo.supportsAsyncGPUReadback) throw new NotSupportedException("Async GPU readback is unavailable.");
                var display = SrpOutput.ActiveDisplay;
                if (display == null) return;
                int passes = display.GetRenderPassCount(), views = 0;
                if (passes < 1 || passes > 2) throw new NotSupportedException("Unknown XR pass layout.");
                var batch = new Batch();
                var description = new StringBuilder("camera=").Append(camera.name).Append("; frame=").Append(_frame)
                    .Append(_sourceInfo).Append("; finalAlpha=").Append(_alpha);
                for (int passIndex = 0; passIndex < passes; passIndex++)
                {
                    display.GetRenderPass(passIndex, out XRDisplaySubsystem.XRRenderPass pass);
                    var descriptor = pass.renderTargetDesc;
                    // The XR provider owns this persistent target. Do not use
                    // the pooled final-blit input or mutate/resolve either one.
                    var target = display.GetRenderTextureForRenderPass(passIndex);
                    if (target == null || !target.IsCreated() || !NativePassthrough.ValidTarget(descriptor, true)
                        || target.width != descriptor.width || target.height != descriptor.height
                        || target.volumeDepth != descriptor.volumeDepth || target.graphicsFormat != descriptor.graphicsFormat)
                        throw new NotSupportedException("Current XR target is unavailable or differs from pass " + passIndex
                            + " (" + descriptor.width + "x" + descriptor.height + "/" + descriptor.graphicsFormat
                            + "/MSAA" + descriptor.msaaSamples + "/" + descriptor.dimension + ").");
                    int parameters = pass.GetRenderParameterCount();
                    if (parameters < 1 || parameters > 2) throw new NotSupportedException("Unknown XR view layout.");
                    for (int parameterIndex = 0; parameterIndex < parameters; parameterIndex++)
                    {
                        if (views == 2) throw new NotSupportedException("More than two XR views.");
                        pass.GetRenderParameter(camera, parameterIndex, out XRDisplaySubsystem.XRRenderParameter parameter);
                        // XRRenderParameter.viewport is normalized. Match the
                        // game's XRSystem.BuildView before using pixel bounds.
                        Rect normalized = parameter.viewport;
                        Rect viewport = PixelViewport(normalized, descriptor.width, descriptor.height);
                        int left = (int)Math.Ceiling(viewport.x), bottom = (int)Math.Ceiling(viewport.y);
                        int right = (int)Math.Floor(viewport.x + viewport.width), top = (int)Math.Floor(viewport.y + viewport.height);
                        int slice = parameter.textureArraySlice;
                        if (left < 0 || bottom < 0 || right > descriptor.width || top > descriptor.height
                            || right - left < Width || top - bottom < Width || top - bottom > 16384
                            || slice < 0 || slice >= descriptor.volumeDepth)
                            throw new NotSupportedException("XR view outside target: pass=" + passIndex + "; view=" + parameterIndex
                                + "; normalized=" + normalized.x + "," + normalized.y + "," + normalized.width + "," + normalized.height
                                + "; pixels=" + left + "," + bottom + "," + right + "," + top
                                + "; slice=" + slice + "; target=" + descriptor.width + "x" + descriptor.height + "x" + descriptor.volumeDepth + ".");
                        batch.Targets[views] = target; batch.Slices[views] = slice;
                        batch.X[views] = left + (right - left - Width) / 2; batch.Y[views] = bottom;
                        batch.Heights[views] = top - bottom;
                        description.Append("; view ").Append(views).Append("=pass ").Append(passIndex).Append("/slice ")
                            .Append(slice).Append('/').Append(descriptor.width).Append('x').Append(descriptor.height)
                            .Append('/').Append(descriptor.graphicsFormat).Append("/MSAA").Append(target.antiAliasing)
                            .Append("/crop=").Append(batch.X[views]).Append(',').Append(bottom).Append(',').Append(Width).Append(',').Append(top - bottom);
                        views++;
                    }
                }
                if (views != 2) throw new NotSupportedException("Two XR views are required.");
                batch.Description = description.ToString(); batch.Pending = 2;
                _active = batch; _frame = -1; _remaining--; _next = Time.unscaledTime + 2f;
                for (int view = 0; view < 2; view++)
                {
                    int capturedView = view;
                    try
                    {
                        Action<AsyncGPUReadbackRequest> completed = request => Complete(batch, capturedView, request);
#if DEMEO
                        batch.Callbacks[view] = completed;
#else
                        batch.Callbacks[view] = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<AsyncGPUReadbackRequest>>(completed);
#endif
                        AsyncGPUReadback.Request(batch.Targets[view], 0, batch.X[view], Width, batch.Y[view], batch.Heights[view],
                            batch.Slices[view], 1, TextureFormat.RGBA32, batch.Callbacks[view]);
                    }
                    catch (Exception error) { batch.Results[view] = "; view " + view + " request failed: " + error.Message; Finish(batch, view); }
                }
            }
            catch (Exception error) { ReportFailure(error); }
        }

        internal static Rect PixelViewport(Rect viewport, int width, int height)
        {
            viewport.x *= width; viewport.width *= width;
            viewport.y *= height; viewport.height *= height;
            return viewport;
        }

        private static void Complete(Batch batch, int view, AsyncGPUReadbackRequest request)
        {
            try
            {
                if (batch.Epoch != _epoch) return;
                if (request.hasError) { batch.Results[view] = "; view " + view + " GPU readback failed"; return; }
                var data = request.GetData<Color32>(0);
                if (request.layerCount != 1 || data.Length != Width * batch.Heights[view])
                    throw new InvalidOperationException("Unexpected XR snapshot length.");
                var text = new StringBuilder();
                // Three 32x32 samples from a bounded vertical strip. Only alpha
                // is inspected; no RGB values or image files are produced.
                for (int region = 0; region < 3; region++)
                {
                    int row = Math.Max(0, Math.Min(batch.Heights[view] - Width, batch.Heights[view] * (region + 1) / 4 - Width / 2));
                    int min = 255, max = 0, zero = 0, opaque = 0; long sum = 0;
                    for (int i = row * Width; i < (row + Width) * Width; i++)
                    {
                        int alpha = data[i].a; min = Math.Min(min, alpha); max = Math.Max(max, alpha); sum += alpha;
                        if (alpha == 0) zero++; if (alpha == 255) opaque++;
                    }
                    text.Append("; view ").Append(view).Append(" region ").Append(region).Append(" alpha min/mean/max=").Append(min).Append('/')
                        .Append(sum / (Width * Width)).Append('/').Append(max)
                        .Append(", zero/partial/opaque=").Append(zero).Append('/').Append(Width * Width - zero - opaque).Append('/').Append(opaque);
                }
                batch.Results[view] = text.ToString();
            }
            catch (Exception error) { batch.Results[view] = "; view " + view + " measurement failed: " + error.Message; }
            finally { Finish(batch, view); }
        }

        private static void Finish(Batch batch, int view)
        {
            // Break managed/native delegate cycles after this callback. Unity
            // retains its executing native delegate until the callback returns.
            batch.Callbacks[view] = null; batch.Targets[view] = null;
            if (Interlocked.Decrement(ref batch.Pending) != 0) return;
            if (ReferenceEquals(_active, batch)) _active = null;
            if (batch.Epoch == _epoch)
                Utils.LogDebug("XR output alpha measurement: " + batch.Description + batch.Results[0] + batch.Results[1]);
        }

        private static void ReportFailure(Exception error)
        {
            _frame = -1; _next = Time.unscaledTime + 2f;
            if (++_failures >= 4) _remaining = 0;
            Utils.LogWarning("XR output alpha measurement " + (_remaining == 0 ? "stopped" : "deferred")
                + " (" + _failures + "/4): " + error.GetType().Name + ": " + error.Message);
        }

        internal static void Shutdown()
        {
            _epoch++; _remaining = 24; _failures = 0; _owner = null; _frame = -1; _next = 0;
            // Keep in-flight native callback delegates rooted until both replies
            // arrive, including after unload/reload. Old replies never log.
            _patch?.Unpatch(_target, typeof(SrpAlphaProbe).GetMethod(nameof(AfterBlit), BindingFlags.Static | BindingFlags.NonPublic));
            _patch = null; _attempted = false;
        }
    }
#else
    // The supplied Unity 6 IL2CPP generator emits XRRenderParameter as a
    // reference wrapper, then passes only a pointer-sized out slot to Unity's
    // value-type API. GetRenderParameter overwrites that slot/stack and can
    // crash the process before a managed catch runs. This optional diagnostic
    // must never call that wrapper. Coverage and native Submit samples do not
    // depend on it. Keep Demeo's working Mono measurement above unchanged.
    internal static class SrpAlphaProbe
    {
        private static bool _reported;
        internal static void Prepare()
        {
            if (_reported) return;
            _reported = true;
            Utils.LogDebug("Optional XR GPU alpha probe disabled for IL2CPP: unsafe generated XRRenderParameter out binding. Native coverage and exact-submit alpha statistics remain active.");
        }
        internal static void AfterCamera(Camera camera) { }
        internal static void Shutdown() => _reported = false;
    }
#endif
}
