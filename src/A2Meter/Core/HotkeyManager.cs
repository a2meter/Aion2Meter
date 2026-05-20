using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Forms;

namespace A2Meter.Core;

/// Global hotkeys via RegisterHotKey — matches A2Power's implementation.
/// Event-driven: Windows sends WM_HOTKEY to the registered form's WndProc.
internal sealed class HotkeyManager : IDisposable
{
    public const int WM_HOTKEY = 0x0312;

    private const uint MOD_ALT      = 0x0001;
    private const uint MOD_CTRL     = 0x0002;
    private const uint MOD_SHIFT    = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly OverlayForm _form;
    private readonly List<int> _registeredIds = new();
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;
    private bool _suspended;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotkeyManager(OverlayForm form)
    {
        _form = form;
    }

    public void RegisterFromSettings(ShortcutSettings shortcuts)
    {
        _suspended = false;
        UnregisterAll();
        TryRegister(shortcuts.Reset,     () => _form.TriggerClearShortcut());
        TryRegister(shortcuts.Restart,   () => _form.TriggerRestart());
        TryRegister(shortcuts.Anonymous, () => _form.TriggerAnonymousToggle());
        TryRegister(shortcuts.Compact,   () => _form.ToggleCompact());
        TryRegister(shortcuts.Hide,      () => _form.ToggleVisibility());
        TryRegister(shortcuts.SwitchTab, () => _form.TriggerSwitchTab());
    }

    public void Suspend()
    {
        _suspended = true;
        foreach (int id in _registeredIds)
            UnregisterHotKey(_form.Handle, id);
    }

    public void Resume(ShortcutSettings shortcuts)
    {
        _suspended = false;
        RegisterFromSettings(shortcuts);
    }

    public void UnregisterAll()
    {
        foreach (int id in _registeredIds)
            UnregisterHotKey(_form.Handle, id);
        _registeredIds.Clear();
        _actions.Clear();
        _nextId = 1;
    }

    public void ProcessHotkey(int id)
    {
        if (!_suspended && _actions.TryGetValue(id, out var action))
            action();
    }

    public void Dispose()
    {
        UnregisterAll();
    }

    private void TryRegister(string? accelerator, Action action)
    {
        if (string.IsNullOrWhiteSpace(accelerator)) return;
        if (!ParseAccelerator(accelerator, out var modifiers, out var vk)) return;
        int id = _nextId++;
        if (RegisterHotKey(_form.Handle, id, modifiers | MOD_NOREPEAT, vk))
        {
            _registeredIds.Add(id);
            _actions[id] = action;
        }
    }

    private static bool ParseAccelerator(string accel, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        foreach (var raw in accel.Split('+'))
        {
            var token = raw.Trim();
            var lower = token.ToLowerInvariant();
            switch (lower)
            {
                case "alt":     modifiers |= MOD_ALT;   continue;
                case "ctrl":
                case "control": modifiers |= MOD_CTRL;  continue;
                case "shift":   modifiers |= MOD_SHIFT; continue;
                case "`": case "~": case "backquote":
                    vk = 0xC0; continue;
                case "-": case "minus":
                    vk = 0xBD; continue;
                case "/": case "slash":
                    vk = 0xBF; continue;
            }
            if (token.Length == 1 && char.IsDigit(token[0])) { vk = token[0]; continue; }
            if (token.Length == 1 && char.IsLetter(token[0])) { vk = (uint)char.ToUpperInvariant(token[0]); continue; }
            if (Enum.TryParse<Keys>(token, ignoreCase: true, out var k)) { vk = (uint)k; continue; }
        }
        return vk != 0;
    }
}
