#pragma once

#include <bit>
#include <cstdint>

namespace namter {

[[nodiscard]] constexpr int32_t sequence_distance(uint32_t from, uint32_t to) noexcept {
    return std::bit_cast<int32_t>(to - from);
}

[[nodiscard]] constexpr int64_t unwrap_sequence(uint32_t sequence, int64_t checkpoint) noexcept {
    const auto checkpoint_sequence = static_cast<uint32_t>(checkpoint);
    return checkpoint + static_cast<int64_t>(sequence_distance(checkpoint_sequence, sequence));
}

}  // namespace namter
