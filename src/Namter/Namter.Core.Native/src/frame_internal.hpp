#pragma once

#include "frame.hpp"

#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace namter::detail {

struct ExpansionBudget {
    size_t remaining_expanded_bytes = 0;
    size_t remaining_nested_frames = frame_max_nested_frames;
    size_t remaining_emitted_messages = frame_max_emitted_messages;
    size_t remaining_allocation_nodes = frame_max_allocation_nodes;
    size_t depth = 0;
};

[[nodiscard]] FrameDiagnosticCode decompress_lz4_batch(
    std::span<const uint8_t> body,
    size_t max_decompressed_bytes,
    ExpansionBudget& budget,
    std::vector<uint8_t>& decompressed);

}  // namespace namter::detail
