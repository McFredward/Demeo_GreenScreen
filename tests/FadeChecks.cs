// Real Harmony dispatch against owned game/Unity boundary ports. No game code.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GreenScreen;
using UnityEngine;
#if DEMEOBATTLEMARKED
using Controller = Jade.GameView.FadeToBlackPresenter;
using View = Jade.GameView.FadeToBlackView;
#else
using Controller = Boardgame.Rendering.CameraFadeController;
using View = Boardgame.Rendering.CameraFadeView;
#endif
internal static class FadeChecks
{
    private static int checks;
    private static void Check(bool value,string why) { checks++; if(!value) throw new Exception(why); }
    private static View View(Material material) => new View { Renderer = new MeshRenderer { sharedMaterial=material }, Primary=material,
        Alternate=new Material(new Shader {name="original alternate"}) };
    private static Material Original() => new Material(new Shader {name="original game fade"}) { color=Color.black, renderQueue=3980 };
    private static void Fade(Controller controller,float opacity)
    {
#if DEMEOBATTLEMARKED
        controller.SetFadePlaneColor(0,0,0,opacity);
#else
        controller.OnUpdate(opacity);
#endif
    }
    public static void Main()
    {
        Console.WriteLine("Starting actual Harmony fade dispatch: "+typeof(Controller).FullName);
        var source=Original();var view=View(source);var game=new Controller(view);
        PassthroughFade.Initialize();
        Fade(game,1);
        Check(game.Calls==1 && view.Renderer.sharedMaterial==source && AssetBundle.LoadAttempts==0,"No grant leaves actual game callback/material unchanged and loads no shader.");
        PassthroughFade.SetActive(true);
        Material owned=view.Renderer.sharedMaterial;
        Check(owned!=source && owned.shader.name=="Hidden/Demeo/NativeFade" && owned.enableInstancing,"Live permission creates only the identified owned fade material.");
        Check(owned.renderQueue==3980 && owned.GetFloat("_PassthroughFade")==1,"Keep original draw order and exact semantic opacity.");
        foreach(float alpha in new[] {1f,.75f,.5f,0f})
        {
            int before=game.Calls;Fade(game,alpha);PassthroughFade.Refresh();
            Check(game.Calls==before+1,"Actual Harmony postfix never skips or repeats the original game update.");
            Check(view.Renderer.sharedMaterial==owned && owned.GetFloat("_PassthroughFade")==alpha,"Every native fade value reaches its owned current renderer.");
#if !DEMEOBATTLEMARKED
            Check(source.color.a==alpha && source.shader.name=="original game fade","Game keeps its own animated material and shader.");
#endif
        }
        int completions=0;game.Finish(()=>completions++);
        Check(completions==1 && view.Renderer.sharedMaterial.GetFloat("_PassthroughFade")==1,
            "Original loading completion callback runs once while the room fade is active.");
        PassthroughFade.SetActive(false);
        Check(view.Renderer.sharedMaterial==source && owned.Destroyed,"Permission loss restores the game's material and retires only our copy.");
        Fade(game,.4f);Check(view.Renderer.sharedMaterial==source,"Disabled updates preserve original game rendering.");
        PassthroughFade.SetActive(true);Check(view.Renderer.sharedMaterial.GetFloat("_PassthroughFade")==.4f,"Reactivation uses the current game fade, never an old opacity.");
        var fadeBundle=AssetBundle.Last;
        Check((fadeBundle.hideFlags&HideFlags.DontUnloadUnusedAsset)!=0
            && (fadeBundle.Asset.hideFlags&HideFlags.DontUnloadUnusedAsset)!=0,"Owned fade assets are retained across native scene cleanup.");
        int fadeLoads=AssetBundle.LoadAttempts;
        UnityEngine.Object.Destroy(fadeBundle.Asset);
        PassthroughFade.Refresh();
        Check(AssetBundle.LoadAttempts==fadeLoads+1 && view.Renderer.sharedMaterial.shader!=null
            && view.Renderer.sharedMaterial.shader==AssetBundle.Last.Asset,
            "Explicitly retired fade shader is reloaded and rebound on the existing owned renderer.");
        Fade(game,float.NaN);Check(view.Renderer.sharedMaterial==source,"Invalid semantic alpha refuses the override.");
        Fade(game,.6f);Check(view.Renderer.sharedMaterial!=source,"A later valid fade resumes normally.");
#if DEMEOBATTLEMARKED
        view.Renderer.sharedMaterial=view.Alternate;Fade(game,.3f);
        Check(view.Renderer.sharedMaterial!=view.Alternate && view.Renderer.sharedMaterial.GetFloat("_PassthroughFade")==.3f,"Follow the game's known transparent/black material switch.");
        PassthroughFade.SetActive(false);Check(view.Renderer.sharedMaterial==view.Alternate,"Restore the latest actual game material choice.");
        PassthroughFade.SetActive(true);
#endif
        Material foreign=Original();view.Renderer.sharedMaterial=foreign;Fade(game,.8f);PassthroughFade.Refresh();PassthroughFade.SetActive(false);
        Check(view.Renderer.sharedMaterial==foreign,"A foreign owner's replacement is never overwritten or restored.");
        PassthroughFade.SetActive(true);
        var lateSource=Original();var lateView=View(lateSource);var late=new Controller(lateView);Fade(late,1);
        Check(lateView.Renderer.sharedMaterial!=lateSource,"A later scene's identified fade is admitted while native permission remains live.");
        PassthroughFade.Shutdown();
        Check(lateView.Renderer.sharedMaterial==lateSource && view.Renderer.sharedMaterial==foreign,"Unload restores owned renderers and preserves foreign owners.");
        int attempts=AssetBundle.LoadAttempts;Fade(late,.7f);
        Check(lateView.Renderer.sharedMaterial==lateSource && AssetBundle.LoadAttempts==attempts,"Actual Harmony observer is removed on unload.");
        PassthroughFade.Initialize();AssetBundle.Reject=true;
        var failedSource=Original();var failedView=View(failedSource);var failed=new Controller(failedView);Fade(failed,1);PassthroughFade.SetActive(true);
        Check(failed.Calls==1 && failedView.Renderer.sharedMaterial==failedSource,"Rejected native shader preserves game callbacks and original material.");
        attempts=AssetBundle.LoadAttempts;for(int i=0;i<5;i++){Fade(failed,.5f);PassthroughFade.Refresh();}
        Check(AssetBundle.LoadAttempts==attempts,"A failed shader is not retried every frame.");
        PassthroughFade.Shutdown();AssetBundle.Reject=false;
        PassthroughFade.Initialize();
        var badSource=Original();var badView=View(badSource);badView.Renderer.Multiple=true;
        var bad=new Controller(badView);Fade(bad,1);PassthroughFade.SetActive(true);
        Check(badView.Renderer.sharedMaterial==badSource,"Unproven multi-material renderers stay original.");
        PassthroughFade.Shutdown();
#if !DEMEOBATTLEMARKED
        TimelineChecks();
        LoadingChecks();
        ConstructorChecks();
#else
        EarlyBattlemarkedLoading();
#endif
        Console.WriteLine("Production passthrough fade with real Harmony: "+checks+" checks PASS; "+typeof(Controller).FullName+"; controlled Unity ports, no headset.");
    }
#if DEMEOBATTLEMARKED
    private static void EarlyBattlemarkedLoading()
    {
        PassthroughFade.Initialize();
        var source=Original();var view=View(source);
        var image=new UnityEngine.UI.Image { color=Color.black,raycastTarget=true };
        view.FadeImage=image;
        var controller=new Controller(view,new Jade.GameView.FadeConfig(),true);
        Check(HarmonyLib.Harmony.GetPatchInfo(typeof(Controller).GetConstructor(new[]{typeof(Jade.GameView.FadeToBlackView),typeof(Jade.GameView.FadeConfig),typeof(bool)}))==null,
            "No managed constructor fallback is mistaken for a native IL2CPP detour.");
        Check(image.color.a==1 && view.Renderer.sharedMaterial==source,"Constructing the loading view cannot enable transparency before source permission.");
        controller.Tick();
        PassthroughFade.SetActive(true);
        Check(controller.Calls==0 && view.Renderer.sharedMaterial!=source
            && view.Renderer.sharedMaterial.GetFloat("_PassthroughFade")==1,
            "Native Tick captures the serialized startup fade without a tween callback.");
        Check(image.color.a==0 && image.raycastTarget && image.enabled,
            "Exact black FadeImage becomes transparent immediately without disabling the loading UI.");
        controller.UpdateView(.5f);
        Check(image.color.a==0,"The original UpdateView may animate the UI background without reintroducing a black frame.");
        PassthroughFade.SetActive(false);
        Check(image.color.a==.5f && view.Renderer.sharedMaterial==source,"Opt-out restores the latest original UI fade value.");
        image.sprite=new UnityEngine.Object();image.color=Color.black;PassthroughFade.SetActive(true);controller.UpdateView(1);
        Check(image.color.a==1,"Loading artwork is retained, even on the identified fade image.");
        PassthroughFade.Shutdown();
    }
#endif
#if !DEMEOBATTLEMARKED
    private static void ConstructorChecks()
    {
        PassthroughFade.Initialize();PassthroughFade.SetActive(true);
        var source=Original();var view=View(source);var first=new Controller(view);
#if DEMEO
        Check(view.Renderer.sharedMaterial!=source && view.Renderer.sharedMaterial.shader.name=="Hidden/Demeo/NativeFade",
            "A controller created with an already-black loading material is captured without an OnUpdate/SetFadedOut callback.");
        Material oldOwned=view.Renderer.sharedMaterial;
        Check(!oldOwned.HasProperty("_Color"),"The fixture replacement matches the actual shader's absence of the game's _Color property.");
        var second=new Controller(view);
        Check(second.Source==source && oldOwned.Destroyed,
            "A reused view's native constructor receives its original game material, never our shader without _Color.");
        second.AnimateNative(.65f);PassthroughFade.Refresh();
        Check(view.Renderer.sharedMaterial.GetFloat("_PassthroughFade")==.65f,
            "Direct native material animation is observed before rendering even when helper OnUpdate is bypassed.");
#else
        Check(view.Renderer.sharedMaterial==source && HarmonyLib.Harmony.GetPatchInfo(typeof(Controller).GetConstructor(new[]{typeof(View)}))==null,
            "The native constructor is never detoured through its allocating generated wrapper.");
        Fade(first,1);Material oldOwned=view.Renderer.sharedMaterial;
        Check(!oldOwned.HasProperty("_Color"),"The native replacement has no game animation property.");
        var second=new Controller(view);
        Check(second.Source==oldOwned,"The owned port reproduces a new native controller caching our renderer replacement.");
        second.OnUpdate(.65f);PassthroughFade.Refresh();
        Check(second.Source==source && view.Renderer.sharedMaterial==oldOwned && oldOwned.GetFloat("_PassthroughFade")==.65f,
            "Before a native fade writes, only its exact cached owned reference is redirected to the original animation material.");
#endif
        int callbacks=0;second.Finish(()=>callbacks++);
        Check(callbacks==1 && view.Renderer.sharedMaterial.GetFloat("_PassthroughFade")==1,
            "The replacement controller retains its original loading completion.");
        PassthroughFade.SetActive(false);
        Check(view.Renderer.sharedMaterial==source,"Permission loss after controller reuse restores the real game shader.");
        PassthroughFade.SetActive(true);PassthroughFade.Shutdown();
        Check(view.Renderer.sharedMaterial==source,"Unload releases the reused controller's replacement.");
        var afterUnload=new Controller(view);
        Check(view.Renderer.sharedMaterial==source && afterUnload.Source==source,"Unload removes native constructor hooks.");
        PassthroughFade.Initialize();PassthroughFade.SetActive(true);
        var nativeSource=new Material(new Shader {name="Utility/CameraFading"}) {color=Color.black,renderQueue=3980};
        var nativeView=View(nativeSource);nativeView.Bind();var camera=new Camera {Extra=nativeView};
        PassthroughFade.TrackCamera(camera);
        Check(nativeView.Renderer.sharedMaterial!=nativeSource && nativeView.Renderer.sharedMaterial.GetFloat("_PassthroughFade")==1,
            "The admitted camera's pre-existing native fade is discovered even with no constructor/helper callback.");
        Material nativeOwned=nativeView.Renderer.sharedMaterial;
        PassthroughFade.TrackCamera(camera);nativeSource.color=new Color(0,0,0,.45f);PassthroughFade.Refresh();
        Check(nativeView.Renderer.sharedMaterial==nativeOwned && nativeOwned.GetFloat("_PassthroughFade")==.45f,
            "Camera discovery is idempotent and direct native animation still updates the owned fade.");
        var foreign=Original();var foreignView=View(foreign);foreignView.Bind();
        PassthroughFade.TrackCamera(new Camera {Extra=foreignView});
        Check(foreignView.Renderer.sharedMaterial==foreign,"An unrelated shader cannot enter the camera-only fade fallback.");
        PassthroughFade.Shutdown();
        Check(nativeView.Renderer.sharedMaterial==nativeSource,"Camera-only discovery restores the original game material on unload.");
    }

