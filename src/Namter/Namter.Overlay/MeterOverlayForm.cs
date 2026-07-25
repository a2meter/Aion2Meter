using System.Drawing;
using System.Windows.Forms;

namespace Namter.Overlay;

/// Frameless, always-on-top, per-pixel-alpha layered window. All pixels come
/// from the Direct2D renderer via UpdateLayeredWindow — there is no WinForms
/// painting. The form only owns input (drag/resize/close) and the render timer;
/// it reads the engine's immutable MeterView and never touches the reducer.
internal sealed class MeterOverlayForm : Form
{
    private const int HeaderHeight = 34;
    private const int ResizeMargin = 6;

    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1, HTCAPTION = 2;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    private readonly LiveMeterEngine _engine;
    private readonly System.Windows.Forms.Timer _timer;
    private D2DMeterRenderer? _renderer;
    private Rectangle _closeRect;

    public MeterOverlayForm(LiveMeterEngine engine)
    {
        _engine = engine;

        Text = "Namter";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(80, 80);
        Size = new Size(440, 460);
        MinimumSize = new Size(320, 200);
        TopMost = true;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.Opaque, true);
        UpdateCloseRect();

        _timer = new System.Windows.Forms.Timer { Interval = 150 };
        _timer.Tick += (_, _) => Render();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _renderer = new D2DMeterRenderer();
        _renderer.Init();
        Render();
        _timer.Start();
    }

    private void Render()
    {
        if (_renderer is null || IsDisposed || Width <= 0 || Height <= 0) return;
        try
        {
            _renderer.RenderFrame(_engine.Latest, _engine.FatalError, Width, Height);
            _renderer.Present(Handle, Left, Top);
        }
        catch
        {
            // Skip a frame on transient device errors rather than crashing the UI.
        }
    }

    private void UpdateCloseRect() => _closeRect = new Rectangle(Width - HeaderHeight, 0, HeaderHeight, HeaderHeight);

    protected override void OnMove(EventArgs e) { base.OnMove(e); Render(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateCloseRect(); Render(); }
    protected override void OnPaintBackground(PaintEventArgs e) { /* layered window paints via UpdateLayeredWindow */ }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool over = _closeRect.Contains(e.Location);
        if (_renderer is not null && _renderer.CloseHovered != over) { _renderer.CloseHovered = over; Render(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_renderer is not null && _renderer.CloseHovered) { _renderer.CloseHovered = false; Render(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _closeRect.Contains(e.Location)) Close();
        base.OnMouseClick(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            var screen = new Point(unchecked((int)(long)m.LParam));
            Point pt = PointToClient(screen);

            int w = ClientSize.Width, h = ClientSize.Height;
            bool left = pt.X < ResizeMargin, right = pt.X >= w - ResizeMargin;
            bool top = pt.Y < ResizeMargin, bottom = pt.Y >= h - ResizeMargin;
            if (top && left) { m.Result = HTTOPLEFT; return; }
            if (top && right) { m.Result = HTTOPRIGHT; return; }
            if (bottom && left) { m.Result = HTBOTTOMLEFT; return; }
            if (bottom && right) { m.Result = HTBOTTOMRIGHT; return; }
            if (left) { m.Result = HTLEFT; return; }
            if (right) { m.Result = HTRIGHT; return; }
            if (top) { m.Result = HTTOP; return; }
            if (bottom) { m.Result = HTBOTTOM; return; }
            if (_closeRect.Contains(pt)) { m.Result = HTCLIENT; return; }
            if (pt.Y < HeaderHeight) { m.Result = HTCAPTION; return; }
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _renderer?.Dispose();
        _ = _engine.DisposeAsync();
        base.OnFormClosed(e);
    }
}
