#include "capture_record.hpp"

#include <array>
#include <limits>

namespace namter {
namespace {

uint16_t read_u16(const uint8_t* bytes, PcapByteOrder order) noexcept {
    if (order == PcapByteOrder::little_endian) {
        return static_cast<uint16_t>(
            static_cast<uint16_t>(bytes[0]) |
            static_cast<uint16_t>(static_cast<uint16_t>(bytes[1]) << 8u));
    }
    return static_cast<uint16_t>(
        static_cast<uint16_t>(static_cast<uint16_t>(bytes[0]) << 8u) |
        static_cast<uint16_t>(bytes[1]));
}

uint32_t read_u32(const uint8_t* bytes, PcapByteOrder order) noexcept {
    uint32_t value = 0;
    if (order == PcapByteOrder::little_endian) {
        for (size_t index = 0; index < 4; ++index) {
            value |= static_cast<uint32_t>(bytes[index]) << (index * 8u);
        }
    } else {
        for (size_t index = 0; index < 4; ++index) {
            value = static_cast<uint32_t>((value << 8u) | bytes[index]);
        }
    }
    return value;
}

bool checked_timestamp(
    uint32_t seconds,
    uint32_t fraction,
    TimestampPrecision precision,
    uint64_t& timestamp_ns,
    CaptureError& error) noexcept {
    const uint32_t fraction_limit =
        precision == TimestampPrecision::microseconds ? 1'000'000u : 1'000'000'000u;
    if (fraction >= fraction_limit) {
        error = CaptureError::timestamp_fraction_out_of_range;
        return false;
    }

    constexpr uint64_t nanoseconds_per_second = 1'000'000'000ull;
    const uint64_t scaled_fraction = precision == TimestampPrecision::microseconds
        ? static_cast<uint64_t>(fraction) * 1'000ull
        : static_cast<uint64_t>(fraction);
    const uint64_t seconds_ns = static_cast<uint64_t>(seconds) * nanoseconds_per_second;
    if (seconds_ns > std::numeric_limits<uint64_t>::max() - scaled_fraction) {
        error = CaptureError::timestamp_overflow;
        return false;
    }
    timestamp_ns = seconds_ns + scaled_fraction;
    return true;
}

}  // namespace

PcapReader::PcapReader(std::istream& input, uint32_t maximum_capture_length)
    : input_(&input), maximum_capture_length_(maximum_capture_length) {
    std::array<uint8_t, 24> bytes{};
    input_->read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    const auto bytes_read = static_cast<size_t>(input_->gcount());
    next_offset_ = bytes_read;
    if (bytes_read != bytes.size()) {
        error_ = CaptureError::truncated_global_header;
        return;
    }

    PcapHeader parsed{};
    const std::array<uint8_t, 4> magic{bytes[0], bytes[1], bytes[2], bytes[3]};
    if (magic == std::array<uint8_t, 4>{0xd4, 0xc3, 0xb2, 0xa1}) {
        parsed.byte_order = PcapByteOrder::little_endian;
        parsed.precision = TimestampPrecision::microseconds;
    } else if (magic == std::array<uint8_t, 4>{0xa1, 0xb2, 0xc3, 0xd4}) {
        parsed.byte_order = PcapByteOrder::big_endian;
        parsed.precision = TimestampPrecision::microseconds;
    } else if (magic == std::array<uint8_t, 4>{0x4d, 0x3c, 0xb2, 0xa1}) {
        parsed.byte_order = PcapByteOrder::little_endian;
        parsed.precision = TimestampPrecision::nanoseconds;
    } else if (magic == std::array<uint8_t, 4>{0xa1, 0xb2, 0x3c, 0x4d}) {
        parsed.byte_order = PcapByteOrder::big_endian;
        parsed.precision = TimestampPrecision::nanoseconds;
    } else {
        error_ = CaptureError::invalid_pcap_magic;
        return;
    }

    parsed.version_major = read_u16(bytes.data() + 4, parsed.byte_order);
    parsed.version_minor = read_u16(bytes.data() + 6, parsed.byte_order);
    parsed.snaplen = read_u32(bytes.data() + 16, parsed.byte_order);
    parsed.link_type = read_u32(bytes.data() + 20, parsed.byte_order);
    if (parsed.version_major != 2 || parsed.version_minor != 4) {
        error_ = CaptureError::unsupported_pcap_version;
        return;
    }
    if (parsed.snaplen == 0) {
        error_ = CaptureError::invalid_pcap_snaplen;
        return;
    }
    header_ = parsed;
}

const std::optional<PcapHeader>& PcapReader::header() const noexcept {
    return header_;
}

CaptureError PcapReader::error() const noexcept {
    return error_;
}

bool PcapReader::eof() const noexcept {
    return eof_;
}

bool PcapReader::read_next(CaptureRecord& record) {
    if (error_ != CaptureError::none || eof_ || !header_.has_value()) {
        return false;
    }

    const uint64_t record_offset = next_offset_;
    std::array<uint8_t, 16> bytes{};
    input_->read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    const auto bytes_read = static_cast<size_t>(input_->gcount());
    next_offset_ += bytes_read;
    if (bytes_read == 0 && input_->eof()) {
        eof_ = true;
        return false;
    }
    if (bytes_read != bytes.size()) {
        error_ = CaptureError::truncated_record_header;
        return false;
    }

    const auto order = header_->byte_order;
    const uint32_t seconds = read_u32(bytes.data(), order);
    const uint32_t fraction = read_u32(bytes.data() + 4, order);
    const uint32_t captured_length = read_u32(bytes.data() + 8, order);
    const uint32_t original_length = read_u32(bytes.data() + 12, order);

    uint64_t timestamp_ns = 0;
    if (!checked_timestamp(seconds, fraction, header_->precision, timestamp_ns, error_)) {
        return false;
    }
    if (captured_length > header_->snaplen) {
        error_ = CaptureError::captured_length_exceeds_snaplen;
        return false;
    }
    if (captured_length > maximum_capture_length_) {
        error_ = CaptureError::captured_length_exceeds_limit;
        return false;
    }
    if (original_length < captured_length) {
        error_ = CaptureError::original_length_smaller_than_captured;
        return false;
    }

    CaptureRecord parsed{
        .source = CaptureSource::pcap,
        .timestamp_ns = timestamp_ns,
        .link_type = header_->link_type,
        .captured_length = captured_length,
        .original_length = original_length,
        .bytes = std::vector<uint8_t>(captured_length),
        .file_offset = record_offset,
    };
    if (captured_length != 0) {
        input_->read(
            reinterpret_cast<char*>(parsed.bytes.data()),
            static_cast<std::streamsize>(captured_length));
        const auto payload_bytes_read = static_cast<uint32_t>(input_->gcount());
        next_offset_ += payload_bytes_read;
        if (payload_bytes_read != captured_length) {
            error_ = CaptureError::truncated_record_data;
            return false;
        }
    }

    record = std::move(parsed);
    return true;
}

}  // namespace namter