    private static void LoadingChecks()
    {
        PassthroughFade.Initialize();
        var image=new UnityEngine.UI.Image { color=Color.black, raycastTarget=true };
        var spinner=new UnityEngine.UI.Image { color=new Color(1,1,1,1) };
        var art=new UnityEngine.UI.Image { color=new Color(1,1,1,1), sprite=new UnityEngine.Object() };
        var root=new Transform();
        root.Children["Background"]=new Transform { Extra=image };
        root.Children["Spinner"]=new Transform { Extra=spinner };
        root.Children["Images/Image (1)"]=new Transform { Extra=art };
        var view=new PVPLoadingScreenView {transform=root};
        view.Awake();
        Check(image.color.a==1 && view.Calls==1,"A registered loading view stays unchanged before native source admission.");
        PassthroughFade.SetActive(true);
        Check(image.color.a==0 && image.raycastTarget && image.enabled && view.Active,
            "Native admission hides only the black loading background while retaining UI behavior and input blocking.");
        Check(spinner.color.a==1 && art.color.a==1,"Loading artwork and spinner alpha are never modified.");
        int shown=0;var presenter=new PVPLoadingScreenPresenter(view);presenter.Completed=()=>shown++;
        presenter.Show(false);view.OnEnable();
        Check(presenter.Calls==1 && shown==1 && view.Calls==2 && image.color.a==0,
            "Real Harmony observers preserve loading Show and OnEnable callbacks exactly once.");
        image.color=new Color(0,0,0,.35f);PassthroughFade.Refresh();
        Check(image.color.a==0,"A new game-written black opacity is followed during an owned source.");
        PassthroughFade.SetActive(false);
        Check(image.color.a==.35f,"Admission loss restores the latest original game opacity rather than an old snapshot.");
        PassthroughFade.SetActive(true);image.color=new Color(1,0,0,1);PassthroughFade.Refresh();PassthroughFade.SetActive(false);
        Check(image.color.r==1 && image.color.a==1,"A foreign coloured replacement is never hidden or overwritten on restore.");
        image.color=Color.black;PassthroughFade.SetActive(true);image.sprite=new UnityEngine.Object();PassthroughFade.Refresh();
        Check(image.color.a==1,"Adding artwork during ownership restores its original visible tint.");
        image.sprite=null;image.color=Color.black;PassthroughFade.Refresh();
        Check(image.color.a==0,"Removing artwork allows the known black background again.");
        var lateImage=new UnityEngine.UI.Image {color=Color.black};var lateRoot=new Transform();
        lateRoot.Children["Background"]=new Transform {Extra=lateImage};
        var lateView=new PVPLoadingScreenView {transform=lateRoot};
        new PVPLoadingScreenPresenter(lateView).Show(false);
        Check(lateImage.color.a==0,"Presenter Show discovers a pre-existing loading view even if Awake was missed.");
        var vanillaImage=new UnityEngine.UI.Image {color=Color.black};var vanillaRoot=new Transform();
        vanillaRoot.Children["Background"]=new Transform {Extra=vanillaImage};
        var vanilla=new VanillaLoadingScreenView {transform=vanillaRoot};vanilla.Awake();
        Check(vanillaImage.color.a==0,"The equivalent Vanilla loading view shares the scoped correction.");
        var textured=new UnityEngine.UI.Image {color=Color.black,sprite=new UnityEngine.Object()};
        var texturedRoot=new Transform();texturedRoot.Children["Background"]=new Transform {Extra=textured};
        new PVPLoadingScreenView {transform=texturedRoot}.Awake();
        Check(textured.color.a==1,"A textured Background with different semantics remains untouched.");
        PassthroughFade.Shutdown();
        Check(image.color.a==1 && lateImage.color.a==1 && vanillaImage.color.a==1,"Unload restores every live owned loading image.");
        image.color=Color.black;presenter.Show(false);view.OnEnable();
        Check(image.color.a==1,"Unload removes both loading view and presenter observers.");
        PassthroughFade.Initialize();PassthroughFade.SetActive(true);view.OnEnable();
        Check(image.color.a==0,"Reload can reacquire the original loading background without stale owner state.");
        PassthroughFade.Shutdown();
    }

