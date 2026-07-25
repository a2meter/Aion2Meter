#include <array>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <span>
#include <string>
#include <utility>
#include <vector>

#include <gtest/gtest.h>

#include "event.hpp"

namespace {

using namter::DecodeDiagnosticCode;
using namter::DecodedEvent;
using namter::ProtocolDecoder;
using namter::ProtocolMessage;

constexpr uint16_t fixed = 0;
constexpr uint16_t var_uint = 1;
constexpr uint16_t utf8 = 2;
constexpr uint16_t sequential_fixed = 3;
constexpr uint16_t sequential_var_uint = 4;

struct Field {
    uint16_t kind;
    uint16_t flags;
    uint32_t offset;
    uint32_t size;
    uint32_t max_count;
};

struct Opcode {
    uint16_t kind;
    std::vector<uint8_t> tag;
    uint32_t layout;
};

void append_u16(std::vector<uint8_t>& bytes, uint16_t value) {
    bytes.push_back(static_cast<uint8_t>(value));
    bytes.push_back(static_cast<uint8_t>(value >> 8u));
}

void append_u32(std::vector<uint8_t>& bytes, uint32_t value) {
    for (int shift = 0; shift < 32; shift += 8) bytes.push_back(static_cast<uint8_t>(value >> shift));
}

void append_u64(std::vector<uint8_t>& bytes, uint64_t value) {
    append_u32(bytes, static_cast<uint32_t>(value));
    append_u32(bytes, static_cast<uint32_t>(value >> 32u));
}

void write_u16(std::vector<uint8_t>& bytes, size_t offset, uint16_t value) {
    bytes[offset] = static_cast<uint8_t>(value);
    bytes[offset + 1u] = static_cast<uint8_t>(value >> 8u);
}

void write_u32(std::vector<uint8_t>& bytes, size_t offset, uint32_t value) {
    for (int shift = 0; shift < 32; shift += 8) bytes[offset++] = static_cast<uint8_t>(value >> shift);
}

void write_u64(std::vector<uint8_t>& bytes, size_t offset, uint64_t value) {
    write_u32(bytes, offset, static_cast<uint32_t>(value));
    write_u32(bytes, offset + 4u, static_cast<uint32_t>(value >> 32u));
}

uint32_t crc32(std::span<const uint8_t> bytes) {
    uint32_t crc = std::numeric_limits<uint32_t>::max();
    for (size_t index = 0; index < bytes.size(); ++index) {
        const uint8_t value = index >= 12u && index < 16u ? uint8_t{0} : bytes[index];
        crc ^= value;
        for (int bit = 0; bit < 8; ++bit) {
            const uint32_t mask = 0u - (crc & 1u);
            crc = (crc >> 1u) ^ (0xedb88320u & mask);
        }
    }
    return ~crc;
}

std::vector<Field> all_fields() {
    using enum namter::ProtocolFieldKind;
    return {
        {static_cast<uint16_t>(actor_id), fixed, 0, 4, 1},
        {static_cast<uint16_t>(target_id), fixed, 4, 4, 1},
        {static_cast<uint16_t>(owner_id), fixed, 8, 4, 1},
        {static_cast<uint16_t>(skill_id), fixed, 12, 4, 1},
        {static_cast<uint16_t>(buff_id), fixed, 16, 4, 1},
        {static_cast<uint16_t>(mob_id), fixed, 20, 4, 1},
        {static_cast<uint16_t>(boss_id), fixed, 24, 4, 1},
        {static_cast<uint16_t>(content_id), fixed, 28, 4, 1},
        {static_cast<uint16_t>(dungeon_id), fixed, 32, 4, 1},
        {static_cast<uint16_t>(party_id), fixed, 36, 4, 1},
        {static_cast<uint16_t>(server_id), fixed, 40, 2, 1},
        {static_cast<uint16_t>(job_id), fixed, 42, 2, 1},
        {static_cast<uint16_t>(damage), fixed, 44, 8, 1},
        {static_cast<uint16_t>(multi_damage), fixed, 52, 8, 1},
        {static_cast<uint16_t>(healing), fixed, 60, 8, 1},
        {static_cast<uint16_t>(current_hp), fixed, 68, 8, 1},
        {static_cast<uint16_t>(max_hp), fixed, 76, 8, 1},
        {static_cast<uint16_t>(special_mask), fixed, 84, 4, 1},
        {static_cast<uint16_t>(duration_ms), fixed, 88, 4, 1},
        {static_cast<uint16_t>(state), fixed, 92, 1, 1},
        {static_cast<uint16_t>(action), fixed, 93, 1, 1},
        {static_cast<uint16_t>(damage_type), fixed, 94, 1, 1},
        {static_cast<uint16_t>(is_dot), fixed, 95, 1, 1},
        {static_cast<uint16_t>(is_self), fixed, 96, 1, 1},
        {static_cast<uint16_t>(is_boss), fixed, 97, 1, 1},
        {static_cast<uint16_t>(name), utf8, 98, 1, 20},
    };
}

std::vector<Field> fields_for(uint16_t opcode_kind) {
    auto fields = all_fields();
    const auto keep = [opcode_kind](uint16_t kind) {
        using enum namter::ProtocolFieldKind;
        switch (opcode_kind) {
            case 1: case 2: return kind == static_cast<uint16_t>(actor_id) || kind == static_cast<uint16_t>(target_id) ||
                kind == static_cast<uint16_t>(skill_id) || kind == static_cast<uint16_t>(damage) ||
                kind == static_cast<uint16_t>(multi_damage) || kind == static_cast<uint16_t>(healing) ||
                kind == static_cast<uint16_t>(special_mask) || kind == static_cast<uint16_t>(damage_type) ||
                kind == static_cast<uint16_t>(is_dot);
            case 3: case 4: return kind == static_cast<uint16_t>(owner_id) || kind == static_cast<uint16_t>(target_id) ||
                kind == static_cast<uint16_t>(buff_id) || kind == static_cast<uint16_t>(duration_ms) || kind == static_cast<uint16_t>(action);
            case 5: case 6: case 11: return kind == static_cast<uint16_t>(actor_id) || kind == static_cast<uint16_t>(owner_id) ||
                kind == static_cast<uint16_t>(server_id) || kind == static_cast<uint16_t>(job_id) ||
                kind == static_cast<uint16_t>(is_self) || kind == static_cast<uint16_t>(name);
            case 7: return kind == static_cast<uint16_t>(actor_id) || kind == static_cast<uint16_t>(owner_id) ||
                kind == static_cast<uint16_t>(mob_id) || kind == static_cast<uint16_t>(boss_id) ||
                kind == static_cast<uint16_t>(current_hp) || kind == static_cast<uint16_t>(max_hp) ||
                kind == static_cast<uint16_t>(is_boss) || kind == static_cast<uint16_t>(name);
            case 8: return kind == static_cast<uint16_t>(actor_id) || kind == static_cast<uint16_t>(boss_id) ||
                kind == static_cast<uint16_t>(current_hp) || kind == static_cast<uint16_t>(max_hp);
            case 10: return kind == static_cast<uint16_t>(actor_id);
            case 101: case 102: case 104: case 105: case 106: case 107: case 108:
                return kind == static_cast<uint16_t>(party_id) || kind == static_cast<uint16_t>(actor_id) ||
                    kind == static_cast<uint16_t>(content_id) || kind == static_cast<uint16_t>(dungeon_id) ||
                    kind == static_cast<uint16_t>(action) || kind == static_cast<uint16_t>(name);
            case 103: case 201: return kind == static_cast<uint16_t>(content_id) || kind == static_cast<uint16_t>(dungeon_id) ||
                kind == static_cast<uint16_t>(state) || kind == static_cast<uint16_t>(name);
            case 202: return kind == static_cast<uint16_t>(actor_id) || kind == static_cast<uint16_t>(state);
            case 203: return kind == static_cast<uint16_t>(actor_id) || kind == static_cast<uint16_t>(target_id) ||
                kind == static_cast<uint16_t>(skill_id) || kind == static_cast<uint16_t>(action);
            default: return false;
        }
    };
    std::erase_if(fields, [&](const Field& field) { return !keep(field.kind); });
    return fields;
}

std::vector<uint8_t> snapshot_with_layouts(
    std::vector<Opcode> opcodes,
    const std::vector<std::vector<Field>>& layouts,
    uint16_t parser_strategy = 0,
    uint32_t max_payload_bytes = 128) {
    std::vector<uint8_t> bytes{'N', 'M', 'P', 'S'};
    append_u16(bytes, 1); append_u16(bytes, 28); append_u32(bytes, 0); append_u32(bytes, 0);
    append_u64(bytes, 7); append_u32(bytes, 3);
    append_u16(bytes, 3); bytes.insert(bytes.end(), {0x06, 0x00, 0x36});
    append_u16(bytes, 1); append_u16(bytes, 13328);
    append_u32(bytes, static_cast<uint32_t>(opcodes.size()));
    for (const auto& opcode : opcodes) {
        append_u16(bytes, opcode.kind); append_u16(bytes, static_cast<uint16_t>(opcode.tag.size()));
        bytes.insert(bytes.end(), opcode.tag.begin(), opcode.tag.end()); append_u32(bytes, opcode.layout);
    }
    append_u32(bytes, static_cast<uint32_t>(layouts.size()));
    for (size_t index = 0; index < layouts.size(); ++index) {
        append_u32(bytes, static_cast<uint32_t>(index + 1u)); append_u32(bytes, max_payload_bytes);
        append_u16(bytes, static_cast<uint16_t>(layouts[index].size())); append_u16(bytes, parser_strategy);
        for (const auto& field : layouts[index]) {
            append_u16(bytes, field.kind); append_u16(bytes, field.flags); append_u32(bytes, field.offset);
            append_u32(bytes, field.size); append_u32(bytes, field.max_count);
        }
    }
    write_u32(bytes, 8, static_cast<uint32_t>(bytes.size()));
    write_u32(bytes, 12, crc32(bytes));
    return bytes;
}

std::vector<uint8_t> snapshot(std::vector<Opcode> opcodes) {
    std::vector<std::vector<Field>> layouts;
    layouts.reserve(opcodes.size());
    for (size_t index = 0; index < opcodes.size(); ++index) {
        opcodes[index].layout = static_cast<uint32_t>(index + 1u);
        layouts.push_back(fields_for(opcodes[index].kind));
    }
    return snapshot_with_layouts(std::move(opcodes), layouts);
}

std::vector<uint8_t> snapshot(std::vector<Opcode> opcodes, std::vector<Field> fields) {
    return snapshot_with_layouts(std::move(opcodes), {std::move(fields)});
}

std::vector<uint8_t> snapshot_with_strategy(std::vector<Opcode> opcodes, uint16_t parser_strategy) {
    std::vector<std::vector<Field>> layouts;
    layouts.reserve(opcodes.size());
    for (size_t index = 0; index < opcodes.size(); ++index) {
        opcodes[index].layout = static_cast<uint32_t>(index + 1u);
        layouts.push_back(fields_for(opcodes[index].kind));
    }
    return snapshot_with_layouts(std::move(opcodes), layouts, parser_strategy);
}

std::vector<uint8_t> payload() {
    std::vector<uint8_t> bytes(128, 0);
    write_u32(bytes, 0, 101); write_u32(bytes, 4, 202); write_u32(bytes, 8, 303);
    write_u32(bytes, 12, 404); write_u32(bytes, 16, 505); write_u32(bytes, 20, 606);
    write_u32(bytes, 24, 707); write_u32(bytes, 28, 808); write_u32(bytes, 32, 909);
    write_u32(bytes, 36, 1001); write_u16(bytes, 40, 1102); write_u16(bytes, 42, 1203);
    write_u64(bytes, 44, 13'004); write_u64(bytes, 52, 14'005); write_u64(bytes, 60, 15'006);
    write_u64(bytes, 68, 16'007); write_u64(bytes, 76, 17'008); write_u32(bytes, 84, 0x1a2b3c4d);
    write_u32(bytes, 88, 18'009); bytes[92] = 19; bytes[93] = 20; bytes[94] = 21;
    bytes[95] = 1; bytes[96] = 1; bytes[97] = 1; bytes[98] = 6;
    std::copy_n("Namter", 6, bytes.begin() + 99);
    return bytes;
}

std::vector<uint8_t> encode_var(uint32_t value) {
    std::vector<uint8_t> bytes;
    do { uint8_t byte = static_cast<uint8_t>(value & 0x7fu); value >>= 7u; if (value) byte |= 0x80u; bytes.push_back(byte); } while (value);
    return bytes;
}

ProtocolMessage message(uint8_t tag, uint64_t file_offset = 400) {
    auto body = payload();
    body.insert(body.begin(), tag);
    auto bytes = encode_var(static_cast<uint32_t>(body.size() + 4u));
    bytes.insert(bytes.end(), body.begin(), body.end());
    return ProtocolMessage{
        .flow = {.source_address = 1, .destination_address = 2, .source_port = 13328, .destination_port = 50000},
        .epoch = 9,
        .bytes = std::move(bytes),
        .first_provenance = {.source = namter::CaptureSource::pcap, .timestamp_ns = 300, .link_type = 101,
                             .captured_length = 140, .original_length = 140, .file_offset = file_offset},
        .last_provenance = {.source = namter::CaptureSource::pcap, .timestamp_ns = 301, .link_type = 101,
                            .captured_length = 140, .original_length = 140, .file_offset = file_offset},
        .first_timestamp_ns = 300,
        .last_timestamp_ns = 301,
    };
}

DecodedEvent only_event(std::vector<namter::ProtocolDecodeOutput> outputs) {
    EXPECT_EQ(outputs.size(), 1u);
    return std::move(std::get<DecodedEvent>(outputs.front()));
}

DecodedEvent decode_real_fixture(
    uint16_t kind,
    std::vector<uint8_t> tag,
    const std::vector<Field>& fields,
    const std::vector<uint8_t>& frame,
    uint64_t file_offset,
    uint32_t max_payload_bytes = 128) {
    ProtocolDecoder decoder(snapshot_with_layouts({{kind, std::move(tag), 1}}, {fields}, 1, max_payload_bytes));
    auto value = message(0x11, file_offset);
    value.bytes = frame;
    return only_event(decoder.decode(value));
}

void expect_real_fixture_boundaries(
    uint16_t kind,
    const std::vector<uint8_t>& tag,
    const std::vector<Field>& fields,
    const std::vector<uint8_t>& frame) {
    auto complete = message(0x11);
    complete.bytes = frame;
    for (size_t size = 0; size < frame.size(); ++size) {
        ProtocolDecoder decoder(snapshot_with_layouts({{kind, tag, 1}}, {fields}, 1));
        auto truncated = complete;
        truncated.bytes.resize(size);
        const auto outputs = decoder.decode(truncated);
        ASSERT_EQ(outputs.size(), 1u) << size;
        EXPECT_TRUE(std::holds_alternative<namter::ProtocolDecodeDiagnostic>(outputs.front())) << size;
    }
    for (const uint8_t mutated : {uint8_t{0}, static_cast<uint8_t>(frame.front() - 1u),
                                  static_cast<uint8_t>(frame.front() + 1u), uint8_t{0x80}}) {
        ProtocolDecoder decoder(snapshot_with_layouts({{kind, tag, 1}}, {fields}, 1));
        auto value = complete;
        value.bytes[0] = mutated;
        const auto outputs = decoder.decode(value);
        ASSERT_EQ(outputs.size(), 1u);
        EXPECT_TRUE(std::holds_alternative<namter::ProtocolDecodeDiagnostic>(outputs.front()));
    }
}

void expect_provenance(const nm_event_v1& event, uint64_t file_offset) {
    EXPECT_EQ(event.first_timestamp_ns, 300u);
    EXPECT_EQ(event.last_timestamp_ns, 301u);
    EXPECT_EQ(event.epoch, 9u);
    EXPECT_EQ(event.first_file_offset, file_offset);
    EXPECT_EQ(event.last_file_offset, file_offset);
    EXPECT_EQ(event.source_address, 1u);
    EXPECT_EQ(event.destination_address, 2u);
    EXPECT_EQ(event.source_port, 13328u);
    EXPECT_EQ(event.destination_port, 50000u);
}

TEST(ProtocolDecoder, DecodesEveryTypedClosedEventFieldAndProvenance) {
    const std::vector<Opcode> opcodes{
        {1, {0x11}, 1}, {2, {0x12}, 1}, {3, {0x13}, 1}, {4, {0x14}, 1}, {5, {0x15}, 1},
        {6, {0x16}, 1}, {7, {0x17}, 1}, {8, {0x18}, 1}, {10, {0x1a}, 1},
        {101, {0x21}, 1}, {201, {0x31}, 1}, {202, {0x32}, 1},
    };
    ProtocolDecoder decoder(snapshot(opcodes));

    const auto damage = only_event(decoder.decode(message(0x11))).view();
    EXPECT_EQ(damage.kind, static_cast<uint32_t>(NM_EVENT_DAMAGE)); EXPECT_EQ(damage.actor_id, 101u); EXPECT_EQ(damage.target_id, 202u);
    EXPECT_EQ(damage.skill_id, 404u); EXPECT_EQ(damage.damage, 13'004u); EXPECT_EQ(damage.multi_damage, 14'005u);
    EXPECT_EQ(damage.healing, 15'006u); EXPECT_EQ(damage.special_mask, 0x1a2b3c4du); EXPECT_EQ(damage.damage_type, 21u);
    EXPECT_EQ(damage.is_dot, 1u); expect_provenance(damage, 400);

    const auto dot = only_event(decoder.decode(message(0x12, 401))).view();
    EXPECT_EQ(dot.kind, static_cast<uint32_t>(NM_EVENT_DOT)); EXPECT_EQ(dot.actor_id, 101u); EXPECT_EQ(dot.target_id, 202u);
    EXPECT_EQ(dot.skill_id, 404u); EXPECT_EQ(dot.damage, 13'004u); EXPECT_EQ(dot.multi_damage, 14'005u);
    EXPECT_EQ(dot.healing, 15'006u); EXPECT_EQ(dot.special_mask, 0x1a2b3c4du); EXPECT_EQ(dot.damage_type, 21u);
    EXPECT_EQ(dot.is_dot, 1u); expect_provenance(dot, 401);

    const auto buff = only_event(decoder.decode(message(0x13, 402))).view();
    EXPECT_EQ(buff.kind, static_cast<uint32_t>(NM_EVENT_BUFF)); EXPECT_EQ(buff.owner_id, 303u); EXPECT_EQ(buff.target_id, 202u);
    EXPECT_EQ(buff.buff_id, 505u); EXPECT_EQ(buff.duration_ms, 18'009u); EXPECT_EQ(buff.action, 20u);
    EXPECT_EQ(buff.buff_operation, static_cast<uint8_t>(NM_BUFF_OPERATION_APPLY)); expect_provenance(buff, 402);

    const auto removed_buff = only_event(decoder.decode(message(0x14, 411))).view();
    EXPECT_EQ(removed_buff.action, 20u);
    EXPECT_EQ(removed_buff.buff_operation, static_cast<uint8_t>(NM_BUFF_OPERATION_REMOVE));

    const auto self_owner = only_event(decoder.decode(message(0x15, 403)));
    const auto self = self_owner.view();
    EXPECT_EQ(self.kind, static_cast<uint32_t>(NM_EVENT_SELF_ACTOR)); EXPECT_EQ(self.actor_id, 101u); EXPECT_EQ(self.owner_id, 303u);
    EXPECT_EQ(self.server_id, 1102u); EXPECT_EQ(self.job_id, 1203u); EXPECT_EQ(self.is_self, 1u);
    EXPECT_EQ(std::string(reinterpret_cast<const char*>(self.name), self.name_size), "Namter");
    expect_provenance(self, 403);
    const auto other_owner = only_event(decoder.decode(message(0x16, 404)));
    const auto other = other_owner.view();
    EXPECT_EQ(other.kind, static_cast<uint32_t>(NM_EVENT_OTHER_ACTOR)); EXPECT_EQ(other.actor_id, 101u); EXPECT_EQ(other.owner_id, 303u);
    EXPECT_EQ(other.server_id, 1102u); EXPECT_EQ(other.job_id, 1203u); EXPECT_EQ(other.is_self, 0u);
    EXPECT_EQ(std::string(reinterpret_cast<const char*>(other.name), other.name_size), "Namter");
    expect_provenance(other, 404);

    const auto mob_owner = only_event(decoder.decode(message(0x17, 405)));
    const auto mob = mob_owner.view();
    EXPECT_EQ(mob.kind, static_cast<uint32_t>(NM_EVENT_MOB_SPAWN)); EXPECT_EQ(mob.actor_id, 101u); EXPECT_EQ(mob.owner_id, 303u);
    EXPECT_EQ(mob.mob_id, 606u); EXPECT_EQ(mob.boss_id, 707u); EXPECT_EQ(mob.current_hp, 16'007u);
    EXPECT_EQ(mob.max_hp, 17'008u); EXPECT_EQ(mob.is_boss, 1u);
    EXPECT_EQ(std::string(reinterpret_cast<const char*>(mob.name), mob.name_size), "Namter");
    expect_provenance(mob, 405);

    const auto boss = only_event(decoder.decode(message(0x18, 406))).view();
    EXPECT_EQ(boss.kind, static_cast<uint32_t>(NM_EVENT_BOSS_HP)); EXPECT_EQ(boss.actor_id, 101u); EXPECT_EQ(boss.boss_id, 707u);
    EXPECT_EQ(boss.current_hp, 16'007u); EXPECT_EQ(boss.max_hp, 17'008u); expect_provenance(boss, 406);

    const auto removed = only_event(decoder.decode(message(0x1a, 407))).view();
    EXPECT_EQ(removed.kind, static_cast<uint32_t>(NM_EVENT_ENTITY_REMOVED)); EXPECT_EQ(removed.actor_id, 101u); expect_provenance(removed, 407);

    const auto party_owner = only_event(decoder.decode(message(0x21, 408)));
    const auto party = party_owner.view();
    EXPECT_EQ(party.kind, static_cast<uint32_t>(NM_EVENT_PARTY)); EXPECT_EQ(party.party_id, 1001u); EXPECT_EQ(party.actor_id, 101u);
    EXPECT_EQ(party.content_id, 808u); EXPECT_EQ(party.dungeon_id, 909u); EXPECT_EQ(party.action, 20u);
    EXPECT_EQ(std::string(reinterpret_cast<const char*>(party.name), party.name_size), "Namter");
    expect_provenance(party, 408);

    const auto content_owner = only_event(decoder.decode(message(0x31, 409)));
    const auto content = content_owner.view();
    EXPECT_EQ(content.kind, static_cast<uint32_t>(NM_EVENT_CONTENT)); EXPECT_EQ(content.content_id, 808u); EXPECT_EQ(content.dungeon_id, 909u);
    EXPECT_EQ(content.state, 19u); EXPECT_EQ(std::string(reinterpret_cast<const char*>(content.name), content.name_size), "Namter");
    expect_provenance(content, 409);

    const auto combat = only_event(decoder.decode(message(0x32, 410))).view();
    EXPECT_EQ(combat.kind, static_cast<uint32_t>(NM_EVENT_COMBAT_STATE)); EXPECT_EQ(combat.actor_id, 101u); EXPECT_EQ(combat.state, 19u);
    expect_provenance(combat, 410);
}

TEST(ProtocolDecoder, UnknownTagsBecomeBoundedUnknownEvents) {
    ProtocolDecoder decoder(snapshot({{1, {0x11}, 1}}));
    auto unknown = message(0x7f);
    const auto event_owner = only_event(decoder.decode(unknown));
    const auto event = event_owner.view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_UNKNOWN_PROTOCOL));
    ASSERT_NE(event.payload, nullptr); EXPECT_EQ(event.payload_size, unknown.bytes.size());
    EXPECT_EQ(std::vector<uint8_t>(event.payload, event.payload + event.payload_size), unknown.bytes);
}

TEST(ProtocolDecoder, ProductionProfilePreservesUnsupportedKnownTagVariantsAsUnknown) {
    auto bytes = snapshot_with_strategy({{1, {0x11}, 1}}, 1);
    write_u32(bytes, 24, 20260710u);
    write_u32(bytes, 12, 0u);
    write_u32(bytes, 12, crc32(bytes));
    ProtocolDecoder decoder(bytes);
    auto value = message(0x11);
    value.bytes = encode_var(6u);
    value.bytes.push_back(0x11);
    value.bytes.push_back(0x01);

    auto outputs = decoder.decode(value);
    ASSERT_EQ(outputs.size(), 1u);
    ASSERT_TRUE(std::holds_alternative<DecodedEvent>(outputs.front()));
    EXPECT_EQ(std::get<DecodedEvent>(outputs.front()).view().kind,
              static_cast<uint32_t>(NM_EVENT_UNKNOWN_PROTOCOL));
}

TEST(ProtocolDecoder, OversizedUnknownRetainsExactly512BytesAndAllProvenance) {
    ProtocolDecoder decoder(snapshot({{1, {0x11}, 1}}));
    auto unknown = message(0x7f);
    unknown.stream_message_id = 77;
    unknown.bytes.resize(900, 0x5a);
    unknown.bytes[0] = 0x86;
    unknown.bytes[1] = 0x07;

    const auto owner = only_event(decoder.decode(unknown));
    const auto event = owner.view();

    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_UNKNOWN_PROTOCOL));
    EXPECT_EQ(event.payload_size, 512u);
    EXPECT_EQ(event.first_timestamp_ns, 300u);
    EXPECT_EQ(event.last_timestamp_ns, 301u);
    EXPECT_EQ(event.epoch, 9u);
    EXPECT_EQ(event.first_file_offset, 400u);
    EXPECT_EQ(event.last_file_offset, 400u);
    EXPECT_EQ(event.source_address, 1u);
    EXPECT_EQ(event.destination_address, 2u);
    EXPECT_EQ(event.source_port, 13328u);
    EXPECT_EQ(event.destination_port, 50000u);
}

TEST(ProtocolDecoder, MatchesKnownTagsAfterTheActiveSnapshotMagicAndOptionalMarker) {
    ProtocolDecoder decoder(snapshot({{1, {0x11}, 1}}));
    auto bytes = payload();
    bytes.insert(bytes.begin(), {0xf2, 0x06, 0x00, 0x36, 0x11});
    auto framed = encode_var(static_cast<uint32_t>(bytes.size() + 4u));
    framed.insert(framed.end(), bytes.begin(), bytes.end());
    auto direct = message(0x11);
    direct.bytes = std::move(framed);

    const auto outputs = decoder.decode(direct);

    ASSERT_EQ(outputs.size(), 1u);
    ASSERT_TRUE(std::holds_alternative<DecodedEvent>(outputs.front()));
    EXPECT_EQ(std::get<DecodedEvent>(outputs.front()).view().kind, static_cast<uint32_t>(NM_EVENT_DAMAGE));
}

TEST(ProtocolDecoder, KnownInvalidLayoutsEmitOneDiagnosticAndNoPartialTypedEvent) {
    auto fields = all_fields();
    std::erase_if(fields, [](const Field& field) { return field.kind == static_cast<uint16_t>(namter::ProtocolFieldKind::damage); });
    EXPECT_THROW((void)ProtocolDecoder(snapshot({{1, {0x11}, 1}}, fields)), std::invalid_argument);
}

TEST(ProtocolDecoder, CanonicalTypedFrameRejectsEveryTruncationAndDeclaredLengthMutation) {
    const auto complete = message(0x11);
    expect_real_fixture_boundaries(1, {0x11}, fields_for(1), complete.bytes);
}

TEST(ProtocolDecoder, CertifiedRealEntityRemovalUsesOnlyTheCompleteVarintAndProvenance) {
    // Oracle-verified entity-removal frame from aion2_part001.pcap at record offset 247913.
    const std::vector<uint8_t> frame{0x0a, 0x21, 0x8d, 0xc9, 0x3f, 0x00, 0x01};
    const std::vector<Field> fields{{1, sequential_var_uint, 0, 1, 5}};
    const auto removed = decode_real_fixture(10, {0x21, 0x8d}, fields, frame, 247913).view();
    EXPECT_EQ(removed.kind, static_cast<uint32_t>(NM_EVENT_ENTITY_REMOVED));
    EXPECT_EQ(removed.actor_id, 8137u);
    EXPECT_EQ(removed.first_timestamp_ns, 300u);
    EXPECT_EQ(removed.last_timestamp_ns, 301u);
    EXPECT_EQ(removed.epoch, 9u);
    EXPECT_EQ(removed.first_file_offset, 247913u);
    EXPECT_EQ(removed.last_file_offset, 247913u);
    EXPECT_EQ(removed.source_address, 1u);
    EXPECT_EQ(removed.destination_address, 2u);
    EXPECT_EQ(removed.source_port, 13328u);
    EXPECT_EQ(removed.destination_port, 50000u);
    expect_real_fixture_boundaries(10, {0x21, 0x8d}, fields, frame);
}

TEST(ProtocolDecoder, EntityRemovalClearsCloneEchoSuppressionBeforeActorIdReuse) {
    ProtocolDecoder decoder(snapshot_with_strategy({
        {7, {0x17}, 1}, {10, {0x1a}, 1}, {203, {0x33}, 1}, {1, {0x11}, 1},
    }, 1));

    auto named_clone = message(0x17);
    std::vector<uint8_t> clone_body{0x17, 0x65, 0x00, 0x00, 0x01, 0x01, 'A', 0x00, 0x00, 0x00, 0x00, 0x00};
    named_clone.bytes = encode_var(static_cast<uint32_t>(clone_body.size() + 4u));
    named_clone.bytes.insert(named_clone.bytes.end(), clone_body.begin(), clone_body.end());
    ASSERT_EQ(only_event(decoder.decode(named_clone)).view().kind, static_cast<uint32_t>(NM_EVENT_MOB_SPAWN));

    const auto removed = only_event(decoder.decode(message(0x1a)));
    ASSERT_EQ(removed.view().actor_id, 101u);

    auto action = message(0x33);
    std::vector<uint8_t> action_body{0x33, 0x65, 0x00, 0x94, 0x01, 0x00, 0x00, 0x03, 0x00, 0xca, 0x01};
    action.bytes = encode_var(static_cast<uint32_t>(action_body.size() + 4u));
    action.bytes.insert(action.bytes.end(), action_body.begin(), action_body.end());
    EXPECT_TRUE(decoder.decode(action).empty());

    const auto damage = decoder.decode(message(0x11));
    ASSERT_EQ(damage.size(), 1u);
    ASSERT_TRUE(std::holds_alternative<DecodedEvent>(damage.front()));
    EXPECT_EQ(std::get<DecodedEvent>(damage.front()).view().kind, static_cast<uint32_t>(NM_EVENT_DAMAGE));
}

TEST(ProtocolDecoder, CertifiedProductionDamageFrameUsesCurrentConditionalWireShape) {
    const std::vector<uint8_t> frame{
        0x26,0x04,0x38,0xf4,0x92,0x01,0x06,0x00,0xc9,0x3f,0xda,0x77,0xbc,0x00,0x0a,0x03,
        0x0c,0x00,0x02,0x34,0xd1,0x9e,0x49,0x01,0x00,0x00,0x00,0xe4,0x95,0x01,0xfc,0xeb,
        0x05,0x01,0x00};
    const auto event = decode_real_fixture(1, {0x04,0x38}, fields_for(1), frame, 347151).view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_DAMAGE));
    EXPECT_EQ(event.actor_id, 8137u); EXPECT_EQ(event.target_id, 18804u);
    EXPECT_EQ(event.skill_id, 12351450u); EXPECT_EQ(event.damage, 95740u);
    EXPECT_EQ(event.multi_damage, 0u); EXPECT_EQ(event.healing, 0u);
    EXPECT_EQ(event.special_mask, 76u); EXPECT_EQ(event.damage_type, 3u);
    EXPECT_EQ(event.is_dot, 0u); expect_provenance(event, 347151);
}

TEST(ProtocolDecoder, CertifiedCategoryFourDamageConsumesMultiHitMarker) {
    const std::vector<uint8_t> frame{
        0x2d,0x04,0x38,0xf4,0x92,0x01,0x34,0x00,0xe8,0x6f,0x16,0x71,0x13,0x01,0x89,0x03,
        0x06,0xa6,0x96,0x6b,0x01,0x00,0x00,0x00,0xf0,0xa3,0x01,0xe9,0x8c,0x04,0x01,0x04,
        0xbd,0x34,0xbd,0x34,0xbd,0x34,0xbd,0x34,0x01,0x00};
    const auto event = decode_real_fixture(1, {0x04,0x38}, fields_for(1), frame, 428674).view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_DAMAGE));
    EXPECT_EQ(event.actor_id, 14312u); EXPECT_EQ(event.target_id, 18804u);
    EXPECT_EQ(event.skill_id, 18051350u); EXPECT_EQ(event.damage, 67177u);
    EXPECT_EQ(event.multi_damage, 26868u); EXPECT_EQ(event.special_mask, 64u);
    EXPECT_EQ(event.damage_type, 3u); expect_provenance(event, 428674);
}

