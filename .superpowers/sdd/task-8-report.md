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
- Added bounded deduplication keyed by flow, epoch, framer-assigned stream message identity,
  capture provenance, and message identity. The framer assigns a monotonically increasing identity
  per epoch, so byte-identical nested messages sharing outer PCAP provenance remain distinct while
  repeated delivery of the same `ProtocolMessage` is suppressed.
- Expanded the managed interop record to mirror the ABI. The unmanaged callback copies UTF-8 names
  and payload views before returning. `CombatEventMapper` creates a closed immutable hierarchy in
  `Namter.Encounter` without collapsing damage, multi-damage, healing, DoT, masks, or provenance.
- Embedded the smallest selected complete real damage-tag frame from the supplied PCAP in native
  tests with PCAP offset `250658`. The external fixture root is used only by verification and is
  not committed.

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
- Only a proven damage frame is embedded as a typed real-byte fixture. Other closed event-family
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

### Real supplied-capture evidence

The native PCAP/TCP/framer search over `captures/aion2_part001.pcap` yielded complete, framed,
typed fixtures that are embedded in the test binary (the PCAP itself remains external):

- damage tag `04 38`, PCAP record offset `250658`;
- DoT tag `05 38`, offset `612974`;
- battle/buff tag `2a 38`, offset `105474`;
- boss-HP tag `01 8d`, offset `986583`;
- entity-removal tag `21 8d`, offset `247913`.

For each certified fixture the tests assert every parser output field plus timestamp, epoch, file
offset, flow addresses, and ports where applicable. They reject every strict truncation and
mutations of the declared frame length.

Search also found a self-actor tag candidate of about 3,180 bytes at offset `199707` and a party
candidate of about 284 bytes at offset `90319`. Their semantic completeness could not be certified
from the supplied capture and active descriptors, so they are intentionally not represented as
typed golden fixtures. Canonical descriptor tests continue to cover those parser families without
inventing capture truth.

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

- Fresh Debug native test-project rebuild with VS 2026/v145 and
  `NamterDisableFileTracking=true`: exit `0`.
- Debug native suite with `NAMTER_FIXTURE_ROOT=E:\A2Viewer\A2Meter\captures`: `115/115` passed.
- Debug managed unit suite: `105/105` passed.
- Release `Namter.slnx` rebuild with VS 2026/v145 and
  `NamterDisableFileTracking=true`: exit `0`.
- Release native suite with supplied fixture root: `115/115` passed.
- Release managed unit suite for `Platform=x64`: `105/105` passed.
- The non-fatal existing native post-build `pwsh.exe` notice remains unchanged.
