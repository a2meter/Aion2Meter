using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Direct2D;
using A2Meter.Dps;

namespace A2Meter.Forms;

/// Frameless, topmost overlay — 100% D2D rendered (no WinForms child controls).
/// Uses offscreen D2D → UpdateLayeredWindow for per-pixel alpha support.
internal sealed class OverlayForm : Form
{
    private const int HeaderHeight = 36;
    private const int ResizeMargin = 10;

    private OverlayRenderer? _renderer;

    private IPacketSource? _source;
    private readonly DpsMeter      _meter   = new();
    private readonly PartyTracker  _party   = new();
    private readonly Dictionary<string, string> _partyTierCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _partyTierLoading = new(StringComparer.Ordinal);
    private DpsPipeline? _pipeline;
    private ProtocolPipeline? _protocol;
    private ForegroundWatcher? _fgWatcher;

    /// Optional override: when set before Load fires, OverlayForm uses this packet source
    /// instead of constructing a live PacketSniffer. Used for replay mode.
    public IPacketSource? PacketSourceOverride { get; set; }

    private bool _locked;
    private bool _anonymous;
    private bool _appCloseRequested;
    private bool _loaded;
    private bool _sliderDragging;

    public HotkeyManager? Hotkeys { get; set; }
    public SecondaryWindows Windows { get; }
    public event EventHandler? AppCloseRequested;

