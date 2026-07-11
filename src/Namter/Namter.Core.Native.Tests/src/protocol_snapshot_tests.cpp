#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>

#include <gtest/gtest.h>

#include "namter/core.h"

namespace {

void NM_CALL ignore_event(void*, const nm_event_v1*) {}
void NM_CALL ignore_diagnostic(void*, const nm_diagnostic_v1*) {}

void append_u16(std::vector<uint8_t>& bytes, uint16_t value) {
    bytes.push_back(static_cast<uint8_t>(value));
    bytes.push_back(static_cast<uint8_t>(value >> 8));
}

void append_u32(std::vector<uint8_t>& bytes, uint32_t value) {
    for (unsigned shift = 0; shift < 32; shift += 8) {
        bytes.push_back(static_cast<uint8_t>(value >> shift));
    }
}

void append_u64(std::vector<uint8_t>& bytes, uint64_t value) {
    for (unsigned shift = 0; shift < 64; shift += 8) {
        bytes.push_back(static_cast<uint8_t>(value >> shift));
    }
}

void write_u16(std::vector<uint8_t>& bytes, size_t offset, uint16_t value) {
    bytes[offset] = static_cast<uint8_t>(value);
    bytes[offset + 1] = static_cast<uint8_t>(value >> 8);
}

void write_u32(std::vector<uint8_t>& bytes, size_t offset, uint32_t value) {
    for (unsigned shift = 0; shift < 32; shift += 8) {
        bytes[offset++] = static_cast<uint8_t>(value >> shift);
    }
}

void write_u64(std::vector<uint8_t>& bytes, size_t offset, uint64_t value) {
    for (unsigned shift = 0; shift < 64; shift += 8) {
        bytes[offset++] = static_cast<uint8_t>(value >> shift);
    }
}

uint32_t crc32(const std::vector<uint8_t>& bytes) {
    uint32_t crc = 0xFFFFFFFFu;
    for (size_t index = 0; index < bytes.size(); ++index) {
        const uint8_t byte = index >= 12 && index < 16 ? 0 : bytes[index];
        crc ^= byte;
        for (int bit = 0; bit < 8; ++bit) {
            const uint32_t mask = 0u - (crc & 1u);
            crc = (crc >> 1u) ^ (0xEDB88320u & mask);
        }
    }
    return ~crc;
}

std::vector<uint8_t> valid_snapshot() {
    std::vector<uint8_t> bytes{'N', 'M', 'P', 'S'};
    append_u16(bytes, 1);
    append_u16(bytes, 28);
    append_u32(bytes, 0);
    append_u32(bytes, 0);
    append_u64(bytes, 1);
    append_u32(bytes, 1);
    append_u16(bytes, 3);
    bytes.insert(bytes.end(), {0x06, 0x00, 0x36});
    append_u16(bytes, 1);
    append_u16(bytes, 13328);
    append_u32(bytes, 1);
    append_u16(bytes, 1);
    append_u16(bytes, 2);
    bytes.insert(bytes.end(), {0x04, 0x38});
    append_u32(bytes, 1);
    append_u32(bytes, 1);
    append_u32(bytes, 1);
    append_u32(bytes, 64);
    append_u16(bytes, 9);
    append_u16(bytes, 0);
    const auto append_field = [&](uint16_t kind, uint32_t offset, uint32_t size) {
        append_u16(bytes, kind); append_u16(bytes, 0); append_u32(bytes, offset);
        append_u32(bytes, size); append_u32(bytes, 1);
    };
    append_field(1, 0, 4);
    append_field(2, 4, 4);
    append_field(4, 8, 4);
    append_field(13, 12, 8);
    append_field(14, 20, 8);
    append_field(15, 28, 8);
    append_field(18, 36, 4);
    append_field(22, 40, 1);
    append_field(23, 41, 1);
    write_u32(bytes, 8, static_cast<uint32_t>(bytes.size()));
    write_u32(bytes, 12, crc32(bytes));
    return bytes;
}

std::vector<uint8_t> valid_snapshot_with_two_opcodes() {
    auto bytes = valid_snapshot();
    const std::array<uint8_t, 10> second_opcode{
        2, 0,
        2, 0,
        0x05, 0x38,
        1, 0, 0, 0,
    };
    bytes.insert(bytes.begin() + 51, second_opcode.begin(), second_opcode.end());
    write_u32(bytes, 37, 2);
    write_u32(bytes, 8, static_cast<uint32_t>(bytes.size()));
    write_u32(bytes, 12, crc32(bytes));
    return bytes;
}

nm_core_handle* create_core() {
    const nm_core_config_v1 config{
        .abi_version = nm_core_abi_version(),
        .struct_size = sizeof(nm_core_config_v1),
        .native_queue_capacity = 1024,
        .max_live_flows = 512,
        .max_ooo_bytes_per_flow = 1024 * 1024,
        .max_frame_bytes = 1024 * 1024,
        .max_decompressed_bytes = 4 * 1024 * 1024,
    };
    const nm_callbacks_v1 callbacks{
        .abi_version = nm_core_abi_version(),
        .struct_size = sizeof(nm_callbacks_v1),
        .user = nullptr,
        .event_callback = &ignore_event,
        .diagnostic_callback = &ignore_diagnostic,
    };
    nm_core_handle* handle = nullptr;
    EXPECT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_OK);
    return handle;
}

