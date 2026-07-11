#pragma once

#include <Windows.h>
#include <filesystem>
#include <utility>
#include <vector>

namespace namter {

class DynamicLibrary final {
  public:
    DynamicLibrary() = default;
    explicit DynamicLibrary(const std::filesystem::path &absolute_path) noexcept {
        if (!absolute_path.is_absolute())
            return;
        handle_ = ::LoadLibraryExW(absolute_path.c_str(), nullptr,
                                   LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    }
    ~DynamicLibrary() {
        if (handle_)
            ::FreeLibrary(handle_);
    }
    DynamicLibrary(const DynamicLibrary &) = delete;
    DynamicLibrary &operator=(const DynamicLibrary &) = delete;
    DynamicLibrary(DynamicLibrary &&other) noexcept
        : handle_(std::exchange(other.handle_, nullptr)) {}
    DynamicLibrary &operator=(DynamicLibrary &&other) noexcept {
        if (this != &other) {
            if (handle_)
                ::FreeLibrary(handle_);
            handle_ = std::exchange(other.handle_, nullptr);
        }
        return *this;
    }
    [[nodiscard]] explicit operator bool() const noexcept { return handle_ != nullptr; }
    template <class T> [[nodiscard]] T symbol(const char *name) const noexcept {
        return reinterpret_cast<T>(::GetProcAddress(handle_, name));
    }

  private:
    HMODULE handle_ = nullptr;
};

inline std::filesystem::path application_library_path(const wchar_t *name) {
    std::vector<wchar_t> buffer(32768);
    const DWORD length =
        ::GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size())
        return {};
    return std::filesystem::path(std::wstring(buffer.data(), length)).parent_path() / name;
}

inline std::filesystem::path npcap_library_path() {
    std::vector<wchar_t> buffer(32768);
    const UINT length = ::GetSystemDirectoryW(buffer.data(), static_cast<UINT>(buffer.size()));
    if (length == 0 || length >= buffer.size())
        return {};
    return std::filesystem::path(std::wstring(buffer.data(), length)) / L"Npcap" / L"wpcap.dll";
}

} // namespace namter
