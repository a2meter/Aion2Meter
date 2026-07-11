#include <algorithm>
#include <cstdint>
#include <limits>
#include <span>
#include <vector>

#include <gtest/gtest.h>

#include "event.hpp"
#include "protocol_snapshot.hpp"

namespace {

struct Field {
    uint16_t kind;
    uint16_t flags;
    uint32_t offset;
    uint32_t size;
    uint32_t max_count;
};

struct Layout {
    uint32_t id;
    std::vector<Field> fields;
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

void write_u32(std::vector<uint8_t>& bytes, size_t offset, uint32_t value) {
    for (int shift = 0; shift < 32; shift += 8) bytes[offset++] = static_cast<uint8_t>(value >> shift);
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

std::vector<Field> damage_fields() {
    using enum namter::ProtocolFieldKind;
    return {
        {static_cast<uint16_t>(actor_id), 0, 0, 4, 1},
        {static_cast<uint16_t>(target_id), 0, 4, 4, 1},
        {static_cast<uint16_t>(skill_id), 0, 8, 4, 1},
        {static_cast<uint16_t>(damage), 0, 12, 8, 1},
        {static_cast<uint16_t>(multi_damage), 0, 20, 8, 1},
        {static_cast<uint16_t>(healing), 0, 28, 8, 1},
        {static_cast<uint16_t>(special_mask), 0, 36, 4, 1},
        {static_cast<uint16_t>(damage_type), 0, 40, 1, 1},
        {static_cast<uint16_t>(is_dot), 0, 41, 1, 1},
    };
}

std::vector<uint8_t> make_snapshot(
    std::vector<Layout> layouts,
    std::vector<Opcode> opcodes = {{1, {0x04, 0x38}, 1}}) {
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
    for (const auto& layout : layouts) {
        append_u32(bytes, layout.id); append_u32(bytes, 128);
        append_u16(bytes, static_cast<uint16_t>(layout.fields.size())); append_u16(bytes, 0);
        for (const auto& field : layout.fields) {
            append_u16(bytes, field.kind); append_u16(bytes, field.flags); append_u32(bytes, field.offset);
            append_u32(bytes, field.size); append_u32(bytes, field.max_count);
        }
    }
    write_u32(bytes, 8, static_cast<uint32_t>(bytes.size()));
    write_u32(bytes, 12, crc32(bytes));
    return bytes;
}

std::vector<uint8_t> valid_snapshot() { return make_snapshot({{1, damage_fields()}}); }

TEST(ProtocolSnapshotDescriptors, RejectsDuplicateLayoutIdsAndDuplicateFieldKinds) {
    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(make_snapshot({{1, damage_fields()}, {1, damage_fields()}})));

    auto duplicate = damage_fields();
    duplicate.push_back(duplicate.front());
    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(make_snapshot({{1, duplicate}})));
}

TEST(ProtocolSnapshotDescriptors, RejectsUnknownFieldKindsAndFlags) {
    auto unknown_kind = damage_fields();
    unknown_kind.front().kind = 27;
    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(make_snapshot({{1, unknown_kind}})));

    auto unknown_flag = damage_fields();
    unknown_flag.front().flags = 3;
    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(make_snapshot({{1, unknown_flag}})));
}

TEST(ProtocolSnapshotDescriptors, RejectsIncompatibleSizeCountAndFlagCombinations) {
    auto fixed_size = damage_fields();
    fixed_size.front().size = 3;
    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(make_snapshot({{1, fixed_size}})));

    auto fixed_count = damage_fields();
    fixed_count.front().max_count = 2;
    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(make_snapshot({{1, fixed_count}})));

    auto variable = damage_fields();
    variable.front().flags = 1; variable.front().size = 2; variable.front().max_count = 5;
    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(make_snapshot({{1, variable}})));

    auto invalid_utf8 = damage_fields();
    invalid_utf8.front().flags = 2; invalid_utf8.front().size = 1; invalid_utf8.front().max_count = 20;
    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(make_snapshot({{1, invalid_utf8}})));
}

TEST(ProtocolSnapshotDescriptors, RejectsMissingRequiredAndDisallowedEventFields) {
    auto missing = damage_fields();
    missing.erase(missing.begin());
    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(make_snapshot({{1, missing}})));

    auto disallowed = damage_fields();
    disallowed.push_back({static_cast<uint16_t>(namter::ProtocolFieldKind::name), 2, 42, 1, 20});
    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(make_snapshot({{1, disallowed}})));
}

TEST(ProtocolSnapshotDescriptors, DecoderRejectsAmbiguousSnapshotsInsteadOfSelectingFirstLayout) {
    const auto duplicate = make_snapshot({{1, damage_fields()}, {1, damage_fields()}});
    EXPECT_THROW((void)namter::ProtocolDecoder(duplicate), std::invalid_argument);
}

TEST(ProtocolSnapshotDescriptors, RejectedReplacementPreservesExactOwnedSnapshot) {
    namter::ProtocolSnapshotStore store;
    const auto valid = valid_snapshot();
    ASSERT_TRUE(store.replace(valid));
    const auto before = store.bytes();
    auto invalid = damage_fields();
    invalid.push_back(invalid.front());

    EXPECT_FALSE(store.replace(make_snapshot({{1, invalid}})));
    EXPECT_EQ(store.bytes(), before);
}

TEST(ProtocolSnapshotDescriptors, RejectsDuplicateWireTagsAcrossDifferentKindsAndPreservesOldSnapshot) {
    namter::ProtocolSnapshotStore store;
    const auto valid = valid_snapshot();
    ASSERT_TRUE(store.replace(valid));
    const auto before = store.bytes();
    const auto duplicate_tag = make_snapshot(
        {{1, damage_fields()}},
        {{1, {0x04, 0x38}, 1}, {2, {0x04, 0x38}, 1}});

    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(duplicate_tag));
    EXPECT_FALSE(store.replace(duplicate_tag));
    EXPECT_EQ(store.bytes(), before);
}

TEST(ProtocolSnapshotDescriptors, AllowsUnknownKindOnlyWithoutTypedLayout) {
    EXPECT_TRUE(namter::validate_protocol_snapshot_v1(
        make_snapshot({}, {{999, {0x55}, 0}})));
    EXPECT_FALSE(namter::validate_protocol_snapshot_v1(
        make_snapshot({{1, damage_fields()}}, {{999, {0x55}, 1}})));
}

}  // namespace
