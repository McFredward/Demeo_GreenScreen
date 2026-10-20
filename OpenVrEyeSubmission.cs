#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GreenScreen
{
    // Public OpenVR ABI: IVRCompositor_026 (SDK 1.14.15), 51 virtual methods,
    // Submit slot 5. Submit separate native 2D eyes while Unity retains its array.
    // Own one object's vptr through an atomic compare/exchange. No executable
    // instructions, global method tables, provider data or disk files are patched.
    internal sealed class OpenVrEyeSubmission : OpenVrSubmission.IBackend
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Texture { internal IntPtr Handle; internal int Type, ColourSpace; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetInterface([MarshalAs(UnmanagedType.LPStr)] string name, out int error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int Submit(IntPtr self, int eye, ref Texture texture, IntPtr bounds, uint flags);
        private readonly IntPtr _object, _originalTable, _table;
        private readonly Submit _original, _callback;
        private readonly OpenVrNativeSettings _provider;
        private OpenVrEyeTextures _eyes;
        private int _inflight, _retiring;
        private SteamLinkCoverageBridge.Snapshot _grant;
        private long _left, _right;
        private int _lastError, _fault;
        // Native callers may have fetched an owned vptr/callback before detach.
        // Retain this tiny table and delegate until process exit; never free a
        // callback that an in-flight graphics call can still enter.
        private static OpenVrEyeSubmission _lifetime;

        private OpenVrEyeSubmission(OpenVrNativeSettings provider, IntPtr instance)
        {
            _provider = provider; _object = instance;
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
            if (_lifetime == null)
                _lifetime = new OpenVrEyeSubmission(provider, instance);
            else if (_lifetime._object != instance || !_lifetime._provider.Current)
                throw new InvalidOperationException("The initialized OpenVR interface was replaced.");
            _lifetime.Collect();
            if (_lifetime._retiring != 0) throw new InvalidOperationException("The previous eye submit is still completing.");
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
            if (Volatile.Read(ref _fault) != 0) throw new InvalidOperationException("2D Submit rejected an eye texture (error " + _lastError + ").");
            if (!_provider.Current || Marshal.ReadIntPtr(_object) != _table)
                throw new InvalidOperationException("OpenVR provider/interface ownership changed.");
            if (!_provider.Sources(out IntPtr first, out IntPtr second)) return false;
            if (_eyes == null || !_eyes.Matches(first, second))
            {
                Deactivate();
                // A callback increments before reading its grant. Once the
                // grant is null and no callback is in flight, no new callback
                // can borrow the old GPU resources. Never wait on graphics.
                if (Volatile.Read(ref _inflight) != 0) return false;
                var next = OpenVrEyeTextures.Create(first, second);
                var old = _eyes; Volatile.Write(ref _eyes, next); old?.Dispose();
            }
            return true;
        }

        internal static void CollectRetired() => _lifetime?.Collect();
        private void Collect()
        {
            if (Volatile.Read(ref _retiring) == 0 || Volatile.Read(ref _inflight) != 0) return;
            var old = _eyes; Volatile.Write(ref _eyes, null); old?.Dispose();
            Volatile.Write(ref _retiring, 0);
        }

        public void Activate(SteamLinkCoverageBridge.Snapshot grant) => Volatile.Write(ref _grant, grant);
        public void Deactivate() => Volatile.Write(ref _grant, null);

        private int Forward(IntPtr self, int eye, ref Texture texture, IntPtr bounds, uint flags)
        {
            Interlocked.Increment(ref _inflight);
            try { return ForwardCore(self, eye, ref texture, bounds, flags); }
            catch
            {
                // Exceptions must never cross the native graphics callback.
                // Do not retry a possibly submitted eye through another API.
                Volatile.Write(ref _lastError, 102); Volatile.Write(ref _fault, 1);
                return 102;
            }
            finally { Interlocked.Decrement(ref _inflight); }
        }

        private int ForwardCore(IntPtr self, int eye, ref Texture texture, IntPtr bounds, uint flags)
        {
            var grant = Volatile.Read(ref _grant);
            var eyes = Volatile.Read(ref _eyes);
            if (grant == null || !grant.CanAcceptOrdinaryRgba || !ReferenceEquals(grant, SteamLinkCoverageBridge.Current)
                || Volatile.Read(ref _fault) != 0 || self != _object || eye < 0 || eye > 1 || texture.Type != 0 || flags != 0
                || eyes == null || !eyes.Contains(texture.Handle))
                return _original(self, eye, ref texture, bounds, flags);
            // GPU copies preserve all RGBA bytes in the current eye and stage.
            // Original Submit receives a resolved 2D image, never the array.
            // No binding, camera, timing, bounds or colour-space changes.
            Texture separate = texture;
            separate.Handle = eyes.Copy(texture.Handle, eye);
            int result = _original(self, eye, ref separate, bounds, flags);
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
            Volatile.Write(ref _retiring, 1); Collect();
        }
        public string Statistics => "OpenVR separate 2D eye submission: left=" + Interlocked.Read(ref _left)
            + "; right=" + Interlocked.Read(ref _right) + "; last-error=" + Volatile.Read(ref _lastError)
            + ". Unity single-pass targets, cameras and timing are retained." + (_eyes?.Statistics ?? "");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
    }
}
