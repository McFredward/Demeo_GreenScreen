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
        var budgetRequest = SteamLinkCoverageBridge.Request(42, Nonce, true);
        Check(budgetRequest[6] == 2 && budgetRequest[10] == 0 && budgetRequest.Skip(40).All(b => b == 0), "v2 requests metadata accounting without reusing reserved flags or source authority");
        var budgetGrant = Grant(); budgetGrant[6] = 2; budgetGrant[10] = 5;
        Check(SteamLinkCoverageBridge.Reply(budgetGrant,42,84,Nonce,true)?.MetadataBudget == true, "explicit v2 budget acknowledgment");
        Check(Parse(budgetGrant) == null && SteamLinkCoverageBridge.Reply(Grant(),42,84,Nonce,true) == null, "versions cannot silently borrow another contract's acknowledgment");
        budgetGrant[10] = 4;
        Check(SteamLinkCoverageBridge.Reply(budgetGrant,42,84,Nonce,true) == null, "rate policy alone cannot grant ordinary RGBA source permission");
        budgetGrant[10] = 1;
        Check(SteamLinkCoverageBridge.Reply(budgetGrant,42,84,Nonce,true) == null, "positive v2 reply must acknowledge its policy");
        int negotiations = 0;
        Check(await SteamLinkCoverageBridge.ObserveCompatibleAsync(budget => { negotiations++; return Task.FromResult(!budget); }) && negotiations == 2,
            "an older endpoint's EOF retries v1 on a fresh connection");
        negotiations = 0;
        Check(await SteamLinkCoverageBridge.ObserveCompatibleAsync(budget => { negotiations++; if(budget) throw new IOException("old endpoint closed"); return Task.FromResult(true); }) && negotiations == 2,
            "older endpoint broken-pipe errors also reach v1 fallback");
        negotiations = 0;
        Check(await SteamLinkCoverageBridge.ObserveCompatibleAsync(budget => { negotiations++; return Task.FromResult(true); }) && negotiations == 1,
            "an observed v2 session never starts an overlapping legacy observer");
        negotiations = 0;
        Check(await SteamLinkCoverageBridge.ObserveCompatibleAsync(budget => { negotiations++; Check(!budget,"bandwidth opt-out only requests the unchanged v1 protocol"); return Task.FromResult(true); }, false) && negotiations == 1,
            "opt-out does not probe or enable v2 policy");
        bool cancelled = false;
        try { await SteamLinkCoverageBridge.ObserveCompatibleAsync(budget => throw new OperationCanceledException()); } catch(OperationCanceledException) { cancelled = true; }
        Check(cancelled, "cancellation never starts a fallback connection");
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
