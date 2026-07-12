#pragma once

#include "capture_record.hpp"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace namter {

enum class ApiResult : uint8_t { ok, would_block, cancelled, failed, incompatible };
enum class BackendError : uint8_t {
    none,
    library_missing,
    symbol_missing,
    incompatible_runtime,
    invalid_config,
    open_failed,
    activate_failed,
    adapter_not_found,
    receive_failed,
    cancelled,
    npcap_not_installed,
};

struct BackendConfig {
    uint16_t port = 0;
    std::string adapter;
    uint64_t queue_length = 4096;
    uint64_t queue_size = 8u * 1024u * 1024u;
    uint64_t queue_time_ms = 1000;
    size_t batch_size = 32;
    int kernel_buffer_size = 4 * 1024 * 1024;
    int user_buffer_size = 1024 * 1024;
};

struct BackendStats {
    uint64_t received = 0;
    uint64_t dropped = 0;
    uint64_t interface_dropped = 0;
    uint64_t captured = 0;
    uint64_t sent = 0;
    uint64_t network_dropped = 0;
};
struct NpcapStatNative {
    uint32_t received;
    uint32_t dropped;
    uint32_t interface_dropped;
    uint32_t captured;
    uint32_t sent;
    uint32_t network_dropped;
};
static_assert(sizeof(NpcapStatNative) == 24);
static_assert(offsetof(NpcapStatNative, received) == 0);
static_assert(offsetof(NpcapStatNative, dropped) == 4);
static_assert(offsetof(NpcapStatNative, interface_dropped) == 8);
static_assert(offsetof(NpcapStatNative, captured) == 12);
static_assert(offsetof(NpcapStatNative, sent) == 16);
static_assert(offsetof(NpcapStatNative, network_dropped) == 20);
[[nodiscard]] BackendStats npcap_stats_from_native(const NpcapStatNative &stats) noexcept;
struct BackendDiagnostic {
    BackendError error = BackendError::none;
    std::string backend;
    std::string runtime_version;
    std::string interface_identity;
    std::string message;
    std::string help_url;
    bool automatic_action = false;
    uint32_t native_error = 0;
    uint32_t stable_native_category = 0;
};

enum class WinDivertFailureCategory : uint32_t {
    none, missing_driver, permission_denied, invalid_signature, driver_blocked,
    bfe_disabled, incompatible_driver, receive_failed, other,
};
[[nodiscard]] WinDivertFailureCategory categorize_windivert_error(uint32_t error,
                                                                  bool receiving) noexcept;

struct LivePacket {
    const uint8_t *bytes = nullptr;
    uint32_t captured_length = 0;
    uint32_t original_length = 0;
    uint64_t timestamp_ns = 0;
    uint32_t link_type = dlt_raw;
    bool outbound = false;
};

struct WinDivertPacketMetadata {
    int64_t qpc_timestamp = 0;
    bool outbound = false;
};
using WinDivertBoundaryParser = bool (*)(void *, const uint8_t *, uint32_t, const uint8_t **,
                                         uint32_t *);
bool split_windivert_batch(std::span<const uint8_t> packed_packets,
                           std::span<const WinDivertPacketMetadata> metadata, int64_t qpc_frequency,
                           WinDivertBoundaryParser parser, void *parser_context,
                           std::span<LivePacket> output) noexcept;

using CaptureSink = std::function<void(const CaptureRecord &)>;

inline constexpr uint32_t windivert_layer_network = 0;
inline constexpr uint64_t windivert_flag_sniff = 1;
inline constexpr uint64_t windivert_flag_recv_only = 4;
inline constexpr uint32_t windivert_param_queue_length = 0;
inline constexpr uint32_t windivert_param_queue_time = 1;
inline constexpr uint32_t windivert_param_queue_size = 2;

struct WinDivertApi {
    void *context = nullptr;
    ApiResult (*identity)(void *) = nullptr;
    bool (*resolve)(void *, const char *) = nullptr;
    bool (*compile_filter)(void *, const char *) = nullptr;
    void *(*open)(void *, const char *, uint32_t, uint64_t) = nullptr;
    bool (*set_param)(void *, void *, uint32_t, uint64_t) = nullptr;
    ApiResult (*receive_batch)(void *, void *, LivePacket *, size_t, size_t *) = nullptr;
    bool (*stats)(void *, void *, BackendStats *) = nullptr;
    bool (*cancel)(void *, void *, uint32_t) = nullptr;
    void (*close)(void *, void *) = nullptr;
    uint32_t (*last_error)(void *) = nullptr;
    const char *(*runtime_version)(void *) = nullptr;
};

