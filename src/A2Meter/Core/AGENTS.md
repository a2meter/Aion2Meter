# A2Meter / src / A2Meter / Core

## Parent
../AGENTS.md

## Summary
Core application services: settings persistence, window state management, hotkey registration, crash reporting, network monitoring, and Win32 interop.

## Key Files

| File | Purpose |
|------|---------|
| AppSettings.cs | Singleton; persists all user settings to JSON (theme, opacity, font, shortcuts, DPS preferences). Debounced saves avoid excessive I/O. |
| WindowState.cs | Tracks overlay position/size (separate from AppSettings for per-machine state). |
| ShortcutSettings.cs | Hotkey key bindings (visibility toggle, reset, tab switch, restart, anonymous mode, etc.). |
| HotkeyManager.cs | Registers Win32 WM_HOTKEY messages; dispatches to OverlayForm on activation. |
| TrayManager.cs | System tray icon; context menu for overlay visibility & Aion-only mode toggle. |
| AutoUpdater.cs | Version check HTTP call; downloads new version; spawns A2Updater.exe. |
| CrashReporter.cs | Uploads crash.log entries to proxy server (opt-in; disabled by default). |
| ForegroundWatcher.cs | Monitors Aion 2 process focus; raises ActiveChanged event. |
| PingMonitor.cs | Measures network latency from segment timestamps. |
| Win32Native.cs | P/Invoke declarations (WS_EX_* flags, UpdateLayeredWindow, SetWindowLong, etc.). |

## AI Agent Instructions

- **AppSettings**: Thread-safe singleton; SaveDebounced() coalesces rapid calls (~400ms window) into one disk write. Load() applies defaults; sanitizes bogus coordinates (multimonitor scenarios).
- **Window State**: Restored from JSON on load; defaults to top-right of primary screen if missing.
- **Hotkeys**: Registered during OverlayForm.HandleCreated; ProcessHotkey() dispatches via tag/WParam matching.
- **Auto-Update**: Runs async on startup; checks remote version; downloads & launches A2Updater if newer version found.
- **Crash Reporting**: Async fire-and-forget; only runs if user opts in; crash entries older than CrashReportingActivatedAt are not sent.
- **Foreground Watcher**: Polls every ~1s; raises ActiveChanged event when Aion 2 gains/loses focus.

## Dependencies

- System.Windows.Forms (WinForms event loop)
- System.Text.Json (settings serialization)
- System.Net.Http (version check, crash upload)
- System.Runtime.InteropServices (Win32 P/Invoke)

## Notes

- Theme colors are hex strings; parsed via ColorTranslator.FromHtml().
- GPU mode autocorrect: if user never explicitly set "off", reverts to "on" on next load.
- Crash log paths: CrashLogPath (exe directory) + CrashLogPathAlt (%APPDATA%\A2Meter\crash.log) for resilience.
