#!/usr/bin/env python3
"""Build all three mods and package an explicit mod-only file list."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parent.parent
GAMES = {
    "DEMEO": ("Demeo", "Demeo_GreenScreen.dll", "netstandard2.1"),
    "DEMEOBATTLES": ("DemeoBattles", "DemeoBattles_GreenScreen.dll", "net6.0"),
    "DEMEOBATTLEMARKED": ("DemeoBattlemarked", "DemeoBattlemarked_GreenScreen.dll", "net6.0"),
}


def main():
    subprocess.run(["python3", str(ROOT / "scripts/check-refasm.py")], check=True)
    subprocess.run([sys.executable, str(ROOT / "scripts/check-coverage.py")], check=True)
    subprocess.run([sys.executable, str(ROOT / "scripts/check-fade.py")], check=True)
    destination = ROOT / "dist"
    destination.mkdir(exist_ok=True)
    channel = os.environ.get("GITHUB_REF_NAME") or subprocess.check_output(
        ["git", "branch", "--show-current"], cwd=ROOT, text=True).strip()
    default_log_level = "Debug" if channel == "dev" else "Important"
    archives = []
    for configuration, (name, assembly, framework) in GAMES.items():
        backend = "BepInEx"
        alternative = "MelonLoader"
        subprocess.run(["dotnet", "build", str(ROOT / "GreenScreen.csproj"), "-c", configuration,
                        f"-p:LoaderBackend={alternative}", "-p:UseReferenceAssemblies=true",
                        "-p:Optimize=true", f"-p:DefaultLogLevel={default_log_level}", "--nologo"], cwd=ROOT, check=True)
        subprocess.run(["dotnet", "build", str(ROOT / "GreenScreen.csproj"), "-c", configuration,
                        "-p:UseReferenceAssemblies=true", "-p:Optimize=true",
                        "-p:ContinuousIntegrationBuild=true", f"-p:DefaultLogLevel={default_log_level}", "--nologo"], cwd=ROOT, check=True)
        binary = ROOT / "bin" / backend / configuration / framework / assembly
        files = {assembly: binary,
                 "README.md": ROOT / "README.md", "LICENSE": ROOT / "LICENSE",
                 "licenses/OpenVR-LICENSE.txt": ROOT / "licenses/OpenVR-LICENSE.txt"}
        for image in ("demeo-mixed-reality.jpg", "steam-frame-logo.svg", "steam-frame-logo-dark.svg", "README.md"):
            files["docs/img/" + image] = ROOT / "docs/img" / image
        if backend == "MelonLoader":
            files["greenscreen.ini"] = ROOT / "greenscreen.ini.example"
        archive_path = destination / f"{name}.zip"
        with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for relative, source in files.items():
                archive.write(source, relative)
        with zipfile.ZipFile(archive_path) as archive:
            if set(archive.namelist()) != set(files):
                raise RuntimeError("Unexpected archive contents")
            if [n for n in archive.namelist() if n.endswith(".dll")] != [assembly]:
                raise RuntimeError("Only the mod DLL may be distributed")
            if backend == "MelonLoader" and "NativePassthrough=true" not in archive.read("greenscreen.ini").decode():
                raise RuntimeError("The shipped config must enable automatic Frame/Steam Link passthrough")
        archives.append(archive_path)
        print(f"Packaged {archive_path.name}: mod DLL, active config, README, licences")
    (destination / "SHA256SUMS").write_text("".join(
        f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.name}\n" for path in archives))
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    (destination / "build-info.json").write_text(json.dumps({"commit": commit, "channel": channel, "default_log_level": default_log_level, "archives": [p.name for p in archives]}, indent=2) + "\n")


if __name__ == "__main__":
    main()
