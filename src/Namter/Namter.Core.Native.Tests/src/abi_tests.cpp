#include <atomic>
#include <chrono>
#include <crtdbg.h>
#include <cstddef>
#include <thread>
#include <type_traits>
#include <vector>

#include <gtest/gtest.h>

#include "live_backend.hpp"
#include "namter/core.h"

namespace {

static_assert(std::is_standard_layout_v<nm_event_v1>);
static_assert(sizeof(void *) == 8);
static_assert(sizeof(nm_event_v1) == 200);

void NM_CALL ignore_event(void *, const nm_event_v1 *) {}
void NM_CALL ignore_diagnostic(void *, const nm_diagnostic_v1 *) {}

std::atomic_uint32_t observed_diagnostic{0};
void NM_CALL observe_diagnostic(void *, const nm_diagnostic_v1 *value) {
    observed_diagnostic.store(value->code);
}

enum class EngineFakeMode { packet, overflow, failure, blocking };
EngineFakeMode engine_fake_mode = EngineFakeMode::packet;
std::atomic_bool engine_fake_stop{false};

class EngineFakeBackend final : public namter::CaptureBackend {
  public:
    namter::BackendError start(const namter::BackendConfig &, namter::CaptureSink sink) override {
        sink_ = std::move(sink);
        return namter::BackendError::none;
    }
    namter::BackendError poll() override {
        if (polled_)
            return namter::BackendError::cancelled;
        polled_ = true;
        if (engine_fake_mode == EngineFakeMode::blocking) {
            while (!engine_fake_stop.load())
                std::this_thread::sleep_for(std::chrono::milliseconds(1));
            return namter::BackendError::cancelled;
        }
        if (engine_fake_mode == EngineFakeMode::failure)
            return namter::BackendError::receive_failed;
        const std::vector<uint8_t> packet = {
            0x45, 0,    0, 0x28, 0, 0, 0, 0, 64, 6, 0, 0, 10,   0,    0, 1, 10, 0, 0, 2,
            0x34, 0x10, 0, 0x50, 0, 0, 0, 1, 0,  0, 0, 0, 0x50, 0x18, 0, 0, 0,  0, 0, 0};
        const size_t count = engine_fake_mode == EngineFakeMode::overflow ? 65 : 1;
        for (size_t index = 0; index < count; ++index) {
            sink_({.source = namter::CaptureSource::npcap,
                   .timestamp_ns = index,
                   .link_type = namter::dlt_raw,
                   .captured_length = static_cast<uint32_t>(packet.size()),
                   .original_length = static_cast<uint32_t>(packet.size()),
                   .bytes = packet});
        }
        return namter::BackendError::cancelled;
    }
    void stop() noexcept override {}
    void request_stop() noexcept override { engine_fake_stop = true; }
    namter::BackendStats stats() const noexcept override { return {}; }
    const namter::BackendDiagnostic &diagnostic() const noexcept override { return diagnostic_; }

  private:
    namter::CaptureSink sink_;
    bool polled_ = false;
    namter::BackendDiagnostic diagnostic_{};
};

std::unique_ptr<namter::CaptureBackend> engine_fake_factory(uint32_t) {
    return std::make_unique<EngineFakeBackend>();
}

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

nm_core_handle *create_started_core() {
    auto config = valid_config();
    auto callbacks = valid_callbacks();
    nm_core_handle *handle = nullptr;
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
    nm_core_handle *handle = nullptr;
    EXPECT_EQ(nm_core_create(&config, &callbacks, &handle), expected);
    if (handle != nullptr) {
        nm_core_destroy(handle);
    }
}

} // namespace

TEST(Abi, ReportsVersionOne) { EXPECT_EQ(nm_core_abi_version(), 1u); }

TEST(Abi, ExplicitNpcapSelectionNeverFallsBackWhenExternalRuntimeIsAbsent) {
    if (namter::probe_npcap_runtime())
        GTEST_SKIP() << "compatible external Npcap runtime is installed";
    auto config = valid_config();
    auto callbacks = valid_callbacks();
    nm_core_handle *handle = nullptr;
    ASSERT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    auto source = valid_source();
    source.kind = NM_SOURCE_NPCAP;
    EXPECT_EQ(nm_core_start(handle, &source), NM_STATUS_NPCAP_NOT_INSTALLED);
    nm_diagnostics_v1 diagnostics{.abi_version = nm_core_abi_version(),
                                  .struct_size = sizeof(nm_diagnostics_v1)};
    ASSERT_EQ(nm_core_get_diagnostics(handle, &diagnostics), NM_STATUS_OK);
    EXPECT_EQ(diagnostics.start_count, 0u);
    EXPECT_EQ(diagnostics.emitted_event_count, 0u);
    nm_core_destroy(handle);
}