TEST(ProtocolDecoder, CategoryFourDamageSupportsImplicitOneHitTail) {
    const std::vector<uint8_t> frame{
        0x26,0x04,0x38,0xf4,0x92,0x01,0x24,0x00,0xc9,0x3f,0xfa,0xd1,0xba,0x00,0x33,0x03,
        0xdb,0x46,0xf5,0x48,0x01,0x00,0x00,0x00,0xb4,0xa5,0x01,0x82,0xac,0x04,0x01,0xcd,
        0x37,0x01,0x00};
    const auto event = decode_real_fixture(1, {0x04,0x38}, fields_for(1), frame, 410856).view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_DAMAGE));
    EXPECT_EQ(event.multi_damage, 7117u); expect_provenance(event, 410856);
}

TEST(ProtocolDecoder, CategoryFourNoMultiTailRetainsHealingAfterPrefix) {
    const std::vector<uint8_t> frame{
        0x26,0x04,0x38,0xf4,0x92,0x01,0x14,0x04,0xf6,0x06,0x02,0xdc,0xc6,0x00,0xa2,0x03,
        0x7d,0x95,0xaa,0x4d,0x02,0x00,0x00,0x00,0xee,0xbd,0x01,0x9c,0x9a,0x01,0x01,0x02,
        0x00,0xa2,0x04};
    const auto event = decode_real_fixture(1, {0x04,0x38}, fields_for(1), frame, 586316).view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_DAMAGE));
    EXPECT_EQ(event.damage, 19740u); EXPECT_EQ(event.multi_damage, 0u);
    EXPECT_EQ(event.healing, 546u); expect_provenance(event, 586316);
}

