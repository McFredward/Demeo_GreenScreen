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
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--provider-pin")
        {
            byte[] provider = File.ReadAllBytes(args[1]);
            Check(OpenVrNativeSettings.Pin(provider), "the actual supplied provider matches the production pin");
            provider[provider.Length-1] ^= 1;
            Check(!OpenVrNativeSettings.Pin(provider), "a changed actual provider is refused");
            Console.WriteLine("Actual OpenVR provider pin checks passed: " + _checks);
            return;
        }
        if (args.Length == 2 && args[0] == "--native-submit") { NativeSubmitChecks.Run(args[1]); return; }
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
            Check(camera.backgroundColor==Color.black,"unclassified startup never writes a green frame");
            Check(TestLog.Messages.Exists(message=>message.Contains("without a green startup frame")),"pending identity reports the opaque startup state");
            Check(camera.clearFlags==CameraClearFlags.SolidColor && camera.Commands==0,"pending identity keeps the background opaque without native commands");
            WriteConfig(true); Tick();
            Check(camera.backgroundColor==Color.black && camera.Commands==0,"INI true cannot expose green or native mode during identity detection");
            SteamLinkConnection.Current=new SteamLinkConnection.Snapshot { State=SteamLinkConnection.Reason.WrongHeadset }; Tick();
            Check(camera.backgroundColor==new Color(0,1,0,1) && camera.clearFlags==CameraClearFlags.Skybox,"a positively identified other headset restores legacy colour and clear policy");
            SteamLinkConnection.Invalidate();
            var earlyCamera=new Camera(); Camera.Render(earlyCamera);
            Check(earlyCamera.backgroundColor==Color.black,"a new camera rendering before Update cannot flash green during pending identity");
            earlyCamera.isActiveAndEnabled=false;
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
            var offscreen=new Camera { targetTexture=new RenderTexture() }; Color offscreenOriginal=offscreen.backgroundColor;
            GameObject.Scene=new[]{camera,offscreen}; Camera.main=offscreen; Tick();
            Check(camera.backgroundColor==Color.clear && offscreen.backgroundColor==offscreenOriginal
                && ((UniversalAdditionalCameraData)offscreen.Extra).renderPostProcessing,
                "a tagged offscreen camera cannot replace the real XR owner or have its post-processing altered");
            GameObject.Scene=new[]{camera}; Camera.main=camera;
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
            var source=new RenderTexture(); var sourceDescription=source.descriptor;
            sourceDescription.volumeDepth=2; sourceDescription.dimension=TextureDimension.Tex2DArray; sourceDescription.msaaSamples=8; source.descriptor=sourceDescription;
            int beforeLeft=AsyncGPUReadback.Requests;
            var leftData=new UniversalCameraData { camera=camera, xr=new UnityEngine.Experimental.Rendering.XRPass { multipassId=0 } };
            FinalBlitPass.ExecutePass(raster,new FinalBlitPass.PassData { enableAlphaOutput=true },new RTHandle { rt=source },new RTHandle(),leftData);
            RenderPipelineManager.End(camera);
            Check(AsyncGPUReadback.Requests==beforeLeft,"a MultiPass left-eye callback cannot read the previous frame's right eye");
            FinalBlitPass.ExecutePass(raster,new FinalBlitPass.PassData { enableAlphaOutput=true },
                new RTHandle { rt=source },new RTHandle(),new UniversalCameraData { camera=camera });
            RenderPipelineManager.End(camera);
            Check(TestLog.Messages.Exists(message=>message.Contains("XR output alpha measurement:")
                && message.Contains("view 0 region 1 alpha min/mean/max=64/64/64") && message.Contains("view 1 region 1 alpha min/mean/max=255/255/255")),
                "the bounded asynchronous probe distinguishes both final XR views without RGB output");
            Check(AsyncGPUReadback.Targets.TrueForAll(target=>target!=source && target.name.StartsWith("XR output")),
                "the probe reads final XR targets and never the pooled MSAA8 game intermediate");
            int reads=AsyncGPUReadback.Requests;
            FinalBlitPass.ExecutePass(raster,new FinalBlitPass.PassData(),new RTHandle { rt=source },new RTHandle(),new UniversalCameraData { camera=camera });
            RenderPipelineManager.End(camera);
            Check(AsyncGPUReadback.Requests==reads && SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,
                "alpha measurements are rate limited and do not change source permission");
            // Array-view diagnostics remain supported for SteamVR/OpenXR. The
            // OpenVR provider path now deliberately requires real native 2D eyes.
            UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.activeLoader=new UnityEngine.XR.OpenXR.OpenXRLoader();
            Time.unscaledTime+=1.1f; Tick();
            var originalXr=XRSettings.eyeTextureDesc;
            XRSettings.eyeTextureDesc.volumeDepth=2; XRSettings.eyeTextureDesc.dimension=TextureDimension.Tex2DArray;
            XRDisplaySubsystem.ReverseSlices=true;
            XRDisplaySubsystem.Viewport=new Rect { x=.05f,y=.1f,width=.8f,height=.75f };
            Time.unscaledTime+=2.1f; Tick();
            FinalBlitPass.ExecutePass(raster,new FinalBlitPass.PassData(),new RTHandle { rt=source },new RTHandle(),new UniversalCameraData { camera=camera });
            RenderPipelineManager.End(camera);
            Check(AsyncGPUReadback.Slices[reads]==1 && AsyncGPUReadback.Slices[reads+1]==0
                && TestLog.Messages.Exists(message=>message.Contains("/crop=884,200,32,1500")),
                "array probes follow the provider's actual slice mapping and cropped viewports rather than guessing an eye index");
            Time.unscaledTime+=2.1f; Tick(); AsyncGPUReadback.Deferred=true;
            int firstReply=AsyncGPUReadback.DeferredReplies.Count, beforeReplies=TestLog.Messages.Count;
            FinalBlitPass.ExecutePass(raster,new FinalBlitPass.PassData(),new RTHandle { rt=source },new RTHandle(),new UniversalCameraData { camera=camera });
            RenderPipelineManager.End(camera);
            AsyncGPUReadback.DeferredReplies[firstReply+1]();
            Check(TestLog.Messages.Count==beforeReplies,"an early second-eye reply does not publish an incomplete pair");
            AsyncGPUReadback.DeferredReplies[firstReply]();
            Check(TestLog.Messages[TestLog.Messages.Count-1].Contains("view 0 region 1 alpha min/mean/max=255/255/255")
                && TestLog.Messages[TestLog.Messages.Count-1].Contains("view 1 region 1 alpha min/mean/max=64/64/64"),
                "out-of-order GPU replies retain the exact requested view and slice");
            AsyncGPUReadback.CompletePending=null; AsyncGPUReadback.Deferred=false;
            XRDisplaySubsystem.ReverseSlices=false; XRDisplaySubsystem.Viewport=null; XRSettings.eyeTextureDesc=originalXr;
            var replacement=new Camera(); camera.isActiveAndEnabled=false;
            GameObject.Scene=new[]{replacement}; Camera.main=replacement; Tick();
            Check(replacement.backgroundColor==Color.clear && SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,
                "the intro-to-menu camera replacement is discovered this Update without a manual invalidation or a one-second wait");
            replacement.isActiveAndEnabled=false; camera.isActiveAndEnabled=true;
            GameObject.Scene=new[]{camera}; Camera.main=camera; Tick();
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
            Time.unscaledTime+=2.1f; Tick(); AsyncGPUReadback.Deferred=true;
            FinalBlitPass.ExecutePass(raster,new FinalBlitPass.PassData(),new RTHandle { rt=source },new RTHandle(),new UniversalCameraData { camera=camera });
            RenderPipelineManager.End(camera);
            Check(AsyncGPUReadback.CompletePending!=null,"the probe supports a real delayed GPU completion");
            NativePassthrough.Shutdown();
            int afterShutdown=TestLog.Messages.Count;
            AsyncGPUReadback.CompletePending(); AsyncGPUReadback.CompletePending=null; AsyncGPUReadback.Deferred=false;
            Check(TestLog.Messages.Count==afterShutdown && !SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,
                "a late GPU reply from an unloaded mod epoch neither logs a new-session value nor grants source permission");
            DrawObjectsPass.ExecutePass(raster,opaqueData,0,0,false);
            Check(raster.Passes.Count==2 && HarmonyLib.Harmony.OpaquePostfix==null,"unload removes the owned URP patch");
            Check(!pipeline.m_AllowPostProcessAlphaOutput,"unload restores original pipeline setting");
            Check(UnityEngine.Experimental.Rendering.XRSystem.singlePassAllowed,"unload retains the original stereo policy");
            Check(((UniversalAdditionalCameraData)camera.Extra).renderPostProcessing,"unload restores original camera post-processing");
            Check(camera.backgroundColor==original,"URP unload restores original camera background");
            pipeline.m_AllowPostProcessAlphaOutput=true;
            NativePassthrough.Initialize(); EnableNative();
            int beforeInvalidView=AsyncGPUReadback.Requests;
            XRDisplaySubsystem.Viewport=new Rect { x=1.1f,width=.5f,height=.5f };
            FinalBlitPass.ExecutePass(raster,new FinalBlitPass.PassData(),new RTHandle { rt=source },new RTHandle(),new UniversalCameraData { camera=camera });
            RenderPipelineManager.End(camera);
            Check(AsyncGPUReadback.Requests==beforeInvalidView && SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba,
                "an invalid diagnostic viewport never issues an unsafe GPU request or revokes native output");
            XRDisplaySubsystem.Viewport=null; Time.unscaledTime+=2.1f; Tick();
            FinalBlitPass.ExecutePass(raster,new FinalBlitPass.PassData(),new RTHandle { rt=source },new RTHandle(),new UniversalCameraData { camera=camera });
            RenderPipelineManager.End(camera);
            Check(AsyncGPUReadback.Requests==beforeInvalidView+2,"a transient invalid XR viewport does not permanently suppress every later measurement");
            XRDisplaySubsystem.Viewport=new Rect { x=2,width=1,height=1 };
            for(int failure=0;failure<3;failure++)
            {
                Time.unscaledTime+=2.1f; Tick();
                FinalBlitPass.ExecutePass(raster,new FinalBlitPass.PassData(),new RTHandle { rt=source },new RTHandle(),new UniversalCameraData { camera=camera });
                RenderPipelineManager.End(camera);
            }
            XRDisplaySubsystem.Viewport=null; Time.unscaledTime+=2.1f; Tick();
            FinalBlitPass.ExecutePass(raster,new FinalBlitPass.PassData(),new RTHandle { rt=source },new RTHandle(),new UniversalCameraData { camera=camera });
            RenderPipelineManager.End(camera);
            Check(AsyncGPUReadback.Requests==beforeInvalidView+2 && SteamLinkCoverageBridge.Current.CanAcceptOrdinaryRgba
                && TestLog.Messages.Exists(message=>message.Contains("measurement stopped (4/4)")),
                "four preflight failures bound diagnostics without disabling native passthrough or retrying indefinitely");
            NativePassthrough.Shutdown();
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
            Check(camera.backgroundColor==Color.black && !pipeline.m_AllowPostProcessAlphaOutput,
                "unconfirmed HMD keeps opaque black and restores URP settings without a green transition");
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
            OpenVrSubmissionChecks.Run(Check);
            OpenVrOriginChecks.Run(Check);
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