TEST(Abi, ExplicitWinDivertSelectionNeverFallsBackWhenRuntimeIsAbsent) {
    if (namter::probe_windivert_runtime())
        GTEST_SKIP() << "compatible app-local WinDivert runtime is installed";
    auto config = valid_config();
    auto callbacks = valid_callbacks();
    nm_core_handle *handle = nullptr;
    ASSERT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    auto source = valid_source();
    source.kind = NM_SOURCE_WINDIVERT;
    EXPECT_EQ(nm_core_start(handle, &source), NM_STATUS_BACKEND_UNAVAILABLE);
    nm_core_destroy(handle);
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
    nm_core_handle *handle = nullptr;

    EXPECT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_ABI_MISMATCH);
    EXPECT_EQ(handle, nullptr);
}

TEST(Abi, CreateRejectsShortConfig) {
    auto config = valid_config();
    config.struct_size = sizeof(config) - 1;
    auto callbacks = valid_callbacks();
    nm_core_handle *handle = nullptr;

    EXPECT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_ABI_MISMATCH);
    EXPECT_EQ(handle, nullptr);
}

TEST(Abi, CreateRejectsNullCallbackTable) {
    auto config = valid_config();
    nm_core_handle *handle = nullptr;

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
    nm_core_handle *handle = nullptr;
    ASSERT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    ASSERT_NE(handle, nullptr);

    const uint8_t byte = 0;
    EXPECT_EQ(nm_core_set_protocol_snapshot(handle, nullptr, 0), NM_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(nm_core_set_protocol_snapshot(handle, nullptr, 1), NM_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(nm_core_set_protocol_snapshot(handle, &byte, NM_CORE_PROTOCOL_SNAPSHOT_MAX + 1ull),
              NM_STATUS_INVALID_ARGUMENT);

    std::vector<uint8_t> maximum_snapshot(NM_CORE_PROTOCOL_SNAPSHOT_MAX);
    EXPECT_EQ(
        nm_core_set_protocol_snapshot(handle, maximum_snapshot.data(), maximum_snapshot.size()),
        NM_STATUS_INVALID_ARGUMENT);

    nm_core_destroy(handle);
}

TEST(Abi, StopIsIdempotent) {
    nm_core_handle *handle = create_started_core();
    ASSERT_NE(handle, nullptr);

    EXPECT_EQ(nm_core_stop(handle), NM_STATUS_OK);
    EXPECT_EQ(nm_core_stop(handle), NM_STATUS_OK);

    nm_diagnostics_v1 diagnostics{.abi_version = nm_core_abi_version(),
                                  .struct_size = sizeof(nm_diagnostics_v1)};
    ASSERT_EQ(nm_core_get_diagnostics(handle, &diagnostics), NM_STATUS_OK);
    EXPECT_EQ(diagnostics.stop_count, 1u);

    nm_core_destroy(handle);
}

TEST(Abi, StopBeforeStartIsANoOpAndDoesNotCountTransition) {
    auto config = valid_config();
    auto callbacks = valid_callbacks();
    nm_core_handle *handle = nullptr;
    ASSERT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    EXPECT_EQ(nm_core_stop(handle), NM_STATUS_OK);
    nm_diagnostics_v1 diagnostics{.abi_version = nm_core_abi_version(),
                                  .struct_size = sizeof(nm_diagnostics_v1)};
    ASSERT_EQ(nm_core_get_diagnostics(handle, &diagnostics), NM_STATUS_OK);
    EXPECT_EQ(diagnostics.stop_count, 0u);
    nm_core_destroy(handle);
}

TEST(Abi, ConcurrentDoubleStartReservesStateBeforeOpeningSource) {
    auto config = valid_config();
    auto callbacks = valid_callbacks();
    nm_core_handle *handle = nullptr;
    ASSERT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    auto source = valid_source();
    nm_status first = NM_STATUS_INTERNAL_ERROR, second = NM_STATUS_INTERNAL_ERROR;
    std::thread a([&] { first = nm_core_start(handle, &source); });
    std::thread b([&] { second = nm_core_start(handle, &source); });
    a.join();
    b.join();
    EXPECT_TRUE((first == NM_STATUS_OK && second == NM_STATUS_INVALID_STATE) ||
                (second == NM_STATUS_OK && first == NM_STATUS_INVALID_STATE));
    EXPECT_EQ(nm_core_stop(handle), NM_STATUS_OK);
    nm_core_destroy(handle);
}

TEST(Abi, LiveAdapterNameRejectsEmbeddedNulAndOversize) {
    auto config = valid_config();
    auto callbacks = valid_callbacks();
    nm_core_handle *handle = nullptr;
    ASSERT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    std::vector<uint8_t> name{'a', 0, 'b'};
    auto source = valid_source();
    source.kind = NM_SOURCE_NPCAP;
    source.source_data = name.data();
    source.source_data_size = name.size();
    EXPECT_EQ(nm_core_start(handle, &source), NM_STATUS_INVALID_ARGUMENT);
    name.assign(NM_CORE_ADAPTER_NAME_MAX + 1, 'x');
    source.source_data = name.data();
    source.source_data_size = name.size();
    EXPECT_EQ(nm_core_start(handle, &source), NM_STATUS_INVALID_ARGUMENT);
    nm_core_destroy(handle);
}

TEST(Abi, InjectedLivePacketsEnterBoundedEngineQueueAndExposeLossCounters) {
    namter::set_backend_factory_for_testing(&engine_fake_factory);
    engine_fake_mode = EngineFakeMode::overflow;
    auto config = valid_config();
    config.native_queue_capacity = NM_CORE_NATIVE_QUEUE_CAPACITY_MIN;
    auto callbacks = valid_callbacks();
    nm_core_handle *handle = nullptr;
    ASSERT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    auto source = valid_source();
    source.kind = NM_SOURCE_NPCAP;
    ASSERT_EQ(nm_core_start(handle, &source), NM_STATUS_OK);
    nm_diagnostics_v1 diagnostics{.abi_version = nm_core_abi_version(),
                                  .struct_size = sizeof(nm_diagnostics_v1)};
    for (int attempt = 0; attempt < 100; ++attempt) {
        ASSERT_EQ(nm_core_get_diagnostics(handle, &diagnostics), NM_STATUS_OK);
        if (diagnostics.captured_packet_count == 64)
            break;
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    EXPECT_EQ(diagnostics.captured_packet_count, 64u);
    EXPECT_EQ(diagnostics.dropped_capture_count, 1u);
    EXPECT_EQ(nm_core_stop(handle), NM_STATUS_OK);
    nm_core_destroy(handle);
    namter::set_backend_factory_for_testing(nullptr);
}

TEST(Abi, InjectedPollFailureEmitsStructuredDiagnosticAndStopDoesNotDeadlock) {
    namter::set_backend_factory_for_testing(&engine_fake_factory);
    engine_fake_mode = EngineFakeMode::failure;
    observed_diagnostic = 0;
    auto config = valid_config();
    auto callbacks = valid_callbacks();
    callbacks.diagnostic_callback = &observe_diagnostic;
    nm_core_handle *handle = nullptr;
    ASSERT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    auto source = valid_source();
    source.kind = NM_SOURCE_WINDIVERT;
    ASSERT_EQ(nm_core_start(handle, &source), NM_STATUS_OK);
    for (int attempt = 0; attempt < 100 && observed_diagnostic.load() == 0; ++attempt) {
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    EXPECT_EQ(observed_diagnostic.load(),
              static_cast<uint32_t>(NM_DIAGNOSTIC_CAPTURE_BACKEND_FAILED));
    EXPECT_EQ(nm_core_stop(handle), NM_STATUS_OK);
    nm_core_destroy(handle);
    namter::set_backend_factory_for_testing(nullptr);
}

TEST(Abi, BlockingPollIsCancelledJoinedThenClosedWithoutDeadlock) {
    namter::set_backend_factory_for_testing(&engine_fake_factory);
    engine_fake_mode = EngineFakeMode::blocking;
    engine_fake_stop = false;
    auto config = valid_config();
    auto callbacks = valid_callbacks();
    nm_core_handle *handle = nullptr;
    ASSERT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    auto source = valid_source();
    source.kind = NM_SOURCE_WINDIVERT;
    ASSERT_EQ(nm_core_start(handle, &source), NM_STATUS_OK);
    EXPECT_EQ(nm_core_stop(handle), NM_STATUS_OK);
    nm_core_destroy(handle);
    namter::set_backend_factory_for_testing(nullptr);
}

TEST(Abi, DestroyingStoppedHandleLeaksNoNativeAllocations) {
    // Warm any one-time CRT/runtime allocations before measuring the handle
    // lifetime.
    nm_core_handle *warm_handle = create_started_core();
    ASSERT_NE(warm_handle, nullptr);
    ASSERT_EQ(nm_core_stop(warm_handle), NM_STATUS_OK);
    nm_core_destroy(warm_handle);

    _CrtMemState before{};
    _CrtMemState after{};
    _CrtMemState difference{};
    _CrtMemCheckpoint(&before);

    nm_core_handle *handle = create_started_core();
    ASSERT_NE(handle, nullptr);
    ASSERT_EQ(nm_core_stop(handle), NM_STATUS_OK);
    nm_core_destroy(handle);

    _CrtMemCheckpoint(&after);
    EXPECT_EQ(_CrtMemDifference(&difference, &before, &after), 0);
}
