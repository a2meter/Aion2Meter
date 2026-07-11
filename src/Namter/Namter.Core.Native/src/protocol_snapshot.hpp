#pragma once

#include <cstdint>
#include <span>

namespace namter {

bool validate_protocol_snapshot_v1(std::span<const uint8_t> bytes) noexcept;

}  // namespace namter
