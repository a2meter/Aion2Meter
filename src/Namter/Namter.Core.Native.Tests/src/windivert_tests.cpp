#include <array>
#include <gtest/gtest.h>

#include "live_backend.hpp"

namespace {
using namespace namter;

struct FakeWinDivert {
    WinDivertApi api{};
    bool compatible = true;
    bool compile_ok = true;
    bool open_ok = true;
    bool receive_cancelled = false;
    std::vector<std::string> resolved;
    std::string filter;
    uint32_t layer = 99;
    uint64_t flags = 0;
    std::vector<std::pair<uint32_t, uint64_t>> params;
    std::vector<LivePacket> packets;
    BackendStats backend_stats{10, 3, 0};

    FakeWinDivert() {
        api.context = this;
        api.identity = [](void *p) {
            return static_cast<FakeWinDivert *>(p)->compatible ? ApiResult::ok
                                                               : ApiResult::incompatible;
        };
        api.resolve = [](void *p, const char *name) {
            static_cast<FakeWinDivert *>(p)->resolved.emplace_back(name);
            return true;
        };
        api.compile_filter = [](void *p, const char *filter) {
            auto &s = *static_cast<FakeWinDivert *>(p);
            s.filter = filter;
            return s.compile_ok;
        };
        api.open = [](void *p, const char *, uint32_t layer, uint64_t flags) {
            auto &s = *static_cast<FakeWinDivert *>(p);
            s.layer = layer;
            s.flags = flags;
            return s.open_ok ? reinterpret_cast<void *>(1) : nullptr;
        };
        api.set_param = [](void *p, void *, uint32_t key, uint64_t value) {
            static_cast<FakeWinDivert *>(p)->params.emplace_back(key, value);
            return true;
        };
        api.receive_batch = [](void *p, void *, LivePacket *out, size_t cap, size_t *count) {
            auto &s = *static_cast<FakeWinDivert *>(p);
            if (s.receive_cancelled)
                return ApiResult::cancelled;
            *count = std::min(cap, s.packets.size());
            for (size_t i = 0; i < *count; ++i)
                out[i] = s.packets[i];
            return ApiResult::ok;
        };
        api.stats = [](void *p, void *, BackendStats *out) {
            *out = static_cast<FakeWinDivert *>(p)->backend_stats;
            return true;
        };
        api.cancel = [](void *, void *) { return true; };
        api.close = [](void *, void *) {};
    }
};

BackendConfig config() {
    return {.port = 13328,
            .queue_length = 4096,
            .queue_size = 8 * 1024 * 1024,
            .queue_time_ms = 1000,
            .batch_size = 8};
}
} // namespace

TEST(WinDivertBackend, RejectsMissingDllAndMissingRequiredSymbol) {
    auto missing = make_windivert_backend(nullptr);
    EXPECT_EQ(missing->start(config(), [](const CaptureRecord &) {}),
              BackendError::library_missing);
    FakeWinDivert fake;
    fake.api.resolve = [](void *, const char *name) {
        return std::string_view(name) != "WinDivertRecvEx";
    };
    EXPECT_EQ(make_windivert_backend(&fake.api)->start(config(), [](const CaptureRecord &) {}),
              BackendError::symbol_missing);
}

TEST(WinDivertBackend, RejectsIncompatibleIdentityAndOpenFailure) {
    FakeWinDivert fake;
    fake.compatible = false;
    EXPECT_EQ(make_windivert_backend(&fake.api)->start(config(), [](const CaptureRecord &) {}),
              BackendError::incompatible_runtime);
    fake.compatible = true;
    fake.open_ok = false;
    EXPECT_EQ(make_windivert_backend(&fake.api)->start(config(), [](const CaptureRecord &) {}),
              BackendError::open_failed);
}

