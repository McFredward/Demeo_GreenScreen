#!/usr/bin/env python3
"""Exercise the real probe's viewport and GPU completion code in native Unity.

Requires a licensed Unity 2021.3 Editor and an actual graphics device. No game
files or headset are used. This is additional API evidence, not a headset test.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

ROOT=Path(__file__).resolve().parent.parent

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity',type=Path,required=True)
    parser.add_argument('--reject-normalized-bug',action='store_true',help='Verify that the GPU fixture rejects the old pixel-coordinate assumption.')
    args=parser.parse_args()
    project=ROOT/('.build/xr-alpha-native-negative' if args.reject_normalized_bug else '.build/xr-alpha-native')
    editor=project/'Assets/Editor'
    editor.mkdir(parents=True,exist_ok=True)
    source=(ROOT/'SrpAlphaProbe.cs').read_text()
    source_hash=hashlib.sha256(source.encode()).hexdigest()
    if args.reject_normalized_bug:
        correct='viewport.x *= width; viewport.width *= width;\n            viewport.y *= height; viewport.height *= height;\n            return viewport;'
        if source.count(correct)!=1:
            raise RuntimeError('Production viewport conversion changed; update the explicit negative control.')
        source=source.replace(correct,'return viewport;')
    (editor/'SrpAlphaProbe.cs').write_text('#define DEMEO\n'+source)
    for name in ('XrAlphaReadbackChecks.cs','XrAlphaProbeStubs.cs'):
        shutil.copyfile(ROOT/'tests'/name,editor/name)
    shutil.copyfile(ROOT/'tests/XrAlphaProbeFixture.shader',project/'Assets/ProbeFixture.shader')
    packages=project/'Packages';packages.mkdir(exist_ok=True)
    (packages/'manifest.json').write_text(json.dumps({'dependencies':{'com.unity.modules.xr':'1.0.0'}}))
    settings=project/'ProjectSettings';settings.mkdir(exist_ok=True)
    shutil.copyfile(ROOT/'unity/ProjectSettings/ProjectVersion.txt',settings/'ProjectVersion.txt')
    log=ROOT/('.build/xr-alpha-native-negative.log' if args.reject_normalized_bug else '.build/xr-alpha-native.log')
    result=subprocess.run(['xvfb-run','-a',str(args.unity),'-batchmode','-force-vulkan','-projectPath',str(project),
                          '-executeMethod','XrAlphaReadbackChecks.Run','-quit','-logFile',str(log)])
    output=log.read_text()
    if args.reject_normalized_bug:
        if result.returncode==0 or 'A full normalized viewport must expand to the full target.' not in output:
            raise RuntimeError('Native fixture did not reject the old viewport assumption for the expected reason; inspect '+str(log))
        print('Native Unity negative control rejected the old viewport assumption as required.')
        return
    result.check_returncode()
    for line in output.splitlines():
        if line.startswith(('XR alpha native readback:','XR alpha native checks passed:')):
            print(line)
    if 'XR alpha native checks passed:' not in output:
        raise RuntimeError('Native Unity did not complete the GPU checks; inspect '+str(log))
    receipt={'production_probe_sha256':source_hash,'graphics_api':'Vulkan',
             'result':next(line for line in output.splitlines() if line.startswith('XR alpha native checks passed:')),
             'headset_test':False,'Windows_D3D11_test':False}
    (ROOT/'.build/xr-alpha-native-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')

if __name__=='__main__':
    main()
