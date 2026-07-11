#pragma once

#include "frame.hpp"
#include "namter/core.h"

#include <cstdint>
#include <memory>
#include <span>
#include <string>
#include <variant>
#include <vector>

namespace namter {

enum class ProtocolFieldKind : uint16_t {
    actor_id = 1,
    target_id = 2,
    owner_id = 3,
    skill_id = 4,
    buff_id = 5,
    mob_id = 6,
    boss_id = 7,
    content_id = 8,
    dungeon_id = 9,
    party_id = 10,
    server_id = 11,
    job_id = 12,
    damage = 13,
    multi_damage = 14,
    healing = 15,
    current_hp = 16,
    max_hp = 17,
    special_mask = 18,
    duration_ms = 19,
    state = 20,
    action = 21,
    damage_type = 22,
    is_dot = 23,
    is_self = 24,
    is_boss = 25,
    name = 26,
};

enum class ProtocolFieldFlags : uint16_t {
    fixed_little_endian = 0,
    variable_uint = 1,
    utf8 = 2,
};

enum class DecodeDiagnosticCode : uint8_t {
    invalid_frame,
    invalid_layout,
    payload_too_large,
};

struct ProtocolDecodeDiagnostic {
    DecodeDiagnosticCode code = DecodeDiagnosticCode::invalid_frame;
    uint64_t first_timestamp_ns = 0;
    uint64_t last_timestamp_ns = 0;
    uint64_t epoch = 0;
    uint64_t first_file_offset = 0;
    uint64_t last_file_offset = 0;
    std::vector<uint8_t> retained_bytes;
};

class DecodedEvent {
public:
    DecodedEvent() = default;
    explicit DecodedEvent(nm_event_v1 record) : record_(record) {}

    [[nodiscard]] nm_event_v1& mutable_record() noexcept { return record_; }
    [[nodiscard]] std::string& mutable_name() noexcept { return name_; }
    [[nodiscard]] std::vector<uint8_t>& mutable_payload() noexcept { return payload_; }
    [[nodiscard]] nm_event_v1 view() const noexcept {
        auto result = record_;
        result.name = name_.empty() ? nullptr : reinterpret_cast<const uint8_t*>(name_.data());
        result.name_size = name_.size();
        result.payload = payload_.empty() ? nullptr : payload_.data();
        result.payload_size = payload_.size();
        return result;
    }

private:
    nm_event_v1 record_{};
    std::string name_;
    std::vector<uint8_t> payload_;
};

using ProtocolDecodeOutput = std::variant<DecodedEvent, ProtocolDecodeDiagnostic>;

class ProtocolDecoder {
public:
    explicit ProtocolDecoder(std::span<const uint8_t> validated_snapshot);
    ~ProtocolDecoder();
    ProtocolDecoder(const ProtocolDecoder&) = delete;
    ProtocolDecoder& operator=(const ProtocolDecoder&) = delete;
    ProtocolDecoder(ProtocolDecoder&&) noexcept;
    ProtocolDecoder& operator=(ProtocolDecoder&&) noexcept;

    [[nodiscard]] std::vector<ProtocolDecodeOutput> decode(const ProtocolMessage& message);

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

}  // namespace namter
