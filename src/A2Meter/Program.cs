using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Direct2D;
using A2Meter.Dps;
using A2Meter.Forms;
using D2DColor = Vortice.Mathematics.Color4;

namespace A2Meter;

internal static class Program
{
    private static Mutex? _mutex;

    private static readonly string CrashLogPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log");

    private static readonly string CrashLogPathAlt =
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "A2Meter", "crash.log");

    // ── Native crash handler (D2D/Vortice access violations) ──
    private delegate int UnhandledExceptionFilterDelegate(IntPtr exceptionPointers);
    private static UnhandledExceptionFilterDelegate? _nativeFilterRef;   // prevent GC

    [DllImport("kernel32.dll")]
    private static extern IntPtr SetUnhandledExceptionFilter(UnhandledExceptionFilterDelegate lpTopLevelExceptionFilter);

    [StructLayout(LayoutKind.Sequential)]
    private struct EXCEPTION_POINTERS { public IntPtr ExceptionRecord; public IntPtr ContextRecord; }

    [StructLayout(LayoutKind.Sequential)]
    private struct EXCEPTION_RECORD { public uint ExceptionCode; public uint ExceptionFlags; public IntPtr Next; public IntPtr ExceptionAddress; }

    // CLR managed exceptions are raised through SEH with this exception code
    // (the legacy "MSC \0" / "throw" code used by the CLR). They are already
    // handled by AppDomain.UnhandledException / Application.ThreadException
    // with a proper managed stack trace, so the native filter must skip them
    // — otherwise every managed crash gets reported twice, once as a useful
    // "UnhandledException" entry and once as a stack-less "NativeCrash".
    private const uint EXCEPTION_CLR = 0xE0434352u;

    private static int NativeExceptionFilter(IntPtr pExInfo)
    {
        try
        {
            if (pExInfo == IntPtr.Zero) return 0;
            var ptrs = Marshal.PtrToStructure<EXCEPTION_POINTERS>(pExInfo);
            if (ptrs.ExceptionRecord == IntPtr.Zero) return 0;
            var rec = Marshal.PtrToStructure<EXCEPTION_RECORD>(ptrs.ExceptionRecord);

            // Managed CLR exception — let the managed handlers report it instead.
            if (rec.ExceptionCode == EXCEPTION_CLR) return 0;

            string detail = $"code=0x{rec.ExceptionCode:X8} addr=0x{rec.ExceptionAddress:X}";
            WriteCrashLog("NativeCrash", new Exception($"Native exception: {detail}"));
        }
        catch { }
        return 0;   // EXCEPTION_CONTINUE_SEARCH
    }

    [STAThread]
    private static void Main(string[] args)
    {
        // Native crash filter — catches D2D / Vortice access violations.
        _nativeFilterRef = NativeExceptionFilter;
        SetUnhandledExceptionFilter(_nativeFilterRef);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("UnhandledException", e.ExceptionObject as Exception);

        Application.ThreadException += (_, e) =>
            WriteCrashLog("ThreadException", e.Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        try
        {

        var parsed = ParseArgs(args);

        if (parsed.Demo)
        {
            RunDemo();
            return;
        }

        var (replayDir, replayRealtime, replaySpeed, _) = parsed;

        if (replayDir is null)
        {
            _mutex = new Mutex(true, "A2Meter.SingleInstance.Mutex", out bool createdNew);
            if (!createdNew) return;
        }

        // ── WinForms + D2D mode (default) ──
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var settings = AppSettings.Instance;

        // Show setup dialog if prerequisites are missing.
        if (NeedsSetup())
        {
            using var setup = new Forms.SetupForm();
            if (setup.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;
        }

        // Fire-and-forget: post any new crash entries to the proxy. Disabled by
        // default; only runs when the user has opted in via SettingsPanelForm.
        _ = Task.Run(() => CrashReporter.ReportPendingAsync(settings));

        // Fire-and-forget: check for game data updates.
        _ = Task.Run(() => Data.DataManager.CheckForUpdateAsync());

        using var overlay = new OverlayForm();

        if (replayDir is not null)
        {
            overlay.PacketSourceOverride = new PcapReplaySource(replayDir, realtime: replayRealtime, speed: replaySpeed);
            overlay.Text = $"A2Meter [replay: {System.IO.Path.GetFileName(replayDir)}]";
            overlay.Tag  = System.IO.Path.GetFileName(replayDir);
        }

        overlay.HandleCreated += (_, _) =>
        {
            var hk = new HotkeyManager(overlay);
            overlay.Hotkeys = hk;
            hk.RegisterFromSettings(settings.Shortcuts);

            // 본체 실행 경로를 appdata에 기록 (업데이터가 이 경로의 exe를 교체).
            AutoUpdater.PersistInstallPath(msg => Console.Error.WriteLine(msg));

            // 업데이터 자동 배치 + 업데이트 확인.
            _ = Task.Run(async () =>
            {
                await AutoUpdater.EnsureUpdaterAsync(msg => Console.Error.WriteLine(msg));
                var result = await AutoUpdater.CheckAsync(msg => Console.Error.WriteLine(msg));
                if (result.HasValue)
                {
                    var (ver, url, notes) = result.Value;
                    overlay.Invoke(() =>
                    {
                        var toast = new Forms.UpdateToastForm(overlay, ver, url, notes);
                        toast.Show();
                    });
                }
            });
        };

        using var tray = new TrayManager(
            overlay,
            getOverlayOnlyWhenAion: () => settings.OverlayOnlyWhenAion,
            setOverlayOnlyWhenAion: v =>
            {
                settings.OverlayOnlyWhenAion = v;
                settings.SaveDebounced();
            });

        overlay.AppCloseRequested += (_, _) =>
        {
            settings.Save();
            Application.Exit();
        };

        Application.Run(overlay);

        }
        catch (Exception ex)
        {
            WriteCrashLog("Main", ex);
            throw;
        }
    }

    private static void RunDemo()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var canvas = new DpsCanvas { Dock = DockStyle.Fill };

        var form = new Form
        {
            Text = "A2Meter [demo]",
            FormBorderStyle = FormBorderStyle.Sizable,
            StartPosition = FormStartPosition.CenterScreen,
            Size = new System.Drawing.Size(460, 500),
            BackColor = System.Drawing.Color.FromArgb(8, 11, 20),
        };
        form.Controls.Add(canvas);

        // Dummy data: boss 1, party 4 (수호성, 살성, 마도성, 치유성)
        // Colors match original A2Viewer palette.
        var skills = new List<DpsCanvas.SkillBar>
        {
            new("철벽 방어",   3_200_000, 142, 0.35, 0.38),
            new("심판의 일격", 2_100_000,  98, 0.42, 0.25),
            new("도발 강타",   1_500_000,  76, 0.28, 0.18),
            new("방패 돌진",     980_000,  54, 0.31, 0.12),
            new("수호의 맹세",   450_000,  32, 0.15, 0.05),
        };

        var rows = new List<DpsCanvas.PlayerRow>
        {
            new("수호성",   "수호성", 8_450_000, 1.00, 352_083, 0.34, 0,
                new D2DColor(0.490f, 0.627f, 0.976f, 1f), new D2DColor(0.490f, 0.627f, 0.976f, 1f),
                skills, 42000, 410_000, 352_083, 120_000),
            new("살성",    "살성",   7_820_000, 0.93, 325_833, 0.48, 0,
                new D2DColor(0.643f, 0.906f, 0.608f, 1f), new D2DColor(0.643f, 0.906f, 0.608f, 1f),
                null, 38500, 398_000, 325_833, 0),
            new("마도성",  "마도성", 6_950_000, 0.82, 289_583, 0.41, 0,
                new D2DColor(0.718f, 0.549f, 0.949f, 1f), new D2DColor(0.718f, 0.549f, 0.949f, 1f),
                null, 35200, 372_000, 289_583, 1_200_000),
            new("치유성",  "치유성", 2_180_000, 0.26, 90_833,  0.22, 4_500_000,
                new D2DColor(0.906f, 0.812f, 0.490f, 1f), new D2DColor(0.906f, 0.812f, 0.490f, 1f),
                null, 31000, 120_000, 90_833, 0),
        };

        long total = 0;
        foreach (var r in rows) total += r.Damage;

        var target = new MobTarget
        {
            Name = "글래스베인",
            EntityId = 99999,
            CurrentHp = 18_600_000,
            MaxHp = 26_330_000,
            IsBoss = true,
        };

        DpsDetailForm? detailForm = null;
        canvas.PlayerRowClicked += row =>
        {
            if (detailForm == null || detailForm.IsDisposed)
            {
                detailForm = new DpsDetailForm();
                detailForm.Show(form);
            }
            detailForm.SetData(row);
            detailForm.BringToFront();
        };

        form.Shown += (_, _) =>
        {
            canvas.SetData(rows, total, "1:24", target);
        };

        Application.Run(form);
    }

    /// CLI:
    ///   A2Meter                                    # live capture
    ///   A2Meter --replay <session-dir>             # offline replay, realtime
    ///   A2Meter --replay <dir> --speed 4           # replay 4x faster
    ///   A2Meter --replay <dir> --fast              # replay as fast as possible
    ///   A2Meter --demo                              # dummy data preview
    private static (string? Dir, bool Realtime, double Speed, bool Demo) ParseArgs(string[] args)
    {
        string? dir = null;
        bool realtime = true;
        double speed = 1.0;
        bool demo = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--replay": dir = args[++i]; break;
                case "--speed":  speed = double.Parse(args[++i]); break;
                case "--fast":   realtime = false; break;
                case "--demo":   demo = true; break;
            }
        }
        return (dir, realtime, speed, demo);
    }

    /// Returns true if the setup dialog should be shown (Npcap missing or data not downloaded).
    private static bool NeedsSetup()
    {
        if (!Data.DataManager.IsReady) return true;

        // Check Npcap via registry.
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Npcap");
            if (key != null) return false;
        }
        catch { }

        // Check Npcap DLL presence.
        var npcapDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "Npcap");
        if (System.IO.File.Exists(System.IO.Path.Combine(npcapDir, "wpcap.dll")))
            return false;

        var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (System.IO.File.Exists(System.IO.Path.Combine(sys32, "wpcap.dll")))
            return false;

        return true;
    }

    private static void WriteCrashLog(string source, Exception? ex)
    {
        var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n";
        try { System.IO.File.AppendAllText(CrashLogPath, msg); } catch { }
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashLogPathAlt)!);
            System.IO.File.AppendAllText(CrashLogPathAlt, msg);
        }
        catch { }
    }
}
