// Executes the user's private, pinned SDK with owned Unity/OpenVR boundaries.
// The real provider chooses targets, creates its pass descriptors and submits
// actual D3D11 textures. No Valve binary or disassembly belongs in test output.
#include <windows.h>
#include <d3d11.h>
#include "ProviderInterface/IUnityGraphics.h"
#include "ProviderInterface/IUnityXRDisplay.h"
#include "provider-pins.h"
#include <array>
#include <cmath>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <exception>
#include <functional>
#include <map>
#include <stdexcept>
#include <vector>

namespace {
unsigned checks = 0;
void check(bool value, const char* label) { ++checks; if (!value) throw std::runtime_error(label); }
template<class T> struct Com { T* p = nullptr; ~Com() { if (p) p->Release(); } T** out() { return &p; } };
struct Image { UnityXRRenderTextureDesc desc; ID3D11Texture2D* texture; };
std::map<UnityXRRenderTextureId, Image> images;
Com<ID3D11Device> device; Com<ID3D11DeviceContext> context;
UnityXRRenderTextureId serial = 0;
bool fixtureSrgbStorage = false;
IUnityGraphics graphics{};
IUnityXRDisplayInterface display{};
void* registeredProvider = nullptr;
const auto Handle = reinterpret_cast<UnitySubsystemHandle>(0x1144);
UnitySubsystemErrorCode UNITY_INTERFACE_API registerGraphics(UnitySubsystemHandle handle, const UnityXRDisplayGraphicsThreadProvider* provider) {
    check(handle == Handle && provider && provider->Start && provider->Stop, "actual lifecycle registers valid graphics callbacks");
    registeredProvider = provider->userData;
    return kUnitySubsystemErrorCodeSuccess;
}
UnitySubsystemErrorCode UNITY_INTERFACE_API registerMain(UnitySubsystemHandle handle, const UnityXRDisplayProvider* provider) {
    check(handle == Handle && provider && provider->userData == registeredProvider, "main and graphics callbacks share the same native display owner");
    return kUnitySubsystemErrorCodeSuccess;
}
UnityGfxRenderer UNITY_INTERFACE_API renderer() { return kUnityGfxRendererD3D11; }
IUnityInterface* UNITY_INTERFACE_API getInterface(UnityInterfaceGUID guid) {
    if (guid == GetUnityInterfaceGUID<IUnityGraphics>()) return &graphics;
    return nullptr;
}
IUnityInterface* UNITY_INTERFACE_API interfaceSplit(unsigned long long a, unsigned long long b) { return getInterface(UnityInterfaceGUID(a, b)); }
UnitySubsystemErrorCode UNITY_INTERFACE_API create(UnitySubsystemHandle h, const UnityXRRenderTextureDesc* requested, UnityXRRenderTextureId* id) {
    check(h == Handle, "native create uses the current display handle");
    D3D11_TEXTURE2D_DESC d{}; d.Width = requested->width; d.Height = requested->height;
    d.ArraySize = requested->textureArrayLength ? requested->textureArrayLength : 1; d.MipLevels = d.SampleDesc.Count = 1;
    // The owned Unity boundary deliberately supplies an sRGB-format native
    // source for the production test, independently of the old SDK's flags.
    // This exercises the actual SRGB -> UNORM GPU copy, not two UNORM images.
    d.Format = fixtureSrgbStorage || (requested->flags & kUnityXRRenderTextureFlagsSRGB)
        ? DXGI_FORMAT_R8G8B8A8_UNORM_SRGB : DXGI_FORMAT_R8G8B8A8_UNORM;
    d.Usage = D3D11_USAGE_DEFAULT; d.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
    ID3D11Texture2D* texture = nullptr; check(SUCCEEDED(device.p->CreateTexture2D(&d, nullptr, &texture)), "actual native-requested D3D11 texture created");
    auto desc = *requested; desc.color.nativePtr = texture; desc.depth.nativePtr = nullptr;
    *id = ++serial; images.emplace(*id, Image{desc, texture}); return kUnitySubsystemErrorCodeSuccess;
}
UnitySubsystemErrorCode UNITY_INTERFACE_API query(UnitySubsystemHandle h, UnityXRRenderTextureId id, UnityXRRenderTextureDesc* out) {
    check(h == Handle && images.count(id), "native query refers to an exact live texture"); *out = images.at(id).desc;
    return kUnitySubsystemErrorCodeSuccess;
}
UnitySubsystemErrorCode UNITY_INTERFACE_API destroy(UnitySubsystemHandle h, UnityXRRenderTextureId id) {
    check(h == Handle, "native destroy uses the current display handle");
    auto it = images.find(id); if (it != images.end()) { it->second.texture->Release(); images.erase(it); }
    return kUnitySubsystemErrorCodeSuccess;
}
struct Texture { void* handle; int type, colourSpace; };
struct Bounds { float uMin, vMin, uMax, vMax; };
Bounds* providerBounds = nullptr;
std::array<bool, 2> capturedWhole{};
std::array<ID3D11Texture2D*, 2> submitted{};
std::array<unsigned, 2> slices{};
unsigned indexedCalls = 0;
unsigned capturedFlags = 0;
int capturedType = -1;
int capturedColourSpace = -1;
std::array<unsigned, 2> capturedEyeFlags{};
HANDLE submitEntered = nullptr, submitContinue = nullptr;
bool blockSubmit = false;
unsigned blockedWidth = 160;
bool capturedFilter = true;
void* settingsObject = nullptr;
using GetSetting = bool (*)(void*, const char*, const char*, int*);
bool filterSetting(const char* section = "steamvr", const char* key = "allowSupersampleFiltering", int* error = nullptr) {
    auto table = *static_cast<void***>(settingsObject);
    return reinterpret_cast<GetSetting>(table[5])(settingsObject, section, key, error);
}
int capture(int eye, const Texture* texture, const void* bounds, unsigned flags) {
    check(eye == 0 || eye == 1, "native eye selector");
    capturedFlags = flags; capturedEyeFlags[eye] = flags;
    capturedType = texture->type; capturedColourSpace = texture->colourSpace;
    if (settingsObject) {
        int error = -1;
        capturedFilter = filterSetting("steamvr", "allowSupersampleFiltering", &error);
        check(error == (capturedFilter ? 77 : 0), "scoped settings preserve the public bool ABI and native error outputs");
        check(filterSetting("steamvr", "someOtherSetting", &error) && error == 77,
            "other settings preserve the exact native result/error inside Submit");
        check(filterSetting("otherSection", "allowSupersampleFiltering", &error) && error == 77,
            "same key in a foreign section remains native inside Submit");
        check(filterSetting("steamvr", "allowSupersampleFilteringSuffix", &error) && error == 77,
            "filter query requires an exact complete key");
    }
    if (!providerBounds && bounds) providerBounds = const_cast<Bounds*>(static_cast<const Bounds*>(bounds));
    capturedWhole[eye] = bounds == nullptr;
    submitted[eye] = static_cast<ID3D11Texture2D*>(texture->handle);
    if (blockSubmit && eye == 0) {
        SetEvent(submitEntered);
        WaitForSingleObject(submitContinue, 30000);
        D3D11_TEXTURE2D_DESC shape{}; submitted[eye]->GetDesc(&shape);
        check(shape.ArraySize == 1 && shape.Width == blockedWidth, "in-flight native submit retains its 2D resource during replacement/opt-out");
    }
    return 0;
}
int captureIndexed(int eye, const Texture* texture, unsigned slice, const void* bounds, unsigned flags) {
    check(slice == static_cast<unsigned>(eye), "legacy indexed negative-control eye slice");
    check(bounds != nullptr && texture->colourSpace == 0, "original SDK bounds and colour space preserved");
    check(texture->type == 0 && flags == 0, "indexed boundary preserves actual SDK D3D11/default flags");
    ++indexedCalls; slices[eye] = slice;
    return capture(eye, texture, bounds, flags);
}
template<class T> T symbol(HMODULE module, const char* name) {
    auto result = reinterpret_cast<T>(GetProcAddress(module, name)); check(result != nullptr, "required native export exists"); return result;
}
void fill(Image& image, unsigned eye, unsigned frame, unsigned slice = 0) {
    D3D11_TEXTURE2D_DESC d{}; image.texture->GetDesc(&d);
    std::vector<std::array<unsigned char, 4>> pixels(d.Width * d.Height);
    for (unsigned y = 0; y < d.Height; ++y) for (unsigned x = 0; x < d.Width; ++x)
        pixels[y*d.Width+x] = {static_cast<unsigned char>(eye ? 17 : 231), static_cast<unsigned char>(frame + x*3), static_cast<unsigned char>(83 + y*5),
            static_cast<unsigned char>((x*3 + y*5 + eye * 73 + frame) % 256)};
    context.p->UpdateSubresource(image.texture, slice, nullptr, pixels.data(), d.Width * 4, 0);
}
void verify(ID3D11Texture2D* source, unsigned eye, unsigned frame, unsigned slice = 0, unsigned array = 1,
            unsigned left = 0, unsigned top = 0, unsigned width = 160, unsigned height = 80) {
    D3D11_TEXTURE2D_DESC d{}; source->GetDesc(&d);
    check(d.ArraySize == array && d.MipLevels == 1 && d.SampleDesc.Count == 1, "submitted native eye retains its original resolved image shape");
    check(d.Width == width && d.Height == height, "the exact submitted pixel extent is retained without scaling");
    d.Usage = D3D11_USAGE_STAGING; d.BindFlags = d.MiscFlags = 0; d.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    Com<ID3D11Texture2D> staging; check(SUCCEEDED(device.p->CreateTexture2D(&d, nullptr, staging.out())), "actual eye readback storage");
    context.p->CopyResource(staging.p, source);
    D3D11_MAPPED_SUBRESOURCE data{}; check(SUCCEEDED(context.p->Map(staging.p, slice, D3D11_MAP_READ, 0, &data)), "actual submitted eye readback");
    for (unsigned y = 0; y < d.Height; ++y) for (unsigned x = 0; x < d.Width; ++x) {
        const auto* pixel = static_cast<const unsigned char*>(data.pData) + y * data.RowPitch + x * 4;
        check(pixel[0] == (eye ? 17 : 231) && pixel[1] == (frame + (x+left)*3)%256 && pixel[2] == (83 + (y+top)*5)%256, "native submit preserves this eye's exact per-pixel RGB");
        check(pixel[3] == ((x+left)*3 + (y+top)*5 + eye * 73 + frame) % 256, "native submit preserves every alpha byte in the correct eye/frame");
    }
    context.p->Unmap(staging.p, slice);
}
void closeGraphics() {
    // DLL fixtures must release D3D outside DllMain/loader lock, before .NET
    // unloads the test host. Native GPU destruction can join driver threads.
    if (context.p) { context.p->ClearState(); context.p->Flush(); context.p->Release(); context.p = nullptr; }
    if (device.p) { device.p->Release(); device.p = nullptr; }
}
struct GraphicsWork { std::function<void()> body; std::exception_ptr error; };
DWORD WINAPI graphicsThread(void* opaque) {
    auto& work = *static_cast<GraphicsWork*>(opaque);
    try { work.body(); } catch (...) { work.error = std::current_exception(); }
    return 0;
}
}
extern "C" __declspec(dllexport) int RunProviderChecks(const wchar_t* path, void (*enable)(), void (*disable)(), void (*originChanged)()) {
    try {
        check(path != nullptr, "private provider path provided");
        fixtureSrgbStorage = enable != nullptr;
        D3D_FEATURE_LEVEL level{};
        auto status = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, device.out(), &level, context.out());
        if (FAILED(status)) status = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, device.out(), &level, context.out());
        check(SUCCEEDED(status), "actual D3D11 graphics device");
        HMODULE module = LoadLibraryExW(path, nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        check(module != nullptr, "private pinned provider loads with the owned offline OpenVR boundary");
        auto* base = reinterpret_cast<unsigned char*>(module);
        auto mode = symbol<int (*)()>(module, ModeExport);
        auto destroyEyes = symbol<void (*)(void*, void*)>(module, DestroyExport);
        check(reinterpret_cast<unsigned char*>(mode) == base + ModeGetterRva
            && reinterpret_cast<unsigned char*>(destroyEyes) == base + DestroyRva, "production ABI export addresses match the real provider");
        auto singleton = symbol<void* (*)()>(module, "?Get@?$Singleton@VUnityInterfaces@@@@SAAEAVUnityInterfaces@@XZ")();
        graphics.GetRenderer = renderer;
        IUnityInterfaces interfaces{}; interfaces.GetInterface = getInterface; interfaces.GetInterfaceSplit = interfaceSplit;
        *reinterpret_cast<IUnityInterfaces**>(static_cast<unsigned char*>(singleton) + 8) = &interfaces;
        display.CreateTexture = create; display.QueryTextureDesc = query; display.DestroyTexture = destroy;
        display.RegisterProvider = registerMain; display.RegisterProviderForGraphicsThread = registerGraphics;
        *reinterpret_cast<IUnityXRDisplayInterface**>(base + 0x5a678) = &display;
        *reinterpret_cast<void**>(base + HandleRva) = Handle;
        alignas(16) std::array<unsigned char, 8192> storage{};
        auto* provider = storage.data();
        symbol<void (*)(void*)>(module, "??0OpenVRDisplayProvider@@QEAA@XZ")(provider);
        std::array<void*, 6> providerContext{&interfaces, nullptr, &display, nullptr, nullptr, nullptr};
        *reinterpret_cast<void**>(base + ContextRva) = providerContext.data();
        auto initialize = symbol<int (*)(void*, void*, void*)>(module,
            "?Lifecycle_Initialize@OpenVRDisplayProvider@@QEAA?AW4UnitySubsystemErrorCode@@PEAX0@Z");
        check(initialize(provider, Handle, provider) == 0 && registeredProvider == provider, "actual lifecycle publishes the provider context used by production");
        check(*reinterpret_cast<void**>(static_cast<unsigned char*>(*reinterpret_cast<void**>(base + ContextRva)) + ProviderOffset) == provider,
            "production context-to-provider offset matches the actual lifecycle ABI");
        auto start = symbol<int (*)(void*, UnityXRRenderingCapabilities*)>(module,
            "?GfxThread_Start@OpenVRDisplayProvider@@QEAA?AW4UnitySubsystemErrorCode@@PEAUUnityXRRenderingCapabilities@@@Z");
        auto stop = symbol<int (*)(void*)>(module, "?GfxThread_Stop@OpenVRDisplayProvider@@QEAA?AW4UnitySubsystemErrorCode@@XZ");
        auto createEyes = symbol<int (*)(void*, const UnityXRFrameSetupHints*)>(module,
            "?CreateEyeTextures@OpenVRDisplayProvider@@AEAA?AW4UnitySubsystemErrorCode@@PEBUUnityXRFrameSetupHints@@@Z");
        auto pass = symbol<void (*)(void*, int, const UnityXRFrameSetupHints*, UnityXRNextFrameDesc*)>(module,
            "?SetupRenderPass@OpenVRDisplayProvider@@AEAAXW4EVREye@vr@@PEBUUnityXRFrameSetupHints@@PEAUUnityXRNextFrameDesc@@@Z");
        auto submit = symbol<bool (*)(void*, int, int)>(module,
            "?SubmitToCompositor@OpenVRDisplayProvider@@AEAA_NW4EVREye@vr@@H@Z");
        symbol<void (*)(decltype(&capture))>(GetModuleHandleW(L"openvr_api.dll"), "SetFixtureSubmit")(&capture);
        symbol<void (*)(decltype(&captureIndexed))>(GetModuleHandleW(L"openvr_api.dll"), "SetFixtureIndexedSubmit")(&captureIndexed);
        UnityXRFrameSetupHints hints{}; hints.appSetup.textureResolutionScale = 1.25f;
        hints.appSetup.zNear = .1f; hints.appSetup.zFar = 100.f; hints.appSetup.sRGB = true;
        UnityXRRenderingCapabilities capabilities{};
        auto* setting = reinterpret_cast<unsigned short*>(base + ModeRva);
        *setting = 1; check(mode() == 1, "production mode word is the actual SDK getter's storage");
        check(start(provider, &capabilities) == 0 && provider[SinglePassOffset] == 1, "actual SDK Start caches SinglePass");
        check(createEyes(provider, &hints) == 0 && provider[CreatedOffset] == 1, "actual SDK creates initial eye arrays");
        UnityXRNextFrameDesc frame{}; pass(provider, 0, &hints, &frame); pass(provider, 1, &hints, &frame);
        check(frame.renderPassesCount == 1 && frame.renderPasses[0].renderParamsCount == 2, "baseline is one native pass containing both eye slices");
        submitted = {}; check(submit(provider, 0, 0) && submit(provider, 1, 0), "original single-pass D3D11 submission executes");
        D3D11_TEXTURE2D_DESC originalShape{}; submitted[0]->GetDesc(&originalShape);
        if (enable) check(originalShape.Format == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB, "owned Unity boundary provides actual sRGB-storage source arrays");
        check(submitted[0] == submitted[1] && originalShape.ArraySize == 2,
            "negative control: original SDK submits the same two-slice D3D11 array for both eyes without an array selector");
        std::puts("Original single-pass boundary confirmed: identical D3D11 array handle for both eyes; Submit_Default; no slice selector.");
        if (enable) {
            auto get = symbol<void* (*)(const char*, int*)>(GetModuleHandleW(L"openvr_api.dll"), "VR_GetGenericInterface");
            int error = 0; auto object = get("IVRCompositor_026", &error);
            settingsObject = get("IVRSettings_003", &error);
            check(settingsObject && !error && filterSetting(), "native filter setting starts unchanged");
            auto* originalSettings = *static_cast<void***>(settingsObject);
            auto* nativeTable = *static_cast<void***>(object);
            enable();
            check(filterSetting(), "cold activation does not change queries outside Submit");
            std::array<std::array<ID3D11Texture2D*, 2>, 2> eyeTargets{};
            GraphicsWork work{[&] { for (unsigned update = 0; update < 3; ++update) {
                // Both provider stages retain their native array shape and
                // receive distinct per-eye/per-frame RGB plus every alpha value.
                for (auto& image : images) for (unsigned eye = 0; eye < 2; ++eye) fill(image.second, eye, update, eye);
                for (int stage = 0; stage < 2; ++stage) {
                    submitted = {};
                    check(submit(provider, 0, stage), "actual SDK enters the production 2D left-eye callback");
                    // A real notification between two eyes must not split this
                    // pair's flags. It applies to the next two complete pairs.
                    if (update == 0 && stage == 1) originChanged();
                    check(submit(provider, 1, stage), "actual SDK enters the production 2D right-eye callback");
                    check(submitted[0] != submitted[1], "completed eyes have distinct native 2D submit resources");
                    for (unsigned eye = 0; eye < 2; ++eye) {
                        if (update == 0) eyeTargets[stage][eye] = submitted[eye];
                        check(submitted[eye] == eyeTargets[stage][eye], "each provider stage retains its own stable eye target");
                        D3D11_TEXTURE2D_DESC canonical{}; submitted[eye]->GetDesc(&canonical);
                        check(canonical.Format == DXGI_FORMAT_R8G8B8A8_UNORM, "submitted storage is canonical RGBA8 independent of the source sRGB tag");
                        verify(submitted[eye], eye, update);
                    }
                    const unsigned expectedFlags = update == 1 ? 0x20 : 0;
                    check(capturedEyeFlags[0] == expectedFlags && capturedEyeFlags[1] == expectedFlags,
                        "origin discontinuity is paired, bounded to two frames and absent during ordinary rendering");
                    check(capturedType == 0 && capturedColourSpace == 1, "same encoded SDR bytes declare Gamma in the ordinary D3D11 submission");
                    check(!capturedFilter, "the admitted original Submit bypasses native supersample filtering");
                }
            } }, {}};
            HANDLE graphics = CreateThread(nullptr, 0, graphicsThread, &work, 0, nullptr);
            check(graphics != nullptr, "actual SDK submission runs on a native graphics worker thread");
            check(WaitForSingleObject(graphics, 30000) == WAIT_OBJECT_0, "managed reverse callbacks complete on the native graphics thread");
            CloseHandle(graphics);
            if (work.error) std::rethrow_exception(work.error);
            check(providerBounds != nullptr, "actual SDK supplied its live viewport bounds");
            const Bounds savedBounds = *providerBounds;
            // The fixture owns this provider object. Change its real bounds,
            // exercise its actual Submit, then prepare GPU storage cold through
            // the production managed callback. No private executable is patched.
            for (const auto crop : {Bounds{.1f,.1f,.7f,.7f}, Bounds{0,0,.5f,.5f}}) {
                *providerBounds = crop;
                const unsigned left = crop.uMin == 0 ? 0 : 16, top = crop.vMin == 0 ? 0 : 8;
                const unsigned width = crop.uMin == 0 ? 80 : 96, height = crop.vMin == 0 ? 40 : 48;
                submitted = {};
                check(submit(provider, 0, 0) && submit(provider, 1, 0), "new viewport has a valid full-image 2D handoff while cold preparation is pending");
                check(!capturedWhole[0] && !capturedWhole[1], "pending viewport preserves the original native bounds for both eyes");
                verify(submitted[0],0,2);verify(submitted[1],1,2);
                Sleep(2100); // Test-only: honor the production cold allocation cadence.
                enable();
                for (int stage=0;stage<2;++stage) {
                    check(submit(provider,0,stage)&&submit(provider,1,stage), "actual SDK submits a prepared cropped pair");
                    check(capturedWhole[0]&&capturedWhole[1], "prepared cropped eyes use documented whole-texture bounds");
                    for (unsigned eye=0;eye<2;++eye) verify(submitted[eye],eye,2,0,1,left,top,width,height);
                }
            }
            *providerBounds = Bounds{.1234567f,0,1,1};
            check(submit(provider,0,0)&&submit(provider,1,0)&&!capturedWhole[0]&&!capturedWhole[1],
                "non-integer native viewport retains full 2D copies and original bounds");
            verify(submitted[0],0,2);verify(submitted[1],1,2);
            *providerBounds = Bounds{0,0,1,1};
            check(submit(provider,0,0)&&submit(provider,1,0)&&capturedWhole[0]&&capturedWhole[1],
                "whole-viewport recovery immediately reuses the prepared full 2D pair");
            verify(submitted[0],0,2);verify(submitted[1],1,2);
            *providerBounds = Bounds{.1f,.1f,.7f,.7f};
            check(submit(provider,0,0)&&submit(provider,1,0), "request the crop used by the held native submit");
            Sleep(2100);enable();enable(); // The second cold tick retires the old pool.
            *providerBounds = Bounds{0,0,.5f,.5f};
            check(submit(provider,0,0)&&submit(provider,1,0), "queue a successor crop without withdrawing the valid full-image source");
            *providerBounds = Bounds{.1f,.1f,.7f,.7f};
            Sleep(2100);
            submitEntered = CreateEventW(nullptr,TRUE,FALSE,nullptr);submitContinue = CreateEventW(nullptr,TRUE,FALSE,nullptr);
            check(submitEntered&&submitContinue,"owned crop-lifetime coordination events");
            blockedWidth=96;blockSubmit=true;
            GraphicsWork heldCrop{[&]{check(submit(provider,0,0),"held native submit completes after crop replacement");},{}};
            HANDLE cropThread=CreateThread(nullptr,0,graphicsThread,&heldCrop,0,nullptr);
            check(cropThread&&WaitForSingleObject(submitEntered,30000)==WAIT_OBJECT_0,"native submit holds the predecessor crop across cold publication");
            enable(); // Publish the requested 80x40 successor while 96x48 is borrowed.
            SetEvent(submitContinue);
            check(WaitForSingleObject(cropThread,30000)==WAIT_OBJECT_0,"replacing a crop never waits for graphics completion on the cold thread");
            CloseHandle(cropThread);CloseHandle(submitEntered);CloseHandle(submitContinue);
            submitEntered=submitContinue=nullptr;blockSubmit=false;blockedWidth=160;
            if(heldCrop.error)std::rethrow_exception(heldCrop.error);
            verify(submitted[0],0,2,0,1,16,8,96,48);
            enable(); // No reader remains; retiring the predecessor is now safe.
            *providerBounds = savedBounds;
            check(indexedCalls == 0 && mode() == 1 && provider[SinglePassOffset] == 1 && provider[CreatedOffset] == 1,
                "all native submits use 2D resources while SDK mode and live arrays remain unchanged");
            for (auto& image : images) for (unsigned eye = 0; eye < 2; ++eye) verify(image.second.texture, eye, 2, eye, 2);
            auto* originalTable = *static_cast<void***>(object);
            std::uintptr_t nativeLocator = 0, adaptedLocator = 0;
            std::memcpy(&nativeLocator, reinterpret_cast<unsigned char*>(nativeTable) - sizeof(void*), sizeof(void*));
            std::memcpy(&adaptedLocator, reinterpret_cast<unsigned char*>(originalTable) - sizeof(void*), sizeof(void*));
            check(nativeLocator == adaptedLocator, "the adapted interface retains the original MSVC RTTI locator");
            using NativeSubmit = int (*)(void*, int, const Texture*, const void*, unsigned);
            auto boundary = reinterpret_cast<NativeSubmit>(originalTable[5]);
            Texture foreign{nullptr, 0, 0};
            check(boundary(object, 0, &foreign, nullptr, 0) == 0 && indexedCalls == 0 && submitted[0] == nullptr,
                "unknown resources retain ordinary submission even with a live source grant");
            check(capturedFilter, "unknown resources do not override native image filtering");
            Texture known{images.begin()->second.texture, 0, 0};
            check(boundary(object, 1, &known, nullptr, 0x10) == 0 && indexedCalls == 0 && capturedFlags == 0x10,
                "non-default submit flags are forwarded without coercion");
            known.type = 2;
            check(boundary(object, 1, &known, nullptr, 0) == 0 && indexedCalls == 0 && capturedType == 2,
                "non-D3D11 submit kinds are forwarded without reinterpretation");
            known.type = 0; known.colourSpace = 2;
            check(boundary(object, 1, &known, nullptr, 0) == 0 && submitted[1] == known.handle && capturedColourSpace == 2,
                "an unexpected explicit Linear declaration is forwarded without relabeling");
            disable(); // Revoke the live source snapshot while callback stays attached.
            check(submit(provider, 0, 0) && submit(provider, 1, 0) && indexedCalls == 0,
                "live source revocation immediately restores ordinary submission inside the callback");
            check(capturedFilter, "revocation immediately restores native filter queries");
            enable();
            submitEntered = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            submitContinue = CreateEventW(nullptr, TRUE, FALSE, nullptr); blockSubmit = true;
            GraphicsWork retiring{[&] { check(submit(provider, 0, 0), "in-flight native submit completes during opt-out"); }, {}};
            HANDLE retiringThread = CreateThread(nullptr, 0, graphicsThread, &retiring, 0, nullptr);
            check(WaitForSingleObject(submitEntered, 30000) == WAIT_OBJECT_0, "native callback reaches the held original Submit");
            check(filterSetting(), "another thread retains native filtering during an active graphics Submit");
            disable();
            check(*static_cast<void***>(object) == nativeTable, "normal detach restores the exact original interface vptr");
            check(*static_cast<void***>(settingsObject) == originalSettings, "opt-out restores the original settings interface vptr");
            SetEvent(submitContinue);
            check(WaitForSingleObject(retiringThread, 30000) == WAIT_OBJECT_0, "opt-out does not wait for or deadlock the graphics callback");
            CloseHandle(retiringThread); CloseHandle(submitEntered); CloseHandle(submitContinue); blockSubmit = false;
            if (retiring.error) std::rethrow_exception(retiring.error);
            enable();
            check(*static_cast<void***>(object) == originalTable, "same owned interface can reattach after an opt-out");
            std::array<void*, 11> foreignSettings{};
            auto* settingsTable = *static_cast<void***>(settingsObject);
            for (unsigned i = 0; i < foreignSettings.size(); ++i) foreignSettings[i] = settingsTable[i];
            *static_cast<void***>(settingsObject) = foreignSettings.data();
            disable();
            check(*static_cast<void***>(settingsObject) == foreignSettings.data(), "settings cleanup preserves a foreign interface owner");
            check(filterSetting(), "a retained old settings callback forwards outside a scope after detach");
            *static_cast<void***>(settingsObject) = originalSettings;
            enable();
            // An external interface owner replacing the vptr must survive our
            // cleanup. Its table intentionally forwards unchanged entries.
            std::array<void*, 51> foreignTable{};
            for (unsigned i = 0; i < foreignTable.size(); ++i) foreignTable[i] = originalTable[i];
            *static_cast<void***>(object) = foreignTable.data();
            disable(); // Detach only while owned.
            check(*static_cast<void***>(object) == foreignTable.data(), "cleanup preserves another interface owner's vptr");
            check(submit(provider, 0, 0) && submit(provider, 1, 0) && indexedCalls == 0,
                "detaching restores the original interface behavior");
            *static_cast<void***>(object) = nativeTable; // The fixture owns this simulated external lease.
            destroyEyes(provider, Handle);
            closeGraphics();
            std::printf("Actual OpenVR provider/D3D11 checks passed: %u; production 2D eye boundary; native graphics thread; two stages; all RGBA bytes; no headset test.\n", checks);
            return 0;
        }
        check(stop(provider) == 0 && provider[CreatedOffset] == 1 && !images.empty(), "negative control: Stop alone retains old array textures");
        destroyEyes(provider, Handle); check(provider[CreatedOffset] == 0 && images.empty(), "original SDK destroy retires old target storage");
        *setting = 0; check(mode() == 0, "production change updates the actual native setting");
        check(start(provider, &capabilities) == 0 && provider[SinglePassOffset] == 0, "actual SDK Start caches MultiPass");
        check(createEyes(provider, &hints) == 0, "actual SDK recreates separate eye targets");
        for (unsigned i = 0; i != 3; ++i) {
            frame = {}; pass(provider, 0, &hints, &frame); pass(provider, 1, &hints, &frame);
            check(frame.renderPassesCount == 2 && frame.renderPasses[0].renderParamsCount == 1 && frame.renderPasses[1].renderParamsCount == 1,
                "actual SDK supplies two valid native pass IDs with one eye each");
            auto left = frame.renderPasses[0].textureId, right = frame.renderPasses[1].textureId;
            check(left != right && images.count(left) && images.count(right), "actual SDK assigns distinct current eye targets");
            check(frame.renderPasses[0].renderParams[0].textureArraySlice == 0 && frame.renderPasses[1].renderParams[0].textureArraySlice == 0,
                "each native 2D eye has its own slice zero");
            fill(images.at(left), 0, i); fill(images.at(right), 1, i);
            submitted = {}; check(submit(provider, 0, 0) && submit(provider, 1, 0), "original native submit executes for both eyes");
            check(submitted[0] == images.at(left).texture && submitted[1] == images.at(right).texture,
                "actual SDK submits the correct current native target for each eye");
            verify(submitted[0], 0, i); verify(submitted[1], 1, i);
        }
        stop(provider); destroyEyes(provider, Handle); *setting = 1;
        check(start(provider, &capabilities) == 0 && createEyes(provider, &hints) == 0, "original native stereo mode can be restored");
        frame = {}; pass(provider, 0, &hints, &frame); pass(provider, 1, &hints, &frame);
        check(frame.renderPassesCount == 1 && frame.renderPasses[0].renderParamsCount == 2, "restored SDK returns its original single-pass array layout");
        destroyEyes(provider, Handle); check(images.empty(), "fixture retires every owned GPU texture");
        closeGraphics();
        std::printf("Actual OpenVR provider/D3D11 checks passed: %u; two native 2D eyes; exact per-eye RGB/alpha; no headset test.\n", checks);
        return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "Provider fixture failed after %u checks: %s (win32=%lu)\n", checks, error.what(), GetLastError()); return 1; }
}
#ifndef MANAGED_FIXTURE
int wmain(int argc, wchar_t** argv) {
    return argc == 2 ? RunProviderChecks(argv[1], nullptr, nullptr, nullptr) : 2;
}
#endif
