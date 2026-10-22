#nullable disable
// Adapted from McFredward/GloomhavenVR, GPL-3.0 (see LICENSE).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GreenScreen;

// The provider, not this process, owns native texture -> composition -> encoder
// association. This is a live source-capability negotiation, never an app-frame
// identifier or proof that the Frame has displayed a particular coverage image.
internal static class SteamLinkCoverageBridge
{
    internal const int PacketBytes = 128;
    internal const string PipePrefix = "SFNP.StreamBridge.V1.";
    private static readonly Monitor State = new(ReadWindowsAsync);
    private static int _reports;
    private static bool _lastAccept, _lastOperational;
    private static volatile bool _budgetPolicy = true;

    internal static bool CanAcceptOrdinaryRgba => State.Current.CanAcceptOrdinaryRgba;
    internal static bool CoverageOperational => State.Current.CoverageOperational;
    internal static Snapshot Current => State.Current;

    internal static void Tick(bool requested, double nowSeconds, bool accountMaskBandwidth = true)
    {
        if (_budgetPolicy != accountMaskBandwidth) { _budgetPolicy = accountMaskBandwidth; Reset(); }
        State.Tick(requested, nowSeconds);
        Snapshot current = State.Current;
        if (!requested) { _reports = 0; _lastAccept = _lastOperational = false; return; }
        if ((_reports == 0 || current.CanAcceptOrdinaryRgba != _lastAccept || current.CoverageOperational != _lastOperational)
            && _reports < 8)
        {
            _reports++;
            Utils.LogDebug("Steam Link coverage bridge: ordinary RGBA source="
                + current.CanAcceptOrdinaryRgba + "; native coverage operational=" + current.CoverageOperational
                + "; metadata wire budget=" + current.MetadataBudget
                + ". Without a compatible paired companion the normal game image remains visible. "
                + "Source negotiation alone does not prove a displayed image.");
        }
        _lastAccept = current.CanAcceptOrdinaryRgba; _lastOperational = current.CoverageOperational;
    }
    internal static void Reset() { State.Reset(); _reports = 0; _lastAccept = _lastOperational = false; }

    internal sealed class Snapshot
    {
        internal static readonly Snapshot Unavailable = new(0, 0, false, false);
        internal Snapshot(ulong generation, uint server, bool accept, bool operational)
        { EncoderGeneration = generation; ServerPid = server; CanAcceptOrdinaryRgba = accept; CoverageOperational = accept && operational; }
        internal string Binding { get; set; } = "";
        internal bool MetadataBudget { get; set; }
        internal bool SameGrant(Snapshot other) => CanAcceptOrdinaryRgba && other.CanAcceptOrdinaryRgba
            && EncoderGeneration == other.EncoderGeneration && ServerPid == other.ServerPid
            && Binding == other.Binding && MetadataBudget == other.MetadataBudget;
        internal ulong EncoderGeneration { get; }
        internal uint ServerPid { get; }
        internal bool CanAcceptOrdinaryRgba { get; }
        internal bool CoverageOperational { get; }
    }

