#!/usr/bin/env python3
"""Exercise actual Harmony fade dispatch with each game's generated member shape."""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent.parent


def main():
    for game in ('DEMEO', 'DEMEOBATTLES', 'DEMEOBATTLEMARKED'):
        subprocess.run(['dotnet', 'build', 'tests/FadeChecks.csproj', '-p:Game=' + game,
                        '-p:RestoreLockedMode=true', '--nologo', '-v:q'], cwd=ROOT, check=True)
        name = 'GameView' if game == 'DEMEOBATTLEMARKED' else 'Assembly-CSharp'
        binary = ROOT / 'tests/bin/Fade' / game / 'net8.0' / (name + '.dll')
        subprocess.run(['dotnet', str(binary)], cwd=ROOT, check=True, timeout=30)


if __name__ == '__main__':
    main()
