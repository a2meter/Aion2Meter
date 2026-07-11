#include <gtest/gtest.h>

#include "namter/core.h"

TEST(Abi, ReportsVersionOne) {
    EXPECT_EQ(nm_core_abi_version(), 1u);
}
