#include "frame_internal.hpp"
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

}  // namespace

FrameDiagnosticCode decompress_lz4_batch(
    std::span<const uint8_t> body,
    size_t max_decompressed_bytes,
    ExpansionBudget& budget,
    std::vector<uint8_t>& decompressed) {
    decompressed.clear();
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
    if (output_size > budget.remaining_expanded_bytes ||
        budget.remaining_allocation_nodes == 0) {
        return FrameDiagnosticCode::resource_limit_exceeded;
    }

    const auto compressed = body.subspan(6u);
    if (compressed.empty() || compressed.size() > static_cast<size_t>(INT_MAX)) {
        return FrameDiagnosticCode::lz4_decompression_failed;
    }

    budget.remaining_expanded_bytes -= output_size;
    --budget.remaining_allocation_nodes;
    decompressed.resize(output_size);
    const int written = LZ4_decompress_safe(
        reinterpret_cast<const char*>(compressed.data()),
        reinterpret_cast<char*>(decompressed.data()),
        static_cast<int>(compressed.size()),
        declared_size);
    if (written != declared_size) {
        decompressed.clear();
        return FrameDiagnosticCode::lz4_decompression_failed;
    }
    return FrameDiagnosticCode::none;
}

}  // namespace namter::detail
