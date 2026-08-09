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
