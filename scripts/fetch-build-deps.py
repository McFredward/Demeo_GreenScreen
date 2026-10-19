#!/usr/bin/env python3
"""Fetch SHA-256-pinned public loader build dependencies; never package them."""
import hashlib
import io
import json
from pathlib import Path
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent.parent


def main():
    sources = json.loads((ROOT / "scripts/loader-sources.json").read_text())
    downloads = {}
    for (backend, game, source) in ((backend, game, source)
            for backend, games in sources.items() for game, source in games.items()):
        destination = ROOT / ".build/loaders" / backend / game
        receipt = destination / "source.sha256"
        identity = source["sha256"] + " " + source["prefix"]
        if (receipt.exists() and receipt.read_text().strip() == identity
                and (destination / ("MelonLoader.dll" if backend == "MelonLoader"
                                     else "BepInEx.dll" if game == "DEMEO" else "BepInEx.Unity.IL2CPP.dll")).is_file()
                and (destination / "0Harmony.dll").is_file()):
            print(f"{backend}/{game}: pinned loader dependencies already present")
            continue
        data = downloads.get(source["url"])
        if data is None:
            request = urllib.request.Request(source["url"], headers={"User-Agent": "Demeo-GreenScreen-build"})
            with urllib.request.urlopen(request, timeout=60) as response:
                data = response.read(80 * 1024 * 1024 + 1)
            downloads[source["url"]] = data
        if hashlib.sha256(data).hexdigest() != source["sha256"]:
            raise RuntimeError(f"{backend}/{game}: loader archive hash mismatch")
        destination.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            names = [n for n in archive.namelist() if n.startswith(source["prefix"]) and n.endswith(".dll")]
            if not names or len({Path(n).name.lower() for n in names}) != len(names):
                raise RuntimeError(f"{backend}/{game}: invalid loader archive layout")
            for old in destination.glob("*.dll"):
                old.unlink()
            for name in names:
                (destination / Path(name).name).write_bytes(archive.read(name))
        receipt.write_text(identity + "\n")
        print(f"{backend}/{game}: verified public loader dependencies")


if __name__ == "__main__":
    main()
