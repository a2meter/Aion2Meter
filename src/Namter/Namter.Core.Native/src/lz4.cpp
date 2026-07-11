#include "capture_record.hpp"
#include "varint.hpp"

#include <lz4.h>

#include <bit>
#include <climits>
#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace namter::detail {
namespace {

int32_t read_i32_le(std::span<const uint8_t> bytes) noexcept {
    const uint32_t value = static_cast<uint32_t>(bytes[0]) |
                           (static_cast<uint32_t>(bytes[1]) << 8u) |
                           (static_cast<uint32_t>(bytes[2]) << 16u) |
                           (static_cast<uint32_t>(bytes[3]) << 24u);
    return std::bit_cast<int32_t>(value);
}

FrameDiagnosticCode split_nested_frames(
    std::span<const uint8_t> bytes,
    size_t max_frame_bytes,
    std::vector<std::vector<uint8_t>>& nested_frames) {
    size_t offset = 0;
    while (offset < bytes.size()) {
        while (offset < bytes.size() && bytes[offset] == 0) {
            ++offset;
        }
        if (offset == bytes.size()) {
            return FrameDiagnosticCode::none;
        }

        const auto decoded = decode_u32_varint(bytes.subspan(offset));
        if (decoded.status != VarintStatus::complete || decoded.value < 4u) {
            return FrameDiagnosticCode::invalid_nested_frame;
        }

        const size_t body_size = static_cast<size_t>(decoded.value - 4u);
        if (body_size > max_frame_bytes ||
            decoded.bytes_consumed > max_frame_bytes - body_size) {
            return FrameDiagnosticCode::invalid_nested_frame;
        }
        const size_t frame_size = decoded.bytes_consumed + body_size;
        if (frame_size > bytes.size() - offset) {
            return FrameDiagnosticCode::invalid_nested_frame;
        }

        const auto frame = bytes.subspan(offset, frame_size);
        nested_frames.emplace_back(frame.begin(), frame.end());
        offset += frame_size;
    }
    return FrameDiagnosticCode::none;
}

}  // namespace

FrameDiagnosticCode expand_lz4_batch(
    std::span<const uint8_t> body,
    size_t max_frame_bytes,
    size_t max_decompressed_bytes,
    std::vector<std::vector<uint8_t>>& nested_frames) {
    nested_frames.clear();
    if (body.size() < 6u) {
        return FrameDiagnosticCode::truncated_lz4_header;
    }
    if (body[0] != 0xffu || body[1] != 0xffu) {
        return FrameDiagnosticCode::invalid_marker;
    }

    const int32_t declared_size = read_i32_le(body.subspan(2u, 4u));
    if (declared_size <= 0) {
        return FrameDiagnosticCode::invalid_decompressed_size;
    }
    const size_t output_size = static_cast<size_t>(declared_size);
    if (output_size > max_decompressed_bytes) {
        return FrameDiagnosticCode::decompressed_size_too_large;
    }

    const auto compressed = body.subspan(6u);
    if (compressed.empty() || compressed.size() > static_cast<size_t>(INT_MAX)) {
        return FrameDiagnosticCode::lz4_decompression_failed;
    }

    std::vector<uint8_t> output(output_size);
    const int written = LZ4_decompress_safe(
        reinterpret_cast<const char*>(compressed.data()),
        reinterpret_cast<char*>(output.data()),
        static_cast<int>(compressed.size()),
        declared_size);
    if (written != declared_size) {
        return FrameDiagnosticCode::lz4_decompression_failed;
    }

    const auto error = split_nested_frames(output, max_frame_bytes, nested_frames);
    if (error != FrameDiagnosticCode::none) {
        nested_frames.clear();
    }
    return error;
}

}  // namespace namter::detail
