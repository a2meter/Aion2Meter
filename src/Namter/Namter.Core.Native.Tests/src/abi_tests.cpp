#include <atomic>
#include <chrono>
#include <condition_variable>
#include <crtdbg.h>
#include <cstddef>
#include <thread>
#include <span>
#include <stdexcept>
#include <type_traits>
#include <tuple>
#include <vector>

#include <gtest/gtest.h>

#include "live_backend.hpp"
#include "namter/core.h"

namespace {

static_assert(std::is_standard_layout_v<nm_event_v1>);
static_assert(sizeof(void *) == 8);
static_assert(sizeof(nm_event_v1) == 200);
static_assert(offsetof(nm_event_v1, abi_version) == 0);
static_assert(offsetof(nm_event_v1, struct_size) == 4);
static_assert(offsetof(nm_event_v1, kind) == 8);
static_assert(offsetof(nm_event_v1, reserved) == 12);
static_assert(offsetof(nm_event_v1, first_timestamp_ns) == 16);
static_assert(offsetof(nm_event_v1, last_timestamp_ns) == 24);
static_assert(offsetof(nm_event_v1, epoch) == 32);
static_assert(offsetof(nm_event_v1, first_file_offset) == 40);
static_assert(offsetof(nm_event_v1, last_file_offset) == 48);
static_assert(offsetof(nm_event_v1, source_address) == 56);
static_assert(offsetof(nm_event_v1, destination_address) == 60);
static_assert(offsetof(nm_event_v1, source_port) == 64);
static_assert(offsetof(nm_event_v1, destination_port) == 66);
static_assert(offsetof(nm_event_v1, actor_id) == 68);
static_assert(offsetof(nm_event_v1, target_id) == 72);
static_assert(offsetof(nm_event_v1, owner_id) == 76);
static_assert(offsetof(nm_event_v1, skill_id) == 80);
static_assert(offsetof(nm_event_v1, buff_id) == 84);
static_assert(offsetof(nm_event_v1, mob_id) == 88);
static_assert(offsetof(nm_event_v1, boss_id) == 92);
static_assert(offsetof(nm_event_v1, content_id) == 96);
static_assert(offsetof(nm_event_v1, dungeon_id) == 100);
static_assert(offsetof(nm_event_v1, party_id) == 104);
static_assert(offsetof(nm_event_v1, server_id) == 108);
static_assert(offsetof(nm_event_v1, job_id) == 110);
static_assert(offsetof(nm_event_v1, damage) == 112);
static_assert(offsetof(nm_event_v1, multi_damage) == 120);
static_assert(offsetof(nm_event_v1, healing) == 128);
static_assert(offsetof(nm_event_v1, current_hp) == 136);
static_assert(offsetof(nm_event_v1, max_hp) == 144);
static_assert(offsetof(nm_event_v1, special_mask) == 152);
static_assert(offsetof(nm_event_v1, duration_ms) == 156);
static_assert(offsetof(nm_event_v1, state) == 160);
static_assert(offsetof(nm_event_v1, action) == 161);
static_assert(offsetof(nm_event_v1, damage_type) == 162);
static_assert(offsetof(nm_event_v1, is_dot) == 163);
static_assert(offsetof(nm_event_v1, is_self) == 164);
static_assert(offsetof(nm_event_v1, is_boss) == 165);
static_assert(offsetof(nm_event_v1, buff_operation) == 166);
static_assert(offsetof(nm_event_v1, flags_reserved) == 167);
static_assert(offsetof(nm_event_v1, name) == 168);
static_assert(offsetof(nm_event_v1, name_size) == 176);
static_assert(offsetof(nm_event_v1, payload) == 184);
static_assert(offsetof(nm_event_v1, payload_size) == 192);
static_assert(sizeof(nm_diagnostic_v1) == 144);
static_assert(offsetof(nm_diagnostic_v1, abi_version) == 0);
static_assert(offsetof(nm_diagnostic_v1, struct_size) == 4);
static_assert(offsetof(nm_diagnostic_v1, code) == 8);
static_assert(offsetof(nm_diagnostic_v1, message) == 16);
static_assert(offsetof(nm_diagnostic_v1, message_size) == 24);
static_assert(offsetof(nm_diagnostic_v1, backend_kind) == 32);
static_assert(offsetof(nm_diagnostic_v1, stable_error) == 36);
static_assert(offsetof(nm_diagnostic_v1, native_error) == 40);
static_assert(offsetof(nm_diagnostic_v1, incomplete) == 44);
static_assert(offsetof(nm_diagnostic_v1, automatic_action) == 45);
static_assert(offsetof(nm_diagnostic_v1, reserved) == 46);
static_assert(offsetof(nm_diagnostic_v1, received) == 48);
static_assert(offsetof(nm_diagnostic_v1, dropped) == 56);
static_assert(offsetof(nm_diagnostic_v1, interface_dropped) == 64);
static_assert(offsetof(nm_diagnostic_v1, queue_high_water) == 72);
static_assert(offsetof(nm_diagnostic_v1, backend_name) == 80);
static_assert(offsetof(nm_diagnostic_v1, backend_name_size) == 88);
static_assert(offsetof(nm_diagnostic_v1, runtime_version) == 96);
static_assert(offsetof(nm_diagnostic_v1, runtime_version_size) == 104);
static_assert(offsetof(nm_diagnostic_v1, interface_identity) == 112);
static_assert(offsetof(nm_diagnostic_v1, interface_identity_size) == 120);
static_assert(offsetof(nm_diagnostic_v1, help_url) == 128);
static_assert(offsetof(nm_diagnostic_v1, help_url_size) == 136);
static_assert(sizeof(nm_diagnostics_v1) == 96);
static_assert(offsetof(nm_diagnostics_v1, abi_version) == 0);
static_assert(offsetof(nm_diagnostics_v1, struct_size) == 4);
static_assert(offsetof(nm_diagnostics_v1, start_count) == 8);
static_assert(offsetof(nm_diagnostics_v1, stop_count) == 16);
static_assert(offsetof(nm_diagnostics_v1, emitted_event_count) == 24);
static_assert(offsetof(nm_diagnostics_v1, captured_packet_count) == 32);
static_assert(offsetof(nm_diagnostics_v1, dropped_capture_count) == 40);
static_assert(offsetof(nm_diagnostics_v1, invalid_packet_count) == 48);
static_assert(offsetof(nm_diagnostics_v1, backend_received) == 56);
static_assert(offsetof(nm_diagnostics_v1, backend_dropped) == 64);
static_assert(offsetof(nm_diagnostics_v1, backend_interface_dropped) == 72);
static_assert(offsetof(nm_diagnostics_v1, queue_high_water) == 80);
static_assert(offsetof(nm_diagnostics_v1, incomplete) == 88);

void NM_CALL ignore_event(void *, const nm_event_v1 *) {}
void NM_CALL ignore_diagnostic(void *, const nm_diagnostic_v1 *) {}
void NM_CALL throw_diagnostic(void *, const nm_diagnostic_v1 *) {
    throw std::runtime_error("diagnostic callback failed");
}

std::atomic_uint32_t observed_diagnostic{0};
std::atomic_uint32_t observed_backend_kind{0}, observed_native_error{0};
std::atomic_bool observed_incomplete{false};
std::string observed_backend_name;
void NM_CALL observe_diagnostic(void *, const nm_diagnostic_v1 *value) {
    observed_backend_kind.store(value->backend_kind);
    observed_native_error.store(value->native_error);
    observed_incomplete.store(value->incomplete != 0);
    observed_backend_name = value->backend_name_size == 0
                                ? std::string{}
                                : std::string(reinterpret_cast<const char *>(value->backend_name),
                                              value->backend_name_size);
    observed_diagnostic.store(value->code, std::memory_order_release);
}

enum class EngineFakeMode { packet, overflow, failure, blocking, start_blocking, throwing, typed };
EngineFakeMode engine_fake_mode = EngineFakeMode::packet;
std::atomic_bool engine_fake_stop{false};
std::atomic_bool engine_fake_start_entered{false}, engine_fake_allow_start{false};
namter::CaptureSource engine_fake_source = namter::CaptureSource::npcap;

class EngineFakeBackend final : public namter::CaptureBackend {
  public:
    namter::BackendError start(const namter::BackendConfig &, namter::CaptureSink sink) override {
        sink_ = std::move(sink);
        if (engine_fake_mode == EngineFakeMode::start_blocking) {
            engine_fake_start_entered = true;
            while (!engine_fake_allow_start.load())
                std::this_thread::yield();
        }
        return namter::BackendError::none;
    }
    namter::BackendError poll() override {
        if (polled_)
            return namter::BackendError::cancelled;
    polled_ = true;
    if (engine_fake_mode == EngineFakeMode::throwing) throw std::runtime_error("injected poll");
        if (engine_fake_mode == EngineFakeMode::blocking) {
            while (!engine_fake_stop.load())
                std::this_thread::sleep_for(std::chrono::milliseconds(1));
            return namter::BackendError::cancelled;
        }
        if (engine_fake_mode == EngineFakeMode::failure)
            return namter::BackendError::receive_failed;
        std::vector<uint8_t> packet = {
            0x45, 0,    0, 0x28, 0, 0, 0, 0, 64, 6, 0, 0, 10,   0,    0, 1, 10, 0, 0, 2,
            0x34, 0x10, 0xc3, 0x50, 0, 0, 0, 1, 0,  0, 0, 0, 0x50, 0x18, 0, 0, 0,  0, 0, 0};
        if (engine_fake_mode == EngineFakeMode::typed) {
            packet[3] = 47;
            packet.insert(packet.end(), {0x0a,0x21,0x8d,0xc9,0x3f,0,1});
        }
        const size_t count = engine_fake_mode == EngineFakeMode::overflow ? 65 : 1;
        for (size_t index = 0; index < count; ++index) {
            sink_({.source = engine_fake_source,
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
    namter::BackendStats stats() const noexcept override {
        return engine_fake_mode == EngineFakeMode::blocking && engine_fake_stop.load()
                   ? namter::BackendStats{9, 2, 1, 9, 0, 0}
                   : namter::BackendStats{};
    }
    const namter::BackendDiagnostic &diagnostic() const noexcept override { return diagnostic_; }

  private:
    namter::CaptureSink sink_;
    bool polled_ = false;
    namter::BackendDiagnostic diagnostic_{};
};

std::unique_ptr<namter::CaptureBackend> engine_fake_factory(uint32_t kind) {
    engine_fake_source = kind == NM_SOURCE_WINDIVERT
        ? namter::CaptureSource::windivert : namter::CaptureSource::npcap;
    return std::make_unique<EngineFakeBackend>();
}

void append_le16(std::vector<uint8_t>& b,uint16_t v){b.push_back(static_cast<uint8_t>(v));b.push_back(static_cast<uint8_t>(v>>8));}
void append_le32(std::vector<uint8_t>& b,uint32_t v){for(int s=0;s<32;s+=8)b.push_back(static_cast<uint8_t>(v>>s));}
void append_le64(std::vector<uint8_t>& b,uint64_t v){append_le32(b,static_cast<uint32_t>(v));append_le32(b,static_cast<uint32_t>(v>>32));}
void write_le32(std::vector<uint8_t>&b,size_t o,uint32_t v){for(int s=0;s<32;s+=8)b[o++]=static_cast<uint8_t>(v>>s);}
uint32_t snapshot_crc(std::span<const uint8_t>b){uint32_t c=0xffffffffu;for(size_t i=0;i<b.size();++i){uint8_t v=i>=12&&i<16?0:b[i];c^=v;for(int bit=0;bit<8;++bit)c=(c>>1)^((0u-(c&1u))&0xedb88320u);}return~c;}
std::vector<uint8_t> removal_snapshot(){std::vector<uint8_t>b{'N','M','P','S'};append_le16(b,1);append_le16(b,28);append_le32(b,0);append_le32(b,0);append_le64(b,7);append_le32(b,3);append_le16(b,3);b.insert(b.end(),{6,0,0x36});append_le16(b,1);append_le16(b,13328);append_le32(b,1);append_le16(b,10);append_le16(b,2);b.insert(b.end(),{0x21,0x8d});append_le32(b,1);append_le32(b,1);append_le32(b,1);append_le32(b,128);append_le16(b,1);append_le16(b,0);append_le16(b,1);append_le16(b,4);append_le32(b,0);append_le32(b,1);append_le32(b,5);write_le32(b,8,static_cast<uint32_t>(b.size()));write_le32(b,12,snapshot_crc(b));return b;}
std::vector<uint8_t> removal_pcap(){std::vector<uint8_t> packet(40,0);packet[0]=0x45;packet[2]=0;packet[3]=47;packet[8]=64;packet[9]=6;packet[12]=10;packet[15]=1;packet[16]=10;packet[19]=2;packet[20]=0x34;packet[21]=0x10;packet[22]=0xc3;packet[23]=0x50;packet[27]=1;packet[32]=0x50;packet[33]=0x18;packet.insert(packet.end(),{0x0a,0x21,0x8d,0xc9,0x3f,0x00,0x01});std::vector<uint8_t>b{0xd4,0xc3,0xb2,0xa1};append_le16(b,2);append_le16(b,4);append_le32(b,0);append_le32(b,0);append_le32(b,65535);append_le32(b,101);append_le32(b,1);append_le32(b,0);append_le32(b,static_cast<uint32_t>(packet.size()));append_le32(b,static_cast<uint32_t>(packet.size()));b.insert(b.end(),packet.begin(),packet.end());return b;}

struct EventProbe{std::mutex mutex;std::condition_variable cv;bool removed=false;uint32_t actor=0;uint16_t source_port=0;uint16_t destination_port=0;};
void NM_CALL observe_event(void*user,const nm_event_v1*event){if(event->kind!=NM_EVENT_ENTITY_REMOVED)return;auto&probe=*static_cast<EventProbe*>(user);{std::scoped_lock lock(probe.mutex);probe.removed=true;probe.actor=event->actor_id;probe.source_port=event->source_port;probe.destination_port=event->destination_port;}probe.cv.notify_one();}
void NM_CALL throwing_event(void*,const nm_event_v1*event){if(event->kind==NM_EVENT_ENTITY_REMOVED)throw std::bad_alloc();}
void NM_CALL throwing_source_event(void*,const nm_event_v1*event){if(event->kind==NM_EVENT_SOURCE_STARTED)throw std::runtime_error("source callback");}
struct ReentrantStopProbe{nm_core_handle*handle=nullptr;nm_status status=NM_STATUS_INTERNAL_ERROR;};
void NM_CALL stop_on_source_event(void*user,const nm_event_v1*event){if(event->kind==NM_EVENT_SOURCE_STARTED){auto&probe=*static_cast<ReentrantStopProbe*>(user);probe.status=nm_core_stop(probe.handle);}}
void NM_CALL destroy_on_source_event(void*user,const nm_event_v1*event){if(event->kind==NM_EVENT_SOURCE_STARTED){auto&probe=*static_cast<ReentrantStopProbe*>(user);nm_core_destroy(probe.handle);probe.status=NM_STATUS_OK;}}
struct WorkerReentryProbe{nm_core_handle*handle=nullptr;std::atomic_bool fired=false;bool destroy=false;};
void NM_CALL worker_reentry_event(void*user,const nm_event_v1*event){auto&probe=*static_cast<WorkerReentryProbe*>(user);if(event->kind==NM_EVENT_ENTITY_REMOVED&&!probe.fired.exchange(true)){if(probe.destroy)nm_core_destroy(probe.handle);else nm_core_stop(probe.handle);}}
void NM_CALL worker_reentry_diagnostic(void*user,const nm_diagnostic_v1*){auto&probe=*static_cast<WorkerReentryProbe*>(user);if(!probe.fired.exchange(true)){if(probe.destroy)nm_core_destroy(probe.handle);else nm_core_stop(probe.handle);}}

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
    EXPECT_EQ(offsetof(nm_event_v1, buff_operation), 166u);
    EXPECT_EQ(offsetof(nm_event_v1, flags_reserved), 167u);
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

TEST(Abi, StopCancelsStartingBackendBeforeCommitAndWaitsForIdleRollback) {
    namter::set_backend_factory_for_testing(&engine_fake_factory);
    engine_fake_mode=EngineFakeMode::start_blocking;
    engine_fake_start_entered=false; engine_fake_allow_start=false;
    auto config=valid_config(); auto callbacks=valid_callbacks();
    nm_core_handle*handle=nullptr; ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);
    auto source=valid_source(); source.kind=NM_SOURCE_WINDIVERT;
    nm_status start_status=NM_STATUS_OK, stop_status=NM_STATUS_INTERNAL_ERROR;
    std::thread starter([&]{start_status=nm_core_start(handle,&source);});
    while(!engine_fake_start_entered.load()) std::this_thread::yield();
    std::thread stopper([&]{stop_status=nm_core_stop(handle);});
    std::this_thread::sleep_for(std::chrono::milliseconds(5));
    engine_fake_allow_start=true;
    starter.join(); stopper.join();
    EXPECT_EQ(start_status,NM_STATUS_INVALID_STATE); EXPECT_EQ(stop_status,NM_STATUS_OK);
    nm_diagnostics_v1 diagnostics{.abi_version=nm_core_abi_version(),.struct_size=sizeof(nm_diagnostics_v1)};
    ASSERT_EQ(nm_core_get_diagnostics(handle,&diagnostics),NM_STATUS_OK);
    EXPECT_EQ(diagnostics.start_count,0u); EXPECT_EQ(diagnostics.stop_count,1u);
    nm_core_destroy(handle); namter::set_backend_factory_for_testing(nullptr);
}

TEST(Abi, DestroyWaitsForStartingBackendRollbackBeforeDeletingHandle) {
    namter::set_backend_factory_for_testing(&engine_fake_factory);engine_fake_mode=EngineFakeMode::start_blocking;engine_fake_start_entered=false;engine_fake_allow_start=false;auto config=valid_config();auto callbacks=valid_callbacks();nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);auto source=valid_source();source.kind=NM_SOURCE_WINDIVERT;nm_status start_status=NM_STATUS_OK;std::thread starter([&]{start_status=nm_core_start(handle,&source);});while(!engine_fake_start_entered.load())std::this_thread::yield();std::thread destroyer([&]{nm_core_destroy(handle);});std::this_thread::sleep_for(std::chrono::milliseconds(5));engine_fake_allow_start=true;starter.join();destroyer.join();EXPECT_EQ(start_status,NM_STATUS_INVALID_STATE);namter::set_backend_factory_for_testing(nullptr);
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
    EXPECT_EQ(observed_backend_kind.load(),static_cast<uint32_t>(NM_SOURCE_WINDIVERT));
    EXPECT_TRUE(observed_incomplete.load());
    EXPECT_EQ(nm_core_stop(handle), NM_STATUS_OK);
    nm_core_destroy(handle);
    namter::set_backend_factory_for_testing(nullptr);
}

TEST(Abi, ThrowingBackendPollBecomesDiagnosticInsteadOfTerminatingWorker) {
    namter::set_backend_factory_for_testing(&engine_fake_factory);engine_fake_mode=EngineFakeMode::throwing;observed_diagnostic=0;auto config=valid_config();auto callbacks=valid_callbacks();callbacks.diagnostic_callback=&observe_diagnostic;nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);auto source=valid_source();source.kind=NM_SOURCE_WINDIVERT;ASSERT_EQ(nm_core_start(handle,&source),NM_STATUS_OK);for(int i=0;i<100&&observed_diagnostic.load()==0;++i)std::this_thread::sleep_for(std::chrono::milliseconds(1));EXPECT_EQ(observed_diagnostic.load(),static_cast<uint32_t>(NM_DIAGNOSTIC_CAPTURE_BACKEND_FAILED));EXPECT_EQ(nm_core_stop(handle),NM_STATUS_OK);nm_core_destroy(handle);namter::set_backend_factory_for_testing(nullptr);
}

TEST(Abi, ThrowingDiagnosticCallbackNeverEscapesWorkerOrStop) {
    namter::set_backend_factory_for_testing(&engine_fake_factory);
    engine_fake_mode = EngineFakeMode::failure;
    auto config = valid_config();
    auto callbacks = valid_callbacks();
    callbacks.diagnostic_callback = &throw_diagnostic;
    nm_core_handle *handle = nullptr;
    ASSERT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    auto source = valid_source();
    source.kind = NM_SOURCE_WINDIVERT;
    ASSERT_EQ(nm_core_start(handle, &source), NM_STATUS_OK);
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
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
    nm_diagnostics_v1 diagnostics{.abi_version=nm_core_abi_version(),
                                  .struct_size=sizeof(nm_diagnostics_v1)};
    ASSERT_EQ(nm_core_get_diagnostics(handle,&diagnostics),NM_STATUS_OK);
    EXPECT_EQ(diagnostics.backend_received,9u);
    EXPECT_EQ(diagnostics.backend_dropped,2u);
    EXPECT_EQ(diagnostics.backend_interface_dropped,1u);
    nm_core_destroy(handle);
    namter::set_backend_factory_for_testing(nullptr);
}

TEST(Abi, PcapBytesReachAuthoritativeDecoderAndEmitTypedEvent) {
    EventProbe probe;
    auto config=valid_config();
    auto callbacks=valid_callbacks();callbacks.user=&probe;callbacks.event_callback=&observe_event;
    nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);
    const auto snapshot=removal_snapshot();ASSERT_EQ(nm_core_set_protocol_snapshot(handle,snapshot.data(),snapshot.size()),NM_STATUS_OK);
    auto pcap=removal_pcap();auto source=valid_source();source.source_data=pcap.data();source.source_data_size=pcap.size();
    ASSERT_EQ(nm_core_start(handle,&source),NM_STATUS_OK);
    std::fill(pcap.begin(),pcap.end(),uint8_t{0});
    {std::unique_lock lock(probe.mutex);ASSERT_TRUE(probe.cv.wait_for(lock,std::chrono::seconds(2),[&]{return probe.removed;}));EXPECT_EQ(probe.actor,8137u);}
    EXPECT_EQ(nm_core_stop(handle),NM_STATUS_OK);nm_core_destroy(handle);
}

TEST(Abi, ThrowingEventSinkBecomesDiagnosticInsteadOfTerminatingWorker) {
    observed_diagnostic=0;auto config=valid_config();auto callbacks=valid_callbacks();callbacks.event_callback=&throwing_event;callbacks.diagnostic_callback=&observe_diagnostic;nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);const auto snapshot=removal_snapshot();ASSERT_EQ(nm_core_set_protocol_snapshot(handle,snapshot.data(),snapshot.size()),NM_STATUS_OK);const auto pcap=removal_pcap();auto source=valid_source();source.source_data=pcap.data();source.source_data_size=pcap.size();ASSERT_EQ(nm_core_start(handle,&source),NM_STATUS_OK);for(int i=0;i<100&&observed_diagnostic.load()==0;++i)std::this_thread::sleep_for(std::chrono::milliseconds(1));EXPECT_EQ(observed_diagnostic.load(),static_cast<uint32_t>(NM_DIAGNOSTIC_CAPTURE_BACKEND_FAILED));EXPECT_EQ(nm_core_stop(handle),NM_STATUS_OK);nm_core_destroy(handle);
}

