#include "namter/core.h"

#include "live_backend.hpp"
#include "capture_pipeline.hpp"
#include "protocol_snapshot.hpp"

#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <memory>
#include <mutex>
#include <string>
#include <sstream>
#include <limits>
#include <exception>
#include <thread>
#include <vector>

struct nm_core_handle {
    enum class Lifecycle { idle, starting, running, stopping };
    nm_core_config_v1 config{};
    nm_callbacks_v1 callbacks{};
    std::mutex mutex;
    std::condition_variable lifecycle_cv;
    namter::ProtocolSnapshotStore protocol_snapshot;
    Lifecycle lifecycle = Lifecycle::idle;
    bool start_cancelled = false;
    std::thread::id starting_thread{};
    uint64_t start_count = 0;
    uint64_t stop_count = 0;
    uint64_t emitted_event_count = 0;
    std::unique_ptr<namter::CaptureBackend> backend;
    std::shared_ptr<namter::BoundedCaptureQueue> capture_queue;
    std::thread capture_thread;
    std::atomic_bool capture_stop{false};
    uint64_t captured_packet_count = 0;
    uint64_t dropped_capture_count = 0;
    uint64_t invalid_packet_count = 0;
    uint64_t backend_received = 0;
    uint64_t backend_dropped = 0;
    uint64_t backend_interface_dropped = 0;
    uint64_t queue_high_water = 0;
    uint64_t tcp_overlaps = 0;
    uint64_t tcp_duplicate_bytes_removed = 0;
    uint64_t tcp_unresolved_byte_gaps = 0;
    bool incomplete = false;
    std::atomic_uint32_t lifetime_refs{1};
    std::atomic_bool destroy_requested{false};
    bool worker_self_finalize = false;
};

namespace {
void retain_handle(nm_core_handle *handle) noexcept { handle->lifetime_refs.fetch_add(1); }
void release_handle(nm_core_handle *handle) noexcept {
    if (handle->lifetime_refs.fetch_sub(1) == 1) delete handle;
}
struct HandleLease {
    nm_core_handle *handle;
    explicit HandleLease(nm_core_handle *value) : handle(value) { retain_handle(handle); }
    ~HandleLease() { release_handle(handle); }
};
void finish_worker_lifetime(nm_core_handle *handle) noexcept;
}