TEST(ProtocolDecoder, CertifiedProductionBossHpFrameUsesMarkerAndExactCurrentHp) {
    const std::vector<uint8_t> frame{
        0x14,0x00,0x8d,0xf4,0x92,0x01,0x02,0x01,0x00,0xc9,0x4b,0xae,0x0d,0x00,0x00,0x00,0x00};
    const auto event = decode_real_fixture(8, {0x00,0x8d}, fields_for(8), frame, 347151).view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_BOSS_HP));
    EXPECT_EQ(event.actor_id, 18804u); EXPECT_EQ(event.current_hp, 229526473u);
    EXPECT_EQ(event.boss_id, 0u); EXPECT_EQ(event.max_hp, 0u);
    expect_provenance(event, 347151);
}

TEST(ProtocolDecoder, CertifiedProductionCombatStartFrameCarriesBossActor) {
    const std::vector<uint8_t> frame{0x0d,0x01,0x8d,0xf4,0x92,0x01,0x02,0x00,0x00,0x00};
    const auto event = decode_real_fixture(202, {0x01,0x8d}, fields_for(202), frame, 347151).view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_COMBAT_STATE));
    EXPECT_EQ(event.actor_id, 18804u); EXPECT_EQ(event.state, 1u);
    expect_provenance(event, 347151);
}

