`coverage-missing-container.bundle` is the mod's original, malformed 1.4.1
coverage resource (SHA-256 `5de4c0ecdc042c91908611ec3debde764996a91cfab08774997d7816c893109b`).
It contains our compiled shader without an AssetBundle container or asset
directory. Both the native Unity load check and CI validator must reject it.
It contains no game code and is excluded from release ZIPs.
