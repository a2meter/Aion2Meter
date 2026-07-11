# Task 8 Report: Versioned Native Protocol Events

## Status

Implemented and verified. The Lore commit uses the required intent
`Make one versioned native decoder authoritative`; the final dispatch records its hash.

## Delivered

- Expanded `nm_event_v1` into one closed version-1 ABI record with fixed-width event, provenance,
  flow, actor, target, owner, skill, buff, mob, boss, content, dungeon, party, damage, healing,
  HP, duration, state, action, and flag fields. UTF-8 names and retained payload bytes remain
  pointer/length callback views only.
- Added native event kinds for damage, DoT, buff, self/other actor, mob spawn, boss HP, entity
  removal, party, content, combat state, and unknown protocol. The pre-existing source-started
  lifecycle event remains for ABI compatibility.
- Added a bounded `SpanReader` with checked byte, little-endian integer, varint, UTF-8, skip, and
  remaining operations. UTF-8 rejects invalid, overlong, surrogate, and out-of-range sequences.
- Added snapshot-owned opcode/tag/layout decoding. Direct frames consume only the active
  snapshot's exact packet magic and optional marker. Longest matching tag wins, and a static
  opcode-to-parser table routes to focused parsers for each closed event family.
- Field descriptors control offsets and fixed/varint/UTF-8 representation. Missing fields,
  malformed lengths, oversized payloads, and truncation produce one decode diagnostic and never
  expose a partial typed event. Unknown tags produce a bounded `UnknownProtocolEvent` with full
  provenance and retained bytes.
- Added bounded deduplication keyed exclusively by flow, epoch, and a nonzero framer-assigned stream
  message identity. Zero identities are intentionally never deduplicated.
- Expanded the managed interop record to mirror the ABI. The unmanaged callback copies UTF-8 names
  and payload views before returning. `CombatEventMapper` creates a closed immutable hierarchy in
  `Namter.Encounter` without collapsing damage, multi-damage, healing, DoT, masks, or provenance.
- Embedded one oracle-certified real entity-removal frame from the supplied PCAP at record offset
  `247913`. The external fixture root is used only by verification and is not committed.

## TDD Evidence

### RED

- The first native focused build failed exactly because `event.hpp` and the decoder did not exist.
- Managed focused compilation failed exactly because the `Namter.Encounter` event hierarchy did
  not exist.
- The direct-frame regression first decoded snapshot-magic-prefixed input as unknown; consuming
  only the active snapshot's exact magic made it green.
- The stable identity test first failed to compile because `ProtocolMessage` had no stream message
  identity; the framer-owned ordinal made equal distinct messages independently observable.

### GREEN

- Focused native decoder suite: 6 passed, 0 failed.
- Focused managed event mapping suite: 4 passed, 0 failed.
- Native suite with `NAMTER_FIXTURE_ROOT=E:\A2Viewer\A2Meter\captures`: 102 passed, 0 failed,
  0 skipped.
- Managed unit suite: 103 passed, 0 failed.
- Managed integration command: exit 0; the existing assembly still contains no discoverable tests.
- Clean Debug and Release `Namter.slnx` rebuilds completed successfully with
  `NamterDisableFileTracking=true`. A final Release native/test build after the stream-identity
  change also completed successfully.
- `git diff --check`: clean apart from the repository's existing LF-to-CRLF checkout notices.

## Remaining Concerns

- The current Task 6 golden seed has placeholder/empty field descriptors for several opcode
  layouts. The decoder deliberately reports those as known-invalid rather than guessing. Publishing
  authoritative descriptors is data work; no legacy or fallback decoder was added.
- Only a proven entity-removal frame is embedded as a typed real-byte fixture. Other closed event-family
  parsers use exhaustive canonical descriptor fixtures because the supplied capture does not prove
  a complete instance for every closed ABI kind.