namespace {

constexpr uint32_t abi_version = 1;
std::atomic<namter::BackendFactoryForTesting> test_backend_factory{nullptr};

template <typename T> bool has_current_layout(const T *value) noexcept {
    return value != nullptr && value->abi_version == abi_version && value->struct_size >= sizeof(T);
}

bool is_valid_source_kind(uint32_t kind) noexcept {
    return kind == NM_SOURCE_WINDIVERT || kind == NM_SOURCE_NPCAP || kind == NM_SOURCE_PCAP;
}

bool is_within(uint32_t value, uint32_t minimum, uint32_t maximum) noexcept {
    return value >= minimum && value <= maximum;
}

bool has_valid_bounds(const nm_core_config_v1 &config) noexcept {
    return is_within(config.native_queue_capacity, NM_CORE_NATIVE_QUEUE_CAPACITY_MIN,
                     NM_CORE_NATIVE_QUEUE_CAPACITY_MAX) &&
           is_within(config.max_live_flows, NM_CORE_MAX_LIVE_FLOWS_MIN,
                     NM_CORE_MAX_LIVE_FLOWS_MAX) &&
           is_within(config.max_ooo_bytes_per_flow, NM_CORE_MAX_OOO_BYTES_PER_FLOW_MIN,
                     NM_CORE_MAX_OOO_BYTES_PER_FLOW_MAX) &&
           is_within(config.max_frame_bytes, NM_CORE_MAX_FRAME_BYTES_MIN,
                     NM_CORE_MAX_FRAME_BYTES_MAX) &&
           is_within(config.max_decompressed_bytes, NM_CORE_MAX_DECOMPRESSED_BYTES_MIN,
                     NM_CORE_MAX_DECOMPRESSED_BYTES_MAX);
}

void emit_diagnostic(nm_core_handle *handle, uint32_t code, const char *message) noexcept {
    const nm_diagnostic_v1 diagnostic{
        .abi_version = abi_version,
        .struct_size = sizeof(nm_diagnostic_v1),
        .code = code,
        .message = reinterpret_cast<const uint8_t *>(message),
        .message_size = std::char_traits<char>::length(message),
    };
    try {
        handle->callbacks.diagnostic_callback(handle->callbacks.user, &diagnostic);
    } catch (...) {
        // Foreign callbacks must never escape a noexcept/native worker boundary.
    }
}

void emit_source_completed(nm_core_handle *handle) noexcept {
    const nm_event_v1 event{.abi_version = abi_version, .struct_size = sizeof(nm_event_v1),
                            .kind = NM_EVENT_SOURCE_COMPLETED};
    {
        std::scoped_lock lock(handle->mutex);
        ++handle->emitted_event_count;
    }
    try {
        handle->callbacks.event_callback(handle->callbacks.user, &event);
    } catch (...) {
        std::scoped_lock lock(handle->mutex);
        handle->incomplete = true;
    }
}

void emit_backend_diagnostic(nm_core_handle *handle, uint32_t kind,
                             const namter::BackendDiagnostic& value,
                             const namter::BackendStats& stats = {},
                             uint32_t code = NM_DIAGNOSTIC_CAPTURE_BACKEND_FAILED) noexcept {
    const nm_diagnostic_v1 diagnostic{
        .abi_version = abi_version, .struct_size = sizeof(nm_diagnostic_v1),
        .code = code,
        .message = reinterpret_cast<const uint8_t*>(value.message.data()),
        .message_size = value.message.size(), .backend_kind = kind,
        .stable_error = value.stable_native_category != 0
            ? value.stable_native_category : static_cast<uint32_t>(value.error),
        .native_error = value.native_error, .incomplete = 1,
        .automatic_action = value.automatic_action ? uint8_t{1} : uint8_t{0},
        .received = stats.received, .dropped = stats.dropped,
        .interface_dropped = stats.interface_dropped,
        .queue_high_water = handle->capture_queue ? handle->capture_queue->high_water() : 0,
        .backend_name = reinterpret_cast<const uint8_t*>(value.backend.data()),
        .backend_name_size = value.backend.size(),
        .runtime_version = reinterpret_cast<const uint8_t*>(value.runtime_version.data()),
        .runtime_version_size = value.runtime_version.size(),
        .interface_identity = reinterpret_cast<const uint8_t*>(value.interface_identity.data()),
        .interface_identity_size = value.interface_identity.size(),
        .help_url = reinterpret_cast<const uint8_t*>(value.help_url.data()),
        .help_url_size = value.help_url.size(),
    };
    try {
        handle->callbacks.diagnostic_callback(handle->callbacks.user, &diagnostic);
    } catch (...) {
        // Foreign callbacks must never escape a noexcept/native worker boundary.
    }
}

void finish_worker_lifetime(nm_core_handle *handle) noexcept {
    bool self_finalize=false;
    {
        std::scoped_lock lock(handle->mutex);
        self_finalize=handle->worker_self_finalize;
    }
    if(self_finalize){
        auto *backend=handle->backend.get();
        if(backend){
            const auto stats=backend->stats();
            {std::scoped_lock lock(handle->mutex);handle->backend_received=stats.received;handle->backend_dropped=stats.dropped;handle->backend_interface_dropped=stats.interface_dropped;if(handle->capture_queue)handle->queue_high_water=handle->capture_queue->high_water();}
            backend->stop();
        }
        {
            std::unique_lock lock(handle->mutex);
            if(handle->capture_thread.joinable()&&handle->capture_thread.get_id()==std::this_thread::get_id())handle->capture_thread.detach();
            handle->backend.reset();handle->capture_queue.reset();handle->lifecycle=nm_core_handle::Lifecycle::idle;handle->worker_self_finalize=false;lock.unlock();handle->lifecycle_cv.notify_all();
        }
    }
    release_handle(handle);
}

} // namespace

nm_status NM_CALL nm_core_create(const nm_core_config_v1 *config, const nm_callbacks_v1 *callbacks,
                                 nm_core_handle **out_handle) noexcept {
    if (out_handle == nullptr) {
        return NM_STATUS_INVALID_ARGUMENT;
    }
    *out_handle = nullptr;

    if (config == nullptr || callbacks == nullptr) {
        return NM_STATUS_INVALID_ARGUMENT;
    }
    if (!has_current_layout(config) || !has_current_layout(callbacks)) {
        return NM_STATUS_ABI_MISMATCH;
    }
    if (callbacks->event_callback == nullptr || callbacks->diagnostic_callback == nullptr) {
        return NM_STATUS_INVALID_ARGUMENT;
    }
    if (!has_valid_bounds(*config)) {
        return NM_STATUS_INVALID_ARGUMENT;
    }

    try {
        auto handle = std::make_unique<nm_core_handle>();
        handle->config = *config;
        handle->callbacks = *callbacks;
        *out_handle = handle.release();
        return NM_STATUS_OK;
    } catch (...) {
        return NM_STATUS_INTERNAL_ERROR;
    }
}

