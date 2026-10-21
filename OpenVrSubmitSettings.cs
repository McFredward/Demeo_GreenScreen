#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GreenScreen
{
    // IVRSettings_003, 11 public virtual methods, GetBool slot 5. SteamVR's
    // optional scene-image filter is RGB processing, not a coverage contract.
    // Override its query only inside this thread's admitted original Submit.
    // No Set*, persisted settings, executable patch or process-wide option.
    internal sealed class OpenVrSubmitSettings
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool GetBool(IntPtr self, IntPtr section, IntPtr key, IntPtr error);
        private readonly IntPtr _object, _originalTable, _table;
        private readonly GetBool _original, _callback;
        private long _overrides;
        // Retain the tiny table/callback for native callers which fetched it
        // before detachment, just like the compositor interface lease.
        private static OpenVrSubmitSettings _lifetime;
        [ThreadStatic] private static Scope _scope;

        internal struct Scope
        {
            internal OpenVrSubmitSettings Owner;
            internal SteamLinkCoverageBridge.Snapshot Grant;
        }

        private OpenVrSubmitSettings(IntPtr instance)
        {
            _object = instance;
            if (!OpenVrNativeSettings.Region(instance, 8, IntPtr.Zero, true))
                throw new NotSupportedException("The OpenVR settings object is unavailable.");
            _originalTable = Marshal.ReadIntPtr(instance);
            if (!OpenVrNativeSettings.Region(IntPtr.Subtract(_originalTable, 8), 12 * 8, IntPtr.Zero, false))
                throw new NotSupportedException("The OpenVR settings table is unavailable.");
            IntPtr getter = Marshal.ReadIntPtr(_originalTable, 5 * 8);
            if (!OpenVrNativeSettings.Region(getter, 1, IntPtr.Zero, false, true))
                throw new NotSupportedException("The OpenVR settings getter is unavailable.");
            _original = Marshal.GetDelegateForFunctionPointer<GetBool>(getter);
            _callback = Forward;
            _table = IntPtr.Add(Marshal.AllocHGlobal(12 * 8), 8);
            Marshal.WriteIntPtr(_table, -8, Marshal.ReadIntPtr(_originalTable, -8));
            for (int i = 0; i < 11; i++) Marshal.WriteIntPtr(_table, i * 8, Marshal.ReadIntPtr(_originalTable, i * 8));
            Marshal.WriteIntPtr(_table, 5 * 8, Marshal.GetFunctionPointerForDelegate(_callback));
        }

        internal static OpenVrSubmitSettings Open(IntPtr instance)
        {
            if (_lifetime == null) _lifetime = new OpenVrSubmitSettings(instance);
            else if (_lifetime._object != instance)
                throw new InvalidOperationException("The OpenVR settings object was replaced.");
            return _lifetime;
        }

        internal bool Current => OpenVrNativeSettings.Region(_object, 8, IntPtr.Zero, true)
            && Marshal.ReadIntPtr(_object) == _table;
        internal unsafe void Attach()
        {
            IntPtr previous = Interlocked.CompareExchange(ref *(IntPtr*)_object, _table, _originalTable);
            if (previous != _originalTable && previous != _table)
                throw new InvalidOperationException("Another owner changed the OpenVR settings interface.");
        }
        internal unsafe void Detach()
        {
            if (OpenVrNativeSettings.Region(_object, 8, IntPtr.Zero, true))
                Interlocked.CompareExchange(ref *(IntPtr*)_object, _originalTable, _table);
        }

        internal Scope Enter(SteamLinkCoverageBridge.Snapshot grant)
        {
            Scope previous = _scope;
            _scope = new Scope { Owner = this, Grant = grant };
            return previous;
        }
        internal void Exit(Scope previous) => _scope = previous;

        private bool Forward(IntPtr self, IntPtr section, IntPtr key, IntPtr error)
        {
            var grant = _scope.Grant;
            if (self == _object && ReferenceEquals(_scope.Owner, this) && grant != null
                && grant.CanAcceptOrdinaryRgba && ReferenceEquals(grant, SteamLinkCoverageBridge.Current)
                && EqualsAscii(section, "steamvr") && EqualsAscii(key, "allowSupersampleFiltering"))
            {
                // Public VRSettingsError_None. Preserve native handling of
                // every other key, errors, null error outputs and threads.
                if (error != IntPtr.Zero) Marshal.WriteInt32(error, 0);
                Interlocked.Increment(ref _overrides);
                return false;
            }
            return _original(self, section, key, error);
        }

        private static unsafe bool EqualsAscii(IntPtr pointer, string value)
        {
            if (!OpenVrNativeSettings.Region(pointer, value.Length + 1, IntPtr.Zero, false)) return false;
            byte* bytes = (byte*)pointer;
            for (int i = 0; i < value.Length; i++) if (bytes[i] != value[i]) return false;
            return bytes[value.Length] == 0;
        }
        internal string Statistics => "; Submit-only supersample filter bypasses=" + Interlocked.Read(ref _overrides);
    }
}