- The integration test project still has no discoverable tests. Native-to-managed layout and callback
  lifetime behavior are exercised by the native ABI suite, existing lifetime tests, and the managed
  mapping suite.
- Existing native post-build steps continue to print the non-fatal missing-child-`pwsh.exe` notice.

## Review Closure (2026-07-12)

This section supersedes the earlier deduplication and fixture-coverage statements.

### Stable identity and bounded state

- Deduplication identity is now exclusively `{flow tuple, epoch, nonzero stream_message_id}`.
  Capture timestamps, file offsets, message bytes/hashes, and decoded semantic fields do not
  participate.
- `stream_message_id == 0` has an explicit no-dedup policy. Repeated zero-ID messages are decoded
  independently.
- The cache retains exactly 65,536 identities. The 65,536th identity does not evict the first;
  insertion 65,537 does. `ProtocolDecoder::reset()` clears both the set and FIFO so an identity can
  be reused after a stream reset.
- Tests cover same ID with mutated bytes (suppressed), distinct IDs (preserved), flow and epoch
  separation, zero-ID repeats, the exact eviction edge, and reuse after reset.

### Snapshot descriptor hardening

- Validation rejects duplicate layout IDs, duplicate fields within a layout, unknown field kinds,
  unknown flags, unsupported fixed widths, invalid varint/UTF-8 size/count combinations, and
  descriptors outside the layout payload bound.
- Every known parser kind has one exact required/allowed field set. Missing or extra fields are
  rejected before decoder construction. Unknown opcode kinds may carry no typed layout.
- `ProtocolDecoder` validates snapshots itself and therefore cannot silently select the first of
  duplicate layouts.
- `ProtocolSnapshotStore` validates and allocates a candidate before swapping. Invalid replacement
  leaves the previous owned bytes exactly unchanged; `nm_core_set_protocol_snapshot` uses this
  store under its existing engine lock.

### Real supplied-capture evidence — corrected certification

The earlier five-family certification claim is withdrawn. Exact tag occurrence and a complete frame
boundary do not prove typed-event semantics. Read-only comparison with the existing PacketDispatcher
oracle leaves exactly one supplied-capture frame certified:

- entity-removal tag `21 8d`, PCAP record offset `247913`, actor varint `8137`.

The test asserts the complete actor value and all provenance and rejects every strict truncation and
declared-length mutation. It does not consume the trailing `00 01` bytes as part of the actor ID.

The following search hits remain found-but-not-certified and are not typed golden fixtures:

- damage `04 38` at `250658`: its post-tag flags select category zero, so the oracle emits no damage;
- DoT `05 38` at `612974`: actor equals target and the oracle suppresses the event;
- battle/buff `2a 38` at `105474`: the former overlapping offsets did not map oracle entity/type/
  caster semantics to Namter owner/target/action fields;
- boss-HP `01 8d` at `986583`: the candidate is too short and lacks the required `02 01 00` marker;
- self-actor candidate around `199707` and party candidate around `90319`: semantic completeness is
  still not proven by authoritative descriptors.

Canonical fixtures continue to cover all closed parser families without inventing capture truth.

### ABI and callback lifetime

- Native compile-time assertions freeze `nm_event_v1` as standard-layout, x64 size `200`; native
  tests assert every `offsetof` from `abi_version` through `payload_size`.
- Managed tests independently assert `Unsafe.SizeOf<NativeEventV1>() == 200` and every corresponding
  `Marshal.OffsetOf`.
- A bounded internal test seam invokes the actual unmanaged Cdecl function pointer for
  `OnNativeEvent`. A fully populated combat record is delivered through that path, its pinned
  native name/payload buffers are overwritten immediately afterward, and the test proves the
  managed `NativeEvent`, immutable byte copy, UTF-8 name, mapped `DamageEvent`, and provenance all
  retain the callback-time values. No test-only native ABI entry point was added.

### Fresh verification

- Fresh Debug `Namter.slnx` rebuild with VS 2026/v145 and
  `NamterDisableFileTracking=true`: exit `0`.
