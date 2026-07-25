#include "event.hpp"
#include "protocol_snapshot.hpp"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <limits>
#include <span>
#include <stdexcept>
#include <string>
#include <unordered_set>
#include <unordered_map>
#include <utility>
#include <vector>

namespace namter {
namespace {

constexpr uint32_t abi_version = 1;
constexpr size_t diagnostic_byte_limit = 256;
constexpr uint32_t production_profile_floor = 20260710u;
constexpr size_t unknown_payload_limit = 512;
constexpr size_t deduplication_limit = 65'536;

class SpanReader {
public:
    explicit SpanReader(std::span<const uint8_t> bytes) noexcept : bytes_(bytes) {}

    bool read_u8(uint8_t& value) noexcept {
        if (remaining() < 1u) return false;
        value = bytes_[offset_++];
        return true;
    }

    bool read_le16(uint16_t& value) noexcept {
        if (remaining() < 2u) return false;
        value = static_cast<uint16_t>(bytes_[offset_]) |
                static_cast<uint16_t>(static_cast<uint16_t>(bytes_[offset_ + 1u]) << 8u);
        offset_ += 2u;
        return true;
    }

    bool read_le32(uint32_t& value) noexcept {
        if (remaining() < 4u) return false;
        value = 0;
        for (size_t index = 0; index < 4u; ++index) {
            value |= static_cast<uint32_t>(bytes_[offset_ + index]) << (index * 8u);
        }
        offset_ += 4u;
        return true;
    }

    bool read_le64(uint64_t& value) noexcept {
        uint32_t low = 0;
        uint32_t high = 0;
        if (!read_le32(low) || !read_le32(high)) return false;
        value = static_cast<uint64_t>(low) | (static_cast<uint64_t>(high) << 32u);
        return true;
    }

    bool read_var_u32(uint32_t& value) noexcept {
        value = 0;
        for (uint32_t index = 0; index < 5u; ++index) {
            uint8_t byte = 0;
            if (!read_u8(byte)) return false;
            if (index == 4u && (byte & 0xf0u) != 0) return false;
            value |= static_cast<uint32_t>(byte & 0x7fu) << (index * 7u);
            if ((byte & 0x80u) == 0) return true;
        }
        return false;
    }

    bool read_utf8(size_t count, std::string& value) noexcept {
        if (count > remaining()) return false;
        const auto text = bytes_.subspan(offset_, count);
        if (!valid_utf8(text)) return false;
        value.assign(reinterpret_cast<const char*>(text.data()), text.size());
        offset_ += count;
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
    static bool valid_utf8(std::span<const uint8_t> bytes) noexcept {
        size_t index = 0;
        while (index < bytes.size()) {
            const uint8_t first = bytes[index++];
            if (first <= 0x7fu) continue;
            size_t continuation = 0;
            uint32_t codepoint = 0;
            if (first >= 0xc2u && first <= 0xdfu) { continuation = 1; codepoint = first & 0x1fu; }
            else if (first >= 0xe0u && first <= 0xefu) { continuation = 2; codepoint = first & 0x0fu; }
            else if (first >= 0xf0u && first <= 0xf4u) { continuation = 3; codepoint = first & 0x07u; }
            else return false;
            if (continuation > bytes.size() - index) return false;
            for (size_t count = 0; count < continuation; ++count) {
                const uint8_t next = bytes[index++];
                if ((next & 0xc0u) != 0x80u) return false;
                codepoint = (codepoint << 6u) | (next & 0x3fu);
            }
            if ((continuation == 2u && codepoint < 0x800u) ||
                (continuation == 3u && codepoint < 0x10000u) ||
                codepoint > 0x10ffffu || (codepoint >= 0xd800u && codepoint <= 0xdfffu)) return false;
        }
        return true;
    }

    std::span<const uint8_t> bytes_;
    size_t offset_ = 0;
};

struct FieldDescriptor {
    ProtocolFieldKind kind{};
    ProtocolFieldFlags flags{};
    uint32_t offset = 0;
    uint32_t size = 0;
    uint32_t max_count = 0;
};

struct Layout {
    uint32_t id = 0;
    uint32_t max_payload_bytes = 0;
    uint16_t parser_strategy = 0;
    std::vector<FieldDescriptor> fields;
};

struct OpcodeDescriptor {
    uint16_t kind = 0;
    std::vector<uint8_t> tag;
    uint32_t layout_id = 0;
};

class FieldReader {
public:
    FieldReader(std::span<const uint8_t> payload, const Layout& layout)
        : payload_(payload), layout_(layout) {
        resolved_offsets_.reserve(layout.fields.size());
        size_t cursor = 0;
        for (const auto& field : layout.fields) {
            const bool sequential = static_cast<uint16_t>(field.flags) >=
                                    static_cast<uint16_t>(ProtocolFieldFlags::sequential_fixed_little_endian);
            if (!sequential) {
                resolved_offsets_.push_back(field.offset);
                continue;
            }
            if (field.offset > payload_.size() - cursor) { valid_ = false; return; }
            cursor += field.offset;
            resolved_offsets_.push_back(cursor);
            SpanReader reader(payload_.subspan(cursor));
            switch (field.flags) {
                case ProtocolFieldFlags::sequential_fixed_little_endian:
                    if (!reader.skip(field.size)) { valid_ = false; return; }
                    break;
                case ProtocolFieldFlags::sequential_variable_uint: {
                    uint32_t value = 0;
                    if (!reader.read_var_u32(value) || reader.offset() > field.max_count) { valid_ = false; return; }
                    break;
                }
                case ProtocolFieldFlags::sequential_utf8: {
                    uint32_t length = 0;
                    std::string value;
                    if (!reader.read_var_u32(length) || length > field.max_count || !reader.read_utf8(length, value)) {
                        valid_ = false; return;
                    }
                    break;
                }
                default:
                    valid_ = false; return;
            }
            cursor += reader.offset();
        }
    }

