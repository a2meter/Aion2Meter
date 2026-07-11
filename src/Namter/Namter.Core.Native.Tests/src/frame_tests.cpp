#include "capture_record.hpp"
#include "varint.hpp"

#include <gtest/gtest.h>

#include <cstddef>
#include <cstdint>
#include <span>
#include <variant>
#include <vector>

namespace {

using namter::FrameConfig;
using namter::FrameDiagnostic;
using namter::FrameDiagnosticCode;
using namter::FrameOutput;
using namter::FrameState;
using namter::IncrementalFramer;
using namter::ProtocolMessage;
using namter::StreamChunk;
using namter::StreamReset;
using namter::StreamResetReason;

constexpr namter::FlowTuple flow{0x01020304u, 0x05060708u, 13328u, 40000u};

std::vector<uint8_t> encode_varint(uint32_t value) {
    std::vector<uint8_t> result;
    do {
        auto byte = static_cast<uint8_t>(value & 0x7fu);
        value >>= 7u;
        if (value != 0) {
            byte = static_cast<uint8_t>(byte | 0x80u);
        }
        result.push_back(byte);
    } while (value != 0);
    return result;
}

std::vector<uint8_t> make_frame(
    std::span<const uint8_t> payload,
    uint8_t marker = 0) {
    const auto body_size = static_cast<uint32_t>(payload.size() + (marker == 0 ? 0u : 1u));
    auto result = encode_varint(body_size + 4u);
    if (marker != 0) {
        result.push_back(marker);
    }
    result.insert(result.end(), payload.begin(), payload.end());
    return result;
}

StreamChunk chunk(
    std::span<const uint8_t> bytes,
    uint64_t timestamp_ns,
    uint64_t epoch = 7) {
    return StreamChunk{
        .flow = flow,
        .epoch = epoch,
        .sequence = 0,
        .bytes = std::vector<uint8_t>(bytes.begin(), bytes.end()),
        .provenance = namter::CaptureProvenance{
            .source = namter::CaptureSource::pcap,
            .timestamp_ns = timestamp_ns,
        },
    };
}

std::vector<ProtocolMessage> messages(const std::vector<FrameOutput>& outputs) {
    std::vector<ProtocolMessage> result;
    for (const auto& output : outputs) {
        if (const auto* message = std::get_if<ProtocolMessage>(&output)) {
            result.push_back(*message);
        }
    }
    return result;
}

std::vector<FrameDiagnostic> diagnostics(const std::vector<FrameOutput>& outputs) {
    std::vector<FrameDiagnostic> result;
    for (const auto& output : outputs) {
        if (const auto* diagnostic = std::get_if<FrameDiagnostic>(&output)) {
            result.push_back(*diagnostic);
        }
    }
    return result;
}

std::vector<FrameOutput> append(
    std::vector<FrameOutput> destination,
    std::vector<FrameOutput> source) {
    destination.insert(
        destination.end(),
        std::make_move_iterator(source.begin()),
        std::make_move_iterator(source.end()));
    return destination;
}

TEST(Varint, DistinguishesCompleteIncompleteAndOverlongInputs) {
    const uint8_t complete[]{0xac, 0x02};
    const uint8_t incomplete[]{0xac};
    const uint8_t overlong[]{0x80, 0x80, 0x80, 0x80, 0x80};
    const uint8_t overflowing_fifth[]{0xff, 0xff, 0xff, 0xff, 0x10};

    const auto decoded = namter::decode_u32_varint(complete);
    EXPECT_EQ(decoded.status, namter::VarintStatus::complete);
    EXPECT_EQ(decoded.value, 300u);
    EXPECT_EQ(decoded.bytes_consumed, 2u);
    EXPECT_EQ(namter::decode_u32_varint(incomplete).status, namter::VarintStatus::incomplete);
    EXPECT_EQ(namter::decode_u32_varint(overlong).status, namter::VarintStatus::invalid);
    EXPECT_EQ(namter::decode_u32_varint(overflowing_fifth).status, namter::VarintStatus::invalid);
}

TEST(IncrementalFramer, EmitsTheSameMessageAtEverySingleSplitBoundary) {
    const std::vector<uint8_t> payload{0x06, 0x00, 0x36, 0xaa, 0xbb, 0xcc};
    const auto frame = make_frame(payload);

    for (size_t split = 0; split <= frame.size(); ++split) {
        IncrementalFramer framer(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 4096});
        auto outputs = framer.process(chunk(std::span(frame).first(split), 100));
        outputs = append(
            std::move(outputs),
            framer.process(chunk(std::span(frame).subspan(split), 200)));

        const auto actual = messages(outputs);
        ASSERT_EQ(actual.size(), 1u) << "split=" << split;
        EXPECT_EQ(actual.front().bytes, frame) << "split=" << split;
        EXPECT_EQ(actual.front().first_timestamp_ns, split == 0 ? 200u : 100u);
        EXPECT_EQ(actual.front().last_timestamp_ns, split == frame.size() ? 100u : 200u);
        EXPECT_TRUE(diagnostics(outputs).empty());
    }
}

