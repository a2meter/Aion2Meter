#include "capture_record.hpp"
#include "varint.hpp"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <iterator>
#include <limits>
#include <memory>
#include <span>
#include <stdexcept>
#include <utility>
#include <vector>

namespace namter {
namespace {

constexpr uint32_t frame_length_bias = 4u;
constexpr size_t maximum_batch_depth = 4u;
constexpr std::array<uint8_t, 3> protocol_boundary_marker{0x06, 0x00, 0x36};

bool is_optional_marker(uint8_t value) noexcept {
    return value >= 0xf0u && value <= 0xfeu;
}

struct ParsedFrame {
    size_t prefix_size = 0;
    size_t frame_size = 0;
};

bool frame_size_from_prefix(
    std::span<const uint8_t> bytes,
    size_t maximum,
    ParsedFrame& parsed) noexcept {
    const auto decoded = decode_u32_varint(bytes);
    if (decoded.status != VarintStatus::complete || decoded.value < frame_length_bias) {
        return false;
    }
    const size_t body_size = static_cast<size_t>(decoded.value - frame_length_bias);
    if (body_size > maximum || decoded.bytes_consumed > maximum - body_size) {
        return false;
    }
    parsed = ParsedFrame{
        .prefix_size = decoded.bytes_consumed,
        .frame_size = decoded.bytes_consumed + body_size,
    };
    return true;
}

size_t payload_offset(std::span<const uint8_t> body) noexcept {
    return !body.empty() && is_optional_marker(body.front()) ? 1u : 0u;
}

bool starts_with_protocol_marker(std::span<const uint8_t> body) noexcept {
    const size_t offset = payload_offset(body);
    return body.size() >= offset + protocol_boundary_marker.size() &&
           std::equal(
               protocol_boundary_marker.begin(),
               protocol_boundary_marker.end(),
               body.begin() + static_cast<std::ptrdiff_t>(offset));
}

}  // namespace

struct IncrementalFramer::Impl {
    struct ProvenanceRun {
        size_t begin = 0;
        size_t end = 0;
        CaptureProvenance provenance;
    };

    explicit Impl(FrameConfig value) : config(value) {
        if (config.max_frame_bytes == 0 || config.max_decompressed_bytes == 0 ||
            config.max_frame_bytes > std::numeric_limits<size_t>::max() - 5u) {
            throw std::invalid_argument("frame limits must be positive");
        }
    }

    FrameConfig config;
    FrameState state = FrameState::need_length;
    bool has_epoch = false;
    FlowTuple flow;
    uint64_t epoch = 0;
    std::array<uint8_t, 5> length_bytes{};
    size_t length_size = 0;
    size_t body_remaining = 0;
    std::vector<uint8_t> frame_bytes;
    CaptureProvenance first_provenance;
    CaptureProvenance last_provenance;
    std::vector<uint8_t> resync_bytes;
    std::vector<ProvenanceRun> resync_runs;

    void clear_regular() noexcept {
        length_size = 0;
        body_remaining = 0;
        frame_bytes.clear();
        first_provenance = {};
        last_provenance = {};
    }

    void clear_all() noexcept {
        state = FrameState::need_length;
        clear_regular();
        resync_bytes.clear();
        resync_runs.clear();
        has_epoch = false;
    }

    void begin_epoch(const StreamChunk& chunk) {
        if (!has_epoch || flow != chunk.flow || epoch != chunk.epoch) {
            clear_all();
            flow = chunk.flow;
            epoch = chunk.epoch;
            has_epoch = true;
        }
    }

    FrameDiagnostic diagnostic(
        FrameDiagnosticCode code,
        const CaptureProvenance& first,
        const CaptureProvenance& last) const {
        return FrameDiagnostic{
            .code = code,
            .flow = flow,
            .epoch = epoch,
            .first_provenance = first,
            .last_provenance = last,
            .first_timestamp_ns = first.timestamp_ns,
            .last_timestamp_ns = last.timestamp_ns,
        };
    }

