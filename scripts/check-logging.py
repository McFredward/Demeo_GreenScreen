#!/usr/bin/env python3
"""Exercise the actual embedded defaults, including incremental branch switches."""
import os
from pathlib import Path
import subprocess
import sys
ROOT=Path(__file__).resolve().parents[1]
if '--artifacts' in sys.argv:
    expected='Debug' if os.environ.get('GITHUB_REF_NAME')=='dev' else 'Important'
    subprocess.run(['dotnet',str(ROOT/'tests/bin/Debug/net8.0/LoggingChecks.dll'),'--artifacts',str(ROOT/'bin'),expected],cwd=ROOT,check=True)
    raise SystemExit(0)
for branch,expected in [('dev','Debug'),('main','Important'),('dev','Debug')]:
    env=dict(os.environ,GITHUB_REF_NAME=branch)
    subprocess.run(['dotnet','build','tests/LoggingChecks.csproj','--nologo','-v:q'],cwd=ROOT,env=env,check=True)
    subprocess.run(['dotnet',str(ROOT/'tests/bin/Debug/net8.0/LoggingChecks.dll'),expected],cwd=ROOT,env=env,check=True)
