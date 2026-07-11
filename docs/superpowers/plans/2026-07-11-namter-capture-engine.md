# Namter Capture Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone Namter phase-1 product whose C++20 core captures or replays Aion traffic, deterministically decodes combat events, and whose C# host updates `aion.db`, reduces encounters, and verifies the supplied PCAP against readable golden records.

**Architecture:** A single native DLL owns WinDivert, Npcap, PCAP, IPv4/TCP, reassembly, framing, LZ4, and protocol decoding. A narrow versioned C ABI sends immutable records to a safe C# wrapper; managed projects own signed SQLite data updates, selective caches, deterministic encounter reduction, CLI composition, and golden comparison. Native code never opens SQLite, and no A2Meter project or binary is referenced.

**Tech Stack:** Windows x64; Visual Studio 2026 MSBuild/v145; C++20; vcpkg manifest with LZ4 and GoogleTest; .NET 8; C# 12; Microsoft.Data.Sqlite; xUnit; `System.Text.Json`; `System.IO.Compression.BrotliStream`; ECDSA P-256/SHA-256; WinDivert 2.2 dynamic API; Npcap/libpcap dynamic API.

## Global Constraints

- Create `Namter.slnx`; do not add Namter projects to `A2Meter.slnx`.
- Put every new product project under `src/Namter/`; do not reference, source-link, copy, or load any A2Meter project or `PacketEngine.dll`.
- Keep capture through protocol decoding in one C++20 implementation; do not create a managed decoder fallback.
- Let users select WinDivert or Npcap explicitly; keep PCAP replay as a third offline source.
- Do not bundle, download, launch, or silently install Npcap. Only show `https://npcap.com/#download` after an explicit user action.
- Use `data/aion.db` as the local SQLite source of truth and selectively cache only bounded hot lookup sets.
- Pin one game-data/protocol snapshot for the lifetime of an encounter.
- Preserve capture timestamp and provenance through every layer; replay speed must never affect results.
- Never force bytes across an unresolved TCP gap.
- Use bounded native queues, managed channels, reassembly buffers, frames, decompressed batches, and retained diagnostic payloads.
- Follow red-green-refactor for every production behavior; watch each new test fail for the intended reason before implementation.
- Do not commit WinDivert/Npcap binaries, private signing keys, generated `aion.db`, downloaded archives, test results, or build artifacts.

## File Structure

```text
Namter.slnx
Directory.Build.props
Directory.Packages.props
vcpkg.json
src/Namter/
├─ Namter.Core.Native/
│  ├─ Namter.Core.Native.vcxproj
│  ├─ include/namter/core.h
│  └─ src/{abi,diagnostics,pcap,packet,flow,tcp,frame,lz4,protocol,engine}.cpp
├─ Namter.Core.Native.Tests/
│  ├─ Namter.Core.Native.Tests.vcxproj
│  └─ src/*_tests.cpp
├─ Namter.Core.Interop/
│  ├─ Namter.Core.Interop.csproj
│  ├─ NativeMethods.cs
│  ├─ NativeCoreHandle.cs
│  ├─ NativeCore.cs
│  └─ NativeModels.cs
├─ Namter.GameData/
│  ├─ Namter.GameData.csproj
│  ├─ GameDataManifest.cs
│  ├─ GameDataRepository.cs
│  ├─ GameDataSnapshot.cs
│  ├─ ProtocolSnapshotCompiler.cs
│  └─ GameDataUpdater.cs
├─ Namter.GameData.Builder/
│  ├─ Namter.GameData.Builder.csproj
│  └─ Program.cs
├─ Namter.GameData.Publisher/
│  ├─ Namter.GameData.Publisher.csproj
│  └─ Program.cs
├─ Namter.Encounter/
│  ├─ Namter.Encounter.csproj
│  ├─ CombatEvents.cs
│  ├─ EncounterReducer.cs
│  ├─ EncounterModels.cs
│  └─ EncounterRecordWriter.cs
├─ Namter.Cli/
│  ├─ Namter.Cli.csproj
│  ├─ Program.cs
│  ├─ Commands/{CaptureCommand,ReplayCommand,CompareCommand,DataCommand}.cs
│  └─ appsettings.json
├─ Namter.Tests.Unit/
│  ├─ Namter.Tests.Unit.csproj
│  └─ {Interop,GameData,Encounter}/*Tests.cs
└─ Namter.Tests.Integration/
   ├─ Namter.Tests.Integration.csproj
   ├─ Golden/*Tests.cs
   └─ Fixtures/ReadableFixtureLoader.cs
db/
├─ schema/001_initial.sql
└─ seed/golden_protocol.sql
```

## Interface Map

| Producer | Contract | Consumer |
|---|---|---|
| WinDivert/Npcap/PCAP adapters | native `CaptureRecord` | packet normalizer |
| TCP reassembler | ordered `StreamChunk` or `GapEvent` | framer |
| protocol decoder | `nm_event_v1` through C callback | `Namter.Core.Interop` |
| `Namter.GameData` | checksummed binary `ProtocolSnapshotV1` | native `nm_core_set_protocol_snapshot` |
| `Namter.Core.Interop` | immutable `CombatEvent` | `EncounterReducer` |
| `EncounterReducer` | `EncounterUpdate` / `EncounterRecord` | CLI now, UI in phase 2 |

---

### Task 1: Scaffold the Standalone Solution and Prove Native/Managed Builds

