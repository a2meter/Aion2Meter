# Namter Capture Engine Design

## 1. Purpose and Scope

Namter is a new product built from scratch. It does not modify, reference, or ship projects or binaries from A2Meter. The existing repository, `captures/aion2_part001.pcap`, and the readable encounter exports are evidence and golden fixtures only.

This specification covers phase 1:

- a new standalone `Namter.slnx` solution;
- live Npcap and offline PCAP ingestion;
- loss-aware TCP reconstruction;
- protocol framing and LZ4 decoding;
- one authoritative C# protocol decoder;
- deterministic combat-event reduction;
- versioned game-data delivery through `aion.db`;
- a command-line PCAP/readable comparison tool;
- automated correctness, resilience, update, and resource-limit tests.

The overlay, encounter detail UI, history UI, and settings UI are phase 2. Phase 2 consumes only the stable phase-1 event and encounter contracts and receives its own design and implementation plan.

## 2. Evidence and Success Criteria

`captures/aion2_part001.pcap` is a classic little-endian PCAP with RAW IPv4 link type. It contains 20,849 TCP packets over 503.320 seconds and two inbound-only flows from server port 13328. The flows contain no missing TCP byte ranges, but contain six partial or complete overlaps totaling 66 duplicate bytes. A valid implementation must accept midstream inbound-only flows, isolate concurrent connections, and remove those duplicate bytes without changing decoded results.

The PCAP contains the encounters represented by these readable fixtures:

- `20260710_221020_의지의 투르겐_readable`;
- `20260710_221214_금기의 마수 그리오사_readable`.

The independent `20260710_213603_위악의 바실루스_readable` fixture is used to validate reducer semantics without claiming that it came from the PCAP.

Phase 1 succeeds when:

- the PCAP deterministically yields exactly the Turgen and Griosa encounters;
- boss identity, dungeon identity, participants, boss-target damage, skill aggregates, healing, DoT markers, flags, and buff windows are compared with the readable fixtures;
- boss total damage and participant identities match the goldens;
- every remaining field-level difference is emitted in a machine-readable comparison report rather than hidden;
- processing is independent of replay speed and TCP segmentation;
- the known 66 duplicate bytes are removed and no unresolved gap is fabricated into a continuous stream;
- bounded queues and buffers remain within configured limits.

## 3. Solution and Project Boundaries

The new solution lives under a new `src/Namter/` root and is independent from all A2Meter projects.

```text
Namter.slnx
src/Namter/
├─ Namter.Capture/
├─ Namter.Protocol/
├─ Namter.Encounter/
├─ Namter.GameData/
├─ Namter.Cli/
├─ Namter.Tests.Unit/
└─ Namter.Tests.Integration/
```

Responsibilities:

- `Namter.Capture` reads Npcap and PCAP records, validates IPv4/TCP, assigns flow epochs, reconstructs ordered byte streams, and exposes transport diagnostics.
- `Namter.Protocol` incrementally frames byte streams, expands LZ4 batches, decodes protocol messages with the active `aion.db` protocol profile, and emits immutable typed events.
- `Namter.Encounter` owns actor, summon, boss, buff, and encounter state. It consumes events on one ordered reducer loop and emits snapshots and final records.
- `Namter.GameData` validates, updates, opens, and selectively caches `aion.db`. It exposes query interfaces without leaking SQLite types to consumers.
- `Namter.Cli` replays PCAP files or directories, exports decoded ledgers and encounters, and compares results with readable fixtures.
- Unit tests verify each boundary. Integration tests use the supplied PCAP and readable fixtures.

No source linking, A2Meter project reference, `PacketEngine.dll`, native decoder fallback, or duplicate decoder implementation is permitted.

## 4. Processing Architecture

The pipeline is unidirectional:

