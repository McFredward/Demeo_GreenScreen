# Metadata-only build references

These assemblies contain type/member signatures and layout, with every method
body removed. They are build inputs and cannot run as game libraries. No game
or loader DLL is copied into a mod ZIP.

Each game has its own directory because the Mono and IL2CPP APIs differ.
`sources.json` records the original and generated SHA-256 hashes and byte sizes.
The original files remain outside Git in the owner's local game installation.

Generate again after updating a game or regenerating IL2CPP interop:

```sh
python3 scripts/make-refasm.py DEMEO /path/to/Demeo_Data/Managed
python3 scripts/make-refasm.py DEMEOBATTLES /path/to/Demeo/BepInEx/interop
python3 scripts/make-refasm.py DEMEOBATTLEMARKED /path/to/Battlemarked/MelonLoader/Il2CppAssemblies
python3 scripts/check-refasm.py
```

The generator derives the required files from the project, uses pinned
JetBrains.Refasmer 2.0.3 with `--all`, and checks every MethodDef RVA is zero
before copying any result here. The same check runs in CI, refusing full DLLs
even if they happen to be small. The scripts are adapted from GloomhavenVR
under this repository's GPL-3.0 licence.

Local builds prefer installed game files. When these are absent, the project
announces the metadata fallback. Use `-p:UseReferenceAssemblies=true` to select
it explicitly. Public loader build dependencies are downloaded separately with
`python3 scripts/fetch-build-deps.py`, with archive hashes pinned in
`scripts/loader-sources.json`. They remain in ignored `.build/loaders/`.
