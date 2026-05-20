# A2Meter / src / A2Meter / Native

## Parent
../AGENTS.md

## Summary
Win32 P/Invoke declarations for native API calls used by the overlay application (window management, mouse input, layered window rendering).

## Key Files

| File | Purpose |
|------|---------|
| Win32Native.cs | P/Invoke signatures for: SetWindowLong/GetWindowLong (WS_EX_* flags), UpdateLayeredWindow, CreateDIBSection, SelectObject, DeleteDC, DeleteObject, CreateCompatibleDC, TrackMouseEvent, SetCapture, ReleaseCapture, and related structures (BLENDFUNCTION, POINT, SIZE, BITMAPINFOHEADER) |

## AI Agent Instructions

- **Window Styles**: WS_EX_LAYERED, WS_EX_NOACTIVATE, WS_EX_TOPMOST, WS_EX_TRANSPARENT (for click-through).
- **Hit Testing**: HTTOP, HTBOTTOM, HTLEFT, HTRIGHT, HTTOPLEFT, etc. for edge resize detection.
- **Layered Window**: UpdateLayeredWindow() blits offscreen HBITMAP with per-pixel alpha (AC_SRC_ALPHA blend mode).
- **Mouse Tracking**: TrackMouseEvent() registers for WM_MOUSELEAVE; SetCapture/ReleaseCapture() lock mouse to overlay during drag.

## Dependencies

- System.Runtime.InteropServices (DllImport, StructLayout)

## Notes

- All P/Invoke is safe (no unsafe code blocks); Win32 calls are inherently unsafe but wrapped in managed signatures.
- Constants (WM_* message codes, GWL_EXSTYLE) defined in this module or System.Windows.Forms.