struct NpcapApi {
    void *context = nullptr;
    const char *(*identity)(void *) = nullptr;
    bool (*resolve)(void *, const char *) = nullptr;
    std::vector<std::string> (*enumerate)(void *) = nullptr;
    void *(*create)(void *, const char *) = nullptr;
    int (*set_immediate)(void *, void *, int) = nullptr;
    int (*set_kernel_buffer)(void *, void *, int) = nullptr;
    int (*set_user_buffer)(void *, void *, int) = nullptr;
    int (*activate)(void *, void *) = nullptr;
    int (*compile_apply)(void *, void *, const char *) = nullptr;
    uint32_t (*link_type)(void *, void *) = nullptr;
    void *(*get_event)(void *, void *) = nullptr;
    ApiResult (*receive)(void *, void *, LivePacket *) = nullptr;
    bool (*stats)(void *, void *, BackendStats *) = nullptr;
    void (*break_loop)(void *, void *) = nullptr;
    void (*close)(void *, void *) = nullptr;
    int (*last_error_code)(void *) = nullptr;
    const char *(*last_error_message)(void *) = nullptr;
};

class CaptureBackend {
  public:
    virtual ~CaptureBackend() = default;
    virtual BackendError start(const BackendConfig &, CaptureSink) = 0;
    virtual BackendError poll() = 0;
    virtual void request_stop() noexcept = 0;
    virtual void stop() noexcept = 0;
    [[nodiscard]] virtual BackendStats stats() const noexcept = 0;
    [[nodiscard]] virtual const BackendDiagnostic &diagnostic() const noexcept = 0;
};

inline CaptureDirection infer_direction(const CaptureRecord &record, uint16_t server_port) {
    const auto normalized = PacketNormalizer::normalize(record);
    if (!normalized.segment)
        return CaptureDirection::unknown;
    if (normalized.segment->flow.source_port == server_port)
        return CaptureDirection::inbound;
    if (normalized.segment->flow.destination_port == server_port)
        return CaptureDirection::outbound;
    return CaptureDirection::unknown;
}

std::unique_ptr<CaptureBackend> make_windivert_backend(const WinDivertApi *api);
std::unique_ptr<CaptureBackend> make_npcap_backend(const NpcapApi *api);
std::unique_ptr<CaptureBackend> make_system_windivert_backend();
std::unique_ptr<CaptureBackend> make_system_npcap_backend();
using BackendFactoryForTesting = std::unique_ptr<CaptureBackend> (*)(uint32_t source_kind);
void set_backend_factory_for_testing(BackendFactoryForTesting factory) noexcept;
[[nodiscard]] bool probe_windivert_runtime() noexcept;
[[nodiscard]] bool probe_npcap_runtime() noexcept;

class BoundedCaptureQueue {
  public:
    explicit BoundedCaptureQueue(size_t capacity) : capacity_(capacity) {}
    bool push(const CaptureRecord &record) {
        std::scoped_lock lock(mutex_);
        if (stopped_ || records_.size() >= capacity_) {
            ++dropped_;
            return false;
        }
        records_.push_back(record);
        high_water_ = (std::max)(high_water_, records_.size());
        return true;
    }
    std::optional<CaptureRecord> pop() {
        std::scoped_lock lock(mutex_);
        if (records_.empty())
            return std::nullopt;
        auto value = std::move(records_.front());
        records_.erase(records_.begin());
        return value;
    }
    void stop() noexcept {
        std::scoped_lock lock(mutex_);
        stopped_ = true;
    }
    [[nodiscard]] uint64_t dropped() const noexcept {
        std::scoped_lock lock(mutex_);
        return dropped_;
    }
    [[nodiscard]] uint64_t high_water() const noexcept {
        std::scoped_lock lock(mutex_);
        return high_water_;
    }

  private:
    size_t capacity_;
    mutable std::mutex mutex_;
    std::vector<CaptureRecord> records_;
    uint64_t dropped_ = 0;
    size_t high_water_ = 0;
    bool stopped_ = false;
};

} // namespace namter