**Files:**
- Create: `Namter.slnx`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `vcpkg.json`
- Create: all project files listed in **File Structure**
- Create: `src/Namter/Namter.Core.Native/include/namter/core.h`
- Create: `src/Namter/Namter.Core.Native/src/abi.cpp`
- Test: `src/Namter/Namter.Core.Native.Tests/src/abi_tests.cpp`
- Test: `src/Namter/Namter.Tests.Unit/Interop/NativeLoadTests.cs`

**Interfaces:**
- Produces: `uint32_t nm_core_abi_version(void)` returning `1`.
- Produces: `NativeMethods.nm_core_abi_version()` returning `uint`.

- [ ] **Step 1: Create the solution and projects without A2Meter references**

Use this solution root and project membership:

```xml
<Solution>
  <Folder Name="/native/">
    <Project Path="src/Namter/Namter.Core.Native/Namter.Core.Native.vcxproj" />
    <Project Path="src/Namter/Namter.Core.Native.Tests/Namter.Core.Native.Tests.vcxproj" />
  </Folder>
  <Folder Name="/managed/">
    <Project Path="src/Namter/Namter.Core.Interop/Namter.Core.Interop.csproj" />
    <Project Path="src/Namter/Namter.GameData/Namter.GameData.csproj" />
    <Project Path="src/Namter/Namter.GameData.Builder/Namter.GameData.Builder.csproj" />
    <Project Path="src/Namter/Namter.GameData.Publisher/Namter.GameData.Publisher.csproj" />
    <Project Path="src/Namter/Namter.Encounter/Namter.Encounter.csproj" />
    <Project Path="src/Namter/Namter.Cli/Namter.Cli.csproj" />
    <Project Path="src/Namter/Namter.Tests.Unit/Namter.Tests.Unit.csproj" />
    <Project Path="src/Namter/Namter.Tests.Integration/Namter.Tests.Integration.csproj" />
  </Folder>
</Solution>
```

Set all C# projects to `net8.0-windows`, nullable enabled, implicit usings enabled, x64, and warnings as errors. Set the native projects to x64, `v145`, `stdcpp20`, `/W4`, `/WX`, `/permissive-`, SDL checks, control-flow guard, and vcpkg manifest mode. Put `lz4` and `gtest` in the root `vcpkg.json`.

- [ ] **Step 2: Write failing ABI tests**

```cpp
TEST(Abi, ReportsVersionOne) {
    EXPECT_EQ(nm_core_abi_version(), 1u);
}
```

```csharp
[Fact]
public void NativeLibrary_reports_supported_abi() =>
    Assert.Equal(1u, NativeMethods.nm_core_abi_version());
```

- [ ] **Step 3: Run the tests and verify RED**

Run:

```powershell
msbuild Namter.slnx -m -p:Configuration=Debug -p:Platform=x64
dotnet test src/Namter/Namter.Tests.Unit/Namter.Tests.Unit.csproj -c Debug --no-restore
```

Expected: native link and managed test fail because `nm_core_abi_version` is not implemented/exported.

- [ ] **Step 4: Add the minimum exported ABI**

```cpp
#if defined(_WIN32)
#define NM_API extern "C" __declspec(dllexport)
#else
#define NM_API extern "C"
#endif

NM_API uint32_t nm_core_abi_version(void);
```

```cpp
uint32_t nm_core_abi_version(void) noexcept { return 1u; }
```

```csharp
[LibraryImport("Namter.Core.Native", EntryPoint = "nm_core_abi_version")]
internal static partial uint nm_core_abi_version();
```

- [ ] **Step 5: Build and verify GREEN**

Run the native GoogleTest executable, the managed unit project, and `rg -n "A2Meter|PacketEngine" Namter.slnx src/Namter`. Expected: both tests pass and the repository-reference search returns no matches.

- [ ] **Step 6: Commit**

Commit with intent `Establish an independent Namter build boundary` and Lore trailers documenting x64, C++20, .NET 8, and zero A2Meter references.

---

### Task 2: Define the Versioned C ABI and Safe Managed Lifetime

**Files:**
- Modify: `src/Namter/Namter.Core.Native/include/namter/core.h`
- Modify: `src/Namter/Namter.Core.Native/src/abi.cpp`
- Create: `src/Namter/Namter.Core.Native/src/engine.cpp`
- Create: `src/Namter/Namter.Core.Interop/NativeCoreHandle.cs`
- Create: `src/Namter/Namter.Core.Interop/NativeCore.cs`
- Create: `src/Namter/Namter.Core.Interop/NativeModels.cs`
- Modify: `src/Namter/Namter.Core.Interop/NativeMethods.cs`
- Test: `src/Namter/Namter.Core.Native.Tests/src/abi_tests.cpp`
- Test: `src/Namter/Namter.Tests.Unit/Interop/NativeLifetimeTests.cs`

**Interfaces:**
- Produces: opaque `nm_core_handle` and create/set/start/stop/diagnostics/destroy functions.
- Produces: `NativeCore : IAsyncDisposable` with `SetProtocolSnapshot(ReadOnlySpan<byte>)`, `ReplayAsync`, and `CaptureAsync`.

- [ ] **Step 1: Write native RED tests for ABI validation and idempotent stop/destroy**

Test that create rejects wrong `abi_version`, short `struct_size`, null callback table, and null out-handle; test that stop can be called twice and that destroying a stopped handle leaks no contexts.

```cpp
nm_core_config_v1 config{.abi_version = 99, .struct_size = sizeof(config)};
nm_core_handle* handle = nullptr;
EXPECT_EQ(nm_core_create(&config, &callbacks, &handle), NM_STATUS_ABI_MISMATCH);
EXPECT_EQ(handle, nullptr);
```

