// Portable OS/Unity/loader boundaries. These are not GPU or Windows pipe tests.
using System;
using System.Collections.Generic;
using System.Reflection;
namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        public T TryCast<T>() where T : Il2CppObjectBase => this as T;
        public T Cast<T>() where T : Il2CppObjectBase => (T)this;
    }
}
namespace Il2CppInterop.Runtime
{
    public static class Il2CppType { public static Type Of<T>() => typeof(T); }
    public static class DelegateSupport
    {
        public static T ConvertDelegate<T>(Delegate callback) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase,new()
        {
            var value=new T();
            ((Il2CppSystem.Delegate)(object)value).Functions.Add(callback);
            return value;
        }
    }
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppReferenceArray<T>
    {
        private readonly T[] values;
        public Il2CppReferenceArray(long count) { values=new T[count]; }
        public int Length => values.Length;
        public T this[int index] { get=>values[index]; set=>values[index]=value; }
    }
}
namespace Il2CppSystem
{
    public class Delegate : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public readonly List<System.Delegate> Functions=new List<System.Delegate>();
        public static Delegate Combine(Delegate a,Delegate b)
        {
            var result=(Delegate)Activator.CreateInstance((a??b).GetType());
            if (a!=null) result.Functions.AddRange(a.Functions);
            if (b!=null) result.Functions.AddRange(b.Functions);
            return result;
        }
        public static Delegate Remove(Delegate a,Delegate b)
        {
            if (a==null) return null;
            var result=(Delegate)Activator.CreateInstance(a.GetType());
            result.Functions.AddRange(a.Functions);
            if (b!=null) foreach (var callback in b.Functions) result.Functions.Remove(callback);
            return result.Functions.Count==0 ? null : result;
        }
    }
    public class Action<T,U> : Delegate
    { public void Invoke(T a,U b) { foreach (var callback in Functions) callback.DynamicInvoke(a,b); } }
    public class Action<T> : Delegate
    { public void Invoke(T a) { foreach (var callback in Functions) callback.DynamicInvoke(a); } }
}
namespace UnityEngine
{
    public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    { public string name; public static void Destroy(Object value) { } }
    public class Component : Object
    {
        public object Extra;
        public T GetComponent<T>() where T : class => Extra as T;
    }
    public class MonoBehaviour : Component { public MonoBehaviour() { } public MonoBehaviour(IntPtr pointer) { } }
    public class GameObject : Object
    {
        public static Camera[] Scene = Array.Empty<Camera>();
        public static readonly Dictionary<string,GameObject> Named=new Dictionary<string,GameObject>();
        public bool activeSelf=true;
        public static T[] FindObjectsOfType<T>() => (T[])(object)Scene;
        public static GameObject Find(string path) => Named.TryGetValue(path,out var item) && item.activeSelf ? item : null;
        public void SetActive(bool enabled) { activeSelf=enabled; }
    }
    public class WaitForSeconds { public WaitForSeconds(float seconds) { } }
    public enum CameraType { Game, Reflection }
    public enum CameraClearFlags { Skybox, SolidColor }
    public enum StereoTargetEyeMask { None, Left, Right, Both }
    public enum RenderingPath { Forward, DeferredShading }
    public enum RuntimePlatform { WindowsPlayer, LinuxPlayer }
    public enum ColorSpace { Gamma, Linear }
    public enum HideFlags { HideAndDontSave }
    public enum MeshTopology { Triangles }
    public enum TextureFormat { RGBA32 }
    public struct Color32 { public byte r,g,b,a; }
    public struct Vector2Int { public int x,y; }
    public struct Rect { public float x,y,width,height; }
    public struct Color : IEquatable<Color>
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r=r; this.g=g; this.b=b; this.a=a; }
        public static Color black => new Color(0,0,0,1);
        public static Color clear => new Color(0,0,0,0);
        public bool Equals(Color other) => r==other.r && g==other.g && b==other.b && a==other.a;
        public override bool Equals(object other) => other is Color color && Equals(color);
        public override int GetHashCode() => HashCode.Combine(r,g,b,a);
        public static bool operator ==(Color a, Color b) => a.Equals(b);
        public static bool operator !=(Color a, Color b) => !a.Equals(b);
    }
    public class Camera : Component
    {
        public static int allCamerasCount { get { int n=0; foreach(var c in GameObject.Scene) if(c.isActiveAndEnabled) n++; return n; } }
#if DEMEO
        public static int GetAllCameras(Camera[] buffer)
#else
        public static int GetAllCameras(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Camera> buffer)
#endif
        { int n=0; foreach(var c in GameObject.Scene) if(c.isActiveAndEnabled) buffer[n++]=c; return n; }
        public Camera() { Extra=new Rendering.Universal.UniversalAdditionalCameraData(); }
        public static Camera main;
#if DEMEO
        public delegate void CameraCallback(Camera camera);
        public static event CameraCallback onPreCull;
#else
        public class CameraCallback : Il2CppSystem.Delegate
        { public void Invoke(Camera camera) { foreach (var callback in Functions) callback.DynamicInvoke(camera); } }
        private static CameraCallback _preCull;
        public static CameraCallback onPreCull
        {
            get => _preCull;
            set => _preCull=value;
        }
#endif
        public static void Render(Camera camera) => onPreCull?.Invoke(camera);
        public bool isActiveAndEnabled=true, stereoEnabled=true, allowHDR, forceIntoRenderTexture;
        public CameraType cameraType=CameraType.Game;
        public StereoTargetEyeMask stereoTargetEye=StereoTargetEyeMask.Both;
        public RenderingPath actualRenderingPath=RenderingPath.Forward;
        public RenderTexture targetTexture;
        public Color backgroundColor=new Color(.1f,.2f,.3f,1);
        public CameraClearFlags clearFlags=CameraClearFlags.Skybox;
        public int Commands;
        public void AddCommandBuffer(Rendering.CameraEvent at, Rendering.CommandBuffer commands) { Commands++; }
        public void RemoveCommandBuffer(Rendering.CameraEvent at, Rendering.CommandBuffer commands) { Commands--; }
    }
    public class RenderTexture : Object
    {
        public int TestPass;
        public RenderTextureDescriptor descriptor=XR.XRSettings.eyeTextureDesc;
        public int width=>descriptor.width;
        public int height=>descriptor.height;
        public int volumeDepth=>descriptor.volumeDepth;
        public int antiAliasing=>descriptor.msaaSamples;
        public Experimental.Rendering.GraphicsFormat graphicsFormat=>descriptor.graphicsFormat;
        public bool IsCreated() => true;
    }
    public struct RenderTextureDescriptor
    {
        public int width,height,volumeDepth,msaaSamples;
        public bool sRGB,useDynamicScale,bindMS;
        public Rendering.TextureDimension dimension;
        public Experimental.Rendering.GraphicsFormat graphicsFormat;
    }
    public struct Matrix4x4 { public static Matrix4x4 identity => default; }
    public class Material : Object
    {
        public HideFlags hideFlags;
        public bool enableInstancing;
        public int passCount=2;
        public Material(Shader shader) { }
        public int FindPass(string name) => name=="RESET" ? 0 : 1;
    }
    public class Shader : Object { public bool isSupported=true; }
    public class AssetBundle : Object
    {
        public static bool Reject;
        public static int LoadAttempts;
        public static AssetBundle LoadFromMemory(byte[] data) { LoadAttempts++; return Reject ? null : new AssetBundle(); }
        public T LoadAsset<T>(string name) where T : Object => (T)LoadAsset(name,typeof(T));
        public Object LoadAsset(string name, Type type) => new Shader { name="Hidden/Demeo/NativeCoverage" };
        public void Unload(bool all) { }
    }
    public static class Time { public static float unscaledTime; public static int frameCount; }
    public static class Application { public static RuntimePlatform platform=RuntimePlatform.WindowsPlayer; }
    public static class QualitySettings { public static ColorSpace activeColorSpace=ColorSpace.Gamma; }
    public static class SystemInfo { public static Rendering.GraphicsDeviceType graphicsDeviceType=Rendering.GraphicsDeviceType.Direct3D11; public static bool supportsAsyncGPUReadback=true; }
    public static class Debug { public static void Log(string text) { } }
}
namespace UnityEngine.Experimental.Rendering
{
    public class XRPass { public int multipassId { get; set; }=1; }
    public enum GraphicsFormat { R8G8B8A8_UNorm, B8G8R8A8_UNorm, Other, R8G8B8A8_SRGB, B8G8R8A8_SRGB }
    public static class XRSystem
    {
        public static bool displayActive => UnityEngine.XR.XRSettings.enabled;
        public static bool isHDRDisplayOutputActive { get; set; }
        public static bool singlePassAllowed { get; set; }=true;
        private static readonly UnityEngine.XR.XRDisplaySubsystem Display=new UnityEngine.XR.XRDisplaySubsystem();
        public static UnityEngine.XR.XRDisplaySubsystem GetActiveDisplay() => Display;
    }
}
namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType { Direct3D11, Vulkan }
    public enum TextureDimension { Tex2D, Tex2DArray }
    public enum CameraEvent { AfterForwardOpaque }
    public class RenderPipelineAsset : UnityEngine.Object { }
    public static class GraphicsSettings { public static RenderPipelineAsset currentRenderPipeline; }
    public struct ScriptableRenderContext { }
    public static class RenderPipelineManager
    {
#if DEMEO
        public static event Action<ScriptableRenderContext,UnityEngine.Camera> beginCameraRendering;
        public static event Action<ScriptableRenderContext,UnityEngine.Camera> endCameraRendering;
#else
        public static Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera> beginCameraRendering;
        public static Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera> endCameraRendering;
        public static void add_beginCameraRendering(Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera> callback)
            => beginCameraRendering=Il2CppSystem.Delegate.Combine(beginCameraRendering,callback).Cast<Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera>>();
        public static void remove_beginCameraRendering(Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera> callback)
            => beginCameraRendering=Il2CppSystem.Delegate.Remove(beginCameraRendering,callback)?.Cast<Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera>>();
        public static void add_endCameraRendering(Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera> callback)
            => endCameraRendering=Il2CppSystem.Delegate.Combine(endCameraRendering,callback).Cast<Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera>>();
        public static void remove_endCameraRendering(Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera> callback)
            => endCameraRendering=Il2CppSystem.Delegate.Remove(endCameraRendering,callback)?.Cast<Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera>>();
#endif
        public static void Render(UnityEngine.Camera camera) => beginCameraRendering?.Invoke(default,camera);
        public static void End(UnityEngine.Camera camera) => endCameraRendering?.Invoke(default,camera);
    }
    public class CommandBuffer
    {
        public string name;
        public void DrawProcedural(UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int pass, UnityEngine.MeshTopology topology, int count) { }
        public void Release() { }
    }
    public class RasterCommandBuffer
    {
        public readonly List<int> Passes=new List<int>();
        public void DrawProcedural(UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int pass, UnityEngine.MeshTopology topology, int count)
        { Passes.Add(pass); }
    }
    public struct RTHandleProperties { public UnityEngine.Vector2Int currentViewportSize; }
    public class RTHandle
    {
        public UnityEngine.RenderTexture rt { get; set; }
        public bool useScaling { get; set; }
        public RTHandleProperties rtHandleProperties { get; set; }
    }
    public struct AsyncGPUReadbackRequest
    {
        public bool hasError;
        public int layerCount;
        public int TestLength;
        public byte TestAlpha;
        public UnityEngine.Color32[] GetData<T>(int layer)
        {
            var data=new UnityEngine.Color32[TestLength];
            for(int i=0;i<data.Length;i++) data[i].a=TestAlpha;
            return data;
        }
    }
    public static class AsyncGPUReadback
    {
        public static int Requests;
        public static bool Deferred;
        public static Action CompletePending;
        public static readonly List<Action> DeferredReplies=new List<Action>();
        public static readonly List<UnityEngine.RenderTexture> Targets=new List<UnityEngine.RenderTexture>();
        public static readonly List<int> Slices=new List<int>();
#if DEMEO
        public static void Request(UnityEngine.RenderTexture target,int mip,int x,int width,int y,int height,int z,int depth,UnityEngine.TextureFormat format,Action<AsyncGPUReadbackRequest> callback)
#else
        public static void Request(UnityEngine.RenderTexture target,int mip,int x,int width,int y,int height,int z,int depth,UnityEngine.TextureFormat format,Il2CppSystem.Action<AsyncGPUReadbackRequest> callback)
#endif
        {
            Requests++; Targets.Add(target); Slices.Add(z);
            Action complete=()=>callback.Invoke(new AsyncGPUReadbackRequest { layerCount=depth,TestLength=width*height,
                TestAlpha=target.TestPass==1 || z==1 ? (byte)255 : (byte)64 });
            if(Deferred) { CompletePending+=complete; DeferredReplies.Add(complete); } else complete();
        }
    }
}
namespace UnityEngine.Rendering.Universal
{
    public class UniversalAdditionalCameraData : UnityEngine.Component
    {
        public bool renderPostProcessing { get; set; }=true;
        public bool allowXRRendering { get; set; }=true;
        public int renderType { get; set; }
        public ScriptableRenderer scriptableRenderer { get; }=new UniversalRenderer();
    }
    public class ScriptableRenderer : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { }
    public class UniversalRenderer : ScriptableRenderer
    {
        public enum RenderingMode { Forward, Deferred, ForwardPlus }
        internal RenderingMode renderingModeActual { get; set; }=RenderingMode.Forward;
    }
    public class UniversalCameraData
    {
        public UnityEngine.Camera camera { get; set; }
        public UnityEngine.Experimental.Rendering.XRPass xr { get; set; }=new UnityEngine.Experimental.Rendering.XRPass();
    }
    public class UniversalRenderPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset
    {
        public bool m_AllowPostProcessAlphaOutput;
        public enum HdrPrecision { Bits32, Bits64 }
        public HdrPrecision m_HDRColorBufferPrecision;
        public bool supportsHDR { get; set; }
    }
}
namespace UnityEngine.XR
{
    public enum XRNode { Head }
    [Flags] public enum InputDeviceCharacteristics { None=0, HeadMounted=1, Controller=2 }
    public struct InputDevice
    {
        public bool isValid;
        public InputDeviceCharacteristics characteristics;
        public int Id;
    }
    public static class InputDevices
    {
        public static InputDevice Head=new InputDevice { isValid=true, characteristics=InputDeviceCharacteristics.HeadMounted, Id=1 };
        public static InputDevice GetDeviceAtXRNode(XRNode node) => Head;
#if DEMEO
        public static event Action<InputDevice> deviceConnected,deviceDisconnected,deviceConfigChanged;
#else
        private static Il2CppSystem.Action<InputDevice> deviceConnected,deviceDisconnected,deviceConfigChanged;
        public static void add_deviceConnected(Il2CppSystem.Action<InputDevice> callback) => deviceConnected=Il2CppSystem.Delegate.Combine(deviceConnected,callback).Cast<Il2CppSystem.Action<InputDevice>>();
        public static void add_deviceDisconnected(Il2CppSystem.Action<InputDevice> callback) => deviceDisconnected=Il2CppSystem.Delegate.Combine(deviceDisconnected,callback).Cast<Il2CppSystem.Action<InputDevice>>();
        public static void add_deviceConfigChanged(Il2CppSystem.Action<InputDevice> callback) => deviceConfigChanged=Il2CppSystem.Delegate.Combine(deviceConfigChanged,callback).Cast<Il2CppSystem.Action<InputDevice>>();
        public static void remove_deviceConnected(Il2CppSystem.Action<InputDevice> callback) => deviceConnected=Il2CppSystem.Delegate.Remove(deviceConnected,callback)?.Cast<Il2CppSystem.Action<InputDevice>>();
        public static void remove_deviceDisconnected(Il2CppSystem.Action<InputDevice> callback) => deviceDisconnected=Il2CppSystem.Delegate.Remove(deviceDisconnected,callback)?.Cast<Il2CppSystem.Action<InputDevice>>();
        public static void remove_deviceConfigChanged(Il2CppSystem.Action<InputDevice> callback) => deviceConfigChanged=Il2CppSystem.Delegate.Remove(deviceConfigChanged,callback)?.Cast<Il2CppSystem.Action<InputDevice>>();
#endif
        public static void Change(InputDevice device) => deviceConfigChanged?.Invoke(device);
        public static void Disconnect(InputDevice device) => deviceDisconnected?.Invoke(device);
        public static void Connect(InputDevice device) => deviceConnected?.Invoke(device);
    }
    public class XRDisplaySubsystem
    {
        public bool running { get; private set; }=true;
        public int Stops,Starts;
        public Action Started;
        public void Stop() { running=false; Stops++; }
        public void Start() { Starts++; Started?.Invoke(); running=true; }
        public static bool ReverseSlices;
        public static UnityEngine.Rect? Viewport;
        public struct XRRenderParameter { public UnityEngine.Rect viewport; public int textureArraySlice; }
        public struct XRRenderPass
        {
            public UnityEngine.RenderTextureDescriptor renderTargetDesc;
            public int GetRenderParameterCount() => renderTargetDesc.volumeDepth==2 ? 2 : 1;
            public void GetRenderParameter(UnityEngine.Camera camera,int index,out XRRenderParameter parameter)
                => parameter=new XRRenderParameter { textureArraySlice=ReverseSlices ? 1-index : index,
                    viewport=Viewport ?? new UnityEngine.Rect { width=1,height=1 } };
        }
        public int GetRenderPassCount() => XRSettings.eyeTextureDesc.volumeDepth==2 ? 1 : 2;
        public void GetRenderPass(int index,out XRRenderPass pass) => pass=new XRRenderPass { renderTargetDesc=XRSettings.eyeTextureDesc };
        public UnityEngine.RenderTexture GetRenderTextureForRenderPass(int index)
            => new UnityEngine.RenderTexture { descriptor=XRSettings.eyeTextureDesc,TestPass=index,name="XR output "+index };
    }
    public static class XRSettings
    {
        public enum StereoRenderingMode { MultiPass, SinglePassInstanced }
        public static bool enabled=true;
        public static StereoRenderingMode stereoRenderingMode;
        public static UnityEngine.RenderTextureDescriptor eyeTextureDesc=new UnityEngine.RenderTextureDescriptor
        { width=2000,height=2000,volumeDepth=1,msaaSamples=4,dimension=UnityEngine.Rendering.TextureDimension.Tex2D };
    }
}
namespace UnityEngine.XR.Management
{
    public class XRGeneralSettings
    {
        public static XRGeneralSettings Instance { get; }=new XRGeneralSettings();
        public XRManagerSettings Manager { get; }=new XRManagerSettings();
    }
    public class XRManagerSettings { public UnityEngine.Object activeLoader { get; set; }=new Unity.XR.OpenVR.OpenVRLoader(); }
}
namespace Unity.XR.OpenVR { public class OpenVRLoader : UnityEngine.Object { } }
namespace UnityEngine.XR.OpenXR
{
    public class OpenXRLoader : UnityEngine.Object { }
    public static class OpenXRRuntime { public static string name { get; set; }="SteamVR/OpenXR"; }
}
namespace HarmonyLib
{
    public class HarmonyPatch : Attribute { public HarmonyPatch() { } public HarmonyPatch(Type type,string method,Type[] args) { } }
    public class HarmonyPrepare : Attribute { }
    public class HarmonyTargetMethod : Attribute { }
    public class HarmonyPrefix : Attribute { }
    public static class AccessTools { public static Type TypeByName(string name) => typeof(Boardgame.GameStartup); }
    public class HarmonyMethod { public MethodInfo Method; public HarmonyMethod(MethodInfo method) { Method=method; } }
    public class Harmony
    {
        public static MethodInfo OpaquePostfix;
        public static MethodInfo FinalPostfix;
        public Harmony(string id) { }
        public static void CreateAndPatchAll(Assembly assembly) { }
        public void Patch(MethodInfo target,HarmonyMethod postfix) { if(target.DeclaringType.Name=="FinalBlitPass") FinalPostfix=postfix.Method; else OpaquePostfix=postfix.Method; }
        public void Unpatch(MethodInfo target,MethodInfo postfix) { if(OpaquePostfix==postfix) OpaquePostfix=null; if(FinalPostfix==postfix) FinalPostfix=null; }
    }
}
namespace UnityEngine.Rendering.Universal.Internal
{
    public class FinalBlitPass
    {
        public class PassData { public bool enableAlphaOutput; }
        public static void ExecutePass(RasterCommandBuffer cmd,PassData data,RTHandle source,RTHandle destination,UniversalCameraData camera)
            => HarmonyLib.Harmony.FinalPostfix?.Invoke(null,new object[]{data,source,camera});
    }
    public class DrawObjectsPass
    {
        public class PassData
        {
#if DEMEO
            internal bool isOpaque;
            internal UniversalCameraData cameraData;
#else
            public bool isOpaque { get; set; }
            public UniversalCameraData cameraData { get; set; }
#endif
        }
        public static void ExecutePass(RasterCommandBuffer cmd,PassData data,int list,int errors,bool yFlip)
            => HarmonyLib.Harmony.OpaquePostfix?.Invoke(null,new object[] { cmd,data });
    }
}
namespace Boardgame { public class GameStartup { } }
namespace BepInEx
{
    public static class Paths { public static string PluginPath=System.IO.Path.GetFullPath("BepInEx/plugins"); }
    public class BepInPlugin : Attribute { public BepInPlugin(string id,string name,string version) { } }
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { public Logging.ManualLogSource Logger=new Logging.ManualLogSource(); }
}
namespace BepInEx.Logging { public class ManualLogSource { public void LogInfo(string message) => GreenScreen.TestLog.Messages.Add(message); } }
namespace BepInEx.Unity.IL2CPP
{
    public class BasePlugin
    {
        public BepInEx.Logging.ManualLogSource Log=new BepInEx.Logging.ManualLogSource();
        public virtual void Load() { }
        public virtual bool Unload() => false;
        public T AddComponent<T>() => default;
    }
}
namespace MelonLoader
{
    public class MelonInfo : Attribute { public MelonInfo(Type type,string name,string version,string author,string url) { } }
    public class MelonGame : Attribute { public MelonGame(string developer,string game) { } }
    public class MelonID : Attribute { public MelonID(string id) { } }
    public class MelonMod
    {
        public virtual void OnInitializeMelon() { }
        public virtual void OnUpdate() { }
        public virtual void OnDeinitializeMelon() { }
        public virtual void OnSceneWasLoaded(int index,string scene) { }
    }
    public static class Melon<T> { public static Logger Logger=new Logger(); }
    public class Logger { public void Msg(string message) => GreenScreen.TestLog.Messages.Add(message); }
    public static class MelonLogger { public static void Msg(string message) { } }
    public static class MelonCoroutines { public static void Start(System.Collections.IEnumerator routine) { } }
}
namespace MelonLoader.Utils
{
    public static class MelonEnvironment { public static string ModsDirectory=System.IO.Path.GetFullPath("Mods"); }
}
namespace GreenScreen
{
    internal static class SteamLinkConnection
    {
        internal enum Reason { NoSession,Pending,Ready,WrongHeadset,WrongTransport }
        internal sealed class Snapshot { internal Reason State; }
        internal static Snapshot Current=new Snapshot { State=Reason.WrongHeadset };
        internal static bool Available => Current.State==Reason.Ready;
        internal static void Tick(bool wanted,float now) { if (!wanted) Reset(); }
        internal static void Reset() => Current=new Snapshot { State=Reason.NoSession };
        internal static void Invalidate() => Current=new Snapshot { State=Reason.Pending };
        internal static void Frame() => Current=new Snapshot { State=Reason.Ready };
    }
    internal static class TestLog { internal static readonly List<string> Messages=new List<string>(); }
    internal static class SteamLinkCoverageBridge
    {
        internal sealed class Snapshot { internal bool CanAcceptOrdinaryRgba; }
        internal static Snapshot Current=new Snapshot();
        internal static void Tick(bool wanted,double now) { if (!wanted) Reset(); }
        internal static void Reset() => Current=new Snapshot();
        internal static void Grant() => Current=new Snapshot { CanAcceptOrdinaryRgba=true };
    }
}
