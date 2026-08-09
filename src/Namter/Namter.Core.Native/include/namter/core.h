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

#define NM_CORE_NATIVE_QUEUE_CAPACITY_MIN 64u
#define NM_CORE_NATIVE_QUEUE_CAPACITY_MAX 1048576u
#define NM_CORE_MAX_LIVE_FLOWS_MIN 1u
#define NM_CORE_MAX_LIVE_FLOWS_MAX 1048576u
#define NM_CORE_MAX_OOO_BYTES_PER_FLOW_MIN 1024u
#define NM_CORE_MAX_OOO_BYTES_PER_FLOW_MAX 67108864u
#define NM_CORE_MAX_FRAME_BYTES_MIN 1024u
#define NM_CORE_MAX_FRAME_BYTES_MAX 16777216u
#define NM_CORE_MAX_DECOMPRESSED_BYTES_MIN 1024u
#define NM_CORE_MAX_DECOMPRESSED_BYTES_MAX 67108864u
#define NM_CORE_PROTOCOL_SNAPSHOT_MAX 16777216u
#define NM_CORE_DEFAULT_GAME_PORT 13328u
#define NM_CORE_ADAPTER_NAME_MAX 1024u

typedef enum nm_status {
    NM_STATUS_OK = 0,
    NM_STATUS_INVALID_ARGUMENT = 1,
    NM_STATUS_ABI_MISMATCH = 2,
    NM_STATUS_INVALID_STATE = 3,
    NM_STATUS_INTERNAL_ERROR = 4,
    NM_STATUS_NPCAP_NOT_INSTALLED = 5,
    NM_STATUS_BACKEND_UNAVAILABLE = 6,
    NM_STATUS_BACKEND_ERROR = 7,
} nm_status;

typedef enum nm_source_kind {
    NM_SOURCE_WINDIVERT = 1,
    NM_SOURCE_NPCAP = 2,
    NM_SOURCE_PCAP = 3,
} nm_source_kind;

typedef enum nm_event_kind {
    NM_EVENT_SOURCE_STARTED = 1,
    NM_EVENT_DAMAGE = 2,
    NM_EVENT_DOT = 3,
    NM_EVENT_BUFF = 4,
    NM_EVENT_SELF_ACTOR = 5,
    NM_EVENT_OTHER_ACTOR = 6,
    NM_EVENT_MOB_SPAWN = 7,
    NM_EVENT_BOSS_HP = 8,
    NM_EVENT_ENTITY_REMOVED = 9,
    NM_EVENT_PARTY = 10,
    NM_EVENT_CONTENT = 11,
    NM_EVENT_COMBAT_STATE = 12,
    NM_EVENT_UNKNOWN_PROTOCOL = 13,
    NM_EVENT_SOURCE_COMPLETED = 14,
} nm_event_kind;

typedef enum nm_buff_operation {
    NM_BUFF_OPERATION_UNKNOWN = 0,
    NM_BUFF_OPERATION_APPLY = 1,
    NM_BUFF_OPERATION_REFRESH = 2,
    NM_BUFF_OPERATION_REMOVE = 3,
} nm_buff_operation;

typedef enum nm_diagnostic_code {
    NM_DIAGNOSTIC_INCOMPLETE_STREAM = 1,
    NM_DIAGNOSTIC_CAPTURE_QUEUE_OVERFLOW = 2,
    NM_DIAGNOSTIC_CAPTURE_BACKEND_FAILED = 3,
} nm_diagnostic_code;

