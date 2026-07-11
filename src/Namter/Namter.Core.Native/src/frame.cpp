#include "frame_internal.hpp"
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
constexpr std::array<uint8_t, 3> protocol_boundary_marker{0x06, 0x00, 0x36};

bool is_optional_marker(uint8_t value) noexcept {
    return value >= 0xf0u && value <= 0xfeu;
}

bool same_provenance(
    const CaptureProvenance& left,
    const CaptureProvenance& right) noexcept {
    return left.source == right.source &&
           left.timestamp_ns == right.timestamp_ns &&
           left.link_type == right.link_type &&
           left.captured_length == right.captured_length &&
           left.original_length == right.original_length &&
           left.file_offset == right.file_offset;
}

struct ParsedFrame {
    size_t prefix_size = 0;
    size_t frame_size = 0;
};

enum class ResyncCandidateStatus : uint8_t {
    invalid,
    incomplete,
    complete,
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
    std::array<CaptureProvenance, 5> length_provenance{};
    size_t length_size = 0;
    size_t body_remaining = 0;
    std::vector<uint8_t> frame_bytes;
    std::vector<ProvenanceRun> frame_runs;
    CaptureProvenance first_provenance;
    CaptureProvenance last_provenance;
    std::vector<uint8_t> resync_bytes;
    std::vector<ProvenanceRun> resync_runs;
    size_t resync_head = 0;
    size_t resync_scan_cursor = 0;
    bool resync_waiting = false;
    uint64_t resync_scan_steps = 0;
    uint64_t resync_incomplete_revisits = 0;

    void clear_regular() noexcept {
        length_size = 0;
        body_remaining = 0;
        frame_bytes.clear();
        frame_runs.clear();
        first_provenance = {};
        last_provenance = {};
    }

