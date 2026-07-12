using System.Security.Cryptography;
using Namter.Cli;
using Namter.GameData.Builder;

namespace Namter.Tests.Integration.Golden;

public sealed class ReplayDeterminismTests
{
    [Fact]
    public async Task Replay_speeds_and_repeated_runs_preserve_semantic_artifact_bytes()
    {
        string root = Path.Combine(Path.GetTempPath(), "namter-determinism-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string repository = FindRepositoryRoot();
            string database = Path.Combine(root, "aion.db");
            await GameDataDatabaseBuilder.BuildAsync(
                database,
                Path.Combine(repository, "db", "schema", "001_initial.sql"),
                Path.Combine(repository, "db", "seed", "golden_protocol.sql"));
            string pcap = Path.Combine(root, "one-event.pcap");
            await File.WriteAllBytesAsync(pcap, OneEntityRemovalPcap());

            var outputs = new Dictionary<int, string>();
            foreach (int speed in new[] { 0, 1, 10 })
            {
                string output = Path.Combine(root, $"speed-{speed}");
                using var errors = new StringWriter();
                int exit = await CliApplication.RunAsync(
                    ["replay", "--input", pcap, "--data", database, "--output", output, "--speed", speed.ToString()],
                    TextWriter.Null,
                    errors);
                Assert.True(exit == 0, errors.ToString());
                outputs.Add(speed, output);
            }

            byte[] canonicalLedger = await File.ReadAllBytesAsync(Path.Combine(outputs[0], "event-ledger.json"));
            byte[] canonicalDiagnostics = await File.ReadAllBytesAsync(Path.Combine(outputs[0], "diagnostics.json"));
            Assert.True(canonicalLedger.AsSpan().IndexOf("EntityRemovedEvent"u8) >= 0);
            foreach (int speed in new[] { 1, 10 })
            {
                Assert.Equal(canonicalLedger, await File.ReadAllBytesAsync(Path.Combine(outputs[speed], "event-ledger.json")));
                Assert.Equal(canonicalDiagnostics, await File.ReadAllBytesAsync(Path.Combine(outputs[speed], "diagnostics.json")));
            }

            string repeat = Path.Combine(root, "speed-0-repeat");
            Assert.Equal(0, await CliApplication.RunAsync(
                ["replay", "--input", pcap, "--data", database, "--output", repeat, "--speed", "0"],
                TextWriter.Null,
                TextWriter.Null));
            Assert.Equal(HashArtifactSet(outputs[0]), HashArtifactSet(repeat));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string HashArtifactSet(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal))
        {
            byte[] name = System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, file).Replace('\\', '/'));
            hash.AppendData(name);
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static byte[] OneEntityRemovalPcap()
    {
        var packet = new byte[47];
        packet[0] = 0x45;
        packet[3] = 47;
        packet[8] = 64;
        packet[9] = 6;
        packet[12] = 10;
        packet[15] = 1;
        packet[16] = 10;
        packet[19] = 2;
        packet[20] = 0x34;
        packet[21] = 0x10;
        packet[22] = 0xc3;
        packet[23] = 0x50;
        packet[27] = 1;
        packet[32] = 0x50;
        packet[33] = 0x18;
        new byte[] { 0x0a, 0x21, 0x8d, 0xc9, 0x3f, 0x00, 0x01 }.CopyTo(packet, 40);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0xa1b2c3d4u);
        writer.Write((ushort)2);
        writer.Write((ushort)4);
        writer.Write(0);
        writer.Write(0u);
        writer.Write(65_535u);
        writer.Write(101u);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write((uint)packet.Length);
        writer.Write((uint)packet.Length);
        writer.Write(packet);
        return stream.ToArray();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Namter.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Namter repository root was not found.");
    }
}