    // One owned off-thread observer per eligibility epoch. Stop/EOF revokes
    // immediately, before waiting for disposal; a stale callback cannot grant
    // authority in a new epoch. No per-frame I/O, overlapping worker or timer
    // which turns off an otherwise healthy live connection.
    internal sealed class Monitor
    {
        private readonly object _gate = new();
        private readonly Func<Action<Snapshot>, CancellationToken, Task> _observe;
        private CancellationTokenSource _cancel;
        private Task _worker;
        private Task _cancellation;
        private bool _closing;
        private int _cleanupReports;
        private Snapshot _current = Snapshot.Unavailable;
        private bool _wanted;
        private ulong _epoch;
        private int _attempts;
        private double _next;
        internal Monitor(Func<Action<Snapshot>, CancellationToken, Task> observe) { _observe = observe; }
        internal Snapshot Current
        {
            get { return Volatile.Read(ref _current); }
        }
        internal Task Settled { get { lock (_gate) return _worker ?? Task.CompletedTask; } }
        internal void Reset()
        {
            lock (_gate)
            {
                _wanted = false; _epoch++; _attempts = 0; _next = 0;
                Volatile.Write(ref _current, Snapshot.Unavailable); RequestCancellationLocked();
            }
        }
        internal void Tick(bool wanted, double now)
        {
            lock (_gate)
            {
                if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) wanted = false;
                if (wanted != _wanted)
                {
                    _wanted = wanted; _epoch++; _attempts = 0; _next = now;
                    Volatile.Write(ref _current, Snapshot.Unavailable); RequestCancellationLocked();
                }
                if (_worker != null && !_worker.IsCompleted) return;
                if (_worker != null && _worker.IsFaulted) { _ = _worker.Exception; }
                _worker = null; _cancel = null; _cancellation = null;
                if (!wanted || now < _next) return;
                _attempts = Math.Min(_attempts + 1, 8);
                // A scene handoff can briefly close the old pipe while the new
                // eye textures are being registered. Do not impose a two-second
                // black dwell after that first short-lived refusal. A small
                // cold retry burst then backs off when no companion is present.
                _next = now + RetryDelay(_attempts);
                ulong epoch = _epoch;
                var cancel = new CancellationTokenSource();
                _cancel = cancel; _closing = false;
                CancellationToken token = cancel.Token;
                _worker = Task.Run(async () =>
                {
                    try
                    {
                        await _observe(value =>
                        {
                            lock (_gate)
                                if (_wanted && epoch == _epoch && !token.IsCancellationRequested
                                    && !_current.SameGrant(value)) Volatile.Write(ref _current, value);
                        }, token).ConfigureAwait(false);
                    }
                    catch (Exception) { /* Optional bridge absence preserves the normal game image. */ }
                    finally
                    {
                        Task cancellation;
                        lock (_gate)
                        {
                            if (epoch == _epoch) Volatile.Write(ref _current, Snapshot.Unavailable);
                            _closing = true;
                            cancellation = _cancellation;
                        }
                        if (cancellation != null) await cancellation.ConfigureAwait(false);
                        try { cancel.Dispose(); }
                        catch (Exception error) { ReportCleanupFailure("dispose", error); }
                    }
                });
            }
        }

        internal static double RetryDelay(int attempt) => attempt == 1 ? .05 : attempt == 2 ? .1
            : attempt == 3 ? .25 : Math.Min(10, (attempt - 3) * 2);

        // Mono pipe cancellation/disposal can race registered callbacks.
        // Neither cancellation nor
        // disposal belongs inside the XR tick/monitor lock. Revoke above first,
        // then schedule exactly one cancellation. The owned observer awaits it
        // before disposing its CTS or completing, so a new epoch cannot overlap
        // pending callbacks and Cancel/Dispose cannot race one another.
        private void RequestCancellationLocked()
        {
            if (_cancel == null || _closing || _cancellation != null) return;
            CancellationTokenSource cancel = _cancel;
            _cancellation = Task.Run(() =>
            {
                try { cancel.Cancel(); }
                catch (Exception error) { ReportCleanupFailure("cancel", error); }
            });
        }

