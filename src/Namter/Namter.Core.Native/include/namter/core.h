#pragma once

#include <cstddef>
#include <cstdint>

#if defined(_WIN32)
#define NM_API extern "C" __declspec(dllexport)
#define NM_CALL __cdecl
#else
#define NM_API extern "C"
#define NM_CALL
#endif

typedef struct nm_core_handle nm_core_handle;

typedef enum nm_status {
    NM_STATUS_OK = 0,
    NM_STATUS_INVALID_ARGUMENT = 1,
    NM_STATUS_ABI_MISMATCH = 2,
    NM_STATUS_INVALID_STATE = 3,
    NM_STATUS_INTERNAL_ERROR = 4,
} nm_status;

typedef enum nm_source_kind {
    NM_SOURCE_WINDIVERT = 1,
    NM_SOURCE_NPCAP = 2,
    NM_SOURCE_PCAP = 3,
} nm_source_kind;

typedef enum nm_event_kind {
    NM_EVENT_SOURCE_STARTED = 1,
} nm_event_kind;

typedef enum nm_diagnostic_code {
    NM_DIAGNOSTIC_INCOMPLETE_STREAM = 1,
} nm_diagnostic_code;

typedef struct nm_event_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t kind;
    const uint8_t* payload;
    size_t payload_size;
} nm_event_v1;

typedef struct nm_diagnostic_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t code;
    const uint8_t* message;
    size_t message_size;
} nm_diagnostic_v1;

typedef struct nm_core_config_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t native_queue_capacity;
    uint32_t max_live_flows;
    uint32_t max_ooo_bytes_per_flow;
    uint32_t max_frame_bytes;
    uint32_t max_decompressed_bytes;
} nm_core_config_v1;

typedef void(NM_CALL* nm_event_callback_v1)(void* user, const nm_event_v1* event);
typedef void(NM_CALL* nm_diagnostic_callback_v1)(void* user, const nm_diagnostic_v1* diagnostic);

typedef struct nm_callbacks_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    void* user;
    nm_event_callback_v1 event_callback;
    nm_diagnostic_callback_v1 diagnostic_callback;
} nm_callbacks_v1;

typedef struct nm_source_config_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t kind;
    const uint8_t* source_data;
    size_t source_data_size;
} nm_source_config_v1;

typedef struct nm_diagnostics_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    uint64_t start_count;
    uint64_t stop_count;
    uint64_t emitted_event_count;
} nm_diagnostics_v1;

NM_API uint32_t NM_CALL nm_core_abi_version(void) noexcept;
NM_API nm_status NM_CALL nm_core_create(
    const nm_core_config_v1* config,
    const nm_callbacks_v1* callbacks,
    nm_core_handle** out_handle) noexcept;
NM_API nm_status NM_CALL nm_core_set_protocol_snapshot(
    nm_core_handle* handle,
    const uint8_t* data,
    size_t size) noexcept;
NM_API nm_status NM_CALL nm_core_start(
    nm_core_handle* handle,
    const nm_source_config_v1* source) noexcept;
NM_API nm_status NM_CALL nm_core_stop(nm_core_handle* handle) noexcept;
NM_API nm_status NM_CALL nm_core_get_diagnostics(
    nm_core_handle* handle,
    nm_diagnostics_v1* diagnostics) noexcept;
NM_API void NM_CALL nm_core_destroy(nm_core_handle* handle) noexcept;
