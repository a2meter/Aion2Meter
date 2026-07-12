#include "dynamic_library.hpp"
#include "live_backend.hpp"

#include <array>
#include <cstdio>

namespace namter {
namespace {
constexpr std::array required_symbols{"pcap_lib_version",
                                      "pcap_findalldevs",
                                      "pcap_freealldevs",
                                      "pcap_create",
                                      "pcap_set_immediate_mode",
                                      "pcap_set_buffer_size",
                                      "pcap_setuserbuffer",
                                      "pcap_activate",
                                      "pcap_compile",
                                      "pcap_setfilter",
                                      "pcap_freecode",
                                      "pcap_getevent",
                                      "pcap_next_ex",
                                      "pcap_breakloop",
                                      "pcap_datalink",
                                      "pcap_stats",
                                      "pcap_geterr",
                                      "pcap_statustostr",
                                      "pcap_close"};
struct PcapIf {
    PcapIf *next;
    char *name;
    char *description;
    void *addresses;
    uint32_t flags;
};
struct PcapTimeval {
    long seconds;
    long microseconds;
};
struct PcapHeaderNative {
    PcapTimeval timestamp;
    uint32_t captured_length;
    uint32_t original_length;
};
struct BpfProgram {
    uint32_t length;
    void *instructions;
};
class SystemNpcap {
  public:
    DynamicLibrary dll{npcap_library_path()};
    NpcapApi api{};
    using Identity = const char *(__cdecl *)();
    using Find = int(__cdecl *)(PcapIf **, char *);
    using Free = void(__cdecl *)(PcapIf *);
    using Create = void *(__cdecl *)(const char *, char *);
    using SetInt = int(__cdecl *)(void *, int);
    using Activate = int(__cdecl *)(void *);
    using Compile = int(__cdecl *)(void *, BpfProgram *, const char *, int, uint32_t);
    using SetFilter = int(__cdecl *)(void *, BpfProgram *);
    using FreeCode = void(__cdecl *)(BpfProgram *);
    using Datalink = int(__cdecl *)(void *);
    using GetEvent = void *(__cdecl *)(void *);
    using Next = int(__cdecl *)(void *, PcapHeaderNative **, const uint8_t **);
    using Stats = int(__cdecl *)(void *, NpcapStatNative *);
    using Break = void(__cdecl *)(void *);
    using Close = void(__cdecl *)(void *);
    using GetError = const char *(__cdecl *)(void *);
    using StatusToString = const char *(__cdecl *)(int);
    Identity identity{};
    Find find{};
    Free free_devices{};
    Create create{};
    SetInt immediate{}, kernel{}, user{};
    Activate activate{};
    Compile compile{};
    SetFilter set_filter{};
    FreeCode free_code{};
    Datalink datalink{};
    GetEvent get_event{};
    Next next{};
    Stats stats{};
    Break break_loop{};
    Close close{};
    GetError get_error{};
    StatusToString status_to_string{};
    int last_error_code = 0;
    std::string last_error_message;
    SystemNpcap() {
        if (!dll)
            return;
        identity = dll.symbol<Identity>("pcap_lib_version");
        find = dll.symbol<Find>("pcap_findalldevs");
        free_devices = dll.symbol<Free>("pcap_freealldevs");
        create = dll.symbol<Create>("pcap_create");
        immediate = dll.symbol<SetInt>("pcap_set_immediate_mode");
        kernel = dll.symbol<SetInt>("pcap_set_buffer_size");
        user = dll.symbol<SetInt>("pcap_setuserbuffer");
        activate = dll.symbol<Activate>("pcap_activate");
        compile = dll.symbol<Compile>("pcap_compile");
        set_filter = dll.symbol<SetFilter>("pcap_setfilter");
        free_code = dll.symbol<FreeCode>("pcap_freecode");
        datalink = dll.symbol<Datalink>("pcap_datalink");
        get_event = dll.symbol<GetEvent>("pcap_getevent");
        next = dll.symbol<Next>("pcap_next_ex");
        stats = dll.symbol<Stats>("pcap_stats");
        break_loop = dll.symbol<Break>("pcap_breakloop");
        close = dll.symbol<Close>("pcap_close");
        get_error = dll.symbol<GetError>("pcap_geterr");
        status_to_string = dll.symbol<StatusToString>("pcap_statustostr");
        api.context = this;
        api.identity = [](void *p) { return static_cast<SystemNpcap *>(p)->identity(); };
        api.resolve = [](void *p, const char *n) {
            return static_cast<SystemNpcap *>(p)->dll.symbol<void *>(n) != nullptr;
        };
        api.enumerate = [](void *p) {
            auto &s = *static_cast<SystemNpcap *>(p);
            std::vector<std::string> v;
            PcapIf *all = nullptr;
            char error[256]{};
            const int result=s.find(&all,error);
            if (result == 0) {
                for (auto *i = all; i; i = i->next)
                    if (i->name)
                        v.emplace_back(i->name);
                s.free_devices(all);
            } else {
                s.last_error_code=result;
                s.last_error_message=error;
            }
            return v;
        };
        api.create = [](void *p, const char *n) {
            auto &s=*static_cast<SystemNpcap *>(p);
            char error[256]{};
            void *handle=s.create(n, error);
            if(!handle){s.last_error_code=-1;s.last_error_message=error;}
            return handle;
        };
        api.set_immediate = [](void *p, void *h, int v) {
            return static_cast<SystemNpcap *>(p)->immediate(h, v);
        };
        api.set_kernel_buffer = [](void *p, void *h, int v) {
            return static_cast<SystemNpcap *>(p)->kernel(h, v);
        };
        api.set_user_buffer = [](void *p, void *h, int v) {
            auto &s = *static_cast<SystemNpcap *>(p);
            return s.user ? s.user(h, v) : 0;
        };
        api.activate = [](void *p, void *h) { auto&s=*static_cast<SystemNpcap *>(p);const int code=s.activate(h);if(code<0){s.last_error_code=code;const char*status=s.status_to_string?s.status_to_string(code):nullptr;const char*detail=s.get_error?s.get_error(h):nullptr;s.last_error_message=status?status:"pcap_activate failed";if(detail&&*detail){s.last_error_message += ": ";s.last_error_message += detail;}}return code; };
        api.compile_apply = [](void *p, void *h, const char *f) {
            auto &s = *static_cast<SystemNpcap *>(p);
            BpfProgram b{};
            if (s.compile(h, &b, f, 1, 0xffffffffu) != 0) {
                s.last_error_code=-1;const char*text=s.get_error?s.get_error(h):nullptr;s.last_error_message=text?text:"pcap_compile failed";
                return -1;
            }
            const int result = s.set_filter(h, &b);
            if(result!=0){s.last_error_code=result;const char*text=s.get_error?s.get_error(h):nullptr;s.last_error_message=text?text:"pcap_setfilter failed";}
            if (s.free_code)
                s.free_code(&b);
            return result;
        };
        api.link_type = [](void *p, void *h) {
            return static_cast<uint32_t>(static_cast<SystemNpcap *>(p)->datalink(h));
        };
        api.get_event = [](void *p, void *h) {
            return static_cast<SystemNpcap *>(p)->get_event(h);
        };
        api.receive = [](void *p, void *h, LivePacket *out) {
            auto &s = *static_cast<SystemNpcap *>(p);
            const DWORD wait = ::WaitForSingleObject(static_cast<HANDLE>(s.get_event(h)), 100);
            if (wait == WAIT_TIMEOUT)
                return ApiResult::would_block;
            if (wait != WAIT_OBJECT_0)
                return ApiResult::failed;
            PcapHeaderNative *header = nullptr;
            const uint8_t *bytes = nullptr;
            const int result = s.next(h, &header, &bytes);
            if (result == 0)
                return ApiResult::would_block;
            if (result == -2)
                return ApiResult::cancelled;
            if (result != 1 || !header) {
                s.last_error_code=result;const char*text=s.get_error?s.get_error(h):nullptr;s.last_error_message=text?text:"pcap_next_ex failed";
                return ApiResult::failed;
            }
            out->bytes = bytes;
            out->captured_length = header->captured_length;
            out->original_length = header->original_length;
            out->timestamp_ns =
                static_cast<uint64_t>(header->timestamp.seconds) * 1'000'000'000ull +
                static_cast<uint64_t>(header->timestamp.microseconds) * 1000ull;
            return ApiResult::ok;
        };
        api.stats = [](void *p, void *h, BackendStats *out) {
            NpcapStatNative s{};
            if (static_cast<SystemNpcap *>(p)->stats(h, &s) != 0)
                return false;
            *out = npcap_stats_from_native(s);
            return true;
        };
        api.break_loop = [](void *p, void *h) { static_cast<SystemNpcap *>(p)->break_loop(h); };
        api.close = [](void *p, void *h) { static_cast<SystemNpcap *>(p)->close(h); };
        api.last_error_code=[](void*p){return static_cast<SystemNpcap*>(p)->last_error_code;};
        api.last_error_message=[](void*p){return static_cast<SystemNpcap*>(p)->last_error_message.c_str();};
    }
    [[nodiscard]] bool ready() const noexcept {
        return dll && identity && find && free_devices && create && immediate && kernel && user &&
               activate && compile && set_filter && free_code && datalink && get_event && next &&
               stats && break_loop && close && get_error && status_to_string;
    }
};
class NpcapBackend final : public CaptureBackend {
  public:
    explicit NpcapBackend(const NpcapApi *a, std::shared_ptr<void> owner = {})
        : api_(a), owner_(std::move(owner)) {
        diagnostic_.backend = "npcap";
        diagnostic_.help_url = "https://npcap.com/#download";
    }
    ~NpcapBackend() override { stop(); }
    BackendError start(const BackendConfig &c, CaptureSink sink) override {
        if (!api_)
            return fail(BackendError::npcap_not_installed,
                        "Npcap is not installed; install it independently from the "
                        "official site");
        if (c.port == 0 || c.kernel_buffer_size <= 0 || c.user_buffer_size <= 0)
            return fail(BackendError::invalid_config, "Npcap configuration is invalid");
        if (!api_->identity)
            return fail(BackendError::symbol_missing, "pcap_lib_version is missing");
        const char *v = api_->identity(api_->context);
        diagnostic_.runtime_version = v ? v : "";
        if (!v || diagnostic_.runtime_version.find("Npcap") == std::string::npos)
            return fail(BackendError::incompatible_runtime,
                        "legacy WinPcap and non-Npcap runtimes are rejected");
        if (!api_->resolve ||
            std::any_of(required_symbols.begin(), required_symbols.end(),
                        [&](const char *n) { return !api_->resolve(api_->context, n); }))
            return fail(BackendError::symbol_missing, "Npcap required export is missing");
        if (!api_->enumerate || !api_->create)
            return fail(BackendError::symbol_missing, "Npcap adapter API is missing");
        auto adapters = api_->enumerate(api_->context);
        if(adapters.empty()&&api_->last_error_code&&api_->last_error_code(api_->context)!=0)
            return fail(BackendError::adapter_not_found,"Npcap adapter enumeration failed");
        const auto selected = c.adapter.empty() && !adapters.empty() ? adapters.front() : c.adapter;
        if (std::find(adapters.begin(), adapters.end(), selected) == adapters.end())
            return fail(BackendError::adapter_not_found, "selected Npcap adapter was not found");
        handle_ = api_->create(api_->context, selected.c_str());
        diagnostic_.interface_identity = selected;
        if (!handle_)
            return fail(BackendError::open_failed, "pcap_create failed");
        char filter[80]{};
        std::snprintf(filter, sizeof(filter), "tcp and (src port %u or dst port %u)", c.port,
                      c.port);
        if (!api_->set_immediate || api_->set_immediate(api_->context, handle_, 1) != 0 ||
            !api_->set_kernel_buffer ||
            api_->set_kernel_buffer(api_->context, handle_, c.kernel_buffer_size) != 0 ||
            !api_->set_user_buffer ||
            api_->set_user_buffer(api_->context, handle_, c.user_buffer_size) != 0 ||
            !api_->activate || api_->activate(api_->context, handle_) < 0) {
            stop();
            return fail(BackendError::activate_failed, "Npcap activation/configuration failed");
        }
        if (!api_->compile_apply || api_->compile_apply(api_->context, handle_, filter) != 0) {
            stop();
            return fail(BackendError::activate_failed, "Npcap BPF compile/apply failed");
        }
        link_type_ = api_->link_type ? api_->link_type(api_->context, handle_) : 0;
        if (link_type_ == 0 || !api_->get_event ||
            api_->get_event(api_->context, handle_) == nullptr) {
            stop();
            return fail(BackendError::activate_failed,
                        "Npcap link type or capture event is unavailable");
        }
        sink_ = std::move(sink);
        server_port_ = c.port;
        started_ = true;
        diagnostic_.error = BackendError::none;
        return BackendError::none;
    }
    BackendError poll() override {
        if (!started_ || !api_->receive)
            return BackendError::receive_failed;
        LivePacket p{};
        auto result = api_->receive(api_->context, handle_, &p);
        if (result == ApiResult::cancelled)
            return BackendError::cancelled;
        if (result == ApiResult::would_block)
            return BackendError::none;
        if (result != ApiResult::ok || (p.bytes == nullptr && p.captured_length != 0) ||
            p.original_length < p.captured_length)
            return fail(BackendError::receive_failed, "pcap_next_ex failed");
        CaptureRecord r{.source = CaptureSource::npcap,
                        .timestamp_ns = p.timestamp_ns,
                        .link_type = link_type_,
                        .captured_length = p.captured_length,
                        .original_length = p.original_length,
                        .bytes = std::vector<uint8_t>(p.bytes, p.bytes + p.captured_length),
                        .direction = p.outbound ? CaptureDirection::outbound : CaptureDirection::unknown,
                        .backend_name = diagnostic_.backend,
                        .runtime_version = diagnostic_.runtime_version,
                        .interface_identity = diagnostic_.interface_identity};
        if (r.direction == CaptureDirection::unknown)
            r.direction = infer_direction(r, server_port_);
        const auto current_stats = stats();
        r.backend_received = current_stats.received;
        r.backend_dropped = current_stats.dropped;
        r.backend_interface_dropped = current_stats.interface_dropped;
        sink_(r);
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
    }
    void request_stop() noexcept override {
        if (handle_ && api_ && api_->break_loop)
            api_->break_loop(api_->context, handle_);
    }
    BackendStats stats() const noexcept override {
        BackendStats value{};
        if (handle_ && api_ && api_->stats)
            api_->stats(api_->context, handle_, &value);
        return value;
    }
    const BackendDiagnostic &diagnostic() const noexcept override { return diagnostic_; }