- Debug native suite with `NAMTER_FIXTURE_ROOT=E:\A2Viewer\A2Meter\captures`: `120/120` passed.
- Debug managed unit suite: `168/168` passed.
- Release `Namter.slnx` rebuild with VS 2026/v145 and
  `NamterDisableFileTracking=true`: exit `0`.
- Release native suite with supplied fixture root: `120/120` passed.
- Release managed unit suite for `Platform=x64`: `168/168` passed.
- Debug and Release managed integration commands both exited `0`; both explicitly reported that
  `Namter.Tests.Integration.dll` contains no discoverable tests.
- The non-fatal existing native post-build `pwsh.exe` notice remains unchanged.

## Important Review Corrections (2026-07-12)

### Sequential snapshot commands

- Absolute offsets remain supported for genuinely fixed layouts.
- New sequential fixed, varuint, and UTF-8 descriptor modes interpret `byte_offset` as a bounded
  skip from the current cursor. The cursor advances by the bytes actually consumed, so every later
  field moves correctly when an earlier varint changes width.
- Native validation rejects mixed absolute/sequential layouts and proves the worst-case sequential
  cursor stays within `max_payload_bytes`. The managed compiler applies the same encoding/mode/bound
  checks and preserves database `field_order` rather than sorting fields by kind.
- The RED regression used otherwise identical damage frames whose first actor varint changed from
  one byte (`127`) to two (`128`); before the change both sequential snapshots were rejected, and
  afterward every following field decoded identically at its shifted position.

### Unambiguous dispatch and unknown contract

- Native validation and the managed compiler reject duplicate raw wire tags even when kinds differ.
  A duplicate-tag replacement test proves `ProtocolSnapshotStore` keeps the exact prior bytes.
- Known kinds require a typed layout. Unknown registered kinds are allowed only with layout zero and
  produce the same bounded `UnknownProtocolEvent` contract as an unregistered wire tag. Unknown
  kinds cannot attach a typed layout, so dispatch has no ambiguous parser path.

### Corrected TDD evidence

- Native RED: sequential snapshots were rejected; a registered unknown kind produced a diagnostic;
  duplicate tags validated and replaced the old snapshot. All four failures were observed.
- Managed RED: duplicate tags and invalid unknown/layout combinations compiled, and compiler kind
  sorting destroyed field order. All three failures were observed.
- Targeted GREEN: native `5/5`; managed protocol snapshot compiler `11/11`.

## Final Acceptance Closure (2026-07-12)

### Managed/native exact field-set parity

- `ProtocolFieldContract` is the single managed table for both known-kind detection and exact
  required/allowed field masks. It mirrors the native validator for all 20 known opcode kinds.
- Before writing bytes, `ProtocolSnapshotCompiler` now rejects any referenced known layout whose
  mask is missing one required field or contains one disallowed field. Unknown kind/layout-zero
  behavior remains unchanged.
- Table-driven TDD covers all 20 known kinds three ways: exact snapshots compile and are accepted by
  the native core (`20/20`), missing-field snapshots fail compilation (`20/20`), and extra-field
  snapshots fail compilation (`20/20`). The 40 invalid cases were observed failing before the
  managed mask contract was implemented; targeted GREEN is `60/60`.

### Complete typed-event assertions

- The native closed-event regression now asserts every field populated by damage, DoT, buff,
  self/other actor, mob, boss HP, entity removal, party, content, and combat-state parsers.
- UTF-8 names are asserted while their owning `DecodedEvent` remains alive for self, other, mob,
  party, and content events. Every typed event also asserts both timestamps, epoch, both file
  offsets, both addresses, and both ports.
- Unknown flags above the supported maximum (`6`) have a dedicated rejection test. Sequential flag
  `3` is separately tested as a mixed absolute/sequential mode error, so the two contracts are no
  longer conflated.