```text
Npcap / PCAP
    -> CaptureRecord
    -> validated TcpSegment
    -> FlowTracker + ConnectionEpoch
    -> loss-aware TcpReassembler
    -> ProtocolFramer + Lz4Decoder
    -> C# ProtocolDecoder
    -> immutable CombatEvent
    -> single-threaded EncounterReducer
    -> EncounterUpdate / EncounterRecord / comparison report
```

Every envelope preserves capture timestamp, capture identifier, interface/link type, flow identifier, connection epoch, source offset, and diagnostic provenance. Replay uses capture timestamps as its authoritative clock; wall-clock replay speed cannot change timeout or encounter results.

The capture-to-decode channel and decode-to-reducer channel are bounded. Queue capacity and overflow behavior are explicit configuration. An overflow marks the capture incomplete, increments structured counters, and prevents a seemingly valid final encounter from being presented without a data-loss warning.

## 5. Capture and TCP Reconstruction

A flow key contains source IP, source port, destination IP, destination port, and connection epoch. Initialization does not require SYN or client-to-server traffic because the golden PCAP begins midstream and contains only inbound traffic.

The reassembler:

- handles arbitrary segmentation and coalescing;
- trims fully duplicated and partially overlapping byte ranges;
- buffers bounded out-of-order ranges;
- supports 32-bit sequence wraparound;
- treats FIN, RST, idle expiry, and observed tuple reuse as epoch boundaries;
- records bytes accepted, duplicates removed, overlaps, gaps, resets, and discarded ranges;
- never concatenates bytes across an unresolved gap;
- never uses wall-clock time for replay decisions.

When a gap cannot be resolved, the affected decoder epoch is reset. Resynchronization is allowed only at a validated protocol boundary with length and marker checks. A weak raw byte-pattern scan is not sufficient proof of a boundary.

## 6. Framing and Protocol Decoding

The framer is incremental and independent of TCP packet boundaries. It validates varints, declared lengths, marker bytes, compressed sizes, decompressed sizes, and configured maximums before allocation. Corrupt data produces a structured flow-scoped diagnostic and cannot terminate unrelated flows or the application.

One C# `ProtocolDecoder` is the authoritative implementation. It reads opcode and layout metadata from an immutable game-data snapshot captured for the encounter. Unknown or unproven messages are preserved as `UnknownProtocolEvent` with provenance and bounded payload diagnostics; they are not guessed into combat totals.

Typed events preserve at least:

- capture time and flow provenance;
- actor, target, skill, buff, boss, mob, content, and dungeon identifiers;
- normal damage, multi-damage, and healing as separate values;
- DoT and special-mask flags including critical, back, perfect, double, power-shard, and smite when present;
- boss HP and lifecycle signals;
- buff apply, refresh, remove, target, owner, and duration information;
- zone, combat-state, party, player, summon, and ownership changes.

Protocol-level deduplication uses stable provenance and message identity. TCP retransmissions must not create duplicate domain events.

## 7. Encounter Reduction

`EncounterReducer` is deterministic and single-threaded. It receives events in capture-time and stream-order sequence and uses an injected capture clock.

It maintains explicit state for:

- current content, dungeon, and protocol data version;
- authoritative boss identity, actor ID, mob code, HP, and maximum HP;
- players, party identities, summons, and summon ownership;
- skill-level events and separate damage, multi-damage, healing, and DoT values;
- buff windows and uptime summaries;
- encounter start, active, finishing, completed, and incomplete states.

Authoritative combat and boss lifecycle signals end encounters. Idle timeout is a documented fallback, not the primary boundary. Boss-target predicates are explicit and backed by `aion.db`; rows targeting players, adds, or unrelated mobs do not silently enter boss damage totals. Every record stores the Namter app version, `aion.db` data version, schema version, and protocol profile version.

## 8. Game Data and Local Storage

Mutable game knowledge lives in a server database and is published to clients as a signed, compressed SQLite snapshot. The local database filename is fixed as `aion.db`.

```text
Namter/
└─ data/
   ├─ aion.db
   ├─ manifest.json
   └─ backup/
      └─ aion.previous.db
```