- [ ] **Step 2: Run native tests and verify RED**

Expected: compile failures for missing structures and functions.

- [ ] **Step 3: Implement the exact ABI surface**

```cpp
typedef struct nm_core_handle nm_core_handle;
typedef struct nm_event_v1 nm_event_v1;
typedef struct nm_diagnostic_v1 nm_diagnostic_v1;

typedef struct nm_core_config_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t native_queue_capacity;
    uint32_t max_live_flows;
    uint32_t max_ooo_bytes_per_flow;
    uint32_t max_frame_bytes;
    uint32_t max_decompressed_bytes;
} nm_core_config_v1;

typedef void (__cdecl *nm_event_callback_v1)(void* user, const nm_event_v1* event);
typedef void (__cdecl *nm_diagnostic_callback_v1)(void* user, const nm_diagnostic_v1* diagnostic);

typedef struct nm_callbacks_v1 {
    uint32_t abi_version;
    uint32_t struct_size;
    void* user;
    nm_event_callback_v1 event_callback;
    nm_diagnostic_callback_v1 diagnostic_callback;
} nm_callbacks_v1;

NM_API nm_status nm_core_create(const nm_core_config_v1*, const nm_callbacks_v1*, nm_core_handle**);
NM_API nm_status nm_core_set_protocol_snapshot(nm_core_handle*, const uint8_t*, size_t);
NM_API nm_status nm_core_start(nm_core_handle*, const nm_source_config_v1*);
NM_API nm_status nm_core_stop(nm_core_handle*);
NM_API nm_status nm_core_get_diagnostics(nm_core_handle*, nm_diagnostics_v1*);
NM_API void nm_core_destroy(nm_core_handle*);
```

Catch every C++ exception at each export and map it to `NM_STATUS_INTERNAL_ERROR`. Never invoke callbacks while holding engine locks.

- [ ] **Step 4: Write managed RED tests for SafeHandle and callback lifetime**

Verify the handle releases exactly once, callbacks remain rooted during forced GC, exceptions in a managed callback are captured and converted to an incomplete-stream diagnostic, and cancellation calls native stop.

- [ ] **Step 5: Implement `LibraryImport`, `SafeHandle`, and callback bridge**

Use `UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])` static callbacks, a `GCHandle` user token, and copy every pointer/length view before the native callback returns. Expose only immutable managed records.

- [ ] **Step 6: Run native and managed tests and verify GREEN**

Expected: ABI validation, lifetime, callback-rooting, and cancellation tests pass with zero native leaks under the Visual Studio native leak checker in Debug.

- [ ] **Step 7: Commit**

Commit with intent `Make the native boundary explicit and failure-safe`.

---

### Task 3: Normalize PCAP and Live Packets into One Capture Contract

**Files:**
- Create: `src/Namter/Namter.Core.Native/src/pcap.cpp`
- Create: `src/Namter/Namter.Core.Native/src/packet.cpp`
- Create: `src/Namter/Namter.Core.Native/src/capture_record.hpp`
- Test: `src/Namter/Namter.Core.Native.Tests/src/pcap_tests.cpp`
- Test: `src/Namter/Namter.Core.Native.Tests/src/packet_tests.cpp`

**Interfaces:**
- Produces: `CaptureRecord { source, timestamp_ns, link_type, captured_length, original_length, bytes }`.
- Produces: `TcpSegment { FlowTuple, sequence, flags, payload, CaptureProvenance }`.

- [ ] **Step 1: Write RED tests against `captures/aion2_part001.pcap`**

Assert classic little-endian PCAP 2.4, snaplen 65,535, link type 101, 20,849 records, monotonic first/last capture times spanning 503.320 seconds, and no truncated records. Add synthetic tests for big-endian PCAP, nanosecond timestamps, truncated record headers, captured length above snaplen, RAW IPv4, and Ethernet IPv4.

- [ ] **Step 2: Run the focused native tests and verify RED**

Expected: missing `PcapReader` and `PacketNormalizer` failures.

- [ ] **Step 3: Implement bounded PCAP parsing and link normalization**

Read integers with explicit endianness, reject record lengths above configured maximum before allocation, convert timestamps with checked arithmetic, and retain original file offset. Support link types `DLT_EN10MB = 1` and `DLT_RAW = 101` in phase 1; emit `unsupported_link_type` for others.

- [ ] **Step 4: Implement strict IPv4/TCP validation**

Validate IPv4 version/IHL/total length/protocol and TCP data offset before constructing spans. Preserve ACK-only and FIN/RST segments. Do not require SYN, payload, client direction, or valid checksum because hardware offload can leave checksum fields unset at live capture boundaries.

- [ ] **Step 5: Verify GREEN and fixture counts**

Expected: 20,849 TCP packets, two directional tuples, server source port 13,328, and zero fixture truncations.

- [ ] **Step 6: Commit**

Commit with intent `Normalize every capture source before protocol work`.

---

### Task 4: Build Loss-Aware Flow Epochs and TCP Reassembly

**Files:**
- Create: `src/Namter/Namter.Core.Native/src/flow.cpp`
- Create: `src/Namter/Namter.Core.Native/src/tcp.cpp`
- Create: `src/Namter/Namter.Core.Native/src/sequence.hpp`
- Test: `src/Namter/Namter.Core.Native.Tests/src/flow_tests.cpp`
- Test: `src/Namter/Namter.Core.Native.Tests/src/tcp_tests.cpp`

