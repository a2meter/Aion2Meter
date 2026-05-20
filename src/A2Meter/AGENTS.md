# A2Meter / src / A2Meter

## Parent
../AGENTS.md

## Summary
The main A2Meter application: a .NET 8 WinForms desktop overlay that captures Aion network traffic in real-time, parses combat packets, calculates DPS statistics, and renders a Direct2D overlay. Supports live capture via Npcap, offline replay from PCAP files, and demo mode with dummy data.

## Key Files

| File | Purpose |
|------|---------|
| Program.cs | Entry point; initializes WinForms, sets up crash handlers, enforces single-instance mutex, launches OverlayForm |
| Properties/ | Project metadata |
| *.csproj | Project configuration |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| [Api/](./Api/AGENTS.md) | Web API clients: combat record upload, skill levels, character data caches |
| Assets/ | Fonts, icons, textures |
| [Calc/](./Calc/AGENTS.md) | Combat score calculation (not DPS — alternative metric) |
| [Core/](./Core/AGENTS.md) | Application settings, window state, hotkey management, crash reporter |
| [Data/](./Data/AGENTS.md) | Game database (skills, items, etc.) |
| [Direct2D/](./Direct2D/AGENTS.md) | D2D rendering engine: OverlayRenderer, fonts, job icons, theme system |
| [Dps/](./Dps/AGENTS.md) | Core DPS pipeline: meter accumulation, packet parsing, combat history, session management |
| [Forms/](./Forms/AGENTS.md) | WinForms UI: OverlayForm (main), detail/history/settings panels, secondary windows |
| [Native/](./Native/AGENTS.md) | Win32 P/Invoke declarations |

## AI Agent Instructions

- **Application Flow**: Program.cs → OverlayForm (WinForms window) → OverlayRenderer (D2D frame composition) → DpsPipeline (packet processing) → DpsMeter (damage accumulation).
- **Packet Ingestion**: DpsPipeline listens to IPacketSource events (CombatHit, TargetChanged, MobSpawned, etc.). PacketSniffer feeds live packets; PcapReplaySource feeds offline PCAP.
- **Rendering**: OverlayRenderer composes frames every ~100ms push interval; UpdateLayeredWindow blits offscreen D2D bitmap to the WS_EX_LAYERED window.
- **Settings**: AppSettings.Instance persists to %APPDATA%\A2Meter\app_settings.json; WindowState stored separately.
- **Hotkeys**: HotkeyManager (Core/) registers Win32 WM_HOTKEY messages; shortcuts configurable via SettingsPanelForm.
- **Crash Handling**: Program.cs installs native exception filter for D2D violations; AppDomain.UnhandledException + Application.ThreadException for managed crashes.

## Dependencies

- System.Windows.Forms (WinForms)
- Vortice.Windows (Direct2D, Direct3D11, DirectWrite)
- System.Text.Json (settings)
- Npcap / wpcap.dll (live packet capture, optional for replay mode)

## Notes

- Single-instance: enforced via Mutex at startup; duplicate launches exit silently.
- Auto-update: checked on launch via AutoUpdater; new version toast appears if available.
- Overlay-only mode: ForegroundWatcher monitors Aion 2 focus; hides overlay when Aion is not active if setting enabled.
- Compact mode: toggles full click-through (WS_EX_TRANSPARENT) and smaller fonts.
