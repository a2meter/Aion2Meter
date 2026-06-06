using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace A2Meter.Api;

internal sealed class GameDataSnapshot
{
    public GameDataVersion? Version { get; set; }
    public ProtocolOpcodeSnapshot? Opcodes { get; set; }
    public List<GameSkillRow> Skills { get; set; } = new();
    public List<GameBuffRow> Buffs { get; set; } = new();
    public List<GameMobRow> Mobs { get; set; } = new();
    public List<GameDungeonRow> Dungeons { get; set; } = new();
    public List<GameDungeonBossRow> DungeonBosses { get; set; } = new();
}

internal sealed class GameDataVersion
{
    public string Tag { get; set; } = "";
    public string Hash { get; set; } = "";
}

internal sealed class GameSkillRow
{
    public int Code { get; set; }
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }
}

internal sealed class GameBuffRow
{
    public int Code { get; set; }
    public string Name { get; set; } = "";
}

internal sealed class GameMobRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int IsBoss { get; set; }
}

internal sealed class GameDungeonRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string BaseName { get; set; } = "";
    public string Tier { get; set; } = "";
}

internal sealed class GameDungeonBossRow
{
    public int DungeonId { get; set; }
    public int Ord { get; set; }
    public string BossName { get; set; } = "";
    public int? MobId { get; set; }
    public int? Level { get; set; }
    public int? Grade { get; set; }
    public int? IsFinalBoss { get; set; }
}

internal sealed class ProtocolOpcodeSnapshot
{
    public int[]? PacketMagic { get; set; }
    public Dictionary<string, int[]> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Party { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal static class GameDataClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Lazy<GameDataSnapshot> SnapshotLazy = new(LoadSnapshot);
    public static string BaseUrl { get; set; } = Environment.GetEnvironmentVariable("A2METER_API_BASE_URL") ?? "https://api.aion2meter.com";

    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "gamedata");

    private static readonly string BootstrapCachePath =
        Path.Combine(CacheDir, "meter-bootstrap.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static GameDataSnapshot Snapshot => SnapshotLazy.Value;

    private static GameDataSnapshot LoadSnapshot()
    {
        try
        {
            var baseUrl = (BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "https://api.aion2meter.com";

            var json = Http.GetStringAsync($"{baseUrl}/api/gamedata/meter-bootstrap")
                .GetAwaiter()
                .GetResult();

            var snapshot = DeserializeSnapshot(json);
            SaveBootstrapCache(json);
            return snapshot;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[gamedata] server bootstrap failed: " + ex.Message);
        }

        try
        {
            if (!File.Exists(BootstrapCachePath)) return new GameDataSnapshot();

            var cachedJson = File.ReadAllText(BootstrapCachePath);
            var snapshot = DeserializeSnapshot(cachedJson);
            Console.Error.WriteLine("[gamedata] using cached bootstrap: " + BootstrapCachePath);
            return snapshot;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[gamedata] cached bootstrap failed: " + ex.Message);
            return new GameDataSnapshot();
        }
    }

    private static GameDataSnapshot DeserializeSnapshot(string json)
        => JsonSerializer.Deserialize<GameDataSnapshot>(json, JsonOptions) ?? new GameDataSnapshot();

    private static void SaveBootstrapCache(string json)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var tmp = BootstrapCachePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, BootstrapCachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[gamedata] bootstrap cache save failed: " + ex.Message);
        }
    }
}
