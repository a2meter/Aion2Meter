# A2Meter / src / A2Meter / Direct2D

## Parent
../AGENTS.md

## Summary
GPU-accelerated rendering engine using Direct2D1, Direct3D11, and DirectWrite. Renders the DPS meter overlay frame every ~100ms; composes toolbar, header, target bar, player rows, and party list into a single offscreen bitmap; presents via UpdateLayeredWindow for per-pixel alpha blending.

## Key Files

| File | Purpose |
|------|---------|
| OverlayRenderer.cs | Core D2D rendering engine; manages device, render target, brushes, fonts; composes frame; handles hit testing for UI interaction |
| DpsCanvas.cs | Data models for player rows, skill bars, session summary (not a WinForms control; pure data) |
| D2DContext.cs | Direct3D11/Direct2D device context wrapper (if used; possibly legacy) |
| D2DFontProvider.cs | DirectWrite font factory; creates text formats for different sizes and weights |
| JobIconAtlas.cs | Job icon sprite atlas; loads and caches 8 job icons (indexed by archetype 0-7) |

## AI Agent Instructions

- **OverlayRenderer.Init()**: Creates D3D11 device (hardware or WARP fallback), Direct2D device, and staging texture for layered window presentation.
- **RenderFrame()**: Renders to offscreen bitmap every push; clears background, draws toolbar/header/target/rows based on ActiveTab (DPS or Party). Compact mode removes toolbar.
- **PresentToLayeredWindow()**: Copies render target to staging texture (GPU read), creates HBITMAP, calls UpdateLayeredWindow with AC_SRC_ALPHA blend mode.
- **Hit Testing**: HitTest() checks toolbar button zones; RowHitTest() maps Y-coordinate to player row index.
- **Theme System**: ApplyThemeBrushes() updates D2D brush colors from AppSettings.Theme hex strings.
- **Fonts**: Dual font sets (normal + 1pt smaller for compact mode); updated via RebuildFonts() when settings change.
- **Zone IDs**: Lock, Anon, History, Settings, Close, Slider, Countdown, CP/Score toggles, DPS/Party tabs.

## Dependencies

- Vortice.Windows (Direct2D1, Direct3D11, DirectWrite, DXGI)
- System.Numerics (Matrix3x2 transforms)
- A2Meter.Core (AppSettings, Win32Native)
- A2Meter.Dps (DpsCanvas, MobTarget, ServerMap)

## Notes

- Layout scales with RowH (user-configurable row height); all elements proportional.
- Brush colors cached; reused across frames to avoid allocation churn.
- Icon geometries (lock, unlock, eye, gear, close) built once via PathGeometry on init.
- CompactMode: toggles full click-through (WS_EX_TRANSPARENT flag) + uses compact font set + no toolbar.
- Toast notifications: brief messages queued in thread-safe queue; auto-expire after 3s.
