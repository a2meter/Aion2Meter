#pragma once

#include "capture_record.hpp"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <variant>
#include <vector>

namespace namter {

struct FrameConfig {
    size_t max_frame_bytes = 2u * 1024u * 1024u;
    size_t max_decompressed_bytes = 4u * 1024u * 1024u;
};

enum class FrameState : uint8_t {
    need_length,
    need_body,
    need_resync,
};

enum class FrameDiagnosticCode : uint8_t {
    none,
    overlong_varint,
    invalid_frame_length,
    frame_too_large,
    invalid_marker,
    truncated_lz4_header,
    invalid_decompressed_size,
    decompressed_size_too_large,
    lz4_decompression_failed,
    invalid_nested_frame,
    resource_limit_exceeded,
};

inline constexpr size_t frame_max_batch_depth = 4u;
inline constexpr size_t frame_max_nested_frames = 4096u;
inline constexpr size_t frame_max_emitted_messages = 4096u;
inline constexpr size_t frame_max_allocation_nodes = 4096u;
inline constexpr size_t frame_max_provenance_runs = 4096u;
inline constexpr size_t frame_provenance_run_accounted_bytes =
    sizeof(CaptureProvenance) + (2u * sizeof(size_t));

struct FrameMetrics {
    uint64_t resync_scan_steps = 0;
    uint64_t resync_incomplete_revisits = 0;
    size_t retained_provenance_runs = 0;
    size_t retained_provenance_metadata_bytes = 0;
};

struct ProtocolMessage {
    FlowTuple flow;
    uint64_t epoch = 0;
    std::vector<uint8_t> bytes;
    CaptureProvenance first_provenance;
    CaptureProvenance last_provenance;
    uint64_t first_timestamp_ns = 0;
    uint64_t last_timestamp_ns = 0;
};

struct FrameDiagnostic {
    FrameDiagnosticCode code = FrameDiagnosticCode::none;
    FlowTuple flow;
    uint64_t epoch = 0;
    CaptureProvenance first_provenance;
    CaptureProvenance last_provenance;
    uint64_t first_timestamp_ns = 0;
    uint64_t last_timestamp_ns = 0;
};

using FrameOutput = std::variant<ProtocolMessage, FrameDiagnostic>;

class IncrementalFramer {
public:
    explicit IncrementalFramer(FrameConfig config);
    ~IncrementalFramer();

    IncrementalFramer(const IncrementalFramer&) = delete;
    IncrementalFramer& operator=(const IncrementalFramer&) = delete;
    IncrementalFramer(IncrementalFramer&&) noexcept;
    IncrementalFramer& operator=(IncrementalFramer&&) noexcept;

    [[nodiscard]] std::vector<FrameOutput> process(const StreamChunk& chunk);
    [[nodiscard]] std::vector<FrameOutput> process(const StreamReset& reset);
    [[nodiscard]] FrameState state() const noexcept;
    [[nodiscard]] size_t buffered_bytes() const noexcept;
    [[nodiscard]] FrameMetrics metrics() const noexcept;

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

}  // namespace namter
