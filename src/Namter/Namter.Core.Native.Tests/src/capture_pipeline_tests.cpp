#include <gtest/gtest.h>
#include "capture_pipeline.hpp"

namespace {
namter::CaptureRecord packet(uint32_t sequence, uint8_t flags, std::vector<uint8_t> payload,
                             uint64_t timestamp, uint16_t source_port = 13328) {
    std::vector<uint8_t> bytes(40, 0);
    bytes[0]=0x45; const auto total=static_cast<uint16_t>(40+payload.size());
    bytes[2]=static_cast<uint8_t>(total>>8);bytes[3]=static_cast<uint8_t>(total);
    bytes[8]=64;bytes[9]=6;bytes[12]=10;bytes[15]=1;bytes[16]=10;bytes[19]=2;
    bytes[20]=static_cast<uint8_t>(source_port>>8);bytes[21]=static_cast<uint8_t>(source_port);bytes[22]=0xc3;bytes[23]=0x50;
    bytes[24]=static_cast<uint8_t>(sequence>>24);bytes[25]=static_cast<uint8_t>(sequence>>16);
    bytes[26]=static_cast<uint8_t>(sequence>>8);bytes[27]=static_cast<uint8_t>(sequence);
    bytes[32]=0x50;bytes[33]=flags;bytes.insert(bytes.end(),payload.begin(),payload.end());
    return {.source=namter::CaptureSource::pcap,.timestamp_ns=timestamp,.link_type=namter::dlt_raw,
            .captured_length=static_cast<uint32_t>(bytes.size()),.original_length=static_cast<uint32_t>(bytes.size()),.bytes=std::move(bytes)};
}
}

TEST(CapturePipeline, TupleReuseAndResetNeverAccumulateEpochFramers) {
    namter::CapturePipeline pipeline({.flow={.max_live_flows=2,.max_out_of_order_bytes_per_flow=4096},.frame={}}, {}, [](const nm_event_v1&){}, [](uint32_t,const char*){});
    for(uint32_t epoch=0;epoch<100;++epoch){ASSERT_EQ(pipeline.ingest(packet(epoch*100,namter::tcp_syn,{0x05},epoch+1)),namter::CaptureError::none);EXPECT_LE(pipeline.active_framer_count(),1u);}
}

TEST(CapturePipeline, ExplicitCaptureTimeFlushClosesPendingFrameWithDiagnostic) {
    uint32_t diagnostics=0;
    namter::CapturePipeline pipeline({.flow={.max_live_flows=2,.max_out_of_order_bytes_per_flow=4096,.idle_timeout_ns=10},.frame={}}, {}, [](const nm_event_v1&){}, [&](uint32_t,const char*){++diagnostics;});
    ASSERT_EQ(pipeline.ingest(packet(1,namter::tcp_ack,{0x0a,0x21},100)),namter::CaptureError::none);
    EXPECT_EQ(pipeline.active_framer_count(),1u);
    pipeline.flush(111);
    EXPECT_EQ(pipeline.active_framer_count(),0u);
    EXPECT_GT(diagnostics,0u);
}

TEST(CapturePipeline, DistinctFlowFramersStayBoundedAndResetReleasesCapacity) {
    namter::CapturePipeline pipeline(
        {.flow={.max_live_flows=2,.max_out_of_order_bytes_per_flow=4096},.frame={}}, {},
        [](const nm_event_v1&){}, [](uint32_t,const char*){});
    ASSERT_EQ(pipeline.ingest(packet(1,namter::tcp_ack,{0x05},1,13328)),namter::CaptureError::none);
    ASSERT_EQ(pipeline.ingest(packet(1,namter::tcp_ack,{0x05},2,13329)),namter::CaptureError::none);
    EXPECT_EQ(pipeline.active_framer_count(),2u);
    EXPECT_EQ(pipeline.ingest(packet(1,namter::tcp_ack,{0x05},3,13330)),
              namter::CaptureError::none);
    EXPECT_EQ(pipeline.active_framer_count(),2u);
    ASSERT_EQ(pipeline.ingest(packet(2,namter::tcp_rst,{},4,13328)),namter::CaptureError::none);
    EXPECT_EQ(pipeline.active_framer_count(),1u);
    ASSERT_EQ(pipeline.ingest(packet(1,namter::tcp_ack,{0x05},5,13330)),namter::CaptureError::none);
    EXPECT_EQ(pipeline.active_framer_count(),2u);
}
