#include "dynamic_library.hpp"
#include "live_backend.hpp"

#include <array>
#include <cstdio>
#include <cstring>

namespace namter {
bool split_windivert_batch(std::span<const uint8_t> packed_packets,
                           std::span<const WinDivertPacketMetadata> metadata, int64_t qpc_frequency,
                           WinDivertBoundaryParser parser, void *parser_context,
                           std::span<LivePacket> output) noexcept {
    if (packed_packets.empty() || metadata.empty() || metadata.size() > output.size() ||
        qpc_frequency <= 0 || parser == nullptr)
        return false;
    const uint8_t *current = packed_packets.data();
    uint32_t remaining = static_cast<uint32_t>(packed_packets.size());
    for (size_t index = 0; index < metadata.size(); ++index) {
        const uint8_t *next = nullptr;
        uint32_t next_length = 0;
        if (!parser(parser_context, current, remaining, &next, &next_length) ||
            next_length > remaining)
            return false;
        const uint32_t packet_length = remaining - next_length;
        const auto qpc = metadata[index].qpc_timestamp;
        const auto timestamp_ns =
            qpc <= 0 ? 0
                     : static_cast<uint64_t>(static_cast<long double>(qpc) * 1'000'000'000.0L /
                                             static_cast<long double>(qpc_frequency));
        output[index] = {.bytes = current,
                         .captured_length = packet_length,
                         .original_length = packet_length,
                         .timestamp_ns = timestamp_ns,
                         .link_type = dlt_raw,
                         .outbound = metadata[index].outbound};
        current = next;
        remaining = next_length;
    }
    return remaining == 0;
}

namespace {
constexpr std::array required_symbols{"WinDivertOpen",
                                      "WinDivertRecvEx",
                                      "WinDivertShutdown",
                                      "WinDivertClose",
                                      "WinDivertSetParam",
                                      "WinDivertGetParam",
                                      "WinDivertHelperCompileFilter",
                                      "WinDivertHelperParsePacket"};
struct WinDivertNetworkData {
    uint32_t interface_index;
    uint32_t subinterface_index;
};
union WinDivertLayerData {
    WinDivertNetworkData network;
    std::array<uint64_t, 8> maximum_layout;
};
struct WinDivertAddress {
    int64_t timestamp;
    uint64_t layer : 8;
    uint64_t event : 8;
    uint64_t sniffed : 1;
    uint64_t outbound : 1;
    uint64_t loopback : 1;
    uint64_t impostor : 1;
    uint64_t ipv6 : 1;
    uint64_t ip_checksum : 1;
    uint64_t tcp_checksum : 1;
    uint64_t udp_checksum : 1;
    uint64_t reserved : 40;
    WinDivertLayerData data;
};
static_assert(sizeof(WinDivertAddress) == 80);
static_assert(offsetof(WinDivertAddress, timestamp) == 0);
static_assert(offsetof(WinDivertAddress, data) == 16);

class SystemWinDivert {
  public:
    DynamicLibrary dll{application_library_path(L"WinDivert.dll")};
    WinDivertApi api{};
    using Open = void *(__cdecl *)(const char *, uint32_t, int16_t, uint64_t);
    using Receive = int(__cdecl *)(void *, void *, uint32_t, uint32_t *, uint64_t, void *,
                                   uint32_t *, OVERLAPPED *);
    using Shutdown = int(__cdecl *)(void *, uint32_t);
    using Close = int(__cdecl *)(void *);
    using Set = int(__cdecl *)(void *, uint32_t, uint64_t);
    using Get = int(__cdecl *)(void *, uint32_t, uint64_t *);
    using Compile = int(__cdecl *)(const char *, uint32_t, void *, uint32_t, const char **,
                                   uint32_t *);
    using Parse = int(__cdecl *)(const void *, uint32_t, void **, void **, uint8_t *, void **,
                                 void **, void **, void **, void **, uint32_t *, void **,
                                 uint32_t *);
    Open open{};
    Receive receive{};
    Shutdown shutdown{};
    Close close{};
    Set set{};
    Get get{};
    Compile compile{};
    Parse parse{};
    std::vector<uint8_t> packet_buffer;
    std::vector<WinDivertAddress> addresses;
    OVERLAPPED overlapped{};
    LARGE_INTEGER qpc_frequency{};
    SystemWinDivert() {
        if (!dll)
            return;
        open = dll.symbol<Open>("WinDivertOpen");
        receive = dll.symbol<Receive>("WinDivertRecvEx");
        shutdown = dll.symbol<Shutdown>("WinDivertShutdown");
        close = dll.symbol<Close>("WinDivertClose");
        set = dll.symbol<Set>("WinDivertSetParam");
        get = dll.symbol<Get>("WinDivertGetParam");
        compile = dll.symbol<Compile>("WinDivertHelperCompileFilter");
        parse = dll.symbol<Parse>("WinDivertHelperParsePacket");
        overlapped.hEvent = ::CreateEventW(nullptr, TRUE, FALSE, nullptr);
        ::QueryPerformanceFrequency(&qpc_frequency);
        api.context = this;
        api.identity = [](void *) { return ApiResult::ok; };
        api.resolve = [](void *p, const char *n) {
            return static_cast<SystemWinDivert *>(p)->dll.symbol<void *>(n) != nullptr;
        };
        api.compile_filter = [](void *p, const char *f) {
            auto &s = *static_cast<SystemWinDivert *>(p);
            std::array<uint8_t, 4096> object{};
            const char *error = nullptr;
            uint32_t position = 0;
            return s.compile(f, windivert_layer_network, object.data(),
                             static_cast<uint32_t>(object.size()), &error, &position) != 0;
        };
        api.open = [](void *p, const char *f, uint32_t layer, uint64_t flags) {
            auto &s = *static_cast<SystemWinDivert *>(p);
            void *h = s.open(f, layer, 0, flags);
            if (h == reinterpret_cast<void *>(static_cast<intptr_t>(-1)))
                return static_cast<void *>(nullptr);
            uint64_t major = 0, minor = 0;
            if (!s.get(h, 3, &major) || !s.get(h, 4, &minor) || major != 2 || minor < 2) {
                s.close(h);
                return static_cast<void *>(nullptr);
            }
            return h;
        };
        api.set_param = [](void *p, void *h, uint32_t k, uint64_t v) {
            return static_cast<SystemWinDivert *>(p)->set(h, k, v) != 0;
        };
        api.receive_batch = [](void *p, void *h, LivePacket *out, size_t cap, size_t *count) {
            auto &s = *static_cast<SystemWinDivert *>(p);
            if (cap == 0 || cap > 255 || s.overlapped.hEvent == nullptr ||
                s.qpc_frequency.QuadPart <= 0)
                return ApiResult::failed;
            s.packet_buffer.resize(cap * 65'535u);
            s.addresses.resize(cap);
            ::ResetEvent(s.overlapped.hEvent);
            s.overlapped.Internal = 0;
            s.overlapped.InternalHigh = 0;
            s.overlapped.Offset = 0;
            s.overlapped.OffsetHigh = 0;
            uint32_t received = 0;
            uint32_t address_length =
                static_cast<uint32_t>(s.addresses.size() * sizeof(WinDivertAddress));
            if (!s.receive(h, s.packet_buffer.data(), static_cast<uint32_t>(s.packet_buffer.size()),
                           &received, 0, s.addresses.data(), &address_length, &s.overlapped)) {
                const auto error = ::GetLastError();
                if (error == ERROR_IO_PENDING) {
                    const DWORD wait = ::WaitForSingleObject(s.overlapped.hEvent, INFINITE);
                    DWORD transferred = 0;
                    if (wait != WAIT_OBJECT_0 ||
                        !::GetOverlappedResult(static_cast<HANDLE>(h), &s.overlapped, &transferred,
                                               FALSE)) {
                        const auto completion_error = ::GetLastError();
                        return completion_error == ERROR_NO_DATA ||
                                       completion_error == ERROR_OPERATION_ABORTED
                                   ? ApiResult::cancelled
                                   : ApiResult::failed;
                    }
                    received = transferred;
                } else {
                    return error == ERROR_NO_DATA || error == ERROR_OPERATION_ABORTED
                               ? ApiResult::cancelled
                               : ApiResult::failed;
                }
            }
            if (address_length == 0 || address_length % sizeof(WinDivertAddress) != 0)
                return ApiResult::failed;
            const size_t packet_count = address_length / sizeof(WinDivertAddress);
            if (packet_count > cap)
                return ApiResult::failed;
            std::vector<WinDivertPacketMetadata> metadata(packet_count);
            for (size_t index = 0; index < packet_count; ++index) {
                metadata[index] = {.qpc_timestamp = s.addresses[index].timestamp,
                                   .outbound = s.addresses[index].outbound != 0};
            }
            const auto parser = [](void *context, const uint8_t *current, uint32_t remaining,
                                   const uint8_t **next, uint32_t *next_length) {
                auto &self = *static_cast<SystemWinDivert *>(context);
                void *parsed_next = nullptr;
                const bool result =
                    self.parse(current, remaining, nullptr, nullptr, nullptr, nullptr, nullptr,
                               nullptr, nullptr, nullptr, nullptr, &parsed_next, next_length) != 0;
                *next = static_cast<const uint8_t *>(parsed_next);
                return result;
            };
            if (!split_windivert_batch(std::span<const uint8_t>(s.packet_buffer.data(), received),
                                       metadata, s.qpc_frequency.QuadPart, parser, &s,
                                       std::span<LivePacket>(out, cap)))
                return ApiResult::failed;
            *count = packet_count;
            return ApiResult::ok;
        };
        api.stats = [](void *, void *, BackendStats *) { return true; };
        api.cancel = [](void *p, void *h) {
            return static_cast<SystemWinDivert *>(p)->shutdown(h, 0) != 0;
        };
        api.close = [](void *p, void *h) { static_cast<SystemWinDivert *>(p)->close(h); };
    }
    ~SystemWinDivert() {
        if (overlapped.hEvent != nullptr)
            ::CloseHandle(overlapped.hEvent);
    }
    [[nodiscard]] bool ready() const noexcept {
        return dll && open && receive && shutdown && close && set && get && compile && parse &&
               overlapped.hEvent != nullptr && qpc_frequency.QuadPart > 0;
    }
};

class WinDivertBackend final : public CaptureBackend {
  public:
    explicit WinDivertBackend(const WinDivertApi *api, std::shared_ptr<void> owner = {})
        : api_(api), owner_(std::move(owner)) {
        diagnostic_.backend = "windivert";
    }
    ~WinDivertBackend() override { stop(); }
    BackendError start(const BackendConfig &c, CaptureSink sink) override {
        if (!api_) {
            return fail(BackendError::library_missing, "WinDivert.dll was not found");
        }
        if (c.port == 0 || c.batch_size == 0 || c.batch_size > 255 || c.queue_length < 32 ||
            c.queue_length > 16384 || c.queue_size < 65535 || c.queue_size > 33554432 ||
            c.queue_time_ms < 100 || c.queue_time_ms > 16000)
            return fail(BackendError::invalid_config,
                        "WinDivert queue configuration is outside documented bounds");
        if (!api_->identity || api_->identity(api_->context) != ApiResult::ok)
            return fail(BackendError::incompatible_runtime,
                        "WinDivert runtime/driver is not compatible with 2.2");
        if (!api_->resolve ||
            std::any_of(required_symbols.begin(), required_symbols.end(),
                        [&](const char *n) { return !api_->resolve(api_->context, n); }))
            return fail(BackendError::symbol_missing, "WinDivert 2.2 required export is missing");
        char filter[96]{};
        std::snprintf(filter, sizeof(filter), "tcp and (tcp.SrcPort == %u or tcp.DstPort == %u)",
                      c.port, c.port);
        if (!api_->compile_filter || !api_->compile_filter(api_->context, filter))
            return fail(BackendError::invalid_config, "WinDivert filter validation failed");
        if (!api_->open ||
            (handle_ = api_->open(api_->context, filter, windivert_layer_network,
                                  windivert_flag_sniff | windivert_flag_recv_only)) == nullptr)
            return fail(BackendError::open_failed, "WinDivertOpen failed");
        if (!api_->set_param ||
            !api_->set_param(api_->context, handle_, windivert_param_queue_length,
                             c.queue_length) ||
            !api_->set_param(api_->context, handle_, windivert_param_queue_size, c.queue_size) ||
            !api_->set_param(api_->context, handle_, windivert_param_queue_time, c.queue_time_ms)) {
            stop();
            return fail(BackendError::open_failed, "WinDivert queue configuration failed");
        }
        sink_ = std::move(sink);
        batch_.resize(c.batch_size);
        started_ = true;
        diagnostic_.error = BackendError::none;
        return BackendError::none;
    }
    BackendError poll() override {
        if (!started_ || !api_->receive_batch)
            return BackendError::receive_failed;
        size_t count = 0;
        const auto result =
            api_->receive_batch(api_->context, handle_, batch_.data(), batch_.size(), &count);
        if (result == ApiResult::cancelled)
            return BackendError::cancelled;
        if (result == ApiResult::would_block)
            return BackendError::none;
        if (result != ApiResult::ok || count > batch_.size())
            return fail(BackendError::receive_failed, "WinDivertRecvEx failed");
        for (size_t i = 0; i < count; ++i) {
            const auto &p = batch_[i];
            if ((p.bytes == nullptr && p.captured_length != 0) ||
                p.original_length < p.captured_length)
                continue;
            CaptureRecord r{.source = CaptureSource::windivert,
                            .timestamp_ns = p.timestamp_ns,
                            .link_type = p.link_type,
                            .captured_length = p.captured_length,
                            .original_length = p.original_length,
                            .bytes = std::vector<uint8_t>(p.bytes, p.bytes + p.captured_length),
                            .direction = p.outbound ? CaptureDirection::outbound
                                                    : CaptureDirection::inbound};
            sink_(r);
            ++stats_.received;
        }
        return BackendError::none;
    }
    void stop() noexcept override {
        if (handle_ && api_) {
            if (api_->close)
                api_->close(api_->context, handle_);
        }
        handle_ = nullptr;
        started_ = false;
        sink_ = {};
        batch_.clear();
    }
    void request_stop() noexcept override {
        if (handle_ && api_ && api_->cancel)
            api_->cancel(api_->context, handle_);
    }
    BackendStats stats() const noexcept override {
        auto value = stats_;
        if (handle_ && api_ && api_->stats)
            api_->stats(api_->context, handle_, &value);
        return value;
    }
    const BackendDiagnostic &diagnostic() const noexcept override { return diagnostic_; }

