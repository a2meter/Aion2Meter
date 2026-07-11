#include "live_backend.hpp"
#include <gtest/gtest.h>

using namespace namter;

TEST(BackendParity, NormalizedPacketsHaveIdenticalPayloadDirectionTupleAndOrdering) {
    const std::vector<uint8_t> ip = {
        0x45, 0, 0,    0x2a, 0, 0, 0, 0, 64, 6, 0, 0,    10,   0, 0, 1, 10, 0, 0, 2,    0x34,
        0x10, 0, 0x50, 0,    0, 0, 1, 0, 0,  0, 0, 0x50, 0x18, 0, 0, 0, 0,  0, 0, 0xaa, 0xbb};
    auto make = [&](CaptureSource source) {
        CaptureRecord r{.source = source,
                        .timestamp_ns = 100,
                        .link_type = dlt_raw,
                        .captured_length = static_cast<uint32_t>(ip.size()),
                        .original_length = static_cast<uint32_t>(ip.size()),
                        .bytes = ip};
        return PacketNormalizer::normalize(r);
    };
    const auto w = make(CaptureSource::windivert), n = make(CaptureSource::npcap),
               p = make(CaptureSource::pcap);
    ASSERT_TRUE(w.segment && n.segment && p.segment);
    EXPECT_EQ(w.segment->flow, n.segment->flow);
    EXPECT_EQ(n.segment->flow, p.segment->flow);
    EXPECT_EQ(w.segment->payload, n.segment->payload);
    EXPECT_EQ(n.segment->payload, p.segment->payload);
    EXPECT_EQ(w.segment->sequence, n.segment->sequence);
    EXPECT_EQ(n.segment->sequence, p.segment->sequence);
    EXPECT_EQ(w.segment->provenance.direction, CaptureDirection::inbound);
    EXPECT_EQ(n.segment->provenance.direction, CaptureDirection::inbound);
    EXPECT_EQ(p.segment->provenance.direction, CaptureDirection::inbound);
}

TEST(BackendParity, BoundedDeliveryQueueDropsNewestAndReportsLoss) {
    BoundedCaptureQueue q(1);
    CaptureRecord a{.bytes = {1}}, b{.bytes = {2}};
    EXPECT_TRUE(q.push(a));
    EXPECT_FALSE(q.push(b));
    EXPECT_EQ(q.dropped(), 1u);
    auto popped = q.pop();
    ASSERT_TRUE(popped);
    EXPECT_EQ(popped->bytes, (std::vector<uint8_t>{1}));
    q.stop();
    EXPECT_FALSE(q.push(a));
}
