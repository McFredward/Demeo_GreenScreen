#nullable disable
using System;
using UnityEngine;
#if !DEMEO
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
#endif

namespace GreenScreen
{
    internal static class UnityShaderLoader
    {
        internal static AssetBundle LoadBundle(byte[] bytes)
        {
#if DEMEO
            return AssetBundle.LoadFromMemory(bytes);
#else
            if (bytes == null || bytes.Length == 0) throw new ArgumentException("Empty shader bundle.", nameof(bytes));
            Bind();
            unsafe
            {
                // Unity6 accepts a byte span, not an IL2CPP array/object pointer.
                // Pin the actual .NET bytes only for this synchronous call.
                fixed (byte* data = bytes)
                {
                    var span = new NativeSpan { Begin = (IntPtr)data, Length = bytes.Length };
                    return Target(_memory(ref span, 0), pointer => new AssetBundle(pointer));
                }
            }
#endif
        }

        internal static Shader LoadShader(AssetBundle bundle, string asset)
        {
#if DEMEO
            return bundle.LoadAsset<Shader>(asset);
#else
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            if (string.IsNullOrEmpty(asset)) throw new ArgumentException("Empty shader asset path.", nameof(asset));
            Bind();
            var type = Il2CppType.Of<Shader>();
            try
            {
                unsafe
                {
                    fixed (char* name = asset)
                    {
                        // String spans count UTF16 characters. The instance is
                        // Unity's native C++ object, distinct from bundle.Pointer.
                        var span = new NativeSpan { Begin = (IntPtr)name, Length = asset.Length };
                        IntPtr self = bundle.m_CachedPtr;
                        if (self == IntPtr.Zero) throw new InvalidDataException("Shader bundle native object was retired.");
                        return Target(_asset(self, ref span, type.Pointer), pointer => new Shader(pointer));
                    }
                }
            }
            finally
            {
                GC.KeepAlive(type);
                GC.KeepAlive(bundle);
            }
#endif
        }

#if !DEMEO
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeSpan { internal IntPtr Begin; internal int Length; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr LoadMemory(ref NativeSpan bytes, uint crc);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr LoadAsset(IntPtr self, ref NativeSpan name, IntPtr type);
        private static LoadMemory _memory;
        private static LoadAsset _asset;

        private static void Bind()
        {
            if (_memory != null && _asset != null) return;
            if (IntPtr.Size != 8 || !Application.unityVersion.StartsWith("6000.", StringComparison.Ordinal))
                throw new NotSupportedException("The IL2CPP shader bundle loader requires the checked Unity6 x64 bindings.");
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            Type span = typeof(UnityEngine.Object).Assembly.GetType("UnityEngine.Bindings.ManagedSpanWrapper");
            if (span == null || !span.IsValueType || Marshal.SizeOf(span) != 16
                || span.GetField("begin")?.FieldType != typeof(IntPtr) || span.GetField("length")?.FieldType != typeof(int)
                || Marshal.OffsetOf(span, "begin").ToInt32() != 0 || Marshal.OffsetOf(span, "length").ToInt32() != 8)
                throw new NotSupportedException("Unknown Unity6 shader bundle span layout.");
            var memory = typeof(AssetBundle).GetMethod("LoadFromMemory_Internal_Injected", flags, null,
                new[] { span.MakeByRefType(), typeof(uint) }, null);
            var asset = typeof(AssetBundle).GetMethod("LoadAsset_Internal_Injected", flags, null,
                new[] { typeof(IntPtr), span.MakeByRefType(), typeof(Il2CppSystem.Type) }, null);
            if (memory?.ReturnType != typeof(IntPtr) || asset?.ReturnType != typeof(IntPtr))
                throw new NotSupportedException("Unknown Unity6 shader bundle injected signatures.");
            // Publish only after both exact entry points resolved. Nothing is
            // patched, and no generated Span/ReadOnlySpan wrapper is invoked.
            var memoryCall = IL2CPP.ResolveICall<LoadMemory>("UnityEngine.AssetBundle::LoadFromMemory_Internal_Injected");
            var assetCall = IL2CPP.ResolveICall<LoadAsset>("UnityEngine.AssetBundle::LoadAsset_Internal_Injected");
            _asset = assetCall; _memory = memoryCall;
        }

        private static T Target<T>(IntPtr handle, Func<IntPtr, T> wrap) where T : UnityEngine.Object
        {
            if (handle == IntPtr.Zero) return null;
            // The injected return is a Unity-owned GC HANDLE, not an object
            // address. A typed wrapper creates its own independent strong root.
            IntPtr pointer = IL2CPP.il2cpp_gchandle_get_target(handle);
            if (pointer == IntPtr.Zero) throw new InvalidDataException("Unity6 shader load returned an unavailable GC-handle target.");
            T target = wrap(pointer);
            if (target.GetInstanceID() == 0)
            {
                // Unity's fake-null case has no native owner for this handle.
                IL2CPP.il2cpp_gchandle_free(handle);
                return null;
            }
            if (!IL2CPP.il2cpp_class_is_assignable_from(Il2CppClassPointerStore<T>.NativeClassPtr, IL2CPP.il2cpp_object_get_class(pointer)))
                throw new InvalidDataException("Unity6 shader load returned an unexpected object type.");
            // Real returned handles remain Unity-owned, matching its Unmarshal
            // contract. Never free them while the engine object is alive.
            return target;
        }
#endif
    }
}
