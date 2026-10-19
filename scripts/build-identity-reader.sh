#!/usr/bin/env bash
# Reproducible small read-only Windows child; no SteamVR DLL is redistributed.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
output="${1:-$root/assets/steamlink/connection-reader.exe}"
x86_64-w64-mingw32-gcc -std=c11 -Os -Wall -Wextra -Werror -municode \
  -Wl,--no-insert-timestamp,--nxcompat,--dynamicbase -s \
  "$root/tools/steamlink-connection-reader.c" -o "$output"
python3 -B - "$output" <<'PY'
import hashlib,struct,sys
p=sys.argv[1];b=open(p,'rb').read();offset=struct.unpack_from('<I',b,60)[0]
assert b[:2]==b'MZ' and b[offset:offset+4]==b'PE\0\0'
assert struct.unpack_from('<H',b,offset+4)[0]==0x8664
assert struct.unpack_from('<H',b,offset+24)[0]==0x20b
print(p,len(b),hashlib.sha256(b).hexdigest())
PY
