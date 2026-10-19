# Steam Link native source

All three games support BepInEx and MelonLoader entry points with the same
configuration, source negotiation, camera owner and URP adapter. Native passthrough is disabled by default.

`NativePassthrough.Tick` runs on the Unity thread through BepInEx Mono Update,
a BepInEx IL2CPP driver, or MelonLoader OnUpdate. Battlemarked's environment
removal uses the shared maintenance tick, including late-created scene objects.
Config polling and camera discovery run on a cold tick. Existing colour and table settings remain valid.
The Unity main stereo camera is preferred; without one, a unique active stereo
game camera is required. Scene/camera replacement and target changes retire
the previous camera state.

## Source contract

The client implements the companion's [application source API v1](https://github.com/McFredward/steam-link-native-passthrough/blob/main/docs/protocols/application-source-v1.md):
128-byte SBN1 records, profile 1, a fresh nonce, kernel pipe-server PID checks,
canonical registered SteamVR runtime paths, complete-record validation, nonzero
provider binding/generation, and a live process-owned connection. Process,
registry and pipe work stays off the rendering thread. There is one observer;
retries cannot overlap teardown. Healthy connections have no feature-expiry
timer. EOF, disablement and malformed replies revoke permission immediately.

Source permission is sampled before rendering a Unity frame. Camera callbacks
can revoke that frame's permission, but cannot activate its remaining eye.
The real render boundary rechecks permission, target, stereo eligibility,
clear policy, colour format and the owned URP settings. There is no old eye,
cached mask, CPU pixel export, RGB atlas, resolution change or FOV change.

The game declares its ordinary full-size RGB contribution plus independent
coverage alpha. The native companion independently proves the actual submitted
resource, current owner, single-slice shared allocation, encoder input, access
unit and decoded image. Source negotiation alone grants none of those proofs.

## Unity 6 and Linear lighting

The supplied Demeo and Battles files use Unity 6000.0.60f1; Battlemarked uses
6000.3.7f1. All three PlayerSettings use Linear colour space and URP. Demeo and
Battles' OpenVR settings request single-pass instanced rendering. Checking only
`XRSettings.stereoRenderingMode` and requiring a Gamma **project** would therefore
reject these games.

The adapter retains Unity's normal sRGB encoding of Linear-lit RGB, and accepts
only SDR RGBA output storage. Native sender source-view admission supports both
RGBA/BGRA UNorm and their sRGB views. The game's lighting space is not changed,
and RGB is not multiplied by alpha again. HDR headset output is refused; HDR
intermediates use the alpha-capable 64-bit precision while this source is owned.

URP's `XRSystem.singlePassAllowed=false` creates separate rendering passes for
the eye views. URP may retain a two-slice XR allocation internally. It resolves
the original eye output through its ordinary native submission path; that
internal allocation never authorizes an array resource in the companion. The
companion still independently requires its actual single-slice source shape.
URP's legacy XRSettings flag is not used as proof of the active SRP layout.

`m_AllowPostProcessAlphaOutput` requests alpha preservation. The adapter also
temporarily disables `renderPostProcessing` on active game cameras: URP can
strip alpha-output shader variants when building a player, and changing an
asset field cannot restore stripped programs. This first integration bypasses
those effects rather than claiming that an unverified post-processing path
preserves coverage. Original camera settings are restored on exit.
Opaque and transparent game shaders supply their native coverage, including
their real draw order and depth. ShaderGraph's ordinary separate alpha blending
is retained. No mask is inferred from the final image's colour.

The legacy built-in Forward path additionally stamps opaque coverage from the
same current-eye colour/depth/MSAA attachments after opaque geometry. The
embedded two-pass shader changes alpha only. Transparent shaders then retain
their native blend semantics. The bundle was built with Unity 2021.3.5f1 for
D3D11, and is optional for the Unity 6 URP path.

All modified camera and pipeline settings retain an original value and restore
only a value still owned by this mod. Native permission loss releases them.
The user's eye size, MSAA, render scale, foveation and unrelated Steam Link
settings are not modified.

## Validation

Run the portable production-code checks:

```sh
dotnet run --project tests/ProtocolChecks.csproj
python3 scripts/check-integration.py
```

Protocol checks execute the real parser and observer, including malformed or
partial records, nonce/PID mismatch, revocation, stale callbacks, no overlapping
workers, and connection lifetime. Integration checks execute the production
configuration and rendering lifecycle against explicit Unity/loader boundaries,
including Linear/sRGB XR arrays, URP stereo policy, HDR alpha precision,
mid-frame permission loss, target replacement, live opt-out and restoration.
They do not execute Windows pipes, a real XR runtime, Unity GPU rendering or the
native encoder/receiver chain.

All three variants have also been compiled against the supplied actual
Unity/interop assemblies, using BepInEx 5.4.22.0 (Mono), BepInEx 6 BE 789
(IL2CPP) and MelonLoader 0.7.3. Both loaders are compiled for every game.
The metadata-only CI builds are checked separately. A successful IL2CPP
compilation is not a runtime or headset result.

No headset run is certified here. Native alpha preservation across D3D11/XR
submission, both eyes, translucent/additive game materials, depthless effects,
post-processing, camera stacks, loading, Steam menus, disconnect/reconnect and
shutdown require per-game headset verification. An alpha-capable target alone
does not establish correct coverage for every custom material. Unknown effects
can still emit incorrect alpha even when native source negotiation succeeds.

Reference source checkpoints:

* Companion: `751dda4851f2c7ceaadcc60a945cb92771cd3098`.
* GloomhavenVR source-client/coverage example: `2b9ef4009ddd5240f20db8af139923778d23d1b9`.
* Supplied Demeo Assembly-CSharp SHA-256: `726ab6a4aaf502a4bcce0c13e288cea3a899b8d4c27241c8aa11676ebcb74e1e`.
* Supplied Demeo URP runtime SHA-256: `5bc1a4c2d8d461ab6ab8be3a5933b2f14e96f76b04ac12f0ce8b731d8b1825c7`.
* Coverage bundle SHA-256: `3cf010b2828442b4e9d1f2a97225de8a3a7f0f34169cc839d3673912558741d1`.
