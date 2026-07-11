#include <algorithm>
#include <concepts>
#include <cstdint>
#include <utility>
#include <vector>

#include <gtest/gtest.h>

#include "capture_record.hpp"

namespace {

using namter::CaptureError;
using namter::CaptureRecord;
using namter::CaptureSource;
using namter::PacketNormalizer;

static_assert(std::same_as<decltype(namter::TcpSegment::payload), std::vector<uint8_t>>);

std::vector<uint8_t> raw_tcp_packet(
    uint8_t flags = namter::tcp_ack,
    const std::vector<uint8_t>& payload = {},
    uint8_t ipv4_words = 5,
    uint8_t tcp_words = 5) {
    const size_t ipv4_size = static_cast<size_t>(ipv4_words) * 4u;
    const size_t tcp_size = static_cast<size_t>(tcp_words) * 4u;
    std::vector<uint8_t> bytes(ipv4_size + tcp_size + payload.size(), 0);
    bytes[0] = static_cast<uint8_t>(0x40u | ipv4_words);
    const auto total_length = static_cast<uint16_t>(bytes.size());
    bytes[2] = static_cast<uint8_t>(total_length >> 8u);
    bytes[3] = static_cast<uint8_t>(total_length & 0xffu);
    bytes[8] = 64;
    bytes[9] = 6;
    bytes[12] = 10;
    bytes[13] = 20;
    bytes[14] = 30;
    bytes[15] = 40;
    bytes[16] = 192;
    bytes[17] = 168;
    bytes[18] = 1;
    bytes[19] = 9;
    bytes[ipv4_size] = 0x34;
    bytes[ipv4_size + 1] = 0x10;
    bytes[ipv4_size + 2] = 0x22;
    bytes[ipv4_size + 3] = 0x1f;
    bytes[ipv4_size + 4] = 0x12;
    bytes[ipv4_size + 5] = 0x34;
    bytes[ipv4_size + 6] = 0x56;
    bytes[ipv4_size + 7] = 0x78;
    bytes[ipv4_size + 12] = static_cast<uint8_t>(tcp_words << 4u);
    bytes[ipv4_size + 13] = flags;
    for (size_t index = 0; index < payload.size(); ++index) {
        bytes[ipv4_size + tcp_size + index] = payload[index];
    }
    return bytes;
}

CaptureRecord record_for(
    std::vector<uint8_t> bytes,
    uint32_t link_type = namter::dlt_raw,
    CaptureSource source = CaptureSource::pcap) {
    const auto length = static_cast<uint32_t>(bytes.size());
    return {
        .source = source,
        .timestamp_ns = 123'456'789,
        .link_type = link_type,
        .captured_length = length,
        .original_length = length,
        .bytes = std::move(bytes),
        .file_offset = 99,
    };
}

std::vector<uint8_t> ethernet_packet(
    const std::vector<uint16_t>& ether_types,
    const std::vector<uint8_t>& network_packet) {
    std::vector<uint8_t> bytes(12, 0);
    for (size_t index = 0; index < ether_types.size(); ++index) {
        const uint16_t ether_type = ether_types[index];
        bytes.push_back(static_cast<uint8_t>(ether_type >> 8u));
        bytes.push_back(static_cast<uint8_t>(ether_type & 0xffu));
        if (index + 1 < ether_types.size()) {
            bytes.push_back(0);
            bytes.push_back(static_cast<uint8_t>(index + 1));
        }
    }
    bytes.insert(bytes.end(), network_packet.begin(), network_packet.end());
    return bytes;
}

void expect_error(CaptureRecord record, CaptureError expected) {
    const auto result = PacketNormalizer::normalize(record);
    EXPECT_EQ(result.error, expected);
    EXPECT_FALSE(result.segment.has_value());
}

}  // namespace

