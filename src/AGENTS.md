# A2Meter / src

## Parent
../AGENTS.md

## Summary
Contains all source projects for A2Meter: the main overlay application (A2Meter), packet capture utility (A2Capture), packet inspector (A2Inspect), auto-updater (A2Updater), native packet engine library (PacketEngine), and PCAP analyzer (PcapAnalyzer).

## Key Files

| File | Purpose |
|------|---------|
| A2Meter/ | Main DPS meter overlay (WinForms + Direct2D) |
| A2Capture/ | Standalone packet capture tool |
| A2Inspect/ | Packet inspection/debugging utility |
| A2Updater/ | Auto-update bootstrapper |
| PacketEngine/ | Native C++/C# interop for packet processing |
| PcapAnalyzer/ | PCAP file analyzer |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| [A2Meter/](./A2Meter/AGENTS.md) | Main application source code |
| A2Capture/ | Packet sniffer CLI tool |
| A2Inspect/ | Packet inspection tool |
| A2Updater/ | Version check & download manager |
| PacketEngine/ | Native library for protocol parsing |
| PcapAnalyzer/ | PCAP file processing utility |

## AI Agent Instructions

- Start with A2Meter/ for the main application logic.
- Packet processing is split: A2Meter/Dps/Protocol/ for managed parsing; PacketEngine/ for native (possibly C++) optimizations.
- A2Capture and A2Inspect are tools; not required for the main meter.
- PacketEngine is a .NET project but may contain P/Invoke calls to native DLLs.

## Dependencies

Each project targets .NET 8 and shares common dependencies (Vortice, System.Text.Json, etc.).