    void clear_all() noexcept {
        state = FrameState::need_length;
        clear_regular();
        resync_bytes.clear();
        resync_runs.clear();
        resync_head = 0;
        resync_scan_cursor = 0;
        resync_waiting = false;
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
        detail::ExpansionBudget& budget,
        std::vector<ProtocolMessage>& output) const {
        if (prefix_size > frame.size()) {
            return FrameDiagnosticCode::invalid_nested_frame;
        }
        const auto body = frame.subspan(prefix_size);
        const size_t offset = payload_offset(body);
        if (offset == body.size()) {
            if (budget.remaining_emitted_messages == 0 ||
                budget.remaining_allocation_nodes == 0) {
                return FrameDiagnosticCode::resource_limit_exceeded;
            }
            --budget.remaining_emitted_messages;
            --budget.remaining_allocation_nodes;
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
            if (budget.remaining_emitted_messages == 0 ||
                budget.remaining_allocation_nodes == 0) {
                return FrameDiagnosticCode::resource_limit_exceeded;
            }
            --budget.remaining_emitted_messages;
            --budget.remaining_allocation_nodes;
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
        if (budget.depth >= frame_max_batch_depth) {
            return FrameDiagnosticCode::invalid_nested_frame;
        }

        std::vector<uint8_t> decompressed;
        const auto expansion_error = detail::decompress_lz4_batch(
            body.subspan(offset),
            config.max_decompressed_bytes,
            budget,
            decompressed);
        if (expansion_error != FrameDiagnosticCode::none) {
            return expansion_error;
        }

        std::vector<ProtocolMessage> nested_output;
        size_t nested_offset = 0;
        while (nested_offset < decompressed.size()) {
            const auto remaining = std::span<const uint8_t>(decompressed).subspan(nested_offset);
            ParsedFrame parsed;
            if (!frame_size_from_prefix(remaining, config.max_frame_bytes, parsed) ||
                parsed.frame_size > remaining.size() ||
                parsed.frame_size == parsed.prefix_size) {
                return FrameDiagnosticCode::invalid_nested_frame;
            }
            if (budget.remaining_nested_frames == 0) {
                return FrameDiagnosticCode::resource_limit_exceeded;
            }
            --budget.remaining_nested_frames;
            const auto nested = remaining.first(parsed.frame_size);
            ++budget.depth;
            const auto nested_error = decode_frame(
                nested,
                parsed.prefix_size,
                first,
                last,
                budget,
                nested_output);
            --budget.depth;
            if (nested_error != FrameDiagnosticCode::none) {
                return nested_error;
            }
            nested_offset += parsed.frame_size;
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
            resync_head = 0;
            resync_scan_cursor = 0;
            resync_waiting = false;
            resync_runs.push_back(ProvenanceRun{
                .begin = 0,
                .end = retention_limit,
                .provenance = provenance,
            });
            return;
        }
        const size_t retained_bytes = resync_bytes.size() - resync_head;
        if (retained_bytes > retention_limit - bytes.size()) {
            erase_resync_prefix(
                retained_bytes - (retention_limit - bytes.size()));
        }
        const size_t begin = resync_bytes.size();
        resync_bytes.insert(resync_bytes.end(), bytes.begin(), bytes.end());
        if (!resync_runs.empty() && resync_runs.back().end == begin &&
            same_provenance(resync_runs.back().provenance, provenance)) {
            resync_runs.back().end = resync_bytes.size();
            return;
        }
        resync_runs.push_back(ProvenanceRun{
            .begin = begin,
            .end = resync_bytes.size(),
            .provenance = provenance,
        });
        while (resync_runs.size() > frame_max_provenance_runs) {
            erase_resync_prefix(resync_runs.front().end - resync_head);
        }
    }

    void append_frame_run(
        size_t begin,
        size_t end,
        const CaptureProvenance& provenance) {
        if (begin == end) {
            return;
        }
        if (!frame_runs.empty() && frame_runs.back().end == begin &&
            same_provenance(frame_runs.back().provenance, provenance)) {
            frame_runs.back().end = end;
            return;
        }
        frame_runs.push_back(ProvenanceRun{
            .begin = begin,
            .end = end,
            .provenance = provenance,
        });
    }

    void append_failed_frame_tail_to_resync(size_t start) {
        for (const auto& run : frame_runs) {
            const size_t begin = std::max(run.begin, start);
            if (begin >= run.end) {
                continue;
            }
            append_resync(
                std::span<const uint8_t>(frame_bytes).subspan(begin, run.end - begin),
                run.provenance);
        }
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
        const size_t retained_byte_count = resync_bytes.size() - resync_head;
        resync_head += std::min(count, retained_byte_count);
        resync_scan_cursor = std::max(resync_scan_cursor, resync_head);
        resync_waiting = false;
        std::vector<ProvenanceRun> retained;
        for (const auto& run : resync_runs) {
            if (run.end <= resync_head) {
                continue;
            }
            retained.push_back(ProvenanceRun{
                .begin = std::max(run.begin, resync_head),
                .end = run.end,
                .provenance = run.provenance,
            });
        }
        resync_runs = std::move(retained);
        if (resync_head == resync_bytes.size()) {
            resync_bytes.clear();
            resync_runs.clear();
            resync_head = 0;
            resync_scan_cursor = 0;
            return;
        }
        const size_t retention_limit = config.max_frame_bytes + 5u;
        if (resync_head >= retention_limit && resync_head * 2u >= resync_bytes.size()) {
            resync_bytes.erase(
                resync_bytes.begin(),
                resync_bytes.begin() + static_cast<std::ptrdiff_t>(resync_head));
            for (auto& run : resync_runs) {
                run.begin -= resync_head;
                run.end -= resync_head;
            }
            resync_scan_cursor -= resync_head;
            resync_head = 0;
        }
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
                length_bytes[length_size] = bytes[offset++];
                length_provenance[length_size] = provenance;
                ++length_size;
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
                    const size_t failed_size = length_size;
                    const auto failed_bytes = length_bytes;
                    const auto failed_provenance = length_provenance;
                    enter_resync();
                    for (size_t index = 1; index < failed_size; ++index) {
                        append_resync(
                            std::span<const uint8_t>(failed_bytes.data() + index, 1u),
                            failed_provenance[index]);
                    }
                    append_resync(bytes.subspan(offset), provenance);
                    break;
                }
                if (decoded.value < frame_length_bias) {
                    outputs.emplace_back(diagnostic(
                        FrameDiagnosticCode::invalid_frame_length,
                        first_provenance,
                        last_provenance));
                    const size_t failed_size = length_size;
                    const auto failed_bytes = length_bytes;
                    const auto failed_provenance = length_provenance;
                    enter_resync();
                    for (size_t index = 1; index < failed_size; ++index) {
                        append_resync(
                            std::span<const uint8_t>(failed_bytes.data() + index, 1u),
                            failed_provenance[index]);
                    }
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
                    const size_t failed_size = length_size;
                    const auto failed_bytes = length_bytes;
                    const auto failed_provenance = length_provenance;
                    enter_resync();
                    for (size_t index = 1; index < failed_size; ++index) {
                        append_resync(
                            std::span<const uint8_t>(failed_bytes.data() + index, 1u),
                            failed_provenance[index]);
                    }
                    append_resync(bytes.subspan(offset), provenance);
                    break;
                }

                const size_t frame_size = decoded.bytes_consumed + body_size;
                frame_bytes.reserve(frame_size);
                frame_bytes.insert(
                    frame_bytes.end(),
                    length_bytes.begin(),
                    length_bytes.begin() + static_cast<std::ptrdiff_t>(length_size));
                for (size_t index = 0; index < length_size; ++index) {
                    append_frame_run(index, index + 1u, length_provenance[index]);
                }
                body_remaining = body_size;
                state = FrameState::need_body;
            }

