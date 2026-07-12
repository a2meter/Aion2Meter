#pragma once

#include "event.hpp"

#include <functional>
#include <memory>
#include <span>

namespace namter {

struct CapturePipelineConfig {
    FlowConfig flow;
    FrameConfig frame;
};

class CapturePipeline {
public:
    using EventSink = std::function<void(const nm_event_v1&)>;
    using DiagnosticSink = std::function<void(uint32_t, const char*)>;

    CapturePipeline(CapturePipelineConfig config, std::span<const uint8_t> snapshot,
                    EventSink events, DiagnosticSink diagnostics);
    ~CapturePipeline();
    CapturePipeline(const CapturePipeline&) = delete;
    CapturePipeline& operator=(const CapturePipeline&) = delete;

    [[nodiscard]] CaptureError ingest(const CaptureRecord& record);
    void flush(uint64_t timestamp_ns);
    [[nodiscard]] size_t active_framer_count() const noexcept;
    [[nodiscard]] const FlowDiagnostics& flow_diagnostics() const noexcept;

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace namter
