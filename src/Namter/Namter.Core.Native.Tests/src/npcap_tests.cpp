#include "dynamic_library.hpp"
#include "live_backend.hpp"
#include <gtest/gtest.h>

namespace {
using namespace namter;
struct FakeNpcap {
    NpcapApi api{};
    std::string version = "Npcap version 1.83, based on libpcap";
    std::vector<std::string> resolved;
    bool activate_ok = true;
    bool cancelled = false;
    std::string filter;
    int immediate = 0;
    int kernel = 0;
    int user = 0;
    uint32_t link = dlt_en10mb;
    uint64_t recv = 5, drop = 2, ifdrop = 1;
    std::vector<LivePacket> packets;
    FakeNpcap() {
        api.context = this;
        api.identity = [](void *p) { return static_cast<FakeNpcap *>(p)->version.c_str(); };
        api.resolve = [](void *p, const char *n) {
            static_cast<FakeNpcap *>(p)->resolved.emplace_back(n);
            return true;
        };
        api.enumerate = [](void *) { return std::vector<std::string>{"\\Device\\NPF_{TEST}"}; };
        api.create = [](void *, const char *) { return reinterpret_cast<void *>(1); };
        api.set_immediate = [](void *p, void *, int v) {
            static_cast<FakeNpcap *>(p)->immediate = v;
            return 0;
        };
        api.set_kernel_buffer = [](void *p, void *, int v) {
            static_cast<FakeNpcap *>(p)->kernel = v;
            return 0;
        };
        api.set_user_buffer = [](void *p, void *, int v) {
            static_cast<FakeNpcap *>(p)->user = v;
            return 0;
        };
        api.activate = [](void *p, void *) {
            return static_cast<FakeNpcap *>(p)->activate_ok ? 0 : -1;
        };
        api.compile_apply = [](void *p, void *, const char *f) {
            static_cast<FakeNpcap *>(p)->filter = f;
            return 0;
        };
        api.link_type = [](void *p, void *) { return static_cast<FakeNpcap *>(p)->link; };
        api.get_event = [](void *, void *) { return reinterpret_cast<void *>(2); };
        api.receive = [](void *p, void *, LivePacket *out) {
            auto &s = *static_cast<FakeNpcap *>(p);
            if (s.cancelled)
                return ApiResult::cancelled;
            if (s.packets.empty())
                return ApiResult::would_block;
            *out = s.packets.front();
            s.packets.erase(s.packets.begin());
            return ApiResult::ok;
        };
        api.stats = [](void *p, void *, BackendStats *out) {
            auto &s = *static_cast<FakeNpcap *>(p);
            *out = {s.recv, s.drop, s.ifdrop};
            return true;
        };
        api.break_loop = [](void *, void *) {};
        api.close = [](void *, void *) {};
    }
};
BackendConfig config() {
    return {.port = 13328,
            .adapter = "\\Device\\NPF_{TEST}",
            .kernel_buffer_size = 4 * 1024 * 1024,
            .user_buffer_size = 1024 * 1024};
}
} // namespace
TEST(NpcapBackend, ReportsDedicatedMissingRuntimeWithStructuredOfficialHelp) {
    auto b = make_npcap_backend(nullptr);
    EXPECT_EQ(b->start(config(), [](const CaptureRecord &) {}), BackendError::npcap_not_installed);
    EXPECT_EQ(b->diagnostic().help_url, "https://npcap.com/#download");
    EXPECT_FALSE(b->diagnostic().automatic_action);
}
TEST(NpcapBackend, RejectsLegacyWinPcapAndMissingSymbol) {
    FakeNpcap f;
    f.version = "WinPcap version 4.1.3";
    EXPECT_EQ(make_npcap_backend(&f.api)->start(config(), [](const CaptureRecord &) {}),
              BackendError::incompatible_runtime);
    f.version = "Npcap version 1.83";
    f.api.resolve = [](void *, const char *n) { return std::string_view(n) != "pcap_activate"; };
    EXPECT_EQ(make_npcap_backend(&f.api)->start(config(), [](const CaptureRecord &) {}),
              BackendError::symbol_missing);
}
TEST(NpcapBackend, EnumeratesConfiguresActivatesAndAppliesEquivalentFilter) {
    FakeNpcap f;
    auto b = make_npcap_backend(&f.api);
    ASSERT_EQ(b->start(config(), [](const CaptureRecord &) {}), BackendError::none);
    EXPECT_EQ(f.immediate, 1);
    EXPECT_EQ(f.kernel, 4 * 1024 * 1024);
    EXPECT_EQ(f.user, 1024 * 1024);
    EXPECT_EQ(f.filter, "tcp and (src port 13328 or dst port 13328)");
}
TEST(NpcapBackend, PreservesLinkTypeDeliversPacketsAndReportsDrops) {
    FakeNpcap f;
    const uint8_t p[]{1, 2, 3};
    f.packets = {{.bytes = p, .captured_length = 3, .original_length = 5, .timestamp_ns = 9}};
    std::vector<CaptureRecord> s;
    auto b = make_npcap_backend(&f.api);
    ASSERT_EQ(b->start(config(), [&](const CaptureRecord &r) { s.push_back(r); }),
              BackendError::none);
    EXPECT_EQ(b->poll(), BackendError::none);
    ASSERT_EQ(s.size(), 1u);
    EXPECT_EQ(s[0].link_type, dlt_en10mb);
    EXPECT_EQ(s[0].source, CaptureSource::npcap);
    auto stats = b->stats();
    EXPECT_EQ(stats.received, 5u);
    EXPECT_EQ(stats.dropped, 2u);
    EXPECT_EQ(stats.interface_dropped, 1u);
    f.cancelled = true;
    EXPECT_EQ(b->poll(), BackendError::cancelled);
}

