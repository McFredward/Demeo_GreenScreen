#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GreenScreen
{
    // This adapter contains only ABI metadata for the supplied OpenVR provider.
    // No provider binary, instructions or replacement implementation is shipped.
    // The provider remains in its original single-pass mode throughout.
    internal sealed class OpenVrNativeSettings
    {
        internal const int FileBytes = 377856, ImageBytes = 401408;
        internal const string FileHash = "0ebad5a4ca7f4aa9b94b5f0128dcbc7bb3139fee317b91a0ec2636b448b4d702";
        internal const string ModeExport = "?GetStereoRenderingMode@UserProjectSettings@@SA?AW4EVRStereoRenderingModes@@XZ";
        internal const string DestroyExport = "?DestroyEyeTextures@OpenVRDisplayProvider@@AEAAXPEAX@Z";
        // The getter, settings word, provider context and display handle belong
        // to this exact file. Refuse every other build instead of pattern scans.
        internal const int ModeGetterRva = 0x6a20, ModeRva = 0x59010;
        internal const int ContextRva = 0x5a690, HandleRva = 0x5a688, DestroyRva = 0xa3e0;
        internal const int ProviderOffset = 24, CreatedOffset = 0xb1, SinglePassOffset = 0xb2;
        private readonly IntPtr _module, _context, _provider, _handle;
        private readonly GetMode _getMode;
        private readonly NativeTexture _texture;
        internal const string TextureExport = "?GetNativeEyeTexture@OpenVRDisplayProvider@@AEAAPEAXHH@Z";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetMode();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr NativeTexture(IntPtr provider, int stage, int eye);

        private OpenVrNativeSettings(IntPtr module, IntPtr context, IntPtr provider, IntPtr handle, IntPtr getter, IntPtr texture)
        {
            _module = module; _context = context; _provider = provider; _handle = handle;
            _getMode = Marshal.GetDelegateForFunctionPointer<GetMode>(getter);
            _texture = Marshal.GetDelegateForFunctionPointer<NativeTexture>(texture);
        }

        internal static bool Pin(byte[] bytes)
        {
            if (bytes == null || bytes.Length != FileBytes) return false;
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() == FileHash;
        }

        internal static OpenVrNativeSettings Open()
        {
            if (IntPtr.Size != 8) throw new NotSupportedException("OpenVR indexed submission requires Windows x64.");
            IntPtr module = GetModuleHandle("XRSDKOpenVR.dll");
            if (module == IntPtr.Zero) throw new NotSupportedException("The game's OpenVR provider is not loaded.");
            var path = new StringBuilder(32768);
            uint length = GetModuleFileName(module, path, path.Capacity);
            if (length == 0 || length >= path.Capacity) throw new IOException("The loaded OpenVR provider path is unavailable.");
            using (var stream = new FileStream(path.ToString(), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length != FileBytes) throw new NotSupportedException("Unknown OpenVR provider build.");
                var bytes = new byte[FileBytes]; int used = 0;
                while (used != bytes.Length)
                {
                    int read = stream.Read(bytes, used, bytes.Length - used);
                    if (read == 0) throw new EndOfStreamException();
                    used += read;
                }
                if (!Pin(bytes)) throw new NotSupportedException("Unknown OpenVR provider SHA-256; submission was not changed.");
            }
            IntPtr getter = GetProcAddress(module, ModeExport), destroy = GetProcAddress(module, DestroyExport);
            IntPtr texture = GetProcAddress(module, TextureExport);
            if (getter != IntPtr.Add(module, ModeGetterRva) || destroy != IntPtr.Add(module, DestroyRva)
                || texture != IntPtr.Add(module, 0xa7c0) || !Region(texture, 8, module, false, true)
                || !Region(getter, 8, module, false, true) || !Region(destroy, 8, module, false, true)
                || !Region(IntPtr.Add(module, ModeRva), 2, module, true)
                || !Region(IntPtr.Add(module, ContextRva), 8, module, false)
                || !Region(IntPtr.Add(module, HandleRva), 8, module, false))
                throw new NotSupportedException("The loaded OpenVR provider does not match its pinned ABI.");
            IntPtr context = Marshal.ReadIntPtr(module, ContextRva);
            if (!Region(context, ProviderOffset + 8, IntPtr.Zero, false)) throw new InvalidOperationException("OpenVR provider context is unavailable.");
            IntPtr provider = Marshal.ReadIntPtr(context, ProviderOffset), handle = Marshal.ReadIntPtr(module, HandleRva);
            if (handle == IntPtr.Zero || !Region(provider, SinglePassOffset + 1, IntPtr.Zero, true))
                throw new InvalidOperationException("OpenVR display is not initialized.");
            var result = new OpenVrNativeSettings(module, context, provider, handle, getter, texture);
            if (result.Mode > 1) throw new NotSupportedException("Unknown OpenVR stereo mode.");
            return result;
        }

        public bool Current => GetModuleHandle("XRSDKOpenVR.dll") == _module
            && Region(IntPtr.Add(_module, ContextRva), 8, _module, false)
            && Region(IntPtr.Add(_module, HandleRva), 8, _module, false)
            && Marshal.ReadIntPtr(_module, ContextRva) == _context
            && Region(_context, ProviderOffset + 8, IntPtr.Zero, false)
            && Marshal.ReadIntPtr(_context, ProviderOffset) == _provider
            && Marshal.ReadIntPtr(_module, HandleRva) == _handle
            && Region(_provider, SinglePassOffset + 1, IntPtr.Zero, true);

        public ushort Mode
        {
            get
            {
                if (!Current || !Region(IntPtr.Add(_module, ModeRva), 2, _module, true))
                    throw new InvalidOperationException("OpenVR display ownership changed.");
                ushort value = unchecked((ushort)Marshal.ReadInt16(_module, ModeRva));
                if (_getMode() != value) throw new InvalidOperationException("OpenVR settings getter disagrees with its storage.");
                return value;
            }
        }
        public bool ProviderSinglePass => Current && Marshal.ReadByte(_provider, SinglePassOffset) != 0;

        // Read the two provider stages. No native settings, textures, camera
        // policy or subsystem lifecycle are changed by this adapter.
        internal bool Sources(out IntPtr first, out IntPtr second)
        {
            first = second = IntPtr.Zero;
            if (!Current || Mode != 1 || !ProviderSinglePass)
                throw new InvalidOperationException("The original OpenVR single-pass owner changed.");
            if (Marshal.ReadByte(_provider, CreatedOffset) == 0) return false;
            first = _texture(_provider, 0, 0); second = _texture(_provider, 1, 0);
            return first != IntPtr.Zero && second != IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Memory
        {
            public IntPtr Base, Allocation;
            public uint AllocationProtect;
            public UIntPtr Bytes;
            public uint State, Protect, Type;
        }
        internal static bool Region(IntPtr address, int bytes, IntPtr allocation, bool writable, bool executable = false)
        {
            if (address == IntPtr.Zero || bytes < 1 || VirtualQuery(address, out Memory info, (UIntPtr)Marshal.SizeOf<Memory>()) == UIntPtr.Zero
                || info.State != 0x1000 || (info.Protect & 0x100) != 0 || allocation != IntPtr.Zero && info.Allocation != allocation)
                return false;
            uint access = info.Protect & 0xff;
            bool permitted = executable ? access == 0x20 || access == 0x40 || access == 0x80
                : writable ? access == 0x04 || access == 0x08 : access == 0x02 || access == 0x04 || access == 0x08;
            ulong offset = unchecked((ulong)(address.ToInt64() - info.Base.ToInt64()));
            return permitted && offset <= info.Bytes.ToUInt64() && (ulong)bytes <= info.Bytes.ToUInt64() - offset;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleFileNameW")]
        private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern UIntPtr VirtualQuery(IntPtr address, out Memory information, UIntPtr size);
    }
}
