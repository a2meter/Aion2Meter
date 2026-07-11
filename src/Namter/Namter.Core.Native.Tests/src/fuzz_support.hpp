#pragma once

#include "frame.hpp"
#include "varint.hpp"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace namter::fuzz_support {

inline StreamChunk make_chunk(std::span<const uint8_t> input) {
    return StreamChunk{
        .epoch = 1,
        .bytes = std::vector<uint8_t>(input.begin(), input.end()),
        .provenance = CaptureProvenance{.source = CaptureSource::pcap, .timestamp_ns = 1},
    };
}

inline int fuzz_varint(const uint8_t* data, size_t size) {
    (void)decode_u32_varint(std::span<const uint8_t>(data, std::min(size, size_t{6})));
    return 0;
}

inline int fuzz_frame(const uint8_t* data, size_t size) {
    constexpr size_t input_limit = 65536u;
    const auto input = std::span<const uint8_t>(data, std::min(size, input_limit));
    IncrementalFramer framer(FrameConfig{
        .max_frame_bytes = input_limit,
        .max_decompressed_bytes = input_limit,
    });
    (void)framer.process(make_chunk(input));
    return 0;
}

inline int fuzz_lz4(const uint8_t* data, size_t size) {
    constexpr size_t input_limit = 65528u;
    const auto input = std::span<const uint8_t>(data, std::min(size, input_limit));
    std::vector<uint8_t> frame;
    const uint32_t declared = static_cast<uint32_t>(input.size()) + 6u;
    uint32_t value = declared;
    do {
        auto byte = static_cast<uint8_t>(value & 0x7fu);
        value >>= 7u;
        if (value != 0) {
            byte = static_cast<uint8_t>(byte | 0x80u);
        }
        frame.push_back(byte);
    } while (value != 0);
    frame.push_back(0xff);
    frame.push_back(0xff);
    frame.insert(frame.end(), input.begin(), input.end());

    IncrementalFramer framer(FrameConfig{
        .max_frame_bytes = 65536u,
        .max_decompressed_bytes = 65536u,
    });
    (void)framer.process(make_chunk(frame));
    return 0;
}

}  // namespace namter::fuzz_support
