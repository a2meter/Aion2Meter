#include "capture_pipeline.hpp"

#include <algorithm>
#include <limits>
#include <optional>

namespace namter {

struct CapturePipeline::Impl {
    struct FramerEntry {
        FlowTuple flow;
        uint64_t epoch;
        IncrementalFramer framer;
    };

    Impl(CapturePipelineConfig config, std::span<const uint8_t> snapshot,
         EventSink event_sink, DiagnosticSink diagnostic_sink)
        : frame_config(config.frame), tracker(config.flow), events(std::move(event_sink)),
          diagnostics(std::move(diagnostic_sink)) {
        if (!snapshot.empty()) decoder.emplace(snapshot);
    }

    IncrementalFramer& framer(const FlowTuple& flow, uint64_t epoch) {
        const auto found = std::find_if(framers.begin(), framers.end(), [&](const auto& value) {
            return value.flow == flow && value.epoch == epoch;
        });
        if (found != framers.end()) return found->framer;
        framers.push_back({flow, epoch, IncrementalFramer(frame_config)});
        return framers.back().framer;
    }

    void frame_outputs(std::vector<FrameOutput> outputs) {
        for (auto& output : outputs) {
            if (auto* message = std::get_if<ProtocolMessage>(&output)) {
                if (!decoder) {
                    diagnostics(NM_DIAGNOSTIC_INCOMPLETE_STREAM,
                                "protocol message dropped because no snapshot is active");
                    continue;
                }
                for (auto& decoded : decoder->decode(*message)) {
                    if (auto* event = std::get_if<DecodedEvent>(&decoded)) {
                        const auto view = event->view();
                        events(view);
                    } else {
                        diagnostics(NM_DIAGNOSTIC_INCOMPLETE_STREAM,
                                    "protocol message decode failed");
                    }
                }
            } else {
                diagnostics(NM_DIAGNOSTIC_INCOMPLETE_STREAM, "protocol frame is invalid");
            }
        }
    }

    void stream_outputs(std::vector<StreamOutput> outputs) {
        for (auto& output : outputs) {
            if (auto* chunk = std::get_if<StreamChunk>(&output)) {
                frame_outputs(framer(chunk->flow, chunk->epoch).process(*chunk));
            } else if (auto* reset = std::get_if<StreamReset>(&output)) {
                const auto found = std::find_if(framers.begin(), framers.end(), [&](const auto& value) {
                    return value.flow == reset->flow && value.epoch == reset->epoch;
                });
                bool retained_partial_frame = false;
                if (found != framers.end()) {
                    retained_partial_frame = found->framer.buffered_bytes() != 0;
                    frame_outputs(found->framer.process(*reset));
                    framers.erase(found);
                }
                const bool lossy_reset = reset->reason == StreamResetReason::gap_expiry ||
                    reset->reason == StreamResetReason::buffer_limit ||
                    reset->reason == StreamResetReason::flow_limit ||
                    reset->reason == StreamResetReason::ambiguous_sequence;
                if (retained_partial_frame || lossy_reset)
                    diagnostics(NM_DIAGNOSTIC_INCOMPLETE_STREAM, "capture stream reset");
            } else {
                diagnostics(NM_DIAGNOSTIC_INCOMPLETE_STREAM, "capture stream gap observed");
            }
        }
    }

    CaptureError ingest(const CaptureRecord& record) {
        const auto normalized = PacketNormalizer::normalize(record);
        if (!normalized.segment) return normalized.error;
        stream_outputs(tracker.process(*normalized.segment));
        return CaptureError::none;
    }

    void flush(uint64_t timestamp_ns) {
        stream_outputs(tracker.expire(timestamp_ns));
    }

    FrameConfig frame_config;
    FlowTracker tracker;
    std::optional<ProtocolDecoder> decoder;
    std::vector<FramerEntry> framers;
    EventSink events;
    DiagnosticSink diagnostics;
};

CapturePipeline::CapturePipeline(CapturePipelineConfig config,
                                 std::span<const uint8_t> snapshot, EventSink events,
                                 DiagnosticSink diagnostics)
    : impl_(std::make_unique<Impl>(config, snapshot, std::move(events),
                                  std::move(diagnostics))) {}
CapturePipeline::~CapturePipeline() = default;
CaptureError CapturePipeline::ingest(const CaptureRecord& record) { return impl_->ingest(record); }
void CapturePipeline::flush(uint64_t timestamp_ns) { impl_->flush(timestamp_ns); }
size_t CapturePipeline::active_framer_count() const noexcept { return impl_->framers.size(); }
const FlowDiagnostics& CapturePipeline::flow_diagnostics() const noexcept { return impl_->tracker.diagnostics(); }

} // namespace namter
