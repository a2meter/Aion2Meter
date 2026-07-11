#include <cstdint>
#include <string>
#include <utility>
#include <variant>
#include <vector>

#include <gtest/gtest.h>

#include "capture_record.hpp"
#include "sequence.hpp"

namespace {

using namter::FlowConfig;
using namter::FlowTracker;
using namter::GapObserved;
using namter::StreamChunk;
using namter::StreamOutput;
using namter::StreamReset;
using namter::StreamResetReason;
using namter::TcpSegment;

constexpr namter::FlowTuple flow_a{
    .source_address = 0x0a000001,
    .destination_address = 0x0a000002,
    .source_port = 13'328,
    .destination_port = 8'735,
};

TcpSegment seg(
    uint32_t sequence,
    std::string payload = {},
    uint64_t timestamp_ns = 1,
    uint8_t flags = namter::tcp_ack,
    namter::FlowTuple flow = flow_a) {
    return {
        .flow = flow,
        .sequence = sequence,
        .flags = flags,
        .payload = std::vector<uint8_t>(payload.begin(), payload.end()),
        .provenance = {.timestamp_ns = timestamp_ns},
    };
}

FlowConfig config(
    size_t max_ooo_bytes = 1'024,
    uint64_t idle_timeout_ns = 1'000,
    uint64_t gap_timeout_ns = 100) {
    return {
        .max_live_flows = 8,
        .max_out_of_order_bytes_per_flow = max_ooo_bytes,
        .idle_timeout_ns = idle_timeout_ns,
        .gap_timeout_ns = gap_timeout_ns,
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

std::vector<uint64_t> chunk_epochs(const std::vector<StreamOutput>& outputs) {
    std::vector<uint64_t> result;
    for (const auto& output : outputs) {
        if (const auto* chunk = std::get_if<StreamChunk>(&output)) {
            result.push_back(chunk->epoch);
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

struct OverlapCase {
    std::vector<TcpSegment> segments;
    std::string expected;
    uint64_t duplicate_bytes;
};

class TcpReassemblerOverlapTest : public testing::TestWithParam<OverlapCase> {};

TEST_P(TcpReassemblerOverlapTest, TrimsDuplicateBytesBeforeEmission) {
    FlowTracker tracker(config());
    std::string actual;
    for (const auto& segment : GetParam().segments) {
        actual += bytes_from(tracker.process(segment));
    }

    EXPECT_EQ(actual, GetParam().expected);
    EXPECT_EQ(tracker.diagnostics().overlaps, 1u);
    EXPECT_EQ(tracker.diagnostics().duplicate_bytes_removed, GetParam().duplicate_bytes);
}

INSTANTIATE_TEST_SUITE_P(
    OverlapCases,
    TcpReassemblerOverlapTest,
    testing::Values(
        OverlapCase{{seg(100, "abcdef"), seg(102, "cdef")}, "abcdef", 4},
        OverlapCase{{seg(100, "abcdef"), seg(104, "efgh")}, "abcdefgh", 2},
        OverlapCase{
            {seg(100, "ab"), seg(104, "efgh"), seg(102, "cdef")},
            "abcdefgh",
            2}));

TEST(TcpReassembler, EmitsInOrderDataAndAckOnlyDoesNotAdvanceSequence) {
    FlowTracker tracker(config());
    EXPECT_TRUE(tracker.process(seg(100)).empty());
    const auto first = tracker.process(seg(100, "abc"));
    const auto second = tracker.process(seg(103, "def"));

    EXPECT_EQ(bytes_from(first), "abc");
    EXPECT_EQ(bytes_from(second), "def");
    ASSERT_EQ(std::get<StreamChunk>(first.front()).sequence, 100u);
    EXPECT_EQ(tracker.diagnostics().accepted_bytes, 6u);
}

TEST(TcpReassembler, RemovesAFullRetransmission) {
    FlowTracker tracker(config());
    EXPECT_EQ(bytes_from(tracker.process(seg(100, "abcdef"))), "abcdef");
    EXPECT_TRUE(bytes_from(tracker.process(seg(100, "abcdef"))).empty());
    EXPECT_EQ(tracker.diagnostics().overlaps, 1u);
    EXPECT_EQ(tracker.diagnostics().duplicate_bytes_removed, 6u);
}

TEST(TcpReassembler, BuffersOutOfOrderDataUntilTheGapFills) {
    FlowTracker tracker(config());
    EXPECT_EQ(bytes_from(tracker.process(seg(100, "abc", 1))), "abc");
    EXPECT_TRUE(tracker.process(seg(106, "ghi", 2)).empty());
    EXPECT_EQ(tracker.buffered_bytes(flow_a), 3u);

    const auto outputs = tracker.process(seg(103, "def", 3));
    EXPECT_EQ(bytes_from(outputs), "defghi");
    EXPECT_EQ(tracker.buffered_bytes(flow_a), 0u);
    EXPECT_EQ(tracker.diagnostics().unresolved_byte_gaps, 0u);
}

TEST(TcpReassembler, GapExpiryResetsBeforeStartingAtTheNextObservedRange) {
    FlowTracker tracker(config(1'024, 1'000, 10));
    const auto initial = tracker.process(seg(100, "abc", 1));
    EXPECT_TRUE(tracker.process(seg(106, "ghi", 2)).empty());

    const auto outputs = tracker.expire(12);
    ASSERT_GE(outputs.size(), 3u);
    ASSERT_TRUE(std::holds_alternative<GapObserved>(outputs[0]));
    EXPECT_EQ(std::get<GapObserved>(outputs[0]).expected_sequence, 103u);
    EXPECT_EQ(std::get<GapObserved>(outputs[0]).next_sequence, 106u);
    ASSERT_TRUE(std::holds_alternative<StreamReset>(outputs[1]));
    EXPECT_EQ(std::get<StreamReset>(outputs[1]).reason, StreamResetReason::gap_expiry);
    EXPECT_EQ(bytes_from(outputs), "ghi");
    EXPECT_NE(chunk_epochs(initial).front(), chunk_epochs(outputs).front());
    EXPECT_EQ(tracker.diagnostics().unresolved_byte_gaps, 1u);
}

TEST(TcpReassembler, SequenceWrapRemainsContiguous) {
    FlowTracker tracker(config());
    const std::string first = "0123456789abcdef";
    EXPECT_EQ(bytes_from(tracker.process(seg(0xfffffff8u, first))), first);
    const auto outputs = tracker.process(seg(0x00000008u, "ghij"));
    EXPECT_EQ(bytes_from(outputs), "ghij");
    EXPECT_EQ(std::get<StreamChunk>(outputs.front()).sequence, 0x00000008u);
}

TEST(TcpReassembler, SynAndFinConsumeSequenceSpaceButAckDoesNot) {
    FlowTracker tracker(config());
    EXPECT_TRUE(tracker.process(seg(100, {}, 1, namter::tcp_syn)).empty());
    const auto data = tracker.process(seg(101, "a", 2));
    EXPECT_EQ(bytes_from(data), "a");
    EXPECT_EQ(std::get<StreamChunk>(data.front()).sequence, 101u);
    EXPECT_TRUE(tracker.process(seg(999, {}, 3, namter::tcp_ack)).empty());

    const auto closed = tracker.process(seg(102, {}, 4, namter::tcp_fin));
    EXPECT_TRUE(has_reset(closed, StreamResetReason::fin));
    EXPECT_EQ(tracker.live_flow_count(), 0u);
}

TEST(TcpReassembler, PerFlowBufferCapResetsInsteadOfConcatenatingAcrossTheGap) {
    FlowTracker tracker(config(4));
    const auto initial = tracker.process(seg(100, "abc", 1));
    const auto outputs = tracker.process(seg(110, "12345", 2));

    EXPECT_TRUE(has_reset(outputs, StreamResetReason::buffer_limit));
    EXPECT_EQ(bytes_from(outputs), "12345");
    EXPECT_NE(chunk_epochs(initial).front(), chunk_epochs(outputs).front());
    EXPECT_EQ(tracker.buffered_bytes(flow_a), 0u);
    EXPECT_EQ(tracker.diagnostics().unresolved_byte_gaps, 1u);
}

TEST(SerialArithmetic, UsesSignedRfcStyleDistanceAndUnwrapsAcrossZero) {
    EXPECT_EQ(namter::sequence_distance(0xfffffff8u, 0x00000008u), 16);
    EXPECT_EQ(namter::sequence_distance(0x00000008u, 0xfffffff8u), -16);
    EXPECT_EQ(namter::unwrap_sequence(0x00000008u, 0xfffffff8ll), 0x100000008ll);
}

}  // namespace
