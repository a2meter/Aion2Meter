#pragma once

#include <cstdint>
#include <span>
#include <vector>

namespace namter {

bool validate_protocol_snapshot_v1(std::span<const uint8_t> bytes) noexcept;

class ProtocolSnapshotStore {
public:
    [[nodiscard]] bool replace(std::span<const uint8_t> bytes);
    [[nodiscard]] const std::vector<uint8_t>& bytes() const noexcept { return bytes_; }

private:
    std::vector<uint8_t> bytes_;
};

}  // namespace namter
