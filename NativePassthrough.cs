#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GreenScreen
{
    // Unity owns the per-eye image and depth. The companion owns native texture,
    // encoder and receiver identity. Never manufacture coverage from RGB colours.
    internal static class NativePassthrough
    {
        private static readonly List<CameraState> Cameras = new List<CameraState>();
        private static CameraState _head;
        private static RenderTexture _target;
        private static RenderPipelineAsset _pipeline;
        private static RenderSettingLease _alphaProcessing, _hdrPrecision, _stereoPolicy;
        private static readonly List<RenderSettingLease> PostProcessing = new List<RenderSettingLease>();
        private static int _cameraVersion, _preparedCameraVersion;
        private static OpaqueCoverage _coverage;
        private static SteamLinkCoverageBridge.Snapshot _grant;
        private static int _frame = -1;
        private static bool _allowHdr;
        private static RenderingPath _renderingPath;
        private static float _nextScan;
        private static bool _started, _rescan = true;
        private static string _status;
#if DEMEO
        private static readonly Camera.CameraCallback PreCullCallback = BeforeCamera;
        private static readonly Action<ScriptableRenderContext, Camera> SrpCallback = BeforeSrpCamera;
#else
        private static readonly Camera.CameraCallback PreCullCallback =
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>((Action<Camera>)BeforeCamera);
        private static readonly Il2CppSystem.Action<ScriptableRenderContext, Camera> SrpCallback =
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>(
                (Action<ScriptableRenderContext, Camera>)BeforeSrpCamera);
#endif

        internal static void Initialize()
        {
            if (_started) return;
            _started = true;
            ConfigurationStore.Refresh();
#if DEMEO
            Camera.onPreCull += PreCullCallback;
            RenderPipelineManager.beginCameraRendering += SrpCallback;
#else
            Camera.onPreCull = Il2CppSystem.Delegate.Combine(Camera.onPreCull, PreCullCallback)?.Cast<Camera.CameraCallback>();
            RenderPipelineManager.add_beginCameraRendering(SrpCallback);
#endif
            _rescan = true;
        }

        internal static void InvalidateCameras() => _rescan = true;

        internal static void Tick()
        {
            if (!_started) return;
            try { TickCore(); }
            catch (Exception error)
            {
                SteamLinkCoverageBridge.Reset();
                LeaveNative();
                Report("Steam Link native passthrough unavailable: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private static void TickCore()
        {
            if (_rescan || Time.unscaledTime >= _nextScan)
            {
                _rescan = false;
                _nextScan = Time.unscaledTime + 1f;
                bool changed = ConfigurationStore.Refresh();
                if (changed) { SteamLinkCoverageBridge.Reset(); LeaveNative(); RestoreCameras(); }
                SrpOutput.Discover();
                ScanCameras();
            }
            ModConfiguration settings = ConfigurationStore.Current;
            if (!settings.SteamLinkNativePassthrough)
            {
                SteamLinkCoverageBridge.Tick(false, Time.unscaledTime);
                LeaveNative();
                foreach (CameraState state in Cameras) state.SetBackground(settings.BackgroundColor, false);
                Report("Steam Link native passthrough disabled; using the configured Virtual Desktop background colour.");
                return;
            }

            CameraState head = null;
            int count = 0;
            Camera main = Camera.main;
            bool mainIsStereo = StereoCamera(main);
            foreach (CameraState state in Cameras)
                if (StereoCamera(state.Camera) && (!mainIsStereo || state.Camera == main)) { head = state; count++; }
            string reason = count == 1 ? Profile(head.Camera) : "Exactly one active stereo game camera is required.";
            bool eligible = count == 1 && reason == null;
            SteamLinkCoverageBridge.Tick(eligible, Time.unscaledTime);
            var grant = SteamLinkCoverageBridge.Current;
            if (!eligible || !grant.CanAcceptOrdinaryRgba)
            {
                LeaveNative();
                foreach (CameraState state in Cameras)
                    if (StereoCamera(state.Camera)) state.SetBackground(Color.black, true);
                Report(reason ?? "Steam Link native passthrough: waiting for the compatible PC/Frame companion.");
                return;
            }

            // Only Update admits a source, before either eye of this Unity frame.
            // A mid-frame callback can revoke this grant, but cannot acquire one.
            if (_head != head || !ReferenceEquals(_grant, grant) || _target != head.Camera.targetTexture
                || _pipeline != GraphicsSettings.currentRenderPipeline || _preparedCameraVersion != _cameraVersion)
            {
                LeaveNative();
                if (!Prepare(head, out reason))
                {
                    SteamLinkCoverageBridge.Reset();
                    head.SetBackground(Color.black, true);
                    Report("Steam Link native passthrough unavailable: " + reason);
                    return;
                }
            }
            _grant = grant;
            _frame = Time.frameCount;
            head.SetBackground(Color.clear, true);
            Report("Steam Link native RGBA source enabled. Both companions and a headset picture check are required.");
        }

        private static void ScanCameras()
        {
            for (int i = Cameras.Count - 1; i >= 0; i--)
                if (Cameras[i].Camera == null) { Cameras.RemoveAt(i); _cameraVersion++; }
            foreach (Camera camera in GameObject.FindObjectsOfType<Camera>())
            {
                bool known = false;
                foreach (CameraState state in Cameras) if (state.Camera == camera) { known = true; break; }
                if (!known) { Cameras.Add(new CameraState(camera)); _cameraVersion++; }
            }
        }

        private static bool StereoCamera(Camera camera) => camera != null && camera.isActiveAndEnabled
            && camera.cameraType == CameraType.Game && camera.stereoTargetEye == StereoTargetEyeMask.Both
            && (GraphicsSettings.currentRenderPipeline == null ? camera.stereoEnabled : SrpOutput.AllowsCamera(camera));

        private static string Profile(Camera camera)
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer || IntPtr.Size != 8)
                return "The companion requires a Windows x64 game process.";
            bool srp = GraphicsSettings.currentRenderPipeline != null;
            if (srp ? !SrpOutput.Running : !XRSettings.enabled)
                return "A running XR display is required.";
            if (!srp && XRSettings.stereoRenderingMode != XRSettings.StereoRenderingMode.MultiPass)
                return "The built-in renderer requires MultiPass VR.";
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                return "The companion's source profile requires Direct3D 11.";
            RenderTexture target = camera.targetTexture;
            if (target != null && !target.IsCreated()) return "The eye target is not created.";
            if (srp && target == null) return SrpOutput.ValidateTargets();
            var descriptor = target == null ? XRSettings.eyeTextureDesc : target.descriptor;
            return ValidTarget(descriptor, false) ? null : "The current eye target must preserve SDR RGBA alpha.";
        }

        internal static bool ValidTarget(RenderTextureDescriptor descriptor, bool allowStereoArray)
        {
            bool linearStorage = descriptor.graphicsFormat == GraphicsFormat.R8G8B8A8_UNorm
                || descriptor.graphicsFormat == GraphicsFormat.B8G8R8A8_UNorm;
            bool srgbStorage = descriptor.graphicsFormat == GraphicsFormat.R8G8B8A8_SRGB
                || descriptor.graphicsFormat == GraphicsFormat.B8G8R8A8_SRGB;
            // In a Linear project Unity retains its normal sRGB framebuffer
            // encoding. The companion accepts both UNorm and sRGB native views;
            // we neither bypass that conversion nor double-premultiply RGB.
            bool colour = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? srgbStorage && descriptor.sRGB : linearStorage && !descriptor.sRGB;
            bool shape = descriptor.dimension == TextureDimension.Tex2D && descriptor.volumeDepth == 1
                || allowStereoArray && descriptor.dimension == TextureDimension.Tex2DArray && descriptor.volumeDepth == 2;
            return colour && shape && descriptor.width > 0 && descriptor.height > 0
                && (descriptor.msaaSamples == 1 || descriptor.msaaSamples == 2
                    || descriptor.msaaSamples == 4 || descriptor.msaaSamples == 8)
                && !descriptor.useDynamicScale && !descriptor.bindMS;
        }

        private static bool Prepare(CameraState head, out string reason)
        {
            _head = head;
            _target = head.Camera.targetTexture;
            _pipeline = GraphicsSettings.currentRenderPipeline;
            _allowHdr = head.Camera.allowHDR;
            _renderingPath = head.Camera.actualRenderingPath;
            _preparedCameraVersion = _cameraVersion;
            if (_pipeline != null)
            {
                object asset = RuntimeObject(_pipeline, "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset");
                if (asset == null) { reason = "Only the built-in renderer and Unity 6 URP are supported."; LeaveNative(); return false; }
                if (!SrpOutput.TryMultiPass(out _stereoPolicy))
                { reason = "This URP build cannot prepare separate eye render passes."; LeaveNative(); return false; }
                if (head.Camera.allowHDR && !RenderSettingLease.IsFalse(asset, "supportsHDR")
                    && !RenderSettingLease.TryAcquire(asset, "m_HDRColorBufferPrecision", 1, out _hdrPrecision))
                { reason = "This URP build cannot preserve alpha through HDR intermediates."; LeaveNative(); return false; }
                if (!RenderSettingLease.TryAcquire(asset, "m_AllowPostProcessAlphaOutput", true, out _alphaProcessing))
                { reason = "This URP build does not expose its alpha-processing setting."; LeaveNative(); return false; }
                // A released player may have stripped the alpha-output shader
                // variants. Do not assume flipping an asset field recreates
                // them. Bypass game post-processing while this source is owned.
                foreach (CameraState state in Cameras)
                {
                    if (state.Camera == null || !state.Camera.isActiveAndEnabled || state.Camera.cameraType != CameraType.Game) continue;
                    object data = SrpOutput.CameraData(state.Camera);
                    if (data == null) continue; // URP's no-additional-data default has no post-processing.
                    if (!RenderSettingLease.TryAcquire(data, "renderPostProcessing", false, out RenderSettingLease lease))
                    { reason = "The camera's post-processing cannot preserve alpha."; LeaveNative(); return false; }
                    PostProcessing.Add(lease);
                }
            }
            else
            {
                if (head.Camera.allowHDR || head.Camera.actualRenderingPath != RenderingPath.Forward)
                { reason = "The built-in renderer requires SDR Forward rendering."; LeaveNative(); return false; }
                // Keep RGB, current-eye depth and MSAA attachments intact. The
                // alpha-only prefix repairs opaque shaders that write no alpha.
                head.ForceIntermediate();
                _coverage = OpaqueCoverage.TryCreate(head.Camera);
                if (_coverage == null)
                { reason = "The bundled opaque-coverage shader could not be prepared."; LeaveNative(); return false; }
            }
            reason = null;
            return true;
        }

        private static void BeforeSrpCamera(ScriptableRenderContext context, Camera camera) => BeforeCamera(camera);

        private static void BeforeCamera(Camera camera)
        {
            if (_head == null || camera != _head.Camera) return;
            bool valid;
            try { valid = _frame == Time.frameCount && StereoCamera(camera) && Profile(camera) == null
                && ReferenceEquals(_grant, SteamLinkCoverageBridge.Current)
                && _grant != null && _grant.CanAcceptOrdinaryRgba
                && _target == camera.targetTexture && _pipeline == GraphicsSettings.currentRenderPipeline
                && camera.allowHDR == _allowHdr && camera.actualRenderingPath == _renderingPath
                && camera.clearFlags == CameraClearFlags.SolidColor && camera.backgroundColor == Color.clear
                && (_alphaProcessing == null || _alphaProcessing.Current)
                && (_hdrPrecision == null || _hdrPrecision.Current)
                && (_stereoPolicy == null || _stereoPolicy.Current); }
            catch { valid = false; }
            foreach (RenderSettingLease lease in PostProcessing) valid &= lease.Current;
            if (valid) return;
            // Revoke the entire source epoch, including when authority is lost
            // between the eyes. Never reuse the preceding eye's coverage.
            SteamLinkCoverageBridge.Reset();
            LeaveNative();
            _rescan = true;
        }

        private static void LeaveNative()
        {
            _grant = null;
            _frame = -1;
            foreach (RenderSettingLease lease in PostProcessing) lease.Dispose();
            PostProcessing.Clear();
            _coverage?.Dispose(); _coverage = null;
            _alphaProcessing?.Dispose(); _alphaProcessing = null;
            _hdrPrecision?.Dispose(); _hdrPrecision = null;
            _stereoPolicy?.Dispose(); _stereoPolicy = null;
            if (_head != null)
            {
                _head.RestoreIntermediate();
                _head.SetBackground(Color.black, true);
            }
            _head = null; _target = null; _pipeline = null;
        }

        internal static void Shutdown()
        {
            if (!_started) return;
            _started = false;
            SteamLinkCoverageBridge.Reset();
#if DEMEO
            Camera.onPreCull -= PreCullCallback;
            RenderPipelineManager.beginCameraRendering -= SrpCallback;
#else
            Camera.onPreCull = Il2CppSystem.Delegate.Remove(Camera.onPreCull, PreCullCallback)?.Cast<Camera.CameraCallback>();
            RenderPipelineManager.remove_beginCameraRendering(SrpCallback);
#endif
            LeaveNative();
            RestoreCameras();
            OpaqueCoverage.Unload();
        }

        private static void RestoreCameras()
        {
            foreach (CameraState state in Cameras) state.Restore();
            Cameras.Clear();
        }

        private static void Report(string message)
        {
            if (_status == message) return;
            _status = message;
            Utils.LogInfo(message);
        }

        internal static object RuntimeObject(UnityEngine.Object source, string name)
        {
#if DEMEOBATTLES || DEMEOBATTLEMARKED
            // IL2CPP base wrappers do not expose the derived native properties.
            // Cast through the installed interop type before reflecting its fields.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name) ?? assembly.GetType("Il2Cpp" + name);
                if (type == null) continue;
                MethodInfo cast = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("TryCast");
                return cast.MakeGenericMethod(type).Invoke(source, null);
            }
            return null;
#else
            return source.GetType().FullName == name ? source : null;
#endif
        }

        private sealed class CameraState
        {
            internal readonly Camera Camera;
            private readonly Color _background;
            private readonly CameraClearFlags _clear;
            private Color _written;
            private bool _changed, _changedClear, _intermediate, _originalIntermediate;
            internal CameraState(Camera camera)
            { Camera = camera; _background = camera.backgroundColor; _clear = camera.clearFlags; }
            internal void SetBackground(Color color, bool solid)
            {
                if (Camera == null) return;
                Camera.backgroundColor = _written = color; _changed = true;
                if (solid) { Camera.clearFlags = CameraClearFlags.SolidColor; _changedClear = true; }
            }
            internal void ForceIntermediate()
            {
                _originalIntermediate = Camera.forceIntoRenderTexture;
                Camera.forceIntoRenderTexture = true; _intermediate = true;
            }
            internal void RestoreIntermediate()
            {
                if (_intermediate && Camera != null && Camera.forceIntoRenderTexture)
                    Camera.forceIntoRenderTexture = _originalIntermediate;
                _intermediate = false;
            }
            internal void Restore()
            {
                RestoreIntermediate();
                if (Camera == null) return;
                if (_changed && Camera.backgroundColor == _written) Camera.backgroundColor = _background;
                if (_changedClear && Camera.clearFlags == CameraClearFlags.SolidColor) Camera.clearFlags = _clear;
            }
        }
    }

    // Unity Mono stores private fields; IL2CPP interop exposes them as properties.
    // Retain the original value and restore only a value still owned by this mod.
    internal sealed class RenderSettingLease : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly object _owner;
        private readonly FieldInfo _field;
        private readonly PropertyInfo _property;
        private readonly object _original, _written;
        private RenderSettingLease(object owner, FieldInfo field, PropertyInfo property, object written)
        { _owner = owner; _field = field; _property = property; _original = Read(); _written = written; }
        internal bool Current { get { try { return Equals(Read(), _written); } catch { return false; } } }
        private object Read() => _field != null ? _field.GetValue(_owner) : _property.GetValue(_owner);
        private void Write(object value)
        { if (_field != null) _field.SetValue(_owner, value); else _property.SetValue(_owner, value); }
        internal static bool TryAcquire(object owner, string name, object value, out RenderSettingLease lease)
        {
            lease = null;
            Type type = owner as Type ?? owner.GetType();
            object instance = owner is Type ? null : owner;
            FieldInfo field = type.GetField(name, Flags);
            PropertyInfo property = type.GetProperty(name, Flags);
            Type memberType = field != null && !field.IsInitOnly ? field.FieldType
                : property != null && property.CanRead && property.CanWrite ? property.PropertyType : null;
            if (memberType == null) return false;
            try
            {
                object written = memberType.IsEnum ? Enum.ToObject(memberType, value) : Convert.ChangeType(value, memberType);
                lease = new RenderSettingLease(instance, field, property, written);
                lease.Write(written);
                if (lease.Current) return true;
            }
            catch { }
            lease?.Dispose(); lease = null; return false;
        }
        internal static bool IsFalse(object owner, string name)
        {
            try { return owner.GetType().GetProperty(name, Flags)?.GetValue(owner) is bool value && !value; }
            catch { return false; }
        }
        public void Dispose()
        { try { if (Current) Write(_original); } catch { /* Destroyed pipeline assets need no restoration. */ } }
    }
}
