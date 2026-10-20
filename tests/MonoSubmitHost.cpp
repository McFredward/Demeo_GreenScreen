// Embed the licensed Unity Editor's Windows Mono runtime to exercise the same
// production managed/native callback under Mono as well as the .NET loaders.
#include <windows.h>
#include <cstdio>
#include <stdexcept>
#include <string>

template<class T> T api(HMODULE module, const char* name) {
    auto result = reinterpret_cast<T>(GetProcAddress(module, name));
    if (!result) throw std::runtime_error(name);
    return result;
}
std::string utf8(const wchar_t* value) {
    int size = WideCharToMultiByte(CP_UTF8, 0, value, -1, nullptr, 0, nullptr, nullptr);
    std::string out(size, 0); WideCharToMultiByte(CP_UTF8, 0, value, -1, out.data(), size, nullptr, nullptr);
    out.pop_back(); return out;
}
int wmain(int argc, wchar_t** argv) {
    try {
        if (argc != 5) return 2;
        HMODULE mono = LoadLibraryW(argv[1]);
        if (!mono) throw std::runtime_error("Unity Windows Mono runtime did not load");
        auto profile = utf8(argv[2]); profile += ";" + profile + "\\Facades";
        api<void (*)(const char*)>(mono, "mono_set_assemblies_path")(profile.c_str());
        auto domain = api<void* (*)(const char*, const char*)>(mono, "mono_jit_init_version")("DemeoSubmitChecks", "v4.0.30319");
        if (!domain) throw std::runtime_error("Mono initialization failed");
        auto assembly = api<void* (*)(void*, const char*)>(mono, "mono_domain_assembly_open")(domain, utf8(argv[3]).c_str());
        if (!assembly) throw std::runtime_error("Mono test assembly did not load");
        auto image = api<void* (*)(void*)>(mono, "mono_assembly_get_image")(assembly);
        auto type = api<void* (*)(void*, const char*, const char*)>(mono, "mono_class_from_name")(image, "", "IntegrationChecks");
        auto method = api<void* (*)(void*, const char*, int)>(mono, "mono_class_get_method_from_name")(type, "Main", 1);
        auto stringType = api<void* (*)()>(mono, "mono_get_string_class")();
        auto strings = api<void* (*)(void*, void*, size_t)>(mono, "mono_array_new")(domain, stringType, 2);
        auto create = api<void* (*)(void*, const char*)>(mono, "mono_string_new");
        auto address = api<void* (*)(void*, int, size_t)>(mono, "mono_array_addr_with_size");
        auto assign = api<void (*)(void*, void*, void*)>(mono, "mono_gc_wbarrier_set_arrayref");
        assign(strings, address(strings, sizeof(void*), 0), create(domain, "--native-submit"));
        assign(strings, address(strings, sizeof(void*), 1), create(domain, utf8(argv[4]).c_str()));
        void* arguments[]{strings}; void* error = nullptr;
        api<void* (*)(void*, void*, void**, void**)>(mono, "mono_runtime_invoke")(method, nullptr, arguments, &error);
        if (error) {
            auto message = api<void* (*)(void*, void**)>(mono, "mono_object_to_string")(error, nullptr);
            auto text = api<char* (*)(void*)>(mono, "mono_string_to_utf8")(message);
            std::fprintf(stderr, "Mono callback fixture failed: %s\n", text); return 1;
        }
        std::puts("Unity Windows Mono runtime: production 2D callback passed.");
        api<void (*)(void*)>(mono, "mono_jit_cleanup")(domain);
        return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "Mono fixture host failed: %s\n", error.what()); return 1; }
}
