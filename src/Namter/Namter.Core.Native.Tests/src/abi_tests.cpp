#include <cstddef>
#include <crtdbg.h>
#include <type_traits>
#include <vector>

#include <gtest/gtest.h>

#include "namter/core.h"

namespace {

static_assert(std::is_standard_layout_v<nm_event_v1>);
static_assert(sizeof(void*) == 8);
static_assert(sizeof(nm_event_v1) == 200);

void NM_CALL ignore_event(void*, const nm_event_v1*) {}
void NM_CALL ignore_diagnostic(void*, const nm_diagnostic_v1*) {}

nm_core_config_v1 valid_config() {
    return {
        .abi_version = nm_core_abi_version(),
        .struct_size = sizeof(nm_core_config_v1),
        .native_queue_capacity = 1024,
        .max_live_flows = 512,
        .max_ooo_bytes_per_flow = 1024 * 1024,
        .max_frame_bytes = 1024 * 1024,
        .max_decompressed_bytes = 4 * 1024 * 1024,
    };
}

nm_callbacks_v1 valid_callbacks() {
    return {
        .abi_version = nm_core_abi_version(),
        .struct_size = sizeof(nm_callbacks_v1),
        .user = nullptr,
        .event_callback = &ignore_event,
        .diagnostic_callback = &ignore_diagnostic,
    };
}

nm_source_config_v1 valid_source() {
    return {
        .abi_version = nm_core_abi_version(),
        .struct_size = sizeof(nm_source_config_v1),
        .kind = NM_SOURCE_PCAP,
        .source_data = nullptr,
        .source_data_size = 0,
    };
}

nm_core_handle* create_started_core() {
    auto config = valid_config();
    auto callbacks = valid_callbacks();
    nm_core_handle* handle = nullptr;
    EXPECT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    EXPECT_NE(handle, nullptr);
    if (handle != nullptr) {
        auto source = valid_source();
        EXPECT_EQ(nm_core_start(handle, &source), NM_STATUS_OK);
    }
    return handle;
}

void expect_create_status(nm_core_config_v1 config, nm_status expected) {
    auto callbacks = valid_callbacks();
    nm_core_handle* handle = nullptr;
    EXPECT_EQ(nm_core_create(&config, &callbacks, &handle), expected);
    if (handle != nullptr) {
        nm_core_destroy(handle);
    }
}

}  // namespace

TEST(Abi, ReportsVersionOne) {
    EXPECT_EQ(nm_core_abi_version(), 1u);
}

