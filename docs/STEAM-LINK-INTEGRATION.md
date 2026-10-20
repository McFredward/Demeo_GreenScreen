# Steam Link native source

All three games support BepInEx and MelonLoader entry points with the same
configuration, source negotiation, camera owner and URP adapter. Native passthrough is enabled by default only on a confirmed Frame / Steam Link
session. The active INI is included in each ZIP.

`NativePassthrough.Tick` runs on the Unity thread through BepInEx Mono Update,
a BepInEx IL2CPP driver, or MelonLoader OnUpdate. Battlemarked's environment
removal uses the shared maintenance tick, including late-created scene objects.
Config polling and camera discovery run on a cold tick. Existing colour and table settings remain valid.
The Unity main stereo camera is preferred; without one, a unique active stereo
game camera is required. Scene/camera replacement and target changes retire
the previous camera state.

## Headset and transport admission

The isolated background reader queries connected HMD0/class and public OpenVR
tracking/model/registered-driver properties. It accepts canonical Steam Frame /
vrlink and the observed Deckard MP / cv profile used by this Frame, as in
GloomhavenVR. It never accepts cv or SteamVR alone. The helper and public
OpenVR application SDK loader are embedded with length/SHA-256/PE checks and
extracted into the mod's own local cache. The Unity process never initializes a
second OpenVR client. Artifact provenance and source are in `assets/steamlink`
and `tools/steamlink-connection-reader.c`; the Valve BSD notice ships in the ZIP.

The game's active XR Management loader must also be OpenVR or OpenXR running
`SteamVR/OpenXR`; a separate SteamVR session cannot authorize a game using
Virtual Desktop's native OpenXR runtime. A positive identity is rechecked every two seconds, expires if refresh stalls,
and is invalidated immediately on HMD device/config changes. Other headsets and
transports restore the regular background and renderer settings regardless of
the INI value. Identity is cooperative public device metadata, independent of
source negotiation and receiver/texture/frame proof. Both gates are required.

Starting with 1.4.3, unknown/pending identity keeps an opaque black clear.
Only a positive other-headset/other-transport result or a non-Windows platform
selects the legacy colour while the native preference is enabled. Early camera
callbacks apply this rule even before the first mod Update. Opting out still
selects the configured legacy colour. These states do not authorize transparency.

Active cameras are refreshed each Update using a reusable `Camera.GetAllCameras`
buffer. The previous one-second discovery interval could retain the old camera
across the intro/lobby transition and withdraw source permission. Discovery now
sees the replacement in that Update without needing the game-startup patch.
The intro and its fades are retained; no scene is skipped.
The URP camera test now also follows `XRLayout.AddCamera`'s explicit-target
exclusion. A camera rendering into a texture is not an XR-display owner even
when tagged MainCamera with `allowXRRendering` and Both eyes. Such cameras no
longer steal the source choice, have their post-processing changed, or trigger
repeated native preparation merely by being created for offscreen work.

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

URP retains the game's original stereo layout. It may render two views in a
single two-slice XR pass. The companion independently requires its actual
single-slice source shape after the game's normal submission/resolve path;
the internal array never authorizes an array resource in the companion.
URP's legacy XRSettings flag is not used as proof of the active SRP layout.

The earlier forced `singlePassAllowed=false` split was removed after the
maintainer reported a black right eye. In the supplied Unity 6000.0.60f1 code,
`CreateDefaultLayout` can split one native render pass into two XRPass objects,
but `ReconfigurePass` then uses each XRPass's multipass ID as a native render-pass
index. It also assigns all native views to the split pass. The second split
pass therefore need not correspond to an existing native render-pass index.
Preserving the native layout avoids this invalid reconfiguration without
copying a left eye, changing resolution, or accepting a previous eye image.
This is a source-proven correction; headset verification is still required.

`m_AllowPostProcessAlphaOutput` requests alpha preservation. The adapter also
temporarily disables `renderPostProcessing` on active game cameras: URP can
strip alpha-output shader variants when building a player, and changing an
asset field cannot restore stripped programs. This first integration bypasses
those effects rather than claiming that an unverified post-processing path
preserves coverage. Original camera settings are restored on exit.

The opaque-coverage path added in 1.4.1 repairs alpha in URP Forward/Forward+;
1.4.2 supplies the corrected loadable bundle described below. Version 1.4.0
relied on native opaque-shader alpha in URP; the maintainer reported continuous
slight transparency through solid objects in the left eye. Such alpha is not
a reliable opaque-coverage contract. The new postfix on
`DrawObjectsPass.ExecutePass` records an alpha-only reset and opaque-depth stamp
inside the existing raster pass, after opaque geometry and before transparents.
Both URP Render Graph and compatibility rendering call this same method in the
supplied assemblies. Other renderers are refused. No extra scene rendering,
depth-texture sampling, colour key, render-target replacement or eye copying is
involved. Transparent shaders retain their native blend semantics and draw order.