nm_status NM_CALL nm_core_set_protocol_snapshot(nm_core_handle *handle, const uint8_t *data,
                                                size_t size) noexcept {
    if (handle == nullptr || data == nullptr || size == 0 || size > NM_CORE_PROTOCOL_SNAPSHOT_MAX) {
        return NM_STATUS_INVALID_ARGUMENT;
    }
    try {
        std::scoped_lock lock(handle->mutex);
        if (handle->lifecycle != nm_core_handle::Lifecycle::idle) {
            return NM_STATUS_INVALID_STATE;
        }
        return handle->protocol_snapshot.replace({data, size}) ? NM_STATUS_OK
                                                               : NM_STATUS_INVALID_ARGUMENT;
    } catch (...) {
        return NM_STATUS_INTERNAL_ERROR;
    }
}

nm_status NM_CALL nm_core_start(nm_core_handle *handle,
                                const nm_source_config_v1 *source) noexcept {
    if (handle == nullptr || source == nullptr) {
        return NM_STATUS_INVALID_ARGUMENT;
    }
    HandleLease lifetime(handle);
    if(handle->destroy_requested.load()) return NM_STATUS_INVALID_STATE;
    if (!has_current_layout(source)) {
        return NM_STATUS_ABI_MISMATCH;
    }
    if (!is_valid_source_kind(source->kind) ||
        (source->source_data == nullptr && source->source_data_size != 0) ||
        (source->kind != NM_SOURCE_PCAP &&
         (source->source_data_size > NM_CORE_ADAPTER_NAME_MAX ||
          (source->source_data != nullptr &&
           std::find(source->source_data, source->source_data + source->source_data_size, 0) !=
               source->source_data + source->source_data_size)))) {
        return NM_STATUS_INVALID_ARGUMENT;
    }

    try {
        auto queue = std::make_shared<namter::BoundedCaptureQueue>(
            handle->config.native_queue_capacity);
        {
            std::unique_lock lock(handle->mutex);
            if (handle->lifecycle != nm_core_handle::Lifecycle::idle) {
                return NM_STATUS_INVALID_STATE;
            }
            handle->lifecycle = nm_core_handle::Lifecycle::starting;
            handle->start_cancelled = false;
            handle->starting_thread = std::this_thread::get_id();
            handle->capture_stop = false;
        }

        const auto rollback = [&]() noexcept {
            std::unique_lock lock(handle->mutex);
            handle->lifecycle = nm_core_handle::Lifecycle::idle;
            handle->start_cancelled = false;
            handle->starting_thread = {};
            handle->backend.reset();
            handle->capture_queue.reset();
            lock.unlock();
            handle->lifecycle_cv.notify_all();
        };

        std::unique_ptr<namter::CaptureBackend> backend;
        if (const auto factory = test_backend_factory.load(std::memory_order_acquire)) {
            backend = factory(source->kind);
        } else if (source->kind == NM_SOURCE_NPCAP) {
            backend = namter::make_system_npcap_backend();
        } else if (source->kind == NM_SOURCE_WINDIVERT) {
            backend = namter::make_system_windivert_backend();
        }
        if (backend) {
            namter::BackendConfig config{.port = NM_CORE_DEFAULT_GAME_PORT};
            if (source->source_data_size != 0) {
                config.adapter.assign(reinterpret_cast<const char *>(source->source_data),
                                      source->source_data_size);
            }
            const auto result =
                backend->start(config, [handle, queue](const namter::CaptureRecord &record) {
                    if (!queue->push(record)) {
                        {
                            std::scoped_lock lock(handle->mutex);
                            ++handle->dropped_capture_count;
                            handle->incomplete = true;
                        }
                        emit_diagnostic(handle, NM_DIAGNOSTIC_CAPTURE_QUEUE_OVERFLOW,
                                        "native capture queue overflowed; capture is incomplete");
                    }
                });
            if (result != namter::BackendError::none) {
                emit_backend_diagnostic(handle, source->kind, backend->diagnostic(),
                                        backend->stats());
                backend->request_stop();
                backend->stop();
                rollback();
                if (result == namter::BackendError::npcap_not_installed) {
                    return NM_STATUS_NPCAP_NOT_INSTALLED;
                }
                if (result == namter::BackendError::library_missing) {
                    return NM_STATUS_BACKEND_UNAVAILABLE;
                }
                return NM_STATUS_BACKEND_ERROR;
            }
        }

        {
            std::unique_lock lock(handle->mutex);
            if (handle->start_cancelled ||
                handle->lifecycle != nm_core_handle::Lifecycle::starting) {
                lock.unlock();
                if (backend) { backend->request_stop(); backend->stop(); }
                rollback();
                return NM_STATUS_INVALID_STATE;
            }
        }

        const nm_event_v1 event{
            .abi_version = abi_version,
            .struct_size = sizeof(nm_event_v1),
            .kind = NM_EVENT_SOURCE_STARTED,
            .payload = source->source_data,
            .payload_size = source->source_data_size,
        };
        try {
            handle->callbacks.event_callback(handle->callbacks.user, &event);
        } catch (...) {
            if (backend) { backend->request_stop(); backend->stop(); }
            {
                std::scoped_lock lock(handle->mutex);
                handle->incomplete = true;
            }
            emit_diagnostic(handle, NM_DIAGNOSTIC_INCOMPLETE_STREAM,
                            "source-started callback failed");
            rollback();
            return NM_STATUS_INTERNAL_ERROR;
        }
        const std::vector<uint8_t> snapshot = handle->protocol_snapshot.bytes();
        std::vector<uint8_t> source_bytes;
        if (source->source_data_size != 0)
            source_bytes.assign(source->source_data,
                                source->source_data + source->source_data_size);
        {
        std::unique_lock lock(handle->mutex);
        if (handle->start_cancelled ||
            handle->lifecycle != nm_core_handle::Lifecycle::starting) {
            lock.unlock();
            if (backend) { backend->request_stop(); backend->stop(); }
            rollback();
            return NM_STATUS_INVALID_STATE;
        }
        handle->backend = std::move(backend);
        handle->capture_queue = queue;
        retain_handle(handle);
        try {
        handle->capture_thread = std::thread([handle, snapshot, source_bytes, kind = source->kind] {
            struct WorkerRelease{nm_core_handle*h;~WorkerRelease(){finish_worker_lifetime(h);}} worker_release{handle};
            try {
                namter::CapturePipeline pipeline(
                    {.flow = {.max_live_flows = handle->config.max_live_flows,
                              .max_out_of_order_bytes_per_flow =
                                  handle->config.max_ooo_bytes_per_flow},
                     .frame = {.max_frame_bytes = handle->config.max_frame_bytes,
                               .max_decompressed_bytes =
                                   handle->config.max_decompressed_bytes}},
                    snapshot,
                    [handle](const nm_event_v1& event) {
                        {
                            std::scoped_lock lock(handle->mutex);
                            ++handle->emitted_event_count;
                        }
                        handle->callbacks.event_callback(handle->callbacks.user, &event);
                    },
                    [handle](uint32_t code, const char* message) {
                        {
                            std::scoped_lock lock(handle->mutex);
                            handle->incomplete = true;
                        }
                        emit_diagnostic(handle, code, message);
                    });

                if (kind == NM_SOURCE_PCAP) {
                    const std::string owned(reinterpret_cast<const char*>(source_bytes.data()),
                                            source_bytes.size());
                    std::istringstream input(owned, std::ios::binary);
                    namter::PcapReader reader(input);
                    namter::CaptureRecord record;
                    uint64_t last_timestamp = 0;
                    while (!handle->capture_stop.load(std::memory_order_acquire) &&
                           reader.read_next(record)) {
                        last_timestamp = record.timestamp_ns;
                        const auto error = pipeline.ingest(record);
                        std::scoped_lock lock(handle->mutex);
                        ++handle->captured_packet_count;
                        if (error != namter::CaptureError::none) ++handle->invalid_packet_count;
                    }
                    if (reader.error() != namter::CaptureError::none) {
                        const namter::BackendDiagnostic diagnostic{
                            .error = namter::BackendError::receive_failed,
                            .backend = "pcap",
                            .runtime_version = "libpcap-v2.4",
                            .interface_identity = "memory",
                            .message = "PCAP input is invalid or truncated",
                            .native_error = static_cast<uint32_t>(reader.error()),
                            .stable_native_category = static_cast<uint32_t>(reader.error()),
                        };
                        emit_backend_diagnostic(handle, NM_SOURCE_PCAP, diagnostic, {},
                                                NM_DIAGNOSTIC_INCOMPLETE_STREAM);
                        std::scoped_lock lock(handle->mutex);
                        ++handle->invalid_packet_count;
                        handle->incomplete = true;
                    }
                    pipeline.flush(last_timestamp == 0
                                       ? std::numeric_limits<uint64_t>::max()
                                       : last_timestamp + 120'000'000'001ull);
                    {
                        const auto& flow = pipeline.flow_diagnostics();
                        std::scoped_lock lock(handle->mutex);
                        handle->tcp_overlaps += flow.overlaps;
                        handle->tcp_duplicate_bytes_removed += flow.duplicate_bytes_removed;
                        handle->tcp_unresolved_byte_gaps += flow.unresolved_byte_gaps;
                    }
                    emit_source_completed(handle);
                    return;
                }

                while (!handle->capture_stop.load(std::memory_order_acquire)) {
                    const auto result = handle->backend->poll();
                    while (auto record = handle->capture_queue->pop()) {
                        const auto error = pipeline.ingest(*record);
                        std::scoped_lock lock(handle->mutex);
                        ++handle->captured_packet_count;
                        if (error != namter::CaptureError::none) {
                            ++handle->invalid_packet_count;
                        }
                    }
                    if (result == namter::BackendError::cancelled) {
                        break;
                    }
                    if (result != namter::BackendError::none) {
                        emit_backend_diagnostic(handle, kind, handle->backend->diagnostic(),
                                                handle->backend->stats());
                        std::scoped_lock lock(handle->mutex);
                        handle->incomplete = true;
                        break;
                    }
                }
                pipeline.flush(std::numeric_limits<uint64_t>::max());
                {
                    const auto& flow = pipeline.flow_diagnostics();
                    std::scoped_lock lock(handle->mutex);
                    handle->tcp_overlaps += flow.overlaps;
                    handle->tcp_duplicate_bytes_removed += flow.duplicate_bytes_removed;
                    handle->tcp_unresolved_byte_gaps += flow.unresolved_byte_gaps;
                }
                emit_source_completed(handle);
            } catch (const std::exception&) {
                emit_diagnostic(handle, NM_DIAGNOSTIC_CAPTURE_BACKEND_FAILED,
                                "capture worker failed with an internal exception");
                {
                    std::scoped_lock lock(handle->mutex);
                    ++handle->invalid_packet_count;
                    handle->incomplete = true;
                }
                emit_source_completed(handle);
            } catch (...) {
                emit_diagnostic(handle, NM_DIAGNOSTIC_CAPTURE_BACKEND_FAILED,
                                "capture worker failed with an unknown exception");
                {
                    std::scoped_lock lock(handle->mutex);
                    ++handle->invalid_packet_count;
                    handle->incomplete = true;
                }
                emit_source_completed(handle);
            }
        });
        } catch (...) {
            release_handle(handle);
            throw;
        }
        handle->lifecycle = nm_core_handle::Lifecycle::running;
        handle->starting_thread = {};
        ++handle->start_count;
        ++handle->emitted_event_count;
        }
        return NM_STATUS_OK;
    } catch (...) {
        std::unique_lock lock(handle->mutex);
        handle->capture_stop = true;
        if (handle->capture_thread.joinable()) {
            lock.unlock();
            handle->capture_thread.join();
            lock.lock();
        }
        if (handle->backend) handle->backend->stop();
        handle->backend.reset();
        handle->capture_queue.reset();
        handle->lifecycle = nm_core_handle::Lifecycle::idle;
        handle->start_cancelled = false;
        handle->starting_thread = {};
        lock.unlock();
        handle->lifecycle_cv.notify_all();
        return NM_STATUS_INTERNAL_ERROR;
    }
}

void namter::set_backend_factory_for_testing(BackendFactoryForTesting factory) noexcept {
    test_backend_factory.store(factory, std::memory_order_release);
}

nm_status NM_CALL nm_core_stop(nm_core_handle *handle) noexcept {
    if (handle == nullptr) {
        return NM_STATUS_INVALID_ARGUMENT;
    }

    try {
        namter::CaptureBackend *backend = nullptr;
        {
            std::unique_lock lock(handle->mutex);
            if (handle->lifecycle == nm_core_handle::Lifecycle::idle) {
                return NM_STATUS_OK;
            }
            if (handle->lifecycle == nm_core_handle::Lifecycle::starting) {
                handle->start_cancelled = true;
                handle->lifecycle = nm_core_handle::Lifecycle::stopping;
                ++handle->stop_count;
                if (handle->starting_thread == std::this_thread::get_id())
                    return NM_STATUS_OK;
                handle->lifecycle_cv.wait(lock, [handle] {
                    return handle->lifecycle == nm_core_handle::Lifecycle::idle;
                });
                return NM_STATUS_OK;
            }
            if (handle->lifecycle == nm_core_handle::Lifecycle::stopping) {
                if(handle->capture_thread.joinable()&&
                   handle->capture_thread.get_id()==std::this_thread::get_id()) return NM_STATUS_OK;
                handle->lifecycle_cv.wait(lock, [handle] {
                    return handle->lifecycle == nm_core_handle::Lifecycle::idle;
                });
                return NM_STATUS_OK;
            }
            handle->lifecycle = nm_core_handle::Lifecycle::stopping;
            handle->capture_stop = true;
            ++handle->stop_count;
            backend = handle->backend.get();
        }
        if (backend != nullptr) {
            backend->request_stop();
        }
        {
            std::scoped_lock lock(handle->mutex);
            if(handle->capture_thread.joinable()&&
               handle->capture_thread.get_id()==std::this_thread::get_id()){
                handle->worker_self_finalize=true;
                return NM_STATUS_OK;
            }
        }
        if (handle->capture_thread.joinable()) {
            handle->capture_thread.join();
        }
        if (backend != nullptr) {
            const auto stats = backend->stats();
            std::scoped_lock lock(handle->mutex);
            handle->backend_received = stats.received;
            handle->backend_dropped = stats.dropped;
            handle->backend_interface_dropped = stats.interface_dropped;
            if (handle->capture_queue)
                handle->queue_high_water = handle->capture_queue->high_water();
        }
        if (backend != nullptr) {
            backend->stop();
        }
        {
            std::unique_lock lock(handle->mutex);
            handle->backend.reset();
            handle->capture_queue.reset();
            handle->lifecycle = nm_core_handle::Lifecycle::idle;
            handle->start_cancelled = false;
            handle->starting_thread = {};
            lock.unlock();
            handle->lifecycle_cv.notify_all();
        }
        return NM_STATUS_OK;
    } catch (...) {
        return NM_STATUS_INTERNAL_ERROR;
    }
}

nm_status NM_CALL nm_core_get_diagnostics(nm_core_handle *handle,
                                          nm_diagnostics_v1 *diagnostics) noexcept {
    if (handle == nullptr || diagnostics == nullptr) {
        return NM_STATUS_INVALID_ARGUMENT;
    }
    if (!has_current_layout(diagnostics)) {
        return NM_STATUS_ABI_MISMATCH;
    }

    try {
        std::scoped_lock lock(handle->mutex);
        diagnostics->start_count = handle->start_count;
        diagnostics->stop_count = handle->stop_count;
        diagnostics->emitted_event_count = handle->emitted_event_count;
        diagnostics->captured_packet_count = handle->captured_packet_count;
        diagnostics->dropped_capture_count = handle->dropped_capture_count;
        diagnostics->invalid_packet_count = handle->invalid_packet_count;
        diagnostics->backend_received = handle->backend_received;
        diagnostics->backend_dropped = handle->backend_dropped;
        diagnostics->backend_interface_dropped = handle->backend_interface_dropped;
        diagnostics->queue_high_water = handle->capture_queue
            ? handle->capture_queue->high_water() : handle->queue_high_water;
        diagnostics->tcp_overlaps = handle->tcp_overlaps;
        diagnostics->tcp_duplicate_bytes_removed = handle->tcp_duplicate_bytes_removed;
        diagnostics->tcp_unresolved_byte_gaps = handle->tcp_unresolved_byte_gaps;
        diagnostics->incomplete = handle->incomplete ? 1 : 0;
        return NM_STATUS_OK;
    } catch (...) {
        return NM_STATUS_INTERNAL_ERROR;
    }
}

void NM_CALL nm_core_destroy(nm_core_handle *handle) noexcept {
    try {
        if (handle == nullptr || handle->destroy_requested.exchange(true)) return;
        nm_core_stop(handle);
        release_handle(handle);
    } catch (...) {
        // A void C ABI destructor has no status channel. Never unwind across it.
    }
}
