# Namter Capture Engine Design

## 1. Purpose and Scope

Namter is a new product built from scratch. It does not modify, reference, or ship projects or binaries from A2Meter. The existing repository, `captures/aion2_part001.pcap`, and the readable encounter exports are evidence and golden fixtures only.

This specification covers phase 1:

- a new standalone `Namter.slnx` solution;
- user-selectable live WinDivert or Npcap ingestion and offline PCAP ingestion;
- loss-aware TCP reconstruction;
- protocol framing and LZ4 decoding;
- one authoritative C++ protocol decoder;
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
├─ Namter.Core.Native/          # C++20 shared library
├─ Namter.Core.Native.Tests/    # native unit and fuzz tests
├─ Namter.Core.Interop/         # C# C-ABI wrapper
├─ Namter.Encounter/            # C# deterministic reducer
├─ Namter.GameData/             # C# aion.db update and queries
├─ Namter.Cli/                  # C# composition root and validator
├─ Namter.Tests.Unit/           # managed unit tests
└─ Namter.Tests.Integration/    # end-to-end golden tests
```

Responsibilities:

- `Namter.Core.Native` owns the WinDivert, Npcap, and PCAP input adapters; validates IPv4/TCP; assigns flow epochs; reconstructs ordered byte streams; incrementally frames streams; expands LZ4 batches; decodes protocol messages; and emits immutable C-ABI event records. It is the only capture and protocol implementation.
- `Namter.Core.Native.Tests` exercises transport, framing, decompression, decoder, ABI, stress, and fuzz behavior without loading the .NET host.
- `Namter.Core.Interop` owns safe handles, native library loading, ABI/version checks, callback lifetime, cancellation, and conversion from ABI records to immutable managed events. It contains no alternate parser.
- `Namter.Encounter` owns actor, summon, boss, buff, and encounter state. It consumes events on one ordered reducer loop and emits snapshots and final records.
- `Namter.GameData` validates, updates, opens, and selectively caches `aion.db`. It compiles the selected protocol profile into a bounded immutable binary snapshot for the native decoder and exposes query interfaces without leaking SQLite types to consumers.
- `Namter.Cli` replays PCAP files or directories, exports decoded ledgers and encounters, and compares results with readable fixtures.
- Unit tests verify each boundary. Integration tests use the supplied PCAP and readable fixtures.

No source linking, A2Meter project reference, legacy `PacketEngine.dll`, managed decoder fallback, or duplicate decoder implementation is permitted.

The native library is C++20 and exports a narrow, versioned C ABI rather than C++ classes or STL types. Every ABI structure begins with `abi_version` and `struct_size`; variable data is represented by pointer-and-length views valid only for the documented callback duration. Native contexts are opaque handles with explicit create, start, stop, flush, diagnostics, and destroy operations. No exception, allocator ownership, `std::string`, or `std::vector` crosses the boundary.

## 4. Processing Architecture

The pipeline is unidirectional:

```text
WinDivert / Npcap / PCAP
    -> CaptureRecord
    -> validated TcpSegment
    -> FlowTracker + ConnectionEpoch
    -> loss-aware TcpReassembler
    -> ProtocolFramer + Lz4Decoder
    -> C++ ProtocolDecoder
    -> versioned C ABI event
    -> immutable managed CombatEvent
    -> single-threaded EncounterReducer
    -> EncounterUpdate / EncounterRecord / comparison report
