#pragma once

#include <compare>
#include <cstdint>
#include <istream>
#include <optional>
#include <vector>

namespace namter {

inline constexpr uint32_t dlt_en10mb = 1;
inline constexpr uint32_t dlt_raw = 101;

inline constexpr uint8_t tcp_fin = 0x01;
inline constexpr uint8_t tcp_rst = 0x04;
inline constexpr uint8_t tcp_ack = 0x10;

enum class CaptureSource : uint8_t {
    windivert,
    npcap,
    pcap,
};

enum class PcapByteOrder : uint8_t {
    little_endian,
    big_endian,
};

enum class TimestampPrecision : uint8_t {
    microseconds,
    nanoseconds,
};

enum class CaptureError : uint8_t {
    none,
    truncated_global_header,
    invalid_pcap_magic,
    unsupported_pcap_version,
    invalid_pcap_snaplen,
    truncated_record_header,
    timestamp_fraction_out_of_range,
    timestamp_overflow,
    captured_length_exceeds_snaplen,
    captured_length_exceeds_limit,
    original_length_smaller_than_captured,
    truncated_record_data,
    capture_length_mismatch,
    unsupported_link_type,
    truncated_link_header,
    truncated_vlan_tag,
    vlan_tag_depth_exceeded,
    non_ipv4,
    truncated_ipv4_header,
    invalid_ipv4_version,
    invalid_ipv4_header_length,
    invalid_ipv4_total_length,
    truncated_ipv4_packet,
    non_tcp_ipv4,
    ipv4_more_fragments,
    ipv4_nonzero_fragment_offset,
    truncated_tcp_header,
    invalid_tcp_data_offset,
};

struct PcapHeader {
    PcapByteOrder byte_order{};
    TimestampPrecision precision{};
    uint16_t version_major = 0;
    uint16_t version_minor = 0;
    uint32_t snaplen = 0;
    uint32_t link_type = 0;
};

struct CaptureRecord {
    CaptureSource source{};
    uint64_t timestamp_ns = 0;
    uint32_t link_type = 0;
    uint32_t captured_length = 0;
    uint32_t original_length = 0;
    std::vector<uint8_t> bytes;
    uint64_t file_offset = 0;
};

struct CaptureProvenance {
    CaptureSource source{};
    uint64_t timestamp_ns = 0;
    uint32_t link_type = 0;
    uint32_t captured_length = 0;
    uint32_t original_length = 0;
    uint64_t file_offset = 0;
};

struct FlowTuple {
    uint32_t source_address = 0;
    uint32_t destination_address = 0;
    uint16_t source_port = 0;
    uint16_t destination_port = 0;

    auto operator<=>(const FlowTuple&) const = default;
};

struct TcpSegment {
    FlowTuple flow;
    uint32_t sequence = 0;
    uint8_t flags = 0;
    std::vector<uint8_t> payload;
    CaptureProvenance provenance;
};

struct NormalizationResult {
    CaptureError error = CaptureError::none;
    std::optional<TcpSegment> segment;
};

class PcapReader {
public:
    explicit PcapReader(std::istream& input, uint32_t maximum_capture_length = 16u * 1024u * 1024u);

    [[nodiscard]] const std::optional<PcapHeader>& header() const noexcept;
    [[nodiscard]] CaptureError error() const noexcept;
    [[nodiscard]] bool eof() const noexcept;
    bool read_next(CaptureRecord& record);

private:
    std::istream* input_;
    std::optional<PcapHeader> header_;
    CaptureError error_ = CaptureError::none;
    uint32_t maximum_capture_length_;
    uint64_t next_offset_ = 0;
    bool eof_ = false;
};

class PacketNormalizer {
public:
    [[nodiscard]] static NormalizationResult normalize(const CaptureRecord& record);
};

}  // namespace namter
