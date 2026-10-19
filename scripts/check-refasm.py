#!/usr/bin/env python3
"""Prove that every DLL in libs/RefAsm/ is metadata only — no method bodies.

WHY THIS IS NOT A SIZE CHECK
----------------------------
The whole point of libs/RefAsm is that the publisher's code never enters this
repository. The failure that would break that promise is silent: someone runs a
`cp` instead of `refasmer`, or refasmer is missing and a wrapper falls back to a
copy, and the repo now ships GH.Runtime.dll in full. A stub is much smaller than
its source, but "smaller" proves nothing on its own and there is no source to
compare against on a CI runner.

The decisive fact lives in the CLI metadata. Every method in a .NET assembly has a
row in the MethodDef table whose first field is the RVA of its IL body; a method
with no body has RVA 0 (ECMA-335 II.22.26). A reference assembly has RVA 0 for
EVERY row. One non-zero RVA means executable game code is present.

So this walks the PE header -> CLI header -> #~ metadata stream -> MethodDef table
and asserts every RVA is zero. No dependencies, runs anywhere python3 does.

Usage:
    scripts/check-refasm.py [<dir-or-dll> ...]      (default: libs/RefAsm)
"""

from __future__ import annotations

import struct
import sys
from pathlib import Path

# ---------------------------------------------------------------------------
# ECMA-335 table shapes. Only the tables at or before MethodDef (0x06) matter:
# to find the MethodDef rows we must know the byte size of every table before it.
# ---------------------------------------------------------------------------
MODULE, TYPEREF, TYPEDEF, FIELDPTR, FIELD, METHODPTR, METHODDEF = range(7)
PARAM = 0x08
MODULEREF = 0x1A
TYPESPEC = 0x1B
ASSEMBLYREF = 0x23


class Bad(Exception):
    pass


def _rva_to_offset(sections, rva: int) -> int:
    for va, vsize, raw_size, raw_ptr in sections:
        if va <= rva < va + max(vsize, raw_size):
            return raw_ptr + (rva - va)
    raise Bad(f"RVA 0x{rva:x} is in no section")


