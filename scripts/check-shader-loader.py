#!/usr/bin/env python3
"""Exercise the production Unity6 span/handle bridge through native C exports."""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent.parent


def main():
    destination = ROOT / '.build/shader-loader-checks'
    destination.mkdir(parents=True, exist_ok=True)
    native = destination / 'libshader-loader-fixture.so'
    subprocess.run(['cc', '-std=c11', '-Wall', '-Wextra', '-Werror', '-fPIC', '-shared',
                    str(ROOT / 'tests/ShaderLoaderNative.c'), '-o', str(native)], check=True)
    for game in ('DEMEO', 'DEMEOBATTLES', 'DEMEOBATTLEMARKED'):
        subprocess.run(['dotnet', 'build', 'tests/ShaderLoaderChecks.csproj', '-p:Game=' + game,
                        '-p:Optimize=true', '--nologo', '-v:q'], cwd=ROOT, check=True)
        binary = ROOT / 'tests/bin/ShaderLoader' / game / 'net8.0/ShaderLoaderChecks.dll'
        subprocess.run(['dotnet', str(binary), str(native)], cwd=ROOT, check=True, timeout=30)


if __name__ == '__main__':
    main()
