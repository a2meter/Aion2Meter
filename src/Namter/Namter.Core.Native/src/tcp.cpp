#include "capture_record.hpp"

#include <algorithm>
#include <cstdint>
#include <iterator>
#include <map>
#include <optional>
#include <utility>

#include "sequence.hpp"

namespace namter {
namespace {

[[nodiscard]] bool elapsed(
    uint64_t capture_time_ns,
    uint64_t since_ns,
    uint64_t timeout_ns) noexcept {
    return capture_time_ns >= since_ns && capture_time_ns - since_ns >= timeout_ns;
}

struct Interval {
    int64_t start = 0;
    std::vector<uint8_t> bytes;
    CaptureProvenance provenance;

    [[nodiscard]] int64_t end() const noexcept {
        return start + static_cast<int64_t>(bytes.size());
    }
};

void append_outputs(std::vector<StreamOutput>& destination, std::vector<StreamOutput> source) {
    destination.insert(
        destination.end(),
        std::make_move_iterator(source.begin()),
        std::make_move_iterator(source.end()));
}

}  // namespace

struct TcpReassembler::Impl {
    FlowTuple flow;
    uint64_t epoch = 0;
    size_t maximum_out_of_order_bytes = 0;
    uint64_t gap_timeout_ns = 0;
    FlowDiagnostics local_diagnostics;
    FlowDiagnostics* diagnostics = nullptr;
    uint64_t* next_epoch_id = nullptr;
    std::optional<int64_t> next_sequence;
    std::optional<int64_t> fin_sequence;
    std::optional<uint64_t> gap_since_ns;
    std::map<int64_t, Interval> intervals;
    size_t buffered_byte_count = 0;
    bool is_closed = false;

    Impl(
        FlowTuple flow_value,
        uint64_t initial_epoch,
        size_t maximum_bytes,
        uint64_t gap_timeout,
        FlowDiagnostics* shared_diagnostics,
        uint64_t* epoch_id_source)
        : flow(flow_value),
          epoch(epoch_id_source == nullptr ? initial_epoch : (*epoch_id_source)++),
          maximum_out_of_order_bytes(maximum_bytes),
          gap_timeout_ns(gap_timeout),
          diagnostics(shared_diagnostics == nullptr ? &local_diagnostics : shared_diagnostics),
          next_epoch_id(epoch_id_source) {
        ++diagnostics->epochs_started;
    }

    [[nodiscard]] std::vector<StreamOutput> reset(
        StreamResetReason reason,
        uint64_t capture_time_ns,
        bool begin_another_epoch,
        bool count_discarded = true) {
        std::vector<StreamOutput> outputs;
        outputs.emplace_back(StreamReset{
            .flow = flow,
            .epoch = epoch,
            .reason = reason,
            .timestamp_ns = capture_time_ns,
        });
        ++diagnostics->resets;
        if (count_discarded) {
            diagnostics->discarded_ranges += intervals.size();
        }
        intervals.clear();
        buffered_byte_count = 0;
        next_sequence.reset();
        fin_sequence.reset();
        gap_since_ns.reset();
        if (begin_another_epoch) {
            epoch = next_epoch_id == nullptr ? epoch + 1 : (*next_epoch_id)++;
            ++diagnostics->epochs_started;
        } else {
            is_closed = true;
        }
        return outputs;
    }

    void update_gap_clock(uint64_t capture_time_ns) {
        const bool has_gap = next_sequence.has_value() &&
            ((!intervals.empty() && intervals.begin()->first > *next_sequence) ||
             (intervals.empty() && fin_sequence.has_value() && *fin_sequence > *next_sequence));
        if (has_gap) {
            if (!gap_since_ns.has_value()) {
                gap_since_ns = capture_time_ns;
            }
        } else {
            gap_since_ns.reset();
        }
    }

    void flush(std::vector<StreamOutput>& outputs) {
        while (next_sequence.has_value() && !intervals.empty()) {
            auto interval = intervals.begin();
            if (interval->first != *next_sequence) {
                break;
            }
            const int64_t start = interval->second.start;
            const size_t byte_count = interval->second.bytes.size();
            outputs.emplace_back(StreamChunk{
                .flow = flow,
                .epoch = epoch,
                .sequence = static_cast<uint32_t>(start),
                .bytes = std::move(interval->second.bytes),
                .provenance = interval->second.provenance,
            });
            *next_sequence = start + static_cast<int64_t>(byte_count);
            buffered_byte_count -= byte_count;
            intervals.erase(interval);
        }
    }

