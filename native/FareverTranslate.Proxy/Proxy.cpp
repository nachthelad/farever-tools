#include <Windows.h>
#include <dinput.h>
#include <olectl.h>

#include <array>
#include <filesystem>
#include <string>
#include <vector>

namespace {

using DirectInput8CreateFn =
    HRESULT(WINAPI*)(HINSTANCE, DWORD, REFIID, LPVOID*, LPUNKNOWN);
using DllCanUnloadNowFn = HRESULT(WINAPI*)();
using DllGetClassObjectFn = HRESULT(WINAPI*)(REFCLSID, REFIID, LPVOID*);
using DllRegisterServerFn = HRESULT(WINAPI*)();
using DllUnregisterServerFn = HRESULT(WINAPI*)();

HMODULE g_self = nullptr;
HMODULE g_forwardModule = nullptr;
INIT_ONCE g_initOnce = INIT_ONCE_STATIC_INIT;

DirectInput8CreateFn g_directInput8Create = nullptr;
DllCanUnloadNowFn g_dllCanUnloadNow = nullptr;
DllGetClassObjectFn g_dllGetClassObject = nullptr;
DllRegisterServerFn g_dllRegisterServer = nullptr;
DllUnregisterServerFn g_dllUnregisterServer = nullptr;

void Debug(const std::wstring& message) {
    const std::wstring line = L"[FareverTranslate.Proxy] " + message + L"\n";
    OutputDebugStringW(line.c_str());
}

std::filesystem::path ModuleDirectory() {
    std::array<wchar_t, 32768> buffer{};
    const DWORD length =
        GetModuleFileNameW(g_self, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) {
        return {};
    }
    return std::filesystem::path(std::wstring(buffer.data(), length)).parent_path();
}

std::filesystem::path SystemDirectInputPath() {
    std::array<wchar_t, MAX_PATH + 1> buffer{};
    const UINT length =
        GetSystemDirectoryW(buffer.data(), static_cast<UINT>(buffer.size()));
    if (length == 0 || length >= buffer.size()) {
        return {};
    }
    return std::filesystem::path(std::wstring(buffer.data(), length)) / L"dinput8.dll";
}

HMODULE LoadAbsolute(const std::filesystem::path& path) {
    if (path.empty()) {
        return nullptr;
    }

    HMODULE module = LoadLibraryExW(
        path.c_str(),
        nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (module == nullptr) {
        module = LoadLibraryW(path.c_str());
    }
    return module;
}

template <typename T>
T Resolve(const char* name) {
    if (g_forwardModule == nullptr) {
        return nullptr;
    }
    return reinterpret_cast<T>(GetProcAddress(g_forwardModule, name));
}

void ResolveExports() {
    g_directInput8Create = Resolve<DirectInput8CreateFn>("DirectInput8Create");
    g_dllCanUnloadNow = Resolve<DllCanUnloadNowFn>("DllCanUnloadNow");
    g_dllGetClassObject = Resolve<DllGetClassObjectFn>("DllGetClassObject");
    g_dllRegisterServer = Resolve<DllRegisterServerFn>("DllRegisterServer");
    g_dllUnregisterServer =
        Resolve<DllUnregisterServerFn>("DllUnregisterServer");
}

void LaunchHost(const std::filesystem::path& directory) {
    const auto hostPath = directory / L"FareverTranslate.Host.exe";
    if (!std::filesystem::is_regular_file(hostPath)) {
        Debug(L"Host executable not found: " + hostPath.wstring());
        return;
    }

    const std::wstring command =
        L"\"" + hostPath.wstring() + L"\" --pid " +
        std::to_wstring(GetCurrentProcessId()) + L" --game-dir \"" +
        directory.wstring() + L"\"";
    std::vector<wchar_t> mutableCommand(command.begin(), command.end());
    mutableCommand.push_back(L'\0');

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = SW_SHOWNOACTIVATE;

    PROCESS_INFORMATION process{};
    const BOOL started = CreateProcessW(
        hostPath.c_str(),
        mutableCommand.data(),
        nullptr,
        nullptr,
        FALSE,
        CREATE_UNICODE_ENVIRONMENT,
        nullptr,
        directory.c_str(),
        &startup,
        &process);
    if (!started) {
        Debug(
            L"CreateProcessW failed with error " +
            std::to_wstring(GetLastError()));
        return;
    }

    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
}

BOOL CALLBACK Initialize(PINIT_ONCE, PVOID, PVOID*) {
    const auto directory = ModuleDirectory();
    const auto chainedPath = directory / L"farever-minimap.dll";

    g_forwardModule = LoadAbsolute(chainedPath);
    if (g_forwardModule != nullptr) {
        Debug(L"Loaded chained minimap: " + chainedPath.wstring());
    } else {
        const auto systemPath = SystemDirectInputPath();
        g_forwardModule = LoadAbsolute(systemPath);
        if (g_forwardModule != nullptr) {
            Debug(L"Minimap missing; using System32 DirectInput.");
        } else {
            Debug(L"Unable to load a DirectInput implementation.");
        }
    }

    ResolveExports();
    LaunchHost(directory);
    return TRUE;
}

void EnsureInitialized() {
    InitOnceExecuteOnce(&g_initOnce, Initialize, nullptr, nullptr);
}

}  // namespace

extern "C" HRESULT WINAPI DirectInput8Create(
    HINSTANCE instance,
    DWORD version,
    REFIID interfaceId,
    LPVOID* output,
    LPUNKNOWN outer) {
    EnsureInitialized();
    if (g_directInput8Create == nullptr) {
        return E_FAIL;
    }
    return g_directInput8Create(instance, version, interfaceId, output, outer);
}

extern "C" HRESULT WINAPI DllCanUnloadNow() {
    EnsureInitialized();
    return g_dllCanUnloadNow == nullptr ? S_FALSE : g_dllCanUnloadNow();
}

extern "C" HRESULT WINAPI DllGetClassObject(
    REFCLSID classId,
    REFIID interfaceId,
    LPVOID* output) {
    EnsureInitialized();
    if (g_dllGetClassObject == nullptr) {
        return CLASS_E_CLASSNOTAVAILABLE;
    }
    return g_dllGetClassObject(classId, interfaceId, output);
}

extern "C" HRESULT WINAPI DllRegisterServer() {
    EnsureInitialized();
    return g_dllRegisterServer == nullptr ? SELFREG_E_CLASS : g_dllRegisterServer();
}

extern "C" HRESULT WINAPI DllUnregisterServer() {
    EnsureInitialized();
    return g_dllUnregisterServer == nullptr ? SELFREG_E_CLASS
                                             : g_dllUnregisterServer();
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}