**Interfaces:**
- Consumes: validated `TcpSegment`.
- Produces: ordered `StreamChunk` and explicit `StreamReset`/`GapObserved` diagnostics.

- [ ] **Step 1: Write table-driven RED tests**

Cover in-order data, ACK-only, full retransmit, prefix/suffix overlap, out-of-order fill, unresolved gap, capture-time expiry, FIN/RST, tuple reuse, inbound-only midstream start, two concurrent flows, per-flow byte cap, flow-count cap, and sequence wrap from `0xfffffff8` to `0x00000008`.

```cpp
INSTANTIATE_TEST_SUITE_P(OverlapCases, TcpReassemblerTest, Values(
    Case{{seg(100,"abcdef"), seg(102,"cdef")}, "abcdef", 4},
    Case{{seg(100,"abcdef"), seg(104,"efgh")}, "abcdefgh", 2}
));
```

- [ ] **Step 2: Run focused tests and verify RED**

Expected: missing `FlowTracker`, serial arithmetic, and reassembler failures.

- [ ] **Step 3: Implement RFC-style serial comparison and interval storage**

Use signed 32-bit sequence differences only within bounded windows, store non-overlapping byte intervals ordered by unwrapped sequence, trim duplicates before insertion, and emit only the contiguous prefix. Make every timeout take capture time as an argument.

- [ ] **Step 4: Implement epoch transitions without fabricated continuity**

Create an epoch on first observed segment; close it on RST, completed FIN, configured capture-time idle expiry, or incompatible tuple reuse. If a gap expires, emit reset/diagnostics and start a new decoder epoch at the next observed range; never append the ranges across the hole.

- [ ] **Step 5: Verify GREEN and the golden overlap count**

Run all native tests plus PCAP flow diagnostics. Expected: two flows, six overlaps, exactly 66 duplicate bytes removed, and zero unresolved byte gaps.

- [ ] **Step 6: Commit**

Commit with intent `Preserve stream truth instead of guessing across gaps`.

---

### Task 5: Implement Incremental Framing and Bounded LZ4 Expansion

**Files:**
- Create: `src/Namter/Namter.Core.Native/src/frame.cpp`
- Create: `src/Namter/Namter.Core.Native/src/lz4.cpp`
- Create: `src/Namter/Namter.Core.Native/src/varint.hpp`
- Test: `src/Namter/Namter.Core.Native.Tests/src/frame_tests.cpp`
- Test: `src/Namter/Namter.Core.Native.Tests/src/lz4_tests.cpp`

**Interfaces:**
- Consumes: ordered `StreamChunk` within one epoch.
- Produces: bounded `ProtocolMessage` with capture provenance.

- [ ] **Step 1: Write RED tests for every segmentation boundary**

Generate one valid message and split it at every byte; coalesce multiple messages; add optional `0xF0..0xFE` marker cases; encode the `0xFF 0xFF + little-endian int32 decompressed-size + LZ4 block` batch; reject overlong varints, negative/oversized lengths, short LZ4 headers, decompression mismatch, and output beyond configured maximum.

- [ ] **Step 2: Run focused tests and verify RED**

Expected: missing incremental framer and LZ4 expansion failures.

- [ ] **Step 3: Implement the frame state machine**

Keep parse state as `NeedLength`, `NeedBody`, or `NeedResync`. Consume at most five bytes for a 32-bit varint, use checked size arithmetic, never allocate before the declared size passes bounds, and attach the timestamp of the first contributing byte plus the last contributing byte.

- [ ] **Step 4: Implement LZ4 using the vcpkg `lz4` dependency**

Call `LZ4_decompress_safe`, require the return value to equal the declared decompressed size, and feed decompressed bytes to a nested bounded message iterator. A bad batch resets only its decoder epoch and emits one structured diagnostic.

- [ ] **Step 5: Verify GREEN and fuzz entry points**

Add `LLVMFuzzerTestOneInput`-compatible functions for varint/frame/LZ4 parsing even when the MSVC CI job initially runs them as corpus tests. Expected: all boundary permutations pass and no input causes unbounded allocation.

- [ ] **Step 6: Commit**

Commit with intent `Decode frames independently of TCP packet shape`.

---

### Task 6: Create `aion.db`, Selective Caches, and the Native Protocol Snapshot

**Files:**
- Create: `db/schema/001_initial.sql`
- Create: `db/seed/golden_protocol.sql`
- Create: `src/Namter/Namter.GameData/GameDataRepository.cs`
- Create: `src/Namter/Namter.GameData/GameDataSnapshot.cs`
- Create: `src/Namter/Namter.GameData/ProtocolSnapshotCompiler.cs`
- Create: `src/Namter/Namter.GameData.Builder/Program.cs`
- Test: `src/Namter/Namter.Tests.Unit/GameData/GameDataRepositoryTests.cs`
- Test: `src/Namter/Namter.Tests.Unit/GameData/ProtocolSnapshotCompilerTests.cs`
- Test: `src/Namter/Namter.Core.Native.Tests/src/protocol_snapshot_tests.cpp`

**Interfaces:**
- Produces: immutable `GameDataSnapshot` with `DataVersion`, `SchemaVersion`, `ProtocolProfileVersion`, and frozen hot maps.
- Produces: binary `ProtocolSnapshotV1` accepted by native `nm_core_set_protocol_snapshot`.

- [ ] **Step 1: Write RED tests for schema and cache bounds**

Require tables `metadata`, `protocol_profiles`, `opcodes`, `message_layouts`, `bosses`, `dungeons`, `dungeon_bosses`, `mobs`, `skills`, and `buffs`; require foreign keys and indexes for profile/name/code lookups. Assert that hot caches load only active profile opcodes, bosses, dungeons, skills, and buffs and stay below a configured entry count.