TEST(Abi, ThrowingSourceStartedCallbackRollsBackWithoutWorkerOrLeak) {
    auto config=valid_config();auto callbacks=valid_callbacks();callbacks.event_callback=&throwing_source_event;callbacks.diagnostic_callback=&observe_diagnostic;nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);const auto pcap=removal_pcap();auto source=valid_source();source.source_data=pcap.data();source.source_data_size=pcap.size();EXPECT_EQ(nm_core_start(handle,&source),NM_STATUS_INTERNAL_ERROR);nm_diagnostics_v1 diagnostics{.abi_version=nm_core_abi_version(),.struct_size=sizeof(nm_diagnostics_v1)};ASSERT_EQ(nm_core_get_diagnostics(handle,&diagnostics),NM_STATUS_OK);EXPECT_EQ(diagnostics.start_count,0u);EXPECT_TRUE(diagnostics.incomplete);EXPECT_EQ(nm_core_stop(handle),NM_STATUS_OK);nm_core_destroy(handle);
}
TEST(Abi, SourceStartedReentrantStopCancelsCommitWithoutDeadlock) {
    ReentrantStopProbe probe;auto config=valid_config();auto callbacks=valid_callbacks();callbacks.user=&probe;callbacks.event_callback=&stop_on_source_event;nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);probe.handle=handle;const auto pcap=removal_pcap();auto source=valid_source();source.source_data=pcap.data();source.source_data_size=pcap.size();EXPECT_EQ(nm_core_start(handle,&source),NM_STATUS_INVALID_STATE);EXPECT_EQ(probe.status,NM_STATUS_OK);nm_diagnostics_v1 diagnostics{.abi_version=nm_core_abi_version(),.struct_size=sizeof(nm_diagnostics_v1)};ASSERT_EQ(nm_core_get_diagnostics(handle,&diagnostics),NM_STATUS_OK);EXPECT_EQ(diagnostics.start_count,0u);nm_core_destroy(handle);
}
TEST(Abi, SourceStartedReentrantDestroyDefersDeleteUntilStartRollback) {
    ReentrantStopProbe probe;auto config=valid_config();auto callbacks=valid_callbacks();callbacks.user=&probe;callbacks.event_callback=&destroy_on_source_event;nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);probe.handle=handle;const auto pcap=removal_pcap();auto source=valid_source();source.source_data=pcap.data();source.source_data_size=pcap.size();EXPECT_EQ(nm_core_start(handle,&source),NM_STATUS_INVALID_STATE);EXPECT_EQ(probe.status,NM_STATUS_OK);
}
TEST(Abi, WorkerEventReentrantStopSelfFinalizesAndAllowsNextStart) {
    WorkerReentryProbe probe;auto config=valid_config();auto callbacks=valid_callbacks();callbacks.user=&probe;callbacks.event_callback=&worker_reentry_event;nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);probe.handle=handle;const auto snapshot=removal_snapshot();ASSERT_EQ(nm_core_set_protocol_snapshot(handle,snapshot.data(),snapshot.size()),NM_STATUS_OK);const auto pcap=removal_pcap();auto source=valid_source();source.source_data=pcap.data();source.source_data_size=pcap.size();ASSERT_EQ(nm_core_start(handle,&source),NM_STATUS_OK);for(int i=0;i<100&&!probe.fired.load();++i)std::this_thread::sleep_for(std::chrono::milliseconds(1));ASSERT_TRUE(probe.fired.load());nm_status restart=NM_STATUS_INVALID_STATE;for(int i=0;i<100&&restart==NM_STATUS_INVALID_STATE;++i){restart=nm_core_start(handle,&source);if(restart==NM_STATUS_INVALID_STATE)std::this_thread::sleep_for(std::chrono::milliseconds(1));}EXPECT_EQ(restart,NM_STATUS_OK);EXPECT_EQ(nm_core_stop(handle),NM_STATUS_OK);nm_core_destroy(handle);
}
TEST(Abi, WorkerEventReentrantDestroyDefersDeleteUntilWorkerExit) {
    WorkerReentryProbe probe;probe.destroy=true;auto config=valid_config();auto callbacks=valid_callbacks();callbacks.user=&probe;callbacks.event_callback=&worker_reentry_event;nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);probe.handle=handle;const auto snapshot=removal_snapshot();ASSERT_EQ(nm_core_set_protocol_snapshot(handle,snapshot.data(),snapshot.size()),NM_STATUS_OK);const auto pcap=removal_pcap();auto source=valid_source();source.source_data=pcap.data();source.source_data_size=pcap.size();ASSERT_EQ(nm_core_start(handle,&source),NM_STATUS_OK);for(int i=0;i<100&&!probe.fired.load();++i)std::this_thread::sleep_for(std::chrono::milliseconds(1));EXPECT_TRUE(probe.fired.load());std::this_thread::sleep_for(std::chrono::milliseconds(10));
}
TEST(Abi, WorkerDiagnosticReentrantStopSelfFinalizesWithoutSelfJoin) {
    namter::set_backend_factory_for_testing(&engine_fake_factory);engine_fake_mode=EngineFakeMode::failure;WorkerReentryProbe probe;auto config=valid_config();auto callbacks=valid_callbacks();callbacks.user=&probe;callbacks.diagnostic_callback=&worker_reentry_diagnostic;nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);probe.handle=handle;auto source=valid_source();source.kind=NM_SOURCE_WINDIVERT;ASSERT_EQ(nm_core_start(handle,&source),NM_STATUS_OK);for(int i=0;i<100&&!probe.fired.load();++i)std::this_thread::sleep_for(std::chrono::milliseconds(1));EXPECT_TRUE(probe.fired.load());std::this_thread::sleep_for(std::chrono::milliseconds(10));nm_core_destroy(handle);namter::set_backend_factory_for_testing(nullptr);
}
TEST(Abi, WorkerDiagnosticReentrantDestroyDefersDeleteUntilWorkerExit) {
    namter::set_backend_factory_for_testing(&engine_fake_factory);engine_fake_mode=EngineFakeMode::failure;WorkerReentryProbe probe;probe.destroy=true;auto config=valid_config();auto callbacks=valid_callbacks();callbacks.user=&probe;callbacks.diagnostic_callback=&worker_reentry_diagnostic;nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);probe.handle=handle;auto source=valid_source();source.kind=NM_SOURCE_WINDIVERT;ASSERT_EQ(nm_core_start(handle,&source),NM_STATUS_OK);for(int i=0;i<100&&!probe.fired.load();++i)std::this_thread::sleep_for(std::chrono::milliseconds(1));EXPECT_TRUE(probe.fired.load());std::this_thread::sleep_for(std::chrono::milliseconds(10));namter::set_backend_factory_for_testing(nullptr);
}

