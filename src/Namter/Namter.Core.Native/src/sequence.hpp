#pragma once

#include <bit>
#include <cstddef>
#include <cstdint>

namespace namter {

inline constexpr uint32_t sequence_half_space = 0x80000000u;

[[nodiscard]] constexpr bool valid_sequence_window(size_t window) noexcept {
    return window > 0 && window < static_cast<size_t>(sequence_half_space);
}

[[nodiscard]] constexpr bool sequence_is_ambiguous(uint32_t from, uint32_t to) noexcept {
    return to - from == sequence_half_space;
}

[[nodiscard]] constexpr int32_t sequence_distance(uint32_t from, uint32_t to) noexcept {
    return std::bit_cast<int32_t>(to - from);
}

[[nodiscard]] constexpr int64_t unwrap_sequence(uint32_t sequence, int64_t checkpoint) noexcept {
    const auto checkpoint_sequence = static_cast<uint32_t>(checkpoint);
    return checkpoint + static_cast<int64_t>(sequence_distance(checkpoint_sequence, sequence));
}

}  // namespace namter
