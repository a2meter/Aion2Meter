#include <gtest/gtest.h>

#include "fuzz_support.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace {

uint64_t next(uint64_t& state) {
    state ^= state << 13u;
    state ^= state >> 7u;
    state ^= state << 17u;
    return state;
}

}  // namespace

TEST(HostileCorpus, DeterministicMutationsRemainBoundedAcrossAllFuzzEntryPoints) {
    constexpr size_t case_count = 1024u;
    constexpr size_t maximum_size = 512u;
    uint64_t state = 0x4e414d5445521234ull;

    for (size_t case_index = 0; case_index < case_count; ++case_index) {
        const size_t size = static_cast<size_t>(next(state) % (maximum_size + 1u));
        std::vector<uint8_t> bytes(size);
        for (auto& byte : bytes) {
            byte = static_cast<uint8_t>(next(state));
        }

        if (!bytes.empty()) {
            constexpr std::array<uint8_t, 8> hostile_prefixes{
                0x00, 0x04, 0x05, 0x7f, 0x80, 0xf0, 0xff, 0xff,
            };
            bytes.front() = hostile_prefixes[case_index % hostile_prefixes.size()];
        }

        EXPECT_EQ(namter::fuzz_support::fuzz_varint(bytes.data(), bytes.size()), 0)
            << "case=" << case_index;
        EXPECT_EQ(namter::fuzz_support::fuzz_frame(bytes.data(), bytes.size()), 0)
            << "case=" << case_index;
        EXPECT_EQ(namter::fuzz_support::fuzz_lz4(bytes.data(), bytes.size()), 0)
            << "case=" << case_index;
    }
}
