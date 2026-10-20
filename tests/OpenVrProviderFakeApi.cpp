// Owned, offline OpenVR boundary for the private-provider test. No headset or
// SteamVR connection is made. Only the interface calls used by the fixture exist.
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <cstdlib>

namespace {
struct Matrix34 { float value[3][4]; };
struct Matrix44 { float value[4][4]; };
struct Mesh { const void* vertices; std::uint32_t triangles; };
struct Object { void** table; };
struct Texture { void* handle; int type, colourSpace; };
using Submit = int (*)(int, const Texture*, const void*, unsigned);
Submit capture = nullptr;
void* systemTable[80]{};
void* compositorTable[80]{};
Object vrSystem{systemTable}, compositor{compositorTable};
void unexpected() { std::abort(); }
void size(Object*, std::uint32_t* width, std::uint32_t* height) { *width = 128; *height = 64; }
Matrix44* projection(Object*, Matrix44* out, int, float, float) {
    std::memset(out, 0, sizeof *out); for (int i = 0; i != 4; ++i) out->value[i][i] = 1; return out;
}
void raw(Object*, int, float* left, float* right, float* top, float* bottom) { *left = *top = -1; *right = *bottom = 1; }
Matrix34* pose(Object*, Matrix34* out, int eye) {
    std::memset(out, 0, sizeof *out); for (int i = 0; i != 3; ++i) out->value[i][i] = 1;
    out->value[0][3] = eye ? .032f : -.032f; return out;
}
Mesh* mesh(Object*, Mesh* out, int, int) { *out = {}; return out; }
int submit(Object*, int eye, const Texture* texture, const void* bounds, unsigned flags) {
    return capture ? capture(eye, texture, bounds, flags) : 1;
}
void initialize() {
    for (auto& slot : systemTable) slot = reinterpret_cast<void*>(&unexpected);
    for (auto& slot : compositorTable) slot = reinterpret_cast<void*>(&unexpected);
    systemTable[0] = reinterpret_cast<void*>(&size); systemTable[1] = reinterpret_cast<void*>(&projection);
    systemTable[2] = reinterpret_cast<void*>(&raw); systemTable[4] = reinterpret_cast<void*>(&pose);
    // IVRSystem_022's mesh slot; the fixture accepts only that exact interface.
    systemTable[32] = reinterpret_cast<void*>(&mesh);
    compositorTable[5] = reinterpret_cast<void*>(&submit);
}
}
extern "C" {
__declspec(dllexport) void SetFixtureSubmit(Submit callback) { capture = callback; }
__declspec(dllexport) void* VR_GetGenericInterface(const char* name, int* error) {
    initialize(); if (error) *error = 0;
    if (!std::strcmp(name, "IVRSystem_022")) return &vrSystem;
    if (!std::strcmp(name, "IVRCompositor_026")) return &compositor;
    if (error) *error = 105;
    return nullptr;
}
__declspec(dllexport) void* VRHeadsetView() { return nullptr; }
__declspec(dllexport) void VR_ShutdownInternal() { }
__declspec(dllexport) std::uint32_t VR_InitInternal2(int* error, int, const char*) { if (error) *error = 0; return 1; }
__declspec(dllexport) std::uint32_t VR_GetInitToken() { return 1; }
__declspec(dllexport) bool VR_IsInterfaceVersionValid(const char*) { return true; }
__declspec(dllexport) const char* VR_GetVRInitErrorAsSymbol(int) { return "offline-fixture"; }
}
