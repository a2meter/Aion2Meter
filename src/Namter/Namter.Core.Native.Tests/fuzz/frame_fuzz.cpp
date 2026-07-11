#include "../src/fuzz_support.hpp"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t* data, size_t size) {
    return namter::fuzz_support::fuzz_frame(data, size);
}