TEST(IncrementalFramer, AcceptsOneByteChunksForEveryByteOfAFrame) {
    const auto frame = make_frame(std::vector<uint8_t>{0x06, 0x00, 0x36, 0x10, 0x20});
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 4096});
    std::vector<FrameOutput> outputs;

    for (size_t index = 0; index < frame.size(); ++index) {
        outputs = append(
            std::move(outputs),
            framer.process(chunk(std::span(frame).subspan(index, 1), 1000u + index)));
    }

    const auto actual = messages(outputs);
    ASSERT_EQ(actual.size(), 1u);
    EXPECT_EQ(actual.front().bytes, frame);
    EXPECT_EQ(actual.front().first_timestamp_ns, 1000u);
    EXPECT_EQ(actual.front().last_timestamp_ns, 1000u + frame.size() - 1u);
}

TEST(IncrementalFramer, EmitsAllCoalescedMessagesInWireOrder) {
    const auto first = make_frame(std::vector<uint8_t>{0x01, 0x02});
    const auto second = make_frame(std::vector<uint8_t>{0x03, 0x04, 0x05});
    auto bytes = first;
    bytes.insert(bytes.end(), second.begin(), second.end());
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 4096});

    const auto actual = messages(framer.process(chunk(bytes, 55)));

    ASSERT_EQ(actual.size(), 2u);
    EXPECT_EQ(actual[0].bytes, first);
    EXPECT_EQ(actual[1].bytes, second);
}

TEST(IncrementalFramer, PreservesEveryOptionalMarkerFromF0ThroughFe) {
    for (uint16_t marker = 0xf0; marker <= 0xfe; ++marker) {
        const auto frame = make_frame(
            std::vector<uint8_t>{0x06, 0x00, 0x36},
            static_cast<uint8_t>(marker));
        IncrementalFramer framer(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 4096});

        const auto actual = messages(framer.process(chunk(frame, marker)));

        ASSERT_EQ(actual.size(), 1u) << "marker=" << marker;
        EXPECT_EQ(actual.front().bytes, frame);
    }
}

TEST(IncrementalFramer, RejectsOverlongAndOversizedLengthsWithoutBodyAllocation) {
    IncrementalFramer overlong(FrameConfig{.max_frame_bytes = 32, .max_decompressed_bytes = 64});
    const std::vector<uint8_t> overlong_bytes{0x80, 0x80, 0x80, 0x80, 0x80};
    const auto overlong_outputs = overlong.process(chunk(overlong_bytes, 1));
    ASSERT_EQ(diagnostics(overlong_outputs).size(), 1u);
    EXPECT_EQ(diagnostics(overlong_outputs).front().code, FrameDiagnosticCode::overlong_varint);
    EXPECT_EQ(overlong.buffered_bytes(), 0u);
    EXPECT_EQ(overlong.state(), FrameState::need_resync);

    IncrementalFramer oversized(FrameConfig{.max_frame_bytes = 32, .max_decompressed_bytes = 64});
    const auto oversized_outputs = oversized.process(chunk(encode_varint(4096), 2));
    ASSERT_EQ(diagnostics(oversized_outputs).size(), 1u);
    EXPECT_EQ(diagnostics(oversized_outputs).front().code, FrameDiagnosticCode::frame_too_large);
    EXPECT_EQ(oversized.buffered_bytes(), 0u);
}