```

Every envelope preserves capture timestamp, capture identifier, interface/link type, flow identifier, connection epoch, source offset, and diagnostic provenance. Replay uses capture timestamps as its authoritative clock; wall-clock replay speed cannot change timeout or encounter results.

The native capture-to-decode queue and managed decode-to-reducer channel are bounded. Queue capacity and overflow behavior are explicit configuration. An overflow marks the capture incomplete, increments structured counters, and prevents a seemingly valid final encounter from being presented without a data-loss warning.

## 5. Live Capture Backends

The user selects `WinDivert` or `Npcap` in configuration. The CLI exposes the same explicit option. Namter never silently changes backend after capture has started. Startup probes availability and reports actionable errors; it may recommend the other installed backend, but switching requires the user to select it or an explicit `auto` policy in a later UI design.

Both backends normalize packets into the same native `CaptureRecord` contract before any IP/TCP or protocol logic. Backend name and version, interface identity, direction, original timestamp, captured length, original length, and backend drop counters remain in provenance. Downstream code cannot branch on backend except for diagnostics.

### 5.1 WinDivert

WinDivert uses `WINDIVERT_LAYER_NETWORK` with `WINDIVERT_FLAG_SNIFF | WINDIVERT_FLAG_RECV_ONLY`. Namter only observes packets; it does not divert-and-reinject, modify, drop, or inject game traffic. The filter is prevalidated and kept as selective and simple as possible, initially limiting capture to TCP and the configured game server port/profile. The backend uses `WinDivertRecvEx` batched overlapped I/O, begins receiving immediately after open, and exposes configured queue length, size, time, high-water, and loss diagnostics.

The official signed WinDivert DLL and matching x64 driver are packaged for the supported x64 build. Startup distinguishes missing files, missing administrator privileges, invalid driver signature, blocked driver, disabled Base Filtering Engine, incompatible loaded driver, and receive-buffer errors. WinDivert timestamps are converted once from the QueryPerformanceCounter clock into the core's monotonic capture-time representation.

### 5.2 Npcap

Npcap dynamically loads its `wpcap.dll`, verifies the runtime library identity/version, enumerates adapters, and applies a compiled BPF filter equivalent to the WinDivert selection predicate. It uses immediate mode for responsive delivery, an explicitly sized kernel buffer, an explicitly sized user buffer, event-driven cancellation, and `pcap_stats`/`pcap_stats_ex` when available to surface received and dropped packet counts. Link type is preserved so Ethernet, raw IP, and other supported captures enter the appropriate normalizer.

Namter does not assume that Npcap is installed and never bundles Npcap binaries, drivers, or an installer. It does not download, launch, or silently automate the Npcap installer. When the user selects Npcap and no compatible runtime is detected, Namter marks that backend unavailable and presents the official Npcap download page, installation requirements, detected status, and a retry action. Opening the external download page requires an explicit user action. After the user installs Npcap independently, Namter re-runs runtime, service, adapter, and version probes before enabling capture.

This external-install policy remains in force even if Namter later obtains OEM redistribution rights; changing it requires a separate product and licensing decision. The WinDivert backend and offline PCAP replay remain usable independently of Npcap availability.

### 5.3 Backend Parity

Backend-specific code ends at `CaptureRecord`. Identical normalized packet sequences must produce byte-for-byte equivalent native event ledgers. Tests feed the same synthetic frames through both adapter normalizers and the PCAP adapter, and assert equivalent direction, timestamp normalization within declared precision, payload bytes, flow identity, and diagnostics semantics.

## 6. Capture and TCP Reconstruction

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

## 7. Framing and Protocol Decoding

The framer is incremental and independent of TCP packet boundaries. It validates varints, declared lengths, marker bytes, compressed sizes, decompressed sizes, and configured maximums before allocation. Corrupt data produces a structured flow-scoped diagnostic and cannot terminate unrelated flows or the application.

One C++ `ProtocolDecoder` is the authoritative implementation. The C# game-data layer compiles opcode and layout metadata from `aion.db` into a versioned, checksummed, bounded binary protocol snapshot. The native context validates and owns a copy of that snapshot; it never opens SQLite. A new snapshot is installed only between encounters. Unknown or unproven messages are preserved as `UnknownProtocolEvent` with provenance and bounded payload diagnostics; they are not guessed into combat totals.

Typed events preserve at least:

- capture time and flow provenance;
- actor, target, skill, buff, boss, mob, content, and dungeon identifiers;
- normal damage, multi-damage, and healing as separate values;
- DoT and special-mask flags including critical, back, perfect, double, power-shard, and smite when present;
- boss HP and lifecycle signals;
- buff apply, refresh, remove, target, owner, and duration information;
- zone, combat-state, party, player, summon, and ownership changes.

Protocol-level deduplication uses stable provenance and message identity. TCP retransmissions must not create duplicate domain events.

## 8. Encounter Reduction

`EncounterReducer` is deterministic and single-threaded. It receives events in capture-time and stream-order sequence and uses an injected capture clock.

It maintains explicit state for:

- current content, dungeon, and protocol data version;
- authoritative boss identity, actor ID, mob code, HP, and maximum HP;
- players, party identities, summons, and summon ownership;
- skill-level events and separate damage, multi-damage, healing, and DoT values;
- buff windows and uptime summaries;
- encounter start, active, finishing, completed, and incomplete states.

Authoritative combat and boss lifecycle signals end encounters. Idle timeout is a documented fallback, not the primary boundary. Boss-target predicates are explicit and backed by `aion.db`; rows targeting players, adds, or unrelated mobs do not silently enter boss damage totals. Every record stores the Namter app version, `aion.db` data version, schema version, and protocol profile version.

## 9. Game Data and Local Storage

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

An encounter pins one immutable game-data snapshot. `Namter.GameData` also compiles and pins the matching native protocol snapshot, including data version, schema version, protocol profile version, opcode layouts, bounds, and a digest. A downloaded update cannot mix mappings inside an active encounter.

## 10. Update and Recovery Protocol

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

## 11. Diagnostics and Operational Limits

Diagnostics are structured and attributable to backend, capture, flow, frame, message, event, and encounter. Required counters include input packets, backend/kernel drops, invalid packets, flows, epochs, accepted bytes, duplicate bytes, overlaps, gaps, resets, frames, compressed batches, decompression failures, unknown opcodes, emitted events, native and managed queue high-water marks, dropped envelopes, ABI callback failures, and incomplete encounters.

Limits for packet size, out-of-order bytes per flow, number of live flows, frame size, decompressed batch size, queue capacity, and diagnostic payload retention are centrally configured and covered by tests. Exceeding a limit isolates the smallest affected scope and never causes unbounded allocation.

## 12. Verification Strategy

Implementation follows test-driven development. Each production behavior begins with a failing test.

Unit tests cover:

- arbitrary segmentation and coalescing;
- ordered, out-of-order, duplicate, and partially overlapping segments;
- missing gaps, epoch reset, inbound-only midstream start, tuple reuse, and sequence wraparound;
- malformed IP/TCP records, truncated captures, corrupt varints, invalid frame lengths, and corrupt LZ4;
- concurrent flow isolation and deterministic capture-clock timeouts;
- WinDivert/Npcap/PCAP adapter normalization and backend parity;
- WinDivert sniff/read-only flags, filter validation, batched receive cancellation, queue diagnostics, and driver error mapping;
- Npcap absence and incompatible-version detection, official-download guidance without automatic installation, adapter enumeration, BPF installation, immediate mode, link-type handling, cancellation, and drop statistics;
- C ABI version/size negotiation, opaque-handle lifetime, callback ownership, cancellation, and native failure mapping;
- native AddressSanitizer, UndefinedBehaviorSanitizer where supported, stress, and fuzz targets for packet, stream, frame, LZ4, protocol-snapshot, and decoder inputs;
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

## 13. External Constraints and Primary References

The capture implementation follows the currently selected upstream APIs and rechecks them before dependency upgrades:

- WinDivert 2.2 documentation: <https://reqrypt.org/windivert-doc.html>. It establishes sniff mode for non-modifying capture, administrator and signed-driver requirements, network-layer behavior, batched overlapped receive, queue-loss risk, timestamps, filtering, and documented errors.
- Npcap reference guide: <https://npcap.com/guide/npcap-api.html> and <https://npcap.com/guide/npcap-devguide.html>. It establishes the libpcap API, Windows buffer/event extensions, immediate delivery, adapter/runtime detection, and capture statistics.
- Npcap download page: <https://npcap.com/#download>. It is the only download destination Namter presents to users.
- Npcap OEM licensing: <https://npcap.com/oem/>. It establishes why Namter must not redistribute Npcap or automate silent installation under the free license; the approved product policy is stricter and forbids bundling even if licensing changes later.

WinDivert is licensed under LGPL version 3 according to its official documentation. Packaging must retain the required license notices and permit replacement of the dynamically linked library. Final distribution review must verify the exact bundled versions and license artifacts; this design does not grant redistribution rights.

## 14. Delivery Boundary

Phase 1 delivers a buildable standalone solution, the C++20 native core, user-selectable WinDivert/Npcap live backends, offline PCAP replay, Npcap external-install detection and official-download guidance, a versioned C ABI and safe C# interop layer, managed encounter and game-data libraries, `aion.db` update support, CLI replay/comparison commands, automated tests, and evidence reports for the supplied fixtures. The Namter artifact contains no Npcap binary, driver, or installer. It does not deliver the overlay or other desktop UI. Phase 2 begins only after the phase-1 contracts and golden results are accepted.
