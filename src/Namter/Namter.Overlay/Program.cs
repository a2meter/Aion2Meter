using System.Text;
using Namter.Core.Interop;

namespace Namter.Overlay;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool selftest = Array.IndexOf(args, "--selftest") >= 0;
        string? data = null, backend = "windivert", replay = null, packetLog = null;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            switch (args[i])
            {
                case "--data": data = args[++i]; break;
                case "--backend": backend = args[++i]; break;
                case "--replay": replay = args[++i]; break;
                case "--packet-log": packetLog = args[++i]; break;
            }
        }

        if (string.IsNullOrEmpty(data) || !File.Exists(data))
        {
            MessageBox.Show(
                "사용법: Namter.Overlay --data <aion.db> [--backend windivert|npcap] [--replay <pcap>] [--packet-log <dir>]",
                "Namter Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        NativeSourceKind kind = (backend ?? "windivert").ToLowerInvariant() switch
        {
            "npcap" => NativeSourceKind.Npcap,
            _ => NativeSourceKind.WinDivert,
        };

        ReadOnlyMemory<byte> replayBytes = default;
        if (!string.IsNullOrEmpty(replay))
        {
            if (!File.Exists(replay))
            {
                MessageBox.Show($"리플레이 PCAP을 찾을 수 없습니다: {replay}",
                    "Namter Overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            replayBytes = File.ReadAllBytes(replay);
        }

        var engine = new LiveMeterEngine(data, kind, replayBytes, packetLog);
        engine.Start();

        if (selftest)
        {
            engine.Completion.Wait(TimeSpan.FromSeconds(60));
            MeterView v = engine.Latest;
            var sb = new StringBuilder();
            sb.AppendLine($"boss={v.BossName} elapsedMs={v.ElapsedMs} live={v.Live} rows={v.Rows.Length} total={v.TotalDamage} finished={engine.Finished}");
            foreach (MeterRow r in v.Rows)
                sb.AppendLine($"  {r.Name,-18} dmg={r.Damage,14} dps={r.DpsPerSec,12:0} share={r.BossHpShare * 100,6:0.0}% self={r.IsSelf}");
            if (engine.FatalError is not null) sb.AppendLine("FATAL: " + engine.FatalError);

            // Exercise the full D2D pipeline once (device, textures, draw, staging map,
            // UpdateLayeredWindow no-op on a null HWND) so startup crashes surface here.
            try
            {
                using var probe = new D2DMeterRenderer();
                probe.Init();
                probe.RenderFrame(v, null, 440, 460);
                probe.Present(IntPtr.Zero, 0, 0);
                sb.AppendLine("d2d-render: OK");
            }
            catch (Exception ex)
            {
                sb.AppendLine("d2d-render: FAIL " + ex.Message);
                Console.Out.Write(sb.ToString());
                Console.Out.Flush();
                return 3;
            }

            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return engine.FatalError is not null ? 2 : 0;
        }

        Application.Run(new MeterOverlayForm(engine));
        return 0;
    }
}
