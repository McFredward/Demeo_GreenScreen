#!/usr/bin/env python3
"""Build and execute each actual game/loader test binary explicitly.

dotnet run can choose the default OutputPath when evaluating launch settings,
even when custom MSBuild properties select a different binary during build.
"""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent.parent


def main():
    for game in ("DEMEO", "DEMEOBATTLES", "DEMEOBATTLEMARKED"):
        for loader in ("BepInEx", "MelonLoader"):
            print(f"Checking {game} / {loader}", flush=True)
            subprocess.run(["dotnet", "build", "tests/IntegrationChecks.csproj",
                            f"-p:Game={game}", f"-p:LoaderBackend={loader}",
                            "--nologo", "-v:q"], cwd=ROOT, check=True)
            binary = ROOT / "tests/bin/Integration" / loader / game / "net8.0/IntegrationChecks.dll"
            subprocess.run(["dotnet", str(binary)], cwd=ROOT, check=True)


if __name__ == "__main__":
    main()