TEST(ProtocolDecoder, ProductionMobHeaderCarriesRuntimeActorAndDatabaseMobCode) {
    std::vector<uint8_t> body{0x41,0x36,0xf4,0x92,0x01,0x0c,0x22,0x00,0x19,0x1f,0x23,0x00,0x00,0x02,0xaa};
    auto frame = encode_var(static_cast<uint32_t>(body.size() + 4u));
    frame.insert(frame.end(), body.begin(), body.end());
    const auto event = decode_real_fixture(7, {0x41,0x36}, fields_for(7), frame, 217201).view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_MOB_SPAWN));
    EXPECT_EQ(event.actor_id, 18804u); EXPECT_EQ(event.mob_id, 2301721u);
    EXPECT_EQ(event.boss_id, 2301721u); EXPECT_EQ(event.is_boss, 1u);
    expect_provenance(event, 217201);
}

// Real captured spawn frame for an anonymous summon: no name and no mob code,
// so the owner is only recoverable from the trailer after the 0xff boundary.
std::vector<uint8_t> anonymous_summon_frame() {
    return {
        0xcd,0x01,0x41,0x36,0x85,0x88,0x02,0x5f,0x00,0x00,0x7b,0x91,
        0x2c,0x00,0x40,0x02,0xb3,0xa2,0x05,0xc7,0x68,0xab,0x9e,0x45,
        0x00,0xf0,0xd3,0x45,0x10,0xe0,0xae,0x42,0x2e,0x3e,0x01,0x92,
        0xe3,0x02,0x92,0xe3,0x02,0x6e,0x2b,0x00,0x00,0x6e,0x2b,0x00,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
        0x00,0xf0,0xc6,0x02,0x00,0x64,0x00,0x00,0x00,0xf0,0x49,0x02,
        0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xa0,0x86,0x01,
        0x00,0x00,0x00,0x00,0x00,0x48,0x1d,0x0e,0x00,0x01,0x01,0x01,
        0x11,0x01,0x81,0x96,0x98,0x00,0xff,0xff,0xff,0xff,0xff,0xff,
        0xff,0xff,0x80,0x75,0xd5,0x2a,0xbb,0x03,0x00,0x00,0x85,0x88,
        0x02,0x01,0x02,0xb3,0xa2,0x05,0xc7,0x68,0xab,0x9e,0x45,0x00,
        0xf0,0xd3,0x45,0x07,0x02,0x06,0x7a,0x2c,0x00,0x00,0xcf,0x08,
        0x00,0x00,0x00,0x00,0xec,0x03,0x0a,0x31,0xec,0x9d,0xb8,0xeb,
        0xa0,0x88,0xea,0xb5,0x94,0x01,0x00,0x00,0x00,0x00,0x00,0x00,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x03,
        0xcd,0x00,0x6e,0x21,0x00,0x00,0xd0,0x00,0x07,0x03,0x00,0x00,
        0xd6,0x00,0x32,0x00,0x00,0x00,0x32,0x00,0x00,0x00,0x00,
    };
}