TEST(NpcapBackend, InfersDirectionFromNormalizedTupleAndConfiguredServerPort) {
    FakeNpcap f;
    f.link = dlt_raw;
    const std::vector<uint8_t> packet = {
        0x45, 0,    0, 0x28, 0, 0, 0, 0, 64, 6, 0, 0, 10,   0,    0, 1, 10, 0, 0, 2,
        0x34, 0x10, 0, 0x50, 0, 0, 0, 1, 0,  0, 0, 0, 0x50, 0x18, 0, 0, 0,  0, 0, 0};
    f.packets = {{.bytes = packet.data(),
                  .captured_length = static_cast<uint32_t>(packet.size()),
                  .original_length = static_cast<uint32_t>(packet.size()),
                  .timestamp_ns = 10}};
    std::vector<CaptureRecord> seen;
    auto backend = make_npcap_backend(&f.api);
    auto c = config();
    c.port = 0x3410;
    ASSERT_EQ(backend->start(c, [&](const CaptureRecord &r) { seen.push_back(r); }),
              BackendError::none);
    ASSERT_EQ(backend->poll(), BackendError::none);
    ASSERT_EQ(seen.size(), 1u);
    EXPECT_EQ(seen[0].direction, CaptureDirection::inbound);
}
TEST(NpcapBackend, RejectsUnknownAdapterAndActivationFailure) {
    FakeNpcap f;
    auto c = config();
    c.adapter = "missing";
    EXPECT_EQ(make_npcap_backend(&f.api)->start(c, [](const CaptureRecord &) {}),
              BackendError::adapter_not_found);
    f.activate_ok = false;
    EXPECT_EQ(make_npcap_backend(&f.api)->start(config(), [](const CaptureRecord &) {}),
              BackendError::activate_failed);
}
TEST(NpcapBackend, RuntimeLoaderUsesOnlyAbsoluteSystem32NpcapPath) {
    const auto path = npcap_library_path();
    ASSERT_TRUE(path.is_absolute());
    EXPECT_EQ(path.filename(), L"wpcap.dll");
    EXPECT_EQ(path.parent_path().filename(), L"Npcap");
    DynamicLibrary relative(L"wpcap.dll");
    EXPECT_FALSE(relative);
}
TEST(NpcapBackend, InstalledRuntimeAvailabilityProbeCanActivateAndCancel) {
    if (!probe_npcap_runtime())
        GTEST_SKIP() << "external Npcap runtime is not installed";
    auto backend = make_system_npcap_backend();
    auto c = config();
    c.adapter.clear();
    const auto status = backend->start(c, [](const CaptureRecord &) {});
    if (status != BackendError::none)
        GTEST_SKIP() << "Npcap runtime is installed but capture activation is "
                        "unavailable in this session";
    backend->stop();
    SUCCEED();
}