- [ ] **Step 2: Write the initial SQL schema and golden seed**

Seed profile `aion2-2026-07-10` with packet magic `06 00 36`, server port `13328`, current observed tags (`04 38`, `05 38`, `2A 38`, `2B 38`, `33 36`, `45 36`, `41 36`, `01 8D`, `03 36`, `21 8D`, `4F 36`), party marker `97`, and party operations `01,02,04,07,0B,13,1D,2A`. Seed content `600153`, Turgen mob code `2301721`, Griosa mob code `2301722`, and Basilus mob code `2301723` from readable metadata. Actor IDs `18804`, `36737`, and `28353` are encounter-scoped observations and belong in golden assertions, not persistent boss identity rows. Treat every seeded value as versioned data, not a C++ constant.

- [ ] **Step 3: Run managed repository tests and verify RED**

Expected: missing builder/repository/snapshot types.

- [ ] **Step 4: Implement builder and read-only repository**

The builder accepts `--output <path> --schema db/schema/001_initial.sql --seed db/seed/golden_protocol.sql`, creates a temporary SQLite DB, enables foreign keys, applies one transaction, runs `PRAGMA integrity_check`, and atomically moves the result. The repository opens `Mode=ReadOnly;Cache=Shared`, reads one transaction, and materializes frozen dictionaries.

- [ ] **Step 5: Specify and compile `ProtocolSnapshotV1`**

Use a little-endian format:

```text
magic "NMPS" (4)
formatVersion u16 = 1
headerSize u16
totalSize u32
crc32 u32
dataVersion u64
profileVersion u32
packetMagicLength u16 + bytes
serverPortCount u16 + u16[]
opcodeCount u32 + repeated { kind u16, tagLength u16, tag bytes, layoutId u32 }
layoutCount u32 + repeated bounded field descriptors
```

Sort rows by numeric key so identical DB content produces identical bytes. Validate all offsets/counts and CRC in C++ before replacing the active snapshot.

- [ ] **Step 6: Run managed/native tests and verify GREEN**

Expected: deterministic snapshot bytes, rejection of wrong magic/version/size/CRC/counts, and no SQLite dependency in the native project.

- [ ] **Step 7: Commit**

Commit with intent `Move mutable protocol knowledge into aion.db`.

---

### Task 7: Implement Signed, Atomic `aion.db` Updates and Rollback

**Files:**
- Create: `src/Namter/Namter.GameData/GameDataManifest.cs`
- Create: `src/Namter/Namter.GameData/GameDataUpdater.cs`
- Create: `src/Namter/Namter.GameData/IGameDataTransport.cs`
- Create: `src/Namter/Namter.GameData/HttpGameDataTransport.cs`
- Create: `src/Namter/Namter.GameData.Publisher/Namter.GameData.Publisher.csproj`
- Create: `src/Namter/Namter.GameData.Publisher/Program.cs`
- Test: `src/Namter/Namter.Tests.Unit/GameData/GameDataUpdaterTests.cs`
- Test: `src/Namter/Namter.Tests.Unit/GameData/GameDataPublisherTests.cs`

**Interfaces:**
- Produces: `CheckAsync(Uri manifestUri, DataVersion current, CancellationToken)`.
- Produces: `StageAsync`, `ActivateWhenIdleAsync`, and `RollbackAsync` results with explicit status codes.
- Produces: a static-server-ready `aion.db.br` plus canonical signed `manifest.json`; uploading/deploying those files remains an external operation.

- [ ] **Step 1: Write RED tests for the full update matrix**

Cover same version/no archive request, newer valid update, cancelled download, length mismatch, SHA-256 mismatch, invalid ECDSA P-256 signature, Brotli failure, SQLite integrity failure, missing required table, incompatible schema/minimum app version, atomic replacement, backup retention, activation deferred during encounter, and rollback after failed reopen.

- [ ] **Step 2: Define canonical manifest serialization**

```csharp
public sealed record GameDataManifest(
    ulong DataVersion,
    uint SchemaVersion,
    uint ProtocolProfileVersion,
    Version MinimumAppVersion,
    Uri ArchiveUri,
    long CompressedSize,
    long UncompressedSize,
    string Sha256,
    string Compression,
    DateTimeOffset CreatedUtc,
    string Signature);
```

Sign the UTF-8 bytes of the JSON object excluding `Signature`, with properties written in the declared order, using ECDSA P-256/SHA-256. Tests generate ephemeral keys; production imports only a public SubjectPublicKeyInfo value from trusted application configuration. No private key enters the runtime project.

- [ ] **Step 3: Run focused tests and verify RED**

Expected: missing updater and validation pipeline.

- [ ] **Step 4: Implement stage-validate-activate**

Stream the archive to `data/.update/aion.db.br.part`, enforce compressed size while hashing, verify signature before decompression, enforce uncompressed size while writing `aion.db.candidate`, open candidate read-only, run integrity/schema checks, then stage it. On activation, move current DB to `backup/aion.previous.db` and atomically move the candidate to `aion.db`; reopen and rebuild caches before deleting transient state.

- [ ] **Step 5: Verify GREEN and filesystem cleanup**

Expected: every failure leaves the active DB byte-for-byte unchanged and removes `.part`/candidate files; successful activation retains exactly one previous validated DB.

- [ ] **Step 6: Implement and test the offline publisher**

Expose this local command:

