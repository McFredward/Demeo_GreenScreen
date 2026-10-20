using System;
using System.IO;
using GreenScreen;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.Rendering.Universal.Internal;

internal static class IntegrationChecks
{
    private static int _checks;
    private static readonly string ConfigPath=Path.Combine(Path.GetDirectoryName(typeof(ConfigurationStore).Assembly.Location), "greenscreen.ini");
    private static void Check(bool value,string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Tick() { Time.frameCount++; Time.unscaledTime+=.1f; NativePassthrough.Tick(); }
    private static void WriteConfig(bool native)
    {
        File.WriteAllText(ConfigPath,"[STEAMLINK]\nNativePassthrough="+native+"\n");
        File.SetLastWriteTimeUtc(ConfigPath,DateTime.UtcNow.AddSeconds(Time.frameCount));
        NativePassthrough.InvalidateCameras();
    }
    private static void EnableNative()
    {
        WriteConfig(true); Tick(); // Reload invalidates a previous declaration.
        SteamLinkConnection.Frame(); SteamLinkCoverageBridge.Grant(); Tick();
    }
    private static Action Callback(object instance,string name) => () => instance.GetType()
        .GetMethod(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
        .Invoke(instance,null);
    private static void CheckLoaderLifecycle(Camera camera)
    {
        Action initialize,update,shutdown;
#if MELONLOADER
        var mod=new GreenScreenMod();
        initialize=mod.OnInitializeMelon; update=mod.OnUpdate; shutdown=mod.OnDeinitializeMelon;
#elif DEMEO
        var mod=new BepInExPlugin();
        initialize=Callback(mod,"Awake"); update=Callback(mod,"Update"); shutdown=Callback(mod,"OnDestroy");
#else
        var mod=new BepInExPlugin(); var driver=new PassthroughDriver(IntPtr.Zero);
        initialize=mod.Load; update=Callback(driver,"Update"); shutdown=()=>mod.Unload();
#endif
        Color original=camera.backgroundColor;
        initialize(); Time.frameCount++; update();
        Check(camera.backgroundColor==new Color(0,1,0,1),"loader initialization and Update drive the shared camera lifecycle");
#if DEMEOBATTLEMARKED
        var room=new GameObject(); var table=new GameObject();
        GameObject.Named["BakeScale40x1/MainEnvironment/RoomBaked"]=room;
        GameObject.Named["BakeScale40x1/MainEnvironment/Table/SM_GameTableModern"]=table;
        Check(room.activeSelf && table.activeSelf,"environment scan waits until after initialization");
        Time.unscaledTime+=.6f; Time.frameCount++; update();
        Check(!room.activeSelf && !table.activeSelf,"both loaders remove Battlemarked environment and configured table");
        var lateFog=new GameObject(); GameObject.Named["BakeScale40x1/MainEnvironment/DistanceFogCatcher"]=lateFog;
        Time.unscaledTime+=1.1f; Time.frameCount++; update();
        Check(!lateFog.activeSelf,"late-created environment is removed on a later maintenance tick");
        GameObject.Named.Clear();
#endif
        shutdown();
        Check(camera.backgroundColor==original,"loader unload restores the game camera");
        Time.unscaledTime+=1.1f; Time.frameCount++; update();
        Check(camera.backgroundColor==original,"remaining driver callbacks stay inactive after unload");
    }
    public static void Main()
    {
#if !MELONLOADER
        BepInExPlugin.Log=new BepInEx.Logging.ManualLogSource();
#endif
        string working=Environment.CurrentDirectory;
        string temp=Path.Combine(Path.GetTempPath(),"demeo-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Environment.CurrentDirectory=temp;
        try
        {
            Check(ModConfiguration.Parse(Array.Empty<string>()).SteamLinkNativePassthrough,"native preference defaults on, with hardware admission required");
            Check(ModConfiguration.Parse(new[] { "[steamlink]", "nativepassthrough=TrUe" }).SteamLinkNativePassthrough,"case insensitive option");
            Check(ModConfiguration.Parse(new[] { "[STEAMLINK]", "NativePassthrough=1" }).SteamLinkNativePassthrough,"numeric true");
            Check(!ModConfiguration.Parse(new[] { "[STEAMLINK]", "NativePassthrough=invalid" }).SteamLinkNativePassthrough,"invalid option fails disabled");
            var legacy=ModConfiguration.Parse(new[] { "R=10", "G=20", "B=30", "DisableTable=0" });
            Check(legacy.BackgroundColor==new Color(10/255f,20/255f,30/255f,1) && !legacy.DisableTable,"legacy unsectioned colour and table");
            Check(ModConfiguration.Parse(new[] { "[TABLE]", "DisableTable=1" }).DisableTable,"table alias");
            Check(!ModConfiguration.Parse(new[] { "[SETTINGS]", "DisableTable=false" }).DisableTable,"legacy settings table");
            Check(ModConfiguration.Parse(new[] { "R=invalid", "G=20", "B=30" }).BackgroundColor==new Color(0,1,0,1),"invalid RGB preserves default");
            Directory.CreateDirectory("nested"); Directory.CreateDirectory("legacy");
            File.WriteAllText("legacy/greenscreen.ini",""); File.WriteAllText("nested/custom_color.ini","");
            Check(ConfigurationStore.Resolve("nested","legacy")==Path.Combine("legacy","greenscreen.ini"),"new filename precedes legacy filename");
            File.WriteAllText("nested/greenscreen.ini","");
            Check(ConfigurationStore.Resolve("nested","legacy")==Path.Combine("nested","greenscreen.ini"),"config beside DLL precedes loader root");

            File.Delete(ConfigPath);
            string loaderRoot=Path.Combine(temp,"installed-loader");
            Directory.CreateDirectory(loaderRoot);
#if MELONLOADER
            MelonLoader.Utils.MelonEnvironment.ModsDirectory=loaderRoot;
#else
            BepInEx.Paths.PluginPath=loaderRoot;
#endif
            string rootConfig=Path.Combine(loaderRoot,"greenscreen.ini");
            File.WriteAllText(rootConfig,"[STEAMLINK]\nNativePassthrough=true\n");
            Environment.CurrentDirectory=Path.Combine(temp,"nested");
            ConfigurationStore.Refresh();
            Check(ConfigurationStore.Current.SteamLinkNativePassthrough && ConfigurationStore.ActivePath==rootConfig,
                "loader root config is found independently of the process working directory");
            Check(TestLog.Messages.Exists(message=>message.Contains(rootConfig) && message.Contains("NativePassthrough=True")),
                "log identifies the loaded config path and enabled native mode");
            int messages=TestLog.Messages.Count;
            Check(!ConfigurationStore.Refresh() && TestLog.Messages.Count==messages,"unchanged configuration does not flood the log");
            File.Delete(rootConfig); ConfigurationStore.Refresh();
            Check(ConfigurationStore.Current.SteamLinkNativePassthrough && ConfigurationStore.ActivePath==""
                && TestLog.Messages.Exists(message=>message.Contains("no INI found") && message.Contains("identity")),
                "missing config defaults to automatic Frame identity admission");
            Environment.CurrentDirectory=temp;
            var camera=new Camera(); Color original=camera.backgroundColor;
            GameObject.Scene=new[] { camera };
            CheckLoaderLifecycle(camera);
            Check(TestLog.Messages.Exists(message=>message.Contains("Unity Update is active")),"first Unity Update is observable in loader logs");
            NativePassthrough.Initialize(); Tick();
            Check(camera.backgroundColor==new Color(0,1,0,1),"Virtual Desktop remains default");
            Check(TestLog.Messages.Exists(message=>message.Contains("Frame through Steam Link is not confirmed")),"non-Frame fallback explicitly reports the hardware gate");
            Check(camera.clearFlags==CameraClearFlags.Skybox && camera.Commands==0,"default leaves clear policy and render commands alone");
            WriteConfig(true); Tick();
            Check(camera.backgroundColor==new Color(0,1,0,1) && camera.Commands==0,"INI true cannot enable native mode on an unconfirmed headset");
            SteamLinkConnection.Frame(); Tick();
            Check(camera.backgroundColor==Color.black && camera.Commands==0,"missing companion keeps opaque background");
            SteamLinkCoverageBridge.Grant(); Tick();
            Check(camera.backgroundColor==Color.clear && camera.Commands==1,"granted built-in source gets transparent clear and depth prefix");
            Check(camera.forceIntoRenderTexture,"built-in intermediate preserves alpha");
            Camera.Render(camera);
            Check(SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,"valid render seam keeps authority");
            SteamLinkCoverageBridge.Reset(); Camera.Render(camera);
            Check(camera.backgroundColor==Color.black && camera.Commands==0,"mid-frame connection loss detaches coverage and fails opaque");
            Check(!camera.forceIntoRenderTexture,"connection loss restores intermediate permission");
            SteamLinkCoverageBridge.Grant(); Camera.Render(camera);
            Check(camera.backgroundColor==Color.black,"render callback cannot activate the remaining eye");
            Tick();
            Check(camera.backgroundColor==Color.clear,"next Update can admit a fresh complete frame");
            camera.targetTexture=new RenderTexture(); Camera.Render(camera);
            Check(!SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba && camera.Commands==0,"target replacement revokes old source epoch");
            SteamLinkCoverageBridge.Grant(); Tick();
            Check(camera.backgroundColor==Color.clear,"replacement target can acquire a new epoch");
            var descriptor=camera.targetTexture.descriptor; descriptor.graphicsFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.Other;
            camera.targetTexture.descriptor=descriptor; Camera.Render(camera);
            Check(camera.backgroundColor==Color.black && !SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,"unsupported changed target fails opaque");
            camera.targetTexture=null;
            QualitySettings.activeColorSpace=ColorSpace.Linear; SteamLinkCoverageBridge.Grant(); Tick();
            Check(!SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba && camera.backgroundColor==Color.black,"linear source never declares supported profile");
            QualitySettings.activeColorSpace=ColorSpace.Gamma;
            XRSettings.stereoRenderingMode=XRSettings.StereoRenderingMode.SinglePassInstanced;
            SteamLinkCoverageBridge.Grant(); Tick();
            Check(!SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,"single-pass source refused");
            XRSettings.stereoRenderingMode=XRSettings.StereoRenderingMode.MultiPass;
            var second=new Camera(); GameObject.Scene=new[] { camera,second }; NativePassthrough.InvalidateCameras();
            SteamLinkCoverageBridge.Grant(); Tick();
            Check(!SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,"ambiguous stereo camera ownership refused");
            second.isActiveAndEnabled=false;
            EnableNative();
            WriteConfig(false); Tick();
            Check(camera.backgroundColor==new Color(0,1,0,1) && camera.clearFlags==CameraClearFlags.Skybox,"live opt-out restores legacy colour and original clear policy");
            Check(camera.Commands==0 && !camera.forceIntoRenderTexture,"live opt-out releases render changes");
            NativePassthrough.Shutdown();
            Check(camera.backgroundColor==original,"unload restores original game camera");

            var pipeline=new UniversalRenderPipelineAsset(); GraphicsSettings.currentRenderPipeline=pipeline;
            GameObject.Scene=new[] { camera };
            NativePassthrough.Initialize(); EnableNative();
            Check(pipeline.m_AllowPostProcessAlphaOutput && camera.backgroundColor==Color.clear,"URP preserves post-processing alpha");
            camera.stereoEnabled=false; Tick(); RenderPipelineManager.Render(camera);
            Check(camera.backgroundColor==Color.clear,"modern URP XR does not depend on the legacy camera stereo flag");
            Check(UnityEngine.Experimental.Rendering.XRSystem.singlePassAllowed,"URP retains its one-pass/two-eye layout so native pass indices remain valid");
            Check(!((UniversalAdditionalCameraData)camera.Extra).renderPostProcessing,"URP bypasses potentially stripped alpha post-processing variants");
            Check(camera.Commands==0 && !camera.forceIntoRenderTexture,"URP records inside its raster pass without legacy camera command buffers");
            RenderPipelineManager.Render(camera);
            Check(SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,"SRP render seam validates current source");
            var raster=new RasterCommandBuffer();
            var opaqueData=new DrawObjectsPass.PassData { isOpaque=true, cameraData=new UniversalCameraData { camera=camera } };
            DrawObjectsPass.ExecutePass(raster,opaqueData,0,0,false);
            Check(raster.Passes.Count==2 && raster.Passes[0]==0 && raster.Passes[1]==1,
                "URP opaque postfix records alpha reset and same-depth stamp inside the raster pass");
            Check(TestLog.Messages.Exists(message=>message.Contains("URP opaque depth coverage is active")),
                "the first executed opaque repair is observable in game logs");
            opaqueData.isOpaque=false; DrawObjectsPass.ExecutePass(raster,opaqueData,0,0,false);
            Check(raster.Passes.Count==2,"transparent pass alpha is retained after the opaque repair");
            opaqueData.isOpaque=true; opaqueData.cameraData.camera=second;
            DrawObjectsPass.ExecutePass(raster,opaqueData,0,0,false);
            Check(raster.Passes.Count==2,"foreign and spectator camera attachments are never stamped");
            opaqueData.cameraData.camera=camera;
            Time.frameCount++; DrawObjectsPass.ExecutePass(raster,opaqueData,0,0,false);
            Check(raster.Passes.Count==2 && !SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,
                "a stale frame cannot record coverage or reuse the preceding eye's authority");
            SteamLinkCoverageBridge.Grant(); Tick();
            pipeline.m_AllowPostProcessAlphaOutput=false; RenderPipelineManager.Render(camera);
            Check(!SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba && camera.backgroundColor==Color.black,"foreign alpha processing change revokes output");
            SteamLinkCoverageBridge.Grant(); Tick();
            Check(pipeline.m_AllowPostProcessAlphaOutput,"URP can prepare again at next boundary");
            NativePassthrough.Shutdown();
            DrawObjectsPass.ExecutePass(raster,opaqueData,0,0,false);
            Check(raster.Passes.Count==2 && HarmonyLib.Harmony.OpaquePostfix==null,"unload removes the owned URP patch");
            Check(!pipeline.m_AllowPostProcessAlphaOutput,"unload restores original pipeline setting");
            Check(UnityEngine.Experimental.Rendering.XRSystem.singlePassAllowed,"unload retains the original stereo policy");
            Check(((UniversalAdditionalCameraData)camera.Extra).renderPostProcessing,"unload restores original camera post-processing");
            Check(camera.backgroundColor==original,"URP unload restores original camera background");
            pipeline.m_AllowPostProcessAlphaOutput=true;
            NativePassthrough.Initialize(); EnableNative(); NativePassthrough.Shutdown();
            Check(pipeline.m_AllowPostProcessAlphaOutput,"pre-existing alpha processing survives teardown");
            var renderer=(UniversalRenderer)((UniversalAdditionalCameraData)camera.Extra).scriptableRenderer;
            renderer.renderingModeActual=UniversalRenderer.RenderingMode.Deferred;
            NativePassthrough.Initialize(); EnableNative();
            Check(camera.backgroundColor==Color.black && !SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,
                "a renderer without the opaque Forward seam is refused");
            NativePassthrough.Shutdown(); renderer.renderingModeActual=UniversalRenderer.RenderingMode.Forward;

            // Match the supplied Unity 6 games: Linear lighting, sRGB XR array,
            // URP MultiPass render passes, and HDR intermediates with SDR output.
            QualitySettings.activeColorSpace=ColorSpace.Linear;
            var eye=XRSettings.eyeTextureDesc;
            eye.sRGB=true;
            eye.graphicsFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB;
            eye.dimension=TextureDimension.Tex2DArray; eye.volumeDepth=2;
            XRSettings.eyeTextureDesc=eye;
            XRSettings.stereoRenderingMode=XRSettings.StereoRenderingMode.SinglePassInstanced;
            camera.allowHDR=true; pipeline.supportsHDR=true;
            pipeline.m_AllowPostProcessAlphaOutput=false;
            NativePassthrough.Initialize(); EnableNative();
            Check(camera.backgroundColor==Color.clear,"Linear URP keeps normal sRGB output instead of requiring a Gamma project");
            Check(UnityEngine.Experimental.Rendering.XRSystem.singlePassAllowed,"native single-pass array layout is retained for both eyes");
            Check(pipeline.m_HDRColorBufferPrecision==UniversalRenderPipelineAsset.HdrPrecision.Bits64,"HDR intermediate retains alpha");
            renderer.renderingModeActual=UniversalRenderer.RenderingMode.Deferred;
            RenderPipelineManager.Render(camera);
            Check(camera.backgroundColor==Color.black && !SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,
                "a renderer change after preparation revokes coverage before drawing");
            renderer.renderingModeActual=UniversalRenderer.RenderingMode.Forward;
            SteamLinkCoverageBridge.Grant(); Tick();
            InputDevices.Change(new InputDevice { isValid=true, characteristics=InputDeviceCharacteristics.Controller, Id=2 });
            Check(SteamLinkConnection.Available,"controller changes do not revoke the HMD identity");
            InputDevices.Disconnect(InputDevices.Head);
            RenderPipelineManager.Render(camera);
            Check(!SteamLinkConnection.Available && !SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba && camera.backgroundColor==Color.black,
                "HMD disconnect revokes source authority before the next render");
            Tick();
            Check(camera.backgroundColor==new Color(0,1,0,1) && !pipeline.m_AllowPostProcessAlphaOutput,
                "unconfirmed HMD restores the regular background and URP settings regardless of INI");
            var manager=UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager;
            manager.activeLoader=new UnityEngine.XR.OpenXR.OpenXRLoader();
            UnityEngine.XR.OpenXR.OpenXRRuntime.name="Virtual Desktop";
            SteamLinkConnection.Frame(); Time.unscaledTime+=1.1f; Tick();
            Check(!SteamLinkConnection.Available && camera.backgroundColor==new Color(0,1,0,1),
                "another game OpenXR runtime blocks native mode even if a separate SteamVR Frame session is available");
            UnityEngine.XR.OpenXR.OpenXRRuntime.name="SteamVR/OpenXR";
            Time.unscaledTime+=1.1f; EnableNative();
            Check(camera.backgroundColor==Color.clear,"confirmed Frame is admitted through the game's SteamVR OpenXR loader");
            NativePassthrough.Shutdown();
            Check(pipeline.m_HDRColorBufferPrecision==UniversalRenderPipelineAsset.HdrPrecision.Bits32,"HDR intermediate precision restored");
            AssetBundle.Reject=true; int attempts=AssetBundle.LoadAttempts;
            NativePassthrough.Initialize(); EnableNative();
            Check(camera.backgroundColor==Color.black && OpaqueCoverage.Failure.Contains("LoadFromMemory"),
                "a rejected bundle stays opaque and identifies the actual native load failure");
            Check(AssetBundle.LoadAttempts==attempts+1,"the immutable embedded bundle is attempted only once per mod lifetime");
            int reports=TestLog.Messages.Count;
            for(int i=0;i<4;i++) { SteamLinkCoverageBridge.Grant(); Tick(); }
            Check(!SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba && AssetBundle.LoadAttempts==attempts+1,
                "a permanent bundle error prevents source re-admission and reconnect churn");
            Check(TestLog.Messages.Count==reports,"a permanent bundle error does not flood the game log");
            NativePassthrough.Shutdown(); AssetBundle.Reject=false;
            NativePassthrough.Initialize(); EnableNative();
            Check(camera.backgroundColor==Color.clear && OpaqueCoverage.Failure==null,"a new mod lifetime retries loading and recovers from a prior bundle error");
            NativePassthrough.Shutdown();
            Console.WriteLine($"Configuration and render lifecycle checks passed: {_checks}");
        }
        finally
        {
            NativePassthrough.Shutdown();
            File.Delete(ConfigPath);
            Environment.CurrentDirectory=working;
            Directory.Delete(temp,true);
        }
    }
}
