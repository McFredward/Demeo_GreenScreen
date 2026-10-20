using System;
using System.Runtime.InteropServices;
using GreenScreen;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

internal static class NativeSubmitChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Configure();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int RunChecks([MarshalAs(UnmanagedType.LPWStr)] string provider, Configure enable, Configure disable);
    internal static void Run(string provider)
    {
#if !MELONLOADER
        BepInExPlugin.Log = new BepInEx.Logging.ManualLogSource();
#endif
        SrpOutput.Discover();
        var descriptor = XRSettings.eyeTextureDesc; descriptor.volumeDepth = 2;
        descriptor.dimension = TextureDimension.Tex2DArray; XRSettings.eyeTextureDesc = descriptor;
        IntPtr host = LoadLibraryEx(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(provider), "provider-native-host.dll"), IntPtr.Zero, 8);
        if (host == IntPtr.Zero) throw new Exception("Native test host failed to load: " + Marshal.GetLastWin32Error());
        var run = Marshal.GetDelegateForFunctionPointer<RunChecks>(GetProcAddress(host, "RunProviderChecks"));
        Exception failure = null;
        Configure enable = () =>
        {
            try
            {
                if (!OpenVrSubmission.Ensure(out string reason)) throw new Exception(reason);
                SteamLinkCoverageBridge.Grant(); OpenVrSubmission.Activate(SteamLinkCoverageBridge.Current);
            }
            catch (Exception error) { failure = error; }
        };
        int disables = 0;
        Configure disable = () =>
        {
            if (disables == 0)
            {
                Time.unscaledTime += 6;
                if (!OpenVrSubmission.Ensure(out string reason)) failure = new Exception(reason);
                string evidence = TestLog.Messages.FindLast(line => line.StartsWith("OpenVR separate 2D eye submission:"));
                Console.WriteLine(evidence);
                if (evidence == null || evidence.Contains("samples left/right=0/") || evidence.Contains("/0; readback-hr"))
                    failure = new Exception("The exact-submit GPU alpha probe did not complete for both eyes.");
            }
            SteamLinkCoverageBridge.Reset(); if (++disables >= 2) OpenVrSubmission.Reset();
        };
        int result = run(provider, enable, disable);
        GC.KeepAlive(enable); GC.KeepAlive(disable);
        if (failure != null) throw failure;
        if (result != 0) throw new Exception("Actual SDK / production 2D submit checks failed: " + result);
        Console.WriteLine("Production C# 2D OpenVR boundary passed with the actual SDK and D3D11 eye arrays. No SteamVR/headset test.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW", SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
