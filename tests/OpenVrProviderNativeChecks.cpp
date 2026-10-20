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
#include <cstring>
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
    d.Format = (requested->flags & kUnityXRRenderTextureFlagsSRGB) ? DXGI_FORMAT_R8G8B8A8_UNORM_SRGB : DXGI_FORMAT_R8G8B8A8_UNORM;
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
std::array<ID3D11Texture2D*, 2> submitted{};
int capture(int eye, const Texture* texture, const void*, unsigned flags) {
    check(eye == 0 || eye == 1, "native eye selector"); check(texture->type == 0 && flags == 0, "native D3D11 submit convention retained");
    submitted[eye] = static_cast<ID3D11Texture2D*>(texture->handle); return 0;
}
template<class T> T symbol(HMODULE module, const char* name) {
    auto result = reinterpret_cast<T>(GetProcAddress(module, name)); check(result != nullptr, "required native export exists"); return result;
}
void fill(Image& image, unsigned eye, unsigned frame) {
    D3D11_TEXTURE2D_DESC d{}; image.texture->GetDesc(&d);
    std::vector<std::array<unsigned char, 4>> pixels(d.Width * d.Height);
    for (unsigned y = 0; y < d.Height; ++y) for (unsigned x = 0; x < d.Width; ++x)
        pixels[y*d.Width+x] = {static_cast<unsigned char>(eye ? 17 : 231), static_cast<unsigned char>(frame), 83,
            static_cast<unsigned char>((x + y + eye * 73 + frame) % 256)};
    context.p->UpdateSubresource(image.texture, 0, nullptr, pixels.data(), d.Width * 4, 0);
}
void verify(ID3D11Texture2D* source, unsigned eye, unsigned frame) {
    D3D11_TEXTURE2D_DESC d{}; source->GetDesc(&d);
    check(d.ArraySize == 1 && d.MipLevels == 1 && d.SampleDesc.Count == 1, "submitted native eye is a separate resolved 2D image");
    check(d.Width == 160 && d.Height == 80, "native resolution scale retained");
    d.Usage = D3D11_USAGE_STAGING; d.BindFlags = d.MiscFlags = 0; d.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    Com<ID3D11Texture2D> staging; check(SUCCEEDED(device.p->CreateTexture2D(&d, nullptr, staging.out())), "actual eye readback storage");
    context.p->CopyResource(staging.p, source);
    D3D11_MAPPED_SUBRESOURCE data{}; check(SUCCEEDED(context.p->Map(staging.p, 0, D3D11_MAP_READ, 0, &data)), "actual submitted eye readback");
    for (unsigned y = 0; y < d.Height; ++y) for (unsigned x = 0; x < d.Width; ++x) {
        const auto* pixel = static_cast<const unsigned char*>(data.pData) + y * data.RowPitch + x * 4;
        check(pixel[0] == (eye ? 17 : 231) && pixel[1] == frame && pixel[2] == 83, "native submit preserves this eye's RGB");
        check(pixel[3] == (x + y + eye * 73 + frame) % 256, "native submit preserves every alpha byte in the correct eye/frame");
    }
    context.p->Unmap(staging.p, 0);
}
}
int wmain(int argc, wchar_t** argv) {
    try {
        check(argc == 2, "usage: provider-native-checks PRIVATE_PROVIDER_DLL");
        D3D_FEATURE_LEVEL level{};
        auto status = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, device.out(), &level, context.out());
        if (FAILED(status)) status = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, device.out(), &level, context.out());
        check(SUCCEEDED(status), "actual D3D11 graphics device");
        HMODULE module = LoadLibraryExW(argv[1], nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
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
        UnityXRFrameSetupHints hints{}; hints.appSetup.textureResolutionScale = 1.25f;
        hints.appSetup.zNear = .1f; hints.appSetup.zFar = 100.f; hints.appSetup.sRGB = true;
        UnityXRRenderingCapabilities capabilities{};
        auto* setting = reinterpret_cast<unsigned short*>(base + ModeRva);
        *setting = 1; check(mode() == 1, "production mode word is the actual SDK getter's storage");
        check(start(provider, &capabilities) == 0 && provider[SinglePassOffset] == 1, "actual SDK Start caches SinglePass");
        check(createEyes(provider, &hints) == 0 && provider[CreatedOffset] == 1, "actual SDK creates initial eye arrays");
        UnityXRNextFrameDesc frame{}; pass(provider, 0, &hints, &frame); pass(provider, 1, &hints, &frame);
        check(frame.renderPassesCount == 1 && frame.renderPasses[0].renderParamsCount == 2, "baseline is one native pass containing both eye slices");
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
        std::printf("Actual OpenVR provider/D3D11 checks passed: %u; two native 2D eyes; exact per-eye RGB/alpha; no headset test.\n", checks);
        return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "Provider fixture failed after %u checks: %s (win32=%lu)\n", checks, error.what(), GetLastError()); return 1; }
}