    bool read_u8(ProtocolFieldKind kind, uint8_t& value) const { uint64_t wide = 0; if (!read_integer(kind, wide) || wide > 0xffu) return false; value = static_cast<uint8_t>(wide); return true; }
    bool read_u16(ProtocolFieldKind kind, uint16_t& value) const { uint64_t wide = 0; if (!read_integer(kind, wide) || wide > 0xffffu) return false; value = static_cast<uint16_t>(wide); return true; }
    bool read_u32(ProtocolFieldKind kind, uint32_t& value) const { uint64_t wide = 0; if (!read_integer(kind, wide) || wide > std::numeric_limits<uint32_t>::max()) return false; value = static_cast<uint32_t>(wide); return true; }
    bool read_u64(ProtocolFieldKind kind, uint64_t& value) const { return read_integer(kind, value); }

    bool read_utf8(ProtocolFieldKind kind, std::string& value) const {
        size_t offset = 0;
        const auto* field = find(kind, offset);
        if (!valid_ || field == nullptr ||
            (field->flags != ProtocolFieldFlags::utf8 && field->flags != ProtocolFieldFlags::sequential_utf8) ||
            offset > payload_.size()) return false;
        SpanReader reader(payload_.subspan(offset));
        uint32_t length = 0;
        return reader.read_var_u32(length) && length <= field->max_count && reader.read_utf8(length, value);
    }

private:
    const FieldDescriptor* find(ProtocolFieldKind kind, size_t& offset) const noexcept {
        const auto found = std::find_if(layout_.fields.begin(), layout_.fields.end(), [kind](const auto& field) { return field.kind == kind; });
        if (found == layout_.fields.end()) return nullptr;
        const size_t index = static_cast<size_t>(found - layout_.fields.begin());
        offset = resolved_offsets_[index];
        return &*found;
    }

    bool read_integer(ProtocolFieldKind kind, uint64_t& value) const {
        size_t offset = 0;
        const auto* field = find(kind, offset);
        if (!valid_ || field == nullptr || offset > payload_.size()) return false;
        SpanReader reader(payload_.subspan(offset));
        if (field->flags == ProtocolFieldFlags::variable_uint ||
            field->flags == ProtocolFieldFlags::sequential_variable_uint) {
            uint32_t result = 0;
            if (!reader.read_var_u32(result) || reader.offset() > field->max_count) return false;
            value = result;
            return true;
        }
        if ((field->flags != ProtocolFieldFlags::fixed_little_endian &&
             field->flags != ProtocolFieldFlags::sequential_fixed_little_endian) || field->max_count != 1u) return false;
        switch (field->size) {
            case 1: { uint8_t result = 0; if (!reader.read_u8(result)) return false; value = result; return true; }
            case 2: { uint16_t result = 0; if (!reader.read_le16(result)) return false; value = result; return true; }
            case 4: { uint32_t result = 0; if (!reader.read_le32(result)) return false; value = result; return true; }
            case 8: return reader.read_le64(value);
            default: return false;
        }
    }