    FrameDiagnosticCode decode_frame(
        std::span<const uint8_t> frame,
        size_t prefix_size,
        const CaptureProvenance& first,
        const CaptureProvenance& last,
        size_t depth,
        std::vector<ProtocolMessage>& output) const {
        if (prefix_size > frame.size()) {
            return FrameDiagnosticCode::invalid_nested_frame;
        }
        const auto body = frame.subspan(prefix_size);
        const size_t offset = payload_offset(body);
        if (offset == body.size()) {
            output.push_back(ProtocolMessage{
                .flow = flow,
                .epoch = epoch,
                .bytes = std::vector<uint8_t>(frame.begin(), frame.end()),
                .first_provenance = first,
                .last_provenance = last,
                .first_timestamp_ns = first.timestamp_ns,
                .last_timestamp_ns = last.timestamp_ns,
            });
            return FrameDiagnosticCode::none;
        }

        if (body[offset] != 0xffu) {
            output.push_back(ProtocolMessage{
                .flow = flow,
                .epoch = epoch,
                .bytes = std::vector<uint8_t>(frame.begin(), frame.end()),
                .first_provenance = first,
                .last_provenance = last,
                .first_timestamp_ns = first.timestamp_ns,
                .last_timestamp_ns = last.timestamp_ns,
            });
            return FrameDiagnosticCode::none;
        }
        if (body.size() < offset + 2u || body[offset + 1u] != 0xffu) {
            return FrameDiagnosticCode::invalid_marker;
        }
        if (depth >= maximum_batch_depth) {
            return FrameDiagnosticCode::invalid_nested_frame;
        }

        std::vector<std::vector<uint8_t>> nested_frames;
        const auto expansion_error = detail::expand_lz4_batch(
            body.subspan(offset),
            config.max_frame_bytes,
            config.max_decompressed_bytes,
            nested_frames);
        if (expansion_error != FrameDiagnosticCode::none) {
            return expansion_error;
        }

        std::vector<ProtocolMessage> nested_output;
        for (const auto& nested : nested_frames) {
            ParsedFrame parsed;
            if (!frame_size_from_prefix(nested, config.max_frame_bytes, parsed) ||
                parsed.frame_size != nested.size()) {
                return FrameDiagnosticCode::invalid_nested_frame;
            }
            const auto nested_error = decode_frame(
                nested,
                parsed.prefix_size,
                first,
                last,
                depth + 1u,
                nested_output);
            if (nested_error != FrameDiagnosticCode::none) {
                return nested_error;
            }
        }
        output.insert(
            output.end(),
            std::make_move_iterator(nested_output.begin()),
            std::make_move_iterator(nested_output.end()));
        return FrameDiagnosticCode::none;
    }

    void append_resync(
        std::span<const uint8_t> bytes,
        const CaptureProvenance& provenance) {
        if (bytes.empty()) {
            return;
        }
        const size_t retention_limit = config.max_frame_bytes + 5u;
        if (bytes.size() >= retention_limit) {
            resync_bytes.assign(bytes.end() - static_cast<std::ptrdiff_t>(retention_limit), bytes.end());
            resync_runs.clear();
            resync_runs.push_back(ProvenanceRun{
                .begin = 0,
                .end = retention_limit,
                .provenance = provenance,
            });
            return;
        }
        if (resync_bytes.size() > retention_limit - bytes.size()) {
            erase_resync_prefix(
                resync_bytes.size() - (retention_limit - bytes.size()));
        }
        const size_t begin = resync_bytes.size();
        resync_bytes.insert(resync_bytes.end(), bytes.begin(), bytes.end());
        resync_runs.push_back(ProvenanceRun{
            .begin = begin,
            .end = resync_bytes.size(),
            .provenance = provenance,
        });
    }

    const CaptureProvenance& resync_provenance_at(size_t index) const {
        for (const auto& run : resync_runs) {
            if (index >= run.begin && index < run.end) {
                return run.provenance;
            }
        }
        return resync_runs.back().provenance;
    }