  private:
    BackendError fail(BackendError e, std::string m) {
        diagnostic_.error = e;
        diagnostic_.message = std::move(m);
        return e;
    }
    const WinDivertApi *api_;
    std::shared_ptr<void> owner_;
    void *handle_ = nullptr;
    bool started_ = false;
    CaptureSink sink_;
    std::vector<LivePacket> batch_;
    BackendStats stats_{};
    BackendDiagnostic diagnostic_{};
};
} // namespace
std::unique_ptr<CaptureBackend> make_windivert_backend(const WinDivertApi *api) {
    return std::make_unique<WinDivertBackend>(api);
}
std::unique_ptr<CaptureBackend> make_system_windivert_backend() {
    auto owner = std::make_shared<SystemWinDivert>();
    if (!owner->ready())
        return make_windivert_backend(nullptr);
    return std::make_unique<WinDivertBackend>(&owner->api, owner);
}
bool probe_windivert_runtime() noexcept {
    try {
        DynamicLibrary dll(application_library_path(L"WinDivert.dll"));
        if (!dll)
            return false;
        return std::all_of(required_symbols.begin(), required_symbols.end(),
                           [&](const char *n) { return dll.symbol<void *>(n) != nullptr; });
    } catch (...) {
        return false;
    }
}
} // namespace namter
