#!/usr/bin/env python3
"""Exercise the real private OpenVR provider's stereo transition and D3D11 submits.

Requires the user's SDK DLL, .NET, MinGW-w64, Wine and an actual graphics device.
Only public Unity interface headers are downloaded. Private provider copies and
the isolated Wine prefix are removed; small receipts/logs remain in .build.
This never connects to SteamVR or a headset and is additional to portable CI.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
REVISION = "a30a0100daacef8c3f0290ce5b179c8a1148abb0"
HEADERS = ("IUnityGraphics.h", "IUnityInterface.h", "IUnityXRDisplay.h", "UnityXRTypes.h",
           "UnityXRSubsystemTypes.h", "UnitySubsystemTypes.h", "UnityTypes.h")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--provider", type=Path, required=True)
    parser.add_argument("--wine", type=Path, default=Path("/usr/lib/wine/wine64"))
    parser.add_argument("--unity", type=Path, help="Optional licensed Linux Unity Editor with Windows Mono build support")
    args = parser.parse_args()
    source = (ROOT / "OpenVrNativeSettings.cs").read_text()
    expected = re.search(r'const string FileHash = "([a-f0-9]{64})";', source)[1]
    provider = args.provider.resolve()
    if hashlib.sha256(provider.read_bytes()).hexdigest() != expected:
        raise RuntimeError("Private provider differs from the production pin; no native code was loaded.")

    subprocess.run(["dotnet", "build", "tests/IntegrationChecks.csproj", "-p:Game=DEMEO",
                    "-p:LoaderBackend=BepInEx", "--nologo", "-v:q"], cwd=ROOT, check=True)
    subprocess.run(["dotnet", "tests/bin/Integration/BepInEx/DEMEO/net8.0/IntegrationChecks.dll",
                    "--provider-pin", str(provider)], cwd=ROOT, check=True)
    cache = ROOT / ".build/openvr-provider-headers/ProviderInterface"
    cache.mkdir(parents=True, exist_ok=True)
    for name in HEADERS:
        path = cache / name
        if not path.exists():
            url = (f"https://raw.githubusercontent.com/ValveSoftware/unity-xr-plugin/{REVISION}/"
                   f"CommonHeaders/ProviderInterface/{name}")
            path.write_bytes(urllib.request.urlopen(url, timeout=30).read())

    log = ROOT / ".build/openvr-provider-native.log"
    with tempfile.TemporaryDirectory(prefix="openvr-provider-run-", dir=ROOT / ".build") as directory:
        owned = Path(directory)
        lines = ["#pragma once"]
        for name in ("ModeExport", "DestroyExport"):
            value = re.search(r'const string ' + name + r' = "([^"]+)";', source)[1]
            lines.append(f'constexpr const char* {name} = "{value}";')
        for name in ("ModeGetterRva", "ModeRva", "ContextRva", "HandleRva", "DestroyRva",
                     "ProviderOffset", "CreatedOffset", "SinglePassOffset"):
            value = re.search(r'\b' + name + r' = (0x[0-9a-f]+|[0-9]+)', source)[1]
            lines.append(f"constexpr unsigned {name} = {value};")
        (owned / "provider-pins.h").write_text("\n".join(lines) + "\n")
        compiler = "x86_64-w64-mingw32-g++"
        flags = ["-std=c++17", "-O2", "-Wall", "-Wextra", "-Werror", "-static", "-Wl,--no-insert-timestamp"]
        subprocess.run([compiler, *flags, "-shared", str(ROOT / "tests/OpenVrProviderFakeApi.cpp"),
                        "-o", str(owned / "openvr_api.dll")], check=True)
        executable = owned / "provider-native-checks.exe"
        subprocess.run([compiler, *flags, "-Wno-cast-function-type", "-municode", "-I", str(cache.parent),
                        "-I", str(owned), str(ROOT / "tests/OpenVrProviderNativeChecks.cpp"),
                        "-ld3d11", "-ldxgi", "-o", str(executable)], check=True)
        subprocess.run([compiler, *flags, "-Wno-cast-function-type", "-shared", "-DMANAGED_FIXTURE", "-I", str(cache.parent),
                        "-I", str(owned), str(ROOT / "tests/OpenVrProviderNativeChecks.cpp"),
                        "-ld3d11", "-ldxgi", "-o", str(owned / "provider-native-host.dll")], check=True)
        managed = ROOT / ".build/native-submit-managed"
        subprocess.run(["dotnet", "publish", "tests/IntegrationChecks.csproj", "-p:Game=DEMEO", "-p:LoaderBackend=BepInEx",
                        "-r", "win-x64", "--self-contained", "true", "-o", str(managed), "--nologo", "-v:q"],
                       cwd=ROOT, check=True)
        if args.unity:
            subprocess.run([compiler, *flags, "-Wno-cast-function-type", "-municode", str(ROOT / "tests/MonoSubmitHost.cpp"),
                            "-o", str(owned / "mono-submit-host.exe")], check=True)
            mono_managed = ROOT / ".build/native-submit-mono"
            subprocess.run(["dotnet", "build", "tests/IntegrationChecks.csproj", "-p:Game=DEMEO", "-p:LoaderBackend=BepInEx",
                            "-p:TestTargetFramework=netstandard2.1", "-p:OutputType=Library", "-p:UseAppHost=false",
                            "-p:BaseIntermediateOutputPath=obj/MonoSubmit/", "-o", str(mono_managed), "--nologo", "-v:q"],
                           cwd=ROOT, check=True)
        private = owned / "XRSDKOpenVR.dll"
        shutil.copyfile(provider, private)
        environment = {**os.environ, "WINEPREFIX": str(owned / "prefix"), "WINEARCH": "win64", "WINEDEBUG": "-all"}
        try:
            windows = "Z:" + str(private).replace("/", "\\")
            result = subprocess.run(["xvfb-run", "-a", str(args.wine), str(executable), windows],
                                    env=environment, capture_output=True, text=True, timeout=180)
            log.write_text(result.stdout + result.stderr)
            success = next((line for line in result.stdout.splitlines()
                            if line.startswith("Actual OpenVR provider/D3D11 checks passed:")), None)
            if result.returncode != 0 or success is None:
                raise RuntimeError("Private native-provider fixture failed; inspect " + str(log))
            print(success)
            # Wine descendants can inherit captured pipes after the app exits.
            # Use owned files so pipe EOF is not mistaken for app completion.
            managed_log = owned / "managed-submit.log"
            with managed_log.open("w") as output:
                managed_result = subprocess.run(["xvfb-run", "-a", str(args.wine), str(managed / "IntegrationChecks.exe"),
                                                 "--native-submit", windows],
                                                env=environment, stdout=output, stderr=subprocess.STDOUT, timeout=90)
            managed_output = managed_log.read_text(errors="replace")
            log.write_text(log.read_text() + "\nProduction C# indexed boundary:\n" + managed_output)
            managed_success = next((line for line in managed_output.splitlines()
                                    if line.startswith("Production C# indexed OpenVR boundary passed")), None)
            if managed_result.returncode != 0 or managed_success is None:
                raise RuntimeError("Production managed/native submit fixture failed; inspect " + str(log))
            print(managed_success)
            indexed_result = next(line for line in managed_output.splitlines()
                                  if line.startswith("Actual OpenVR provider/D3D11 checks passed:"))
            if args.unity:
                editor = args.unity.resolve().parent / "Data"
                runtime = editor / "PlaybackEngines/WindowsStandaloneSupport/Variations/win64_player_nondevelopment_mono/MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll"
                profile = editor / "MonoBleedingEdge/lib/mono/unityjit-win32"
                def win(path): return "Z:" + str(path).replace("/", "\\")
                with (owned / "mono-submit.log").open("w") as output:
                    mono_result = subprocess.run(["xvfb-run", "-a", str(args.wine), str(owned / "mono-submit-host.exe"),
                                                 win(runtime), win(profile), win(mono_managed / "IntegrationChecks.dll"), windows],
                                                env=environment, stdout=output, stderr=subprocess.STDOUT, timeout=90)
                mono_output = (owned / "mono-submit.log").read_text(errors="replace")
                log.write_text(log.read_text() + "\nUnity Windows Mono boundary:\n" + mono_output)
                if mono_result.returncode != 0 or "Unity Windows Mono runtime: production indexed callback passed." not in mono_output:
                    raise RuntimeError("Unity Mono submit fixture failed; inspect " + str(log))
                print("Unity Windows Mono runtime: production indexed callback passed.")
            receipt = {"provider_sha256": expected,
                       "production_adapter_sha256": hashlib.sha256(source.encode()).hexdigest(),
                       "public_interface_revision": REVISION, "result": success,
                       "indexed_result": indexed_result,
                       "native_SDK_executed": True, "actual_D3D11_textures": True,
                       "production_managed_submit_executed": True,
                       "Unity_Windows_Mono_executed": bool(args.unity),
                       "production_submit_sha256": hashlib.sha256((ROOT / "OpenVrIndexedSubmit.cs").read_bytes()).hexdigest(),
                       "SteamVR_or_headset_test": False}
            (ROOT / ".build/openvr-provider-native-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
        finally:
            # This prefix belongs only to this fixture; no host/user Wine or
            # SteamVR process is stopped by this cleanup.
            server = args.wine.with_name("wineserver64")
            subprocess.run([str(server), "-k"], env=environment, capture_output=True, timeout=15, check=False)
            subprocess.run([str(server), "-w"], env=environment, capture_output=True, timeout=15, check=False)


if __name__ == "__main__":
    main()
