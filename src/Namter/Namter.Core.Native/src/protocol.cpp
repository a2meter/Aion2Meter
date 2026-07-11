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
#include <utility>
#include <vector>

namespace namter {
namespace {

constexpr uint32_t abi_version = 1;
constexpr size_t diagnostic_byte_limit = 256;
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
    std::vector<FieldDescriptor> fields;
};

struct OpcodeDescriptor {
    uint16_t kind = 0;
    std::vector<uint8_t> tag;
    uint32_t layout_id = 0;
};

class FieldReader {
public:
    FieldReader(std::span<const uint8_t> payload, const Layout& layout) : payload_(payload), layout_(layout) {}

    bool read_u8(ProtocolFieldKind kind, uint8_t& value) const { uint64_t wide = 0; if (!read_integer(kind, wide) || wide > 0xffu) return false; value = static_cast<uint8_t>(wide); return true; }
    bool read_u16(ProtocolFieldKind kind, uint16_t& value) const { uint64_t wide = 0; if (!read_integer(kind, wide) || wide > 0xffffu) return false; value = static_cast<uint16_t>(wide); return true; }
    bool read_u32(ProtocolFieldKind kind, uint32_t& value) const { uint64_t wide = 0; if (!read_integer(kind, wide) || wide > std::numeric_limits<uint32_t>::max()) return false; value = static_cast<uint32_t>(wide); return true; }
    bool read_u64(ProtocolFieldKind kind, uint64_t& value) const { return read_integer(kind, value); }

    bool read_utf8(ProtocolFieldKind kind, std::string& value) const {
        const auto* field = find(kind);
        if (field == nullptr || field->flags != ProtocolFieldFlags::utf8 || field->offset > payload_.size()) return false;
        SpanReader reader(payload_.subspan(field->offset));
        uint32_t length = 0;
        return reader.read_var_u32(length) && length <= field->max_count && reader.read_utf8(length, value);
    }

private:
    const FieldDescriptor* find(ProtocolFieldKind kind) const noexcept {
        const auto found = std::find_if(layout_.fields.begin(), layout_.fields.end(), [kind](const auto& field) { return field.kind == kind; });
        return found == layout_.fields.end() ? nullptr : &*found;
    }

    bool read_integer(ProtocolFieldKind kind, uint64_t& value) const {
        const auto* field = find(kind);
        if (field == nullptr || field->offset > payload_.size()) return false;
        SpanReader reader(payload_.subspan(field->offset));
        if (field->flags == ProtocolFieldFlags::variable_uint) {
            uint32_t result = 0;
            if (!reader.read_var_u32(result) || reader.offset() > field->max_count) return false;
            value = result;
            return true;
        }
        if (field->flags != ProtocolFieldFlags::fixed_little_endian || field->max_count != 1u) return false;
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
    std::vector<uint8_t> packet_magic;
    std::vector<OpcodeDescriptor> opcodes;
    std::vector<Layout> layouts;
    std::unordered_set<Identity, IdentityHash> seen;
    std::deque<Identity> seen_order;

    explicit Impl(std::span<const uint8_t> snapshot) { parse_snapshot(snapshot); }

    void parse_snapshot(std::span<const uint8_t> snapshot) {
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
            DecodedEvent unknown(base_event(message, NM_EVENT_UNKNOWN_PROTOCOL));
            const size_t retained = std::min(message.bytes.size(), unknown_payload_limit);
            unknown.mutable_payload().assign(message.bytes.begin(), message.bytes.begin() + static_cast<std::ptrdiff_t>(retained));
            return {std::move(unknown)};
        }
        const Layout* active_layout = layout(opcode->layout_id);
        if (active_layout == nullptr) return {diagnostic(message, DecodeDiagnosticCode::invalid_layout)};
        const auto payload = body.subspan(opcode->tag.size());
        if (payload.size() > active_layout->max_payload_bytes) return {diagnostic(message, DecodeDiagnosticCode::payload_too_large)};
        DecodedEvent event(base_event(message, NM_EVENT_UNKNOWN_PROTOCOL));
        if (!populate(event, opcode->kind, FieldReader(payload, *active_layout))) {
            return {diagnostic(message, DecodeDiagnosticCode::invalid_layout)};
        }
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
