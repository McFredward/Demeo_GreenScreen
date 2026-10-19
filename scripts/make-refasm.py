#!/usr/bin/env python3
"""Generate metadata-only build references from the owner's game/interop files."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("game", choices=["DEMEO", "DEMEOBATTLES", "DEMEOBATTLEMARKED"])
    parser.add_argument("assembly_directory", type=Path)
    args = parser.parse_args()
    source = args.assembly_directory.resolve(strict=True)
    # Resolve references from the project rather than copying an entire game.
    evaluated = subprocess.check_output([
        "dotnet", "msbuild", str(ROOT / "GreenScreen.csproj"), "-nologo",
        f"-p:Configuration={args.game}", f"-p:UnityAssemblyDirectory={source}",
        "-getItem:Reference"], cwd=ROOT, text=True)
    references = json.loads(evaluated)["Items"]["Reference"]
    inputs = sorted({Path(item["HintPath"]).resolve() for item in references
                     if Path(item.get("HintPath", "")).parent.resolve() == source})
    if not inputs or any(not p.is_file() or p.suffix != ".dll" for p in inputs):
        raise RuntimeError("Missing or invalid project reference inputs")
    subprocess.run(["dotnet", "tool", "restore"], cwd=ROOT, check=True)
    destination = ROOT / "libs/RefAsm" / args.game
    with tempfile.TemporaryDirectory(prefix="demeo-refasm-") as temporary:
        stage = Path(temporary)
        subprocess.run(["dotnet", "tool", "run", "refasmer", "--", "--all", "--quiet",
                        "--outputdir", str(stage), *map(str, inputs)], cwd=ROOT, check=True)
        subprocess.run(["python3", str(ROOT / "scripts/check-refasm.py"), str(stage)], check=True)
        manifest = {"tool": "JetBrains.Refasmer.CliTool 2.0.3", "kind": "metadata only; all MethodDef RVAs are zero",
                    "assemblies": {}}
        for original in inputs:
            stub = stage / original.name
            manifest["assemblies"][original.name] = {
                "sourceSha256": hashlib.sha256(original.read_bytes()).hexdigest(),
                "sourceBytes": original.stat().st_size,
                "referenceSha256": hashlib.sha256(stub.read_bytes()).hexdigest(),
                "referenceBytes": stub.stat().st_size}
        destination.mkdir(parents=True, exist_ok=True)
        for old in destination.glob("*.dll"):
            old.unlink()
        for original in inputs:
            shutil.copyfile(stage / original.name, destination / original.name)
        (destination / "sources.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"{args.game}: generated {len(inputs)} reference assemblies; no game IL copied")


if __name__ == "__main__":
    main()
