#include "capture_record.hpp"

#include <iterator>
#include <map>
#include <stdexcept>
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

void append_outputs(std::vector<StreamOutput>& destination, std::vector<StreamOutput> source) {
    destination.insert(
        destination.end(),
        std::make_move_iterator(source.begin()),
        std::make_move_iterator(source.end()));
}

[[nodiscard]] FlowConfig require_valid_flow_config(FlowConfig config) {
    if (!valid_sequence_window(config.max_out_of_order_bytes_per_flow)) {
        throw std::invalid_argument("flow sequence window must be between zero and half-space");
    }
    return config;
}

}  // namespace

struct FlowTracker::Impl {
    struct Entry {
        TcpReassembler reassembler;
        uint64_t last_capture_time_ns = 0;
    };

    explicit Impl(FlowConfig config_value) : config(config_value) {}

    FlowConfig config;
    FlowDiagnostics diagnostics;
    uint64_t next_epoch_id = 1;
    std::map<FlowTuple, Entry> flows;
};

FlowTracker::FlowTracker(FlowConfig config)
    : impl_(std::make_unique<Impl>(require_valid_flow_config(config))) {}
FlowTracker::~FlowTracker() = default;
FlowTracker::FlowTracker(FlowTracker&&) noexcept = default;
FlowTracker& FlowTracker::operator=(FlowTracker&&) noexcept = default;

std::vector<StreamOutput> FlowTracker::process(const TcpSegment& segment) {
    std::vector<StreamOutput> outputs = expire(segment.provenance.timestamp_ns);
    auto found = impl_->flows.find(segment.flow);
    if (found == impl_->flows.end()) {
        if (impl_->flows.size() >= impl_->config.max_live_flows) {
            outputs.emplace_back(StreamReset{
                .flow = segment.flow,
                .epoch = 0,
                .reason = StreamResetReason::flow_limit,
                .timestamp_ns = segment.provenance.timestamp_ns,
            });
            ++impl_->diagnostics.resets;
            ++impl_->diagnostics.discarded_ranges;
            return outputs;
        }
        auto [inserted, unused] = impl_->flows.emplace(
            std::piecewise_construct,
            std::forward_as_tuple(segment.flow),
            std::forward_as_tuple(Impl::Entry{
                .reassembler = TcpReassembler(
                    segment.flow,
                    1,
                    impl_->config.max_out_of_order_bytes_per_flow,
                    impl_->config.gap_timeout_ns,
                    &impl_->diagnostics,
                    &impl_->next_epoch_id),
                .last_capture_time_ns = segment.provenance.timestamp_ns,
            }));
        (void)unused;
        found = inserted;
        ++impl_->diagnostics.flows_started;
    } else if ((segment.flags & tcp_syn) != 0 &&
               !found->second.reassembler.is_syn_retransmission(segment.sequence)) {
        append_outputs(
            outputs,
            found->second.reassembler.start_new_epoch(
                StreamResetReason::tuple_reuse,
                segment.provenance.timestamp_ns));
    }

    found->second.last_capture_time_ns = segment.provenance.timestamp_ns;
    append_outputs(outputs, found->second.reassembler.process(segment));
    if (found->second.reassembler.closed()) {
        impl_->flows.erase(found);
    }
    return outputs;
}

std::vector<StreamOutput> FlowTracker::expire(uint64_t capture_time_ns) {
    std::vector<StreamOutput> outputs;
    for (auto flow = impl_->flows.begin(); flow != impl_->flows.end();) {
        append_outputs(outputs, flow->second.reassembler.expire(capture_time_ns));
        if (elapsed(
                capture_time_ns,
                flow->second.last_capture_time_ns,
                impl_->config.idle_timeout_ns)) {
            append_outputs(
                outputs,
                flow->second.reassembler.close(
                    StreamResetReason::idle_expiry,
                    capture_time_ns));
            flow = impl_->flows.erase(flow);
        } else {
            ++flow;
        }
    }
    return outputs;
}

size_t FlowTracker::live_flow_count() const noexcept {
    return impl_->flows.size();
}

size_t FlowTracker::buffered_bytes(const FlowTuple& flow) const noexcept {
    const auto found = impl_->flows.find(flow);
    return found == impl_->flows.end() ? 0u : found->second.reassembler.buffered_bytes();
}

const FlowDiagnostics& FlowTracker::diagnostics() const noexcept {
    return impl_->diagnostics;
}

}  // namespace namter
