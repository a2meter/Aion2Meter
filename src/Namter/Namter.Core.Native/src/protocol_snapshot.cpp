#include "protocol_snapshot.hpp"

#include "event.hpp"

#include <cstddef>
#include <cstdint>
#include <limits>

namespace namter {
namespace {

constexpr size_t header_size = 28;
constexpr size_t crc_offset = 12;
constexpr uint16_t format_version = 1;
constexpr uint16_t max_packet_magic_length = 32;
constexpr uint16_t max_server_ports = 256;
constexpr uint32_t max_opcodes = 4096;
constexpr uint16_t max_tag_length = 32;
constexpr uint32_t max_layouts = 1024;
constexpr uint16_t max_fields_per_layout = 256;
constexpr uint32_t max_payload_bytes = 16 * 1024 * 1024;
constexpr uint16_t field_kind_max = static_cast<uint16_t>(ProtocolFieldKind::name);

constexpr uint32_t field_bit(ProtocolFieldKind kind) noexcept {
    return 1u << (static_cast<uint16_t>(kind) - 1u);
}

template <ProtocolFieldKind... Kinds>
constexpr uint32_t field_mask() noexcept {
    return (field_bit(Kinds) | ...);
}

bool expected_fields(uint16_t opcode_kind, uint32_t& mask) noexcept {
    using enum ProtocolFieldKind;
    switch (opcode_kind) {
        case 1: case 2:
            mask = field_mask<actor_id, target_id, skill_id, damage, multi_damage,
                              healing, special_mask, damage_type, is_dot>(); return true;
        case 3: case 4:
            mask = field_mask<owner_id, target_id, buff_id, duration_ms, action>(); return true;
        case 5: case 6: case 11:
            mask = field_mask<actor_id, owner_id, server_id, job_id, is_self, name>(); return true;
        case 7:
            mask = field_mask<actor_id, owner_id, mob_id, boss_id, current_hp,
                              max_hp, is_boss, name>(); return true;
        case 8:
            mask = field_mask<actor_id, boss_id, current_hp, max_hp>(); return true;
        case 10:
            mask = field_mask<actor_id>(); return true;
        case 101: case 102: case 104: case 105: case 106: case 107: case 108:
            mask = field_mask<party_id, actor_id, content_id, dungeon_id, action, name>(); return true;
        case 103: case 201:
            mask = field_mask<content_id, dungeon_id, state, name>(); return true;
        case 202:
            mask = field_mask<actor_id, state>(); return true;
        default:
            mask = 0;
            return false;
    }
}

bool field_encoding_is_valid(
    uint16_t kind,
    uint16_t flags,
    uint32_t size,
    uint32_t max_count) noexcept {
    if (kind == 0 || kind > field_kind_max || flags > 2u) return false;
    const auto typed_kind = static_cast<ProtocolFieldKind>(kind);
    if (flags == static_cast<uint16_t>(ProtocolFieldFlags::utf8)) {
        return typed_kind == ProtocolFieldKind::name && size == 1u && max_count != 0;
    }
    if (typed_kind == ProtocolFieldKind::name) return false;
    if (flags == static_cast<uint16_t>(ProtocolFieldFlags::variable_uint)) {
        return size == 1u && max_count >= 1u && max_count <= 5u;
    }
    if (max_count != 1u) return false;
    const bool is_unsigned_width = size == 1u || size == 2u || size == 4u;
    const bool is_wide_unsigned_width = is_unsigned_width || size == 8u;
    using enum ProtocolFieldKind;
    switch (typed_kind) {
        case actor_id: case target_id: case owner_id: case skill_id: case buff_id:
        case mob_id: case boss_id: case content_id: case dungeon_id: case party_id:
        case special_mask: case duration_ms:
            return is_unsigned_width;
        case server_id: case job_id:
            return size == 1u || size == 2u;
        case damage: case multi_damage: case healing: case current_hp: case max_hp:
            return is_wide_unsigned_width;
        case state: case action: case damage_type: case is_dot: case is_self: case is_boss:
            return size == 1u;
        default:
            return false;
    }
}

class reader {
public:
    explicit reader(std::span<const uint8_t> bytes) noexcept : bytes_(bytes) {}

    bool read_u16(uint16_t& value) noexcept {
        if (remaining() < sizeof(uint16_t)) return false;
        value = static_cast<uint16_t>(bytes_[offset_]) |
                static_cast<uint16_t>(static_cast<uint16_t>(bytes_[offset_ + 1]) << 8u);
        offset_ += sizeof(uint16_t);
        return true;
    }