The embedded shader uses Unity stereo instance/array-output macros. The current
XR instance multiplier supplies both eyes; the mod does not multiply the draw's
instance count again. Each slice retains its own depth and MSAA samples. The
legacy built-in Forward path uses the same shader after opaque geometry.
The bundle was built with Unity 2021.3.5f1 for D3D11. Its build enables VR/stereo
compilation and checks that both ordinary and stereo-instanced variants reach
the shader compiler. Unity otherwise strips VR variants when VR support is
disabled ([Unity shader stripping documentation](https://unity.com/blog/engine-platform/stripping-scriptable-shader-variants)).

The maintainer's 1.4.1 run stopped at `The bundled stereo opaque-coverage shader
could not be prepared`. The build project had omitted
`com.unity.modules.assetbundle`. Unity still returned a build manifest and wrote
shader data, but warned that the AssetBundle module was disabled. The shipped
file contained a Shader object without the AssetBundle container or asset
directory; native `AssetBundle.LoadFromMemory` rejects that exact file. The
previous source-shader GPU checks never attempted to load the shipped bundle.

Version 1.4.2 enables the built-in module and requires a native load of the
produced file, its exact asset directory and Shader asset after building. CI
independently parses the bundle, validates the directory's shader reference and
compiled profile, and rejects the original 1.4.1 file retained as a regression
fixture. The produced-DLL checks compare the embedded bundle bytes against that
validated file. No game objects or code are included in this bundle.

Runtime failures now identify the missing resource, native bundle rejection,
missing Shader, wrong name or unsupported graphics API. A permanent embedded
resource failure prevents renewed source negotiation for that mod lifetime;
restart/reload clears it. This avoids the 1.4.1 reconnect/log loop while keeping
the background opaque when preparation fails.

All modified camera and pipeline settings retain an original value and restore
only a value still owned by this mod. Native permission loss releases them.
The user's eye size, MSAA, render scale, foveation and unrelated Steam Link
settings are not modified.

## Validation

Run the portable production-code checks:

```sh
dotnet run --project tests/ProtocolChecks.csproj
dotnet run --project tests/IdentityChecks.csproj
python3 scripts/check-integration.py
```

Protocol checks execute the real parser and observer, including malformed or
partial records, nonce/PID mismatch, revocation, stale callbacks, no overlapping
workers, and connection lifetime. Integration checks execute the production
configuration and rendering lifecycle against explicit Unity/loader boundaries,
including Linear/sRGB XR arrays, URP stereo policy, HDR alpha precision,
mid-frame permission loss, target replacement, live opt-out and restoration.
They also exercise the opaque raster hook with Mono fields and IL2CPP-style
properties, skip transparent and foreign-camera passes, refuse stale frames,
and remove the owned hook on unload.
They do not execute Windows pipes, a real XR runtime, Unity GPU rendering or the
native encoder/receiver chain.

`CoverageGpuChecks.Run` in the Unity editor separately executes the actual
coverage shader on an Intel RPL-P Vulkan GPU. Different opaque triangles in
the two eye slices start with incorrect alpha. The single-pass repair matches
independent per-eye depth repairs at every pixel, preserves RGB and retains
fractional 4x MSAA edges. The 1x and 4x runs plus the original broken-bundle
negative control passed 8,209 checks. Run without
`-nographics` using `-executeMethod CoverageGpuChecks.Run`. This exercises the
shader source on Vulkan. The same run natively loads the actual Windows bundle
and its Shader asset and rejects the broken 1.4.1 bundle; it does not execute
the Windows D3D11 shader programs or a headset stream.
Set `DEMEO_COVERAGE_REJECT` to the absolute path of
`tests/fixtures/coverage-missing-container.bundle` to include that negative control.

The latest Frame logs were read over SSH without changing device files or
settings. In the 2026-10-20 run, the PC mod repeatedly failed coverage-shader
preparation. The provider logged only 11 packaged masks;
the Frame received mostly metadata without coverage and recorded one guarded
submit by the end of that session. This agrees with the preparation/reconnect
loop and the reported black background. It is not a headset result for 1.4.2.

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

The maintainer's subsequent 1.4.2 test confirms native shader loading and the
opaque raster hook, but still reports left-eye transparency and a black right
eye during the intro. Receiver counters show paired guarded draws, not the
alpha values in either Unity eye image. The green startup and stale-camera
paths above are source-proven fixes. The remaining stereo defect is unresolved;
1.4.3 is a measurement checkpoint, not a certified correction for that defect.

The 1.4.3 probe observes `FinalBlitPass.ExecutePass` and, after camera submission,
asynchronously reads a 32-by-32 region at the centre of the actual source
viewport in each source slice. It reports only alpha min/mean/max and the
zero/partial/opaque counts, camera/frame, source format/MSAA/viewport and the
final pass's alpha-output flag. There are at most 24 requests, two seconds
apart, and one outstanding request. No RGB values or image files are exported.
It does not change render settings, source permission or masks. This measures
the Unity final-blit input; it does not measure submitted OpenVR textures,
native conversion or Frame output. These values are needed to localize the
remaining defect before changing that chain.

The maintainer's 1.4.3 run confirms that the green startup flash is gone,
while the intro/right-eye and left-eye transparency defects remain. The 24
input measurements have 15 fully opaque centre pairs, three fully zero pairs,
and six different/mixed pairs, with an RGBA8 sRGB, 8x-MSAA intermediate.
These small samples are not identified opaque objects: stereo parallax and
camera movement can legitimately change them. They do not establish an
always-translucent left input, a correct final blit, or a correct native mask.

The same run's Frame logs contain 77 packet/frame retries in the latest
19:44 session and an unrecoverable FEC error at 19:45:35.703, followed by late
metadata, empty decoder submissions and encoder reset. Those events can
explain brief ghosting/jumps, but there is no timestamped observation linking
the reported table artifact to that event. They do not explain the persistent
eye defect. The Demeo plugin log contains no plugin exception; the BepInEx
Unity-log-writer startup failure limits visibility into Unity-native messages.
Frame counters show no rejected mask uploads, quarantined pool entries,
guarded-submit refusals or device loss. They are cumulative, not eye proof.

Starting with 1.4.4, the probe reads the XR provider's actual targets from
`XRDisplaySubsystem.GetRenderTextureForRenderPass` after camera submission.
The final-blit input is used only for descriptor logging. The probe follows
each pass's actual `GetRenderParameter` slice and viewport, and requests a
32-pixel-wide vertical strip per view. It reports alpha summaries for three
32-by-32 regions at 25/50/75 percent of that viewport's height. A pair has one
Unity frame number; independently completed callbacks retain their own view
identity, and no next pair starts before both replies complete. There are at
most 24 pairs, at least two seconds apart. Readback storage is bounded to
2 MiB per view (normally about 0.5 MiB at 4040 pixels), with no file or RGB
exports. No pooled URP input is asynchronously read, no partial MSAA resource
copy is attempted, and no source mask, render setting, XR layout or Frame
configuration is changed. Provider/descriptor mismatches disable only the
probe. Late callbacks after unload cannot emit new-session measurements.

This remains a diagnostic checkpoint, not an eye fix. Final XR alpha still
is not evidence of the SteamVR conversion, encoded mask or Frame output.
The actual provider readback needs the next headset run; compilation and
lifecycle tests are not that runtime result.

A separate Windows Unity 2021.3 player loaded the exact shipped D3D11 bundle
under Wine 9/wined3d and passed the single-sample stereo/depth/RGB comparisons.
Its 4x MSAA readback fixture returned zero RGB and alpha even before the repair,
so that part is not valid evidence for the game or an MSAA correction. The
Vulkan shader/MSAA evidence above is unchanged. No headset output was captured.

Reference source checkpoints:

* Companion: `751dda4851f2c7ceaadcc60a945cb92771cd3098`.
* GloomhavenVR source-client/coverage example: `2b9ef4009ddd5240f20db8af139923778d23d1b9`.
* Supplied Demeo Assembly-CSharp SHA-256: `726ab6a4aaf502a4bcce0c13e288cea3a899b8d4c27241c8aa11676ebcb74e1e`.
* Supplied Demeo URP runtime SHA-256: `5bc1a4c2d8d461ab6ab8be3a5933b2f14e96f76b04ac12f0ce8b731d8b1825c7`.
* Coverage bundle SHA-256: `084a7608526982ac5736929d39723671b16502e1c1fd58691a8d16499034a600`.

## Reading runtime logs

Check the loader log for `GreenScreen configuration:` (absolute INI path and
`NativePassthrough` value) and `GreenScreen Unity Update is active.`. The green
Virtual Desktop background is expected with `NativePassthrough=false` or a
confirmed legacy headset/transport. Pending identity stays black in 1.4.3.
New installs default to `true`. The installer prints the configuration path;
startup also reports when no INI was found. Configs in the loader root remain
available even when the game's working directory is elsewhere.

In version 1.4.2, `Opaque coverage shader loaded from the embedded AssetBundle`
confirms successful resource loading. `URP opaque depth coverage is active` confirms that the first
opaque repair actually recorded commands, beyond merely preparing its hook.

A SteamVR provider log with `no-declaration` for the game's PID and
`masked-frames=0` shows that native coverage was not delivered. It does not
identify whether the option was disabled, configuration was missing, source
eligibility failed, or negotiation failed; correlate it with the mod log.
`AcquireSync FAILED with WAIT_TIMEOUT` and `ReleaseSync FAILED` are separate
native texture-synchronization failures. If they occur before the game's
connection, a game-only render-setting change cannot explain their onset.
These messages alone do not identify the responsible component.
