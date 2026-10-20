#!/usr/bin/env python3
"""Validate the owned Windows shader bundle, including its asset directory."""
import argparse
from pathlib import Path
import sys

import UnityPy


ROOT = Path(__file__).resolve().parent.parent


def verify(path):
    objects = list(UnityPy.load(str(path)).objects)
    if sorted(obj.type.name for obj in objects) != ["AssetBundle", "Shader"]:
        raise ValueError("expected the owned Shader AND its AssetBundle container")
    bundle = next(obj for obj in objects if obj.type.name == "AssetBundle").read_typetree()
    shader = next(obj for obj in objects if obj.type.name == "Shader")
    directory = bundle["m_Container"]
    if len(directory) != 1 or directory[0][0] != "assets/nativecoverage.shader":
        raise ValueError("missing or unexpected shader asset directory")
    target = directory[0][1]["asset"]
    if target["m_FileID"] != 0 or target["m_PathID"] != shader.path_id:
        raise ValueError("asset directory does not resolve to the embedded Shader")
    if bundle["m_Dependencies"] or bundle["m_IsStreamedSceneAssetBundle"]:
        raise ValueError("coverage must be a self-contained asset bundle")
    data = shader.read_typetree()
    parsed = data["m_ParsedForm"]
    if parsed["m_Name"] != "Hidden/Demeo/NativeCoverage" or data["platforms"] != [4]:
        raise ValueError("wrong shader identity or missing Windows D3D11 programs")
    if len(parsed["m_SubShaders"]) != 1:
        raise ValueError("unexpected coverage subshaders")
    passes = parsed["m_SubShaders"][0]["m_Passes"]
    if [item["m_State"]["m_Name"] for item in passes] != ["RESET", "STAMP"]:
        raise ValueError("missing or reordered coverage passes")
    if "STEREO_INSTANCING_ON" not in parsed["m_KeywordNames"]:
        raise ValueError("missing stereo-instancing keyword")
    print(f"Verified {path.name}: asset directory and shader reference, D3D11, RESET/STAMP, stereo keyword")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", type=Path, nargs="*")
    args = parser.parse_args()
    for path in args.paths or [ROOT / "assets/nativecoverage.bundle"]:
        try:
            verify(path)
        except Exception as error:
            print(f"Invalid coverage bundle {path}: {error}", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