```text
namter-data-publisher --input <aion.db> --output <directory> --archive-uri <https-uri> --data-version <ulong> --minimum-app-version <version> --private-key <pem-file>
```

The publisher validates the input DB, Brotli-compresses it to `aion.db.br`, computes compressed/uncompressed lengths and SHA-256, writes the canonical unsigned manifest, signs it with an ECDSA P-256 PEM private key, and writes `manifest.json`. It refuses non-HTTPS archive URIs outside tests, refuses an output path inside the source tree, overwrites nothing without `--force`, and never prints or copies the private key. Tests publish into a temporary directory, verify the manifest with the corresponding public key, and run the runtime updater against an in-memory HTTP transport. The tool does not upload or mutate any external server.

- [ ] **Step 7: Commit**

Commit with intent `Keep game-data updates verifiable and reversible`.

---

### Task 8: Decode Versioned Protocol Messages into Native Events

**Files:**
- Create: `src/Namter/Namter.Core.Native/src/protocol.cpp`
- Create: `src/Namter/Namter.Core.Native/src/event.hpp`
- Modify: `src/Namter/Namter.Core.Native/include/namter/core.h`
- Test: `src/Namter/Namter.Core.Native.Tests/src/protocol_tests.cpp`
- Create: `src/Namter/Namter.Encounter/CombatEvents.cs`
- Test: `src/Namter/Namter.Tests.Unit/Interop/EventMappingTests.cs`

**Interfaces:**
- Consumes: `ProtocolMessage` plus validated `ProtocolSnapshotV1`.
- Produces: `nm_event_v1` variants and equivalent immutable managed `CombatEvent` records.

- [ ] **Step 1: Define ABI event types and write RED mapping tests**

Define event kinds for damage, DoT, buff, self/other actor, mob spawn, boss HP, entity removal, party, dungeon/content, combat state, and unknown protocol. Use fixed-width fields plus pointer/length UTF-8 names. Damage carries separate `damage`, `multi_damage`, `healing`, `special_mask`, and `is_dot` fields.

- [ ] **Step 2: Add native RED fixtures**

Extract the smallest complete frame bytes for each known event kind from the golden PCAP into source-code byte arrays with packet offsets in comments. Assert every decoded field, not merely event kind. Add truncation at every byte and mutated-length tests; none may read out of bounds or emit a partial typed event.

- [ ] **Step 3: Run focused native tests and verify RED**

Expected: no dispatcher exists.

- [ ] **Step 4: Implement table-driven dispatch and bounded readers**

Lookup tag and layout by the active snapshot. Use a `SpanReader`-style C++ cursor with checked `read_u8`, `read_le16/32/64`, `read_var_u32`, `read_utf8`, `skip`, and `remaining`. Put each event parser in a focused function returning `expected<Event, DecodeError>`. Emit unknown events for unknown tags and diagnostics for known tags with invalid layouts.

- [ ] **Step 5: Map ABI events to managed records**

Use a closed C# hierarchy such as `DamageEvent`, `BuffEvent`, `ActorObservedEvent`, `MobSpawnedEvent`, `BossHpEvent`, `PartyEvent`, `ContentEvent`, and `UnknownProtocolEvent`. Copy UTF-8 and retained diagnostic bytes inside the callback.

- [ ] **Step 6: Verify GREEN and deduplication**

Feed repeated TCP segments and assert one event ledger; feed two distinct protocol messages with equal semantic fields and assert both remain. Deduplication identity must come from stream provenance, not damage values.

- [ ] **Step 7: Commit**

Commit with intent `Make one versioned native decoder authoritative`.

---

### Task 9: Add User-Selectable WinDivert and Npcap Backends

**Files:**
- Create: `src/Namter/Namter.Core.Native/src/windivert.cpp`
- Create: `src/Namter/Namter.Core.Native/src/npcap.cpp`
- Create: `src/Namter/Namter.Core.Native/src/dynamic_library.hpp`
- Modify: `src/Namter/Namter.Core.Native/src/engine.cpp`
- Test: `src/Namter/Namter.Core.Native.Tests/src/windivert_tests.cpp`
- Test: `src/Namter/Namter.Core.Native.Tests/src/npcap_tests.cpp`
- Test: `src/Namter/Namter.Core.Native.Tests/src/backend_parity_tests.cpp`

**Interfaces:**
- Consumes: `nm_source_config_v1` with explicit `WINDIVERT`, `NPCAP`, or `PCAP` kind.
- Produces: normalized `CaptureRecord` and backend diagnostics.

- [ ] **Step 1: Write RED tests using injected API tables**

Wrap both C APIs behind function-pointer tables so tests simulate missing DLL, missing symbol, open failure, receive cancellation, packet delivery, and drop stats without drivers. Assert WinDivert flags are exactly `SNIFF | RECV_ONLY`, layer is `NETWORK`, and no send/reinject function is called. Assert Npcap compiles/applies BPF, enables immediate mode, sizes buffers, preserves link type, and reports `pcap_stats` drops.

- [ ] **Step 2: Run focused native tests and verify RED**

Expected: missing adapters and source selection.

- [ ] **Step 3: Implement WinDivert dynamic loading**

Load only matching x64 `WinDivert.dll`, resolve required 2.2 exports, precompile/validate the selective TCP filter, open sniff/read-only, configure queue length/size/time within documented bounds, and use batched overlapped `WinDivertRecvEx`. Map documented Windows errors to stable Namter diagnostics. Ship WinDivert license notices and official signed x64 driver/DLL only in release packaging, never in source control.

- [ ] **Step 4: Implement Npcap external-runtime loading**