The database contains versioned protocol profiles, opcodes, message layouts, bosses, mobs, dungeons, content identifiers, skills, buffs, and other update-sensitive mappings. Tables use integer primary keys and query-specific indexes. Large unrelated item or equipment data is excluded from phase 1.

`manifest.json` contains data version, schema version, protocol profile version, minimum supported app version, compressed and uncompressed sizes, SHA-256 digest, creation time, download location, and a digital signature. The application embeds only the public verification key.

SQLite remains the source of truth. Startup selectively caches hot, bounded lookup sets such as active opcodes, bosses, dungeons, skills, and buffs. Cold or relational queries use SQLite. The target incremental working-set cost for game data is 5–20 MB rather than materializing the whole database as an object graph.

An encounter pins one immutable game-data snapshot. A downloaded update cannot mix mappings inside an active encounter.

## 9. Update and Recovery Protocol

Startup validates the local manifest, database digest, SQLite integrity, schema version, and required tables. It then sends the current data version to the update endpoint and retrieves only the latest manifest when checking for updates.

For a newer compatible version:

1. download to a temporary file;
2. verify transport length, SHA-256, and digital signature;
3. decompress to a temporary SQLite file;
4. run SQLite integrity and required-schema checks;
5. stage the validated snapshot;
6. wait for the current encounter to finish;
7. atomically replace `aion.db` and rebuild selective caches;
8. retain one previous validated snapshot for rollback.

Network failure, interruption, digest mismatch, signature failure, decompression failure, or invalid SQLite leaves the active database untouched. An unsupported schema prompts for an application update and continues with the last compatible database. If no valid local database exists, capture does not start and the CLI returns a distinct recovery error.

## 10. Diagnostics and Operational Limits

Diagnostics are structured and attributable to capture, flow, frame, message, event, and encounter. Required counters include input packets, invalid packets, flows, epochs, accepted bytes, duplicate bytes, overlaps, gaps, resets, frames, compressed batches, decompression failures, unknown opcodes, emitted events, queue high-water marks, dropped envelopes, and incomplete encounters.

Limits for packet size, out-of-order bytes per flow, number of live flows, frame size, decompressed batch size, queue capacity, and diagnostic payload retention are centrally configured and covered by tests. Exceeding a limit isolates the smallest affected scope and never causes unbounded allocation.

## 11. Verification Strategy

Implementation follows test-driven development. Each production behavior begins with a failing test.

Unit tests cover:

- arbitrary segmentation and coalescing;
- ordered, out-of-order, duplicate, and partially overlapping segments;
- missing gaps, epoch reset, inbound-only midstream start, tuple reuse, and sequence wraparound;
- malformed IP/TCP records, truncated captures, corrupt varints, invalid frame lengths, and corrupt LZ4;
- concurrent flow isolation and deterministic capture-clock timeouts;
- typed event field preservation and deduplication;
- encounter start/end transitions, boss predicates, summons, damage categories, healing, DoTs, flags, and buff windows;
- same-version update checks, signed update success, atomic replacement, all validation failures, schema incompatibility, deferred activation, and rollback;
- bounded queue and buffer behavior.

Integration tests cover:

- intact `aion2_part001.pcap` to the two matching readable encounters;
- multiple replay speeds producing byte-for-byte equivalent event ledgers and encounter records;
- re-segmented and reordered versions of equivalent streams producing the same result;
- the Basilus readable fixture as a reducer semantic fixture;
- field-level golden comparison reports;
- resource high-water marks and zero fabricated continuity.

## 12. Delivery Boundary

Phase 1 delivers a buildable standalone solution, reusable libraries, `aion.db` update support, CLI replay/comparison commands, automated tests, and evidence reports for the supplied fixtures. It does not deliver the overlay or other desktop UI. Phase 2 begins only after the phase-1 contracts and golden results are accepted.
