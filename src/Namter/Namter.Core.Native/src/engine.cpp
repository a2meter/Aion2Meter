#include "namter/core.h"

#include "live_backend.hpp"
#include "protocol_snapshot.hpp"

#include <algorithm>
#include <atomic>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

struct nm_core_handle {
    nm_core_config_v1 config{};
    nm_callbacks_v1 callbacks{};
    std::mutex mutex;
    namter::ProtocolSnapshotStore protocol_snapshot;
    bool started = false;
    uint64_t start_count = 0;
    uint64_t stop_count = 0;
    uint64_t emitted_event_count = 0;
    std::unique_ptr<namter::CaptureBackend> backend;
    std::unique_ptr<namter::BoundedCaptureQueue> capture_queue;
    std::thread capture_thread;
    std::atomic_bool capture_stop{false};
    uint64_t captured_packet_count = 0;
    uint64_t dropped_capture_count = 0;
    uint64_t invalid_packet_count = 0;
};

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
    handle->callbacks.diagnostic_callback(handle->callbacks.user, &diagnostic);
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
        if (handle->started) {
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
        {
            std::scoped_lock lock(handle->mutex);
            if (handle->started) {
                return NM_STATUS_INVALID_STATE;
            }
            handle->started = true;
            handle->capture_stop = false;
            handle->capture_queue =
                std::make_unique<namter::BoundedCaptureQueue>(handle->config.native_queue_capacity);
        }

        const auto rollback = [&]() noexcept {
            std::scoped_lock lock(handle->mutex);
            handle->started = false;
            handle->capture_queue.reset();
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
                backend->start(config, [handle](const namter::CaptureRecord &record) {
                    if (!handle->capture_queue->push(record)) {
                        {
                            std::scoped_lock lock(handle->mutex);
                            ++handle->dropped_capture_count;
                        }
                        emit_diagnostic(handle, NM_DIAGNOSTIC_CAPTURE_QUEUE_OVERFLOW,
                                        "native capture queue overflowed; capture is incomplete");
                    }
                });
            if (result != namter::BackendError::none) {
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

        nm_event_callback_v1 event_callback = nullptr;
        void *user = nullptr;
        {
            std::scoped_lock lock(handle->mutex);
            handle->backend = std::move(backend);
            ++handle->start_count;
            ++handle->emitted_event_count;
            event_callback = handle->callbacks.event_callback;
            user = handle->callbacks.user;
        }

        const nm_event_v1 event{
            .abi_version = abi_version,
            .struct_size = sizeof(nm_event_v1),
            .kind = NM_EVENT_SOURCE_STARTED,
            .payload = source->source_data,
            .payload_size = source->source_data_size,
        };
        event_callback(user, &event);
        if (handle->backend) {
            handle->capture_thread = std::thread([handle] {
                while (!handle->capture_stop.load(std::memory_order_acquire)) {
                    const auto result = handle->backend->poll();
                    while (auto record = handle->capture_queue->pop()) {
                        const auto normalized = namter::PacketNormalizer::normalize(*record);
                        std::scoped_lock lock(handle->mutex);
                        ++handle->captured_packet_count;
                        if (normalized.error != namter::CaptureError::none) {
                            ++handle->invalid_packet_count;
                        }
                    }
                    if (result == namter::BackendError::cancelled) {
                        break;
                    }
                    if (result != namter::BackendError::none) {
                        emit_diagnostic(handle, NM_DIAGNOSTIC_CAPTURE_BACKEND_FAILED,
                                        "live capture backend receive failed");
                        break;
                    }
                }
            });
        }
        return NM_STATUS_OK;
    } catch (...) {
        std::scoped_lock lock(handle->mutex);
        handle->started = false;
        handle->capture_queue.reset();
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
            std::scoped_lock lock(handle->mutex);
            if (!handle->started) {
                return NM_STATUS_OK;
            }
            handle->started = false;
            handle->capture_stop = true;
            ++handle->stop_count;
            backend = handle->backend.get();
        }
        if (backend != nullptr) {
            backend->request_stop();
        }
        if (handle->capture_thread.joinable()) {
            handle->capture_thread.join();
        }
        if (backend != nullptr) {
            backend->stop();
        }
        {
            std::scoped_lock lock(handle->mutex);
            handle->backend.reset();
            handle->capture_queue.reset();
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
        return NM_STATUS_OK;
    } catch (...) {
        return NM_STATUS_INTERNAL_ERROR;
    }
}

void NM_CALL nm_core_destroy(nm_core_handle *handle) noexcept {
    try {
        if (handle != nullptr) {
            nm_core_stop(handle);
        }
        delete handle;
    } catch (...) {
        // A void C ABI destructor has no status channel. Never unwind across it.
    }
}
