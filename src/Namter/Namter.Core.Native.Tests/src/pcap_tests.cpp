#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <map>
#include <memory>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

#include <gtest/gtest.h>

#include "capture_record.hpp"
#include "pcapng_writer.hpp"

namespace {

using namter::CaptureError;
using namter::CaptureRecord;
using namter::FlowTuple;
using namter::PacketNormalizer;
using namter::PcapByteOrder;
using namter::PcapReader;
using namter::TimestampPrecision;

void append_u16(std::string& bytes, uint16_t value, PcapByteOrder order) {
    const char low = static_cast<char>(value & 0xffu);
    const char high = static_cast<char>((value >> 8u) & 0xffu);
    if (order == PcapByteOrder::little_endian) {
        bytes.push_back(low);
        bytes.push_back(high);
    } else {
        bytes.push_back(high);
        bytes.push_back(low);
    }
}

void append_u32(std::string& bytes, uint32_t value, PcapByteOrder order) {
    for (int index = 0; index < 4; ++index) {
        const int shift = order == PcapByteOrder::little_endian ? index * 8 : (3 - index) * 8;
        bytes.push_back(static_cast<char>((value >> shift) & 0xffu));
    }
}

std::string pcap_header(
    PcapByteOrder order,
    TimestampPrecision precision,
    uint32_t snaplen = 65'535,
    uint32_t link_type = namter::dlt_raw) {
    std::string bytes;
    if (order == PcapByteOrder::little_endian) {
        bytes.append(
            precision == TimestampPrecision::microseconds ? "\xd4\xc3\xb2\xa1" : "\x4d\x3c\xb2\xa1",
            4);
    } else {
        bytes.append(
            precision == TimestampPrecision::microseconds ? "\xa1\xb2\xc3\xd4" : "\xa1\xb2\x3c\x4d",
            4);
    }
    append_u16(bytes, 2, order);
    append_u16(bytes, 4, order);
    append_u32(bytes, 0, order);
    append_u32(bytes, 0, order);
    append_u32(bytes, snaplen, order);
    append_u32(bytes, link_type, order);
    return bytes;
}

void append_record(
    std::string& bytes,
    PcapByteOrder order,
    uint32_t seconds,
    uint32_t fraction,
    const std::vector<uint8_t>& payload,
    uint32_t original_length = 0) {
    append_u32(bytes, seconds, order);
    append_u32(bytes, fraction, order);
    append_u32(bytes, static_cast<uint32_t>(payload.size()), order);
    append_u32(
        bytes,
        original_length == 0 ? static_cast<uint32_t>(payload.size()) : original_length,
        order);
    for (const uint8_t byte : payload) {
        bytes.push_back(static_cast<char>(byte));
    }
}

std::istringstream stream_for(std::string bytes) {
    return std::istringstream(std::move(bytes), std::ios::in | std::ios::binary);
}

std::filesystem::path fixture_path() {
    char* value = nullptr;
    size_t size = 0;
    if (_dupenv_s(&value, &size, "NAMTER_FIXTURE_ROOT") != 0 || value == nullptr) {
        return {};
    }
    const std::unique_ptr<char, decltype(&std::free)> root(value, &std::free);
    return std::filesystem::path(root.get()) / "aion2_part001.pcap";
}

}  // namespace

