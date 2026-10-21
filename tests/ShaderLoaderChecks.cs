// Executes the actual production bridge through native function pointers.
// Owned engine/IL2CPP ports model the three different pointer domains.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using GreenScreen;
using UnityEngine;

internal static class ShaderLoaderChecks
{
    private static int checks;
    private static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    private static void Refused<T>(Action action, string reason) where T : Exception
    {
        bool refused=false;try{action();}catch(T){refused=true;}
        Check(refused,reason);
    }
    public static unsafe void Main(string[] args)
    {
        NativeShaderFixture.Open(args[0]);
        byte[] data=new byte[4097];for(int i=0;i<data.Length;i++)data[i]=(byte)i;
        string path="assets/nativecoverage-\u00e4.shader";
#if DEMEO
        var monoBundle=UnityShaderLoader.LoadBundle(data);
        var monoShader=UnityShaderLoader.LoadShader(monoBundle,path);
        Check(monoBundle!=null&&monoShader!=null,"Mono retains its original public bundle/shader calls");
        Check(AssetBundle.GeneratedCalls==2,"Both original Mono public API calls execute once");
        Check(NativeShaderFixture.Stat(1)==0&&NativeShaderFixture.Stat(2)==0,"Mono never enters Unity6 IL2CPP native span entry points");
        GC.KeepAlive(monoBundle);GC.KeepAlive(monoShader);
#else
        fixed(char* name=path) NativeShaderFixture.Name((IntPtr)name,path.Length);
        NativeShaderFixture.Visit(CollectDuringNativeCall);
        Check(Marshal.SizeOf<UnityShaderLoader.NativeSpan>()==16
            && Marshal.OffsetOf<UnityShaderLoader.NativeSpan>("Length").ToInt32()==8,"Production span matches native x64 layout");
        Refused<ArgumentException>(()=>UnityShaderLoader.LoadBundle(null),"Null bytes are rejected before native calls");
        Refused<ArgumentException>(()=>UnityShaderLoader.LoadBundle(Array.Empty<byte>()),"Empty bytes are rejected before native calls");
        Application.unityVersion="2021.3.5f1";
        Refused<NotSupportedException>(()=>UnityShaderLoader.LoadBundle(data),"Unverified Unity version cannot use Unity6 native entry points");
        Application.unityVersion="6000.0.60f1";
        Il2CppInterop.Runtime.IL2CPP.MissingAsset=true;
        Refused<EntryPointNotFoundException>(()=>UnityShaderLoader.LoadBundle(data),"Missing native asset entry point refuses the entire binding");
        Check(NativeShaderFixture.Stat(1)==0,"A partial resolution never calls the native memory loader");
        Il2CppInterop.Runtime.IL2CPP.MissingAsset=false;

        NativeShaderFixture.Reset(0);
        var bundle=UnityShaderLoader.LoadBundle(data);
        Check(bundle!=null && bundle.Pointer==new IntPtr(0x400110),"A high-bit Unity handle is resolved to the IL2CPP object before wrapping");
        var shader=UnityShaderLoader.LoadShader(bundle,path);
        Check(shader!=null && shader.Pointer==new IntPtr(0x400220),"Native shader handle becomes a rooted typed shader wrapper");
        Check(NativeShaderFixture.Stat(1)==1&&NativeShaderFixture.Stat(2)==1&&NativeShaderFixture.Stat(4)==2,
            "Both real native function pointers execute once with forced GC inside each call");
        Check(NativeShaderFixture.Stat(0)==0,"Native fixture verifies pinned bytes, UTF16 path, native self pointer and live bundle/type roots");
        Check(NativeShaderFixture.Stat(3)==0,"Real Unity-owned handles are never freed by the bridge");
        Check(AssetBundle.GeneratedCalls==0,"Broken generated Span wrappers are never called");
        Refused<ArgumentNullException>(()=>UnityShaderLoader.LoadShader(null,path),"A missing bundle is refused");
        Refused<ArgumentException>(()=>UnityShaderLoader.LoadShader(bundle,""),"An empty asset path is refused");

        NativeShaderFixture.Reset(1);
        Check(UnityShaderLoader.LoadBundle(data)==null && NativeShaderFixture.Stat(0)==0,"Native null bundle remains a clean failure");
        NativeShaderFixture.Reset(2);
        Refused<InvalidDataException>(()=>UnityShaderLoader.LoadBundle(data),"An unavailable GC-handle target cannot become an object pointer");
        Check(NativeShaderFixture.Stat(3)==0,"Unverified handle is not freed or guessed");
        NativeShaderFixture.Reset(3);
        Check(UnityShaderLoader.LoadBundle(data)==null && NativeShaderFixture.Stat(3)==1 && NativeShaderFixture.Stat(0)==0,
            "Only Unity's fake-null handle is freed, exactly once");
        NativeShaderFixture.Reset(4);
        Refused<InvalidDataException>(()=>UnityShaderLoader.LoadBundle(data),"Unexpected native object type is refused");
        Check(NativeShaderFixture.Stat(3)==0,"A live wrong-type handle remains owned by the engine");
        NativeShaderFixture.Reset(5);
        Check(UnityShaderLoader.LoadShader(bundle,path)==null && NativeShaderFixture.Stat(0)==0,"Native missing shader remains null");
        NativeShaderFixture.Reset(6);
        Refused<InvalidDataException>(()=>UnityShaderLoader.LoadShader(bundle,path),"Retired Unity native self pointer is refused before calling asset load");
        Check(NativeShaderFixture.Stat(2)==0,"Retired bundle is never passed to native shader loading");
        NativeShaderFixture.Reset(0);
        Check(UnityShaderLoader.LoadShader(bundle,path)!=null && NativeShaderFixture.Stat(0)==0,"A later valid load still uses the checked native bridge");
        GC.KeepAlive(bundle);GC.KeepAlive(shader);
#endif
        Console.WriteLine("Native Unity6 shader loader checks passed: "+checks);
    }
    private static void CollectDuringNativeCall() { GC.Collect();GC.WaitForPendingFinalizers();GC.Collect(); }
}

