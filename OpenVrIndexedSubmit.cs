#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GreenScreen
{
    // Public OpenVR ABI: IVRCompositor_026 (SDK 1.14.15), 51 virtual methods,
    // Submit slot 5; FnTable:IVRCompositor_028 (SDK 2.2.3), array submit slot 6.
    // Own one object's vptr through an atomic compare/exchange. No executable
    // instructions, global method tables, provider data or disk files are patched.
    internal sealed class OpenVrIndexedSubmit : OpenVrSubmission.IBackend
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Texture { internal IntPtr Handle; internal int Type, ColourSpace; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Shape
        {
            internal uint Width, Height, Mips, Slices, Format, Samples, Quality, Usage, Bind, Cpu, Misc;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetInterface([MarshalAs(UnmanagedType.LPStr)] string name, out int error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int Submit(IntPtr self, int eye, ref Texture texture, IntPtr bounds, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        internal delegate int IndexedSubmit(int eye, ref Texture texture, uint slice, IntPtr bounds, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetShape(IntPtr self, out Shape shape);

        private readonly IntPtr _object, _originalTable, _table;
        private readonly Submit _original, _callback;
        private readonly IndexedSubmit _indexed;
        private readonly OpenVrNativeSettings _provider;
        private IntPtr _source0, _source1;
        private SteamLinkCoverageBridge.Snapshot _grant;
        private long _left, _right;
        private int _lastError, _fault;
        // Native callers may have fetched an owned vptr/callback before detach.
        // Retain this tiny table and delegate until process exit; never free a
        // callback that an in-flight graphics call can still enter.
        private static OpenVrIndexedSubmit _lifetime;

        private OpenVrIndexedSubmit(OpenVrNativeSettings provider, IntPtr instance, IndexedSubmit indexed)
        {
            _provider = provider; _object = instance; _indexed = indexed;
            if (!OpenVrNativeSettings.Region(instance, 8, IntPtr.Zero, true))
                throw new NotSupportedException("The OpenVR interface object is not writable.");
            _originalTable = Marshal.ReadIntPtr(instance);
            if (!OpenVrNativeSettings.Region(IntPtr.Subtract(_originalTable, 8), 52 * 8, IntPtr.Zero, false))
                throw new NotSupportedException("The OpenVR interface table is unavailable.");
            IntPtr submit = Marshal.ReadIntPtr(_originalTable, 5 * 8);
            if (!OpenVrNativeSettings.Region(submit, 1, IntPtr.Zero, false, true))
                throw new NotSupportedException("The OpenVR Submit entry is unavailable.");
            _original = Marshal.GetDelegateForFunctionPointer<Submit>(submit);
            _callback = Forward;
            // Preserve MSVC's complete-object/RTTI locator preceding the public
            // method table as well as every documented virtual entry.
            _table = IntPtr.Add(Marshal.AllocHGlobal(52 * 8), 8);
            Marshal.WriteIntPtr(_table, -8, Marshal.ReadIntPtr(_originalTable, -8));
            for (int i = 0; i < 51; i++) Marshal.WriteIntPtr(_table, i * 8, Marshal.ReadIntPtr(_originalTable, i * 8));
            Marshal.WriteIntPtr(_table, 5 * 8, Marshal.GetFunctionPointerForDelegate(_callback));
        }

        internal static OpenVrSubmission.IBackend Open()
        {
            var provider = OpenVrNativeSettings.Open();
            IntPtr api = GetModuleHandle("openvr_api.dll");
            IntPtr getter = api == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(api, "VR_GetGenericInterface");
            if (getter == IntPtr.Zero) throw new NotSupportedException("The game's initialized OpenVR API is unavailable.");
            var get = Marshal.GetDelegateForFunctionPointer<GetInterface>(getter);
            IntPtr instance = get("IVRCompositor_026", out int error);
            if (instance == IntPtr.Zero || error != 0) throw new NotSupportedException("IVRCompositor_026 is unavailable.");
            IntPtr functions = get("FnTable:IVRCompositor_028", out error);
            if (error != 0 || !OpenVrNativeSettings.Region(functions, 7 * 8, IntPtr.Zero, false))
                throw new NotSupportedException("SteamVR does not provide indexed array submission.");
            IntPtr entry = Marshal.ReadIntPtr(functions, 6 * 8);
            if (!OpenVrNativeSettings.Region(entry, 1, IntPtr.Zero, false, true))
                throw new NotSupportedException("OpenVR's indexed Submit entry is unavailable.");
            if (_lifetime == null)
                _lifetime = new OpenVrIndexedSubmit(provider, instance, Marshal.GetDelegateForFunctionPointer<IndexedSubmit>(entry));
            else if (_lifetime._object != instance || !_lifetime._provider.Current)
                throw new InvalidOperationException("The initialized OpenVR interface was replaced.");
            _lifetime.Attach();
            return _lifetime;
        }

        private unsafe void Attach()
        {
            IntPtr previous = Interlocked.CompareExchange(ref *(IntPtr*)_object, _table, _originalTable);
            if (previous != _originalTable && previous != _table)
                throw new InvalidOperationException("Another owner changed the OpenVR interface.");
        }

        public bool Refresh()
        {
            if (Volatile.Read(ref _fault) != 0) throw new InvalidOperationException("Indexed Submit rejected an eye texture (error " + _lastError + ").");
            if (!_provider.Current || Marshal.ReadIntPtr(_object) != _table)
                throw new InvalidOperationException("OpenVR provider/interface ownership changed.");
            if (!_provider.Sources(out IntPtr first, out IntPtr second)) return false;
            if (first != _source0 || second != _source1)
            {
                Deactivate(); Validate(first); Validate(second);
                Volatile.Write(ref _source0, first); Volatile.Write(ref _source1, second);
            }
            return true;
        }

        private static void Validate(IntPtr texture)
        {
            IntPtr table = Marshal.ReadIntPtr(texture);
            var get = Marshal.GetDelegateForFunctionPointer<GetShape>(Marshal.ReadIntPtr(table, 10 * 8));
            get(texture, out Shape shape);
            if (shape.Width == 0 || shape.Height == 0 || shape.Width > 16384 || shape.Height > 16384
                || shape.Mips != 1 || shape.Slices != 2 || shape.Samples != 1
                || (shape.Format != 27 && shape.Format != 28 && shape.Format != 29))
                throw new NotSupportedException("The provider's D3D11 eye resource must be a resolved two-slice RGBA array.");
        }

        public void Activate(SteamLinkCoverageBridge.Snapshot grant) => Volatile.Write(ref _grant, grant);
        public void Deactivate() => Volatile.Write(ref _grant, null);

        private int Forward(IntPtr self, int eye, ref Texture texture, IntPtr bounds, uint flags)
        {
            try { return ForwardCore(self, eye, ref texture, bounds, flags); }
            catch
            {
                // Exceptions must never cross the native graphics callback.
                // Do not retry a possibly submitted eye through another API.
                Volatile.Write(ref _lastError, 102); Volatile.Write(ref _fault, 1);
                return 102;
            }
        }

        private int ForwardCore(IntPtr self, int eye, ref Texture texture, IntPtr bounds, uint flags)
        {
            var grant = Volatile.Read(ref _grant);
            if (grant == null || !grant.CanAcceptOrdinaryRgba || !ReferenceEquals(grant, SteamLinkCoverageBridge.Current)
                || Volatile.Read(ref _fault) != 0 || self != _object || eye < 0 || eye > 1 || texture.Type != 0 || flags != 0
                || (texture.Handle != Volatile.Read(ref _source0) && texture.Handle != Volatile.Read(ref _source1)))
                return _original(self, eye, ref texture, bounds, flags);
            // Same resource, RGB/alpha bytes, bounds, flags, colour space, eye,
            // graphics thread and call order. No render, resolve, copy or wait.
            int result = _indexed(eye, ref texture, (uint)eye, bounds, flags);
            Volatile.Write(ref _lastError, result);
            if (result == 0)
            {
                if (eye == 0) Interlocked.Increment(ref _left); else Interlocked.Increment(ref _right);
            }
            // Focus loss (101) is an ordinary compositor result. Unsupported
            // texture/device/format errors revoke the adapter on the cold tick.
            else if (result != 101) Volatile.Write(ref _fault, 1);
            return result;
        }

        public unsafe void Detach()
        {
            Deactivate();
            if (OpenVrNativeSettings.Region(_object, 8, IntPtr.Zero, true))
                Interlocked.CompareExchange(ref *(IntPtr*)_object, _originalTable, _table);
        }
        public string Statistics => "OpenVR indexed array submission: left=" + Interlocked.Read(ref _left)
            + "; right=" + Interlocked.Read(ref _right) + "; last-error=" + Volatile.Read(ref _lastError)
            + ". Unity single-pass targets, cameras and timing are retained.";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
    }
}
