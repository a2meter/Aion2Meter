#include "protocol_snapshot.hpp"

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

bool contains_layout(
    std::span<const uint8_t> bytes,
    size_t layout_section_offset,
    uint32_t layout_count,
    uint32_t wanted_layout_id) noexcept {
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
        if (layout_id == wanted_layout_id) return true;
        if (!layouts.skip(static_cast<size_t>(field_count) * 16u)) return false;
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
        if (layout_id != 0 && !contains_layout(bytes, layout_section_offset, layout_count, layout_id)) {
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
        for (uint16_t field_index = 0; field_index < field_count; ++field_index) {
            uint16_t kind = 0;
            uint16_t flags = 0;
            uint32_t offset = 0;
            uint32_t size = 0;
            uint32_t max_count = 0;
            if (!input.read_u16(kind) || kind == 0 || !input.read_u16(flags) ||
                !input.read_u32(offset) || !input.read_u32(size) || !input.read_u32(max_count) ||
                size == 0 || max_count == 0 || offset > payload_bound ||
                size > (payload_bound - offset) / max_count) {
                return false;
            }
        }
    }
    return input.remaining() == 0 && all_opcode_wire_identities_are_valid(
        bytes,
        opcode_section_offset,
        opcode_count,
        layout_section_offset,
        layout_count);
}

}  // namespace namter
