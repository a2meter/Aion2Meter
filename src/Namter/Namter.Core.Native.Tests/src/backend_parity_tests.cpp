#include "live_backend.hpp"
#include "capture_pipeline.hpp"
#include <array>
#include <sstream>
#include <limits>
#include <tuple>
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

namespace {
void bp16(std::vector<uint8_t> &bytes, uint16_t value) {
    bytes.push_back(static_cast<uint8_t>(value));
    bytes.push_back(static_cast<uint8_t>(value >> 8));
}
void bp32(std::vector<uint8_t> &bytes, uint32_t value) {
    for (int shift = 0; shift < 32; shift += 8)
        bytes.push_back(static_cast<uint8_t>(value >> shift));
}
void bp64(std::vector<uint8_t> &bytes, uint64_t value) {
    bp32(bytes, static_cast<uint32_t>(value));
    bp32(bytes, static_cast<uint32_t>(value >> 32));
}
void bpw32(std::vector<uint8_t> &bytes, size_t offset, uint32_t value) {
    for (int shift = 0; shift < 32; shift += 8)
        bytes[offset++] = static_cast<uint8_t>(value >> shift);
}
uint32_t bpcrc(std::span<const uint8_t> bytes) {
    uint32_t crc = ~0u;
    for (size_t i = 0; i < bytes.size(); ++i) {
        crc ^= i >= 12 && i < 16 ? 0u : bytes[i];
        for (int bit = 0; bit < 8; ++bit)
            crc = (crc >> 1) ^ ((0u - (crc & 1u)) & 0xedb88320u);
    }
    return ~crc;
}
std::vector<uint8_t> bpsnapshot() {
    std::vector<uint8_t> bytes{'N', 'M', 'P', 'S'};
    bp16(bytes, 1); bp16(bytes, 28); bp32(bytes, 0); bp32(bytes, 0); bp64(bytes, 7);
    bp32(bytes, 3); bp16(bytes, 3); bytes.insert(bytes.end(), {6, 0, 0x36});
    bp16(bytes, 1); bp16(bytes, 13328); bp32(bytes, 1); bp16(bytes, 10); bp16(bytes, 2);
    bytes.insert(bytes.end(), {0x21, 0x8d});
    bp32(bytes, 1); bp32(bytes, 1); bp32(bytes, 1); bp32(bytes, 128);
    bp16(bytes, 1); bp16(bytes, 0); bp16(bytes, 1); bp16(bytes, 4);
    bp32(bytes, 0); bp32(bytes, 1); bp32(bytes, 5);
    bpw32(bytes, 8, static_cast<uint32_t>(bytes.size()));
    bpw32(bytes, 12, bpcrc(bytes));
    return bytes;
}
std::vector<uint8_t> bpraw() {
    std::vector<uint8_t> packet(40, 0);
    packet[0] = 0x45; packet[3] = 47; packet[8] = 64; packet[9] = 6;
    packet[12] = 10; packet[15] = 1; packet[16] = 10; packet[19] = 2;
    packet[20] = 0x34; packet[21] = 0x10; packet[22] = 0xc3; packet[23] = 0x50;
    packet[27] = 1; packet[32] = 0x50; packet[33] = 0x18;
    packet.insert(packet.end(), {0x0a, 0x21, 0x8d, 0xc9, 0x3f, 0, 1});
    return packet;
}
std::string bppcap(const std::vector<uint8_t> &packet) {
    std::vector<uint8_t> bytes{0xd4, 0xc3, 0xb2, 0xa1};
    bp16(bytes, 2); bp16(bytes, 4); bp32(bytes, 0); bp32(bytes, 0);
    bp32(bytes, 65535); bp32(bytes, 101); bp32(bytes, 1); bp32(bytes, 0);
    bp32(bytes, static_cast<uint32_t>(packet.size()));
    bp32(bytes, static_cast<uint32_t>(packet.size()));
    bytes.insert(bytes.end(), packet.begin(), packet.end());
    return {reinterpret_cast<const char *>(bytes.data()), bytes.size()};
}
struct OwnedEvent {
    std::array<uint64_t,39> scalars;
    std::string name;
    std::vector<uint8_t> payload;
    auto operator<=>(const OwnedEvent&) const = default;
};
using Ledger=std::vector<OwnedEvent>;
namter::CapturePipeline pipeline_for(Ledger&ledger){auto snapshot=bpsnapshot();return namter::CapturePipeline({.flow={.max_live_flows=4,.max_out_of_order_bytes_per_flow=4096},.frame={}},snapshot,[&](const nm_event_v1&e){if(e.kind==NM_EVENT_ENTITY_REMOVED)ledger.push_back({{e.abi_version,e.struct_size,e.kind,e.reserved,e.first_timestamp_ns,e.last_timestamp_ns,e.epoch,e.first_file_offset,e.last_file_offset,e.source_address,e.destination_address,e.source_port,e.destination_port,e.actor_id,e.target_id,e.owner_id,e.skill_id,e.buff_id,e.mob_id,e.boss_id,e.content_id,e.dungeon_id,e.party_id,e.server_id,e.job_id,e.damage,e.multi_damage,e.healing,e.current_hp,e.max_hp,e.special_mask,e.duration_ms,e.state,e.action,e.damage_type,e.is_dot,e.is_self,e.is_boss,e.flags_reserved},e.name_size?std::string(reinterpret_cast<const char*>(e.name),e.name_size):std::string{},e.payload_size?std::vector<uint8_t>(e.payload,e.payload+e.payload_size):std::vector<uint8_t>{}});},[](uint32_t,const char*){});}
struct WCtx{WinDivertApi api{};std::vector<uint8_t> bytes=bpraw();bool sent=false;WCtx(){api.context=this;api.identity=[](void*){return ApiResult::ok;};api.resolve=[](void*,const char*){return true;};api.compile_filter=[](void*,const char*){return true;};api.open=[](void*,const char*,uint32_t,uint64_t){return reinterpret_cast<void*>(1);};api.set_param=[](void*,void*,uint32_t,uint64_t){return true;};api.receive_batch=[](void*p,void*,LivePacket*out,size_t,size_t*n){auto&s=*static_cast<WCtx*>(p);if(s.sent)return ApiResult::cancelled;s.sent=true;out[0]={s.bytes.data(),static_cast<uint32_t>(s.bytes.size()),static_cast<uint32_t>(s.bytes.size()),1'000'000'000,dlt_raw,false};*n=1;return ApiResult::ok;};api.cancel=[](void*,void*,uint32_t how){return how==1;};api.close=[](void*,void*){};}};
struct NCtx{NpcapApi api{};std::vector<uint8_t> bytes=bpraw();bool sent=false;NCtx(){api.context=this;api.identity=[](void*){return "Npcap test";};api.resolve=[](void*,const char*){return true;};api.enumerate=[](void*){return std::vector<std::string>{"test"};};api.create=[](void*,const char*){return reinterpret_cast<void*>(1);};api.set_immediate=[](void*,void*,int){return 0;};api.set_kernel_buffer=[](void*,void*,int){return 0;};api.set_user_buffer=[](void*,void*,int){return 0;};api.activate=[](void*,void*){return 0;};api.compile_apply=[](void*,void*,const char*){return 0;};api.link_type=[](void*,void*){return dlt_raw;};api.get_event=[](void*,void*){return reinterpret_cast<void*>(2);};api.receive=[](void*p,void*,LivePacket*out){auto&s=*static_cast<NCtx*>(p);if(s.sent)return ApiResult::cancelled;s.sent=true;*out={s.bytes.data(),static_cast<uint32_t>(s.bytes.size()),static_cast<uint32_t>(s.bytes.size()),1'000'000'000};return ApiResult::ok;};api.stats=[](void*,void*,BackendStats*out){*out={1,0,0,1,0,0};return true;};api.break_loop=[](void*,void*){};api.close=[](void*,void*){};}};
}

TEST(BackendParity, ActualInjectedAdaptersAndPcapReaderProduceSameOrderedEventLedger) {
    Ledger w, n, p;
    auto wp = pipeline_for(w), np = pipeline_for(n), pp = pipeline_for(p);
    WCtx wc; NCtx nc;
    auto wb = make_windivert_backend(&wc.api);
    auto nb = make_npcap_backend(&nc.api);
    BackendConfig config{.port=13328,.adapter="test"};
    std::optional<CaptureProvenance> wprov, nprov, pprov;
    ASSERT_EQ(wb->start(config,[&](const CaptureRecord&r){
        auto normalized=PacketNormalizer::normalize(r); ASSERT_TRUE(normalized.segment);
        wprov=normalized.segment->provenance; EXPECT_EQ(wp.ingest(r),CaptureError::none);
    }),BackendError::none);
    ASSERT_EQ(nb->start(config,[&](const CaptureRecord&r){
        auto normalized=PacketNormalizer::normalize(r); ASSERT_TRUE(normalized.segment);
        nprov=normalized.segment->provenance; EXPECT_EQ(np.ingest(r),CaptureError::none);
    }),BackendError::none);
    EXPECT_EQ(wb->poll(),BackendError::none); EXPECT_EQ(nb->poll(),BackendError::none);
    auto input=std::istringstream(bppcap(bpraw()),std::ios::binary); PcapReader reader(input);
    CaptureRecord record; ASSERT_TRUE(reader.read_next(record));
    auto normalized=PacketNormalizer::normalize(record); ASSERT_TRUE(normalized.segment);
    pprov=normalized.segment->provenance; EXPECT_EQ(pp.ingest(record),CaptureError::none);
    ASSERT_TRUE(wprov && nprov && pprov);
    EXPECT_EQ(wprov->source,CaptureSource::windivert); EXPECT_EQ(nprov->source,CaptureSource::npcap);
    EXPECT_EQ(pprov->source,CaptureSource::pcap);
    EXPECT_EQ(wprov->timestamp_ns,1'000'000'000u); EXPECT_EQ(nprov->timestamp_ns,1'000'000'000u);
    EXPECT_EQ(pprov->timestamp_ns,1'000'000'000u);
    EXPECT_EQ(wprov->captured_length,bpraw().size()); EXPECT_EQ(nprov->captured_length,bpraw().size());
    EXPECT_EQ(pprov->captured_length,bpraw().size());
    EXPECT_EQ(wprov->original_length,wprov->captured_length);
    EXPECT_EQ(nprov->original_length,nprov->captured_length);
    EXPECT_EQ(pprov->original_length,pprov->captured_length);
    EXPECT_EQ(wprov->direction,CaptureDirection::inbound);
    EXPECT_EQ(nprov->direction,CaptureDirection::inbound);
    EXPECT_EQ(pprov->direction,CaptureDirection::inbound);
    EXPECT_EQ(wprov->link_type,dlt_raw); EXPECT_EQ(nprov->link_type,dlt_raw);
    EXPECT_EQ(pprov->link_type,dlt_raw);
    EXPECT_EQ(wprov->backend_name,"windivert"); EXPECT_EQ(nprov->backend_name,"npcap");
    EXPECT_EQ(pprov->backend_name,"pcap");
    EXPECT_FALSE(wprov->runtime_version.empty()); EXPECT_FALSE(nprov->runtime_version.empty());
    EXPECT_TRUE(pprov->runtime_version.empty());
    EXPECT_TRUE(wprov->interface_identity.empty()); EXPECT_EQ(nprov->interface_identity,"test");
    EXPECT_TRUE(pprov->interface_identity.empty());
    EXPECT_EQ(wprov->backend_received,0u); EXPECT_EQ(wprov->backend_dropped,0u);
    EXPECT_EQ(wprov->backend_interface_dropped,0u);
    EXPECT_EQ(nprov->backend_received,1u); EXPECT_EQ(nprov->backend_dropped,0u);
    EXPECT_EQ(nprov->backend_interface_dropped,0u);
    EXPECT_EQ(pprov->backend_received,0u); EXPECT_EQ(pprov->backend_dropped,0u);
    EXPECT_EQ(pprov->backend_interface_dropped,0u);
    EXPECT_EQ(wprov->file_offset,0u); EXPECT_EQ(nprov->file_offset,0u);
    EXPECT_GT(pprov->file_offset,0u);
    EXPECT_EQ(wprov->timestamp_precision,TimestampPrecision::nanoseconds);
    EXPECT_EQ(nprov->timestamp_precision,TimestampPrecision::nanoseconds);
    EXPECT_EQ(pprov->timestamp_precision,TimestampPrecision::microseconds);
    ASSERT_EQ(w.size(),1u); ASSERT_EQ(n.size(),1u); ASSERT_EQ(p.size(),1u);
    EXPECT_EQ(w[0].scalars[7],0u); EXPECT_EQ(w[0].scalars[8],0u);
    EXPECT_EQ(n[0].scalars[7],0u); EXPECT_EQ(n[0].scalars[8],0u);
    EXPECT_GT(p[0].scalars[7],0u); EXPECT_GE(p[0].scalars[8],p[0].scalars[7]);
    auto w_event=w[0],n_event=n[0],p_event=p[0];w_event.scalars[7]=w_event.scalars[8]=0;
    n_event.scalars[7]=n_event.scalars[8]=0;p_event.scalars[7]=p_event.scalars[8]=0;
    EXPECT_EQ(w_event,n_event); EXPECT_EQ(n_event,p_event);
    EXPECT_EQ(p[0].scalars[13],8137u); EXPECT_EQ(p[0].scalars[11],13328u);
    EXPECT_EQ(p[0].scalars[12],50000u);
}
