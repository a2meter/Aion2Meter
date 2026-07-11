#include "capture_record.hpp"

#include <gtest/gtest.h>
#include <lz4.h>

#include <cstddef>
#include <cstdint>
#include <limits>
#include <span>
#include <variant>
#include <vector>

namespace {

using namter::FrameConfig;
using namter::FrameDiagnostic;
using namter::FrameDiagnosticCode;
using namter::FrameOutput;
using namter::IncrementalFramer;
using namter::ProtocolMessage;
using namter::StreamChunk;

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

std::vector<uint8_t> make_frame(std::span<const uint8_t> body) {
    auto result = encode_varint(static_cast<uint32_t>(body.size()) + 4u);
    result.insert(result.end(), body.begin(), body.end());
    return result;
}

void append_i32_le(std::vector<uint8_t>& bytes, int32_t value) {
    const auto unsigned_value = static_cast<uint32_t>(value);
    bytes.push_back(static_cast<uint8_t>(unsigned_value));
    bytes.push_back(static_cast<uint8_t>(unsigned_value >> 8u));
    bytes.push_back(static_cast<uint8_t>(unsigned_value >> 16u));
    bytes.push_back(static_cast<uint8_t>(unsigned_value >> 24u));
}

std::vector<uint8_t> compress(std::span<const uint8_t> bytes) {
    const auto source_size = static_cast<int>(bytes.size());
    std::vector<char> compressed(static_cast<size_t>(LZ4_compressBound(source_size)));
    const int written = LZ4_compress_default(
        reinterpret_cast<const char*>(bytes.data()),
        compressed.data(),
        source_size,
        static_cast<int>(compressed.size()));
    EXPECT_GT(written, 0);
    return std::vector<uint8_t>(
        reinterpret_cast<const uint8_t*>(compressed.data()),
        reinterpret_cast<const uint8_t*>(compressed.data()) + written);
}

std::vector<uint8_t> make_batch(
    std::span<const uint8_t> decompressed,
    int32_t declared_size,
    uint8_t marker = 0) {
    std::vector<uint8_t> body;
    if (marker != 0) {
        body.push_back(marker);
    }
    body.push_back(0xff);
    body.push_back(0xff);
    append_i32_le(body, declared_size);
    const auto compressed = compress(decompressed);
    body.insert(body.end(), compressed.begin(), compressed.end());
    return make_frame(body);
}

StreamChunk chunk(std::span<const uint8_t> bytes, uint64_t timestamp_ns) {
    return StreamChunk{
        .flow = flow,
        .epoch = 9,
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

TEST(Lz4Batch, ExpandsNestedMessagesWithOuterCaptureProvenance) {
    const auto first = make_frame(std::vector<uint8_t>{0x06, 0x00, 0x36, 0x01});
    const auto second = make_frame(std::vector<uint8_t>{0xf0, 0x06, 0x00, 0x36, 0x02});
    auto nested = first;
    nested.insert(nested.end(), second.begin(), second.end());
    const auto batch = make_batch(nested, static_cast<int32_t>(nested.size()), 0xf4);
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 4096});

    std::vector<FrameOutput> outputs;
    for (size_t index = 0; index < batch.size(); ++index) {
        auto next = framer.process(chunk(std::span(batch).subspan(index, 1), 100u + index));
        outputs.insert(
            outputs.end(),
            std::make_move_iterator(next.begin()),
            std::make_move_iterator(next.end()));
    }

    const auto actual = messages(outputs);
    ASSERT_EQ(actual.size(), 2u);
    EXPECT_EQ(actual[0].bytes, first);
    EXPECT_EQ(actual[1].bytes, second);
    EXPECT_EQ(actual[0].first_timestamp_ns, 100u);
    EXPECT_EQ(actual[0].last_timestamp_ns, 100u + batch.size() - 1u);
    EXPECT_EQ(actual[1].first_timestamp_ns, 100u);
    EXPECT_EQ(actual[1].last_timestamp_ns, 100u + batch.size() - 1u);
    EXPECT_TRUE(diagnostics(outputs).empty());
}

TEST(Lz4Batch, RejectsShortHeadersWithOneStructuredDiagnostic) {
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 4096});
    const auto short_batch = make_frame(std::vector<uint8_t>{0xff, 0xff, 0x01, 0x02, 0x03});

    const auto outputs = framer.process(chunk(short_batch, 1));

    ASSERT_EQ(diagnostics(outputs).size(), 1u);
    EXPECT_EQ(diagnostics(outputs).front().code, FrameDiagnosticCode::truncated_lz4_header);
    EXPECT_TRUE(messages(outputs).empty());
}