typedef struct nm_event_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t kind;
    uint32_t reserved;
    uint64_t first_timestamp_ns;
    uint64_t last_timestamp_ns;
    uint64_t epoch;
    uint64_t first_file_offset;
    uint64_t last_file_offset;
    uint32_t source_address;
    uint32_t destination_address;
    uint16_t source_port;
    uint16_t destination_port;
    uint32_t actor_id;
    uint32_t target_id;
    uint32_t owner_id;
    uint32_t skill_id;
    uint32_t buff_id;
    uint32_t mob_id;
    uint32_t boss_id;
    uint32_t content_id;
    uint32_t dungeon_id;
    uint32_t party_id;
    uint16_t server_id;
    uint16_t job_id;
    uint64_t damage;
    uint64_t multi_damage;
    uint64_t healing;
    uint64_t current_hp;
    uint64_t max_hp;
    uint32_t special_mask;
    uint32_t duration_ms;
    uint8_t state;
    uint8_t action;
    uint8_t damage_type;
    uint8_t is_dot;
    uint8_t is_self;
    uint8_t is_boss;
    uint8_t buff_operation;
    uint8_t flags_reserved;
    const uint8_t *name;
    size_t name_size;
    const uint8_t *payload;
    size_t payload_size;
} nm_event_v1;

typedef struct nm_diagnostic_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t code;
    const uint8_t *message;
    size_t message_size;
    uint32_t backend_kind;
    uint32_t stable_error;
    uint32_t native_error;
    uint8_t incomplete;
    uint8_t automatic_action;
    uint16_t reserved;
    uint64_t received;
    uint64_t dropped;
    uint64_t interface_dropped;
    uint64_t queue_high_water;
    const uint8_t *backend_name;
    size_t backend_name_size;
    const uint8_t *runtime_version;
    size_t runtime_version_size;
    const uint8_t *interface_identity;
    size_t interface_identity_size;
    const uint8_t *help_url;
    size_t help_url_size;
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

typedef void(NM_CALL *nm_event_callback_v1)(void *user, const nm_event_v1 *event);
typedef void(NM_CALL *nm_diagnostic_callback_v1)(void *user, const nm_diagnostic_v1 *diagnostic);

typedef struct nm_callbacks_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    void *user;
    nm_event_callback_v1 event_callback;
    nm_diagnostic_callback_v1 diagnostic_callback;
} nm_callbacks_v1;

typedef struct nm_source_config_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t kind;
    const uint8_t *source_data;
    size_t source_data_size;
} nm_source_config_v1;
/* For live sources, source_data is an optional UTF-8 Npcap adapter name
   (without NUL). WinDivert ignores it. Both live sources use
   NM_CORE_DEFAULT_GAME_PORT in ABI v1. */

typedef struct nm_diagnostics_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    uint64_t start_count;
    uint64_t stop_count;
    uint64_t emitted_event_count;
    uint64_t captured_packet_count;
    uint64_t dropped_capture_count;
    uint64_t invalid_packet_count;
    uint64_t backend_received;
    uint64_t backend_dropped;
    uint64_t backend_interface_dropped;
    uint64_t queue_high_water;
    uint64_t tcp_overlaps;
    uint64_t tcp_duplicate_bytes_removed;
    uint64_t tcp_unresolved_byte_gaps;
    uint8_t incomplete;
} nm_diagnostics_v1;

NM_API uint32_t NM_CALL nm_core_abi_version(void) noexcept;
NM_API nm_status NM_CALL nm_core_create(const nm_core_config_v1 *config,
                                        const nm_callbacks_v1 *callbacks,
                                        nm_core_handle **out_handle) noexcept;
NM_API nm_status NM_CALL nm_core_set_protocol_snapshot(nm_core_handle *handle, const uint8_t *data,
                                                       size_t size) noexcept;
NM_API nm_status NM_CALL nm_core_start(nm_core_handle *handle,
                                       const nm_source_config_v1 *source) noexcept;
NM_API nm_status NM_CALL nm_core_stop(nm_core_handle *handle) noexcept;
NM_API nm_status NM_CALL nm_core_get_diagnostics(nm_core_handle *handle,
                                                 nm_diagnostics_v1 *diagnostics) noexcept;
NM_API void NM_CALL nm_core_destroy(nm_core_handle *handle) noexcept;
