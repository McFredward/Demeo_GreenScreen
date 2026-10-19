#!/usr/bin/env python3
"""Build all three mods and package an explicit mod-only file list."""
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parent.parent
GAMES = {
    "DEMEO": ("Demeo", "Demeo_GreenScreen.dll", "netstandard2.1"),
    "DEMEOBATTLES": ("DemeoBattles", "DemeoBattles_GreenScreen.dll", "net6.0"),
    "DEMEOBATTLEMARKED": ("DemeoBattlemarked", "DemeoBattlemarked_GreenScreen.dll", "net6.0"),
}


def main():
    subprocess.run(["python3", str(ROOT / "scripts/check-refasm.py")], check=True)
    destination = ROOT / "dist"
    destination.mkdir(exist_ok=True)
    archives = []
    for configuration, (name, assembly, framework) in GAMES.items():
        subprocess.run(["dotnet", "build", str(ROOT / "GreenScreen.csproj"), "-c", configuration,
                        "-p:UseReferenceAssemblies=true", "-p:Optimize=true",
                        "-p:ContinuousIntegrationBuild=true", "--nologo"], cwd=ROOT, check=True)
        binary = ROOT / "bin" / configuration / framework / assembly
        files = {assembly: binary, "greenscreen.ini.example": ROOT / "greenscreen.ini.example",
                 "README.md": ROOT / "README.md", "LICENSE": ROOT / "LICENSE"}
        archive_path = destination / f"{name}.zip"
        with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for relative, source in files.items():
                archive.write(source, relative)
        with zipfile.ZipFile(archive_path) as archive:
            if set(archive.namelist()) != set(files):
                raise RuntimeError("Unexpected archive contents")
            if [n for n in archive.namelist() if n.endswith(".dll")] != [assembly]:
                raise RuntimeError("Only the mod DLL may be distributed")
        archives.append(archive_path)
        print(f"Packaged {archive_path.name}: mod DLL, example config, README, licence")
    (destination / "SHA256SUMS").write_text("".join(
        f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.name}\n" for path in archives))
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    (destination / "build-info.json").write_text(json.dumps({"commit": commit, "archives": [p.name for p in archives]}, indent=2) + "\n")


if __name__ == "__main__":
    main()