    // ── Win32 for mouse tracking ──
    private const int WM_MOUSEMOVE    = 0x0200;
    private const int WM_LBUTTONDOWN  = 0x0201;
    private const int WM_LBUTTONUP    = 0x0202;
    private const int WM_MOUSELEAVE   = 0x02A3;
    private const int TME_LEAVE       = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public int cbSize;
        public int dwFlags;
        public IntPtr hwndTrack;
        public int dwHoverTime;
    }
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT evt);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();

    private bool _trackingMouse;

    private void PersistWindowState()
    {
        if (!_loaded) return;
        if (WindowState != FormWindowState.Normal) return;
        var s = AppSettings.Instance;
        s.WindowState.X = Location.X;
        s.WindowState.Y = Location.Y;
        s.WindowState.Width  = Size.Width;
        s.WindowState.Height = Size.Height;
        s.SaveDebounced();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        PersistWindowState();
        RequestRender();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PersistWindowState();
        RequestRender();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        PersistWindowState();
        RequestRender();
    }

    public OverlayForm()
    {
        Text = "A2Meter";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        TopMost = true;
        try { var exe = Environment.ProcessPath; if (exe != null) Icon = System.Drawing.Icon.ExtractAssociatedIcon(exe); } catch { }
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(100, 100);

        var ws = AppSettings.Instance.WindowState;
        Location = new Point(ws.X, ws.Y);
        int wantW = ws.Width  >= MinimumSize.Width  ? ws.Width  : 460;
        int wantH = ws.Height >= MinimumSize.Height ? ws.Height : 500;
        Size = new Size(wantW, wantH);
        Windows = new SecondaryWindows(this);

        Load += (_, _) => { _loaded = true; InitOverlay(); };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32Native.WS_EX_LAYERED
                        | Win32Native.WS_EX_NOACTIVATE
                        | Win32Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    private void InitOverlay()
    {
        _renderer = new OverlayRenderer();
        _renderer.Init();

        _source ??= PacketSourceOverride ?? new PacketSniffer();
        _protocol = new ProtocolPipeline(_source, log: msg => Console.Error.WriteLine(msg));
        _pipeline = new DpsPipeline(_source, _meter, _party);
        _pipeline.DataPushed += OnDataPushed;
        _pipeline.CombatStarted += OnCombatStarted;
        _source.PartyRequestReceived += OnPartyRequestReceived;
        try { _pipeline.Start(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[overlay] packet source failed to start: " + ex.Message);
        }

        // Foreground watcher: hide overlay when Aion 2 is not active.
        _fgWatcher = new ForegroundWatcher("aion2");
        _fgWatcher.ActiveChanged += OnAionActiveChanged;
        if (AppSettings.Instance.OverlayOnlyWhenAion)
            _fgWatcher.Start();

        RequestRender();
    }

    /// Called at ~10 Hz from the pipeline timer — push data to renderer and re-render.
    private void OnDataPushed(IReadOnlyList<DpsCanvas.PlayerRow> rows, long total,
        string timer, MobTarget? target, DpsCanvas.SessionSummary? summary)
    {
        if (_renderer == null || !IsHandleCreated || IsDisposed) return;
        try
        {
            BeginInvoke(() =>
            {
                if (_renderer == null) return;
                _renderer.CountdownSec = _pipeline?.CountdownSeconds ?? 0;
                _renderer.CountdownExpired = _pipeline?.CountdownExpired ?? false;
                _renderer.PingMs = _pipeline?.Ping.CurrentPingMs ?? 0;
                _renderer.SetData(rows, total, timer, target, summary);
                _renderer.SetPartyData(BuildPartyRows());
                RequestRender();
            });
        }
        catch { }
    }

    private void OnCombatStarted()
    {
        if (_renderer == null || !IsHandleCreated || IsDisposed) return;
        try
        {
            BeginInvoke(() =>
            {
                if (_renderer != null && _renderer.ActiveTab != OverlayRenderer.TabId.Dps)
                {
                    _renderer.ActiveTab = OverlayRenderer.TabId.Dps;
                    RequestRender();
                }
            });
        }
        catch { }
    }

    /// 07 97 party-request packet → pop a toast with the requester's tier
    /// (fetched async from /api/players/tier on the web side).
    private void OnPartyRequestReceived(PartyMember member)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (!AppSettings.Instance.LookupToastEnabled) return;
        // Only show toasts for requests with enough identity to look up.
        if (string.IsNullOrEmpty(member.Nickname)) return;
        var identity = Api.PlayncClient.NormalizeCharacterQuery(member.Nickname, member.ServerId, member.ServerName);
        if (identity.ServerId <= 0) return;
        member.Nickname = identity.Name;
        member.ServerId = identity.ServerId;
        member.ServerName = identity.ServerName;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                var toast = new PartyRequestToastForm(this, member, _pipeline?.CurrentDungeonId);
                toast.Show(this);
                if (_renderer != null)
                {
                    _renderer.SetPartyData(BuildPartyRows());
                    RequestRender();
                }
            });
        }
        catch { }
    }

    private List<OverlayRenderer.PartyRow> BuildPartyRows()
    {
        var list = new List<OverlayRenderer.PartyRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        PartyMember[] snapshot = _party.SnapshotMembers();
        foreach (var pm in snapshot)
        {
            if (string.IsNullOrEmpty(pm.Nickname)) continue;
            if (!pm.IsSelf && !pm.IsPartyMember && !pm.IsLookup && !pm.IsPartyRequest) continue;

            int cp = pm.CombatPower;
            int score = 0;
            int sid = pm.ServerId;
            string sname = pm.ServerName;
            var identity = Api.PlayncClient.NormalizeCharacterQuery(pm.Nickname, sid, sname);
            string lookupName = identity.Name;
            sid = identity.ServerId;
            sname = identity.ServerName;
            if (string.IsNullOrEmpty(sname) && sid > 0)
                sname = Dps.Protocol.ServerMap.GetName(sid);

            var api = Api.SkillLevelCache.Instance.Get(lookupName, sid);
            if (api != null)
            {
                if (cp == 0 && api.CombatPower > 0) cp = api.CombatPower;
                if (score == 0 && api.CombatScore > 0) score = api.CombatScore;
            }
            else if (sid > 0)
            {
                Api.SkillLevelCache.Instance.EnsureLoaded(lookupName, sid);
            }

            string tier = GetCachedTier(lookupName, sid);

            string displayName = !string.IsNullOrEmpty(sname) && !lookupName.Contains('[') ? $"{lookupName}[{sname}]" : lookupName;
            if (!seen.Add(displayName)) continue;

            list.Add(new OverlayRenderer.PartyRow(
                Name: displayName,
                JobIconKey: Dps.JobMapping.GameToJobName(pm.JobCode),
                CombatPower: cp,
                CombatScore: score,
                ServerId: sid,
                ServerName: sname,
                IsSelf: pm.IsSelf,
                Level: pm.Level,
                Tier: tier,
                IsPartyRequest: pm.IsPartyRequest,
                PartyRequestOrder: pm.PartyRequestOrder));
        }
        list.Sort(ComparePartyRows);
        return list;
    }

    private static int ComparePartyRows(OverlayRenderer.PartyRow a, OverlayRenderer.PartyRow b)
    {
        if (a.IsSelf != b.IsSelf) return a.IsSelf ? -1 : 1;
        if (a.IsPartyRequest != b.IsPartyRequest) return a.IsPartyRequest ? -1 : 1;
        if (a.IsPartyRequest)
        {
            long ao = a.PartyRequestOrder > 0 ? a.PartyRequestOrder : long.MaxValue;
            long bo = b.PartyRequestOrder > 0 ? b.PartyRequestOrder : long.MaxValue;
            int order = ao.CompareTo(bo);
            if (order != 0) return order;
        }
        int cp = b.CombatPower.CompareTo(a.CombatPower);
        return cp != 0 ? cp : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
    }

    private string GetCachedTier(string nickname, int serverId)
    {
        if (string.IsNullOrWhiteSpace(nickname) || serverId <= 0) return "";
        string key = TierKey(Api.PlayerTierClient.CleanPlayerName(nickname), serverId);
        if (_partyTierCache.TryGetValue(key, out var tier)) return tier;
        EnsureTierLoaded(nickname, serverId, key);
        return "";
    }

    private void EnsureTierLoaded(string nickname, int serverId, string key)
    {
        if (_partyTierLoading.Contains(key)) return;
        _partyTierLoading.Add(key);

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            string? tier = null;
            try
            {
                var resp = await Api.PlayerTierClient.FetchAsync(nickname, serverId).ConfigureAwait(false);
                if (resp?.Dungeons is { Count: > 0 })
                {
                    var pick = resp.Dungeons[0];
                    if (_pipeline?.CurrentDungeonId is int dgId)
                    {
                        var current = resp.Dungeons.Find(d => d.DungeonId == dgId);
                        if (current != null) pick = current;
                    }
                    if (!string.IsNullOrWhiteSpace(pick.Tier)) tier = pick.Tier;
                }
                else if (resp != null)
                {
                    tier = "Unranked";
                }
            }
            catch { }

            if (IsDisposed) return;
            try
            {
                BeginInvoke(() =>
                {
                    _partyTierLoading.Remove(key);
                    if (tier != null)
                        _partyTierCache[key] = tier;
                    _renderer?.SetPartyData(BuildPartyRows());
                    RequestRender();
                });
            }
            catch { }
        });
    }

    private static string TierKey(string nickname, int serverId)
        => $"{serverId}\u001f{nickname.Trim()}";

    /// Render the D2D frame and present via UpdateLayeredWindow.
    private void RequestRender()
    {
        if (_renderer == null || !IsHandleCreated || IsDisposed) return;
        if (Width <= 0 || Height <= 0) return;
        _renderer.RenderFrame(Width, Height);
        _renderer.PresentToLayeredWindow(Handle, Left, Top, Width, Height);
    }

    private void OnAionActiveChanged(bool active)
    {
        if (!AppSettings.Instance.OverlayOnlyWhenAion) return;
        if (active) ShowOverlay();
        else        HideOverlay();
    }

    /// Called from TrayManager when the setting is toggled.
    public void SetOverlayOnlyWhenAion(bool enabled)
    {
        if (enabled)
        {
            _fgWatcher?.Start();
            if (_fgWatcher != null && !_fgWatcher.IsActive)
                HideOverlay();
        }
        else
        {
            _fgWatcher?.Stop();
            ShowOverlay();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _fgWatcher?.Dispose();
        _lockBtn?.Close();
        _pipeline?.Dispose();
        _protocol?.Dispose();
        _renderer?.Dispose();
        base.OnFormClosed(e);
    }

    private void OpenHistory()
    {
        if (_pipeline == null) return;
        var form = Windows.Open<CombatHistoryForm>();
        form.FormClosed += (_, _) => _pipeline.ExitHistoryView();
        form.SetData(_pipeline.History, record =>
        {
            _pipeline.EnterHistoryView();
            var rows = _pipeline.MapSnapshotForCanvas(record.Snapshot);
            var summary = new DpsCanvas.SessionSummary(
                DurationSec:    record.DurationSec,
                TotalDamage:    record.TotalDamage,
                AverageDps:     record.AverageDps,
                PeakDps:        record.PeakDps,
                TopActorName:   rows.Count > 0 ? rows[0].Name : "",
                TopActorDamage: rows.Count > 0 ? rows[0].Damage : 0,
                BossName:       record.BossName);
            string timer = $"{(int)record.DurationSec / 60}:{(int)record.DurationSec % 60:00}";
            _renderer?.SetData(rows, record.TotalDamage, timer, record.Snapshot.Target, summary);
            RequestRender();
        });
    }

    private void OpenSettings()
    {
        var form = Windows.Open<SettingsPanelForm>();
        form.SettingsChanged += () =>
        {
            _renderer?.ApplySettings();
            RequestRender();
        };
        form.FormClosed += (_, _) =>
        {
            Hotkeys?.RegisterFromSettings(AppSettings.Instance.Shortcuts);
        };
    }

    private void OnOpacitySlider(int value)
    {
        AppSettings.Instance.Opacity = value;
        AppSettings.Instance.SaveDebounced();
        RequestRender();
    }

    private LockButtonForm? _lockBtn;

    private void SetLocked(bool locked)
    {
        _locked = locked;
        _renderer?.SetLocked(locked);
        var ex = Win32Native.GetWindowLong(Handle, Win32Native.GWL_EXSTYLE);
        bool passthrough = locked || (_renderer?.CompactMode ?? false);
        if (passthrough) ex |=  Win32Native.WS_EX_TRANSPARENT;
        else             ex &= ~Win32Native.WS_EX_TRANSPARENT;
        Win32Native.SetWindowLong(Handle, Win32Native.GWL_EXSTYLE, ex);

        if (locked)
        {
            _lockBtn ??= new LockButtonForm(this);
            _lockBtn.PlaceNear(this);
            _lockBtn.Show();
        }
        else
        {
            _lockBtn?.Hide();
        }
        RequestRender();
    }

    public void Unlock()
    {
        SetLocked(false);
        _lockBtn?.Hide();
    }

    public void ShowOverlay()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();
        RequestRender();
    }

    public void HideOverlay()
    {
        Hide();
    }

    public void ToggleVisibility()
    {
        if (Visible) HideOverlay(); else ShowOverlay();
    }

    public void ToggleCompact()
    {
        if (_renderer == null) return;
        bool compact = !_renderer.CompactMode;
        _renderer.CompactMode = compact;

        // Compact mode = full click-through
        var ex = Win32Native.GetWindowLong(Handle, Win32Native.GWL_EXSTYLE);
        if (compact) ex |=  Win32Native.WS_EX_TRANSPARENT;
        else if (!_locked) ex &= ~Win32Native.WS_EX_TRANSPARENT;
        Win32Native.SetWindowLong(Handle, Win32Native.GWL_EXSTYLE, ex);

        RequestRender();
    }

    public void TriggerClearShortcut()  => _pipeline?.Reset();
    public void TriggerSwitchTab()
    {
        if (_renderer == null) return;
        _renderer.ActiveTab = _renderer.ActiveTab == OverlayRenderer.TabId.Dps
            ? OverlayRenderer.TabId.Party
            : OverlayRenderer.TabId.Dps;
        RequestRender();
    }

    public void TriggerRestart()
    {
        var exe = Environment.ProcessPath;
        // Validate before spawning: the user may have moved/deleted the binary
        // (typical case: ran from Downloads, then cleaned the folder) — in that
        // case Process.Start throws Win32Exception (2) "file not found" and the
        // app would die without restart. Notify and stay alive instead.
        if (string.IsNullOrEmpty(exe) || !System.IO.File.Exists(exe))
        {
            try
            {
                MessageBox.Show(
                    $"A2Meter 실행 파일을 찾을 수 없어 재시작할 수 없습니다.\n경로: {exe ?? "(unknown)"}",
                    "A2Meter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch { }
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(exe);
        }
        catch (Exception ex)
        {
            try
            {
                MessageBox.Show(
                    $"A2Meter 재시작에 실패했습니다: {ex.Message}",
                    "A2Meter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch { }
            return;
        }
        Environment.Exit(0);
    }

    public void TriggerAnonymousToggle()
    {
        _anonymous = !_anonymous;
        _renderer?.SetAnonymous(_anonymous);
        RequestRender();
    }

    private void OnCountdownClicked()
    {
        if (_pipeline == null) return;
        _pipeline.CycleCountdown();
        if (_renderer != null)
        {
            _renderer.CountdownSec = _pipeline.CountdownSeconds;
            _renderer.CountdownExpired = _pipeline.CountdownExpired;
        }
        RequestRender();
    }

    public void RequestAppClose()
    {
        _appCloseRequested = true;
        Windows.CloseAll();
        AppCloseRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    // ── WndProc: hit testing, mouse interaction ──

    protected override void WndProc(ref Message m)
    {
        if (IsDisposed || !IsHandleCreated) return;

        if (m.Msg == HotkeyManager.WM_HOTKEY)
        {
            Hotkeys?.ProcessHotkey(m.WParam.ToInt32());
            return;
        }

        // ── Snap-to-edge while dragging/resizing ──
        if (m.Msg == Win32Native.WM_MOVING)
        {
            SnapEdges(m.LParam);
            m.Result = IntPtr.Zero;
        }

        if (m.Msg == Win32Native.WM_NCHITTEST && !_locked)
        {
            int lp = unchecked((int)(long)m.LParam);
            var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));

            int hit = HitTestEdges(pt);
            if (hit != Win32Native.HTCLIENT)
            {
                m.Result = (IntPtr)hit;
                return;
            }

            // Drag from the toolbar area — but only outside of buttons/slider.
            if (_renderer != null && _renderer.IsDragArea(pt))
            {
                m.Result = (IntPtr)Win32Native.HTCAPTION;
                return;
            }
        }

        // Mouse interaction for D2D toolbar + row clicks
        if (m.Msg == WM_MOUSEMOVE && _renderer != null)
        {
            if (!_trackingMouse)
            {
                var tme = new TRACKMOUSEEVENT
                {
                    cbSize = Marshal.SizeOf<TRACKMOUSEEVENT>(),
                    dwFlags = TME_LEAVE,
                    hwndTrack = Handle,
                };
                TrackMouseEvent(ref tme);
                _trackingMouse = true;
            }

            int lp = unchecked((int)(long)m.LParam);
            var pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));

            if (_sliderDragging)
            {
                float ratio = _renderer.SliderValueFromX(pt.X);
                int val = 20 + (int)(ratio * 80);
                OnOpacitySlider(Math.Clamp(val, 20, 100));
                return;
            }

            var zone = _renderer.HitTest(pt);
            _renderer.SetHoveredZone(zone);
            RequestRender();
        }

        if (m.Msg == WM_MOUSELEAVE)
        {
            _trackingMouse = false;
            _renderer?.SetHoveredZone(OverlayRenderer.ZoneId.None);
            RequestRender();
        }

        if (m.Msg == WM_LBUTTONDOWN && _renderer != null)
        {
            int lp = unchecked((int)(long)m.LParam);
            var pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            var zone = _renderer.HitTest(pt);

            if (zone == OverlayRenderer.ZoneId.Slider)
            {
                _sliderDragging = true;
                SetCapture(Handle);
                float ratio = _renderer.SliderValueFromX(pt.X);
                int val = 20 + (int)(ratio * 80);
                OnOpacitySlider(Math.Clamp(val, 20, 100));
                return;
            }
        }

        if (m.Msg == WM_LBUTTONUP && _renderer != null)
        {
            if (_sliderDragging)
            {
                _sliderDragging = false;
                ReleaseCapture();
                return;
            }

            int lp = unchecked((int)(long)m.LParam);
            var pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            var zone = _renderer.HitTest(pt);

            switch (zone)
            {
                case OverlayRenderer.ZoneId.Lock:
                    SetLocked(!_locked);
                    break;
                case OverlayRenderer.ZoneId.Anon:
                    TriggerAnonymousToggle();
                    break;
                case OverlayRenderer.ZoneId.History:
                    OpenHistory();
                    break;
                case OverlayRenderer.ZoneId.Settings:
                    OpenSettings();
                    break;
                case OverlayRenderer.ZoneId.Close:
                    RequestAppClose();
                    break;
                case OverlayRenderer.ZoneId.Countdown:
                    OnCountdownClicked();
                    break;
                case OverlayRenderer.ZoneId.CpToggle:
                {
                    var s = AppSettings.Instance;
                    s.ShowCombatPower = !s.ShowCombatPower;
                    s.SaveDebounced();
                    RequestRender();
                    break;
                }
                case OverlayRenderer.ZoneId.ScoreToggle:
                {
                    var s = AppSettings.Instance;
                    s.ShowCombatScore = !s.ShowCombatScore;
                    s.SaveDebounced();
                    RequestRender();
                    break;
                }
                case OverlayRenderer.ZoneId.TabDps:
                    if (_renderer.ActiveTab != OverlayRenderer.TabId.Dps)
                    { _renderer.ActiveTab = OverlayRenderer.TabId.Dps; RequestRender(); }
                    break;
                case OverlayRenderer.ZoneId.TabParty:
                    if (_renderer.ActiveTab != OverlayRenderer.TabId.Party)
                    { _renderer.ActiveTab = OverlayRenderer.TabId.Party; RequestRender(); }
                    break;
                case OverlayRenderer.ZoneId.None:
                {
                    // Row click
                    int rowIdx = _renderer.RowHitTest(pt.Y);
                    if (rowIdx >= 0)
                        OnPlayerRowClicked(rowIdx);
                    break;
                }
            }
        }

        try { base.WndProc(ref m); }
        catch (ObjectDisposedException) { }
    }

    private void OnPlayerRowClicked(int rowIdx)
    {
        var rows = _renderer?.GetRows();
        if (rows == null || rowIdx < 0 || rowIdx >= rows.Count) return;
        var detail = Windows.Open<DpsDetailForm>();
        detail.SetData(rows[rowIdx]);
    }

    private int HitTestEdges(Point pt)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        bool L = pt.X < ResizeMargin;
        bool R = pt.X >= w - ResizeMargin;
        bool T = pt.Y < ResizeMargin;
        bool B = pt.Y >= h - ResizeMargin;

        if (T && L) return Win32Native.HTTOPLEFT;
        if (T && R) return Win32Native.HTTOPRIGHT;
        if (B && L) return Win32Native.HTBOTTOMLEFT;
        if (B && R) return Win32Native.HTBOTTOMRIGHT;
        if (L) return Win32Native.HTLEFT;
        if (R) return Win32Native.HTRIGHT;
        if (T) return Win32Native.HTTOP;
        if (B) return Win32Native.HTBOTTOM;
        return Win32Native.HTCLIENT;
    }

    // ── Snap-to-edge (magnet) ──
    // 이미 가장자리에 붙어있으면 snap을 건너뜀 → 조금만 움직여도 즉시 떨어짐.

    private unsafe void SnapEdges(IntPtr lParam)
    {
        var settings = Core.AppSettings.Instance;
        if (!settings.SnapEnabled) return;
        int snapDist = Math.Clamp(settings.SnapDistance, 1, 8);

        ref var rc = ref *(RECT*)lParam;
        int winW = rc.Right - rc.Left;
        int winH = rc.Bottom - rc.Top;

        var screen = Screen.FromRectangle(new Rectangle(rc.Left, rc.Top, winW, winH));
        var wa = screen.WorkingArea;

        // Current position (before this move)
        var cur = Bounds;
        bool wasLeft  = cur.Left == wa.Left;
        bool wasRight = cur.Right == wa.Right;
        bool wasTop   = cur.Top == wa.Top;
        bool wasBottom = cur.Bottom == wa.Bottom;

        // Horizontal: only snap if NOT already attached to that edge
        if (!wasLeft && Math.Abs(rc.Left - wa.Left) < snapDist)
        { rc.Left = wa.Left; rc.Right = rc.Left + winW; }
        else if (!wasRight && Math.Abs(rc.Right - wa.Right) < snapDist)
        { rc.Right = wa.Right; rc.Left = rc.Right - winW; }

        // Vertical
        if (!wasTop && Math.Abs(rc.Top - wa.Top) < snapDist)
        { rc.Top = wa.Top; rc.Bottom = rc.Top + winH; }
        else if (!wasBottom && Math.Abs(rc.Bottom - wa.Bottom) < snapDist)
        { rc.Bottom = wa.Bottom; rc.Top = rc.Bottom - winH; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
}
