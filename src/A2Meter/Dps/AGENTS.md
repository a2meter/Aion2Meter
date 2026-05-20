# A2Meter / src / A2Meter / Dps

## Parent
../AGENTS.md

## Summary
Core DPS calculation and session management pipeline. Accumulates damage per actor from packet hits, manages combat start/end detection, tracks target switching, saves combat records to history, and publishes snapshots to the renderer at ~10 Hz.

## Key Files

| File | Purpose |
|------|---------|
| DpsPipeline.cs | Main state machine; listens to packet events; manages session lifecycle (start on first hit, end on idle/boss death); publishes DataPushed events |
| DpsMeter.cs | Accumulates damage/healing per actor; maintains top skills, crit rates, DoT tracking; builds snapshots on demand |
| PartyTracker.cs | Tracks detected party members; maintains self entity ID; used for enrichment (CP, score, level) |
| BuffTracker.cs | Accumulates buff uptime per actor; builds snapshot on session end for history persistence |
| CombatHistory.cs | JSON persistence layer; saves/loads CombatRecord to %APPDATA%\A2Meter\history\*.json |
| Models.cs | Data models: ActorDps, DpsSnapshot, CombatRecord, BuffUptime, etc. |
| JobMapping.cs | Maps game job codes to UI archetypes (검성, 궁성, 마도성, etc.); color palette indexing |
| Protocol/ | Packet parsing: ProtocolPipeline, PacketProcessor, TcpReassembler, Lz4Decoder, PartyStreamParser, SkillDatabase, ServerMap |
| IPacketSource.cs | Interface for packet providers (PacketSniffer, PcapReplaySource) |
| PacketSniffer.cs | Live packet capture via Npcap (wpcap.dll); WinPcap adapter enumeration |
| PcapReplaySource.cs | Offline PCAP replay; reads .pcap files; feeds packets with optional speed control |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| [Protocol/](./Protocol/AGENTS.md) | Packet parsing: stream processor, flow reassembly, LZ4 decompression, combat packet parser, skill database |

## AI Agent Instructions

- **Session Lifecycle**: OnCombatHit() starts session on first hit to known boss/dummy. Push() ends session on idle (3s no hits), boss death (HP=0), or boss reset (HP restored to max).
- **Target Switching**: _currentTargetId tracks primary target. OnCombatHit() applies complex logic to prevent duplicate saves on target switch.
- **Hit Filtering**: Only accumulates hits to known bosses (from _knownBosses dict, populated by MobSpawned + TargetChanged).
- **Dummy Logic**: Dummies (허수아비, 샌드백) are self-only; non-self hits on dummies are dropped.
- **Party Enrichment**: OnPartyMemberSeen() triggers async SkillLevelCache fetch for party members.
- **Countdown Mode**: CycleCountdown() toggles between off/30/60/90.../180s. When active, Push() freezes measurement at time limit.
- **Snapshot Mapping**: MapForCanvas() converts ActorDps list to PlayerRow list; deduplicates same-name rows; appends detected party members as placeholder rows.
- **History Save**: SaveRecord() enriches actor CP/Score from party + API cache before persistence.

## Dependencies

- A2Meter.Api (SkillLevelCache, WebUploader)
- A2Meter.Core (AppSettings, PingMonitor)
- A2Meter.Direct2D (DpsCanvas models)
- A2Meter.Dps.Protocol (packet parsing)
- System.Net.NetworkInformation (possibly for interface enumeration)
- SharpZipLib (possibly; if packet compression handled here)

## Notes

- Perf: Per-session peak DPS tracked; per-actor peak DPS resets per session.
- Max HP correction: When first damage lands, corrects boss MaxHp = FirstBossHpSample + TotalDamageReceived (one-time per session).
- Death detection: Confirmed by explicit HP=0 packet OR cumulative damage exceeding last-sampled HP.
- Timeline recording: Appends per-second snapshots (cumulative actor damage) to _timeline during session.
- HitLog recording: Every hit appended with timestamp (T), actor ID, skill name, damage, hit flags.
