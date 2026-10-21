#!/usr/bin/env python3
"""Validate the owned fade bundle's exact RGBA blend and asset contract."""
import argparse
from pathlib import Path
import sys
import UnityPy

ROOT = Path(__file__).resolve().parent.parent


def verify(path):
    objects = list(UnityPy.load(str(path)).objects)
    if sorted(obj.type.name for obj in objects) != ['AssetBundle', 'Shader']:
        raise ValueError('expected the owned fade Shader and AssetBundle only')
    bundle = next(obj for obj in objects if obj.type.name == 'AssetBundle').read_typetree()
    shader = next(obj for obj in objects if obj.type.name == 'Shader')
    directory = bundle['m_Container']
    if len(directory) != 1 or directory[0][0] != 'assets/nativefade.shader':
        raise ValueError('unexpected fade asset directory')
    target = directory[0][1]['asset']
    if target['m_FileID'] != 0 or target['m_PathID'] != shader.path_id or bundle['m_Dependencies']:
        raise ValueError('fade Shader must be self contained')
    data = shader.read_typetree()
    parsed = data['m_ParsedForm']
    if parsed['m_Name'] != 'Hidden/Demeo/NativeFade' or data['platforms'] != [4]:
        raise ValueError('wrong fade shader identity or missing D3D11 programs')
    if len(parsed['m_SubShaders']) != 1:
        raise ValueError('unexpected fade subshaders')
    passes = parsed['m_SubShaders'][0]['m_Passes']
    if len(passes) != 1 or passes[0]['m_State']['m_Name'] != 'ROOM_FADE':
        raise ValueError('unexpected fade passes')
    state = passes[0]['m_State']
    blend = state['rtBlend0']
    expected = {'srcBlend': 0, 'destBlend': 10, 'srcBlendAlpha': 0, 'destBlendAlpha': 10,
                'blendOp': 0, 'blendOpAlpha': 0, 'colMask': 15}
    if any(blend[name]['val'] != value for name, value in expected.items()):
        raise ValueError('fade must multiply both RGB and coverage by one minus fade opacity')
    if any(state[name]['val'] != value for name, value in {
            'zWrite': 0, 'zTest': 8, 'culling': 0, 'alphaToMask': 0, 'stencilWriteMask': 0}.items()):
        raise ValueError('fade must not change depth, stencil or sample coverage')
    if 'STEREO_INSTANCING_ON' not in parsed['m_KeywordNames']:
        raise ValueError('fade needs its stereo-instancing variant')
    print(f'Verified {path.name}: owned D3D11 fade, RGB/alpha multiply, transparent background, stereo keyword')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths', type=Path, nargs='*')
    args = parser.parse_args()
    for path in args.paths or [ROOT / 'assets/nativefade.bundle']:
        try:
            verify(path)
        except Exception as error:
            print(f'Invalid fade bundle {path}: {error}', file=sys.stderr)
            return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