TEST(Abi, EventV1HasFrozenX64Layout) {
    EXPECT_EQ(offsetof(nm_event_v1, abi_version), 0u);
    EXPECT_EQ(offsetof(nm_event_v1, struct_size), 4u);
    EXPECT_EQ(offsetof(nm_event_v1, kind), 8u);
    EXPECT_EQ(offsetof(nm_event_v1, reserved), 12u);
    EXPECT_EQ(offsetof(nm_event_v1, first_timestamp_ns), 16u);
    EXPECT_EQ(offsetof(nm_event_v1, last_timestamp_ns), 24u);
    EXPECT_EQ(offsetof(nm_event_v1, epoch), 32u);
    EXPECT_EQ(offsetof(nm_event_v1, first_file_offset), 40u);
    EXPECT_EQ(offsetof(nm_event_v1, last_file_offset), 48u);
    EXPECT_EQ(offsetof(nm_event_v1, source_address), 56u);
    EXPECT_EQ(offsetof(nm_event_v1, destination_address), 60u);
    EXPECT_EQ(offsetof(nm_event_v1, source_port), 64u);
    EXPECT_EQ(offsetof(nm_event_v1, destination_port), 66u);
    EXPECT_EQ(offsetof(nm_event_v1, actor_id), 68u);
    EXPECT_EQ(offsetof(nm_event_v1, target_id), 72u);
    EXPECT_EQ(offsetof(nm_event_v1, owner_id), 76u);
    EXPECT_EQ(offsetof(nm_event_v1, skill_id), 80u);
    EXPECT_EQ(offsetof(nm_event_v1, buff_id), 84u);
    EXPECT_EQ(offsetof(nm_event_v1, mob_id), 88u);
    EXPECT_EQ(offsetof(nm_event_v1, boss_id), 92u);
    EXPECT_EQ(offsetof(nm_event_v1, content_id), 96u);
    EXPECT_EQ(offsetof(nm_event_v1, dungeon_id), 100u);
    EXPECT_EQ(offsetof(nm_event_v1, party_id), 104u);
    EXPECT_EQ(offsetof(nm_event_v1, server_id), 108u);
    EXPECT_EQ(offsetof(nm_event_v1, job_id), 110u);
    EXPECT_EQ(offsetof(nm_event_v1, damage), 112u);
    EXPECT_EQ(offsetof(nm_event_v1, multi_damage), 120u);
    EXPECT_EQ(offsetof(nm_event_v1, healing), 128u);
    EXPECT_EQ(offsetof(nm_event_v1, current_hp), 136u);
    EXPECT_EQ(offsetof(nm_event_v1, max_hp), 144u);
    EXPECT_EQ(offsetof(nm_event_v1, special_mask), 152u);
    EXPECT_EQ(offsetof(nm_event_v1, duration_ms), 156u);
    EXPECT_EQ(offsetof(nm_event_v1, state), 160u);
    EXPECT_EQ(offsetof(nm_event_v1, action), 161u);
    EXPECT_EQ(offsetof(nm_event_v1, damage_type), 162u);
    EXPECT_EQ(offsetof(nm_event_v1, is_dot), 163u);
    EXPECT_EQ(offsetof(nm_event_v1, is_self), 164u);
    EXPECT_EQ(offsetof(nm_event_v1, is_boss), 165u);
    EXPECT_EQ(offsetof(nm_event_v1, flags_reserved), 166u);
    EXPECT_EQ(offsetof(nm_event_v1, name), 168u);
    EXPECT_EQ(offsetof(nm_event_v1, name_size), 176u);
    EXPECT_EQ(offsetof(nm_event_v1, payload), 184u);
    EXPECT_EQ(offsetof(nm_event_v1, payload_size), 192u);
}

TEST(Abi, CreateRejectsWrongAbiVersion) {
    auto config = valid_config();
    config.abi_version = 99;
    auto callbacks = valid_callbacks();
    nm_core_handle* handle = nullptr;

    EXPECT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_ABI_MISMATCH);
    EXPECT_EQ(handle, nullptr);
}

TEST(Abi, CreateRejectsShortConfig) {
    auto config = valid_config();
    config.struct_size = sizeof(config) - 1;
    auto callbacks = valid_callbacks();
    nm_core_handle* handle = nullptr;

    EXPECT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_ABI_MISMATCH);
    EXPECT_EQ(handle, nullptr);
}