  private:
    BackendError fail(BackendError e, std::string m) {
        diagnostic_.error = e;
        if(api_&&api_->last_error_code) diagnostic_.native_error=static_cast<uint32_t>(api_->last_error_code(api_->context));
        diagnostic_.stable_native_category=diagnostic_.native_error;
        if(api_&&api_->last_error_message){const char*detail=api_->last_error_message(api_->context);if(detail&&*detail)m += ": "+std::string(detail);}
        diagnostic_.message = std::move(m);
        diagnostic_.automatic_action = false;
        return e;
    }
    const NpcapApi *api_;
    std::shared_ptr<void> owner_;
    void *handle_ = nullptr;
    bool started_ = false;
    uint32_t link_type_ = 0;
    uint16_t server_port_ = 0;
    CaptureSink sink_;
    BackendDiagnostic diagnostic_{};
};
} // namespace
std::unique_ptr<CaptureBackend> make_npcap_backend(const NpcapApi *api) {
    return std::make_unique<NpcapBackend>(api);
}
BackendStats npcap_stats_from_native(const NpcapStatNative &stats) noexcept {
    return {stats.received, stats.dropped, stats.interface_dropped,
            stats.captured, stats.sent, stats.network_dropped};
}
std::unique_ptr<CaptureBackend> make_system_npcap_backend() {
    auto owner = std::make_shared<SystemNpcap>();
    if (!owner->ready())
        return make_npcap_backend(nullptr);
    return std::make_unique<NpcapBackend>(&owner->api, owner);
}
bool probe_npcap_runtime() noexcept {
    try {
        DynamicLibrary dll(npcap_library_path());
        if (!dll)
            return false;
        using Identity = const char *(__cdecl *)();
        const auto identity = dll.symbol<Identity>("pcap_lib_version");
        if (!identity)
            return false;
        const char *version = identity();
        if (!version || std::string_view(version).find("Npcap") == std::string_view::npos)
            return false;
        return std::all_of(required_symbols.begin(), required_symbols.end(),
                           [&](const char *n) { return dll.symbol<void *>(n) != nullptr; });
    } catch (...) {
        return false;
    }
}
} // namespace namter
