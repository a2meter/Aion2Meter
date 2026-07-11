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
            default: return false;
        }
    };
    std::erase_if(fields, [&](const Field& field) { return !keep(field.kind); });
    return fields;
}

std::vector<uint8_t> snapshot_with_layouts(
    std::vector<Opcode> opcodes,
    const std::vector<std::vector<Field>>& layouts) {
    std::vector<uint8_t> bytes{'N', 'M', 'P', 'S'};
    append_u16(bytes, 1); append_u16(bytes, 28); append_u32(bytes, 0); append_u32(bytes, 0);
    append_u64(bytes, 7); append_u32(bytes, 3);
    append_u16(bytes, 3); bytes.insert(bytes.end(), {0x06, 0x00, 0x36});
    append_u16(bytes, 1); append_u16(bytes, 13328);
    append_u32(bytes, static_cast<uint32_t>(opcodes.size()));
    for (size_t index = 0; index < opcodes.size(); ++index) {
        const auto& opcode = opcodes[index];
        append_u16(bytes, opcode.kind); append_u16(bytes, static_cast<uint16_t>(opcode.tag.size()));
        bytes.insert(bytes.end(), opcode.tag.begin(), opcode.tag.end()); append_u32(bytes, static_cast<uint32_t>(index + 1u));
    }
    append_u32(bytes, static_cast<uint32_t>(layouts.size()));
    for (size_t index = 0; index < layouts.size(); ++index) {
        append_u32(bytes, static_cast<uint32_t>(index + 1u)); append_u32(bytes, 128);
        append_u16(bytes, static_cast<uint16_t>(layouts[index].size())); append_u16(bytes, 0);
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
    for (const auto& opcode : opcodes) layouts.push_back(fields_for(opcode.kind));
    return snapshot_with_layouts(std::move(opcodes), layouts);
}

std::vector<uint8_t> snapshot(std::vector<Opcode> opcodes, std::vector<Field> fields) {
    return snapshot_with_layouts(std::move(opcodes), {std::move(fields)});
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
    uint64_t file_offset) {
    ProtocolDecoder decoder(snapshot({{kind, std::move(tag), 1}}, fields));
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
        ProtocolDecoder decoder(snapshot({{kind, tag, 1}}, fields));
        auto truncated = complete;
        truncated.bytes.resize(size);
        const auto outputs = decoder.decode(truncated);
        ASSERT_EQ(outputs.size(), 1u) << size;
        EXPECT_TRUE(std::holds_alternative<namter::ProtocolDecodeDiagnostic>(outputs.front())) << size;
    }
    for (const uint8_t mutated : {uint8_t{0}, static_cast<uint8_t>(frame.front() - 1u),
                                  static_cast<uint8_t>(frame.front() + 1u), uint8_t{0x80}}) {
        ProtocolDecoder decoder(snapshot({{kind, tag, 1}}, fields));
        auto value = complete;
        value.bytes[0] = mutated;
        const auto outputs = decoder.decode(value);
        ASSERT_EQ(outputs.size(), 1u);
        EXPECT_TRUE(std::holds_alternative<namter::ProtocolDecodeDiagnostic>(outputs.front()));
    }
}