TEST(IncrementalFramer, RejectsDeclaredLengthsSmallerThanTheProtocolBias) {
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 32, .max_decompressed_bytes = 64});

    const auto outputs = framer.process(chunk(std::vector<uint8_t>{0x03}, 8));

    ASSERT_EQ(diagnostics(outputs).size(), 1u);
    EXPECT_EQ(diagnostics(outputs).front().code, FrameDiagnosticCode::invalid_frame_length);
    EXPECT_EQ(framer.buffered_bytes(), 0u);
}

TEST(IncrementalFramer, ReportsEachRetainedPrefixAndBodyByteExactlyOnce) {
    const auto frame = make_frame(std::vector<uint8_t>{0x01, 0x02, 0x03, 0x04});
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 32, .max_decompressed_bytes = 64});

    EXPECT_TRUE(framer.process(chunk(std::span(frame).first(2), 1)).empty());

    EXPECT_EQ(framer.state(), FrameState::need_body);
    EXPECT_EQ(framer.buffered_bytes(), 2u);
}

TEST(IncrementalFramer, AStreamResetDiscardsPartialStateAndStartsAtTheNewEpoch) {
    const auto abandoned = make_frame(std::vector<uint8_t>{0x01, 0x02, 0x03});
    const auto replacement = make_frame(std::vector<uint8_t>{0x06, 0x00, 0x36});
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 4096});
    EXPECT_TRUE(framer.process(chunk(std::span(abandoned).first(2), 10, 7)).empty());

    const auto reset_outputs = framer.process(StreamReset{
        .flow = flow,
        .epoch = 7,
        .reason = StreamResetReason::gap_expiry,
        .timestamp_ns = 20,
    });
    EXPECT_TRUE(reset_outputs.empty());
    const auto actual = messages(framer.process(chunk(replacement, 30, 8)));

    ASSERT_EQ(actual.size(), 1u);
    EXPECT_EQ(actual.front().epoch, 8u);
    EXPECT_EQ(actual.front().bytes, replacement);
    EXPECT_EQ(actual.front().first_timestamp_ns, 30u);
}

TEST(IncrementalFramer, ResynchronizesOnlyToACompleteValidatedFrameBoundary) {
    const auto valid = make_frame(std::vector<uint8_t>{0x06, 0x00, 0x36, 0x99});
    std::vector<uint8_t> corrupt{0x80, 0x80, 0x80, 0x80, 0x80, 0x06, 0x00, 0x36};
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 64, .max_decompressed_bytes = 128});

    const auto first = framer.process(chunk(corrupt, 1));
    EXPECT_TRUE(messages(first).empty());
    ASSERT_EQ(diagnostics(first).size(), 1u);
    EXPECT_EQ(framer.state(), FrameState::need_resync);

    const auto second = framer.process(chunk(valid, 2));
    const auto actual = messages(second);
    ASSERT_EQ(actual.size(), 1u);
    EXPECT_EQ(actual.front().bytes, valid);
}

int LLVMFuzzerTestOneInputVarint(const uint8_t* data, size_t size) {
    (void)namter::decode_u32_varint(std::span<const uint8_t>(data, size));
    return 0;
}

int LLVMFuzzerTestOneInputFrame(const uint8_t* data, size_t size) {
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 64, .max_decompressed_bytes = 128});
    (void)framer.process(chunk(std::span<const uint8_t>(data, size), 1));
    return 0;
}

TEST(FrameCorpus, FuzzEntryPointsAcceptHostileInputsWithinConfiguredBounds) {
    const std::vector<std::vector<uint8_t>> corpus{
        {},
        {0x80},
        {0xff, 0xff, 0xff, 0xff, 0xff},
        {0x04},
        {0x84, 0x80, 0x80, 0x80, 0x00},
        {0x06, 0x00, 0x36},
    };
    for (const auto& input : corpus) {
        EXPECT_EQ(LLVMFuzzerTestOneInputVarint(input.data(), input.size()), 0);
        EXPECT_EQ(LLVMFuzzerTestOneInputFrame(input.data(), input.size()), 0);
    }
}

}  // namespace
