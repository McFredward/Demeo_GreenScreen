#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;

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
        private readonly OpenVrSubmitSettings _settings;
        private OpenVrEyeTextures _eyes;
        private int _inflight, _retiring;
        private SteamLinkCoverageBridge.Snapshot _grant;
        private long _left, _right;
        private long _requestedCrop, _wholeSubmits, _cropSubmits, _cropPending, _unknownViewport;
        private long _nextCropPreparation;
        private SteamLinkCoverageBridge.Snapshot _pairGrant;
        private long _originRevision, _discontinuousPairs;
        private int _discontinuousRemaining;
        private uint _pairFlags;
        private int _lastError, _fault;
        // Native callers may have fetched an owned vptr/callback before detach.
        // Retain this tiny table and delegate until process exit; never free a
        // callback that an in-flight graphics call can still enter.
        private static OpenVrEyeSubmission _lifetime;

        private OpenVrEyeSubmission(OpenVrNativeSettings provider, IntPtr instance, OpenVrSubmitSettings settings)
        {
            _provider = provider; _object = instance; _settings = settings;
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
            IntPtr settingsObject = get("IVRSettings_003", out error);
            if (settingsObject == IntPtr.Zero || error != 0) throw new NotSupportedException("IVRSettings_003 is unavailable.");
            var settings = OpenVrSubmitSettings.Open(settingsObject);
            if (_lifetime == null)
                _lifetime = new OpenVrEyeSubmission(provider, instance, settings);
            else if (_lifetime._object != instance || !_lifetime._provider.Current)
                throw new InvalidOperationException("The initialized OpenVR interface was replaced.");
            _lifetime.Collect();
            if (_lifetime._retiring != 0) throw new InvalidOperationException("The previous eye submit is still completing.");
            settings.Attach();
            try { _lifetime.Attach(); }
            catch { settings.Detach(); throw; }
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
            if (!_provider.Current || Marshal.ReadIntPtr(_object) != _table || !_settings.Current)
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
                Interlocked.Exchange(ref _requestedCrop, 0);
            }
            long requested = Interlocked.Read(ref _requestedCrop);
            if (Volatile.Read(ref _inflight) == 0) _eyes.CollectCrop();
            long now = Stopwatch.GetTimestamp();
            if (!_eyes.HasCrop(requested) && !_eyes.CropRetirementPending && now >= _nextCropPreparation)
            {
                _eyes.PrepareCrop(requested);
                _nextCropPreparation = now + Stopwatch.Frequency * 2;
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
                || Volatile.Read(ref _fault) != 0 || self != _object || eye < 0 || eye > 1 || texture.Type != 0
                || texture.ColourSpace != 0 || flags != 0
                || eyes == null || !eyes.Contains(texture.Handle))
                return _original(self, eye, ref texture, bounds, flags);
            // GPU copies preserve all RGBA bytes in the current eye and stage.
            // Original Submit receives a resolved 2D image, never the array.
            // A pixel-aligned submitted crop becomes an equivalent whole 2D
            // texture. No render, resample, camera, FOV or pixel-value change.
            bool known = eyes.Viewport(bounds, out long crop);
            bool canonical = known && eyes.HasCrop(crop);
            if (!known) Interlocked.Increment(ref _unknownViewport);
            Texture separate = texture;
            separate.Handle = eyes.Copy(texture.Handle, eye, crop, ref canonical, known);
            if (known && !canonical) { Interlocked.Exchange(ref _requestedCrop, crop); Interlocked.Increment(ref _cropPending); }
            separate.ColourSpace = 1; // Gamma: these are Unity's already encoded SDR bytes.
            if (eye == 0)
            {
                long revision = OpenVrOrigin.Revision;
                if (!ReferenceEquals(_pairGrant, grant))
                {
                    _pairGrant = grant; _originRevision = revision; _discontinuousRemaining = 0;
                }
                else if (_originRevision != revision)
                { _originRevision = revision; _discontinuousRemaining = 2; }
                // Public Submit_FrameDiscontinuity. Both eyes of this native
                // pair retain the same flag, including a notification between
                // their callbacks. No timing call or pose is replaced.
                _pairFlags = _discontinuousRemaining > 0 ? 0x20u : 0;
            }
            uint submitFlags = ReferenceEquals(_pairGrant, grant) ? _pairFlags : 0;
            var previous = _settings.Enter(grant);
            int result;
            try { result = _original(self, eye, ref separate, canonical ? IntPtr.Zero : bounds, submitFlags); }
            finally { _settings.Exit(previous); }
            if (canonical)
            {
                Interlocked.Increment(ref _wholeSubmits);
                if (crop != 0) Interlocked.Increment(ref _cropSubmits);
            }
            if (eye == 1 && submitFlags != 0)
            { _discontinuousRemaining--; Interlocked.Increment(ref _discontinuousPairs); }
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
            _settings.Detach();
            Volatile.Write(ref _retiring, 1); Collect();
        }
        public string Statistics => "OpenVR separate 2D eye submission: left=" + Interlocked.Read(ref _left)
            + "; right=" + Interlocked.Read(ref _right) + "; last-error=" + Volatile.Read(ref _lastError)
            + "; origin notifications=" + OpenVrOrigin.Revision + "; discontinuous pairs=" + Interlocked.Read(ref _discontinuousPairs)
            + "; whole-texture/cropped=" + Interlocked.Read(ref _wholeSubmits) + "/" + Interlocked.Read(ref _cropSubmits)
            + "; viewport pending/unknown=" + Interlocked.Read(ref _cropPending) + "/" + Interlocked.Read(ref _unknownViewport)
            + _settings.Statistics
            + ". Unity single-pass targets, cameras and timing are retained." + (_eyes?.Statistics ?? "");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
    }
}
