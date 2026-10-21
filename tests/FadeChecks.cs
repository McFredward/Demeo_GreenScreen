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
        Console.WriteLine("Production passthrough fade with real Harmony: "+checks+" checks PASS; "+typeof(Controller).FullName+"; controlled Unity ports, no headset.");
    }
}
namespace GreenScreen { internal static class Utils { internal static void LogInfo(string message) { Console.WriteLine(message); } } }
namespace UnityEngine
{
    public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public string name; public bool Destroyed;
        public static void Destroy(Object value) { if(!ReferenceEquals(value,null)) value.Destroyed=true; }
        public static bool operator ==(Object a,Object b) => (ReferenceEquals(a,null)||a.Destroyed) ? ReferenceEquals(b,null)||b.Destroyed : ReferenceEquals(a,b);
        public static bool operator !=(Object a,Object b) => !(a==b);
        public override bool Equals(object value) => ReferenceEquals(this,value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public struct Color { public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}public static Color black=>new Color(0,0,0,1); }
    public enum HideFlags {HideAndDontSave}
    public class Shader : Object {public bool isSupported=true;}
    public class Material : Object
    {
        public Shader shader;public Color color;public HideFlags hideFlags;public bool enableInstancing;public int renderQueue;
        private readonly Dictionary<string,float> floats=new Dictionary<string,float>();
        public Material(Shader shader){this.shader=shader;}
        public int passCount=>shader.name=="Hidden/Demeo/NativeFade"?1:2;
        public int FindPass(string name)=>name=="ROOM_FADE"?0:-1;
        public bool HasProperty(string name)=>name=="_Color";
        public void SetFloat(string name,float value)=>floats[name]=value;
        public float GetFloat(string name)=>floats[name];
    }
    public class MeshRenderer : Object {public Material sharedMaterial;public bool Multiple;public Material[] sharedMaterials=>Multiple?new[]{sharedMaterial,sharedMaterial}:new[]{sharedMaterial};}
    public class AssetBundle : Object
    {
        public static bool Reject;public static int LoadAttempts;
        public static AssetBundle LoadFromMemory(byte[] raw){LoadAttempts++;return Reject?null:new AssetBundle();}
        public T LoadAsset<T>(string name) where T:Object =>(T)LoadAsset(name,typeof(T));
        public Object LoadAsset(string name,Type type)=>new Shader{name="Hidden/Demeo/NativeFade"};
        public void Unload(bool all){}
    }
}
namespace Il2CppInterop.Runtime.InteropTypes {public class Il2CppObjectBase {public T Cast<T>() where T:Il2CppObjectBase =>(T)this;} }
namespace Il2CppInterop.Runtime {public static class Il2CppType {public static Type Of<T>()=>typeof(T);} }
#if DEMEOBATTLEMARKED
namespace Jade.GameView
{
    public class FadeToBlackView
    {
        public MeshRenderer Renderer; public Material Primary,Alternate;
        public MeshRenderer FadeCubeRenderer=>Renderer;
        public Material BlackFadeMaterial=>Primary;
        public Material TransparentFadeMaterial=>Alternate;
    }
    public class FadeToBlackPresenter
    {
        public FadeToBlackView view {get;}
        public int Calls;
        public FadeToBlackPresenter(FadeToBlackView view){this.view=view;}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetFadePlaneColor(float r,float g,float b,float a){Calls++;}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Finish(Action completed){SetFadePlaneColor(0,0,0,1);completed();}
    }
}
#else
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
        public Material fadeMaterial {get;}
#endif
        public int Calls;
        public CameraFadeController(CameraFadeView view){view.Bind();cameraFadeView=view;fadeMaterial=view.Primary;}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnUpdate(float value){Calls++;fadeMaterial.color=new Color(0,0,0,value);}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetFadedOut(){Calls++;fadeMaterial.color=Color.black;}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Finish(Action completed){SetFadedOut();completed();}
    }
}
#endif
