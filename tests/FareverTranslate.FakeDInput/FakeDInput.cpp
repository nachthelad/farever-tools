#include <Windows.h>
#include <Unknwn.h>

extern "C" HRESULT WINAPI DirectInput8Create(
    HINSTANCE,
    DWORD,
    REFIID,
    LPVOID*,
    LPUNKNOWN) {
    return static_cast<HRESULT>(0x12340101L);
}

extern "C" HRESULT WINAPI DllCanUnloadNow() {
    return static_cast<HRESULT>(0x12340102L);
}

extern "C" HRESULT WINAPI DllGetClassObject(
    REFCLSID,
    REFIID,
    LPVOID*) {
    return static_cast<HRESULT>(0x12340103L);
}

extern "C" HRESULT WINAPI DllRegisterServer() {
    return static_cast<HRESULT>(0x12340104L);
}

extern "C" HRESULT WINAPI DllUnregisterServer() {
    return static_cast<HRESULT>(0x12340105L);
}