TEST(ProtocolDecoder, ProductionAnonymousSummonSpawnCarriesOwnerForAttribution) {
    // The bounded current-wire parser runs before any declared layout, so a
    // recovered owner can only have come from the spawn trailer.
    const auto event = decode_real_fixture(7, {0x41,0x36}, fields_for(7), anonymous_summon_frame(), 512001, 4096).view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_MOB_SPAWN));
    EXPECT_EQ(event.actor_id, 33797u);
    EXPECT_EQ(event.owner_id, 11386u);
    EXPECT_EQ(event.mob_id, 0u);
    EXPECT_EQ(event.boss_id, 0u);
    EXPECT_EQ(event.is_boss, 0u);
    EXPECT_EQ(event.name_size, 0u);
    expect_provenance(event, 512001);
}

TEST(ProtocolDecoder, SpawnVariantWithoutSummonTrailerNeverInventsAnOwner) {
    auto frame = anonymous_summon_frame();
    // Break the eight-byte boundary marker. Without it there is no evidence of a
    // summoner, so the decoder must not hand back the owner it would otherwise
    // have recovered from the trailer.
    frame[102] = 0x00;
    const auto event = decode_real_fixture(7, {0x41,0x36}, fields_for(7), frame, 512002, 4096).view();
    EXPECT_NE(event.owner_id, 11386u);
}

