# A2Meter / src / A2Meter / Forms

## Parent
../AGENTS.md

## Summary
WinForms UI components: main overlay window, toolbar, detail panels, settings, history browser, secondary windows manager, and tray icon. All UI is decoupled from D2D rendering via data model bindings and event handlers.

## Key Files

| File | Purpose |
|------|---------|
| OverlayForm.cs | Main frameless, topmost overlay window; WS_EX_LAYERED; hosts OverlayRenderer; routes mouse/keyboard input; manages secondary windows |
| SecondaryWindows.cs | Factory for secondary WinForms windows (detail, history, settings); tracks open instances; enforces single-instance per type |
| DpsDetailForm.cs | Shows expanded damage breakdown for a single player (skills, crit rates, buffs) |
| CombatHistoryForm.cs | List of past combat records; clicking a record replays its snapshot in the overlay |
| SettingsPanelForm.cs | UI for all user settings (theme colors, opacity, font, shortcuts, preferences); emits SettingsChanged event on save |
| SkillHitDetailForm.cs | Detailed hit log for a skill (individual hits, crit %, back attacks, etc.) |
| UpdateToastForm.cs | Small notification popup for new version available; click to download |
| UpdateDetailForm.cs | Release notes viewer for available update |
| SetupForm.cs | First-launch setup dialog; checks Npcap + game database installation |
| OverlayHeaderPanel.cs | (Likely obsolete; header now part of OverlayRenderer) |
| VirtualListPanel.cs | Efficient list rendering (possibly scrollable virtual list for large data) |
| LockButtonForm.cs | Tiny floating button shown when overlay is locked; click to unlock |

## AI Agent Instructions

- **OverlayForm**: Only WinForms form visible by default; all UI rendered by OverlayRenderer (not WinForms controls). WndProc() dispatches WM_HOTKEY, WM_NCHITTEST, WM_MOUSEMOVE, etc. to OverlayRenderer.HitTest().
- **Mouse Interaction**: OverlayForm.WndProc() converts mouse coords to client space; calls OverlayRenderer.HitTest(pt) → ZoneId; dispatches to button/slider handlers.
- **Resize/Drag**: Hit testing for edges (10px ResizeMargin) returns HTNORTH/HTSOUTH/HTEAST/HTWEST; Windows handles resize; OverlayForm calls PersistWindowState() on move/resize.
- **Secondary Windows**: SecondaryWindows.Open<T>() creates or shows existing window; tracks FormClosed events for cleanup.
- **Settings Panel**: Emits SettingsChanged event → OverlayRenderer.ApplySettings() → RebuildFonts() + ApplyThemeBrushes().
- **History Panel**: SetData() binds combat records; clicking a record calls DpsPipeline.EnterHistoryView() + renders snapshot.
- **Setup Dialog**: Shows if Npcap or game database missing; user must install before main app runs.

## Dependencies

- System.Windows.Forms (WinForms event loop, dialogs)
- A2Meter.Core (AppSettings, HotkeyManager, AutoUpdater)
- A2Meter.Direct2D (OverlayRenderer)
- A2Meter.Dps (DpsPipeline, CombatRecord)
- A2Meter.Api (SkillLevelCache for detail panels)

## Notes

- OverlayForm.CreateParams adds WS_EX_LAYERED + WS_EX_NOACTIVATE + WS_EX_TOPMOST.
- ShowWithoutActivation override prevents overlay from stealing focus.
- Secondary windows are not always-on-top; they are regular WinForms forms.
- LockButtonForm is tiny and positioned relative to OverlayForm; used to unlock when overlay is locked.
