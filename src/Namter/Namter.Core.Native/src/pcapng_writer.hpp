#pragma once

#include <cstdint>
#include <cstdio>
#include <span>
#include <string>
#include <vector>

namespace namter {

// Minimal PCAPNG writer for operator-requested raw packet logs: one section
// header, one interface description block per observed link type, and one
// enhanced packet block per packet. Timestamps are written at the capture's
// native nanosecond resolution (if_tsresol = 9) so a reader does not have to
// guess microseconds. The writer is bounded: once the configured byte budget is
// spent it stops appending and reports itself truncated instead of growing
// without limit.
class PcapngWriter {
public:
    PcapngWriter() = default;
    ~PcapngWriter() { close(); }
    PcapngWriter(const PcapngWriter&) = delete;
    PcapngWriter& operator=(const PcapngWriter&) = delete;

    bool open(const std::string& path, uint64_t max_bytes) {
        close();
        if (max_bytes == 0u) return false;
#if defined(_WIN32)
        if (fopen_s(&file_, path.c_str(), "wb") != 0) file_ = nullptr;
#else
        file_ = std::fopen(path.c_str(), "wb");
#endif
        if (file_ == nullptr) return false;
        budget_ = max_bytes;
        written_ = 0;
        truncated_ = false;
        link_types_.clear();
        std::vector<uint8_t> block;
        append_u32(block, 0x0A0D0D0Au);
        append_u32(block, 28u);
        append_u32(block, 0x1A2B3C4Du);
        append_u16(block, 1u);
        append_u16(block, 0u);
        append_u64(block, 0xFFFFFFFFFFFFFFFFull); // unspecified section length
        append_u32(block, 28u);
        return emit(block);
    }

    [[nodiscard]] bool is_open() const noexcept { return file_ != nullptr; }
    [[nodiscard]] bool truncated() const noexcept { return truncated_; }
    [[nodiscard]] uint64_t written_bytes() const noexcept { return written_; }

    bool write(uint32_t link_type, uint64_t timestamp_ns, uint32_t original_length,
               std::span<const uint8_t> bytes) {
        if (file_ == nullptr || truncated_) return false;
        uint32_t interface_id = 0;
        if (!interface_for(link_type, interface_id)) return false;

        const size_t padded = (bytes.size() + 3u) & ~static_cast<size_t>(3u);
        const uint32_t total = static_cast<uint32_t>(32u + padded);
        std::vector<uint8_t> block;
        block.reserve(total);
        append_u32(block, 0x00000006u);
        append_u32(block, total);
        append_u32(block, interface_id);
        append_u32(block, static_cast<uint32_t>(timestamp_ns >> 32u));
        append_u32(block, static_cast<uint32_t>(timestamp_ns & 0xFFFFFFFFull));
        append_u32(block, static_cast<uint32_t>(bytes.size()));
        append_u32(block, original_length != 0u ? original_length : static_cast<uint32_t>(bytes.size()));
        block.insert(block.end(), bytes.begin(), bytes.end());
        block.resize(block.size() + (padded - bytes.size()), 0u);
        append_u32(block, total);
        return emit(block);
    }

    void close() {
        if (file_ != nullptr) {
            std::fclose(file_);
            file_ = nullptr;
        }
        link_types_.clear();
        written_ = 0;
        budget_ = 0;
        truncated_ = false;
    }

private:
    static void append_u16(std::vector<uint8_t>& out, uint16_t value) {
        out.push_back(static_cast<uint8_t>(value));
        out.push_back(static_cast<uint8_t>(value >> 8u));
    }
    static void append_u32(std::vector<uint8_t>& out, uint32_t value) {
        for (int shift = 0; shift < 32; shift += 8) out.push_back(static_cast<uint8_t>(value >> shift));
    }
    static void append_u64(std::vector<uint8_t>& out, uint64_t value) {
        for (int shift = 0; shift < 64; shift += 8) out.push_back(static_cast<uint8_t>(value >> shift));
    }

    bool interface_for(uint32_t link_type, uint32_t& interface_id) {
        for (size_t index = 0; index < link_types_.size(); ++index) {
            if (link_types_[index] == link_type) {
                interface_id = static_cast<uint32_t>(index);
                return true;
            }
        }
        std::vector<uint8_t> block;
        append_u32(block, 0x00000001u);
        append_u32(block, 32u);
        append_u16(block, static_cast<uint16_t>(link_type));
        append_u16(block, 0u);
        append_u32(block, 0u); // no snapshot limit
        append_u16(block, 9u); // if_tsresol
        append_u16(block, 1u);
        block.push_back(9u);   // 10^-9 seconds
        block.insert(block.end(), 3u, 0u);
        append_u16(block, 0u); // opt_endofopt
        append_u16(block, 0u);
        append_u32(block, 32u);
        if (!emit(block)) return false;
        link_types_.push_back(link_type);
        interface_id = static_cast<uint32_t>(link_types_.size() - 1u);
        return true;
    }

    bool emit(const std::vector<uint8_t>& block) {
        if (file_ == nullptr) return false;
        if (written_ + block.size() > budget_) {
            truncated_ = true;
            return false;
        }
        if (std::fwrite(block.data(), 1u, block.size(), file_) != block.size()) {
            truncated_ = true;
            return false;
        }
        written_ += block.size();
        return true;
    }

    std::FILE* file_ = nullptr;
    std::vector<uint32_t> link_types_;
    uint64_t written_ = 0;
    uint64_t budget_ = 0;
    bool truncated_ = false;
};

} // namespace namter
