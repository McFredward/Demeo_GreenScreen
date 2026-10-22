#!/usr/bin/env python3
"""Run actual constructor hooks, optionally with the shipped loader on Mono."""
import argparse
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--mono', help='Optional path to a Mono runtime for both pinned Demeo loaders')
    args = parser.parse_args()
    targets = [('net8.0', 'BepInEx')]
    if args.mono:
        targets += [('net472', loader) for loader in ('BepInEx', 'MelonLoader')]
    for framework, loader in targets:
        subprocess.run(['dotnet', 'build', 'tests/RecenterChecks.csproj',
                        '-p:TestTargetFramework=' + framework, '-p:LoaderBackend=' + loader,
                        '-p:RestoreLockedMode=true', '--nologo', '-v:q'], cwd=ROOT, check=True)
        name = 'Assembly-CSharp.exe' if framework == 'net472' else 'Assembly-CSharp.dll'
        binary = ROOT / 'tests/bin/Recenter' / loader / framework / name
        runner = args.mono if framework == 'net472' else 'dotnet'
        subprocess.run([runner, str(binary)], cwd=ROOT, check=True, timeout=30)

    native = ROOT / '.build/stereo-pose-checks/libstereo-pose-fixture.so'
    native.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(['cc','-std=c11','-Wall','-Wextra','-Werror','-fPIC','-shared',
                    str(ROOT/'tests/StereoPoseNative.c'),'-o',str(native)],check=True)
    for game in ('DEMEOBATTLES', 'DEMEOBATTLEMARKED'):
        subprocess.run(['dotnet', 'build', 'tests/Il2CppRecenterChecks.csproj',
                        '-p:Game=' + game, '-p:RestoreLockedMode=true', '--nologo', '-v:q'], cwd=ROOT, check=True)
        name = 'Assembly-CSharp' if game == 'DEMEOBATTLES' else 'Jade.XRRecentering'
        binary = ROOT / 'tests/bin/Il2CppRecenter' / game / 'net8.0' / (name + '.dll')
        subprocess.run(['dotnet', str(binary), str(native)], cwd=ROOT, check=True, timeout=30)


if __name__ == '__main__':
    main()
