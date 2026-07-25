# Namter Capture Operations

This runbook covers the phase-1 command-line capture engine. Phase 1 has no
overlay, settings window, installer, or other desktop UI.

## Prerequisites

- Windows x64 and the published Namter x64 binaries.
- A valid local `aion.db` whose schema and data versions are supported by the
  application.
- One explicitly selected live backend: `windivert` or `npcap`. Namter never
  switches backends automatically.

Offline PCAP replay does not require either live backend.

## WinDivert operation and release input

Live WinDivert capture requires Administrator privileges because Windows loads
the signed driver on demand. Namter opens `WINDIVERT_LAYER_NETWORK` with
`WINDIVERT_FLAG_SNIFF | WINDIVERT_FLAG_RECV_ONLY`. It observes copies only: no
send/reinject export is loaded, and Namter does not modify, block, drop, or
inject game traffic.

WinDivert is not downloaded or committed by the source build. A release
operator supplies the official `WinDivert-2.2.2-A` x64 binary distribution as
an external release input. Before packaging, fail closed unless all of these
checks pass:

1. The input version is exactly 2.2.2 and the approved release record identifies
   the `A` signed distribution.
2. The package contains the matching x64 `WinDivert.dll` and
   `WinDivert64.sys`; no x86 DLL/driver or unrelated sample executable enters
   the release layout.
3. PE machine type is x64 for both files, file versions match the approved
   release, and the driver has a valid trusted Authenticode signature.
4. SHA-256 values match the organization-approved release-input record captured
   independently from the archive. Do not establish trust from a checksum
   stored only beside the candidate archive.
5. `THIRD-PARTY-NOTICES.txt`, the complete LGPL v3 license text, and a written
   replacement procedure accompany the dynamically loaded files.

The validation step only reads a pre-provided local input. It must not download
an archive, alter source control, accept an unsigned/self-built driver, or
silently substitute another WinDivert version.

Common startup diagnostics include missing DLL/driver, missing Administrator
privileges, invalid driver signature, security-software driver block,
incompatible already-loaded driver, disabled Base Filtering Engine, invalid
filter, receive failure, and queue pressure. Resolve the reported cause; do not
work around it by silently changing backend.

## Npcap policy

Npcap is external software selected by the user. Namter does not bundle Npcap
binaries, a driver, an installer, or an OEM redistributable; does not download
Npcap; and does not launch an installer, browser, or download URL. When an
explicit `--backend npcap` selection finds no compatible x64 runtime, the CLI
prints status and the official page `https://npcap.com/#download`, then exits.
The user may open that page and install Npcap independently before retrying the
same command.

Runtime diagnostics distinguish not installed, legacy WinPcap, missing API
symbols, adapter enumeration/selection failure, BPF compile/apply failure,
activation failure, receive failure, unsupported link type, cancellation, and
kernel/interface drops. Npcap availability never changes WinDivert or offline
PCAP behavior.

## Commands

Show command syntax:

```powershell
Namter.Cli.exe --help
```

Start a live capture with an explicit backend:

```powershell
Namter.Cli.exe capture --backend windivert --data C:\Namter\data\aion.db --output C:\Namter\records
Namter.Cli.exe capture --backend npcap --data C:\Namter\data\aion.db --output C:\Namter\records
```

Optionally record the raw traffic while capturing. Each time a new dungeon or
content instance is entered, a fresh `dungeon-<id>-<timestamp>.pcapng` is opened
in the given directory, seeded with the packets already buffered so the entry
itself is present:

```powershell
Namter.Cli.exe capture --backend windivert --data C:\Namter\data\aion.db --output C:\Namter\records --packet-log C:\Namter\packets
```

The packet log is off unless requested. It is best-effort evidence collection:
each file stops at 512 MiB, and a failure to open or extend one never changes
capture behaviour, diagnostics, or completeness reporting. The files contain raw
game traffic, so treat them as sensitive and retain them deliberately.

Replay one PCAP or a directory recursively. Speed `0` means unpaced processing;
supported paced values are `1` and `10`:

```powershell
Namter.Cli.exe replay --input C:\captures\aion2_part001.pcap --data C:\Namter\data\aion.db --output C:\Namter\replay --speed 0
```

Compare one generated encounter record with readable fixtures. A mismatch is a
normal comparison result with exit code 7 and a machine-readable report; it is
not suppressed as a warning:

```powershell
Namter.Cli.exe compare --actual C:\Namter\replay\encounter.json --expected C:\captures\readable --report C:\Namter\reports\comparison.json
```

## Capture completeness and diagnostics

Treat a record as incomplete when diagnostics report backend/kernel drops,
queue overflow or dropped envelopes, unresolved TCP gaps, decoder epoch resets,
truncation, corrupt frames/LZ4 data, or an incomplete encounter state. Damage,
healing, buff uptime, and end time may then be understated or unavailable.
Never present or publish such a record as a complete result merely because a
boss identity and some events were decoded.

Preserve the event ledger, encounter record, and diagnostics together. Useful
counters include input packets, backend drops, invalid packets, flows/epochs,
accepted and duplicate bytes, overlaps/gaps/resets, frames, decompression
failures, unknown opcodes, emitted events, queue high-water marks, dropped
envelopes, callback failures, and incomplete encounters.

For the supplied full `aion2_part001.pcap`, the evidence contains a first run
with Turgen (`actorId=18804`) and Griosa (`actorId=36737`), followed by a real
separate-run Basilus (`actorId=17968`). The readable Basilus fixture with
`actorId=28353` is independent and remains a reducer-only semantic fixture. A
full-file comparison must report the later actor-17968 encounter as unmatched
when expected input contains only the first-run readable fixtures; it must not
hide or discard that encounter to force an exact count of two.

## `aion.db` status, update, and rollback

The active file is always `<data-dir>\aion.db`; the one-generation rollback is
`<data-dir>\backup\aion.previous.db`. Inspect local validity and rollback
availability with:

```powershell
Namter.Cli.exe data status --data-dir C:\Namter\data
Namter.Cli.exe data check --data-dir C:\Namter\data
```

The runtime update service validates an HTTPS manifest signature, compressed
and uncompressed sizes, SHA-256, minimum app version, schema version, SQLite
integrity/foreign keys, required tables, and database version rows before it
stages a candidate. Activation waits for an idle encounter boundary, replaces
`aion.db` atomically, rebuilds selective caches, and preserves one validated
previous snapshot. Any failed validation or interrupted staging leaves the
active database unchanged.

Rollback is explicit:

```powershell
Namter.Cli.exe data rollback --data-dir C:\Namter\data
```

`NoBackup` means no validated previous generation is available. `BackupInvalid`
or another invalid-data result requires restoring a trusted published database;
do not rename an arbitrary SQLite file into place. A capture cannot start
without a valid supported `aion.db`.

## Phase-1 boundary

Phase 1 produces native/managed capture components, deterministic ledgers and
encounter records, diagnostics, update support, and CLI comparison artifacts.
It intentionally has no product UI. Backend choice, Npcap download-page opening,
and result presentation belong to a separately designed phase-2 UI and must
preserve the explicit-action and incomplete-warning contracts above.