    [[nodiscard]] std::vector<StreamOutput> complete_fin_if_ready(uint64_t capture_time_ns) {
        if (!next_sequence.has_value() || !fin_sequence.has_value()) {
            return {};
        }
        if (*fin_sequence < *next_sequence) {
            fin_sequence.reset();
            return {};
        }
        if (*fin_sequence != *next_sequence) {
            return {};
        }
        ++*next_sequence;
        return reset(StreamResetReason::fin, capture_time_ns, false);
    }

    [[nodiscard]] std::vector<StreamOutput> gap_reset_at(
        int64_t next_observed,
        StreamResetReason reason,
        uint64_t capture_time_ns,
        bool preserve_intervals) {
        std::vector<StreamOutput> outputs;
        outputs.emplace_back(GapObserved{
            .flow = flow,
            .epoch = epoch,
            .expected_sequence = static_cast<uint32_t>(*next_sequence),
            .next_sequence = static_cast<uint32_t>(next_observed),
            .timestamp_ns = capture_time_ns,
        });
        ++diagnostics->unresolved_byte_gaps;

        std::map<int64_t, Interval> saved_intervals;
        size_t saved_byte_count = 0;
        std::optional<int64_t> saved_fin;
        if (preserve_intervals) {
            saved_intervals = std::move(intervals);
            saved_byte_count = buffered_byte_count;
            saved_fin = fin_sequence;
        }
        append_outputs(outputs, reset(reason, capture_time_ns, true, !preserve_intervals));
        next_sequence = next_observed;
        if (preserve_intervals) {
            intervals = std::move(saved_intervals);
            buffered_byte_count = saved_byte_count;
            fin_sequence = saved_fin;
            flush(outputs);
            append_outputs(outputs, complete_fin_if_ready(capture_time_ns));
        }
        return outputs;
    }