    std::span<const uint8_t> payload_;
    const Layout& layout_;
    std::vector<size_t> resolved_offsets_;
    bool valid_ = true;
};

struct Identity {
    FlowTuple flow;
    uint64_t epoch = 0;
    uint64_t stream_message_id = 0;
    bool operator==(const Identity&) const = default;
};

void hash_mix(size_t& hash, uint64_t value) noexcept {
    hash ^= static_cast<size_t>(value) + static_cast<size_t>(0x9e3779b97f4a7c15ull) + (hash << 6u) + (hash >> 2u);
}

struct IdentityHash {
    size_t operator()(const Identity& value) const noexcept {
        size_t result = 0;
        hash_mix(result, value.flow.source_address); hash_mix(result, value.flow.destination_address);
        hash_mix(result, value.flow.source_port); hash_mix(result, value.flow.destination_port); hash_mix(result, value.epoch);
        hash_mix(result, value.stream_message_id);
        return result;
    }
};

Identity identity_of(const ProtocolMessage& message) noexcept {
    return {message.flow, message.epoch, message.stream_message_id};
}

nm_event_v1 base_event(const ProtocolMessage& message, nm_event_kind kind) noexcept {
    return {
        .abi_version = abi_version,
        .struct_size = sizeof(nm_event_v1),
        .kind = static_cast<uint32_t>(kind),
        .first_timestamp_ns = message.first_timestamp_ns,
        .last_timestamp_ns = message.last_timestamp_ns,
        .epoch = message.epoch,
        .first_file_offset = message.first_provenance.file_offset,
        .last_file_offset = message.last_provenance.file_offset,
        .source_address = message.flow.source_address,
        .destination_address = message.flow.destination_address,
        .source_port = message.flow.source_port,
        .destination_port = message.flow.destination_port,
    };
}

ProtocolDecodeDiagnostic diagnostic(const ProtocolMessage& message, DecodeDiagnosticCode code) {
    const size_t retained = std::min(message.bytes.size(), diagnostic_byte_limit);
    return {code, message.first_timestamp_ns, message.last_timestamp_ns, message.epoch,
            message.first_provenance.file_offset, message.last_provenance.file_offset,
            std::vector<uint8_t>(message.bytes.begin(), message.bytes.begin() + static_cast<std::ptrdiff_t>(retained))};
}

DecodedEvent unknown_event(const ProtocolMessage& message) {
    DecodedEvent unknown(base_event(message, NM_EVENT_UNKNOWN_PROTOCOL));
    const size_t retained = std::min(message.bytes.size(), unknown_payload_limit);
    unknown.mutable_payload().assign(
        message.bytes.begin(),
        message.bytes.begin() + static_cast<std::ptrdiff_t>(retained));
    return unknown;
}

bool parse_frame_prefix(std::span<const uint8_t> bytes, size_t& prefix_size) noexcept {
    SpanReader reader(bytes);
    uint32_t declared = 0;
    if (!reader.read_var_u32(declared) || declared < 4u) return false;
    prefix_size = reader.offset();
    const uint64_t frame_size = static_cast<uint64_t>(prefix_size) + declared - 4u;
    return frame_size == bytes.size();
}

using EventParser = bool (*)(DecodedEvent&, const FieldReader&);

bool parse_damage(DecodedEvent& event, const FieldReader& fields) {
    auto& record = event.mutable_record();
    record.kind = NM_EVENT_DAMAGE;
    using enum ProtocolFieldKind;
    return fields.read_u32(actor_id, record.actor_id) && fields.read_u32(target_id, record.target_id) &&
           fields.read_u32(skill_id, record.skill_id) && fields.read_u64(damage, record.damage) &&
           fields.read_u64(multi_damage, record.multi_damage) && fields.read_u64(healing, record.healing) &&
           fields.read_u32(special_mask, record.special_mask) && fields.read_u8(damage_type, record.damage_type) &&
           fields.read_u8(is_dot, record.is_dot);
}

bool parse_current_damage(DecodedEvent& event, std::span<const uint8_t> payload) {
    SpanReader reader(payload);
    uint32_t flags = 0;
    uint32_t ignored = 0;
    uint8_t skill_discriminator = 0;
    auto& record = event.mutable_record();
    if (!reader.read_var_u32(record.target_id) || !reader.read_var_u32(flags) ||
        !reader.read_var_u32(ignored) || !reader.read_var_u32(record.actor_id) ||
        !reader.read_le32(record.skill_id) || !reader.read_u8(skill_discriminator) ||
        !reader.read_var_u32(ignored)) {
        return false;
    }
    (void)skill_discriminator;
    record.damage_type = static_cast<uint8_t>(ignored);
    const uint32_t category = flags & 0x0fu;
    uint8_t raw_flags = 0;
    uint8_t modifier = 0;
    if (category == 4u) {
        if (!reader.skip(8u)) return false;
    } else if (category == 6u) {
        uint8_t zero = 0;
        if (!reader.read_u8(raw_flags) || !reader.read_u8(zero) || zero != 0u ||
            !reader.read_u8(modifier) || !reader.skip(8u)) {
            return false;
        }
    } else {
        return false;
    }
    if (!reader.read_var_u32(ignored)) return false;
    uint32_t damage = 0;
    if (!reader.read_var_u32(damage)) return false;
    record.damage = damage;

    const auto try_multi = [damage](SpanReader candidate, bool consume_marker,
                                    SpanReader& accepted, uint64_t& total) {
        uint32_t marker = 0;
        uint32_t count = 0;
        total = 0;
        if ((consume_marker && (!candidate.read_var_u32(marker) || marker != 1u)) ||
            !candidate.read_var_u32(count) || count == 0u || count >= 100u) return false;
        for (uint32_t index = 0; index < count; ++index) {
            uint32_t hit = 0;
            if (!candidate.read_var_u32(hit) || hit == 0u ||
                total > std::numeric_limits<uint64_t>::max() - hit) return false;
            total += hit;
        }
        if (total == 0u || (damage != 0u && total * 200u < damage)) return false;
        accepted = candidate;
        return true;
    };
    SpanReader accepted = reader;
    uint64_t multi = 0;
    bool valid_multi = try_multi(reader, false, accepted, multi);
    if (!valid_multi && category == 4u) valid_multi = try_multi(reader, true, accepted, multi);
    if (valid_multi) {
        record.multi_damage = multi;
        reader = accepted;
    } else if (category == 4u && reader.remaining() > 2u) {
        auto prefix_reader = reader;
        uint32_t prefix = 0;
        if (prefix_reader.read_var_u32(prefix) && prefix == 1u) reader = prefix_reader;
    }

    if (reader.remaining() < 2u || !reader.skip(2u)) return false;
    if (reader.remaining() != 0u) {
        uint32_t healing = 0;
        if (!reader.read_var_u32(healing)) return false;
        record.healing = healing;
    }
    if (reader.remaining() != 0u) return false;

    record.kind = NM_EVENT_DAMAGE;
    record.is_dot = 0u;
    record.special_mask = category == 6u ? static_cast<uint32_t>(raw_flags & 0x7fu) : 0u;
    if (category == 6u && (raw_flags & 0x80u) != 0u) record.special_mask |= 0x20u;
    if (category == 6u && modifier == 1u) record.special_mask |= 0x01u;
    if (record.damage_type == 3u) record.special_mask |= 0x40u;
    return true;
}

bool parse_current_dot(DecodedEvent& event, std::span<const uint8_t> payload) {
    SpanReader reader(payload);
    uint32_t flags = 0;
    uint32_t primary_amount = 0;
    uint32_t raw_skill = 0;
    uint32_t damage = 0;
    auto& record = event.mutable_record();
    if (!reader.read_var_u32(record.target_id) || !reader.read_var_u32(flags) ||
        !reader.read_var_u32(record.actor_id) || !reader.read_var_u32(primary_amount) ||
        !reader.read_le32(raw_skill) || (flags & 0x02u) == 0u ||
        !reader.read_var_u32(damage) || record.target_id == 0u || record.actor_id == 0u ||
        raw_skill < 100u || damage == 0u || reader.remaining() > 64u) {
        return false;
    }
    (void)primary_amount;
    record.kind = NM_EVENT_DOT;
    record.skill_id = raw_skill / 100u;
    record.damage = damage;
    record.is_dot = 1u;
    return record.skill_id != 0u;
}

bool parse_current_buff(DecodedEvent& event, std::span<const uint8_t> payload, bool applied) {
    SpanReader reader(payload);
    uint8_t ignored = 0;
    uint8_t slot = 0;
    uint32_t sequence = 0;
    uint32_t reserved = 0;
    uint64_t expiry = 0;
    auto& record = event.mutable_record();
    if (!reader.read_var_u32(record.target_id) || record.target_id == 0u ||
        (applied && !reader.read_u8(ignored)) || !reader.read_u8(slot) ||
        !reader.read_var_u32(sequence) || !reader.read_le32(record.buff_id) ||
        !reader.read_le32(record.duration_ms) || !reader.read_le32(reserved) ||
        (reserved != 0u && !(reserved == std::numeric_limits<uint32_t>::max() &&
                            record.duration_ms == std::numeric_limits<uint32_t>::max())) ||
        !reader.read_le64(expiry) || !reader.read_var_u32(record.owner_id) ||
        record.owner_id == 0u || record.buff_id == 0u || record.duration_ms == 0u ||
        reader.remaining() > 64u) return false;
    (void)ignored; (void)slot; (void)sequence; (void)expiry;
    record.kind = NM_EVENT_BUFF;
    record.buff_operation = static_cast<uint8_t>(applied ? NM_BUFF_OPERATION_APPLY : NM_BUFF_OPERATION_REFRESH);
    return true;
}

bool parse_current_boss_hp(DecodedEvent& event, std::span<const uint8_t> payload) {
    SpanReader reader(payload);
    uint8_t marker0 = 0;
    uint8_t marker1 = 0;
    uint8_t marker2 = 0;
    uint32_t current_hp = 0;
    uint32_t reserved = 0;
    auto& record = event.mutable_record();
    if (!reader.read_var_u32(record.actor_id) || !reader.read_u8(marker0) ||
        !reader.read_u8(marker1) || !reader.read_u8(marker2) || marker0 != 2u ||
        marker1 != 1u || marker2 != 0u || !reader.read_le32(current_hp) ||
        !reader.read_le32(reserved) || reserved != 0u || reader.remaining() != 0u) {
        return false;
    }
    record.kind = NM_EVENT_BOSS_HP;
    record.current_hp = current_hp;
    return true;
}

// A summoned entity carries no name and no mob code, so its only link to the
// player who summoned it is a trailer inside the same spawn message: an
// eight-byte boundary marker, then a {7,2,6} header whose following two bytes
// hold the summoner's actor id as little-endian uint16. Anything below 100 is a
// slot/index rather than an actor, so the scan keeps looking past it.
bool find_summon_owner(std::span<const uint8_t> payload, uint32_t& owner) noexcept {
    constexpr size_t boundary_size = 8u;
    constexpr size_t header_size = 5u;
    size_t cursor = 0;
    while (cursor + boundary_size <= payload.size()) {
        size_t boundary = payload.size();
        for (size_t i = cursor; i + boundary_size <= payload.size(); ++i) {
            bool matched = true;
            for (size_t k = 0; k < boundary_size; ++k) {
                if (payload[i + k] != 0xffu) { matched = false; break; }
            }
            if (matched) { boundary = i; break; }
        }
        if (boundary == payload.size()) return false;
        const size_t after = boundary + boundary_size;
        size_t header = payload.size();
        for (size_t j = after; j + header_size <= payload.size(); ++j) {
            if (payload[j] == 7u && payload[j + 1u] == 2u && payload[j + 2u] == 6u) { header = j; break; }
        }
        if (header == payload.size()) { cursor = after; continue; }
        const uint32_t candidate = static_cast<uint32_t>(payload[header + 3u]) |
                                   (static_cast<uint32_t>(payload[header + 4u]) << 8u);
        if (candidate > 99u) { owner = candidate; return true; }
        cursor = boundary + 1u;
    }
    return false;
}

bool parse_current_mob(DecodedEvent& event, std::span<const uint8_t> payload) {
    SpanReader reader(payload);
    uint8_t code0 = 0;
    uint8_t code1 = 0;
    uint8_t code2 = 0;
    uint8_t marker0 = 0;
    uint8_t marker1 = 0;
    uint8_t marker2 = 0;
    auto& record = event.mutable_record();
    if (!reader.read_var_u32(record.actor_id)) return false;
    auto named_reader = reader;
    uint8_t header0 = 0;
    uint8_t header1 = 0;
    uint8_t header2 = 0;
    uint32_t name_size = 0;
    std::string owner_name;
    if (named_reader.read_u8(header0) && named_reader.read_u8(header1) && named_reader.read_u8(header2) &&
        header1 == 0u && header2 == 1u && named_reader.read_var_u32(name_size) &&
        name_size > 0u && name_size <= 72u && named_reader.read_utf8(name_size, owner_name) &&
        named_reader.skip(4u) && named_reader.read_u8(record.state)) {
        (void)header0;
        event.mutable_name() = std::move(owner_name);
        record.kind = NM_EVENT_MOB_SPAWN;
        return true;
    }
    if (!reader.skip(3u) || !reader.read_u8(code0) || !reader.read_u8(code1) ||
        !reader.read_u8(code2) || !reader.read_u8(marker0) || !reader.read_u8(marker1) ||
        !reader.read_u8(marker2) || marker0 != 0u || marker1 != 0u || marker2 != 2u) {
        // Neither a named spawn nor a mob-code spawn: the remaining supported
        // variant is a summon whose owner is carried in the trailer.
        uint32_t summon_owner = 0;
        if (record.actor_id != 0u && find_summon_owner(payload, summon_owner) &&
            summon_owner != record.actor_id) {
            record.owner_id = summon_owner;
            record.kind = NM_EVENT_MOB_SPAWN;
            return true;
        }
        return false;
    }
    record.mob_id = static_cast<uint32_t>(code0) |
                    (static_cast<uint32_t>(code1) << 8u) |
                    (static_cast<uint32_t>(code2) << 16u);
    if (record.mob_id == 0u) return false;
    record.boss_id = record.mob_id;
    record.is_boss = 1u;
    record.kind = NM_EVENT_MOB_SPAWN;
    return true;
}

bool parse_current_actor(DecodedEvent& event, std::span<const uint8_t> payload, bool is_self) {
    SpanReader root(payload);
    auto& record = event.mutable_record();
    if (!root.read_var_u32(record.actor_id) || record.actor_id == 0u) return false;
    if (is_self) {
        uint32_t name_size = 0;
        uint32_t server = 0;
        uint32_t raw_job = 0;
        std::string name;
        if (root.skip(5u) && root.read_var_u32(name_size) && name_size > 0u && name_size <= 72u &&
            root.read_utf8(name_size, name) && root.read_var_u32(server) &&
            root.read_le32(raw_job) && raw_job > 0u && raw_job <= std::numeric_limits<uint16_t>::max()) {
            event.mutable_name() = std::move(name);
            record.server_id = server <= std::numeric_limits<uint16_t>::max() ? static_cast<uint16_t>(server) : 0u;
            record.job_id = static_cast<uint16_t>(raw_job);
            record.is_self = 1u;
            record.kind = NM_EVENT_SELF_ACTOR;
            return true;
        }
        return false;
    }
    const size_t scan_limit = std::min<size_t>(payload.size(), 96u);
    for (size_t offset = root.offset(); offset + 2u < scan_limit; ++offset) {
        if (payload[offset] != 0x07u) continue;
        SpanReader candidate(payload.subspan(offset + 1u));
        uint32_t name_size = 0;
        std::string name;
        uint32_t raw_job = 0;
        if (!candidate.read_var_u32(name_size) || name_size == 0u || name_size > 72u ||
            !candidate.read_utf8(name_size, name) || !candidate.read_le32(raw_job) ||
            raw_job == 0u || raw_job > std::numeric_limits<uint16_t>::max()) {
            continue;
        }
        event.mutable_name() = std::move(name);
        record.job_id = static_cast<uint16_t>(raw_job);
        record.is_self = is_self ? 1u : 0u;
        record.kind = is_self ? NM_EVENT_SELF_ACTOR : NM_EVENT_OTHER_ACTOR;
        return true;
    }
    return false;
}

bool parse_current_content(DecodedEvent& event, std::span<const uint8_t> payload) {
    SpanReader reader(payload);
    uint8_t reserved = 0;
    uint8_t state = 0;
    auto& record = event.mutable_record();
    if (!reader.read_le32(record.content_id) || record.content_id == 0u ||
        !reader.skip(8u) || !reader.read_u8(reserved) || !reader.read_u8(state) ||
        state == 0u || reader.remaining() > 64u) {
        return false;
    }
    (void)reserved;
    record.kind = NM_EVENT_CONTENT;
    record.dungeon_id = record.content_id;
    record.state = state;
    return true;
}

bool parse_current_combat_state(DecodedEvent& event, std::span<const uint8_t> payload) {
    SpanReader reader(payload);
    uint8_t marker = 0;
    auto& record = event.mutable_record();
    if (!reader.read_var_u32(record.actor_id) || record.actor_id == 0u ||
        !reader.read_u8(marker) || marker != 2u || !reader.skip(3u) ||
        reader.remaining() != 0u) return false;
    record.kind = NM_EVENT_COMBAT_STATE;
    record.state = 1u;
    return true;
}

bool parse_current_action(std::span<const uint8_t> payload, std::array<uint32_t, 3>& echo) {
    SpanReader reader(payload);
    uint8_t ignored = 0;
    uint8_t action = 0;
    uint8_t trailing = 0;
    uint32_t actor = 0;
    uint32_t skill = 0;
    uint32_t target = 0;
    if (!reader.read_var_u32(actor) || !reader.read_u8(ignored) || !reader.read_le32(skill) ||
        !reader.read_u8(action) || !reader.read_u8(trailing) || !reader.read_var_u32(target) ||
        actor == 0u || skill == 0u || target == 0u || reader.remaining() > 64u) return false;
    (void)ignored; (void)trailing;
    echo = {actor, target, action == 3u ? skill : 0u};
    return true;
}

bool parse_dot(DecodedEvent& event, const FieldReader& fields) {
    if (!parse_damage(event, fields)) return false;
    event.mutable_record().kind = NM_EVENT_DOT;
    event.mutable_record().is_dot = 1u;
    return true;
}

bool parse_buff(DecodedEvent& event, const FieldReader& fields) {
    auto& record = event.mutable_record();
    record.kind = NM_EVENT_BUFF;
    using enum ProtocolFieldKind;
    return fields.read_u32(owner_id, record.owner_id) && fields.read_u32(target_id, record.target_id) &&
           fields.read_u32(buff_id, record.buff_id) && fields.read_u32(duration_ms, record.duration_ms) &&
           fields.read_u8(action, record.action);
}

bool parse_self_actor(DecodedEvent& event, const FieldReader& fields) {
    auto& record = event.mutable_record();
    record.kind = NM_EVENT_SELF_ACTOR;
    using enum ProtocolFieldKind;
    return fields.read_u32(actor_id, record.actor_id) && fields.read_u32(owner_id, record.owner_id) &&
           fields.read_u16(server_id, record.server_id) && fields.read_u16(job_id, record.job_id) &&
           fields.read_u8(is_self, record.is_self) && fields.read_utf8(name, event.mutable_name());
}

bool parse_other_actor(DecodedEvent& event, const FieldReader& fields) {
    if (!parse_self_actor(event, fields)) return false;
    event.mutable_record().kind = NM_EVENT_OTHER_ACTOR;
    event.mutable_record().is_self = 0u;
    return true;
}

bool parse_mob(DecodedEvent& event, const FieldReader& fields) {
    auto& record = event.mutable_record();
    record.kind = NM_EVENT_MOB_SPAWN;
    using enum ProtocolFieldKind;
    return fields.read_u32(actor_id, record.actor_id) && fields.read_u32(owner_id, record.owner_id) &&
           fields.read_u32(mob_id, record.mob_id) && fields.read_u32(boss_id, record.boss_id) &&
           fields.read_u64(current_hp, record.current_hp) && fields.read_u64(max_hp, record.max_hp) &&
           fields.read_u8(is_boss, record.is_boss) && fields.read_utf8(name, event.mutable_name());
}

bool parse_boss_hp(DecodedEvent& event, const FieldReader& fields) {
    auto& record = event.mutable_record();
    record.kind = NM_EVENT_BOSS_HP;
    using enum ProtocolFieldKind;
    return fields.read_u32(actor_id, record.actor_id) && fields.read_u32(boss_id, record.boss_id) &&
           fields.read_u64(current_hp, record.current_hp) && fields.read_u64(max_hp, record.max_hp);
}

bool parse_removed(DecodedEvent& event, const FieldReader& fields) {
    event.mutable_record().kind = NM_EVENT_ENTITY_REMOVED;
    return fields.read_u32(ProtocolFieldKind::actor_id, event.mutable_record().actor_id);
}

bool parse_party(DecodedEvent& event, const FieldReader& fields) {
    auto& record = event.mutable_record();
    record.kind = NM_EVENT_PARTY;
    using enum ProtocolFieldKind;
    return fields.read_u32(party_id, record.party_id) && fields.read_u32(actor_id, record.actor_id) &&
           fields.read_u32(content_id, record.content_id) && fields.read_u32(dungeon_id, record.dungeon_id) &&
           fields.read_u8(action, record.action) && fields.read_utf8(name, event.mutable_name());
}

bool parse_content(DecodedEvent& event, const FieldReader& fields) {
    auto& record = event.mutable_record();
    record.kind = NM_EVENT_CONTENT;
    using enum ProtocolFieldKind;
    return fields.read_u32(content_id, record.content_id) && fields.read_u32(dungeon_id, record.dungeon_id) &&
           fields.read_u8(state, record.state) && fields.read_utf8(name, event.mutable_name());
}

bool parse_combat_state(DecodedEvent& event, const FieldReader& fields) {
    auto& record = event.mutable_record();
    record.kind = NM_EVENT_COMBAT_STATE;
    return fields.read_u32(ProtocolFieldKind::actor_id, record.actor_id) &&
           fields.read_u8(ProtocolFieldKind::state, record.state);
}

struct ParserEntry { uint16_t protocol_kind; EventParser parser; };
struct EchoAction { uint32_t actor; uint32_t target; uint32_t skill; uint64_t timestamp_ns; };

constexpr std::array parser_table{
    ParserEntry{1, &parse_damage}, ParserEntry{2, &parse_dot},
    ParserEntry{3, &parse_buff}, ParserEntry{4, &parse_buff},
    ParserEntry{5, &parse_self_actor}, ParserEntry{6, &parse_other_actor},
    ParserEntry{7, &parse_mob}, ParserEntry{8, &parse_boss_hp}, ParserEntry{10, &parse_removed},
    ParserEntry{11, &parse_other_actor},
    ParserEntry{101, &parse_party}, ParserEntry{102, &parse_party}, ParserEntry{103, &parse_content},
    ParserEntry{104, &parse_party}, ParserEntry{105, &parse_party}, ParserEntry{106, &parse_party},
    ParserEntry{107, &parse_party}, ParserEntry{108, &parse_party},
    ParserEntry{201, &parse_content}, ParserEntry{202, &parse_combat_state},
};

}  // namespace

struct ProtocolDecoder::Impl {
    uint32_t profile_version = 0;
    std::vector<uint8_t> packet_magic;
    std::vector<OpcodeDescriptor> opcodes;
    std::vector<Layout> layouts;
    std::unordered_set<Identity, IdentityHash> seen;
    std::deque<Identity> seen_order;
    std::unordered_map<uint32_t, uint32_t> mob_codes;
    std::vector<EchoAction> echo_actions;
    std::unordered_set<uint32_t> echo_clone_actors;

