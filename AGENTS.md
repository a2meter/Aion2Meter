# A2Meter

## Parent
../AGENTS.md

## Summary
A2Meter is a .NET 8 WinForms + Direct2D overlay DPS meter for the MMORPG Aion. The application captures network traffic to parse combat packets in real-time, calculates damage per second (DPS) statistics, and renders results on a transparent, always-on-top overlay. It supports both live packet capture via Npcap and offline replay from packet captures.

## Key Files

| File | Purpose |
|------|---------|
| A2Meter.slnx | Solution file; contains all project definitions |
| src/ | All source code (organized by project and feature) |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| [src/](./src/AGENTS.md) | All source projects: A2Meter (main), A2Capture, A2Inspect, A2Updater, PacketEngine, PcapAnalyzer |
| captures/ | Offline packet captures (.pcap files) for testing and replay |
| docs/ | Project documentation |
| publish/ | Release artifacts organized by version |

## AI Agent Instructions

- **Exploration**: Start in `src/A2Meter/` to understand the main overlay application architecture (Program.cs → OverlayForm → OverlayRenderer).
- **Packet Processing**: The Dps/ directory contains the core DPS pipeline; Protocol/ subdirectory handles packet parsing (LZ4 decompression, TCP reassembly, stream processing).
- **Rendering**: Direct2D rendering is isolated in Direct2D/; OverlayRenderer handles D2D initialization, frame composition, and UpdateLayeredWindow presentation.
- **Settings & State**: AppSettings (Core/) persists configuration to JSON; WindowState tracks overlay position/size.
- **For Bugs**: Check crash.log in AppData\A2Meter\ for unhandled exceptions. Program.cs installs native crash handlers for D2D/Vortice access violations.
- **Testing**: Use --demo flag to show dummy DPS data; --replay <dir> for offline packet playback.

## Dependencies

- **.NET 8** (WinForms, SystemWindows.Forms)
- **Vortice.Windows** (Direct2D1, Direct3D11, DirectWrite) — GPU-accelerated rendering
- **Npcap** (wpcap.dll) — Live packet capture (optional, required for live mode)
- **System.Text.Json** — Settings serialization
- **SharpZipLib** (optional) — Possibly used in packet processing

## Notes

- Single-instance mutex prevents multiple A2Meter processes (Mutex "A2Meter.SingleInstance.Mutex").
- Auto-update mechanism via AutoUpdater checks for new versions and downloads via update toast.
- Crash reporting is opt-in; crashes are logged to crash.log and uploaded to a proxy server if enabled.