TEST(Abi, CreateRejectsNullCallbackTable) {
    auto config = valid_config();
    nm_core_handle* handle = nullptr;

    EXPECT_EQ(nm_core_create(&config, nullptr, &handle), NM_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(handle, nullptr);
}

TEST(Abi, CreateRejectsNullOutHandle) {
    auto config = valid_config();
    auto callbacks = valid_callbacks();

    EXPECT_EQ(nm_core_create(&config, &callbacks, nullptr), NM_STATUS_INVALID_ARGUMENT);
}

TEST(Abi, CreateEnforcesAllConfiguredBounds) {
    auto config = valid_config();

    config.native_queue_capacity = 0;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);
    config.native_queue_capacity = NM_CORE_NATIVE_QUEUE_CAPACITY_MIN - 1;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);
    config.native_queue_capacity = NM_CORE_NATIVE_QUEUE_CAPACITY_MAX;
    expect_create_status(config, NM_STATUS_OK);
    config.native_queue_capacity = NM_CORE_NATIVE_QUEUE_CAPACITY_MAX + 1;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);

    config = valid_config();
    config.max_live_flows = 0;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);
    config.max_live_flows = NM_CORE_MAX_LIVE_FLOWS_MIN - 1;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);
    config.max_live_flows = NM_CORE_MAX_LIVE_FLOWS_MAX;
    expect_create_status(config, NM_STATUS_OK);
    config.max_live_flows = NM_CORE_MAX_LIVE_FLOWS_MAX + 1;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);

    config = valid_config();
    config.max_ooo_bytes_per_flow = 0;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);
    config.max_ooo_bytes_per_flow = NM_CORE_MAX_OOO_BYTES_PER_FLOW_MIN - 1;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);
    config.max_ooo_bytes_per_flow = NM_CORE_MAX_OOO_BYTES_PER_FLOW_MAX;
    expect_create_status(config, NM_STATUS_OK);
    config.max_ooo_bytes_per_flow = NM_CORE_MAX_OOO_BYTES_PER_FLOW_MAX + 1;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);

    config = valid_config();
    config.max_frame_bytes = 0;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);
    config.max_frame_bytes = NM_CORE_MAX_FRAME_BYTES_MIN - 1;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);
    config.max_frame_bytes = NM_CORE_MAX_FRAME_BYTES_MAX;
    expect_create_status(config, NM_STATUS_OK);
    config.max_frame_bytes = NM_CORE_MAX_FRAME_BYTES_MAX + 1;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);

    config = valid_config();
    config.max_decompressed_bytes = 0;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);
    config.max_decompressed_bytes = NM_CORE_MAX_DECOMPRESSED_BYTES_MIN - 1;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);
    config.max_decompressed_bytes = NM_CORE_MAX_DECOMPRESSED_BYTES_MAX;
    expect_create_status(config, NM_STATUS_OK);
    config.max_decompressed_bytes = NM_CORE_MAX_DECOMPRESSED_BYTES_MAX + 1;
    expect_create_status(config, NM_STATUS_INVALID_ARGUMENT);
}

TEST(Abi, ProtocolSnapshotRejectsInvalidPointerAndSizeBeforeAllocation) {
    auto config = valid_config();
    auto callbacks = valid_callbacks();
    nm_core_handle* handle = nullptr;
    ASSERT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    ASSERT_NE(handle, nullptr);

    const uint8_t byte = 0;
    EXPECT_EQ(nm_core_set_protocol_snapshot(handle, nullptr, 0), NM_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(nm_core_set_protocol_snapshot(handle, nullptr, 1), NM_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(
        nm_core_set_protocol_snapshot(handle, &byte, NM_CORE_PROTOCOL_SNAPSHOT_MAX + 1ull),
        NM_STATUS_INVALID_ARGUMENT);

    std::vector<uint8_t> maximum_snapshot(NM_CORE_PROTOCOL_SNAPSHOT_MAX);
    EXPECT_EQ(
        nm_core_set_protocol_snapshot(handle, maximum_snapshot.data(), maximum_snapshot.size()),
        NM_STATUS_INVALID_ARGUMENT);

    nm_core_destroy(handle);
}

TEST(Abi, StopIsIdempotent) {
    nm_core_handle* handle = create_started_core();
    ASSERT_NE(handle, nullptr);

    EXPECT_EQ(nm_core_stop(handle), NM_STATUS_OK);
    EXPECT_EQ(nm_core_stop(handle), NM_STATUS_OK);

    nm_core_destroy(handle);
}

TEST(Abi, DestroyingStoppedHandleLeaksNoNativeAllocations) {
    // Warm any one-time CRT/runtime allocations before measuring the handle lifetime.
    nm_core_handle* warm_handle = create_started_core();
    ASSERT_NE(warm_handle, nullptr);
    ASSERT_EQ(nm_core_stop(warm_handle), NM_STATUS_OK);
    nm_core_destroy(warm_handle);

    _CrtMemState before{};
    _CrtMemState after{};
    _CrtMemState difference{};
    _CrtMemCheckpoint(&before);

    nm_core_handle* handle = create_started_core();
    ASSERT_NE(handle, nullptr);
    ASSERT_EQ(nm_core_stop(handle), NM_STATUS_OK);
    nm_core_destroy(handle);

    _CrtMemCheckpoint(&after);
    EXPECT_EQ(_CrtMemDifference(&difference, &before, &after), 0);
}
