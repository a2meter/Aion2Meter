#include "capture_pipeline.hpp"
#include "pcapng_writer.hpp"

#include <algorithm>
#include <deque>
#include <limits>
#include <optional>
#include <string>

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
                        if (view.kind == NM_EVENT_CONTENT && view.dungeon_id != 0u &&
                            view.dungeon_id != logged_dungeon) {
                            pending_dungeon = view.dungeon_id;
                        }
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

    // Keeps the most recent packets so a log opened on dungeon entry still
    // contains the entry itself and whatever preceded it inside the budget.
    void remember(const CaptureRecord& record) {
        if (log_directory.empty()) return;
        ring.push_back({record.link_type, record.timestamp_ns, record.original_length, record.bytes});
        ring_bytes += record.bytes.size();
        while (ring_bytes > ring_budget && !ring.empty()) {
            ring_bytes -= ring.front().bytes.size();
            ring.pop_front();
        }
    }

    void rotate_log(uint32_t dungeon_id, uint64_t timestamp_ns) {
        writer.close();
        logged_dungeon = dungeon_id;
        std::string path = log_directory;
        if (!path.empty() && path.back() != '/' && path.back() != '\\') path.push_back('\\');
        path += "dungeon-" + std::to_string(dungeon_id) + "-" + std::to_string(timestamp_ns) + ".pcapng";
        if (!writer.open(path, max_log_bytes)) return;
        for (const auto& entry : ring)
            if (!writer.write(entry.link_type, entry.timestamp_ns, entry.original_length, entry.bytes)) break;
    }

    CaptureError ingest(const CaptureRecord& record) {
        const auto normalized = PacketNormalizer::normalize(record);
        if (!normalized.segment) return normalized.error;
        remember(record);
        stream_outputs(tracker.process(*normalized.segment));
        if (log_directory.empty()) return CaptureError::none;
        if (pending_dungeon != 0u) {
            const uint32_t dungeon = pending_dungeon;
            pending_dungeon = 0u;
            rotate_log(dungeon, record.timestamp_ns);
        } else if (writer.is_open()) {
            (void)writer.write(record.link_type, record.timestamp_ns, record.original_length, record.bytes);
        }
        return CaptureError::none;
    }

    void flush(uint64_t timestamp_ns) {
        stream_outputs(tracker.expire(timestamp_ns));
    }

    struct RingEntry {
        uint32_t link_type;
        uint64_t timestamp_ns;
        uint32_t original_length;
        std::vector<uint8_t> bytes;
    };

    static constexpr size_t ring_budget = 4u * 1024u * 1024u;
    static constexpr uint64_t max_log_bytes = 512ull * 1024ull * 1024ull;

    FrameConfig frame_config;
    FlowTracker tracker;
    std::optional<ProtocolDecoder> decoder;
    std::vector<FramerEntry> framers;
    EventSink events;
    DiagnosticSink diagnostics;
    std::string log_directory;
    PcapngWriter writer;
    std::deque<RingEntry> ring;
    size_t ring_bytes = 0;
    uint32_t logged_dungeon = 0;
    uint32_t pending_dungeon = 0;
};

CapturePipeline::CapturePipeline(CapturePipelineConfig config,
                                 std::span<const uint8_t> snapshot, EventSink events,
                                 DiagnosticSink diagnostics)
    : impl_(std::make_unique<Impl>(config, snapshot, std::move(events),
                                  std::move(diagnostics))) {}
CapturePipeline::~CapturePipeline() = default;
void CapturePipeline::set_packet_log(std::string directory) { impl_->log_directory = std::move(directory); }
CaptureError CapturePipeline::ingest(const CaptureRecord& record) { return impl_->ingest(record); }
void CapturePipeline::flush(uint64_t timestamp_ns) { impl_->flush(timestamp_ns); }
size_t CapturePipeline::active_framer_count() const noexcept { return impl_->framers.size(); }
const FlowDiagnostics& CapturePipeline::flow_diagnostics() const noexcept { return impl_->tracker.diagnostics(); }

} // namespace namter