TEST(ProtocolDecoder, DecodesClosedEventKindsWithEveryFieldAndProvenance) {
    const std::vector<Opcode> opcodes{
        {1, {0x11}, 1}, {2, {0x12}, 1}, {3, {0x13}, 1}, {5, {0x15}, 1},
        {6, {0x16}, 1}, {7, {0x17}, 1}, {8, {0x18}, 1}, {10, {0x1a}, 1},
        {101, {0x21}, 1}, {201, {0x31}, 1}, {202, {0x32}, 1},
    };
    ProtocolDecoder decoder(snapshot(opcodes));

    const auto damage = only_event(decoder.decode(message(0x11))).view();
    EXPECT_EQ(damage.kind, static_cast<uint32_t>(NM_EVENT_DAMAGE)); EXPECT_EQ(damage.actor_id, 101u); EXPECT_EQ(damage.target_id, 202u);
    EXPECT_EQ(damage.skill_id, 404u); EXPECT_EQ(damage.damage, 13'004u); EXPECT_EQ(damage.multi_damage, 14'005u);
    EXPECT_EQ(damage.healing, 15'006u); EXPECT_EQ(damage.special_mask, 0x1a2b3c4du); EXPECT_EQ(damage.damage_type, 21u);
    EXPECT_EQ(damage.is_dot, 1u); EXPECT_EQ(damage.epoch, 9u); EXPECT_EQ(damage.first_file_offset, 400u);

    const auto dot = only_event(decoder.decode(message(0x12, 401))).view();
    EXPECT_EQ(dot.kind, static_cast<uint32_t>(NM_EVENT_DOT)); EXPECT_EQ(dot.is_dot, 1u);

    const auto buff = only_event(decoder.decode(message(0x13, 402))).view();
    EXPECT_EQ(buff.kind, static_cast<uint32_t>(NM_EVENT_BUFF)); EXPECT_EQ(buff.owner_id, 303u); EXPECT_EQ(buff.target_id, 202u);
    EXPECT_EQ(buff.buff_id, 505u); EXPECT_EQ(buff.duration_ms, 18'009u); EXPECT_EQ(buff.action, 20u);

    const auto self_owner = only_event(decoder.decode(message(0x15, 403)));
    const auto self = self_owner.view();
    EXPECT_EQ(self.kind, static_cast<uint32_t>(NM_EVENT_SELF_ACTOR)); EXPECT_EQ(self.actor_id, 101u); EXPECT_EQ(self.owner_id, 303u);
    EXPECT_EQ(self.server_id, 1102u); EXPECT_EQ(self.job_id, 1203u); EXPECT_EQ(self.is_self, 1u);
    EXPECT_EQ(std::string(reinterpret_cast<const char*>(self.name), self.name_size), "Namter");
    const auto other = only_event(decoder.decode(message(0x16, 404))).view(); EXPECT_EQ(other.kind, static_cast<uint32_t>(NM_EVENT_OTHER_ACTOR));

    const auto mob = only_event(decoder.decode(message(0x17, 405))).view();
    EXPECT_EQ(mob.kind, static_cast<uint32_t>(NM_EVENT_MOB_SPAWN)); EXPECT_EQ(mob.mob_id, 606u); EXPECT_EQ(mob.boss_id, 707u);
    EXPECT_EQ(mob.current_hp, 16'007u); EXPECT_EQ(mob.max_hp, 17'008u); EXPECT_EQ(mob.is_boss, 1u);

    const auto boss = only_event(decoder.decode(message(0x18, 406))).view();
    EXPECT_EQ(boss.kind, static_cast<uint32_t>(NM_EVENT_BOSS_HP)); EXPECT_EQ(boss.actor_id, 101u); EXPECT_EQ(boss.boss_id, 707u);
    EXPECT_EQ(boss.current_hp, 16'007u); EXPECT_EQ(boss.max_hp, 17'008u);

    const auto removed = only_event(decoder.decode(message(0x1a, 407))).view();
    EXPECT_EQ(removed.kind, static_cast<uint32_t>(NM_EVENT_ENTITY_REMOVED)); EXPECT_EQ(removed.actor_id, 101u);

    const auto party = only_event(decoder.decode(message(0x21, 408))).view();
    EXPECT_EQ(party.kind, static_cast<uint32_t>(NM_EVENT_PARTY)); EXPECT_EQ(party.party_id, 1001u); EXPECT_EQ(party.actor_id, 101u);
    EXPECT_EQ(party.content_id, 808u); EXPECT_EQ(party.dungeon_id, 909u); EXPECT_EQ(party.action, 20u);

    const auto content = only_event(decoder.decode(message(0x31, 409))).view();
    EXPECT_EQ(content.kind, static_cast<uint32_t>(NM_EVENT_CONTENT)); EXPECT_EQ(content.content_id, 808u); EXPECT_EQ(content.dungeon_id, 909u);
    EXPECT_EQ(content.state, 19u);

    const auto combat = only_event(decoder.decode(message(0x32, 410))).view();
    EXPECT_EQ(combat.kind, static_cast<uint32_t>(NM_EVENT_COMBAT_STATE)); EXPECT_EQ(combat.actor_id, 101u); EXPECT_EQ(combat.state, 19u);
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

TEST(ProtocolDecoder, EveryTypedFixtureBoundaryAndLengthMutationIsRejected) {
    // Smallest complete real damage-tag frame extracted from aion2_part001.pcap at PCAP offset 250658.
    const std::vector<uint8_t> real_frame{
        0x1e, 0x04, 0x38, 0xc9, 0x3f, 0x00, 0x00, 0xc9, 0x3f, 0x37, 0xb9, 0x21, 0x00, 0x02,
        0x02, 0x87, 0x59, 0x2c, 0x0d, 0x01, 0x00, 0x00, 0x00, 0x90, 0x4e, 0x01, 0x00,
    };
    const std::vector<Field> fields{
        {static_cast<uint16_t>(namter::ProtocolFieldKind::actor_id), var_uint, 0, 1, 5},
        {static_cast<uint16_t>(namter::ProtocolFieldKind::target_id), var_uint, 4, 1, 5},
        {static_cast<uint16_t>(namter::ProtocolFieldKind::skill_id), fixed, 6, 4, 1},
        {static_cast<uint16_t>(namter::ProtocolFieldKind::damage), fixed, 12, 8, 1},
        {static_cast<uint16_t>(namter::ProtocolFieldKind::multi_damage), fixed, 16, 8, 1},
        {static_cast<uint16_t>(namter::ProtocolFieldKind::healing), fixed, 20, 4, 1},
        {static_cast<uint16_t>(namter::ProtocolFieldKind::special_mask), fixed, 8, 4, 1},
        {static_cast<uint16_t>(namter::ProtocolFieldKind::damage_type), fixed, 10, 1, 1},
        {static_cast<uint16_t>(namter::ProtocolFieldKind::is_dot), fixed, 11, 1, 1},
    };
    ProtocolDecoder decoder(snapshot({{1, {0x04, 0x38}, 1}}, fields));
    auto complete = message(0x11);
    complete.bytes = real_frame;
    EXPECT_TRUE(std::holds_alternative<DecodedEvent>(decoder.decode(complete).front()));

    for (size_t size = 0; size < real_frame.size(); ++size) {
        ProtocolDecoder truncated_decoder(snapshot({{1, {0x04, 0x38}, 1}}, fields));
        auto truncated = complete; truncated.bytes.resize(size);
        const auto outputs = truncated_decoder.decode(truncated);
        ASSERT_EQ(outputs.size(), 1u) << size;
        EXPECT_TRUE(std::holds_alternative<namter::ProtocolDecodeDiagnostic>(outputs.front())) << size;
    }
    for (const uint8_t mutated : {uint8_t{0}, uint8_t{0x1d}, uint8_t{0x1f}, uint8_t{0x80}}) {
        ProtocolDecoder mutated_decoder(snapshot({{1, {0x04, 0x38}, 1}}, fields));
        auto value = complete; value.bytes[0] = mutated;
        EXPECT_TRUE(std::holds_alternative<namter::ProtocolDecodeDiagnostic>(mutated_decoder.decode(value).front()));
    }
}

TEST(ProtocolDecoder, RealPcapFixturesDecodeDamageDotBattleBossHpAndEntityRemovalWithProvenance) {
    // Each exact framed message was extracted from captures/aion2_part001.pcap by the native PCAP/TCP/framer path.
    const std::vector<uint8_t> damage_frame{
        0x1e, 0x04, 0x38, 0xc9, 0x3f, 0x00, 0x00, 0xc9, 0x3f, 0x37, 0xb9, 0x21, 0x00, 0x02,
        0x02, 0x87, 0x59, 0x2c, 0x0d, 0x01, 0x00, 0x00, 0x00, 0x90, 0x4e, 0x01, 0x00,
    };
    const std::vector<Field> damage_fields{
        {1, var_uint, 0, 1, 5}, {2, var_uint, 4, 1, 5}, {4, fixed, 6, 4, 1},
        {13, fixed, 12, 8, 1}, {14, fixed, 16, 8, 1}, {15, fixed, 20, 4, 1},
        {18, fixed, 8, 4, 1}, {22, fixed, 9, 1, 1}, {23, fixed, 16, 1, 1},
    };
    const auto damage = decode_real_fixture(1, {0x04, 0x38}, damage_fields, damage_frame, 250658).view();
    EXPECT_EQ((std::array<uint32_t, 5>{damage.actor_id, damage.target_id, damage.skill_id,
                                      damage.special_mask, damage.damage_type}),
              (std::array<uint32_t, 5>{8137, 8137, 0x0021b937, 0x02020021, 0}));
    EXPECT_EQ(damage.damage, 0x000000010d2c5987ull);
    EXPECT_EQ(damage.multi_damage, 0x00014e9000000001ull);
    EXPECT_EQ(damage.healing, 0x00014e90ull);
    EXPECT_EQ(damage.is_dot, 1u);
    EXPECT_EQ(damage.first_file_offset, 250658u);
    EXPECT_EQ(damage.last_file_offset, 250658u);
    EXPECT_EQ(damage.epoch, 9u);
    EXPECT_EQ(damage.first_timestamp_ns, 300u);
    EXPECT_EQ(damage.last_timestamp_ns, 301u);
    EXPECT_EQ(damage.source_address, 1u);
    EXPECT_EQ(damage.destination_address, 2u);
    EXPECT_EQ(damage.source_port, 13328u);
    EXPECT_EQ(damage.destination_port, 50000u);

    const std::vector<uint8_t> dot_frame{0x11, 0x05, 0x38, 0xe8, 0x6f, 0x00, 0xe8, 0x6f, 0x93, 0x02, 0x2e, 0x81, 0x1f, 0x6c};
    const std::vector<Field> dot_fields{
        {1, var_uint, 0, 1, 5}, {2, var_uint, 3, 1, 5}, {4, fixed, 5, 2, 1},
        {13, fixed, 7, 4, 1}, {14, fixed, 0, 4, 1}, {15, fixed, 3, 4, 1},
        {18, fixed, 7, 4, 1}, {22, fixed, 2, 1, 1}, {23, fixed, 10, 1, 1},
    };
    const auto dot = decode_real_fixture(2, {0x05, 0x38}, dot_fields, dot_frame, 612974).view();
    EXPECT_EQ(dot.kind, static_cast<uint32_t>(NM_EVENT_DOT));
    EXPECT_EQ((std::array<uint32_t, 5>{dot.actor_id, dot.target_id, dot.skill_id, dot.special_mask, dot.damage_type}),
              (std::array<uint32_t, 5>{14312, 14312, 0x0293, 0x6c1f812e, 0}));
    EXPECT_EQ(dot.damage, 0x6c1f812eull);
    EXPECT_EQ(dot.multi_damage, 0xe8006fe8ull);
    EXPECT_EQ(dot.healing, 0x02936fe8ull);
    EXPECT_EQ(dot.is_dot, 1u);
    EXPECT_EQ(dot.first_file_offset, 612974u);

    const std::vector<uint8_t> battle_frame{
        0x2f, 0x2a, 0x38, 0x97, 0x70, 0x01, 0x11, 0x01, 0xeb, 0x2e, 0x00, 0x00,
        0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x80, 0x75, 0xd5, 0x2a,
        0xbb, 0x03, 0x00, 0x00, 0x97, 0x70, 0x01, 0x00, 0x34, 0x86, 0x5e, 0x47,
        0x73, 0xb2, 0x55, 0x47, 0x00, 0x37, 0xc8, 0x47,
    };
    const std::vector<Field> battle_fields{
        {3, var_uint, 0, 1, 5}, {2, var_uint, 3, 1, 5}, {5, fixed, 5, 4, 1},
        {19, fixed, 9, 4, 1}, {21, fixed, 4, 1, 1},
    };
    const auto battle = decode_real_fixture(3, {0x2a, 0x38}, battle_fields, battle_frame, 105474).view();
    EXPECT_EQ(battle.kind, static_cast<uint32_t>(NM_EVENT_BUFF));
    EXPECT_EQ((std::array<uint32_t, 5>{battle.owner_id, battle.target_id, battle.buff_id,
                                      battle.duration_ms, battle.action}),
              (std::array<uint32_t, 5>{14359, 17, 12011, 0xffffffffu, 1}));
    EXPECT_EQ(battle.first_file_offset, 105474u);

    const std::vector<uint8_t> boss_frame{0x0c, 0x01, 0x8d, 0xf4, 0x7e, 0x02, 0x00, 0x00, 0x00};
    const std::vector<Field> boss_fields{
        {1, var_uint, 0, 1, 5}, {7, fixed, 2, 4, 1}, {16, fixed, 0, 4, 1}, {17, fixed, 2, 4, 1},
    };
    const auto boss = decode_real_fixture(8, {0x01, 0x8d}, boss_fields, boss_frame, 986583).view();
    EXPECT_EQ((std::array<uint64_t, 4>{boss.actor_id, boss.boss_id, boss.current_hp, boss.max_hp}),
              (std::array<uint64_t, 4>{16244, 2, 0x00027ef4, 2}));
    EXPECT_EQ(boss.first_file_offset, 986583u);

    const std::vector<uint8_t> removed_frame{0x0a, 0x21, 0x8d, 0xc9, 0x3f, 0x00, 0x01};
    const std::vector<Field> removed_fields{{1, fixed, 0, 4, 1}};
    const auto removed = decode_real_fixture(10, {0x21, 0x8d}, removed_fields, removed_frame, 247913).view();
    EXPECT_EQ(removed.kind, static_cast<uint32_t>(NM_EVENT_ENTITY_REMOVED));
    EXPECT_EQ(removed.actor_id, 0x01003fc9u);
    EXPECT_EQ(removed.first_file_offset, 247913u);

    expect_real_fixture_boundaries(1, {0x04, 0x38}, damage_fields, damage_frame);
    expect_real_fixture_boundaries(2, {0x05, 0x38}, dot_fields, dot_frame);
    expect_real_fixture_boundaries(3, {0x2a, 0x38}, battle_fields, battle_frame);
    expect_real_fixture_boundaries(8, {0x01, 0x8d}, boss_fields, boss_frame);
    expect_real_fixture_boundaries(10, {0x21, 0x8d}, removed_fields, removed_frame);
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