    [[nodiscard]] std::vector<StreamOutput> insert_payload(
        const TcpSegment& segment,
        int64_t start,
        uint64_t capture_time_ns,
        bool enforce_limit = true) {
        std::vector<StreamOutput> outputs;
        const int64_t end = start + static_cast<int64_t>(segment.payload.size());

        if (enforce_limit && next_sequence.has_value() && start > *next_sequence) {
            const uint64_t gap_size = static_cast<uint64_t>(start - *next_sequence);
            if (gap_size > maximum_out_of_order_bytes ||
                buffered_byte_count + segment.payload.size() > maximum_out_of_order_bytes) {
                append_outputs(
                    outputs,
                    gap_reset_at(start, StreamResetReason::buffer_limit, capture_time_ns, false));
                append_outputs(outputs, insert_payload(segment, start, capture_time_ns, false));
                return outputs;
            }
        }

        int64_t cursor = start;
        size_t duplicate_bytes = 0;
        if (next_sequence.has_value() && cursor < *next_sequence) {
            const int64_t prefix_end = std::min(end, *next_sequence);
            duplicate_bytes += static_cast<size_t>(prefix_end - cursor);
            cursor = prefix_end;
        }

        struct Fragment {
            int64_t start = 0;
            int64_t end = 0;
        };
        std::vector<Fragment> fragments;
        auto interval = intervals.lower_bound(cursor);
        if (interval != intervals.begin()) {
            --interval;
        }
        while (cursor < end) {
            while (interval != intervals.end() && interval->second.end() <= cursor) {
                ++interval;
            }
            if (interval == intervals.end() || interval->first >= end) {
                fragments.push_back({cursor, end});
                break;
            }
            if (interval->first > cursor) {
                fragments.push_back({cursor, std::min(end, interval->first)});
                cursor = std::min(end, interval->first);
                continue;
            }
            const int64_t overlap_end = std::min(end, interval->second.end());
            duplicate_bytes += static_cast<size_t>(overlap_end - cursor);
            cursor = overlap_end;
            ++interval;
        }

        if (duplicate_bytes != 0) {
            ++diagnostics->overlaps;
            diagnostics->duplicate_bytes_removed += duplicate_bytes;
        }
        for (const Fragment& fragment : fragments) {
            const size_t offset = static_cast<size_t>(fragment.start - start);
            const size_t length = static_cast<size_t>(fragment.end - fragment.start);
            std::vector<uint8_t> bytes(
                segment.payload.begin() + static_cast<std::ptrdiff_t>(offset),
                segment.payload.begin() + static_cast<std::ptrdiff_t>(offset + length));
            intervals.emplace(
                fragment.start,
                Interval{
                    .start = fragment.start,
                    .bytes = std::move(bytes),
                    .provenance = segment.provenance,
                });
            buffered_byte_count += length;
            diagnostics->accepted_bytes += length;
        }
        flush(outputs);
        return outputs;
    }
};

TcpReassembler::TcpReassembler(
    FlowTuple flow,
    uint64_t initial_epoch,
    size_t maximum_out_of_order_bytes,
    uint64_t gap_timeout_ns,
    FlowDiagnostics* shared_diagnostics)
    : TcpReassembler(
          flow,
          initial_epoch,
          maximum_out_of_order_bytes,
          gap_timeout_ns,
          shared_diagnostics,
          nullptr) {}

TcpReassembler::TcpReassembler(
    FlowTuple flow,
    uint64_t initial_epoch,
    size_t maximum_out_of_order_bytes,
    uint64_t gap_timeout_ns,
    FlowDiagnostics* shared_diagnostics,
    uint64_t* next_epoch_id)
    : impl_(std::make_unique<Impl>(
          flow,
          initial_epoch,
          maximum_out_of_order_bytes,
          gap_timeout_ns,
          shared_diagnostics,
          next_epoch_id)) {}

TcpReassembler::~TcpReassembler() = default;
TcpReassembler::TcpReassembler(TcpReassembler&&) noexcept = default;
TcpReassembler& TcpReassembler::operator=(TcpReassembler&&) noexcept = default;

std::vector<StreamOutput> TcpReassembler::process(const TcpSegment& segment) {
    std::vector<StreamOutput> outputs;
    if (impl_->is_closed) {
        return outputs;
    }

    const bool has_syn = (segment.flags & tcp_syn) != 0;
    const bool has_fin = (segment.flags & tcp_fin) != 0;
    const bool has_rst = (segment.flags & tcp_rst) != 0;
    const uint32_t payload_sequence = segment.sequence + (has_syn ? 1u : 0u);

    if (!impl_->next_sequence.has_value() && (has_syn || !segment.payload.empty() || has_fin)) {
        impl_->next_sequence = static_cast<int64_t>(payload_sequence);
    }

    if (!segment.payload.empty()) {
        const int64_t start = unwrap_sequence(payload_sequence, *impl_->next_sequence);
        append_outputs(outputs, impl_->insert_payload(segment, start, segment.provenance.timestamp_ns));
    }

    if (has_fin && !impl_->is_closed) {
        const int64_t checkpoint = impl_->next_sequence.value_or(static_cast<int64_t>(payload_sequence));
        const int64_t start = unwrap_sequence(payload_sequence, checkpoint);
        impl_->fin_sequence = start + static_cast<int64_t>(segment.payload.size());
        append_outputs(outputs, impl_->complete_fin_if_ready(segment.provenance.timestamp_ns));
    }

    if (has_rst && !impl_->is_closed) {
        append_outputs(outputs, impl_->reset(
            StreamResetReason::rst,
            segment.provenance.timestamp_ns,
            false));
    }

    if (!impl_->is_closed) {
        impl_->update_gap_clock(segment.provenance.timestamp_ns);
    }
    return outputs;
}

std::vector<StreamOutput> TcpReassembler::expire(uint64_t capture_time_ns) {
    if (impl_->is_closed || !impl_->gap_since_ns.has_value() ||
        !elapsed(capture_time_ns, *impl_->gap_since_ns, impl_->gap_timeout_ns)) {
        return {};
    }
    const int64_t next_observed = !impl_->intervals.empty()
        ? impl_->intervals.begin()->first
        : *impl_->fin_sequence;
    auto outputs = impl_->gap_reset_at(
        next_observed,
        StreamResetReason::gap_expiry,
        capture_time_ns,
        true);
    if (!impl_->is_closed) {
        impl_->update_gap_clock(capture_time_ns);
    }
    return outputs;
}

std::vector<StreamOutput> TcpReassembler::start_new_epoch(
    StreamResetReason reason,
    uint64_t capture_time_ns) {
    if (impl_->is_closed) {
        return {};
    }
    return impl_->reset(reason, capture_time_ns, true);
}

std::vector<StreamOutput> TcpReassembler::close(
    StreamResetReason reason,
    uint64_t capture_time_ns) {
    if (impl_->is_closed) {
        return {};
    }
    return impl_->reset(reason, capture_time_ns, false);
}

size_t TcpReassembler::buffered_bytes() const noexcept {
    return impl_->buffered_byte_count;
}

bool TcpReassembler::closed() const noexcept {
    return impl_->is_closed;
}

uint64_t TcpReassembler::epoch() const noexcept {
    return impl_->epoch;
}

const FlowDiagnostics& TcpReassembler::diagnostics() const noexcept {
    return *impl_->diagnostics;
}

}  // namespace namter