Search approved Npcap runtime locations with safe DLL search flags, call `pcap_lib_version`, reject legacy WinPcap, enumerate adapters, create/activate a handle with immediate mode and buffers, compile/apply BPF, capture with cancellation, and sample stats. Do not implement installer download or launch APIs. Return `NM_STATUS_NPCAP_NOT_INSTALLED` with the official URL as structured help metadata.

- [ ] **Step 5: Verify backend parity**

Feed identical normalized IP packets through injected WinDivert and Npcap callbacks and PCAP input. Expected: equivalent payload bytes, direction, flow tuple, and ordered events; only backend/version/timestamp-precision diagnostic fields may differ.

- [ ] **Step 6: Commit**

Commit with intent `Give users capture choice without changing semantics`.

---

### Task 10: Reduce Immutable Events into Deterministic Encounters

**Files:**
- Create: `src/Namter/Namter.Encounter/EncounterModels.cs`
- Create: `src/Namter/Namter.Encounter/EncounterReducer.cs`
- Create: `src/Namter/Namter.Encounter/EncounterRecordWriter.cs`
- Test: `src/Namter/Namter.Tests.Unit/Encounter/EncounterReducerTests.cs`
- Test: `src/Namter/Namter.Tests.Unit/Encounter/BuffWindowTests.cs`

**Interfaces:**
- Consumes: ordered `CombatEvent` plus pinned `GameDataSnapshot`.
- Produces: `EncounterUpdate` and immutable final `EncounterRecord`.

- [ ] **Step 1: Write RED reducer transition tests**

Cover idle→active on authoritative boss damage/combat state, boss HP updates, participant identity enrichment, summon ownership, separate damage/multi/heal/DoT values, flags, buff apply/refresh/remove/close, add/player target exclusion, boss death completion, content exit completion, idle fallback, incomplete capture, and no state mutation after completion.

- [ ] **Step 2: Write the public immutable models**

```csharp
public sealed record EncounterRecord(
    Guid Id,
    EncounterIdentity Encounter,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsComplete,
    ImmutableArray<ParticipantRecord> Participants,
    ImmutableArray<DamageRecord> Events,
    ImmutableArray<BuffWindowRecord> BuffWindows,
    DataProvenance Provenance);
```

Use integer milliseconds from capture timestamps for internal ordering and derive wall-clock timestamps only at serialization boundaries.

- [ ] **Step 3: Run focused tests and verify RED**

Expected: missing reducer and models.

- [ ] **Step 4: Implement a pure ordered reducer**

Keep mutable state private to one reducer instance and one consumer loop. Every `Apply` returns zero or more immutable updates. Boss predicates come from the pinned data snapshot. Idle completion advances only when a later capture timestamp or explicit end-of-input flush is applied.

- [ ] **Step 5: Implement deterministic JSON records**

Write UTF-8 JSON with stable property order, invariant numbers, sorted definitions, and event order preserved. Include app, ABI, data, schema, profile, backend, capture, and completeness provenance.

- [ ] **Step 6: Verify GREEN**

Expected: reducer unit tests pass and serializing the same input twice yields identical bytes except when a caller explicitly supplies a different record ID.

- [ ] **Step 7: Commit**

Commit with intent `Make encounter results a deterministic event reduction`.

---

### Task 11: Build the CLI, Npcap Guidance, and Golden Comparator

**Files:**
- Create: `src/Namter/Namter.Cli/Program.cs`
- Create: `src/Namter/Namter.Cli/Commands/ReplayCommand.cs`
- Create: `src/Namter/Namter.Cli/Commands/CaptureCommand.cs`
- Create: `src/Namter/Namter.Cli/Commands/CompareCommand.cs`
- Create: `src/Namter/Namter.Cli/Commands/DataCommand.cs`
- Create: `src/Namter/Namter.Cli/appsettings.json`
- Create: `src/Namter/Namter.Tests.Integration/Fixtures/ReadableFixtureLoader.cs`
- Test: `src/Namter/Namter.Tests.Integration/Golden/GoldenComparisonTests.cs`

**Interfaces:**
- Produces commands `replay`, `capture`, `compare`, `data status`, `data check`, and `data rollback`.
- Produces JSON comparison report with matches, tolerances, missing/extra events, and provenance.

- [ ] **Step 1: Write RED command tests**

Require:

```text
namter replay --input <file-or-dir> --data <aion.db> --output <dir> [--speed 0|1|10]
namter capture --backend windivert|npcap --data <aion.db> --output <dir>
namter compare --actual <record.json> --expected <readable-dir> --report <report.json>
namter data status|check|rollback --data-dir <dir>
```

Assert that selecting absent Npcap returns a dedicated exit code, prints the official URL, never starts a process/browser automatically, and leaves WinDivert/PCAP commands usable.

- [ ] **Step 2: Run integration tests and verify RED**

Expected: missing command composition and fixture loader.

- [ ] **Step 3: Implement composition and bounded channels**

Load/validate `aion.db`, pin the managed and native snapshots, create `NativeCore`, start the chosen explicit source, copy callbacks into a bounded `Channel<CombatEvent>`, consume on one reducer task, flush at end-of-input, then write event ledger, encounter record, diagnostics, and comparison report.