    void erase_resync_prefix(size_t count) {
        if (count == 0) {
            return;
        }
        resync_bytes.erase(
            resync_bytes.begin(),
            resync_bytes.begin() + static_cast<std::ptrdiff_t>(count));
        std::vector<ProvenanceRun> retained;
        for (const auto& run : resync_runs) {
            if (run.end <= count) {
                continue;
            }
            retained.push_back(ProvenanceRun{
                .begin = run.begin > count ? run.begin - count : 0u,
                .end = run.end - count,
                .provenance = run.provenance,
            });
        }
        resync_runs = std::move(retained);
    }

    void enter_resync() noexcept {
        state = FrameState::need_resync;
        clear_regular();
    }

    void consume_normal(
        std::span<const uint8_t> bytes,
        const CaptureProvenance& provenance,
        std::vector<FrameOutput>& outputs) {
        size_t offset = 0;
        while (offset < bytes.size() && state != FrameState::need_resync) {
            if (state == FrameState::need_length) {
                if (length_size == 0) {
                    first_provenance = provenance;
                }
                length_bytes[length_size++] = bytes[offset++];
                last_provenance = provenance;
                const auto decoded = decode_u32_varint(
                    std::span<const uint8_t>(length_bytes.data(), length_size));
                if (decoded.status == VarintStatus::incomplete) {
                    continue;
                }
                if (decoded.status == VarintStatus::invalid) {
                    outputs.emplace_back(diagnostic(
                        FrameDiagnosticCode::overlong_varint,
                        first_provenance,
                        last_provenance));
                    enter_resync();
                    append_resync(bytes.subspan(offset), provenance);
                    break;
                }
                if (decoded.value < frame_length_bias) {
                    outputs.emplace_back(diagnostic(
                        FrameDiagnosticCode::invalid_frame_length,
                        first_provenance,
                        last_provenance));
                    enter_resync();
                    append_resync(bytes.subspan(offset), provenance);
                    break;
                }

                const size_t body_size = static_cast<size_t>(decoded.value - frame_length_bias);
                if (body_size > config.max_frame_bytes ||
                    decoded.bytes_consumed > config.max_frame_bytes - body_size) {
                    outputs.emplace_back(diagnostic(
                        FrameDiagnosticCode::frame_too_large,
                        first_provenance,
                        last_provenance));
                    enter_resync();
                    append_resync(bytes.subspan(offset), provenance);
                    break;
                }

                const size_t frame_size = decoded.bytes_consumed + body_size;
                frame_bytes.reserve(frame_size);
                frame_bytes.insert(
                    frame_bytes.end(),
                    length_bytes.begin(),
                    length_bytes.begin() + static_cast<std::ptrdiff_t>(length_size));
                body_remaining = body_size;
                state = FrameState::need_body;
            }

            if (state == FrameState::need_body) {
                const size_t available = bytes.size() - offset;
                const size_t taken = std::min(body_remaining, available);
                frame_bytes.insert(
                    frame_bytes.end(),
                    bytes.begin() + static_cast<std::ptrdiff_t>(offset),
                    bytes.begin() + static_cast<std::ptrdiff_t>(offset + taken));
                offset += taken;
                body_remaining -= taken;
                if (taken != 0) {
                    last_provenance = provenance;
                }
                if (body_remaining != 0) {
                    continue;
                }

                std::vector<ProtocolMessage> decoded_messages;
                const auto error = decode_frame(
                    frame_bytes,
                    length_size,
                    first_provenance,
                    last_provenance,
                    0,
                    decoded_messages);
                if (error != FrameDiagnosticCode::none) {
                    outputs.emplace_back(diagnostic(error, first_provenance, last_provenance));
                    enter_resync();
                    append_resync(bytes.subspan(offset), provenance);
                    break;
                }
                outputs.insert(
                    outputs.end(),
                    std::make_move_iterator(decoded_messages.begin()),
                    std::make_move_iterator(decoded_messages.end()));
                state = FrameState::need_length;
                clear_regular();
            }
        }
    }

    bool is_validated_resync_boundary(
        std::span<const uint8_t> candidate,
        size_t prefix_size) const {
        const auto body = candidate.subspan(prefix_size);
        const size_t offset = payload_offset(body);
        if (body.size() >= offset + 2u &&
            body[offset] == 0xffu && body[offset + 1u] == 0xffu) {
            std::vector<ProtocolMessage> ignored;
            return decode_frame(
                       candidate,
                       prefix_size,
                       resync_provenance_at(0),
                       resync_provenance_at(candidate.size() - 1u),
                       0,
                       ignored) == FrameDiagnosticCode::none;
        }
        return starts_with_protocol_marker(body);
    }