            if (state == FrameState::need_body) {
                const size_t available = bytes.size() - offset;
                const size_t taken = std::min(body_remaining, available);
                const size_t frame_begin = frame_bytes.size();
                frame_bytes.insert(
                    frame_bytes.end(),
                    bytes.begin() + static_cast<std::ptrdiff_t>(offset),
                    bytes.begin() + static_cast<std::ptrdiff_t>(offset + taken));
                offset += taken;
                body_remaining -= taken;
                if (taken != 0) {
                    append_frame_run(frame_begin, frame_begin + taken, provenance);
                    last_provenance = provenance;
                }
                if (body_remaining != 0) {
                    continue;
                }

                std::vector<ProtocolMessage> decoded_messages;
                detail::ExpansionBudget budget{
                    .remaining_expanded_bytes = config.max_decompressed_bytes,
                };
                const auto error = decode_frame(
                    frame_bytes,
                    length_size,
                    first_provenance,
                    last_provenance,
                    budget,
                    decoded_messages);
                if (error != FrameDiagnosticCode::none) {
                    outputs.emplace_back(diagnostic(error, first_provenance, last_provenance));
                    if (error == FrameDiagnosticCode::invalid_marker) {
                        append_failed_frame_tail_to_resync(1u);
                    }
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
        size_t prefix_size,
        size_t candidate_offset) const {
        const auto body = candidate.subspan(prefix_size);
        const size_t offset = payload_offset(body);
        if (body.size() >= offset + 2u &&
            body[offset] == 0xffu && body[offset + 1u] == 0xffu) {
            std::vector<ProtocolMessage> ignored;
            detail::ExpansionBudget budget{
                .remaining_expanded_bytes = config.max_decompressed_bytes,
            };
            return decode_frame(
                       candidate,
                       prefix_size,
                       resync_provenance_at(candidate_offset),
                       resync_provenance_at(candidate_offset + candidate.size() - 1u),
                       budget,
                       ignored) == FrameDiagnosticCode::none;
        }
        return starts_with_protocol_marker(body);
    }

    ResyncCandidateStatus inspect_resync_candidate(
        size_t offset,
        ParsedFrame& parsed) const {
        const auto remaining = std::span<const uint8_t>(resync_bytes).subspan(offset);
        const auto decoded = decode_u32_varint(remaining);
        if (decoded.status == VarintStatus::incomplete) {
            return ResyncCandidateStatus::incomplete;
        }
        if (decoded.status == VarintStatus::invalid || decoded.value < frame_length_bias) {
            return ResyncCandidateStatus::invalid;
        }
        const size_t body_size = static_cast<size_t>(decoded.value - frame_length_bias);
        if (body_size > config.max_frame_bytes ||
            decoded.bytes_consumed > config.max_frame_bytes - body_size) {
            return ResyncCandidateStatus::invalid;
        }
        parsed = ParsedFrame{
            .prefix_size = decoded.bytes_consumed,
            .frame_size = decoded.bytes_consumed + body_size,
        };

        const size_t available_body = remaining.size() - decoded.bytes_consumed;
        const auto body = remaining.subspan(decoded.bytes_consumed, std::min(body_size, available_body));
        size_t marker_offset = 0;
        if (!body.empty() && is_optional_marker(body.front())) {
            marker_offset = 1u;
        }
        if (body.size() <= marker_offset) {
            return parsed.frame_size <= remaining.size()
                       ? ResyncCandidateStatus::invalid
                       : ResyncCandidateStatus::incomplete;
        }
        if (body[marker_offset] == 0xffu) {
            if (body.size() <= marker_offset + 1u) {
                return parsed.frame_size <= remaining.size()
                           ? ResyncCandidateStatus::invalid
                           : ResyncCandidateStatus::incomplete;
            }
            if (body[marker_offset + 1u] != 0xffu) {
                return ResyncCandidateStatus::invalid;
            }
        } else {
            const size_t comparable = std::min(
                protocol_boundary_marker.size(),
                body.size() - marker_offset);
            if (!std::equal(
                    protocol_boundary_marker.begin(),
                    protocol_boundary_marker.begin() + static_cast<std::ptrdiff_t>(comparable),
                    body.begin() + static_cast<std::ptrdiff_t>(marker_offset))) {
                return ResyncCandidateStatus::invalid;
            }
            if (comparable < protocol_boundary_marker.size()) {
                return parsed.frame_size <= remaining.size()
                           ? ResyncCandidateStatus::invalid
                           : ResyncCandidateStatus::incomplete;
            }
        }
        if (parsed.frame_size > remaining.size()) {
            return ResyncCandidateStatus::incomplete;
        }
        return is_validated_resync_boundary(
                   remaining.first(parsed.frame_size),
                   parsed.prefix_size,
                   offset)
                   ? ResyncCandidateStatus::complete
                   : ResyncCandidateStatus::invalid;
    }

    void drain_resync(std::vector<FrameOutput>& outputs) {
        while (state == FrameState::need_resync) {
            bool found = false;
            size_t candidate_offset = 0;
            ParsedFrame candidate;
            resync_scan_cursor = std::max(resync_scan_cursor, resync_head);
            while (resync_scan_cursor < resync_bytes.size()) {
                ++resync_scan_steps;
                ParsedFrame parsed;
                const auto status = inspect_resync_candidate(resync_scan_cursor, parsed);
                if (status == ResyncCandidateStatus::incomplete) {
                    if (resync_waiting) {
                        ++resync_incomplete_revisits;
                    }
                    resync_waiting = true;
                    return;
                }
                resync_waiting = false;
                if (status == ResyncCandidateStatus::complete) {
                    found = true;
                    candidate_offset = resync_scan_cursor;
                    candidate = parsed;
                    break;
                }
                ++resync_scan_cursor;
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
            detail::ExpansionBudget budget{
                .remaining_expanded_bytes = config.max_decompressed_bytes,
            };
            const auto error = decode_frame(
                frame,
                candidate.prefix_size,
                first,
                last,
                budget,
                decoded_messages);
            if (error != FrameDiagnosticCode::none) {
                erase_resync_prefix((candidate_offset - resync_head) + 1u);
                continue;
            }

            outputs.insert(
                outputs.end(),
                std::make_move_iterator(decoded_messages.begin()),
                std::make_move_iterator(decoded_messages.end()));
            erase_resync_prefix(
                (candidate_offset - resync_head) + candidate.frame_size);
            state = FrameState::need_length;
            clear_regular();

            auto remaining_bytes = std::move(resync_bytes);
            auto remaining_runs = std::move(resync_runs);
            resync_bytes.clear();
            resync_runs.clear();
            resync_head = 0;
            resync_scan_cursor = 0;
            resync_waiting = false;
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
    return regular_bytes + (impl_->resync_bytes.size() - impl_->resync_head);
}

FrameMetrics IncrementalFramer::metrics() const noexcept {
    return FrameMetrics{
        .resync_scan_steps = impl_->resync_scan_steps,
        .resync_incomplete_revisits = impl_->resync_incomplete_revisits,
        .retained_provenance_runs = impl_->resync_runs.size(),
        .retained_provenance_metadata_bytes =
            impl_->resync_runs.size() * frame_provenance_run_accounted_bytes,
    };
}

}  // namespace namter
