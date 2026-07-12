using System.Text.Json;
using Namter.Cli;
using Namter.GameData;
using Namter.GameData.Builder;

namespace Namter.Tests.Integration.Golden;

public sealed class AionPart001Tests
{
    [Fact]
    public async Task Full_capture_locks_exact_encounters_totals_and_later_real_basilus()
    {
        string? capture = FindExternalCapture();
        if (capture is null)
        {
            Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NAMTER_FIXTURE_ROOT")),
                "NAMTER_FIXTURE_ROOT was set but aion2_part001.pcap was absent.");
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "namter-aion-part001-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string repository = FindRepositoryRoot();
            string database = Path.Combine(root, "aion.db");
            await GameDataDatabaseBuilder.BuildAsync(
                database,
                Path.Combine(repository, "db", "schema", "001_initial.sql"),
                Path.Combine(repository, "db", "seed", "golden_protocol.sql"));
            string output = Path.Combine(root, "replay");
            using var errors = new StringWriter();
            int exit = await CliApplication.RunAsync(
                ["replay", "--input", capture, "--data", database, "--output", output, "--speed", "0"],
                TextWriter.Null, errors);
            Assert.True(exit == 0, errors.ToString());

            using JsonDocument metadata = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(output, "metadata.json")));
            Assert.Equal(107_937, metadata.RootElement.GetProperty("eventCount").GetInt32());
            Assert.Equal(3, metadata.RootElement.GetProperty("encounterCount").GetInt32());
            Assert.Equal(6UL, metadata.RootElement.GetProperty("tcpOverlaps").GetUInt64());
            Assert.Equal(66UL, metadata.RootElement.GetProperty("tcpDuplicateBytesRemoved").GetUInt64());
            Assert.Equal(0UL, metadata.RootElement.GetProperty("tcpUnresolvedByteGaps").GetUInt64());
            Assert.True(metadata.RootElement.GetProperty("isComplete").GetBoolean());
            Assert.Empty(metadata.RootElement.GetProperty("incompleteReasons").EnumerateArray());
            using JsonDocument diagnostics = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(output, "diagnostics.json")));
            Assert.Empty(diagnostics.RootElement.EnumerateArray());

            JsonDocument[] encounters = await Task.WhenAll(Enumerable.Range(0, 3).Select(async index =>
                JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(output, "encounters", $"encounter-{index:D4}.json")))));
            try
            {
                Assert.Equal(new[] { 18_804u, 36_737u, 17_968u }, encounters.Select(BossActor).ToArray());
                Assert.Equal(new[] { 2_301_721u, 2_301_722u, 2_301_723u }, encounters.Select(BossCode).ToArray());
                Assert.All(encounters, encounter => Assert.Equal(600_153u,
                    encounter.RootElement.GetProperty("encounter").GetProperty("contentId").GetUInt32()));
                Assert.Equal(5, encounters[0].RootElement.GetProperty("participants").GetArrayLength());
                Assert.Equal(5, encounters[1].RootElement.GetProperty("participants").GetArrayLength());
                Assert.Equal((230_291_779UL, 49_648_275UL), Totals(encounters[0]));
                Assert.Equal((229_795_893UL, 48_619_527UL), Totals(encounters[1]));

                uint[] readableBossCandidates = [18_804u, 36_737u];
                Assert.All(readableBossCandidates, actor => Assert.Contains(actor, encounters.Select(BossActor)));
                Assert.Equal(17_968u, Assert.Single(encounters.Select(BossActor).Except(readableBossCandidates)));
            }
            finally
            {
                foreach (JsonDocument encounter in encounters) encounter.Dispose();
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static uint BossActor(JsonDocument value) =>
        value.RootElement.GetProperty("encounter").GetProperty("bossActorId").GetUInt32();

    private static uint BossCode(JsonDocument value) =>
        value.RootElement.GetProperty("encounter").GetProperty("bossCode").GetUInt32();

    private static (ulong DamageAndDot, ulong MultiDamage) Totals(JsonDocument value)
    {
        ulong damage = 0;
        ulong multi = 0;
        foreach (JsonElement participant in value.RootElement.GetProperty("participants").EnumerateArray())
        {
            damage += participant.GetProperty("damage").GetUInt64() + participant.GetProperty("dotDamage").GetUInt64();
            multi += participant.GetProperty("multiDamage").GetUInt64();
        }
        return (damage, multi);
    }

    private static string? FindExternalCapture()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("NAMTER_FIXTURE_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            string candidate = Path.Combine(overrideRoot, "aion2_part001.pcap");
            if (File.Exists(candidate)) return candidate;
        }

        DirectoryInfo? current = new(FindRepositoryRoot());
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "captures", "aion2_part001.pcap");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        return null;
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
