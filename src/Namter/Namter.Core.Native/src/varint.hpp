#pragma once

#include <cstddef>
#include <cstdint>
#include <span>

namespace namter {

enum class VarintStatus : uint8_t {
    complete,
    incomplete,
    invalid,
};

struct VarintResult {
    VarintStatus status = VarintStatus::incomplete;
    uint32_t value = 0;
    size_t bytes_consumed = 0;
};

[[nodiscard]] inline VarintResult decode_u32_varint(
    std::span<const uint8_t> bytes) noexcept {
    uint32_t value = 0;
    const size_t limit = bytes.size() < 5u ? bytes.size() : 5u;

    for (size_t index = 0; index < limit; ++index) {
        const uint8_t byte = bytes[index];
        if (index == 4u && (byte & 0xf0u) != 0) {
            return VarintResult{
                .status = VarintStatus::invalid,
                .bytes_consumed = 5u,
            };
        }

        value |= static_cast<uint32_t>(byte & 0x7fu) << (index * 7u);
        if ((byte & 0x80u) == 0) {
            return VarintResult{
                .status = VarintStatus::complete,
                .value = value,
                .bytes_consumed = index + 1u,
            };
        }
    }

    if (bytes.size() >= 5u) {
        return VarintResult{
            .status = VarintStatus::invalid,
            .bytes_consumed = 5u,
        };
    }
    return VarintResult{
        .status = VarintStatus::incomplete,
        .bytes_consumed = bytes.size(),
    };
}

}  // namespace namter