TEST(WinDivertBackend, UsesSelectiveReadOnlyNetworkCaptureAndBoundedQueue) {
    FakeWinDivert fake;
    auto backend = make_windivert_backend(&fake.api);
    ASSERT_EQ(backend->start(config(), [](const CaptureRecord &) {}), BackendError::none);
    EXPECT_EQ(fake.layer, windivert_layer_network);
    EXPECT_EQ(fake.flags, windivert_flag_sniff | windivert_flag_recv_only);
    EXPECT_EQ(fake.filter, "tcp and (tcp.SrcPort == 13328 or tcp.DstPort == 13328)");
    EXPECT_EQ(fake.params, (std::vector<std::pair<uint32_t, uint64_t>>{
                               {windivert_param_queue_length, 4096},
                               {windivert_param_queue_size, 8 * 1024 * 1024},
                               {windivert_param_queue_time, 1000}}));
    EXPECT_EQ(std::find(fake.resolved.begin(), fake.resolved.end(), "WinDivertSend"),
              fake.resolved.end());
    EXPECT_EQ(std::find(fake.resolved.begin(), fake.resolved.end(), "WinDivertSendEx"),
              fake.resolved.end());
}

TEST(WinDivertBackend, DeliversBatchAndTreatsCancellationAsCleanStop) {
    FakeWinDivert fake;
    const uint8_t bytes[]{0x45, 0, 0, 20};
    fake.packets = {{.bytes = bytes,
                     .captured_length = 4,
                     .original_length = 4,
                     .timestamp_ns = 7,
                     .link_type = dlt_raw,
                     .outbound = true}};
    std::vector<CaptureRecord> seen;
    auto backend = make_windivert_backend(&fake.api);
    ASSERT_EQ(backend->start(config(), [&](const CaptureRecord &r) { seen.push_back(r); }),
              BackendError::none);
    EXPECT_EQ(backend->poll(), BackendError::none);
    ASSERT_EQ(seen.size(), 1u);
    EXPECT_EQ(seen[0].bytes, (std::vector<uint8_t>{0x45, 0, 0, 20}));
    EXPECT_EQ(seen[0].source, CaptureSource::windivert);
    EXPECT_EQ(seen[0].timestamp_ns, 7u);
    EXPECT_EQ(seen[0].direction, CaptureDirection::outbound);
    fake.receive_cancelled = true;
    EXPECT_EQ(backend->poll(), BackendError::cancelled);
    backend->stop();
    backend->stop();
}

TEST(WinDivertBackend, ReportsInjectedBackendDropStatistics) {
    FakeWinDivert fake;
    auto backend = make_windivert_backend(&fake.api);
    ASSERT_EQ(backend->start(config(), [](const CaptureRecord &) {}), BackendError::none);
    const auto stats = backend->stats();
    EXPECT_EQ(stats.received, 10u);
    EXPECT_EQ(stats.dropped, 3u);
}

TEST(WinDivertBackend, SplitsPackedBatchOnlyAtHelperBoundariesAndConvertsQpcMetadata) {
    const std::vector<uint8_t> packed{3, 0xaa, 0xbb, 2, 0xcc};
    const std::array<WinDivertPacketMetadata, 2> metadata{{{10, false}, {20, true}}};
    std::array<LivePacket, 2> packets{};
    const auto parser = [](void *, const uint8_t *current, uint32_t remaining, const uint8_t **next,
                           uint32_t *next_length) {
        if (remaining == 0 || current[0] == 0 || current[0] > remaining)
            return false;
        *next = current + current[0];
        *next_length = remaining - current[0];
        return true;
    };
    ASSERT_TRUE(split_windivert_batch(packed, metadata, 10, parser, nullptr, packets));
    EXPECT_EQ(packets[0].captured_length, 3u);
    EXPECT_EQ(packets[1].captured_length, 2u);
    EXPECT_EQ(packets[0].timestamp_ns, 1'000'000'000u);
    EXPECT_EQ(packets[1].timestamp_ns, 2'000'000'000u);
    EXPECT_FALSE(packets[0].outbound);
    EXPECT_TRUE(packets[1].outbound);
}

TEST(WinDivertBackend, RejectsQueueAndBatchValuesOutsideDocumentedBounds) {
    FakeWinDivert fake;
    auto c = config();
    c.queue_length = 31;
    EXPECT_EQ(make_windivert_backend(&fake.api)->start(c, [](const CaptureRecord &) {}),
              BackendError::invalid_config);
    c = config();
    c.batch_size = 0;
    EXPECT_EQ(make_windivert_backend(&fake.api)->start(c, [](const CaptureRecord &) {}),
              BackendError::invalid_config);
}