Use these configuration keys: `DataDirectory`, `GameDataManifestUri`, `GameDataPublicKeySpki`, `NativeQueueCapacity`, `ManagedQueueCapacity`, `MaxLiveFlows`, `MaxOutOfOrderBytesPerFlow`, `MaxFrameBytes`, and `MaxDecompressedBytes`. Environment variables `NAMTER_GAMEDATA_MANIFEST_URI` and `NAMTER_GAMEDATA_PUBLIC_KEY_SPKI` override the two deployment values. If either deployment value is absent, local `aion.db` remains usable but remote checking reports `NotConfigured`; tests supply an HTTPS loopback transport and a generated public key. A production release gate must inject both values without committing a private key.

- [ ] **Step 4: Implement readable fixture loading and comparison**

Read `summary.txt`, `participants.csv`, `events.csv`, `buff-windows.csv`, and `buff-uptimes.csv` with strict headers and invariant parsing. Compare boss/content IDs, start/end, participants, total boss damage, per-actor/skill damage, healing, DoT, flags, and buff windows. Never treat readable JSON `eventCount` as damage-row count because it includes heterogeneous logical rows.

- [ ] **Step 5: Verify GREEN for command behavior**

Expected: help text is stable; invalid input has distinct exit codes; absent Npcap only guides; replay creates deterministic artifacts; comparison discrepancies are data, not swallowed warnings.

- [ ] **Step 6: Commit**

Commit with intent `Expose capture correctness through reproducible CLI artifacts`.

---

### Task 12: Close the Golden PCAP, Stress, Security, and Packaging Gates

**Files:**
- Create: `src/Namter/Namter.Tests.Integration/Golden/AionPart001Tests.cs`
- Create: `src/Namter/Namter.Tests.Integration/Golden/ReplayDeterminismTests.cs`
- Create: `src/Namter/Namter.Core.Native.Tests/src/stress_tests.cpp`
- Create: `src/Namter/Namter.Core.Native.Tests/src/fuzz_corpus_tests.cpp`
- Create: `src/Namter/Namter.Cli/THIRD-PARTY-NOTICES.txt`
- Create: `docs/NAMTER_CAPTURE_OPERATIONS.md`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: all phase-1 projects and supplied fixtures.
- Produces: final validation evidence and a clean x64 release artifact.

- [ ] **Step 1: Write the final RED golden assertions**

Assert `aion2_part001.pcap` yields exactly two matching encounters in chronological order: Turgen (`actorId=18804`, `mobCode=2301721`, `contentCode=600153`, `totalDamage=230291779`) and Griosa (`actorId=36737`, `mobCode=2301722`, `contentCode=600153`, `totalDamage=229795893`). Assert five participants each, zero TCP gaps, six overlaps, 66 duplicate bytes, zero unexplained invalid frames, and a comparison report for every non-identical field. Add Basilus (`actorId=28353`, `mobCode=2301723`, `contentCode=600153`, `totalDamage=354172044`) reducer-semantic assertions without claiming it came from the PCAP.

- [ ] **Step 2: Add replay-determinism and hostile-input tests**

Replay at speeds 0, 1, and 10; re-chunk the same byte streams at every boundary; permute within bounded out-of-order windows; duplicate ranges; truncate files; mutate frame/LZ4/snapshot bytes; push flow/queue limits. Expected: equivalent ledgers for semantically identical inputs and explicit incomplete/failure results otherwise.

- [ ] **Step 3: Run the full verification matrix**

Run:

```powershell
msbuild Namter.slnx -m -p:Configuration=Debug -p:Platform=x64
src\Namter\Namter.Core.Native.Tests\x64\Debug\Namter.Core.Native.Tests.exe
dotnet test src\Namter\Namter.Tests.Unit\Namter.Tests.Unit.csproj -c Debug --no-build
dotnet test src\Namter\Namter.Tests.Integration\Namter.Tests.Integration.csproj -c Debug --no-build
msbuild Namter.slnx -m -p:Configuration=Release -p:Platform=x64
```

Expected: zero failures, warnings-as-errors clean, and deterministic artifact hashes for repeated replay outputs.

- [ ] **Step 4: Audit dependency and product boundaries**

Run searches proving no A2Meter/PacketEngine reference, no Npcap runtime/installer, no private key, and no generated DB/artifact is tracked. Verify WinDivert notices and replaceable dynamically linked files in the release layout. Record the exact WinDivert/LZ4/GoogleTest/Microsoft.Data.Sqlite versions in `THIRD-PARTY-NOTICES.txt`.

- [ ] **Step 5: Write operator documentation**

Document administrator requirements for WinDivert, external Npcap installation and explicit selection, backend diagnostics, `aion.db` update/rollback, capture-incomplete warnings, replay/comparison commands, and the phase-1 UI non-goal.

- [ ] **Step 6: Commit**

Commit with intent `Prove Namter phase one against real and hostile traffic` and Lore trailers listing the exact full verification evidence and any remaining golden differences.

---

## Completion Gate

Do not claim phase 1 complete until all of the following are freshly verified:

- `Namter.slnx` builds Debug and Release x64 from a clean dependency restore.
- Every native and managed unit/integration test passes.
- The supplied PCAP yields exactly Turgen and Griosa, with every field compared to readable fixtures.
- Six overlaps and 66 duplicate bytes are reported; no gap is fabricated.
- Replay speed and stream chunking do not change event or encounter results.
- Npcap is absent from the product artifact and absent-Npcap guidance never launches an installer or browser automatically.
- WinDivert uses sniff/read-only capture and never reinjects traffic.
- `aion.db` update failures cannot replace the active DB and rollback is proven.
- Native ABI, callbacks, queues, buffers, and fuzz/stress inputs show no known leak, race, out-of-bounds read, or unbounded allocation.
- No A2Meter source/project/native binary is referenced by Namter.