TEST(Lz4Batch, RejectsNegativeAndOversizedDeclaredOutputBeforeAllocation) {
    const std::vector<uint8_t> nested{0x04};

    IncrementalFramer negative(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 64});
    const auto negative_outputs = negative.process(chunk(make_batch(nested, -1), 1));
    ASSERT_EQ(diagnostics(negative_outputs).size(), 1u);
    EXPECT_EQ(diagnostics(negative_outputs).front().code, FrameDiagnosticCode::invalid_decompressed_size);
    EXPECT_EQ(negative.buffered_bytes(), 0u);
    EXPECT_EQ(negative.state(), namter::FrameState::need_resync);

    IncrementalFramer oversized(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 64});
    const auto oversized_outputs = oversized.process(chunk(make_batch(nested, 65), 1));
    ASSERT_EQ(diagnostics(oversized_outputs).size(), 1u);
    EXPECT_EQ(diagnostics(oversized_outputs).front().code, FrameDiagnosticCode::decompressed_size_too_large);
    EXPECT_EQ(oversized.buffered_bytes(), 0u);
}

TEST(Lz4Batch, RequiresExactDecompressedSizeMatch) {
    const auto nested = make_frame(std::vector<uint8_t>{0x06, 0x00, 0x36});
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 4096});
    const auto batch = make_batch(nested, static_cast<int32_t>(nested.size() + 1u));

    const auto outputs = framer.process(chunk(batch, 1));

    ASSERT_EQ(diagnostics(outputs).size(), 1u);
    EXPECT_EQ(diagnostics(outputs).front().code, FrameDiagnosticCode::lz4_decompression_failed);
    EXPECT_TRUE(messages(outputs).empty());
}

TEST(Lz4Batch, RejectsCorruptCompressedBlocks) {
    std::vector<uint8_t> body{0xff, 0xff};
    append_i32_le(body, 16);
    body.insert(body.end(), {0xff, 0xff, 0xff, 0xff});
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 4096});

    const auto outputs = framer.process(chunk(make_frame(body), 1));

    ASSERT_EQ(diagnostics(outputs).size(), 1u);
    EXPECT_EQ(diagnostics(outputs).front().code, FrameDiagnosticCode::lz4_decompression_failed);
}

TEST(Lz4Batch, RejectsMalformedNestedFramesAsOneBatchDiagnostic) {
    const std::vector<uint8_t> invalid_nested{0x80, 0x80, 0x80, 0x80, 0x80};
    const auto batch = make_batch(invalid_nested, static_cast<int32_t>(invalid_nested.size()));
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 1024, .max_decompressed_bytes = 4096});

    const auto outputs = framer.process(chunk(batch, 1));

    ASSERT_EQ(diagnostics(outputs).size(), 1u);
    EXPECT_EQ(diagnostics(outputs).front().code, FrameDiagnosticCode::invalid_nested_frame);
    EXPECT_TRUE(messages(outputs).empty());
}

int LLVMFuzzerTestOneInputLz4(const uint8_t* data, size_t size) {
    IncrementalFramer framer(FrameConfig{.max_frame_bytes = 64, .max_decompressed_bytes = 128});
    (void)framer.process(chunk(std::span<const uint8_t>(data, size), 1));
    return 0;
}

TEST(Lz4Corpus, FuzzEntryPointAcceptsMalformedContainersWithinConfiguredBounds) {
    const std::vector<std::vector<uint8_t>> corpus{
        {},
        {0x0a, 0xff, 0xff},
        {0x0f, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff},
        {0x0e, 0xf0, 0xff, 0xff, 0x40, 0x00, 0x00, 0x00, 0xff},
    };
    for (const auto& input : corpus) {
        EXPECT_EQ(LLVMFuzzerTestOneInputLz4(input.data(), input.size()), 0);
    }
}

}  // namespace