void expect_rejected(nm_core_handle* handle, std::vector<uint8_t> snapshot) {
    EXPECT_EQ(
        nm_core_set_protocol_snapshot(handle, snapshot.data(), snapshot.size()),
        NM_STATUS_INVALID_ARGUMENT);
}

}  // namespace

TEST(ProtocolSnapshot, AcceptsCanonicalVersionOneSnapshot) {
    nm_core_handle* handle = create_core();
    ASSERT_NE(handle, nullptr);
    const auto snapshot = valid_snapshot();

    EXPECT_EQ(nm_core_set_protocol_snapshot(handle, snapshot.data(), snapshot.size()), NM_STATUS_OK);

    nm_core_destroy(handle);
}

TEST(ProtocolSnapshot, RejectsCorruptHeaderFieldsAndCrcBeforeReplacement) {
    nm_core_handle* handle = create_core();
    ASSERT_NE(handle, nullptr);

    auto corrupt = valid_snapshot();
    corrupt[0] = 'X';
    expect_rejected(handle, corrupt);
    corrupt = valid_snapshot();
    write_u16(corrupt, 4, 2);
    expect_rejected(handle, corrupt);
    corrupt = valid_snapshot();
    write_u16(corrupt, 6, 27);
    expect_rejected(handle, corrupt);
    corrupt = valid_snapshot();
    write_u32(corrupt, 8, static_cast<uint32_t>(corrupt.size() - 1));
    expect_rejected(handle, corrupt);
    corrupt = valid_snapshot();
    corrupt[12] ^= 0x80;
    expect_rejected(handle, corrupt);

    nm_core_destroy(handle);
}

TEST(ProtocolSnapshot, RejectsDeclaredCountsAndFieldBoundsWithoutTrustingThem) {
    nm_core_handle* handle = create_core();
    ASSERT_NE(handle, nullptr);

    auto corrupt = valid_snapshot();
    write_u32(corrupt, 37, 0xFFFFFFFFu);
    write_u32(corrupt, 12, crc32(corrupt));
    expect_rejected(handle, corrupt);

    corrupt = valid_snapshot();
    write_u32(corrupt, 51, 0xFFFFFFFFu);
    write_u32(corrupt, 12, crc32(corrupt));
    expect_rejected(handle, corrupt);

    corrupt = valid_snapshot();
    write_u32(corrupt, 71, 63);
    write_u32(corrupt, 12, crc32(corrupt));
    expect_rejected(handle, corrupt);

    nm_core_destroy(handle);
}

TEST(ProtocolSnapshot, RejectsZeroDataAndProfileVersionsAndUndeclaredLayoutReferences) {
    nm_core_handle* handle = create_core();
    ASSERT_NE(handle, nullptr);

    auto corrupt = valid_snapshot();
    write_u64(corrupt, 16, 0);
    write_u32(corrupt, 12, crc32(corrupt));
    expect_rejected(handle, corrupt);

    corrupt = valid_snapshot();
    write_u32(corrupt, 24, 0);
    write_u32(corrupt, 12, crc32(corrupt));
    expect_rejected(handle, corrupt);

    corrupt = valid_snapshot();
    write_u32(corrupt, 47, 2);
    write_u32(corrupt, 12, crc32(corrupt));
    expect_rejected(handle, corrupt);

    nm_core_destroy(handle);
}

TEST(ProtocolSnapshot, RejectsPortAndFieldCountsThatRunPastAvailableBytes) {
    nm_core_handle* handle = create_core();
    ASSERT_NE(handle, nullptr);

    auto corrupt = valid_snapshot();
    write_u16(corrupt, 33, 2);
    write_u32(corrupt, 12, crc32(corrupt));
    expect_rejected(handle, corrupt);

    corrupt = valid_snapshot();
    write_u16(corrupt, 63, 2);
    write_u32(corrupt, 12, crc32(corrupt));
    expect_rejected(handle, corrupt);

    nm_core_destroy(handle);
}

TEST(ProtocolSnapshot, RejectsDuplicateWireKindsWithoutAllocatingASet) {
    nm_core_handle* handle = create_core();
    ASSERT_NE(handle, nullptr);
    auto corrupt = valid_snapshot_with_two_opcodes();
    write_u16(corrupt, 51, 1);
    write_u32(corrupt, 12, crc32(corrupt));

    expect_rejected(handle, corrupt);

    nm_core_destroy(handle);
}

TEST(ProtocolSnapshot, OwnsTheAcceptedBytesAndKeepsRunningAfterCallerReleasesThem) {
    nm_core_handle* handle = create_core();
    ASSERT_NE(handle, nullptr);
    {
        auto snapshot = valid_snapshot();
        ASSERT_EQ(nm_core_set_protocol_snapshot(handle, snapshot.data(), snapshot.size()), NM_STATUS_OK);
        std::fill(snapshot.begin(), snapshot.end(), uint8_t{0});
    }
    const nm_source_config_v1 source{
        .abi_version = nm_core_abi_version(),
        .struct_size = sizeof(nm_source_config_v1),
        .kind = NM_SOURCE_PCAP,
        .source_data = nullptr,
        .source_data_size = 0,
    };

    EXPECT_EQ(nm_core_start(handle, &source), NM_STATUS_OK);

    nm_core_destroy(handle);
}
