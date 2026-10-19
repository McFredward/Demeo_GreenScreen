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
    for game, source in sources.items():
        destination = ROOT / ".build/loaders" / game
        receipt = destination / "source.sha256"
        if receipt.exists() and receipt.read_text().strip() == source["sha256"]:
            print(f"{game}: pinned loader dependencies already present")
            continue
        request = urllib.request.Request(source["url"], headers={"User-Agent": "Demeo-GreenScreen-build"})
        with urllib.request.urlopen(request, timeout=60) as response:
            data = response.read(80 * 1024 * 1024 + 1)
        if hashlib.sha256(data).hexdigest() != source["sha256"]:
            raise RuntimeError(f"{game}: loader archive hash mismatch")
        destination.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            names = [n for n in archive.namelist() if n.startswith(source["prefix"]) and n.endswith(".dll")]
            if not names or len({Path(n).name.lower() for n in names}) != len(names):
                raise RuntimeError(f"{game}: invalid loader archive layout")
            for name in names:
                (destination / Path(name).name).write_bytes(archive.read(name))
        receipt.write_text(source["sha256"] + "\n")
        print(f"{game}: verified public loader dependencies")


if __name__ == "__main__":
    main()