    private static Material TimelineMaterial()
    {
        var material = new Material(new Shader { name="Universal Render Pipeline/Unlit" }) { renderQueue=3000, color=Color.black };
        foreach (var entry in new Dictionary<string,float> { {"_Surface",1}, {"_ZWrite",0}, {"_SrcBlend",5}, {"_DstBlend",10},
            {"_SrcBlendAlpha",1}, {"_DstBlendAlpha",10}, {"_Cull",2} }) material.SetFloat(entry.Key,entry.Value);
        return material;
    }
    private static void TimelineChecks()
    {
        PassthroughFade.Initialize();
        Material first=TimelineMaterial(), second=TimelineMaterial();
        var left=new MeshRenderer { sharedMaterial=first };
        var right=new MeshRenderer { sharedMaterial=second };
        var root=new Transform();
        root.Children["Intro/invert_sphere"]=new Transform { Renderer=left };
        root.Children["Intro/invert_sphere_end"]=new Transform { Renderer=right };
        var intro=new Boardgame.Intro.IntroController { transform=root };
        intro.Awake();
        Check(intro.Calls==1 && left.sharedMaterial==first && right.sharedMaterial==second,"Discovery retains original intro callbacks and opt-out materials");
        PassthroughFade.SetActive(true);
        Material owned=left.sharedMaterial;
        Check(owned!=first && right.sharedMaterial!=second,"Both exact Timeline spheres receive independent owned copies");
        Check(owned.shader==first.shader && owned.renderQueue==3000 && owned.GetFloat("_Cull")==2 && owned.GetFloat("_ZWrite")==0,
            "Timeline shader, culling, depth and draw order remain original");
        Check(owned.GetFloat("_SrcBlend")==0 && owned.GetFloat("_SrcBlendAlpha")==0 && owned.GetFloat("_DstBlend")==10 && owned.GetFloat("_DstBlendAlpha")==10,
            "Both RGB and alpha use Zero / OneMinusSrcAlpha");
        Check(first.GetFloat("_SrcBlend")==5 && first.GetFloat("_SrcBlendAlpha")==1,"Original shared game material is not modified");
        foreach(float alpha in new[] {0f,.25f,.5f,1f})
        {
            left.Animated["_BaseColor"]=new Color(0,0,0,alpha);
            PassthroughFade.Refresh();
            Check(left.sharedMaterial==owned && left.Animated["_BaseColor"].a==alpha,"Animated GPU property block is retained, including partial and zero opacity");
        }
        intro.Awake();
        Check(intro.Calls==2 && left.sharedMaterial==owned,"Repeated discovery does not replace the owned material or skip the original callback");
        PassthroughFade.SetActive(false);
        Check(left.sharedMaterial==first && right.sharedMaterial==second && owned.Destroyed,"Opt-out restores both original materials");
        PassthroughFade.SetActive(true);
        Material foreign=TimelineMaterial();left.sharedMaterial=foreign;
        PassthroughFade.Refresh();PassthroughFade.SetActive(false);
        Check(left.sharedMaterial==foreign,"Another owner's material is retained on permission loss");
        PassthroughFade.Shutdown();
        Check(right.sharedMaterial==second,"Unload restores the remaining owned Timeline material");
        PassthroughFade.Initialize();
        var badRoot=new Transform();var bad=new MeshRenderer { sharedMaterial=Original() };
        badRoot.Children["Intro/invert_sphere"]=new Transform { Renderer=bad };
        var multiple=new MeshRenderer {sharedMaterial=TimelineMaterial(),Multiple=true};
        badRoot.Children["Intro/invert_sphere_end"]=new Transform {Renderer=multiple};
        var unknown=new Boardgame.Intro.IntroController {transform=badRoot};unknown.Awake();PassthroughFade.SetActive(true);
        Check(bad.sharedMaterial.shader.name=="original game fade" && multiple.sharedMaterial.GetFloat("_SrcBlend")==5,
            "Unknown shaders and multi-material Timeline renderers are refused");
        PassthroughFade.Shutdown();
    }
#endif
}
namespace GreenScreen { internal static class Utils {  internal static void LogDebug(string m) => LogInfo(m); internal static void LogWarning(string m) => LogInfo(m); internal static void LogError(string m) => LogInfo(m); internal static void LogInfo(string message) { Console.WriteLine(message); } } }
namespace UnityEngine
{
    public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public string name; public bool Destroyed; public HideFlags hideFlags;
        public static void Destroy(Object value) { if(!ReferenceEquals(value,null)) value.Destroyed=true; }
        public static bool operator ==(Object a,Object b) => (ReferenceEquals(a,null)||a.Destroyed) ? ReferenceEquals(b,null)||b.Destroyed : ReferenceEquals(a,b);
        public static bool operator !=(Object a,Object b) => !(a==b);
        public override bool Equals(object value) => ReferenceEquals(this,value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public struct Color { public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}public static Color black=>new Color(0,0,0,1); }
    [Flags] public enum HideFlags {None=0,DontUnloadUnusedAsset=32,HideAndDontSave=61}
    public class Component : Object
    {
        public Transform transform; public object Extra;
        public T GetComponent<T>() where T:class => Extra as T;
    }
    public class Camera : Component { }
    public class Transform : Object
    {
        public readonly Dictionary<string,Transform> Children=new Dictionary<string,Transform>();
        public MeshRenderer Renderer;
        public Object Extra;
        public Transform Find(string path)=>Children.TryGetValue(path,out var child)?child:null;
        public T GetComponent<T>() where T:Object =>Renderer as T ?? Extra as T;
    }
    public class Shader : Object {public bool isSupported=true;}
    public class Material : Object
    {
        public Shader shader;public Color color;public bool enableInstancing;public int renderQueue;
        private readonly Dictionary<string,float> floats=new Dictionary<string,float>();
        public Material(Shader shader){this.shader=shader;}
        public Material(Material source){shader=source.shader;color=source.color;renderQueue=source.renderQueue;enableInstancing=source.enableInstancing;foreach(var entry in source.floats)floats[entry.Key]=entry.Value;}
        public int passCount=>shader.name=="Hidden/Demeo/NativeFade"?1:2;
        public int FindPass(string name)=>name=="ROOM_FADE"?0:-1;
        public bool HasProperty(string name)=>shader.name=="Hidden/Demeo/NativeFade" ? name=="_PassthroughFade" : name=="_Color" || name=="_BaseColor" || floats.ContainsKey(name);
        public void SetFloat(string name,float value)=>floats[name]=value;
        public float GetFloat(string name)=>floats[name];
    }
    public class MeshRenderer : Object {public Material sharedMaterial;public bool Multiple;public readonly Dictionary<string,Color> Animated=new Dictionary<string,Color>();public Material[] sharedMaterials=>Multiple?new[]{sharedMaterial,sharedMaterial}:new[]{sharedMaterial};}
    public class AssetBundle : Object
    {
        public static AssetBundle Last; public Shader Asset;
        public static bool Reject;public static int LoadAttempts;
        public static AssetBundle LoadFromMemory(byte[] raw){LoadAttempts++;return Reject?null:Last=new AssetBundle();}
        public T LoadAsset<T>(string name) where T:Object =>(T)LoadAsset(name,typeof(T));
        public Object LoadAsset(string name,Type type)=>Asset=new Shader{name="Hidden/Demeo/NativeFade"};
        public void Unload(bool all){if(all)Destroy(Asset);Destroy(this);}
    }
}
namespace Il2CppInterop.Runtime.InteropTypes {public class Il2CppObjectBase {public T Cast<T>() where T:Il2CppObjectBase =>(T)this;} }
namespace Il2CppInterop.Runtime {public static class Il2CppType {public static Type Of<T>()=>typeof(T);} }
#if DEMEOBATTLEMARKED
namespace Jade.GameView
{
    public class FadeToBlackView
    {
        public UnityEngine.UI.Image FadeImage {get;set;}
        public MeshRenderer Renderer; public Material Primary,Alternate;
        public MeshRenderer FadeCubeRenderer=>Renderer;
        public Material BlackFadeMaterial=>Primary;
        public Material TransparentFadeMaterial=>Alternate;
    }
    public class FadeToBlackPresenter
    {
        public FadeToBlackView view {get;}
        public int Calls;
        private float alpha=1;
        public FadeToBlackPresenter(FadeToBlackView view):this(view,new FadeConfig(),true){}
        [MethodImpl(MethodImplOptions.NoInlining)] public FadeToBlackPresenter(FadeToBlackView view,FadeConfig config,bool showTextOnStartup){this.view=view;}
        [MethodImpl(MethodImplOptions.NoInlining)] public void Tick() { }
        public float GetFadePlaneAlpha()=>alpha;
        [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateView(float value) {if(view.FadeImage!=null)view.FadeImage.color=new Color(0,0,0,value);}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetFadePlaneColor(float r,float g,float b,float a){Calls++;alpha=a;}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Finish(Action completed){SetFadePlaneColor(0,0,0,1);completed();}
    }
    public class FadeConfig { }
}
#else
namespace Boardgame.Intro
{
    public sealed class IntroController : UnityEngine.Component
    {
        public int Calls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Awake(){Calls++;}
    }
}
namespace Boardgame.Rendering
{
    public class CameraFadeView
    {
        public MeshRenderer Renderer; public Material Primary,Alternate;
#if DEMEO
        public MeshRenderer faderMeshRenderer;
        public void Bind()=>faderMeshRenderer=Renderer;
#else
        public MeshRenderer faderMeshRenderer=>Renderer;
        public void Bind(){}
#endif
    }
    public class CameraFadeController
    {
#if DEMEO
        private readonly CameraFadeView cameraFadeView;
        private readonly Material fadeMaterial;
#else
        public CameraFadeView cameraFadeView {get;}
        public Material fadeMaterial {get;set;}
#endif
        public int Calls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public CameraFadeController(CameraFadeView view){view.Bind();cameraFadeView=view;fadeMaterial=view.Renderer.sharedMaterial;}
        public Material Source=>fadeMaterial;
        public void AnimateNative(float value){fadeMaterial.color=new Color(0,0,0,value);}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnUpdate(float value){Calls++;fadeMaterial.color=new Color(0,0,0,value);}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetFadedOut(){Calls++;fadeMaterial.color=Color.black;}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Finish(Action completed){SetFadedOut();completed();}
    }
}
#endif

namespace UnityEngine.UI
{
    public class Image : UnityEngine.Object
    {
        public UnityEngine.Color color {get;set;}
        public UnityEngine.Object sprite {get;set;}
        public bool raycastTarget,enabled=true;
    }
}
#if !DEMEOBATTLEMARKED
public class PVPLoadingScreenView : UnityEngine.Component
{
    public int Calls;public bool Active=true;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Awake(){Calls++;}
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnEnable(){Calls++;}
}
public class VanillaLoadingScreenView : UnityEngine.Component
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void Awake(){}
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnEnable(){}
}
public class PVPLoadingScreenPresenter
{
#if DEMEO
    private readonly PVPLoadingScreenView view;
#else
    public PVPLoadingScreenView view {get;}
#endif
    public int Calls;public Action Completed;
    public PVPLoadingScreenPresenter(PVPLoadingScreenView view){this.view=view;}
    [MethodImpl(MethodImplOptions.NoInlining)] public void Show(bool previous){Calls++;Completed?.Invoke();}
}
public class VanillaLoadingScreenPresenter
{
    public VanillaLoadingScreenView view {get;}
    public VanillaLoadingScreenPresenter(VanillaLoadingScreenView view){this.view=view;}
    [MethodImpl(MethodImplOptions.NoInlining)] public void Show(bool previous){}
}
#endif
