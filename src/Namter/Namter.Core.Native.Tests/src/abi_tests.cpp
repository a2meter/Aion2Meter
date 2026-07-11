#include <cstddef>
#include <crtdbg.h>
#include <vector>

#include <gtest/gtest.h>

#include "namter/core.h"

namespace {

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
        NM_STATUS_OK);

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