    void drain_resync(std::vector<FrameOutput>& outputs) {
        while (state == FrameState::need_resync) {
            bool found = false;
            size_t candidate_offset = 0;
            ParsedFrame candidate;

            for (size_t offset = 0; offset < resync_bytes.size(); ++offset) {
                ParsedFrame parsed;
                const auto remaining = std::span<const uint8_t>(resync_bytes).subspan(offset);
                if (!frame_size_from_prefix(remaining, config.max_frame_bytes, parsed) ||
                    parsed.frame_size > remaining.size()) {
                    continue;
                }
                const auto bytes = remaining.first(parsed.frame_size);
                if (!is_validated_resync_boundary(bytes, parsed.prefix_size)) {
                    continue;
                }
                found = true;
                candidate_offset = offset;
                candidate = parsed;
                break;
            }

            if (!found) {
                return;
            }

            const auto& first = resync_provenance_at(candidate_offset);
            const auto& last = resync_provenance_at(candidate_offset + candidate.frame_size - 1u);
            const std::vector<uint8_t> frame(
                resync_bytes.begin() + static_cast<std::ptrdiff_t>(candidate_offset),
                resync_bytes.begin() + static_cast<std::ptrdiff_t>(candidate_offset + candidate.frame_size));
            std::vector<ProtocolMessage> decoded_messages;
            const auto error = decode_frame(
                frame,
                candidate.prefix_size,
                first,
                last,
                0,
                decoded_messages);
            if (error != FrameDiagnosticCode::none) {
                erase_resync_prefix(candidate_offset + 1u);
                continue;
            }

            outputs.insert(
                outputs.end(),
                std::make_move_iterator(decoded_messages.begin()),
                std::make_move_iterator(decoded_messages.end()));
            erase_resync_prefix(candidate_offset + candidate.frame_size);
            state = FrameState::need_length;
            clear_regular();

            auto remaining_bytes = std::move(resync_bytes);
            auto remaining_runs = std::move(resync_runs);
            resync_bytes.clear();
            resync_runs.clear();
            for (const auto& run : remaining_runs) {
                const auto segment = std::span<const uint8_t>(remaining_bytes).subspan(
                    run.begin,
                    run.end - run.begin);
                if (state == FrameState::need_resync) {
                    append_resync(segment, run.provenance);
                } else {
                    consume_normal(segment, run.provenance, outputs);
                }
            }
        }
    }

    std::vector<FrameOutput> process(const StreamChunk& chunk) {
        begin_epoch(chunk);
        std::vector<FrameOutput> outputs;
        if (state == FrameState::need_resync) {
            append_resync(chunk.bytes, chunk.provenance);
        } else {
            consume_normal(chunk.bytes, chunk.provenance, outputs);
        }
        if (state == FrameState::need_resync) {
            drain_resync(outputs);
        }
        return outputs;
    }
};

IncrementalFramer::IncrementalFramer(FrameConfig config)
    : impl_(std::make_unique<Impl>(config)) {}

IncrementalFramer::~IncrementalFramer() = default;
IncrementalFramer::IncrementalFramer(IncrementalFramer&&) noexcept = default;
IncrementalFramer& IncrementalFramer::operator=(IncrementalFramer&&) noexcept = default;

std::vector<FrameOutput> IncrementalFramer::process(const StreamChunk& chunk) {
    return impl_->process(chunk);
}

std::vector<FrameOutput> IncrementalFramer::process(const StreamReset&) {
    impl_->clear_all();
    return {};
}

FrameState IncrementalFramer::state() const noexcept {
    return impl_->state;
}

size_t IncrementalFramer::buffered_bytes() const noexcept {
    const size_t regular_bytes = impl_->state == FrameState::need_length
                                     ? impl_->length_size
                                     : impl_->frame_bytes.size();
    return regular_bytes + impl_->resync_bytes.size();
}

}  // namespace namter