    bool read_u32(uint32_t& value) noexcept {
        if (remaining() < sizeof(uint32_t)) return false;
        value = static_cast<uint32_t>(bytes_[offset_]) |
                (static_cast<uint32_t>(bytes_[offset_ + 1]) << 8u) |
                (static_cast<uint32_t>(bytes_[offset_ + 2]) << 16u) |
                (static_cast<uint32_t>(bytes_[offset_ + 3]) << 24u);
        offset_ += sizeof(uint32_t);
        return true;
    }

    bool read_u64(uint64_t& value) noexcept {
        uint32_t low = 0;
        uint32_t high = 0;
        if (!read_u32(low) || !read_u32(high)) return false;
        value = static_cast<uint64_t>(low) | (static_cast<uint64_t>(high) << 32u);
        return true;
    }

    bool skip(size_t count) noexcept {
        if (count > remaining()) return false;
        offset_ += count;
        return true;
    }

    [[nodiscard]] size_t remaining() const noexcept { return bytes_.size() - offset_; }
    [[nodiscard]] size_t offset() const noexcept { return offset_; }

private:
    std::span<const uint8_t> bytes_;
    size_t offset_ = 0;
};

uint32_t compute_crc32(std::span<const uint8_t> bytes) noexcept {
    uint32_t crc = std::numeric_limits<uint32_t>::max();
    for (size_t index = 0; index < bytes.size(); ++index) {
        const uint8_t value = index >= crc_offset && index < crc_offset + sizeof(uint32_t)
                                  ? uint8_t{0}
                                  : bytes[index];
        crc ^= value;
        for (int bit = 0; bit < 8; ++bit) {
            const uint32_t mask = 0u - (crc & 1u);
            crc = (crc >> 1u) ^ (0xEDB88320u & mask);
        }
    }
    return ~crc;
}

bool find_layout_mask(
    std::span<const uint8_t> bytes,
    size_t layout_section_offset,
    uint32_t layout_count,
    uint32_t wanted_layout_id,
    uint32_t& wanted_mask) noexcept {
    reader layouts(bytes.subspan(layout_section_offset));
    for (uint32_t index = 0; index < layout_count; ++index) {
        uint32_t layout_id = 0;
        uint32_t payload_bound = 0;
        uint16_t field_count = 0;
        uint16_t reserved = 0;
        if (!layouts.read_u32(layout_id) || !layouts.read_u32(payload_bound) ||
            !layouts.read_u16(field_count) || !layouts.read_u16(reserved)) {
            return false;
        }
        uint32_t mask = 0;
        for (uint16_t field_index = 0; field_index < field_count; ++field_index) {
            uint16_t kind = 0;
            uint16_t flags = 0;
            uint32_t offset = 0;
            uint32_t size = 0;
            uint32_t max_count = 0;
            if (!layouts.read_u16(kind) || !layouts.read_u16(flags) ||
                !layouts.read_u32(offset) || !layouts.read_u32(size) ||
                !layouts.read_u32(max_count)) {
                return false;
            }
            mask |= 1u << (kind - 1u);
        }
        if (layout_id == wanted_layout_id) {
            wanted_mask = mask;
            return true;
        }
    }
    return false;
}

bool all_opcode_wire_identities_are_valid(
    std::span<const uint8_t> bytes,
    size_t opcode_section_offset,
    uint32_t opcode_count,
    size_t layout_section_offset,
    uint32_t layout_count) noexcept {
    reader opcodes(bytes.subspan(opcode_section_offset));
    for (uint32_t index = 0; index < opcode_count; ++index) {
        uint16_t kind = 0;
        uint16_t tag_length = 0;
        uint32_t layout_id = 0;
        if (!opcodes.read_u16(kind) || !opcodes.read_u16(tag_length) ||
            !opcodes.skip(tag_length) || !opcodes.read_u32(layout_id)) {
            return false;
        }
        reader previous(bytes.subspan(opcode_section_offset));
        for (uint32_t previous_index = 0; previous_index < index; ++previous_index) {
            uint16_t previous_kind = 0;
            uint16_t previous_tag_length = 0;
            uint32_t previous_layout_id = 0;
            if (!previous.read_u16(previous_kind) || !previous.read_u16(previous_tag_length) ||
                !previous.skip(previous_tag_length) || !previous.read_u32(previous_layout_id)) {
                return false;
            }
            if (previous_kind == kind) return false;
        }
        uint32_t expected_mask = 0;
        if (!expected_fields(kind, expected_mask)) {
            if (layout_id != 0) return false;
            continue;
        }
        uint32_t actual_mask = 0;
        if (layout_id == 0 ||
            !find_layout_mask(bytes, layout_section_offset, layout_count, layout_id, actual_mask) ||
            actual_mask != expected_mask) {
            return false;
        }
    }
    return true;
}

}  // namespace

bool validate_protocol_snapshot_v1(std::span<const uint8_t> bytes) noexcept {
    if (bytes.size() < header_size ||
        bytes[0] != 'N' || bytes[1] != 'M' || bytes[2] != 'P' || bytes[3] != 'S') {
        return false;
    }

    reader input(bytes);
    if (!input.skip(4)) return false;
    uint16_t version = 0;
    uint16_t declared_header_size = 0;
    uint32_t total_size = 0;
    uint32_t stored_crc = 0;
    if (!input.read_u16(version) || !input.read_u16(declared_header_size) ||
        !input.read_u32(total_size) || !input.read_u32(stored_crc) ||
        version != format_version || declared_header_size != header_size || total_size != bytes.size() ||
        stored_crc != compute_crc32(bytes)) {
        return false;
    }
    uint64_t data_version = 0;
    uint32_t profile_version = 0;
    if (!input.read_u64(data_version) || data_version == 0 ||
        !input.read_u32(profile_version) || profile_version == 0) {
        return false;
    }

    uint16_t packet_magic_length = 0;
    if (!input.read_u16(packet_magic_length) || packet_magic_length == 0 ||
        packet_magic_length > max_packet_magic_length || !input.skip(packet_magic_length)) {
        return false;
    }

    uint16_t server_port_count = 0;
    if (!input.read_u16(server_port_count) || server_port_count == 0 ||
        server_port_count > max_server_ports) {
        return false;
    }
    for (uint16_t index = 0; index < server_port_count; ++index) {
        uint16_t port = 0;
        if (!input.read_u16(port) || port == 0) return false;
    }

    uint32_t opcode_count = 0;
    if (!input.read_u32(opcode_count) || opcode_count == 0 || opcode_count > max_opcodes) {
        return false;
    }
    const size_t opcode_section_offset = input.offset();
    for (uint32_t index = 0; index < opcode_count; ++index) {
        uint16_t kind = 0;
        uint16_t tag_length = 0;
        uint32_t layout_id = 0;
        if (!input.read_u16(kind) || kind == 0 || !input.read_u16(tag_length) ||
            tag_length == 0 || tag_length > max_tag_length || !input.skip(tag_length) ||
            !input.read_u32(layout_id)) {
            return false;
        }
    }

    uint32_t layout_count = 0;
    if (!input.read_u32(layout_count) || layout_count > max_layouts) return false;
    const size_t layout_section_offset = input.offset();
    for (uint32_t layout_index = 0; layout_index < layout_count; ++layout_index) {
        uint32_t layout_id = 0;
        uint32_t payload_bound = 0;
        uint16_t field_count = 0;
        uint16_t reserved = 0;
        if (!input.read_u32(layout_id) || layout_id == 0 ||
            !input.read_u32(payload_bound) || payload_bound == 0 || payload_bound > max_payload_bytes ||
            !input.read_u16(field_count) || field_count > max_fields_per_layout ||
            !input.read_u16(reserved) || reserved != 0) {
            return false;
        }
        reader previous(bytes.subspan(layout_section_offset));
        for (uint32_t previous_index = 0; previous_index < layout_index; ++previous_index) {
            uint32_t previous_id = 0;
            uint32_t previous_bound = 0;
            uint16_t previous_field_count = 0;
            uint16_t previous_reserved = 0;
            if (!previous.read_u32(previous_id) || !previous.read_u32(previous_bound) ||
                !previous.read_u16(previous_field_count) || !previous.read_u16(previous_reserved) ||
                !previous.skip(static_cast<size_t>(previous_field_count) * 16u)) {
                return false;
            }
            if (previous_id == layout_id) return false;
        }
        uint32_t seen_fields = 0;
        for (uint16_t field_index = 0; field_index < field_count; ++field_index) {
            uint16_t kind = 0;
            uint16_t flags = 0;
            uint32_t offset = 0;
            uint32_t size = 0;
            uint32_t max_count = 0;
            if (!input.read_u16(kind) || !input.read_u16(flags) ||
                !input.read_u32(offset) || !input.read_u32(size) || !input.read_u32(max_count) ||
                !field_encoding_is_valid(kind, flags, size, max_count) ||
                (seen_fields & (1u << (kind - 1u))) != 0 ||
                offset > payload_bound ||
                size > (payload_bound - offset) / max_count) {
                return false;
            }
            seen_fields |= 1u << (kind - 1u);
        }
    }
    return input.remaining() == 0 && all_opcode_wire_identities_are_valid(
        bytes,
        opcode_section_offset,
        opcode_count,
        layout_section_offset,
        layout_count);
}

bool ProtocolSnapshotStore::replace(std::span<const uint8_t> bytes) {
    if (!validate_protocol_snapshot_v1(bytes)) return false;
    std::vector<uint8_t> candidate(bytes.begin(), bytes.end());
    bytes_.swap(candidate);
    return true;
}

}  // namespace namter