TEST(ProtocolDecoder, ProductionNamedSummonHeaderCarriesOwnerNameForAttribution) {
    std::vector<uint8_t> body{
        0x41,0x36,0x8b,0xb5,0x01,0x5f,0x00,0x01,0x06,
        0xeb,0x82,0xa8,0xed,0x9e,0x90,0xca,0x90,0x2c,0x00,0x40,0x02,0x00};
    auto frame = encode_var(static_cast<uint32_t>(body.size() + 4u));
    frame.insert(frame.end(), body.begin(), body.end());
    const auto decoded = decode_real_fixture(7, {0x41,0x36}, fields_for(7), frame, 384173);
    const auto event = decoded.view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_MOB_SPAWN));
    EXPECT_EQ(event.actor_id, 23179u); EXPECT_EQ(event.owner_id, 0u);
    EXPECT_EQ(event.mob_id, 0u); EXPECT_EQ(event.is_boss, 0u);
    EXPECT_EQ(event.state, 0x40u);
    EXPECT_EQ(std::string(reinterpret_cast<const char*>(event.name), event.name_size),
              std::string("\xeb\x82\xa8\xed\x9e\x90", 6));
    expect_provenance(event, 384173);
}

TEST(ProtocolDecoder, CertifiedProductionSelfActorFrameCarriesRawIdentity) {
    const std::vector<uint8_t> frame{
        0x1a,0x33,0x36,0x97,0x70,0x00,0x00,0x00,0x00,0x00,0x06,
        0xeb,0x82,0xa8,0xed,0x9e,0x90,0xea,0x03,0x1e,0x00,0x00,0x00};
    const auto decoded = decode_real_fixture(5, {0x33,0x36}, fields_for(5), frame, 100000);
    const auto event = decoded.view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_SELF_ACTOR));
    EXPECT_EQ(event.actor_id, 14359u); EXPECT_EQ(event.server_id, 490u);
    EXPECT_EQ(event.job_id, 30u); EXPECT_EQ(event.is_self, 1u);
    EXPECT_EQ(std::string(reinterpret_cast<const char*>(event.name), event.name_size),
              std::string("\xeb\x82\xa8\xed\x9e\x90", 6));
    expect_provenance(event, 100000);
}

