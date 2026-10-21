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

## October21 no-OVR control and PC0.2.14 continuity candidate

The controlled Demeo1.4.11 run fully exits OVR Advanced Settings before the game:
OVR PID41360 disconnects at13:06:07.170; Demeo PID45732 connects13:06:27.228.
The maintainer still sees intro flashes but no menu jitter. Only two isolated
standing-origin events remain, at13:06:27.922 and13:06:54.173, with no repeated
menu bursts. This supports an OVR interaction for the position glitches; the
logs still cannot prove the writer of each origin event. Keep tracking unchanged.

The private read-only capture `frame-no-ovr-20261021-131307` freezes13 PC/Frame
files with hashes and an audit. During Frame13:06:34–49 the compositor records
28additional exact-picture draw misses and14additional opaque submits, without
new signed-None, rejected-mask or ready-mask refusals. Both interruption counters
remain unchanged through the next120seconds of menu. All14,523 certified PC
masks reach the Frame metadata path, which does not count every video picture.
The native decoder reset precedes game startup; there is no in-game hard reset.
The video process's last intermediate milestone has16,384 waits,16,368 ready,
17exact-unavailable and0emergency timeouts. This separates remaining intro
availability gaps from the former stereo/alpha defect and menu origin bursts.

PC0.2.14 repairs two independently reproduced missing-certificate schedules in
its actual production NVENC boundary. Known bitrate/framerate adjustments no
longer temporarily block an exact captured input or completed native output.
A successful native output Lock can also associate its unique live output and
input timestamp before the original Encode caller publishes its CPU return.
The matching receipt retires only with its native Unlock; the late Encode
return cannot resurrect it. Real reset/source changes, wrong or ambiguous
frames, failed/busy output calls and missing peer authority remain refused.
There is no latest-mask substitution or weakened frame/eye association.

