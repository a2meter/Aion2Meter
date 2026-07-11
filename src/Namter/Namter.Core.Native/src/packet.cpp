#include "capture_record.hpp"
#include "namter/core.h"

#include <cstddef>

namespace namter {
namespace {

uint16_t read_network_u16(const std::vector<uint8_t> &bytes, size_t offset) noexcept {
    return static_cast<uint16_t>(static_cast<uint16_t>(static_cast<uint16_t>(bytes[offset]) << 8u) |
                                 static_cast<uint16_t>(bytes[offset + 1]));
}

uint32_t read_network_u32(const std::vector<uint8_t> &bytes, size_t offset) noexcept {
    uint32_t value = 0;
    for (size_t index = 0; index < 4; ++index) {
        value = static_cast<uint32_t>((value << 8u) | bytes[offset + index]);
    }
    return value;
}

NormalizationResult failure(CaptureError error) noexcept {
    return {.error = error, .segment = std::nullopt};
}

} // namespace

NormalizationResult PacketNormalizer::normalize(const CaptureRecord &record) {
    if (record.bytes.size() != static_cast<size_t>(record.captured_length)) {
        return failure(CaptureError::capture_length_mismatch);
    }

    size_t network_offset = 0;
    if (record.link_type == dlt_en10mb) {
        constexpr size_t ethernet_header_size = 14;
        if (record.bytes.size() < ethernet_header_size) {
            return failure(CaptureError::truncated_link_header);
        }

        constexpr uint16_t ether_type_ipv4 = 0x0800;
        constexpr uint16_t ether_type_dot1q = 0x8100;
        constexpr uint16_t ether_type_dot1ad = 0x88a8;
        constexpr size_t vlan_tag_size = 4;
        constexpr size_t maximum_vlan_tags = 2;
        uint16_t ether_type = read_network_u16(record.bytes, 12);
        network_offset = ethernet_header_size;
        size_t vlan_tag_count = 0;
        while (ether_type == ether_type_dot1q || ether_type == ether_type_dot1ad) {
            if (vlan_tag_count == maximum_vlan_tags) {
                return failure(CaptureError::vlan_tag_depth_exceeded);
            }
            if (record.bytes.size() - network_offset < vlan_tag_size) {
                return failure(CaptureError::truncated_vlan_tag);
            }
            ether_type = read_network_u16(record.bytes, network_offset + 2);
            network_offset += vlan_tag_size;
            ++vlan_tag_count;
        }
        if (ether_type != ether_type_ipv4) {
            return failure(CaptureError::non_ipv4);
        }
    } else if (record.link_type != dlt_raw) {
        return failure(CaptureError::unsupported_link_type);
    }

    constexpr size_t minimum_ipv4_header_size = 20;
    const size_t network_size = record.bytes.size() - network_offset;
    if (network_size < minimum_ipv4_header_size) {
        return failure(CaptureError::truncated_ipv4_header);
    }

    const uint8_t version_and_ihl = record.bytes[network_offset];
    if ((version_and_ihl >> 4u) != 4) {
        return failure(CaptureError::invalid_ipv4_version);
    }
    const uint8_t ihl_words = static_cast<uint8_t>(version_and_ihl & 0x0fu);
    if (ihl_words < 5) {
        return failure(CaptureError::invalid_ipv4_header_length);
    }
    const size_t ipv4_header_size = static_cast<size_t>(ihl_words) * 4u;
    if (ipv4_header_size > network_size) {
        return failure(CaptureError::truncated_ipv4_header);
    }

    const size_t ipv4_total_length = read_network_u16(record.bytes, network_offset + 2);
    if (ipv4_total_length < ipv4_header_size) {
        return failure(CaptureError::invalid_ipv4_total_length);
    }
    if (ipv4_total_length > network_size) {
        return failure(CaptureError::truncated_ipv4_packet);
    }
    if (record.bytes[network_offset + 9] != 6) {
        return failure(CaptureError::non_tcp_ipv4);
    }

    const uint16_t fragment_field = read_network_u16(record.bytes, network_offset + 6);
    constexpr uint16_t more_fragments = 0x2000;
    constexpr uint16_t fragment_offset_mask = 0x1fff;
    if ((fragment_field & fragment_offset_mask) != 0) {
        return failure(CaptureError::ipv4_nonzero_fragment_offset);
    }
    if ((fragment_field & more_fragments) != 0) {
        return failure(CaptureError::ipv4_more_fragments);
    }

    constexpr size_t minimum_tcp_header_size = 20;
    const size_t tcp_offset = network_offset + ipv4_header_size;
    const size_t tcp_size = ipv4_total_length - ipv4_header_size;
    if (tcp_size < minimum_tcp_header_size) {
        return failure(CaptureError::truncated_tcp_header);
    }

    const uint8_t tcp_words = static_cast<uint8_t>(record.bytes[tcp_offset + 12] >> 4u);
    if (tcp_words < 5) {
        return failure(CaptureError::invalid_tcp_data_offset);
    }
    const size_t tcp_header_size = static_cast<size_t>(tcp_words) * 4u;
    if (tcp_header_size > tcp_size) {
        return failure(CaptureError::truncated_tcp_header);
    }

    const size_t payload_offset = tcp_offset + tcp_header_size;
    const size_t payload_size = tcp_size - tcp_header_size;
    TcpSegment segment{
        .flow =
            {
                .source_address = read_network_u32(record.bytes, network_offset + 12),
                .destination_address = read_network_u32(record.bytes, network_offset + 16),
                .source_port = read_network_u16(record.bytes, tcp_offset),
                .destination_port = read_network_u16(record.bytes, tcp_offset + 2),
            },
        .sequence = read_network_u32(record.bytes, tcp_offset + 4),
        .flags = record.bytes[tcp_offset + 13],
        .payload = std::vector<uint8_t>(record.bytes.data() + payload_offset,
                                        record.bytes.data() + payload_offset + payload_size),
        .provenance =
            {
                .source = record.source,
                .timestamp_ns = record.timestamp_ns,
                .link_type = record.link_type,
                .captured_length = record.captured_length,
                .original_length = record.original_length,
                .file_offset = record.file_offset,
                .direction = record.direction,
            },
    };
    if (segment.provenance.direction == CaptureDirection::unknown) {
        if (segment.flow.source_port == NM_CORE_DEFAULT_GAME_PORT)
            segment.provenance.direction = CaptureDirection::inbound;
        else if (segment.flow.destination_port == NM_CORE_DEFAULT_GAME_PORT)
            segment.provenance.direction = CaptureDirection::outbound;
    }
    return {.error = CaptureError::none, .segment = segment};
}

} // namespace namter