TEST(PcapReader, ReadsSuppliedAionFixtureAndNormalizesEveryPacket) {
    const auto path = fixture_path();
    if (path.empty() || !std::filesystem::is_regular_file(path)) {
        GTEST_SKIP() << "NAMTER_FIXTURE_ROOT does not contain aion2_part001.pcap";
    }

    std::ifstream input(path, std::ios::binary);
    ASSERT_TRUE(input.is_open());
    PcapReader reader(input);
    ASSERT_EQ(reader.error(), CaptureError::none);
    ASSERT_TRUE(reader.header().has_value());
    EXPECT_EQ(reader.header()->byte_order, PcapByteOrder::little_endian);
    EXPECT_EQ(reader.header()->precision, TimestampPrecision::microseconds);
    EXPECT_EQ(reader.header()->version_major, 2);
    EXPECT_EQ(reader.header()->version_minor, 4);
    EXPECT_EQ(reader.header()->snaplen, 65'535u);
    EXPECT_EQ(reader.header()->link_type, namter::dlt_raw);

    size_t record_count = 0;
    size_t truncated_count = 0;
    size_t tcp_count = 0;
    size_t server_source_count = 0;
    uint64_t first_timestamp = 0;
    uint64_t last_timestamp = 0;
    uint64_t previous_timestamp = 0;
    std::map<FlowTuple, size_t> flows;
    CaptureRecord record;
    while (reader.read_next(record)) {
        if (record_count == 0) {
            first_timestamp = record.timestamp_ns;
        } else {
            EXPECT_GE(record.timestamp_ns, previous_timestamp);
        }
        previous_timestamp = record.timestamp_ns;
        last_timestamp = record.timestamp_ns;
        truncated_count += record.captured_length != record.original_length ? 1u : 0u;

        const auto normalized = PacketNormalizer::normalize(record);
        ASSERT_EQ(normalized.error, CaptureError::none) << "record " << record_count;
        ASSERT_TRUE(normalized.segment.has_value());
        ++tcp_count;
        ++flows[normalized.segment->flow];
        server_source_count += normalized.segment->flow.source_port == 13'328 ? 1u : 0u;
        ++record_count;
    }

    EXPECT_EQ(reader.error(), CaptureError::none);
    EXPECT_TRUE(reader.eof());
    EXPECT_EQ(record_count, 20'849u);
    EXPECT_EQ(tcp_count, 20'849u);
    EXPECT_EQ(flows.size(), 2u);
    EXPECT_EQ(
        flows.at({
            .source_address = 0xce7f9c8eu,
            .destination_address = 0xc0a80008u,
            .source_port = 13'328,
            .destination_port = 8'735,
        }),
        994u);
    EXPECT_EQ(
        flows.at({
            .source_address = 0xce7f9c25u,
            .destination_address = 0xc0a80008u,
            .source_port = 13'328,
            .destination_port = 12'415,
        }),
        19'855u);
    EXPECT_EQ(server_source_count, 20'849u);
    EXPECT_EQ(truncated_count, 0u);
    EXPECT_EQ(first_timestamp, 1'783'688'952'639'306'000ull);
    EXPECT_EQ(last_timestamp, 1'783'689'455'959'005'000ull);
    EXPECT_EQ(last_timestamp - first_timestamp, 503'319'699'000ull);
}

TEST(PcapReader, ReadsBigEndianClassicPcapAndRetainsRecordOffset) {
    std::string bytes = pcap_header(PcapByteOrder::big_endian, TimestampPrecision::microseconds);
    append_record(bytes, PcapByteOrder::big_endian, 7, 123'456, {1, 2, 3, 4});
    auto input = stream_for(std::move(bytes));

    PcapReader reader(input);
    ASSERT_EQ(reader.error(), CaptureError::none);
    ASSERT_TRUE(reader.header().has_value());
    EXPECT_EQ(reader.header()->byte_order, PcapByteOrder::big_endian);
    CaptureRecord record;
    ASSERT_TRUE(reader.read_next(record));
    EXPECT_EQ(record.timestamp_ns, 7'123'456'000ull);
    EXPECT_EQ(record.file_offset, 24u);
    EXPECT_EQ(record.bytes, (std::vector<uint8_t>{1, 2, 3, 4}));
}

TEST(PcapReader, ConvertsNanosecondPcapTimestampsWithoutPrecisionLoss) {
    std::string bytes = pcap_header(PcapByteOrder::little_endian, TimestampPrecision::nanoseconds);
    append_record(bytes, PcapByteOrder::little_endian, 9, 123'456'789, {0xaa});
    auto input = stream_for(std::move(bytes));

    PcapReader reader(input);
    ASSERT_TRUE(reader.header().has_value());
    EXPECT_EQ(reader.header()->precision, TimestampPrecision::nanoseconds);
    CaptureRecord record;
    ASSERT_TRUE(reader.read_next(record));
    EXPECT_EQ(record.timestamp_ns, 9'123'456'789ull);
}

TEST(PcapReader, RejectsTruncatedRecordHeader) {
    std::string bytes = pcap_header(PcapByteOrder::little_endian, TimestampPrecision::microseconds);
    bytes.append(15, '\0');
    auto input = stream_for(std::move(bytes));

    PcapReader reader(input);
    CaptureRecord record;
    EXPECT_FALSE(reader.read_next(record));
    EXPECT_EQ(reader.error(), CaptureError::truncated_record_header);
}

TEST(PcapReader, RejectsCapturedLengthAboveSnaplenBeforeReadingPayload) {
    std::string bytes = pcap_header(
        PcapByteOrder::little_endian,
        TimestampPrecision::microseconds,
        64);
    append_u32(bytes, 1, PcapByteOrder::little_endian);
    append_u32(bytes, 0, PcapByteOrder::little_endian);
    append_u32(bytes, 65, PcapByteOrder::little_endian);
    append_u32(bytes, 65, PcapByteOrder::little_endian);
    auto input = stream_for(std::move(bytes));

    PcapReader reader(input, 1'024);
    CaptureRecord record;
    EXPECT_FALSE(reader.read_next(record));
    EXPECT_EQ(reader.error(), CaptureError::captured_length_exceeds_snaplen);
}

TEST(PcapReader, RejectsCapturedLengthAboveConfiguredMaximumBeforeReadingPayload) {
    std::string bytes = pcap_header(
        PcapByteOrder::little_endian,
        TimestampPrecision::microseconds,
        1'024);
    append_u32(bytes, 1, PcapByteOrder::little_endian);
    append_u32(bytes, 0, PcapByteOrder::little_endian);
    append_u32(bytes, 65, PcapByteOrder::little_endian);
    append_u32(bytes, 65, PcapByteOrder::little_endian);
    auto input = stream_for(std::move(bytes));

    PcapReader reader(input, 64);
    CaptureRecord record;
    EXPECT_FALSE(reader.read_next(record));
    EXPECT_EQ(reader.error(), CaptureError::captured_length_exceeds_limit);
}

TEST(PcapReader, RejectsOutOfRangeTimestampFraction) {
    std::string bytes = pcap_header(PcapByteOrder::little_endian, TimestampPrecision::microseconds);
    append_record(bytes, PcapByteOrder::little_endian, 1, 1'000'000, {0xaa});
    auto input = stream_for(std::move(bytes));

    PcapReader reader(input);
    CaptureRecord record;
    EXPECT_FALSE(reader.read_next(record));
    EXPECT_EQ(reader.error(), CaptureError::timestamp_fraction_out_of_range);
}

TEST(PcapReader, RejectsTruncatedRecordData) {
    std::string bytes = pcap_header(PcapByteOrder::little_endian, TimestampPrecision::microseconds);
    append_u32(bytes, 1, PcapByteOrder::little_endian);
    append_u32(bytes, 0, PcapByteOrder::little_endian);
    append_u32(bytes, 4, PcapByteOrder::little_endian);
    append_u32(bytes, 4, PcapByteOrder::little_endian);
    bytes.append("\x01\x02\x03", 3);
    auto input = stream_for(std::move(bytes));

    PcapReader reader(input);
    CaptureRecord record;
    EXPECT_FALSE(reader.read_next(record));
    EXPECT_EQ(reader.error(), CaptureError::truncated_record_data);
}

TEST(PcapReader, RejectsOriginalLengthSmallerThanCapturedBeforeReadingData) {
    std::string bytes = pcap_header(PcapByteOrder::little_endian, TimestampPrecision::microseconds);
    append_u32(bytes, 1, PcapByteOrder::little_endian);
    append_u32(bytes, 0, PcapByteOrder::little_endian);
    append_u32(bytes, 4, PcapByteOrder::little_endian);
    append_u32(bytes, 3, PcapByteOrder::little_endian);
    auto input = stream_for(std::move(bytes));

    PcapReader reader(input);
    CaptureRecord record;
    EXPECT_FALSE(reader.read_next(record));
    EXPECT_EQ(reader.error(), CaptureError::original_length_smaller_than_captured);
}

namespace {

std::vector<uint8_t> read_all(const std::filesystem::path& path) {
    std::ifstream stream(path, std::ios::binary);
    return {std::istreambuf_iterator<char>(stream), std::istreambuf_iterator<char>()};
}

uint32_t le32_at(const std::vector<uint8_t>& bytes, size_t offset) {
    return static_cast<uint32_t>(bytes[offset]) | (static_cast<uint32_t>(bytes[offset + 1]) << 8u) |
           (static_cast<uint32_t>(bytes[offset + 2]) << 16u) | (static_cast<uint32_t>(bytes[offset + 3]) << 24u);
}

} // namespace

TEST(PcapngWriter, WritesReadableSectionInterfaceAndPacketBlocksPerLinkType) {
    const auto path = std::filesystem::temp_directory_path() / "namter-pcapng-writer.pcapng";
    std::filesystem::remove(path);
    const std::vector<uint8_t> first{0x45, 0x00, 0x00, 0x1c, 0xde, 0xad};
    const std::vector<uint8_t> second{0x01, 0x02, 0x03};
    {
        namter::PcapngWriter writer;
        ASSERT_TRUE(writer.open(path.string(), 1u << 20u));
        EXPECT_TRUE(writer.write(101u, 1'234'567'890'123u, 60u, first));
        EXPECT_TRUE(writer.write(101u, 1'234'567'890'456u, 3u, second));
        EXPECT_TRUE(writer.write(1u, 1'234'567'890'789u, 3u, second));
        EXPECT_FALSE(writer.truncated());
    }

    const auto bytes = read_all(path);
    ASSERT_GE(bytes.size(), 28u);
    EXPECT_EQ(le32_at(bytes, 0), 0x0A0D0D0Au);           // section header block
    EXPECT_EQ(le32_at(bytes, 8), 0x1A2B3C4Du);           // little-endian byte-order magic

    // Walk the block chain: every block must declare the same length twice.
    size_t offset = 0, interfaces = 0, packets = 0;
    std::vector<uint16_t> link_types;
    while (offset + 12u <= bytes.size()) {
        const uint32_t type = le32_at(bytes, offset);
        const uint32_t length = le32_at(bytes, offset + 4u);
        ASSERT_GE(length, 12u);
        ASSERT_LE(offset + length, bytes.size());
        EXPECT_EQ(length, le32_at(bytes, offset + length - 4u));
        if (type == 0x00000001u) {
            ++interfaces;
            link_types.push_back(static_cast<uint16_t>(bytes[offset + 8u] | (bytes[offset + 9u] << 8u)));
        } else if (type == 0x00000006u) {
            ++packets;
            const uint64_t timestamp = (static_cast<uint64_t>(le32_at(bytes, offset + 12u)) << 32u) |
                                       le32_at(bytes, offset + 16u);
            EXPECT_GE(timestamp, 1'234'567'890'123u);
            if (packets == 1u) {
                EXPECT_EQ(le32_at(bytes, offset + 8u), 0u);              // first interface
                EXPECT_EQ(le32_at(bytes, offset + 20u), first.size());   // captured length
                EXPECT_EQ(le32_at(bytes, offset + 24u), 60u);            // original length preserved
            }
            if (packets == 3u) EXPECT_EQ(le32_at(bytes, offset + 8u), 1u); // second link type
        }
        offset += length;
    }
    EXPECT_EQ(offset, bytes.size());
    EXPECT_EQ(interfaces, 2u);
    EXPECT_EQ(packets, 3u);
    ASSERT_EQ(link_types.size(), 2u);
    EXPECT_EQ(link_types[0], 101u);
    EXPECT_EQ(link_types[1], 1u);
    std::filesystem::remove(path);
}

TEST(PcapngWriter, StopsAtTheConfiguredBudgetInsteadOfGrowingWithoutLimit) {
    const auto path = std::filesystem::temp_directory_path() / "namter-pcapng-budget.pcapng";
    std::filesystem::remove(path);
    namter::PcapngWriter writer;
    ASSERT_TRUE(writer.open(path.string(), 64u)); // room for the section header only
    const std::vector<uint8_t> packet(256u, 0x5Au);
    EXPECT_FALSE(writer.write(101u, 1u, 256u, packet));
    EXPECT_TRUE(writer.truncated());
    EXPECT_LE(writer.written_bytes(), 64u);
    writer.close();
    std::filesystem::remove(path);
}
