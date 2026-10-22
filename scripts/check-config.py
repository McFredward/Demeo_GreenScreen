#!/usr/bin/env python3
"""Validate migration/reload using the pinned real BepInEx 5 and 6 ConfigFile."""
from pathlib import Path
import subprocess
ROOT = Path(__file__).resolve().parents[1]
for game in ('DEMEO', 'DEMEOBATTLEMARKED'):
    subprocess.run(['dotnet', 'build', 'tests/ConfigChecks.csproj', '-p:Game=' + game,
                    '--nologo', '-v:q'], cwd=ROOT, check=True)
    subprocess.run(['dotnet', str(ROOT / 'tests/bin/Config' / game / 'net8.0/ConfigChecks.dll')],
                   cwd=ROOT, check=True, timeout=30)