TEST(Abi, TruncatedPcapEmitsStructuredIncompleteDiagnostic) {
    observed_diagnostic=0;auto config=valid_config();auto callbacks=valid_callbacks();callbacks.diagnostic_callback=&observe_diagnostic;nm_core_handle*handle=nullptr;ASSERT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);auto pcap=removal_pcap();pcap.pop_back();auto source=valid_source();source.source_data=pcap.data();source.source_data_size=pcap.size();ASSERT_EQ(nm_core_start(handle,&source),NM_STATUS_OK);for(int i=0;i<100&&observed_diagnostic.load()==0;++i)std::this_thread::sleep_for(std::chrono::milliseconds(1));EXPECT_EQ(observed_diagnostic.load(),static_cast<uint32_t>(NM_DIAGNOSTIC_INCOMPLETE_STREAM));EXPECT_EQ(observed_backend_kind.load(),static_cast<uint32_t>(NM_SOURCE_PCAP));EXPECT_EQ(observed_backend_name,"pcap");EXPECT_NE(observed_native_error.load(),0u);EXPECT_TRUE(observed_incomplete.load());EXPECT_EQ(nm_core_stop(handle),NM_STATUS_OK);nm_core_destroy(handle);
}

TEST(Abi, InjectedWinDivertNpcapAndPcapProduceIdenticalTypedLedger) {
    const auto run=[&](uint32_t kind){EventProbe probe;auto config=valid_config();auto callbacks=valid_callbacks();callbacks.user=&probe;callbacks.event_callback=&observe_event;nm_core_handle*handle=nullptr;EXPECT_EQ(nm_core_create(&config,&callbacks,&handle),NM_STATUS_OK);const auto snapshot=removal_snapshot();EXPECT_EQ(nm_core_set_protocol_snapshot(handle,snapshot.data(),snapshot.size()),NM_STATUS_OK);std::vector<uint8_t> source_bytes;if(kind==NM_SOURCE_PCAP){namter::set_backend_factory_for_testing(nullptr);source_bytes=removal_pcap();}else{engine_fake_mode=EngineFakeMode::typed;namter::set_backend_factory_for_testing(&engine_fake_factory);}auto source=valid_source();source.kind=kind;source.source_data=source_bytes.empty()?nullptr:source_bytes.data();source.source_data_size=source_bytes.size();EXPECT_EQ(nm_core_start(handle,&source),NM_STATUS_OK);{std::unique_lock lock(probe.mutex);EXPECT_TRUE(probe.cv.wait_for(lock,std::chrono::seconds(2),[&]{return probe.removed;}));}EXPECT_EQ(nm_core_stop(handle),NM_STATUS_OK);nm_core_destroy(handle);namter::set_backend_factory_for_testing(nullptr);return std::tuple{probe.actor,probe.source_port,probe.destination_port};};
    const auto windivert=run(NM_SOURCE_WINDIVERT);const auto npcap=run(NM_SOURCE_NPCAP);const auto pcap=run(NM_SOURCE_PCAP);EXPECT_EQ(windivert,npcap);EXPECT_EQ(npcap,pcap);EXPECT_EQ(std::get<0>(pcap),8137u);EXPECT_EQ(std::get<1>(pcap),13328u);EXPECT_EQ(std::get<2>(pcap),50000u);
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
