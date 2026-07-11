#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <limits>
#include <memory>
#include <string>
#include <variant>
#include <vector>

#include <gtest/gtest.h>

#include "capture_record.hpp"

namespace {

using namter::CaptureError;
using namter::CaptureRecord;
using namter::FlowConfig;
using namter::FlowTracker;
using namter::FlowTuple;
using namter::PacketNormalizer;
using namter::PcapReader;
using namter::StreamChunk;
using namter::StreamOutput;
using namter::StreamReset;
using namter::StreamResetReason;
using namter::TcpSegment;

constexpr FlowTuple flow_a{0x0a000001, 0x0a000002, 13'328, 8'735};
constexpr FlowTuple flow_b{0x0a000003, 0x0a000004, 13'328, 12'415};

TcpSegment seg(
    FlowTuple flow,
    uint32_t sequence,
    std::string payload = {},
    uint64_t timestamp_ns = 1,
    uint8_t flags = namter::tcp_ack) {
    return {
        .flow = flow,
        .sequence = sequence,
        .flags = flags,
        .payload = std::vector<uint8_t>(payload.begin(), payload.end()),
        .provenance = {.timestamp_ns = timestamp_ns},
    };
}

FlowConfig config(
    size_t max_flows = 8,
    size_t max_ooo = 1'024,
    uint64_t idle_timeout = 100,
    uint64_t gap_timeout = 50) {
    return {
        .max_live_flows = max_flows,
        .max_out_of_order_bytes_per_flow = max_ooo,
        .idle_timeout_ns = idle_timeout,
        .gap_timeout_ns = gap_timeout,
    };
}

std::string bytes_from(const std::vector<StreamOutput>& outputs) {
    std::string result;
    for (const auto& output : outputs) {
        if (const auto* chunk = std::get_if<StreamChunk>(&output)) {
            result.append(chunk->bytes.begin(), chunk->bytes.end());
        }
    }
    return result;
}

bool has_reset(const std::vector<StreamOutput>& outputs, StreamResetReason reason) {
    for (const auto& output : outputs) {
        if (const auto* reset = std::get_if<StreamReset>(&output)) {
            if (reset->reason == reason) {
                return true;
            }
        }
    }
    return false;
}

std::filesystem::path fixture_path() {
    char* value = nullptr;
    size_t size = 0;
    if (_dupenv_s(&value, &size, "NAMTER_FIXTURE_ROOT") != 0 || value == nullptr) {
        return {};
    }
    const std::unique_ptr<char, decltype(&std::free)> root(value, &std::free);
    return std::filesystem::path(root.get()) / "aion2_part001.pcap";
}

TEST(FlowTracker, StartsInboundOnlyMidstreamAndKeepsConcurrentFlowsIsolated) {
    FlowTracker tracker(config());
    EXPECT_EQ(bytes_from(tracker.process(seg(flow_a, 50'000, "alpha", 1))), "alpha");
    EXPECT_EQ(bytes_from(tracker.process(seg(flow_b, 900'000, "bravo", 2))), "bravo");
    EXPECT_EQ(bytes_from(tracker.process(seg(flow_a, 50'005, "-a", 3))), "-a");
    EXPECT_EQ(bytes_from(tracker.process(seg(flow_b, 900'005, "-b", 4))), "-b");
    EXPECT_EQ(tracker.live_flow_count(), 2u);
    EXPECT_EQ(tracker.diagnostics().flows_started, 2u);
    EXPECT_EQ(tracker.diagnostics().epochs_started, 2u);
}

TEST(FlowTracker, UsesCaptureTimeForIdleExpiry) {
    FlowTracker tracker(config(8, 1'024, 50, 1'000));
    (void)tracker.process(seg(flow_a, 100, "abc", 1'000));
    EXPECT_TRUE(tracker.expire(1'049).empty());
    EXPECT_EQ(tracker.live_flow_count(), 1u);

    const auto outputs = tracker.expire(1'050);
    EXPECT_TRUE(has_reset(outputs, StreamResetReason::idle_expiry));
    EXPECT_EQ(tracker.live_flow_count(), 0u);
}

TEST(FlowTracker, RstClosesOnlyItsFlowEpoch) {
    FlowTracker tracker(config());
    (void)tracker.process(seg(flow_a, 100, "a", 1));
    (void)tracker.process(seg(flow_b, 200, "b", 2));
    const auto outputs = tracker.process(seg(flow_a, 101, {}, 3, namter::tcp_rst));

    EXPECT_TRUE(has_reset(outputs, StreamResetReason::rst));
    EXPECT_EQ(tracker.live_flow_count(), 1u);
    EXPECT_EQ(bytes_from(tracker.process(seg(flow_b, 201, "c", 4))), "c");
}

TEST(FlowTracker, SynOnAnActiveTupleStartsANewEpoch) {
    FlowTracker tracker(config());
    const auto first = tracker.process(seg(flow_a, 100, "old", 1));
    const auto second = tracker.process(seg(
        flow_a,
        500,
        "new",
        2,
        static_cast<uint8_t>(namter::tcp_syn | namter::tcp_ack)));

    EXPECT_TRUE(has_reset(second, StreamResetReason::tuple_reuse));
    EXPECT_EQ(bytes_from(second), "new");
    EXPECT_NE(std::get<StreamChunk>(first.front()).epoch, std::get<StreamChunk>(second.back()).epoch);
    EXPECT_EQ(std::get<StreamChunk>(second.back()).sequence, 501u);
}

TEST(FlowTracker, SameIsnSynRetransmissionsStayInOneEpochAndDedupePayload) {
    FlowTracker tracker(config(8, 2));
    EXPECT_TRUE(tracker.process(seg(flow_a, 100, {}, 1, namter::tcp_syn)).empty());
    const auto repeated_syn = tracker.process(seg(flow_a, 100, {}, 2, namter::tcp_syn));
    EXPECT_FALSE(has_reset(repeated_syn, StreamResetReason::tuple_reuse));
    EXPECT_EQ(tracker.diagnostics().epochs_started, 1u);

    const auto with_payload = tracker.process(seg(
        flow_a,
        100,
        "abc",
        3,
        static_cast<uint8_t>(namter::tcp_syn | namter::tcp_ack)));
    ASSERT_EQ(bytes_from(with_payload), "abc");
    const auto payload_retransmission = tracker.process(seg(
        flow_a,
        100,
        "abc",
        4,
        static_cast<uint8_t>(namter::tcp_syn | namter::tcp_ack)));
    EXPECT_FALSE(has_reset(payload_retransmission, StreamResetReason::tuple_reuse));
    EXPECT_TRUE(bytes_from(payload_retransmission).empty());
    EXPECT_EQ(tracker.diagnostics().epochs_started, 1u);
    EXPECT_EQ(tracker.diagnostics().duplicate_bytes_removed, 3u);

    const auto changed_isn = tracker.process(seg(
        flow_a,
        500,
        "new",
        5,
        static_cast<uint8_t>(namter::tcp_syn | namter::tcp_ack)));
    EXPECT_TRUE(has_reset(changed_isn, StreamResetReason::tuple_reuse));
    EXPECT_EQ(bytes_from(changed_isn), "new");
    EXPECT_EQ(tracker.diagnostics().epochs_started, 2u);
}

TEST(FlowTracker, ReusingAClosedTupleNeverReusesItsEpochIdentifier) {
    FlowTracker tracker(config());
    const auto first = tracker.process(seg(
        flow_a,
        100,
        "old",
        1,
        static_cast<uint8_t>(namter::tcp_ack | namter::tcp_fin)));
    ASSERT_EQ(tracker.live_flow_count(), 0u);

    const auto second = tracker.process(seg(flow_a, 500, "new", 2));
    const auto first_epoch = std::get<StreamChunk>(first.front()).epoch;
    const auto second_epoch = std::get<StreamChunk>(second.front()).epoch;
    EXPECT_GT(second_epoch, first_epoch);
}

TEST(FlowTracker, RejectsANewFlowWhenTheConfiguredFlowCountIsFull) {
    FlowTracker tracker(config(1));
    (void)tracker.process(seg(flow_a, 100, "a", 1));
    const auto outputs = tracker.process(seg(flow_b, 200, "b", 2));

    EXPECT_TRUE(has_reset(outputs, StreamResetReason::flow_limit));
    EXPECT_TRUE(bytes_from(outputs).empty());
    EXPECT_EQ(tracker.live_flow_count(), 1u);
    EXPECT_EQ(tracker.diagnostics().flows_started, 1u);
    EXPECT_EQ(tracker.diagnostics().discarded_ranges, 1u);
}

TEST(FlowTracker, SuppliedFixtureHasExactOverlapGoldenWithoutUnresolvedGaps) {
    const auto path = fixture_path();
    if (path.empty() || !std::filesystem::is_regular_file(path)) {
        GTEST_SKIP() << "NAMTER_FIXTURE_ROOT does not contain aion2_part001.pcap";
    }

    std::ifstream input(path, std::ios::binary);
    ASSERT_TRUE(input.is_open());
    PcapReader reader(input);
    FlowTracker tracker(config(
        512,
        1024u * 1024u,
        std::numeric_limits<uint64_t>::max(),
        std::numeric_limits<uint64_t>::max()));

    CaptureRecord record;
    while (reader.read_next(record)) {
        const auto normalized = PacketNormalizer::normalize(record);
        ASSERT_EQ(normalized.error, CaptureError::none);
        ASSERT_TRUE(normalized.segment.has_value());
        (void)tracker.process(*normalized.segment);
    }

    ASSERT_EQ(reader.error(), CaptureError::none);
    EXPECT_EQ(tracker.diagnostics().flows_started, 2u);
    EXPECT_EQ(tracker.diagnostics().overlaps, 6u);
    EXPECT_EQ(tracker.diagnostics().duplicate_bytes_removed, 66u);
    EXPECT_EQ(tracker.diagnostics().unresolved_byte_gaps, 0u);
}

}  // namespace
