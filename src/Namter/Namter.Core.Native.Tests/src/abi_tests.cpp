#include <cstddef>
#include <crtdbg.h>

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
