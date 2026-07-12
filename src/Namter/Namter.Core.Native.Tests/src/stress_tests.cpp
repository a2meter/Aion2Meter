#include <gtest/gtest.h>

#include "capture_pipeline.hpp"

#include <cstdint>
#include <utility>
#include <vector>

namespace {

namter::CaptureRecord packet(
    uint16_t source_port,
    uint32_t sequence,
    uint8_t flags,
    std::vector<uint8_t> payload,
    uint64_t timestamp_ns) {
    std::vector<uint8_t> bytes(40, 0);
    bytes[0] = 0x45;
    const auto total = static_cast<uint16_t>(bytes.size() + payload.size());
    bytes[2] = static_cast<uint8_t>(total >> 8u);
    bytes[3] = static_cast<uint8_t>(total);
    bytes[8] = 64;
    bytes[9] = 6;
    bytes[12] = 10;
    bytes[15] = 1;
    bytes[16] = 10;
    bytes[19] = 2;
    bytes[20] = static_cast<uint8_t>(source_port >> 8u);
    bytes[21] = static_cast<uint8_t>(source_port);
    bytes[22] = 0xc3;
    bytes[23] = 0x50;
    bytes[24] = static_cast<uint8_t>(sequence >> 24u);
    bytes[25] = static_cast<uint8_t>(sequence >> 16u);
    bytes[26] = static_cast<uint8_t>(sequence >> 8u);
    bytes[27] = static_cast<uint8_t>(sequence);
    bytes[32] = 0x50;
    bytes[33] = flags;
    bytes.insert(bytes.end(), payload.begin(), payload.end());
    return {
        .source = namter::CaptureSource::pcap,
        .timestamp_ns = timestamp_ns,
        .link_type = namter::dlt_raw,
        .captured_length = static_cast<uint32_t>(bytes.size()),
        .original_length = static_cast<uint32_t>(bytes.size()),
        .bytes = std::move(bytes),
    };
}

}  // namespace

TEST(CapturePipelineStress, SustainedFlowSprayNeverExceedsConfiguredFramerCount) {
    constexpr size_t flow_limit = 32u;
    constexpr size_t attempted_flows = 4096u;
    uint64_t diagnostics = 0;
    namter::CapturePipeline pipeline(
        {
            .flow = {
                .max_live_flows = flow_limit,
                .max_out_of_order_bytes_per_flow = 4096,
            },
            .frame = {
                .max_frame_bytes = 1024,
                .max_decompressed_bytes = 4096,
            },
        },
        {},
        [](const nm_event_v1&) {},
        [&](uint32_t, const char*) { ++diagnostics; });

    for (size_t index = 0; index < attempted_flows; ++index) {
        const auto source_port = static_cast<uint16_t>(10'000u + index);
        ASSERT_EQ(
            pipeline.ingest(packet(
                source_port,
                1,
                namter::tcp_ack,
                {0x80, 0x80, 0x80, 0x80, 0x80},
                index + 1u)),
            namter::CaptureError::none);
        ASSERT_LE(pipeline.active_framer_count(), flow_limit);
    }

    EXPECT_EQ(pipeline.active_framer_count(), flow_limit);
    EXPECT_GT(diagnostics, 0u);

    for (size_t index = 0; index < flow_limit; ++index) {
        const auto source_port = static_cast<uint16_t>(10'000u + index);
        ASSERT_EQ(
            pipeline.ingest(packet(
                source_port,
                6,
                namter::tcp_rst,
                {},
                attempted_flows + index + 1u)),
            namter::CaptureError::none);
    }
    pipeline.flush(attempted_flows + flow_limit + 1u);
    EXPECT_EQ(pipeline.active_framer_count(), 0u);
}
