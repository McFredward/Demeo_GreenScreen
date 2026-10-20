#nullable disable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace GreenScreen
{
    // Own GPU storage only. No Unity API, CPU colour export, independent UV,
    // shader, render-target rebind, resolve or replacement frame is involved.
    internal sealed class OpenVrEyeTextures : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Shape
        {
            internal uint Width, Height, Mips, Slices, Format, Samples, Quality, Usage, Bind, Cpu, Misc;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Box { internal uint Left, Top, Front, Right, Bottom, Back; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Mapped { internal IntPtr Data; internal uint RowPitch, DepthPitch; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Query { internal uint Kind, Misc; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint Ref(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetDevice(IntPtr self, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetContext(IntPtr self, out IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetShape(IntPtr self, out Shape shape);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(IntPtr self, ref Shape shape, IntPtr data, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateQuery(IntPtr self, ref Query query, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyRegion(IntPtr self, IntPtr destination, uint sub, uint x, uint y, uint z, IntPtr source, uint slice, IntPtr box);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void EndQuery(IntPtr self, IntPtr query);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetData(IntPtr self, IntPtr query, out uint done, uint bytes, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Map(IntPtr self, IntPtr resource, uint sub, uint mode, uint flags, out Mapped mapped);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Unmap(IntPtr self, IntPtr resource, uint sub);

        private readonly IntPtr[] _sources = new IntPtr[2], _targets = new IntPtr[4], _staging = new IntPtr[2], _queries = new IntPtr[2];
        private sealed class CropPool
        {
            internal readonly long Key;
            internal readonly IntPtr[] Targets;
            internal CropPool(long key, IntPtr[] targets) { Key = key; Targets = targets; }
            internal void Dispose() { for (int i = 0; i < Targets.Length; i++) Release(ref Targets[i]); }
        }
        private CropPool _cropped, _retiredCrop;
        private IntPtr _device, _context;
        private CopyRegion _copy;
        private EndQuery _end;
        private GetData _get;
        private Map _map;
        private Unmap _unmap;
        private Shape _shape;
        private readonly bool[] _pending = new bool[2];
        private readonly long[] _next = new long[2], _samples = new long[2], _summaries = new long[6];
        private readonly long[] _pendingCrop = new long[2], _sampleCrop = new long[2];
        private int _readbackError;
        private bool _readback;

        private static T Method<T>(IntPtr instance, int slot) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * 8));
        private static void Release(ref IntPtr value)
        {
            IntPtr old = value; value = IntPtr.Zero;
            if (old != IntPtr.Zero) Method<Ref>(old, 2)(old);
        }

        internal static OpenVrEyeTextures Create(IntPtr first, IntPtr second)
        {
            var result = new OpenVrEyeTextures();
            try { result.Prepare(first, second); return result; }
            catch { result.Dispose(); throw; }
        }

        private void Prepare(IntPtr first, IntPtr second)
        {
            var sources = new[] { first, second };
            for (int stage = 0; stage < 2; stage++)
            {
                IntPtr source = sources[stage];
                Method<Ref>(source, 1)(source); _sources[stage] = source;
                Method<GetShape>(source, 10)(source, out Shape shape);
                if (shape.Width < 32 || shape.Height < 32 || shape.Width > 16384 || shape.Height > 16384
                    || shape.Mips != 1 || shape.Slices != 2 || shape.Samples != 1
                    || (shape.Format != 27 && shape.Format != 28 && shape.Format != 29))
                    throw new NotSupportedException("OpenVR source must be a resolved two-slice RGBA array.");
                if (stage == 0) _shape = shape;
                else if (shape.Width != _shape.Width || shape.Height != _shape.Height || shape.Format != _shape.Format)
                    throw new NotSupportedException("OpenVR stages must retain the same size and format.");
                Method<GetDevice>(source, 3)(source, out IntPtr device);
                if (_device == IntPtr.Zero) _device = device;
                else
                {
                    bool same = device == _device; Release(ref device);
                    if (!same) throw new NotSupportedException("OpenVR stages belong to different graphics devices.");
                }
            }
            Method<GetContext>(_device, 40)(_device, out _context);
            _copy = Method<CopyRegion>(_context, 46);
            _end = Method<EndQuery>(_context, 28); _get = Method<GetData>(_context, 29);
            _map = Method<Map>(_context, 14); _unmap = Method<Unmap>(_context, 15);
            var create = Method<CreateTexture>(_device, 5);
            Shape target = _shape;
            // Unity's Linear project already encoded these SDR bytes for its
            // sRGB eye view. Publish the same bytes as ordinary RGBA8/Gamma,
            // without asking OpenVR to infer semantics from a typeless/sRGB
            // resource. CopySubresourceRegion permits this format family;
            // it performs no colour conversion, including on alpha.
            target.Format = 28;
            target.Slices = 1; target.Usage = 0; target.Bind = 0x28; target.Cpu = target.Misc = 0;
            for (int i = 0; i < _targets.Length; i++)
            {
                int hr = create(_device, ref target, IntPtr.Zero, out _targets[i]);
                if (hr < 0 || _targets[i] == IntPtr.Zero) Marshal.ThrowExceptionForHR(hr < 0 ? hr : unchecked((int)0x80004005));
            }
            // Optional, bounded same-submit alpha evidence. Setup failure does
            // not disable an otherwise valid GPU eye submission.
            try
            {
                Shape staging = target; staging.Width = 32; staging.Height = 96;
                staging.Usage = 3; staging.Bind = 0; staging.Cpu = 0x20000;
                var query = new Query(); var createQuery = Method<CreateQuery>(_device, 24);
                for (int i = 0; i < 2; i++)
                {
                    int hr = create(_device, ref staging, IntPtr.Zero, out _staging[i]);
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                    hr = createQuery(_device, ref query, out _queries[i]);
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                }
                _readback = true;
            }
            catch (Exception error) { _readbackError = error.HResult; }
        }

        internal bool Matches(IntPtr first, IntPtr second) => first == _sources[0] && second == _sources[1];
        internal bool Contains(IntPtr source) => source == _sources[0] || source == _sources[1];
        internal bool Viewport(IntPtr bounds, out long crop)
        {
            crop = 0;
            if (bounds == IntPtr.Zero) return true;
            if (!OpenVrNativeSettings.Region(bounds, 16, IntPtr.Zero, false)) return false;
            return OpenVrViewport.TryPack(Marshal.PtrToStructure<OpenVrViewport.Bounds>(bounds), _shape.Width, _shape.Height, out crop);
        }
        internal bool HasCrop(long crop)
        { var pool = Volatile.Read(ref _cropped); return crop == 0 || pool != null && pool.Key == crop; }
        internal bool CropRetirementPending => _retiredCrop != null;
        internal void CollectCrop()
        { var old = _retiredCrop; _retiredCrop = null; old?.Dispose(); }

        // Cold device allocation only. Publish an immutable pool and retain its
        // predecessor until the outer adapter has no in-flight borrowers. The
        // existing full 2D handoff and live source grant remain available.
        internal void PrepareCrop(long crop)
        {
            if (HasCrop(crop)) return;
            uint x = OpenVrViewport.X(crop), y = OpenVrViewport.Y(crop);
            uint w = OpenVrViewport.Width(crop, _shape.Width), h = OpenVrViewport.Height(crop, _shape.Height);
            if (w < 32 || h < 32 || x + w > _shape.Width || y + h > _shape.Height)
                throw new NotSupportedException("The requested OpenVR viewport no longer belongs to this source.");
            Shape target = _shape; target.Width = w; target.Height = h; target.Format = 28;
            target.Slices = 1; target.Usage = 0; target.Bind = 0x28; target.Cpu = target.Misc = 0;
            var create = Method<CreateTexture>(_device, 5); var next = new IntPtr[4];
            try
            {
                for (int i = 0; i < next.Length; i++)
                {
                    int hr = create(_device, ref target, IntPtr.Zero, out next[i]);
                    if (hr < 0 || next[i] == IntPtr.Zero) Marshal.ThrowExceptionForHR(hr < 0 ? hr : unchecked((int)0x80004005));
                }
            }
            catch { for (int i = 0; i < next.Length; i++) Release(ref next[i]); throw; }
            if (_retiredCrop != null)
            { for (int i = 0; i < next.Length; i++) Release(ref next[i]); return; }
            _retiredCrop = Interlocked.Exchange(ref _cropped, new CropPool(crop, next));
        }

        internal unsafe IntPtr Copy(IntPtr source, int eye, long crop, ref bool canonical, bool validViewport)
        {
            int stage = source == _sources[0] ? 0 : 1;
            var pool = Volatile.Read(ref _cropped);
            canonical &= crop == 0 || pool != null && pool.Key == crop;
            IntPtr target = canonical && crop != 0 ? pool.Targets[stage * 2 + eye] : _targets[stage * 2 + eye];
            uint x = OpenVrViewport.X(crop), y = OpenVrViewport.Y(crop);
            uint w = OpenVrViewport.Width(crop, _shape.Width), h = OpenVrViewport.Height(crop, _shape.Height);
            Box box = new Box { Left = x, Top = y, Right = x + w, Bottom = y + h, Back = 1 };
            _copy(_context, target, 0, 0, 0, 0, source, (uint)eye,
                canonical && crop != 0 ? (IntPtr)(&box) : IntPtr.Zero);
            if (_readback && validViewport) Sample(eye, target, crop, canonical);
            return target;
        }

        private unsafe void Sample(int eye, IntPtr target, long crop, bool canonical)
        {
            if (_pending[eye])
            {
                int hr = _get(_context, _queries[eye], out uint done, 4, 1); // DONOTFLUSH
                if (hr == 1 || hr == 0 && done == 0) return;
                if (hr == 0)
                {
                    hr = _map(_context, _staging[eye], 0, 1, 0x100000, out Mapped mapped); // READ, DO_NOT_WAIT
                    if (hr == unchecked((int)0x887a000a)) return;
                    if (hr == 0)
                    {
                        try
                        {
                            if (mapped.Data == IntPtr.Zero || mapped.RowPitch < 128)
                            { Volatile.Write(ref _readbackError, unchecked((int)0x80004005)); _pending[eye] = false; return; }
                            for (int region = 0; region < 3; region++)
                            {
                                int min = 255, max = 0, zero = 0, partial = 0, sum = 0;
                                for (int y = region * 32; y < (region + 1) * 32; y++)
                                    for (int x = 0; x < 32; x++)
                                    {
                                        int a = *((byte*)mapped.Data + y * mapped.RowPitch + x * 4 + 3);
                                        min = Math.Min(min, a); max = Math.Max(max, a); sum += a;
                                        if (a == 0) zero++; else if (a != 255) partial++;
                                    }
                                long summary = (long)min | (long)(sum / 1024) << 8 | (long)max << 16 | (long)zero << 24 | (long)partial << 40;
                                Interlocked.Exchange(ref _summaries[eye * 3 + region], summary);
                            }
                            Interlocked.Exchange(ref _sampleCrop[eye], _pendingCrop[eye]);
                            Interlocked.Increment(ref _samples[eye]);
                        }
                        finally { _unmap(_context, _staging[eye], 0); }
                    }
                }
                if (hr < 0) Volatile.Write(ref _readbackError, hr);
                _pending[eye] = false;
            }
            long now = Stopwatch.GetTimestamp();
            if (now < _next[eye]) return;
            uint width = OpenVrViewport.Width(crop, _shape.Width), height = OpenVrViewport.Height(crop, _shape.Height);
            uint left = canonical ? 0 : OpenVrViewport.X(crop), top = canonical ? 0 : OpenVrViewport.Y(crop);
            for (int region = 0; region < 3; region++)
            {
                uint x = left + (width - 32) / 2;
                uint y = top + Math.Min(height - 32, Math.Max(16, height * (uint)(region + 1) / 4) - 16);
                Box box = new Box { Left = x, Top = y, Right = x + 32, Bottom = y + 32, Back = 1 };
                _copy(_context, _staging[eye], 0, 0, (uint)region * 32, 0, target, 0, (IntPtr)(&box));
            }
            _end(_context, _queries[eye]); _pendingCrop[eye] = crop; _pending[eye] = true; _next[eye] = now + Stopwatch.Frequency * 2;
        }

        internal string Statistics
        {
            get
            {
                string text = "; input DXGI format=" + _shape.Format + "; submit=RGBA8_UNORM/Gamma; exact-submit alpha samples left/right=" + Interlocked.Read(ref _samples[0]) + "/" + Interlocked.Read(ref _samples[1])
                    + "; readback-hr=0x" + Volatile.Read(ref _readbackError).ToString("x8");
                for (int eye = 0; eye < 2; eye++) for (int region = 0; region < 3; region++)
                {
                    long s = Interlocked.Read(ref _summaries[eye * 3 + region]);
                    text += "; eye " + eye + " region " + region + " alpha=" + (s & 255) + "/" + (s >> 8 & 255) + "/" + (s >> 16 & 255)
                        + ", zero/partial=" + (s >> 24 & 65535) + "/" + (s >> 40 & 65535);
                }
                for (int eye = 0; eye < 2; eye++)
                {
                    long crop = Interlocked.Read(ref _sampleCrop[eye]);
                    text += "; eye " + eye + " sampled source viewport=" + OpenVrViewport.X(crop) + "," + OpenVrViewport.Y(crop)
                        + "," + OpenVrViewport.Width(crop, _shape.Width) + "," + OpenVrViewport.Height(crop, _shape.Height);
                }
                return text;
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _targets.Length; i++) Release(ref _targets[i]);
            _cropped?.Dispose(); CollectCrop();
            for (int i = 0; i < 2; i++) { Release(ref _staging[i]); Release(ref _queries[i]); Release(ref _sources[i]); }
            Release(ref _context); Release(ref _device);
        }
    }
}