    explicit Impl(std::span<const uint8_t> snapshot) { parse_snapshot(snapshot); }

    void parse_snapshot(std::span<const uint8_t> snapshot) {
        profile_version = static_cast<uint32_t>(snapshot[24]) |
                          (static_cast<uint32_t>(snapshot[25]) << 8u) |
                          (static_cast<uint32_t>(snapshot[26]) << 16u) |
                          (static_cast<uint32_t>(snapshot[27]) << 24u);
        SpanReader reader(snapshot);
        if (!reader.skip(28u)) throw std::invalid_argument("invalid protocol snapshot");
        uint16_t magic_size = 0;
        if (!reader.read_le16(magic_size) || magic_size > reader.remaining()) throw std::invalid_argument("invalid protocol snapshot");
        packet_magic.resize(magic_size);
        for (auto& byte : packet_magic) if (!reader.read_u8(byte)) throw std::invalid_argument("invalid protocol snapshot");
        uint16_t port_count = 0;
        if (!reader.read_le16(port_count) || !reader.skip(static_cast<size_t>(port_count) * 2u)) throw std::invalid_argument("invalid protocol snapshot");
        uint32_t opcode_count = 0;
        if (!reader.read_le32(opcode_count)) throw std::invalid_argument("invalid protocol snapshot");
        opcodes.reserve(opcode_count);
        for (uint32_t index = 0; index < opcode_count; ++index) {
            uint16_t kind = 0; uint16_t tag_size = 0; uint32_t layout = 0;
            if (!reader.read_le16(kind) || !reader.read_le16(tag_size) || tag_size > reader.remaining()) throw std::invalid_argument("invalid protocol snapshot");
            std::vector<uint8_t> tag(tag_size);
            for (auto& byte : tag) if (!reader.read_u8(byte)) throw std::invalid_argument("invalid protocol snapshot");
            if (!reader.read_le32(layout)) throw std::invalid_argument("invalid protocol snapshot");
            opcodes.push_back({kind, std::move(tag), layout});
        }
        uint32_t layout_count = 0;
        if (!reader.read_le32(layout_count)) throw std::invalid_argument("invalid protocol snapshot");
        layouts.reserve(layout_count);
        for (uint32_t index = 0; index < layout_count; ++index) {
            Layout layout;
            uint16_t field_count = 0; uint16_t reserved = 0;
            if (!reader.read_le32(layout.id) || !reader.read_le32(layout.max_payload_bytes) ||
                !reader.read_le16(field_count) || !reader.read_le16(reserved)) throw std::invalid_argument("invalid protocol snapshot");
            layout.parser_strategy = reserved;
            layout.fields.reserve(field_count);
            for (uint16_t field_index = 0; field_index < field_count; ++field_index) {
                uint16_t kind = 0; uint16_t flags = 0; FieldDescriptor field;
                if (!reader.read_le16(kind) || !reader.read_le16(flags) || !reader.read_le32(field.offset) ||
                    !reader.read_le32(field.size) || !reader.read_le32(field.max_count)) throw std::invalid_argument("invalid protocol snapshot");
                field.kind = static_cast<ProtocolFieldKind>(kind);
                field.flags = static_cast<ProtocolFieldFlags>(flags);
                layout.fields.push_back(field);
            }
            layouts.push_back(std::move(layout));
        }
        if (reader.remaining() != 0) throw std::invalid_argument("invalid protocol snapshot");
    }

