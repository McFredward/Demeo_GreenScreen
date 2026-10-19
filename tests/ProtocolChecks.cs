using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GreenScreen;

namespace GreenScreen { public static class Utils { public static void LogInfo(string message) { } } }

internal static class ProtocolChecks
{
    private static int _checks;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); _checks++; }
    private static void Put32(byte[] packet, int at, uint value) => BitConverter.GetBytes(value).CopyTo(packet, at);
    private static readonly byte[] Nonce = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
    private static byte[] Grant()
    {
        byte[] packet = SteamLinkCoverageBridge.Request(42, Nonce);
        packet[4] = 2; packet[10] = 1;
        Put32(packet, 20, 84);
        packet[40] = 7; packet[56] = 1;
        return packet;
    }
    private static SteamLinkCoverageBridge.Snapshot? Parse(byte[] packet) => SteamLinkCoverageBridge.Reply(packet, 42, 84, Nonce);

    public static async Task Main()
    {
        byte[] request = SteamLinkCoverageBridge.Request(42, Nonce);
        Check(request.Length == 128 && request[4] == 1 && request[10] == 0, "request framing");
        Check(request.Skip(40).All(b => b == 0), "request reserved fields");
        Check(Parse(Grant())?.CanAcceptOrdinaryRgba == true, "ordinary RGBA format grant bootstraps without coverage");
        foreach (int offset in new[] { 0, 4, 6, 8, 12, 16, 20, 24, 64, 127 })
        {
            byte[] packet = Grant(); packet[offset] ^= 128;
            Check(Parse(packet) == null, "reject malformed field " + offset);
        }
        Check(Parse(Grant().Take(127).ToArray()) == null, "reject partial record");
        Check(Parse(Grant().Concat(new byte[1]).ToArray()) == null, "reject oversized record");
        foreach (int flags in new[] { 2, 4, 5, 255 })
        {
            byte[] packet = Grant(); packet[10] = (byte)flags;
            Check(Parse(packet) == null, "reject invalid flags " + flags);
        }
        byte[] zeroGeneration = Grant(); zeroGeneration[56] = 0;
        Check(Parse(zeroGeneration) == null, "reject positive reply with zero generation");
        byte[] zeroBinding = Grant(); zeroBinding[40] = 0;
        Check(Parse(zeroBinding) == null, "reject positive reply with zero binding");
        byte[] unavailable = Grant(); unavailable[10] = 0;
        Check(Parse(unavailable)?.CanAcceptOrdinaryRgba == false, "negative reply never grants permission");
        Check(SteamLinkCoverageBridge.RuntimeServers("{\"jsonid\":\"vrpathreg\",\"version\":1,\"runtime\":[\"C:\\\\SteamVR\"]}").Length == 2, "registered native owners");
        Check(SteamLinkCoverageBridge.RuntimeServers("{\"jsonid\":\"vrpathreg\",\"version\":1,\"runtime\":[\"\\\\\\\\server\\\\SteamVR\"]}").Length == 0, "reject network runtime");
        Check(!SteamLinkCoverageBridge.LocalWindowsPath(@"C:\SteamVR\..\foreign"), "reject noncanonical runtime");
        Check(!SteamLinkCoverageBridge.LocalWindowsPath(@"\\server\SteamVR"), "reject UNC");

        Action<SteamLinkCoverageBridge.Snapshot>? publish = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int workers = 0, peak = 0;
        var monitor = new SteamLinkCoverageBridge.Monitor(async (callback, token) =>
        {
            int active = Interlocked.Increment(ref workers); peak = Math.Max(peak, active);
            publish = callback;
            entered.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref workers);
        });
        monitor.Tick(true, 0);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var granted = Parse(Grant())!;
        publish!(granted);
        Check(ReferenceEquals(monitor.Current, granted), "live worker publishes format grant");
        publish(Parse(Grant())!);
        Check(ReferenceEquals(monitor.Current, granted), "identical live replies retain stereo epoch");
        monitor.Tick(true, 100000);
        Check(monitor.Current.CanAcceptOrdinaryRgba, "healthy source has no feature expiry");
        monitor.Reset();
        Check(!monitor.Current.CanAcceptOrdinaryRgba, "disable immediately revokes before worker cleanup");
        publish(granted);
        Check(!monitor.Current.CanAcceptOrdinaryRgba, "late old observer cannot regrant");
        monitor.Tick(true, 100001);
        Check(peak == 1 && workers == 1, "new observer cannot overlap closing worker");
        release.TrySetResult();
        await monitor.Settled.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!monitor.Current.CanAcceptOrdinaryRgba, "worker EOF remains revoked");

        Action<SteamLinkCoverageBridge.Snapshot>? update = null;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconnect = new SteamLinkCoverageBridge.Monitor(async (callback, token) =>
        { update = callback; start.TrySetResult(); await finish.Task; });
        reconnect.Tick(true, 0);
        await start.Task.WaitAsync(TimeSpan.FromSeconds(5));
        update!(granted);
        update(SteamLinkCoverageBridge.Snapshot.Unavailable);
        Check(!reconnect.Current.CanAcceptOrdinaryRgba, "explicit sender revocation");
        update(granted);
        Check(reconnect.Current.CanAcceptOrdinaryRgba, "sender can regrant on same live connection");
        finish.TrySetResult();
        await reconnect.Settled.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!reconnect.Current.CanAcceptOrdinaryRgba, "EOF revokes previously positive grant");
        reconnect.Reset(); monitor.Reset();
        Console.WriteLine($"Protocol and observer checks passed: {_checks}");
    }
}