def method_rvas(path: Path):
    """Yield every MethodDef RVA in the assembly at *path*."""
    data = path.read_bytes()

    if data[:2] != b"MZ":
        raise Bad("not a PE file (no MZ)")
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe : pe + 4] != b"PE\0\0":
        raise Bad("not a PE file (no PE signature)")

    coff = pe + 4
    n_sections = struct.unpack_from("<H", data, coff + 2)[0]
    opt_size = struct.unpack_from("<H", data, coff + 16)[0]
    opt = coff + 20
    magic = struct.unpack_from("<H", data, opt)[0]
    if magic == 0x10B:
        dd = opt + 96
    elif magic == 0x20B:
        dd = opt + 112
    else:
        raise Bad(f"unknown optional header magic 0x{magic:x}")

    sec_start = opt + opt_size
    sections = []
    for i in range(n_sections):
        s = sec_start + i * 40
        vsize, va, raw_size, raw_ptr = struct.unpack_from("<IIII", data, s + 8)
        sections.append((va, vsize, raw_size, raw_ptr))

    # Data directory 14 = CLI header.
    cli_rva, cli_size = struct.unpack_from("<II", data, dd + 14 * 8)
    if cli_rva == 0:
        raise Bad("no CLI header — this is not a managed assembly")
    cli = _rva_to_offset(sections, cli_rva)
    md_rva, _md_size = struct.unpack_from("<II", data, cli + 8)
    md = _rva_to_offset(sections, md_rva)

    if struct.unpack_from("<I", data, md)[0] != 0x424A5342:  # 'BSJB'
        raise Bad("no BSJB metadata signature")
    ver_len = struct.unpack_from("<I", data, md + 12)[0]
    p = md + 16 + ((ver_len + 3) & ~3)
    n_streams = struct.unpack_from("<H", data, p + 2)[0]
    p += 4

    streams = {}
    for _ in range(n_streams):
        off, size = struct.unpack_from("<II", data, p)
        p += 8
        end = data.index(b"\0", p)
        name = data[p:end].decode("ascii")
        p = end + 1
        p = (p + 3) & ~3
        streams[name] = (md + off, size)

    tbl_name = "#~" if "#~" in streams else "#-"
    if tbl_name not in streams:
        raise Bad("no #~ / #- table stream")
    t = streams[tbl_name][0]

    heap_sizes = data[t + 6]
    valid = struct.unpack_from("<Q", data, t + 8)[0]
    p = t + 24

    rows = {}
    for i in range(64):
        if valid >> i & 1:
            rows[i] = struct.unpack_from("<I", data, p)[0]
            p += 4
    row_start = p

    str_i = 4 if heap_sizes & 1 else 2
    guid_i = 4 if heap_sizes & 2 else 2
    blob_i = 4 if heap_sizes & 4 else 2

    def simple(table: int) -> int:
        return 4 if rows.get(table, 0) >= (1 << 16) else 2

    def coded(tables, bits: int) -> int:
        biggest = max(rows.get(x, 0) for x in tables)
        return 4 if biggest >= (1 << (16 - bits)) else 2

    res_scope = coded((MODULE, MODULEREF, ASSEMBLYREF, TYPEREF), 2)
    type_def_or_ref = coded((TYPEDEF, TYPEREF, TYPESPEC), 2)

    size_of = {
        MODULE: 2 + str_i + 3 * guid_i,
        TYPEREF: res_scope + 2 * str_i,
        TYPEDEF: 4 + 2 * str_i + type_def_or_ref + simple(FIELD) + simple(METHODDEF),
        FIELDPTR: simple(FIELD),
        FIELD: 2 + str_i + blob_i,
        METHODPTR: simple(METHODDEF),
        METHODDEF: 4 + 2 + 2 + str_i + blob_i + simple(PARAM),
    }

    off = row_start
    for tbl in (MODULE, TYPEREF, TYPEDEF, FIELDPTR, FIELD, METHODPTR):
        off += rows.get(tbl, 0) * size_of[tbl]

    n_methods = rows.get(METHODDEF, 0)
    stride = size_of[METHODDEF]
    for i in range(n_methods):
        yield struct.unpack_from("<I", data, off + i * stride)[0]


def check(path: Path) -> tuple[bool, str]:
    try:
        rvas = list(method_rvas(path))
    except Bad as exc:
        return False, f"unreadable: {exc}"
    except Exception as exc:  # noqa: BLE001 — a parse crash must fail the gate, not the run
        return False, f"unreadable: {type(exc).__name__}: {exc}"
    bodies = sum(1 for r in rvas if r != 0)
    if bodies:
        return False, (
            f"{bodies} of {len(rvas)} methods still have an IL body "
            "— this is a FULL assembly, not a reference assembly"
        )
    return True, f"{len(rvas)} methods, 0 with an IL body"


def main(argv: list[str]) -> int:
    root = Path(__file__).resolve().parent.parent
    targets = [Path(a) for a in argv[1:]] or [root / "libs" / "RefAsm"]

    files: list[Path] = []
    for t in targets:
        if t.is_dir():
            files.extend(sorted(t.rglob("*.dll")))
        elif t.is_file():
            files.append(t)
        else:
            print(f"error: no such file or directory: {t}", file=sys.stderr)
            return 1

    if not files:
        print(f"error: no .dll found in {targets[0]} — nothing to verify.", file=sys.stderr)
        return 1

    failed = 0
    for f in files:
        ok, msg = check(f)
        print(f"  {'ok  ' if ok else 'FAIL'} {f.name:<34} {msg}")
        if not ok:
            failed += 1

    print()
    if failed:
        print(f"error: {failed} of {len(files)} file(s) contain executable code.", file=sys.stderr)
        return 1
    print(f"verified: {len(files)} reference assemblies, metadata only, no method bodies.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