The complete production capture/encoder/certificate/compression fixture passes
140/140 full1152×4608 R8 masks, with60concurrent copies,32outputs during native
bitrate adjustment and16outputs before Encode returns. Restoring any of the
three former barriers makes the corresponding exact-frame assertion fail. All
25 local host suites, native Windows/Linux CI and ASan/UBSan pass. These use controlled driver callbacks,
not the NVIDIA GPU encoder or headset; a new Demeo intro/menu headset test is
required before claiming every black flash gone. Cold bounded counters expose
these overlap schedules in the next PC log. See the
[companion change and evidence](https://github.com/McFredward/steam-link-native-passthrough/blob/29c5c5d0b60a8627f8ece35d3d466c8dc9ad30a4/docs/development/FRAME-CONTINUITY.md#exact-output-during-native-encoder-calls-dev-pc0214).

Only the PC patch changes. Demeo stays1.4.11, Frame stays0.2.12 and the installer
stays0.2.14. The new archive preserves all Frame binaries, helper payloads and
manifest pins byte-for-byte. Updating an already matching Frame uses the PC-only
installer path without Frame writes, settings changes or service restart. The
working Gloomhaven alpha/rendering path, shaders, source contract and native wire
format are unchanged; existing shared-path regressions remain required.

## October21 follow-up: separate origin and picture continuity failures

The maintainer's second October21 Demeo1.4.11 test still has short black flashes,
mainly during the intro, and menu glitches/unwanted recentering. The maintainer
explicitly reports no deliberate recenter or playspace adjustment. Preserve the
previously confirmed opacity/stereo correction; the PC0.2.13 cleanup candidate
does not resolve the remaining hardware failures.

The private capture is `.planning/debug/frame-1411-20261021-1250/`, including
frozen PC originals, Frame native logs, the journal, Steam's `steam_output.log`
and `console_log.txt`, SHA256 manifests and a structured audit. The compositor
survived the preceding test, so Frame totals must be differenced against that
session rather than interpreted as fresh counts. All device access was limited
to reading logs; no installation, setting write or restart was performed.

The PC native log proves the new cleanup behavior is present:49 requests,
41 empty,8 changing,0 lock misses. During the game the two genuine allocation
removals remain distinct from recurring empty identity-reader cleanup. Final
copy-pair refusals are4, versus30 in the preceding test, but these totals do not
identify every visible black frame. The mod records equal successful eye counts,
error0 and zero Unity origin notifications/discontinuous pairs. Native readback
reports4,785 certified masks, zero busy/errors; the Frame adds4,785 decoded mask
metadata records, zero upload refusals and zero guarded-submit refusals. Those
metadata totals are not a count of displayed pictures.

Between Frame journal samples12:45:04 and12:45:19, signed-None metadata stays
4,852, rejected masks stay0, and the ready-mask draw refusal stays0. However,
exact-picture draw refusals grow94 to114 and opaque submits grow70,969 to70,979.
This is an intermittent exact decoded-picture lookup failure, **not evidence of
missing uploaded mask storage**. It cannot be fixed by weakening association or
reusing a different frame's coverage. Aggregate submit/draw counts are not eye
frame counts, and do not prove the precise on-headset flash times. Fully opaque
mask content also grows by106 between12:45:14 and12:45:19; retained intro fades
are a separate possible contributor. Neither observation alone attributes every
reported flash.

Steam's separate output log supplies the video-process handoff diagnostics,
which are absent from its ordinary `vrlink.txt` log. Its last in-game milestone
records4,096 waits,4,085 ready returns,11 exact-unavailable returns,0 emergency
timeouts and21,714us maximum handoff duration. It is an intermediate milestone,
not the final session total. The compositor's corresponding wait counters are
normally zero because the waiting callback belongs to the video process. Do not
infer decoder success or failure from the wrong process's counters.

Native transport fails at Frame12:44:59.894795 with a backtrack-window/FEC error,
then hard-resets at12:45:00.895180. The PC still reports zero certified masks at
12:45:02.772 and first certifies coverage at12:45:04.076. Consequently this
particular native reset precedes the first MR mask payload. The mask bandwidth
overhead is not established as its cause. Cross-device clocks were not calibrated;
nearby PC and Frame timestamps cannot be treated as an exact latency measurement.
There is no further decoder hard reset during the later menu origin bursts.

The PC compositor records74 standing-origin reset events. Eight occur between
12:45:35.945 and12:45:36.209 without another decoder restart; the corresponding
standing translation initially changes about6.2cm. Further bursts occur at
12:45:40.992,12:45:56.068 and12:46:01.090. Most of the74 events belong to the
final12:46:06–08 burst near headset presence/tracking shutdown and must not all
be labeled ordinary gameplay glitches. These are real native origin updates,
independent of the mod's zero Unity callback count. Treating every position
change as a video reconnect is incorrect.

OVR Advanced Settings starts alongside this session. Its upstream
[MoveCenterTabController](https://github.com/OpenVR-Advanced-Settings/OpenVR-AdvancedSettings/blob/master/src/tabcontrollers/MoveCenterTabController.cpp)
can write seated/standing origins and preview chaperone transforms. This makes
it a candidate external writer, not a proven culprit: SteamVR's supplied logs
do not identify the caller of each update, and the working Gloomhaven comparison
also has that overlay installed. Inspection of the supplied Demeo assembly finds
an automatic standing reset in SteamVR input construction and input-driven
avatar recentering; static call sites do not prove the writer of the later bursts.
No game implementation or Valve runtime code is redistributed.

The next controlled headset comparison should retain the exact same mod and
companions while OVR Advanced Settings is fully exited. Record intro flashes
and menu position changes separately and collect both PC logs and the Frame
Steam output log. This distinguishes an external origin feedback/offset path
before adding tracking compensation or suppressing legitimate resets. It is
not a claim that either remaining defect has been fixed. Gloomhaven and the
PC-only installer behavior remain unchanged.

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

URP follows the actual native provider layout, including Demeo/Battles'
single two-view array pass. The companion independently requires its actual
single-slice source shape after the normal SteamVR submission/resolve path;
the internal array never authorizes an array resource in the sender companion.

Version 1.4.6's native MultiPass transition failed the maintainer's test:
both eyes appeared transparent, the right eye lost scene contents, and table
glitches increased. Its PC log has zero alpha in every sampled right-eye lobby
region while left-eye regions include alpha 255. The matching Frame log adds
4,297 fully transparent masks and no mixed masks during that game session.
The SDK texture/pass test did not exercise Unity 6 URP or SteamVR and was not
sufficient evidence that the transition would render correctly. Version 1.4.7
removes that transition, all SDK setting writes and display stop/start calls.
The earlier `XRSystem.singlePassAllowed=false` split also remains removed:
URP's ReconfigurePass uses native pass IDs, so a synthetic second pass over one
native two-view pass is invalid.

The actual pinned SDK's D3D11 single-pass submit sends the same two-slice
resource for both eyes through `IVRCompositor_026::Submit`. Version 1.4.7 added
explicit indices through `SubmitWithArrayIndex`. The maintainer's 1.4.7 test
returned to the original problem: the left eye sees through solid assets,
the right eye renders correctly, and table glitches recur. Both indexed-submit
counters advance equally with error zero. Multiple final-URP alpha samples are
255 in both views. Those sparse samples do not prove whole-object coverage or
exclude later camera work. The same installed companion works correctly with
GloomhavenVR in both eyes, confirmed in this conversation. Therefore a generic
companion failure is not established, and changing the camera layout again is
not justified by these logs.

The maintainer's 1.4.8 hardware test confirms that the separate 2D images fixed
the difference between eyes. Solid assets are now transparent in both eyes.
The matching session is Demeo PID 24488, 2026-10-20 22:10:19–22:11:46 local PC
time. The actual-submit probes repeatedly read alpha 255 in all three regions
of both eyes; no probe readback or Submit error is reported. On the Frame,
between 22:10:26 and 22:11:41, exactly 6,403 new decoded masks are classified
fully transparent, with no new mixed/opaque masks. These are content readings,
not just successful handshake counters. They localize the discrepancy after
the sampled game Submit input and before/at native mask extraction; sparse
regions still do not prove the entire image or identify a private SteamVR
instruction. Forcing game-object alpha globally would not address this evidence.

Version 1.4.9 keeps the original Unity/URP array and separate 2D targets, but
canonicalizes the exchange **storage and declaration**, not the pixel values.
`OpenVrNativeSettings` reads the supplied loaded SDK (377,856 bytes, SHA-256
`0ebad5a4ca7f4aa9b94b5f0128dcbc7bb3139fee317b91a0ec2636b448b4d702`).
It validates its exported getters, current provider and both existing stages.
Each source must be resolved two-slice RGBA on the same D3D11 device. Unity's
Linear-project eye view has already encoded SDR RGB for sRGB display. Copying
those bytes into compatible `R8G8B8A8_UNORM` storage and declaring OpenVR
`ColorSpace_Gamma` makes that contract explicit, as in GloomhavenVR's working
Gamma/UNorm image path. Demeo's project lighting stays Linear. D3D11 permits
copies within the same compatible format family without conversion; every RGB
and alpha byte is verified in the actual GPU test. No alpha threshold or RGB
premultiplication is added. Unknown explicit color-space declarations forward
unchanged rather than being relabeled.

`OpenVrEyeTextures` prepares one left/right 2D pair for each SDK stage. At the
real SDK Submit callback, `CopySubresourceRegion` copies the matching current
slice into that stage's eye target. Original Submit receives the same eye,
bounds and ordinary D3D11 texture kind. Four full-size resolved images add GPU
memory/copy bandwidth (about 249 MiB for 4040-square RGBA). There is no extra
render, shader, resolve, CPU color export, UV mapping or render-target rebind.
Stage reuse follows the provider cadence; no older frame/other eye is used.

The logs also record 248 `StandingZeroPoseReset (bResetManually)` messages
while Demeo is connected, often in rapid bursts. Their caller and exact visual
consequence are not established. `OpenVrOrigin` subscribes to the public
`XRInputSubsystem.trackingOriginUpdated` event in Demeo. A real notification
under a live Frame/OpenVR grant increments an atomic revision. On the graphics
thread, a new revision sets public `Submit_FrameDiscontinuity` for two complete
native eye pairs, then stops. A notification between eyes applies to the next
pair so flags stay coherent. Source-grant changes establish a fresh baseline.
This is the API's motion-smoothing discontinuity signal; no pose, origin,
recenter request, callback event queue or frame timing is replaced. Subscription
maintenance runs cold once per second, unsubscribes on stop/opt-out/unload, and
records at most 12 notifications. IL2CPP input callbacks remain unchanged until
their runtime delivery is validated. This cannot fix packet loss or prove that
all compositor origin resets arrive as Unity input notifications.

References: [Microsoft D3D11 CopySubresourceRegion](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-copysubresourceregion),
[Valve OpenVR texture/color-space and submit flags](https://github.com/ValveSoftware/openvr/blob/f51d87ecf8f7903e859b0aa4d617ff1e5f33db5a/headers/openvr.h).
The maintainer's 1.4.9 hardware test reports unchanged transparency in both eyes
and recurring table glitches. Its PC log again reads alpha 255 in multiple
actual-Submit regions of both eyes, without Submit/readback errors. The Frame
adds 6,309 fully-zero masks and one mixed mask. No Unity tracking-origin
notification reaches the new subscription, so no discontinuity pair is sent.
This format/declaration change is not a confirmed correction.

"Fully transparent" in the receiver counter describes the decoded coverage
bytes. It does not measure the final visible opacity or require the entire
game image to disappear. Native video RGB is preserved independently of this
mask; with a premultiplied contribution, nonzero scene color can remain visible
over the camera even at coverage zero. The log does not prove the headset's
actual blend mode or a numeric 50% opacity. The user's observed translucent
objects therefore do not contradict the zero-mask reading.

PC companion patch 0.2.12 adds a small, read-only alpha probe at the native
shared-texture input, after the existing current source/PID/eye/allocation
proof. Its `native source alpha:` records include three raw-alpha region
summaries per eye and the current native shader alpha-related constants. This
is needed to distinguish loss during the game-to-SteamVR shared-texture handoff
from loss during downstream mask generation. It changes no game render setting,
native RGB, coverage algorithm or Frame component. See the companion's
[boundary analysis](https://github.com/McFredward/steam-link-native-passthrough/blob/dev/docs/research/demeo-source-alpha-20261020.md).
The next hardware test must provide these new sender measurements; the visible
defects remain unresolved.

The next test used Demeo 1.4.9 with PC companion 0.2.12, PID 45204,
2026-10-20 23:06:56–23:08:37 local PC time. The maintainer noticed no table
glitches, but both-eye transparency remained. The PC compositor records seven
`StandingZeroPoseReset` messages, versus 79 in the preceding 22:37:33–22:38:58
session. Per-application dropped presents remain comparable: 5/8,961 versus
4/7,424. The Frame records 97 substantive packet-retry messages versus 78;
neither game interval contains a vrlink error. Decoder-reset errors in this
new connection occur before Demeo connects and must not be assigned to gameplay.
This is consistent with fewer tracking-origin bursts, not proof of a glitch
fix or better transport. The diagnostic patch does not change tracking.

All 18 emitted sender-alpha reports (eight distinct capture pairs; the final
report repeats the last pair) read zero in all three raw regions of both eyes,
without readback errors. The sampled native tint alpha is 1, direct-foveated
sampling is enabled and sharpening is zero. Both PC and Frame count 7,962
coverage masks; the Frame classifies 7,961 as fully zero and one as mixed,
with zero rejected uploads or guarded-submit refusals. Game-side probes still
include fully opaque regions and equal successful Submit counters.

These samples narrow the investigation to the submitted/source coverage path,
but do not establish a same-pixel alpha loss: native source size is 4040-square
while its later native shader UV range is only `(0,0)..(0.707178,0.707178)`, about
2857-square. Both probes currently sample raw full-texture positions, so the
last region lies outside that source crop and the capture times are not paired
across processes. Do not call every source pixel zero or identify a private
SteamVR instruction from this evidence. A current GloomhavenVR run with the same
PC diagnostic would provide the missing working-route comparison without
another mod/driver update. Original logs and the bounded audit are retained
privately under `.planning/debug/frame-pc0212/`.

The requested comparison is Gloomhaven PID 9084, first connected at 23:15:55
local PC time on 2026-10-20. It uses **OpenXR** (`SteamVR/OpenXR 2.17.10`,
Unity OpenXR 1.10, MultiPass); Demeo uses the pinned **OpenVR** provider.
Both are measured with the same PC companion 0.2.12 and Frame companion 0.2.12.
Gloomhaven has 18 emitted reports containing seven distinct captured eye pairs.
They include fully opaque 255 regions, zero background regions and genuine
fractional regions, without readback errors. Its shared source is typeless
RGBA format 27 with sRGB view 29 and full `(0,0)..(1,1)` native shader UVs.
Demeo's source is format 29/view 29 with `(0,0)..(0.707178,0.707178)` UVs.
Both report native tint alpha 1, direct-foveated sampling enabled and sharpening
zero. Source format alone is not a demonstrated cause.

The same running Frame receiver's counters move from `0/7961/1`
(opaque/zero/mixed) before Gloomhaven to `1/7961/5926` at 23:17:34: one new
fully opaque and 5,925 new mixed masks, with no new fully-zero masks. Thus the
working route carries a spatial coverage field through the same companions.
This is stronger than successful negotiation and excludes an always-zero
diagnostic or generally broken mask decoder. It does not prove which private
OpenVR conversion, viewport or game render operation loses Demeo's coverage.

Version 1.4.10 attempted to remove a cropped-texture handoff as a variable;
the subsequent hardware run below disproves that interpretation for Demeo.
`OpenVrViewport` accepts only ascending, finite normalized bounds whose edges
recover integer pixels within 1/256 pixel of normalized-float rounding. An
integer source box is copied to a matching own UNORM/Gamma 2D image, preserving
all RGBA bytes. OpenVR receives null bounds, its documented whole-image case;
the same source rectangle still spans the same eye/FOV. No game camera, XR
viewport scale, scene render, shader, pose, timing, compositor API or project
color space changes. This is a targeted candidate, not a confirmed headset fix.

The graphics callback observes the actual current SDK bounds and publishes a
packed allocation request only. GPU textures are created cold at most once
every two seconds. Until a matching pool exists, the original full-size own
2D images and original bounds remain available under the same source grant.
An immutable crop pool is published atomically; its predecessor stays alive
until all native Submit callbacks finish. At most one predecessor is retained,
and a pending retirement delays further crop allocation. There is no render-
thread allocation/wait, stale frame, array fallback or routine source-epoch
reset on a viewport change. The four full-size targets add about 249 MiB at
4040-square; four current 2857-square crop targets add about 125 MiB. A previous
crop pool can temporarily remain during replacement. Opt-out releases the
owned targets after in-flight callers retire.

Fractional, flipped, invalid or too-small bounds retain the prior 2D behavior
and original bounds. Statistics distinguish whole/cropped submits, pending
preparation and unknown bounds. The alpha probe now samples inside the actual
submitted viewport and reports that source rectangle with each eye's completed
readback; it no longer treats an unused full-texture region as viewport evidence.
The exact source/current-frame/eye checks remain independent of these samples.

The actual supplied SDK/D3D11 fixture checks both provider stages, per-pixel
RGB/all alpha values, offset and changed crops, unknown bounds, immediate full-
viewport recovery and a native Submit held across cold crop-pool replacement.
It runs the production C# callback under Windows .NET and Unity Windows Mono.
These software checks do not execute the proprietary SteamVR handoff or prove
the final headset appearance. The 1.4.10 hardware run is recorded below.

### 1.4.10 hardware result and the 1.4.11 filter candidate

The maintainer reports continuing both-eye transparency and more frequent table
glitches. Demeo PID 45184 ran from 23:52:45.196 to 23:54:03.859 local PC time on
2026-10-20. Logs were pulled read-only and frozen with the supplied PC logs in
`.planning/debug/frame-1410-20261020-2356/`. The device is no longer needed for
this offline analysis. The last mod report has 4,722 successful submits per eye,
9,444 whole-image submits, **zero cropped/pending/unknown viewports**, alpha
samples completed for both eyes, and no Submit/readback errors. Thus the earlier
claim that Demeo's public Submit passes a 2857-square crop was wrong. The native
`app-uv` diagnostic describes a later SteamVR/Steam Link boundary, not the app's
public Submit bounds. Retain that distinction when interpreting previous logs.

The sender has 16 reports/eight distinct captured pairs. The first right-eye
report includes 255 regions with full native UVs; the other reports have zero
regions and approximately 0.707178 native UV extents. Source alpha is therefore
not universally unavailable to OpenVR. The latest Frame counters add 5,329
fully-zero and one mixed mask after the Gloomhaven comparison, with no rejected
mask uploads. Sparse samples remain neither a whole-image nor synchronized
same-pixel/frame proof. The game records nine dropped presents out of 6,922;
the Frame has 80 substantive retry messages/5,189 retry packets in the game
interval. Those counters do not establish the visual cause of table glitches.

Private offline inspection of the locally retained SteamVR client identifies
an optional supersample-filter decision in its shared-image copy path. This
uses public `IVRSettings_003.GetBool("steamvr", "allowSupersampleFiltering")`.
An offline Windows-runtime probe also executes the actual private client's
factory and interface lookup, without Init or a running SteamVR server, and
confirms that the copy path's settings lookup returns the same public interface
object. No runtime binary, shader or disassembly is shipped. This establishes
an addressable decision, not the exact alpha-loss instruction or a headset fix.

`OpenVrSubmitSettings` leases only this settings object's vptr, preserving the
RTTI locator and all 11 public methods except a scoped GetBool wrapper. The
wrapper returns false/VRSettingsError_None only for the exact section and key
inside the same thread's eligible original eye Submit, and only while the
identical live source grant remains valid. Other settings, other sections,
prefix/suffix keys, outside-Submit reads, other threads, unknown eye inputs and
revocation forward to the original getter. No Set*, file write, persistent/global
SteamVR setting, reserved submit flag, shader replacement or additional render
is involved. Detachment uses compare/exchange and retains the small table and
callback for fetched native callers; a foreign interface owner is preserved.
A native counter reports actual scoped filter queries, so a zero count cannot
be mistaken for a successfully exercised bypass.

The actual provider/D3D11 fixture now checks the native bool/error ABI, exact key
matching, two eyes/stages, outside-scope reads, another thread while a native
Submit is held, source revocation, settings detachment/reattachment and foreign
ownership, alongside per-pixel RGBA and resource-lifetime regressions. These
checks execute the production callback under Windows .NET and Unity Windows
Mono. They do not run a complete SteamVR copy/filter/encoder pipeline or prove
the final headset appearance. The next Demeo test needs mod 1.4.11 and the
existing PC/Frame 0.2.12 components. Table glitches remain unresolved; no tracking
or motion-smoothing behavior is changed in this candidate.

References: [Valve OpenVR Submit bounds](https://github.com/ValveSoftware/openvr/wiki/IVRCompositor%3A%3ASubmit),
[Khronos OpenXR layer blending](https://registry.khronos.org/OpenXR/specs/1.1-khr/html/xrspec.html#composition-layer-blending).

`OpenVrEyeSubmission` leases the old compositor interface object's vptr with
an atomic compare/exchange. Its private table retains the MSVC RTTI locator and
all 51 public methods; only Submit redirects. No executable instructions or
global method tables are modified. Only registered sources, D3D11/default input
flags, auto color space, valid eyes and the identical live source grant redirect. Unknown inputs forward
unchanged. Revocation immediately stops copying. Source replacement prepares new
resources on the Unity Update path after old users leave; it never waits on a
graphics callback. Detached in-flight callbacks retain their resources until
the cold collection tick can release them. Callback/table storage stays rooted
until process exit, and cleanup preserves a foreign vptr owner.

The same-submit alpha probe uses two small staging images and event queries.
It captures three 32-square regions from each actual copied eye target every
two seconds, polls once with `DONOTFLUSH`, and maps once with `DO_NOT_WAIT` only
after its query completes. No diagnostic Flush or blocking wait is added.
Only alpha summaries leave the graphics callback; no image/RGB capture is
exported. Counters and min/mean/max/zero/partial values appear in the bounded
`OpenVR separate 2D eye submission` log: six early reports five seconds apart,
then 14 reports 30 seconds apart, covering a long intro and the later menu.
This measures the actual Submit input after all Unity camera work, rather than the earlier URP-camera-end snapshot.
Probe setup/readback failure cannot authorize a source and does not alter eye
copying. The native companion's session/frame/eye/encoder proof remains required.
No Frame files or configuration are changed.

The matching 1.4.7 Frame session also records 72 substantive FEC retries, one
submission without metadata and two late-metadata errors. These can account
for transport disturbance, but no timed visual observation proves that they
caused the reported table glitches. A mod-only build does not establish a
network fix. The remaining handoff work addresses the source texture contract;
source-alpha and transport evidence must stay distinct in hardware validation.
The 1.4.8 session also has 78 substantive FEC retries between 22:10:19 and
22:11:40; no camera/texture change is evidence of a network repair.

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

The 1.4.4 headset run did not produce XR-output measurements. Its preflight
mistakenly treated `XRRenderParameter.viewport` as pixels, whereas the actual
Demeo `XRSystem.BuildView` multiplies its normalized rectangle by the target
width/height. The diagnostic stopped with the reported viewport/slice error
before issuing any GPU readbacks. The reported eye defects still occurred;
this run supplies no final-output alpha evidence. This was a diagnostic bug,
not a newly established cause of the rendering defect.

Version 1.4.5 applies the same float coordinate conversion as `BuildView`,
corrects the simulated provider to return normalized rectangles, includes the
raw and converted viewport/slice/extent in failures, and permits four bounded
preflight failures with a two-second delay instead of permanently stopping
on the first transient invalid viewport. Native source admission is unchanged.

The maintainer's 1.4.5 run on 2026-10-20 still has the intro/right-eye and
left-eye transparency defects. Unlike 1.4.4, it completes all 24 final-XR
measurement pairs: ten on `Main Camera`, fourteen on
`MainCamera_VR_Lobby_HighQuality`. Both views use pass 0, separate slices 0/1,
and 4040-by-4040 RGBA8 sRGB, single-sample provider targets; the logged
final-blit input is still 8x MSAA. Both middle samples are entirely A255 in
18 of 24 pairs. All six samples are A255 at frames 309, 489, 1212 and 2000,
and all six are A0 at frames 1574, 1754 and 1934. This does not establish
uniformly translucent left-eye alpha at the measured Unity boundary. Samples
are not labelled game objects, however, and cannot certify the complete image,
unmeasured frames, RGB or any later submission/conversion/rendering step.

The corresponding PC session is 20:31:17--20:33:10 CEST (Demeo PID 46816).
The Frame logs were collected read-only at 18:34:19 UTC into the ignored
`frame-145` debug directory. They show no rejected mask uploads, quarantined
pool entries, guarded-submit refusals or device loss. These cumulative counters
do not prove correct per-eye mask content or mapping. The retained `vrlink`
log has 59 substantive packet/frame retries, decoder recovery at startup and
late metadata for frames 4763 and 9939. These are possible causes of brief
flicker/ghosting, without a timestamped observation proving that connection;
they do not explain the persistent one-eye defect by themselves.

Taking over camera properties is already part of this adapter. Replacing
Demeo's complete VR rendering would additionally require ownership of its
camera transitions, stereo views, fades, UI and native presentation. The
GloomhavenVR scene implementation controls its own rig and currently requires
built-in Forward, Gamma SDR and MultiPass; Demeo uses Unity 6 URP with a native
two-view array. Cloning a Camera or disabling URP single-pass rendering alone
does not reproduce that presentation contract. In particular, the previously
rejected split-pass workaround cannot be reintroduced over a one-pass provider.
At that 1.4.5 checkpoint the next unmeasured boundary was the actual per-eye
OpenVR/SteamVR input before native coverage extraction. Those logs alone did
not establish a new eye fix. Version 1.4.9 addresses that handoff through per-eye native 2D submission;
its actual managed/native boundary test is described below.

The submission adapter has an additional private-input test:

```sh
python3 scripts/check-openvr-provider.py --provider /path/to/the/games/XRSDKOpenVR.dll
# Also exercise Unity's Windows Mono reverse callback (licensed Editor needed):
python3 scripts/check-openvr-provider.py --provider /path/to/XRSDKOpenVR.dll --unity /path/to/Editor/Unity
```

The file pin rejects a changed copy before native loading. The real SDK runs
under Wine with owned offline Unity/OpenVR interfaces and actual D3D11 textures.
A negative control confirms its original identical array/no-index submissions.
A Windows .NET host then executes the **production C# adapter** against that
SDK. Two stages and three updates have distinct per-eye RGB and all alpha values;
the production boundary runs on a native graphics worker and must copy the
correct slice to a distinct 2D target for every stage and preserve every byte.
Source arrays must also remain byte-identical. The new UNorm targets must
preserve every sRGB-source byte and declare Gamma. A notification injected
between native eye callbacks must mark only the next two complete pairs with
`Submit_FrameDiscontinuity`, retaining ordinary flags before/after. The same-submit diagnostic must
complete actual GPU queries/readbacks for both eyes. The optional licensed-Editor path runs
the same production callback in Unity 2021.3.5's actual Windows Mono runtime,
covering Mono reverse-P/Invoke independently of the .NET host. This is runtime
ABI evidence; it is not Unity 6 scene-rendering or headset evidence.
Live source revocation must return to ordinary submission before detachment,
and detachment must restore the original interface while preserving a simulated
foreign owner. Opt-out during a held native Submit must retain its live 2D
resource, return without waiting, and allow reattachment after that call exits. Unknown textures, other graphics APIs and non-default flags must
retain ordinary forwarding. The older native mode
transition fixture remains as SDK contract evidence, not a shipping behavior.
No SteamVR server, encoder or headset is exercised; those remain hardware
validation limits. Only public interface headers are downloaded. Private SDK
copies and the owned Wine prefix are removed; small receipts/logs remain in
`.build`. Portable lifecycle checks cover readiness, failure/foreign ownership,
absence of display restarts and original two-pass games across all six builds.

Additional native API validation is available with a licensed Unity 2021.3
Editor and a Vulkan graphics device on Linux:

```sh
python3 scripts/check-xr-readback.py --unity /path/to/Editor/Unity
python3 scripts/check-xr-readback.py --unity /path/to/Editor/Unity --reject-normalized-bug
```

The runner compiles the actual production `SrpAlphaProbe.cs` into a temporary
Editor project with unrelated loader/lifecycle services stubbed. It exercises
the production viewport conversion and completion summaries with real GPU
readbacks of known per-view/per-pixel alpha values: 2D and 2D-array targets,
UNorm/sRGB storage, and 1x/4x MSAA. The positive run passed 32,842 checks on
Vulkan/Intel RPL-P; no Windows D3D11, 8x-MSAA or headset result is implied.
The negative control restores the old pixel assumption and must fail the
full-viewport assertion, rather than merely failing to compile. Temporary
outputs remain under `.build`; no game code or images are used or exported.

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


## Confirmed Demeo1.4.11 result and remaining continuity work

The maintainer's October21 test confirms that1.4.11 removes unwanted asset
translucency and makes both eyes agree. The matching11:20:42–11:22:19 capture
has7,413 successful original submits per eye and14,826 scoped supersample-filter
bypasses, with no Submit errors or actual cropped public submits. Native source
samples now contain spatial alpha0/255 in both eyes. Preserve this correction;
a switch to OpenXR/OpenComposite is not required to fix the demonstrated alpha
loss. This does not establish Battles/Battlemarked headset results.

The remaining isolated black flashes coincide with individual mask-free certified
pictures during otherwise active MR. The PC has zero readback busy/errors
and the Frame zero upload/guard refusals; this is distinct from the former
uniformly-zero alpha. A source regression in the companion reproduces empty
native client cleanup invalidating an unchanged in-flight game pair. The PC-only
0.2.13 candidate changes epochs only for actual owned removals, still revoking
before native destruction and retaining all exact frame/eye/source proofs.
Bounded cleanup counters will distinguish observed causes in the next run.
The old aggregate logs cannot attribute every short flash to that defect.

A separate longer interruption starts with native Frame backtrack/FEC failure
at11:21:29.666, followed by late packets, a decoder hard reset at11:21:30.712,
PC encoder/control recovery and camera pauses/resumes through11:21:31.962.
Origin reset events also occur. These are real recovery events; their initial
packet/scheduling cause and the caller of every origin reset remain unresolved.
The continuity candidate does not claim to fix the longer reset. No speculative
bitrate, quality, tracking origin or native FEC changes are made.

The working Gloomhaven path is a regression constraint. Its supplied OpenXR
MultiPass capture reaches the same public compositor boundary as separate
4040-square format27 shared eyes. The candidate exercises that shape for2,048
successive exact pairs, preserving original per-eye pose, projection, bounds
and handles, alongside common certification and opaque fallback checks. This
is software boundary evidence, not an executed Gloomhaven or headset test.
Demeo1.4.11, Frame0.2.12, game alpha/shaders and the authenticated wire format
remain unchanged. Updating/testing the PC candidate is a separate user-run
installer step; no Frame write or restart is performed during log collection.
