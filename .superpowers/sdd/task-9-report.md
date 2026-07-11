# Task 9 Implementer Report

## Status

Implemented explicit WinDivert, Npcap, and PCAP source selection without fallback. Both live adapters terminate at owned `CaptureRecord` values and feed the engine's bounded native queue. Overflow and receive failures produce structured diagnostics. WinDivert never resolves or calls send/reinject exports. Npcap remains external and loads only from `%SystemRoot%\System32\Npcap\wpcap.dll`.

## TDD Evidence

The three backend test files and their project entries were added first. The first Debug rebuild failed as intended with `C1083: live_backend.hpp: No such file or directory`. A later ABI cycle failed with `C2065: NM_STATUS_NPCAP_NOT_INSTALLED` before the dedicated status existed.

Injected API tables cover missing libraries/symbols, incompatible identity, open/activation/filter failures, cancellation, packet delivery, queue bounds, stats/drops, link type, adapter enumeration, immediate mode, kernel/user buffers, BPF, and event cancellation. Engine injection covers concurrent state reservation, a capacity-64 queue receiving a 65-packet burst, structured poll failure, never-started/idempotent stop, and deterministic blocking-poll cancel then join then close.

## Official Contracts

- WinDivert 2.2 documentation, accessed 2026-07-12: <https://reqrypt.org/windivert-doc.html>. Namter uses `NETWORK`, exact `SNIFF | RECV_ONLY`, documented queue bounds, driver major 2/minor at least 2, batched overlapped `WinDivertRecvEx`, and `WinDivertHelperParsePacket` boundaries. The official-layout address equivalent has `sizeof == 80`, timestamp offset 0, and union offset 16 assertions. QPC timestamps convert to nanoseconds and the official outbound bit sets direction.
- Npcap API documentation, accessed 2026-07-12: <https://npcap.com/guide/npcap-api.html> and <https://npcap.com/guide/wpcap/pcap.html>. Namter requires `pcap_lib_version` to identify Npcap, rejects WinPcap, enumerates adapters, configures immediate/kernel/user buffers, activates, compiles/applies BPF, waits on `pcap_getevent`, preserves link type, and reports `pcap_stats`.
- Missing Npcap maps to `NM_STATUS_NPCAP_NOT_INSTALLED` with `https://npcap.com/#download` metadata. No automatic browser, installer, process, HTTP, or download path exists.
- ABI v1 uses fixed `NM_CORE_DEFAULT_GAME_PORT` 13328. Live `source_data` is an optional bounded UTF-8 Npcap adapter name without NUL.

## Final Verification

- Debug `Namter.slnx` full rebuild: exit 0.
- Debug native suite with fixture root: 145 total, 143 passed, two availability skips.
- Debug managed unit suite: 168/168 passed.
- Release `Namter.slnx` full rebuild: exit 0.
- Release native suite with fixture root: 145 total, 143 passed, two availability skips.
- Release managed unit suite: 168/168 passed.
- Integration project builds but currently has no discoverable tests (pre-existing state).
- `git diff --check`: clean except Git line-ending notices.
- Guards: zero tracked runtime/driver/installer binaries; zero A2Meter/PacketEngine references; zero installer/download/browser/process, `WinDivertSend`, `pcap_sendpacket`, or `pcap_inject` APIs in product native code.

## Availability Skips

The machine has an installed Npcap DLL whose identity/export probe succeeds, so the runtime-absent ABI test skips; the injected missing-runtime test executes and passes. Live Npcap activation also skips because activation is unavailable in this session, while all activation contracts execute via injected APIs. No WinDivert runtime/driver is present and none was downloaded or added. The production batch splitter is independently executed with two packed packets, helper-provided boundaries, QPC conversion, and opposing directions.
