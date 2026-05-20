# A2Meter / src / A2Meter / Dps / Protocol

## Parent
../AGENTS.md

## Summary
Packet parsing layer. Captures raw packets from Npcap, reassembles TCP streams, decompresses LZ4 payload, parses combat protocol, and extracts combat hits, target changes, mob spawns, and buff events. Bridges low-level packet I/O to high-level DpsMetrics.

## Key Files

| File | Purpose |
|------|---------|
| ProtocolPipeline.cs | Top-level orchestrator; owns PacketProcessor + PartyStreamParser; fires CombatHit, TargetChanged, MobSpawned, etc. events |
| PacketProcessor.cs | Unpacks raw packets; delegates to stream processor per (source IP, source port, dest port) flow tuple |
| StreamProcessor.cs | Manages TCP reassembly + LZ4 decompression per flow; feeds decompressed packets to PartyStreamParser |
| TcpReassembler.cs | Buffers out-of-order TCP segments; detects holes; emits complete frames when all segments received |
| Lz4Decoder.cs | LZ4 decompression (likely using external LZ4 library) |
| FlowKey.cs | (Source IP, Source Port, Dest Port) tuple for flow demultiplexing |
| ChannelState.cs | Per-flow state: TCP sequence number tracking, reassembly buffer, decompressed buffer |
| PartyStreamParser.cs | Parses decompressed combat protocol; extracts CombatHitArgs, MobTarget, BuffEvents, etc. |
| PacketDispatcher.cs | Routes parsed packets to DpsPipeline event handlers |
| SkillDatabase.cs | In-memory skill catalog (ID → name, category); dungeon detection |
| ServerMap.cs | Server ID → name mapping (e.g., 1001 → 한나우, 2001 → 마리그라운드); short name abbreviations |
| StatMapping.cs | Game constants (stat formulas, damage types, hit flags) |
| ProtocolUtils.cs | Bit manipulation, endianness helpers for packet parsing |

## AI Agent Instructions

- **Packet Flow**: Npcap → PacketProcessor → TcpReassembler (per-flow) → Lz4Decoder → PartyStreamParser → ProtocolPipeline → DpsPipeline events.
- **Flow Demux**: PacketProcessor maps (sip, sport, dport) to ChannelState; reassembles and decompresses independently per flow.
- **TCP Reassembly**: TcpReassembler tracks seq# range; buffers out-of-order; emits complete segments when holes filled.
- **LZ4 Stream**: Decompressed output accumulated in ChannelState buffer; PartyStreamParser reads frames (opcode + data) sequentially.
- **Combat Protocol**: Frame opcodes identify hit packets, target changes, mob spawns, buff events, etc. Opcode values likely reverse-engineered.
- **Hit Extraction**: CombatHitArgs.Damage, TargetId, ActorId, HitFlags parsed from payload; skill name resolved via SkillDatabase.
- **Server Detection**: ServerMap.GetName(serverId) used for player display name enrichment (e.g., "NickName[한나우]").
- **Dungeon Detection**: SkillDatabase.IsDungeon(dungeonId) used by DpsPipeline to track intra-zone sessions.

## Dependencies

- A2Meter.Dps (DpsMetrics, PartyTracker event handlers)
- System.Net (IP address parsing)
- System.Collections (Dictionary for flow state)
- SharpZipLib or similar (LZ4 decompression)
- Npcap / wpcap.dll (via PacketSniffer)

## Notes

- Protocol is reverse-engineered; opcode values hardcoded or in config.
- Error handling: Malformed packets logged to Console.Error; stream continues.
- Perf: Per-flow decompression is CPU-bound; LZ4 is fast enough for real-time.
- Filtering: Only processes flows to/from detected Aion game servers.