TEST(ProtocolDecoder, CertifiedProductionDotFrameUsesExtraDamageAndCanonicalSkill) {
    const std::vector<uint8_t> frame{
        0x17,0x05,0x38,0xf4,0x92,0x01,0x0a,0xe8,0x6f,0x31,0xfb,
        0x56,0xe3,0x11,0xd9,0x05,0x1c,0xcb,0x2d,0x00};
    const auto event = decode_real_fixture(2, {0x05,0x38}, fields_for(2), frame, 428246).view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_DOT));
    EXPECT_EQ(event.actor_id, 14312u); EXPECT_EQ(event.target_id, 18804u);
    EXPECT_EQ(event.skill_id, 3001116u); EXPECT_EQ(event.damage, 729u);
    EXPECT_EQ(event.multi_damage, 0u); EXPECT_EQ(event.healing, 0u);
    EXPECT_EQ(event.is_dot, 1u); expect_provenance(event, 428246);
}

TEST(ProtocolDecoder, CertifiedProductionBuffFramesUseObservedApplyAndStateLayouts) {
    const std::vector<uint8_t> applied{
        0x34,0x2a,0x38,0xc9,0x3f,0x01,0x13,0x99,0x01,0x71,0x77,0x5c,0x07,0x98,0x3a,
        0x00,0x00,0x00,0x00,0x00,0x00,0x9d,0xc6,0x26,0x4c,0x9f,0x01,0x00,0x00,0xc9,
        0x3f,0x01,0xda,0x77,0xbc,0x00,0x00,0xe1,0x10,0x07,0xc7,0x6a,0x05,0xaf,0x45,
        0x00,0x00,0xd4,0x45};
    const std::vector<uint8_t> state{
        0x33,0x2b,0x38,0xc9,0x3f,0x13,0x99,0x01,0x71,0x77,0x5c,0x07,0x98,0x3a,0x00,
        0x00,0x00,0x00,0x00,0x00,0xd0,0xc6,0x26,0x4c,0x9f,0x01,0x00,0x00,0xc9,0x3f,
        0x01,0xc2,0x72,0xbc,0x00,0x02,0xe1,0x10,0x07,0xc7,0x6a,0x05,0xaf,0x45,0x00,
        0x00,0xd4,0x45};
    const auto apply_event = decode_real_fixture(3, {0x2a,0x38}, fields_for(3), applied, 347514).view();
    const auto state_event = decode_real_fixture(4, {0x2b,0x38}, fields_for(4), state, 347934).view();
    for (const auto event : {apply_event, state_event}) {
        EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_BUFF));
        EXPECT_EQ(event.owner_id, 8137u); EXPECT_EQ(event.target_id, 8137u);
        EXPECT_EQ(event.buff_id, 123500401u); EXPECT_EQ(event.duration_ms, 15000u);
    }
    EXPECT_EQ(apply_event.buff_operation, static_cast<uint8_t>(NM_BUFF_OPERATION_APPLY));
    EXPECT_EQ(state_event.buff_operation, static_cast<uint8_t>(NM_BUFF_OPERATION_REFRESH));
}