        private void ReportCleanupFailure(string operation, Exception error)
        {
            if (Interlocked.Increment(ref _cleanupReports) <= 3)
                Utils.LogWarning("Steam Link coverage bridge observer teardown: " + operation
                    + " failure=" + error.GetType().Name + "; source permission remains revoked.");
        }
    }

    internal static byte[] Request(uint appPid, byte[] nonce, bool metadataBudget = false)
    {
        if (appPid == 0 || nonce == null || nonce.Length != 16 || nonce.All(value => value == 0))
            throw new ArgumentException("A process and fresh nonzero client nonce are required.");
        var packet = new byte[PacketBytes];
        packet[0] = (byte)'S'; packet[1] = (byte)'B'; packet[2] = (byte)'N'; packet[3] = (byte)'1';
        Put16(packet, 4, 1); Put16(packet, 6, metadataBudget ? 2 : 1); Put16(packet, 8, PacketBytes);
        Put32(packet, 12, 1); Put32(packet, 16, appPid);
        Array.Copy(nonce, 0, packet, 24, 16);
        return packet;
    }

    internal static Snapshot Reply(byte[] packet, uint appPid, uint serverPid, byte[] nonce, bool metadataBudget = false)
    {
        if (packet == null || packet.Length != PacketBytes || appPid == 0 || serverPid == 0
            || nonce == null || nonce.Length != 16 || nonce.All(value => value == 0)
            || packet[0] != 'S' || packet[1] != 'B' || packet[2] != 'N' || packet[3] != '1'
            || Read16(packet, 4) != 2 || Read16(packet, 6) != (metadataBudget ? 2 : 1) || Read16(packet, 8) != PacketBytes
            || Read32(packet, 12) != 1 || Read32(packet, 16) != appPid || Read32(packet, 20) != serverPid
            || !System.Linq.Enumerable.SequenceEqual(packet.Skip(24).Take(16), nonce)
            || packet.Skip(64).Any(value => value != 0)) return null;
        int flags = Read16(packet, 10);
        ulong generation = Read64(packet, 56);
        if ((flags & ~(metadataBudget ? 7 : 3)) != 0 || (flags & 4) != 0 && (flags & 1) == 0
            || metadataBudget && (flags & 1) != 0 && (flags & 4) == 0 || (flags & 2) != 0 && (flags & 1) == 0
            || flags != 0 && (generation == 0 || packet.Skip(40).Take(16).All(value => value == 0))) return null;
        return new Snapshot(generation, serverPid, (flags & 1) != 0, (flags & 2) != 0)
            { Binding = Convert.ToBase64String(packet, 40, 16), MetadataBudget = (flags & 4) != 0 };
    }
    private static void Put16(byte[] p, int at, int value) { p[at] = (byte)value; p[at + 1] = (byte)(value >> 8); }
    private static void Put32(byte[] p, int at, uint value) { for (int i = 0; i < 4; i++) p[at + i] = (byte)(value >> (i * 8)); }
    private static int Read16(byte[] p, int at) => p[at] | p[at + 1] << 8;
    private static uint Read32(byte[] p, int at) => (uint)p[at] | (uint)p[at + 1] << 8 | (uint)p[at + 2] << 16 | (uint)p[at + 3] << 24;
    private static ulong Read64(byte[] p, int at) => Read32(p, at) | (ulong)Read32(p, at + 4) << 32;

    [DataContract] private sealed class Paths
    {
        [DataMember] public string jsonid = "";
        [DataMember] public int version = 0;
        [DataMember] public string[] runtime = Array.Empty<string>();
    }

    // Use the SDK's actual registered runtime directories, not a guessed Steam
    // installation path, process name alone or a PATH-resolved executable.
    internal static string[] RuntimeServers(string raw)
    {
        if (raw == null || raw.Length == 0 || raw.Length > 16384
            || Regex.Matches(raw, "\\\"runtime\\\"\\s*:").Count != 1
            || Regex.Matches(raw, "\\\"version\\\"\\s*:").Count != 1
            || Regex.Matches(raw, "\\\"jsonid\\\"\\s*:").Count != 1) return Array.Empty<string>();
        try
        {
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(raw));
            Paths value = new DataContractJsonSerializer(typeof(Paths)).ReadObject(data) as Paths;
            if (value == null || value.jsonid != "vrpathreg" || value.version != 1 || value.runtime == null
                || value.runtime.Length == 0 || value.runtime.Length > 8) return Array.Empty<string>();
            return value.runtime.Where(path => LocalWindowsPath(path))
                .SelectMany(path => new[] { path.TrimEnd('\\') + @"\bin\win64\vrcompositor.exe",
                    path.TrimEnd('\\') + @"\bin\win64\vrserver.exe" })
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception) { return Array.Empty<string>(); }
    }

    internal static bool LocalWindowsPath(string path) => path != null && path.Length >= 4 && path.Length < 1024
        && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
        && path[1] == ':' && path[2] == '\\'
        && !System.Linq.Enumerable.Skip(path, 2).Any(c => c < 32 || c == '"' || c == '/' || c == ':')
        && !path.Substring(3).Split('\\').Any(part => part == "." || part == "..");

    private static bool LocalUnredirected(string path)
    {
        if (!LocalWindowsPath(path)
            || !string.Equals(path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return false;
        string current = path;
        while (!string.IsNullOrEmpty(current) && current!.Length > 3)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            current = Path.GetDirectoryName(current);
        }
        return true;
    }

    private static Process[] Servers()
    {
        string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(local)) local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (local.Length == 0) return Array.Empty<Process>();
        string registry = Path.Combine(local, "openvr", "openvrpaths.vrpath");
        if (!LocalUnredirected(registry)) return Array.Empty<Process>();
        string raw;
        using (var file = new FileStream(registry, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (file.Length == 0 || file.Length > 16384) return Array.Empty<Process>();
            byte[] bytes = new byte[(int)file.Length]; int read = 0;
            while (read < bytes.Length) { int count = file.Read(bytes, read, bytes.Length - read); if (count <= 0) return Array.Empty<Process>(); read += count; }
            if (file.ReadByte() != -1) return Array.Empty<Process>();
            raw = new UTF8Encoding(false, true).GetString(bytes);
        }
        string[] paths = RuntimeServers(raw);
        var candidates = new List<Process>();
        var selected = new List<Process>();
        try
        {
            // The public compositor plugin executes in vrcompositor; the server
            // provider executes in vrserver. Only the actual native draw/encode
            // owner opens a capable pipe. A process name alone is not admission.
            candidates.AddRange(Process.GetProcessesByName("vrcompositor"));
            candidates.AddRange(Process.GetProcessesByName("vrserver"));
            if (candidates.Count > 8) return Array.Empty<Process>();
            foreach (Process candidate in candidates)
            {
                string path = candidate.MainModule?.FileName;
                if (path != null && LocalUnredirected(path) && paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    selected.Add(candidate);
            }
            return selected.ToArray();
        }
        catch (Exception) { selected.Clear(); return Array.Empty<Process>(); }
        finally { foreach (Process candidate in candidates) if (!selected.Contains(candidate)) candidate.Dispose(); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverPid);

    private static async Task ReadWindowsAsync(Action<Snapshot> publish, CancellationToken cancellation)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8) return;
        Process[] servers = Servers();
        try
        {
            foreach (Process server in servers)
            {
                cancellation.ThrowIfCancellationRequested();
                if (await ObserveCompatibleAsync(version => ReadServerAsync(server, publish, cancellation, version), _budgetPolicy, cancellation).ConfigureAwait(false)) return;
            }
        }
        finally { foreach (Process server in servers) server.Dispose(); }
    }

    // A rejected old endpoint may close with EOF or IOException. Neither can
    // grant permission; both must still allow a fresh v1 connection. Once a
    // valid session was observed, its later EOF stays a real withdrawal.
    internal static async Task<bool> ObserveCompatibleAsync(Func<bool, Task<bool>> observe, bool requestBudget = true, CancellationToken cancellation = default)
    {
        foreach (bool budget in requestBudget ? new[] { true, false } : new[] { false })
        {
            cancellation.ThrowIfCancellationRequested();
            try { if (await observe(budget).ConfigureAwait(false)) return true; }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { }
        }
        return false;
    }

    private static async Task<bool> ReadServerAsync(Process server, Action<Snapshot> publish, CancellationToken cancellation, bool metadataBudget)
    {
        if (server.HasExited) return false;
        using Process app = Process.GetCurrentProcess();
        uint serverPid = checked((uint)server.Id), appPid = checked((uint)app.Id);
        DateTime start = server.StartTime;
        using var pipe = new NamedPipeClientStream(".", PipePrefix + serverPid, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        using (cancellation.Register(() => pipe.Dispose()))
        {
            await pipe.ConnectAsync(1000, cancellation).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint actual) || actual != serverPid
                || server.HasExited || server.StartTime != start) return false;
            byte[] nonce = new byte[16];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
            byte[] request = Request(appPid, nonce, metadataBudget);
            await pipe.WriteAsync(request, 0, request.Length, cancellation).ConfigureAwait(false);
            await pipe.FlushAsync(cancellation).ConfigureAwait(false);
            byte[] packet = new byte[PacketBytes];
            bool observed = false;
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    int used = 0;
                    while (used < packet.Length)
                    {
                        int count = await pipe.ReadAsync(packet, used, packet.Length - used, cancellation).ConfigureAwait(false);
                        if (count == 0) return observed;
                        used += count;
                    }
                    if (server.HasExited || server.StartTime != start) return observed;
                    Snapshot value = Reply(packet, appPid, serverPid, nonce, metadataBudget);
                    if (value == null) return observed;
                    observed = true;
                    publish(value);
                }
                return observed;
            }
            catch (IOException) when (observed) { return true; }
            finally { publish(Snapshot.Unavailable); }
        }
    }
}
