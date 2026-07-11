#pragma once

#include <cstdint>

#if defined(_WIN32)
#define NM_API extern "C" __declspec(dllexport)
#else
#define NM_API extern "C"
#endif

NM_API uint32_t nm_core_abi_version(void) noexcept;