TEST(ProtocolDecoder, CertifiedProductionContentFrameCarriesObservedScope) {
    const std::vector<uint8_t> frame{
        0x1e,0x01,0x40,0x52,0x28,0x09,0x00,0x01,0xff,0x9f,0x27,0x4c,
        0x9f,0x01,0x00,0x00,0x03,0x01,0x02,0xf5,0xca,0x4e,0xfe,0x3e,0xba,0xf4,0x24};
    const auto event = decode_real_fixture(103, {0x01,0x40}, fields_for(103), frame, 2186284).view();
    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_CONTENT));
    EXPECT_EQ(event.content_id, 600146u); EXPECT_EQ(event.dungeon_id, 600146u);
    EXPECT_EQ(event.state, 3u); expect_provenance(event, 2186284);
}

TEST(ProtocolDecoder, SequentialDescriptorsMoveFollowingFieldsWithVarintWidth) {
    const std::vector<Field> fields{
        {1, sequential_var_uint, 0, 1, 5}, {2, sequential_var_uint, 0, 1, 5},
        {4, sequential_fixed, 0, 4, 1}, {13, sequential_var_uint, 0, 1, 5},
        {14, sequential_var_uint, 0, 1, 5}, {15, sequential_var_uint, 0, 1, 5},
        {18, sequential_fixed, 0, 4, 1}, {22, sequential_fixed, 0, 1, 1},
        {23, sequential_fixed, 0, 1, 1},
    };
    const auto make_frame = [](uint32_t actor) {
        std::vector<uint8_t> body{0x66};
        const auto append_var = [&](uint32_t value) {
            auto encoded = encode_var(value);
            body.insert(body.end(), encoded.begin(), encoded.end());
        };
        append_var(actor); append_var(300); append_u32(body, 303);
        append_var(40'004); append_var(50'005); append_var(60'006);
        append_u32(body, 0x12345678); body.push_back(7); body.push_back(0);
        auto frame = encode_var(static_cast<uint32_t>(body.size() + 4u));
        frame.insert(frame.end(), body.begin(), body.end());
        return frame;
    };
    ProtocolDecoder decoder(snapshot({{1, {0x66}, 1}}, fields));
    auto one_byte = message(0x66); one_byte.bytes = make_frame(127);
    auto two_bytes = message(0x66); two_bytes.bytes = make_frame(128);

    const auto first = only_event(decoder.decode(one_byte)).view();
    const auto second = only_event(decoder.decode(two_bytes)).view();

    EXPECT_EQ(first.actor_id, 127u); EXPECT_EQ(second.actor_id, 128u);
    for (const auto event : {first, second}) {
        EXPECT_EQ(event.target_id, 300u); EXPECT_EQ(event.skill_id, 303u);
        EXPECT_EQ(event.damage, 40'004u); EXPECT_EQ(event.multi_damage, 50'005u);
        EXPECT_EQ(event.healing, 60'006u); EXPECT_EQ(event.special_mask, 0x12345678u);
        EXPECT_EQ(event.damage_type, 7u); EXPECT_EQ(event.is_dot, 0u);
    }
}

TEST(ProtocolDecoder, RegisteredUnknownKindWithoutLayoutProducesBoundedUnknownEvent) {
    ProtocolDecoder decoder(snapshot_with_layouts({{999, {0x55}, 0}}, {}));
    auto value = message(0x55);
    const auto event = only_event(decoder.decode(value)).view();

    EXPECT_EQ(event.kind, static_cast<uint32_t>(NM_EVENT_UNKNOWN_PROTOCOL));
    EXPECT_EQ(event.payload_size, value.bytes.size());
    EXPECT_EQ(event.first_file_offset, 400u);
}

TEST(ProtocolDecoder, DeduplicatesOnlyStableMessageIdentity) {
    ProtocolDecoder decoder(snapshot({{1, {0x11}, 1}}));
    auto first = message(0x11, 500);
    first.stream_message_id = 41;
    auto equal_but_distinct = first;
    equal_but_distinct.stream_message_id = 42;

    EXPECT_EQ(decoder.decode(first).size(), 1u);
    EXPECT_TRUE(decoder.decode(first).empty());
    EXPECT_EQ(decoder.decode(equal_but_distinct).size(), 1u);
}

TEST(ProtocolDecoder, NonzeroIdentityIgnoresBytesAndZeroIdentityNeverDeduplicates) {
    ProtocolDecoder decoder(snapshot({{1, {0x11}, 1}}));
    auto first = message(0x11, 500);
    first.stream_message_id = 41;
    auto mutated_same_identity = first;
    mutated_same_identity.bytes.assign({0xff, 0xff, 0xff});

    EXPECT_EQ(decoder.decode(first).size(), 1u);
    EXPECT_TRUE(decoder.decode(mutated_same_identity).empty());

    auto zero = message(0x11, 500);
    zero.stream_message_id = 0;
    EXPECT_EQ(decoder.decode(zero).size(), 1u);
    EXPECT_EQ(decoder.decode(zero).size(), 1u);
}

TEST(ProtocolDecoder, IdentityIncludesFlowAndEpochButNoSemanticFields) {
    ProtocolDecoder decoder(snapshot({{1, {0x11}, 1}}));
    auto first = message(0x11, 500);
    first.stream_message_id = 9;
    auto next_epoch = first;
    next_epoch.epoch++;
    auto next_flow = first;
    next_flow.flow.destination_port++;

    EXPECT_EQ(decoder.decode(first).size(), 1u);
    EXPECT_EQ(decoder.decode(next_epoch).size(), 1u);
    EXPECT_EQ(decoder.decode(next_flow).size(), 1u);
    EXPECT_TRUE(decoder.decode(first).empty());
}

TEST(ProtocolDecoder, DeduplicationCacheEvictsExactlyAt65536Entries) {
    ProtocolDecoder decoder(snapshot({{1, {0x11}, 1}}));
    auto value = message(0x11, 500);
    for (uint64_t identity = 1; identity <= 65'536u; ++identity) {
        value.stream_message_id = identity;
        ASSERT_EQ(decoder.decode(value).size(), 1u) << identity;
    }
    value.stream_message_id = 1;
    EXPECT_TRUE(decoder.decode(value).empty());
    value.stream_message_id = 65'537;
    EXPECT_EQ(decoder.decode(value).size(), 1u);
    value.stream_message_id = 1;
    EXPECT_EQ(decoder.decode(value).size(), 1u);
}

TEST(ProtocolDecoder, ResetAllowsTheSameStableIdentityToBeReused) {
    ProtocolDecoder decoder(snapshot({{1, {0x11}, 1}}));
    auto value = message(0x11);
    value.stream_message_id = 77;
    ASSERT_EQ(decoder.decode(value).size(), 1u);
    ASSERT_TRUE(decoder.decode(value).empty());

    decoder.reset();

    EXPECT_EQ(decoder.decode(value).size(), 1u);
}

}  // namespace