TEST(PacketNormalizer, NormalizesRawIpv4TcpWithNetworkEndianFieldsAndProvenance) {
    const CaptureRecord record = record_for(
        raw_tcp_packet(namter::tcp_ack, {0xde, 0xad}),
        namter::dlt_raw,
        CaptureSource::windivert);
    const auto result = PacketNormalizer::normalize(record);

    ASSERT_EQ(result.error, CaptureError::none);
    ASSERT_TRUE(result.segment.has_value());
    EXPECT_EQ(result.segment->flow.source_address, 0x0a141e28u);
    EXPECT_EQ(result.segment->flow.destination_address, 0xc0a80109u);
    EXPECT_EQ(result.segment->flow.source_port, 13'328);
    EXPECT_EQ(result.segment->flow.destination_port, 8'735);
    EXPECT_EQ(result.segment->sequence, 0x12345678u);
    EXPECT_EQ(result.segment->flags, namter::tcp_ack);
    EXPECT_EQ(result.segment->payload.size(), 2u);
    EXPECT_EQ(result.segment->payload[0], 0xde);
    EXPECT_EQ(result.segment->payload[1], 0xad);
    EXPECT_EQ(result.segment->provenance.source, CaptureSource::windivert);
    EXPECT_EQ(result.segment->provenance.timestamp_ns, 123'456'789u);
    EXPECT_EQ(result.segment->provenance.link_type, namter::dlt_raw);
    EXPECT_EQ(result.segment->provenance.captured_length, record.captured_length);
    EXPECT_EQ(result.segment->provenance.original_length, record.original_length);
    EXPECT_EQ(result.segment->provenance.file_offset, 99u);
}

TEST(PacketNormalizer, OwnsPayloadAfterTemporaryAndSourceStorageAreDestroyed) {
    const auto from_temporary = PacketNormalizer::normalize(
        record_for(raw_tcp_packet(namter::tcp_ack, {0xde, 0xad})));
    ASSERT_TRUE(from_temporary.segment.has_value());

    auto source = record_for(raw_tcp_packet(namter::tcp_ack, {0xbe, 0xef}));
    const auto from_overwritten_source = PacketNormalizer::normalize(source);
    ASSERT_TRUE(from_overwritten_source.segment.has_value());
    std::ranges::fill(source.bytes, uint8_t{0});
    source.bytes.clear();
    source.bytes.shrink_to_fit();

    EXPECT_EQ(from_temporary.segment->payload, (std::vector<uint8_t>{0xde, 0xad}));
    EXPECT_EQ(from_overwritten_source.segment->payload, (std::vector<uint8_t>{0xbe, 0xef}));
}

TEST(PacketNormalizer, NormalizesEthernetIpv4AndIgnoresTrailingPadding) {
    std::vector<uint8_t> ethernet(14, 0);
    ethernet[12] = 0x08;
    ethernet[13] = 0x00;
    const auto raw = raw_tcp_packet(namter::tcp_ack, {1, 2, 3});
    ethernet.insert(ethernet.end(), raw.begin(), raw.end());
    ethernet.insert(ethernet.end(), 8, 0xee);
    const auto result = PacketNormalizer::normalize(record_for(std::move(ethernet), namter::dlt_en10mb));

    ASSERT_EQ(result.error, CaptureError::none);
    ASSERT_TRUE(result.segment.has_value());
    EXPECT_EQ(result.segment->payload.size(), 3u);
}

TEST(PacketNormalizer, NormalizesSingleTaggedEthernetIpv4) {
    const auto ethernet = ethernet_packet({0x8100, 0x0800}, raw_tcp_packet());
    const auto result = PacketNormalizer::normalize(record_for(ethernet, namter::dlt_en10mb));
    EXPECT_EQ(result.error, CaptureError::none);
    EXPECT_TRUE(result.segment.has_value());
}

TEST(PacketNormalizer, NormalizesDoubleTaggedEthernetIpv4QinQ) {
    const auto ethernet = ethernet_packet({0x88a8, 0x8100, 0x0800}, raw_tcp_packet());
    const auto result = PacketNormalizer::normalize(record_for(ethernet, namter::dlt_en10mb));
    EXPECT_EQ(result.error, CaptureError::none);
    EXPECT_TRUE(result.segment.has_value());
}

TEST(PacketNormalizer, RejectsTruncatedEthernetVlanTag) {
    std::vector<uint8_t> ethernet(12, 0);
    ethernet.insert(ethernet.end(), {0x81, 0x00, 0x00, 0x01, 0x08});
    expect_error(
        record_for(std::move(ethernet), namter::dlt_en10mb),
        CaptureError::truncated_vlan_tag);
}

TEST(PacketNormalizer, RejectsMoreThanTwoEthernetVlanTags) {
    const auto ethernet = ethernet_packet(
        {0x88a8, 0x8100, 0x8100, 0x0800},
        raw_tcp_packet());
    expect_error(
        record_for(ethernet, namter::dlt_en10mb),
        CaptureError::vlan_tag_depth_exceeded);
}

TEST(PacketNormalizer, RejectsVlanEncapsulatedNonIpv4) {
    const auto ethernet = ethernet_packet({0x8100, 0x86dd}, std::vector<uint8_t>(40, 0));
    expect_error(record_for(ethernet, namter::dlt_en10mb), CaptureError::non_ipv4);
}

TEST(PacketNormalizer, PreservesAckOnlyFinAndRstSegmentsWithoutPayload) {
    for (const uint8_t flags : {
             namter::tcp_ack,
             namter::tcp_fin,
             namter::tcp_rst,
             static_cast<uint8_t>(namter::tcp_ack | namter::tcp_fin),
             static_cast<uint8_t>(namter::tcp_ack | namter::tcp_rst)}) {
        const auto result = PacketNormalizer::normalize(record_for(raw_tcp_packet(flags)));
        ASSERT_EQ(result.error, CaptureError::none);
        ASSERT_TRUE(result.segment.has_value());
        EXPECT_EQ(result.segment->flags, flags);
        EXPECT_TRUE(result.segment->payload.empty());
    }
}

TEST(PacketNormalizer, AcceptsUnsetIpv4AndTcpChecksums) {
    const auto result = PacketNormalizer::normalize(record_for(raw_tcp_packet()));
    EXPECT_EQ(result.error, CaptureError::none);
    EXPECT_TRUE(result.segment.has_value());
}

TEST(PacketNormalizer, RejectsUnsupportedAndTruncatedLinkHeaders) {
    expect_error(record_for(raw_tcp_packet(), 147), CaptureError::unsupported_link_type);
    expect_error(record_for(std::vector<uint8_t>(13), namter::dlt_en10mb), CaptureError::truncated_link_header);

    std::vector<uint8_t> ethernet(14, 0);
    ethernet[12] = 0x86;
    ethernet[13] = 0xdd;
    expect_error(record_for(std::move(ethernet), namter::dlt_en10mb), CaptureError::non_ipv4);
}

TEST(PacketNormalizer, RejectsCaptureLengthMismatchBeforePayloadAllocation) {
    auto record = record_for(raw_tcp_packet());
    ++record.captured_length;
    expect_error(std::move(record), CaptureError::capture_length_mismatch);
}

TEST(PacketNormalizer, ValidatesIpv4VersionIhlTotalLengthAndProtocol) {
    auto bytes = raw_tcp_packet();
    bytes[0] = 0x65;
    expect_error(record_for(std::move(bytes)), CaptureError::invalid_ipv4_version);

    bytes = raw_tcp_packet();
    bytes[0] = 0x44;
    expect_error(record_for(std::move(bytes)), CaptureError::invalid_ipv4_header_length);

    bytes = raw_tcp_packet();
    bytes[0] = 0x4f;
    expect_error(record_for(std::move(bytes)), CaptureError::truncated_ipv4_header);

    bytes = raw_tcp_packet();
    bytes[2] = 0;
    bytes[3] = 19;
    expect_error(record_for(std::move(bytes)), CaptureError::invalid_ipv4_total_length);

    bytes = raw_tcp_packet();
    bytes[2] = 0xff;
    bytes[3] = 0xff;
    expect_error(record_for(std::move(bytes)), CaptureError::truncated_ipv4_packet);

    bytes = raw_tcp_packet();
    bytes[9] = 17;
    expect_error(record_for(std::move(bytes)), CaptureError::non_tcp_ipv4);
}

TEST(PacketNormalizer, RejectsIpv4MoreFragmentsBeforeTcpParsing) {
    auto bytes = raw_tcp_packet();
    bytes[6] = 0x20;
    expect_error(record_for(std::move(bytes)), CaptureError::ipv4_more_fragments);
}

TEST(PacketNormalizer, RejectsIpv4NonzeroFragmentOffsetBeforeTcpParsing) {
    auto bytes = raw_tcp_packet();
    bytes[7] = 0x01;
    expect_error(record_for(std::move(bytes)), CaptureError::ipv4_nonzero_fragment_offset);
}

TEST(PacketNormalizer, AllowsUnfragmentedIpv4WithDontFragmentFlag) {
    auto bytes = raw_tcp_packet();
    bytes[6] = 0x40;
    const auto result = PacketNormalizer::normalize(record_for(std::move(bytes)));
    EXPECT_EQ(result.error, CaptureError::none);
    EXPECT_TRUE(result.segment.has_value());
}

TEST(PacketNormalizer, ValidatesTcpHeaderAndDataOffset) {
    auto bytes = raw_tcp_packet();
    bytes[2] = 0;
    bytes[3] = 39;
    bytes.resize(39);
    expect_error(record_for(std::move(bytes)), CaptureError::truncated_tcp_header);

    bytes = raw_tcp_packet();
    bytes[20 + 12] = 0x40;
    expect_error(record_for(std::move(bytes)), CaptureError::invalid_tcp_data_offset);

    bytes = raw_tcp_packet();
    bytes[20 + 12] = 0xf0;
    expect_error(record_for(std::move(bytes)), CaptureError::truncated_tcp_header);
}