internal static class NativeShaderFixture
{
    private static IntPtr library;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Visitor();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetVisitor(Visitor value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SetName(IntPtr name,int length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SetMode(int mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Status(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr PointerCall(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int IdCall(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void FreeCall(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RootCall(IntPtr value,int delta);
    internal static SetName Name;internal static SetMode Reset;internal static Status Stat;
    internal static PointerCall Target,Class,Cached;internal static IdCall Id;internal static FreeCall Free;internal static RootCall Root;
    private static SetVisitor setVisitor;private static Visitor visitor;
    internal static T Bind<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
    internal static void Open(string path)
    {
        library=NativeLibrary.Load(path);Name=Bind<SetName>("shader_test_name");Reset=Bind<SetMode>("shader_test_reset");Stat=Bind<Status>("shader_test_stat");
        Target=Bind<PointerCall>("shader_test_target");Class=Bind<PointerCall>("shader_test_class");Cached=Bind<PointerCall>("shader_test_cached");
        Id=Bind<IdCall>("shader_test_id");Free=Bind<FreeCall>("shader_test_free");Root=Bind<RootCall>("shader_test_root");
        setVisitor=Bind<SetVisitor>("shader_test_visit");
    }
    internal static void Visit(Visitor value) { visitor=value;setVisitor(value); }
}

namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        public IntPtr Pointer { get; }
        public Il2CppObjectBase(IntPtr pointer) { Pointer=pointer;NativeShaderFixture.Root(pointer,1); }
        ~Il2CppObjectBase() { NativeShaderFixture.Root(Pointer,-1); }
    }
}
namespace Il2CppInterop.Runtime
{
    public static class IL2CPP
    {
        public static bool MissingAsset;
        public static T ResolveICall<T>(string name) where T:Delegate
        {
            if(name=="UnityEngine.AssetBundle::LoadFromMemory_Internal_Injected")return NativeShaderFixture.Bind<T>("shader_test_memory");
            if(name=="UnityEngine.AssetBundle::LoadAsset_Internal_Injected"&&!MissingAsset)return NativeShaderFixture.Bind<T>("shader_test_asset");
            throw new EntryPointNotFoundException(name);
        }
        public static IntPtr il2cpp_gchandle_get_target(IntPtr handle)=>NativeShaderFixture.Target(handle);
        public static void il2cpp_gchandle_free(IntPtr handle)=>NativeShaderFixture.Free(handle);
        public static IntPtr il2cpp_object_get_class(IntPtr pointer)=>NativeShaderFixture.Class(pointer);
        public static bool il2cpp_class_is_assignable_from(IntPtr expected,IntPtr actual)=>expected==actual;
    }
    public static class Il2CppType { public static Il2CppSystem.Type Of<T>()=>new(); }
    public static class Il2CppClassPointerStore<T>
    { public static IntPtr NativeClassPtr=>typeof(T)==typeof(UnityEngine.AssetBundle)?new IntPtr(0x501):new IntPtr(0x502); }
}
namespace Il2CppSystem
{
    public class Type:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { public Type():base(new IntPtr(0x700033)){} }
}
namespace UnityEngine.Bindings
{
    [StructLayout(LayoutKind.Explicit)]
    public struct ManagedSpanWrapper { [FieldOffset(0)]public IntPtr begin;[FieldOffset(8)]public int length; }
}
namespace UnityEngine
{
    public static class Application { public static string unityVersion="6000.0.60f1"; }
    public class Object:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public Object(IntPtr pointer):base(pointer){}
        public int GetInstanceID()=>NativeShaderFixture.Id(Pointer);
        public IntPtr m_CachedPtr=>NativeShaderFixture.Cached(Pointer);
    }
    public class Shader:Object { public Shader(IntPtr pointer):base(pointer){} }
    public class AssetBundle:Object
    {
        public static int GeneratedCalls;
        public AssetBundle(IntPtr pointer):base(pointer){}
        public static AssetBundle LoadFromMemory(byte[] bytes)
        {
            GeneratedCalls++;
#if DEMEO
            return new AssetBundle(new IntPtr(0x400110));
#else
            throw new Exception("Owned reproduction of broken generated span wrapper");
#endif
        }
        public T LoadAsset<T>(string path) where T:Object
        {
            GeneratedCalls++;
#if DEMEO
            return (T)(Object)new Shader(new IntPtr(0x400220));
#else
            throw new Exception("Generated shader wrapper must not execute");
#endif
        }
        public static IntPtr LoadFromMemory_Internal_Injected(ref Bindings.ManagedSpanWrapper span,uint crc)=>throw new Exception("Generated wrapper must not execute");
        public static IntPtr LoadAsset_Internal_Injected(IntPtr self,ref Bindings.ManagedSpanWrapper span,Il2CppSystem.Type type)=>throw new Exception("Generated wrapper must not execute");
    }
}