    const Layout* layout(uint32_t id) const noexcept {
        const auto found = std::find_if(layouts.begin(), layouts.end(), [id](const auto& item) { return item.id == id; });
        return found == layouts.end() ? nullptr : &*found;
    }

    bool remember(const Identity& identity) {
        if (!seen.insert(identity).second) return false;
        seen_order.push_back(identity);
        if (seen_order.size() > deduplication_limit) { seen.erase(seen_order.front()); seen_order.pop_front(); }
        return true;
    }

    void reset() noexcept {
        seen.clear();
        seen_order.clear();
        mob_codes.clear();
        echo_actions.clear();
        echo_clone_actors.clear();
    }

    bool populate(DecodedEvent& event, uint16_t protocol_kind, const FieldReader& fields) const {
        const auto found = std::find_if(parser_table.begin(), parser_table.end(), [protocol_kind](const auto& entry) {
            return entry.protocol_kind == protocol_kind;
        });
        return found != parser_table.end() && found->parser(event, fields);
    }

    std::vector<ProtocolDecodeOutput> decode(const ProtocolMessage& message) {
        if (message.stream_message_id != 0 && !remember(identity_of(message))) return {};
        size_t prefix_size = 0;
        if (!parse_frame_prefix(message.bytes, prefix_size)) return {diagnostic(message, DecodeDiagnosticCode::invalid_frame)};
        auto body = std::span<const uint8_t>(message.bytes).subspan(prefix_size);
        if (!body.empty() && body.front() >= 0xf0u && body.front() <= 0xfeu) body = body.subspan(1u);
        if (packet_magic.size() <= body.size() &&
            std::equal(packet_magic.begin(), packet_magic.end(), body.begin())) {
            body = body.subspan(packet_magic.size());
        }
        const OpcodeDescriptor* opcode = nullptr;
        for (const auto& candidate : opcodes) {
            if (candidate.tag.size() <= body.size() && std::equal(candidate.tag.begin(), candidate.tag.end(), body.begin()) &&
                (opcode == nullptr || candidate.tag.size() > opcode->tag.size())) opcode = &candidate;
        }
        if (opcode == nullptr) {
            return {unknown_event(message)};
        }
        if (opcode->layout_id == 0) return {unknown_event(message)};
        const Layout* active_layout = layout(opcode->layout_id);
        if (active_layout == nullptr) return {diagnostic(message, DecodeDiagnosticCode::invalid_layout)};
        const auto payload = body.subspan(opcode->tag.size());
        if (payload.size() > active_layout->max_payload_bytes) return {diagnostic(message, DecodeDiagnosticCode::payload_too_large)};
        if (opcode->kind == 203u && active_layout->parser_strategy == 1u) {
            std::array<uint32_t, 3> echo{};
            if (!parse_current_action(payload, echo)) {
                if (profile_version >= production_profile_floor) return {unknown_event(message)};
                return {diagnostic(message, DecodeDiagnosticCode::invalid_layout)};
            }
            if (echo[2] != 0u && echo_clone_actors.contains(echo[0])) {
                constexpr uint64_t echo_window_ns = 1'000'000'000u;
                std::erase_if(echo_actions, [&](const EchoAction& action) {
                    return message.first_timestamp_ns > action.timestamp_ns + echo_window_ns;
                });
                if (echo_actions.size() >= 4096u)
                    return {diagnostic(message, DecodeDiagnosticCode::resource_limit)};
                echo_actions.push_back({echo[0], echo[1], echo[2], message.first_timestamp_ns});
            }
            return {};
        }
        DecodedEvent event(base_event(message, NM_EVENT_UNKNOWN_PROTOCOL));
        const bool focused = active_layout->parser_strategy == 1u &&
                            ((opcode->kind == 1u && parse_current_damage(event, payload)) ||
                             (opcode->kind == 2u && parse_current_dot(event, payload)) ||
                             (opcode->kind == 3u && parse_current_buff(event, payload, true)) ||
                             (opcode->kind == 4u && parse_current_buff(event, payload, false)) ||
                             (opcode->kind == 5u && parse_current_actor(event, payload, true)) ||
                             ((opcode->kind == 6u || opcode->kind == 11u) && parse_current_actor(event, payload, false)) ||
                             (opcode->kind == 7u && parse_current_mob(event, payload)) ||
                             (opcode->kind == 8u && parse_current_boss_hp(event, payload)) ||
                             ((opcode->kind == 103u || opcode->kind == 201u) && parse_current_content(event, payload)) ||
                             (opcode->kind == 202u && parse_current_combat_state(event, payload)));
        if (focused) {
            auto& record = event.mutable_record();
            if (opcode->kind == 1u) {
                constexpr uint64_t echo_window_ns = 1'000'000'000u;
                std::erase_if(echo_actions, [&](const EchoAction& action) {
                    return message.first_timestamp_ns > action.timestamp_ns + echo_window_ns;
                });
                const auto found = std::find_if(echo_actions.begin(), echo_actions.end(), [&](const EchoAction& action) {
                    return action.actor == record.actor_id && action.target == record.target_id &&
                           action.skill == record.skill_id && message.first_timestamp_ns >= action.timestamp_ns;
                });
                if (found != echo_actions.end()) { echo_actions.erase(found); return {}; }
            }
            if (opcode->kind == 7u) {
                const bool known_actor = mob_codes.contains(record.actor_id);
                if (!known_actor && mob_codes.size() >= 4096u) {
                    std::vector<ProtocolDecodeOutput> outputs;
                    outputs.emplace_back(std::move(event));
                    outputs.emplace_back(diagnostic(message, DecodeDiagnosticCode::resource_limit));
                    return outputs;
                }
                mob_codes.insert_or_assign(record.actor_id, record.mob_id);
                if (record.mob_id == 0u && record.state == 0u) echo_clone_actors.insert(record.actor_id);
            } else if (opcode->kind == 8u) {
                const auto found = mob_codes.find(record.actor_id);
                if (found != mob_codes.end()) record.boss_id = found->second;
            }
            return {std::move(event)};
        }
        if (!populate(event, opcode->kind, FieldReader(payload, *active_layout))) {
            if (profile_version >= production_profile_floor) return {unknown_event(message)};
            return {diagnostic(message, DecodeDiagnosticCode::invalid_layout)};
        }
        if (opcode->kind == 3u) event.mutable_record().buff_operation = NM_BUFF_OPERATION_APPLY;
        else if (opcode->kind == 4u) event.mutable_record().buff_operation = NM_BUFF_OPERATION_REMOVE;
        else if (opcode->kind == 10u) echo_clone_actors.erase(event.view().actor_id);
        return {std::move(event)};
    }
};

ProtocolDecoder::ProtocolDecoder(std::span<const uint8_t> validated_snapshot)
    : impl_(validate_protocol_snapshot_v1(validated_snapshot)
                ? std::make_unique<Impl>(validated_snapshot)
                : throw std::invalid_argument("invalid protocol snapshot")) {}
ProtocolDecoder::~ProtocolDecoder() = default;
ProtocolDecoder::ProtocolDecoder(ProtocolDecoder&&) noexcept = default;
ProtocolDecoder& ProtocolDecoder::operator=(ProtocolDecoder&&) noexcept = default;
std::vector<ProtocolDecodeOutput> ProtocolDecoder::decode(const ProtocolMessage& message) { return impl_->decode(message); }
void ProtocolDecoder::reset() noexcept { impl_->reset(); }

}  // namespace namter
