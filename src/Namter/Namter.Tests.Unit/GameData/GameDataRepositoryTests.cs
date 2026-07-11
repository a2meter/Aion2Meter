using System.Collections.Frozen;
using Microsoft.Data.Sqlite;
using Namter.GameData;
using Namter.GameData.Builder;

namespace Namter.Tests.Unit.GameData;

public sealed class GameDataRepositoryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task BuilderCreatesRequiredSchemaWithForeignKeysAndLookupIndexes()
    {
        using var fixture = await DatabaseFixture.CreateAsync();
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = fixture.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
        await connection.OpenAsync();

        var tables = await ReadNamesAsync(connection, "table");
        var requiredTables = new[]
        {
            "metadata", "protocol_profiles", "opcodes", "message_layouts", "bosses",
            "dungeons", "dungeon_bosses", "mobs", "skills", "buffs",
        };
        Assert.All(requiredTables, table => Assert.Contains(table, tables));

        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            await using var reader = await foreignKeys.ExecuteReaderAsync();
            Assert.False(await reader.ReadAsync());
        }

        var indexes = await ReadNamesAsync(connection, "index");
        Assert.Contains("idx_protocol_profiles_active", indexes);
        Assert.Contains("idx_opcodes_profile_kind", indexes);
        Assert.Contains("idx_opcodes_profile_name", indexes);
        Assert.Contains("idx_bosses_name", indexes);
        Assert.Contains("idx_dungeons_code", indexes);
        Assert.Contains("idx_mobs_code", indexes);
        Assert.Contains("idx_skills_code", indexes);
        Assert.Contains("idx_buffs_code", indexes);
    }

    [Fact]
    public async Task BuilderSeedsExactVersionedGoldenProtocolDataWithoutEncounterActorIds()
    {
        using var fixture = await DatabaseFixture.CreateAsync();
        await using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Mode=ReadOnly");
        await connection.OpenAsync();

        Assert.Equal("aion2-2026-07-10", await ScalarAsync<string>(connection,
            "SELECT name FROM protocol_profiles WHERE is_active = 1;"));
        Assert.Equal("060036", await ScalarAsync<string>(connection,
            "SELECT hex(packet_magic) FROM protocol_profiles WHERE is_active = 1;"));
        Assert.Equal(151L, await ScalarAsync<long>(connection,
            "SELECT party_marker FROM protocol_profiles WHERE is_active = 1;"));
        Assert.Equal(new[] { 13328L }, await ReadInt64sAsync(connection,
            "SELECT port FROM protocol_profile_ports ORDER BY port;"));
        Assert.Equal(
            new[] { "0438", "0538", "2A38", "2B38", "3336", "4536", "4136", "018D", "0336", "218D", "4F36" },
            await ReadStringsAsync(connection,
                "SELECT hex(tag) FROM opcodes WHERE family = 1 ORDER BY id;"));
        Assert.Equal(new[] { "0197", "0297", "0497", "0797", "0B97", "1397", "1D97", "2A97" },
            await ReadStringsAsync(connection,
                "SELECT hex(tag) FROM opcodes WHERE family = 2 ORDER BY kind;"));
        Assert.Equal(new[] { 2301721L, 2301722L, 2301723L }, await ReadInt64sAsync(connection,
            "SELECT code FROM bosses ORDER BY code;"));
        Assert.Equal(new[] { "Turgen", "Griosa", "Basilus" }, await ReadStringsAsync(connection,
            "SELECT name FROM bosses ORDER BY code;"));
        Assert.Equal(600153L, await ScalarAsync<long>(connection,
            "SELECT code FROM dungeons;"));

        foreach (var actorId in new[] { "18804", "36737", "28353" })
        {
            Assert.DoesNotContain(actorId, await File.ReadAllTextAsync(Path.Combine(RepositoryRoot, "db", "seed", "golden_protocol.sql")));
        }
    }

    [Fact]
    public async Task BuilderPreservesExistingOutputAndCleansTemporaryDatabaseWhenTransactionFails()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"namter-builder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string output = Path.Combine(directory, "aion.db");
            string invalidSeed = Path.Combine(directory, "invalid-seed.sql");
            byte[] sentinel = "existing-valid-database"u8.ToArray();
            await File.WriteAllBytesAsync(output, sentinel);
            await File.WriteAllTextAsync(invalidSeed, "INSERT INTO table_that_does_not_exist VALUES (1);");

            await Assert.ThrowsAsync<SqliteException>(() => GameDataDatabaseBuilder.BuildAsync(
                output,
                Path.Combine(RepositoryRoot, "db", "schema", "001_initial.sql"),
                invalidSeed));

            Assert.Equal(sentinel, await File.ReadAllBytesAsync(output));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task BuilderAtomicallyReplacesExistingOutputAndLeavesNoTemporaryDatabase()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"namter-builder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string output = Path.Combine(directory, "aion.db");
            await File.WriteAllTextAsync(output, "old-output");

            await GameDataDatabaseBuilder.BuildAsync(
                output,
                Path.Combine(RepositoryRoot, "db", "schema", "001_initial.sql"),
                Path.Combine(RepositoryRoot, "db", "seed", "golden_protocol.sql"));

            Assert.Equal("SQLite format 3\0"u8.ToArray(), (await File.ReadAllBytesAsync(output))[..16]);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void NativeProjectHasNoSqliteDependency()
    {
        string project = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Namter", "Namter.Core.Native", "Namter.Core.Native.vcxproj"));

        Assert.DoesNotContain("sqlite", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepositoryLoadsOnlyTheActiveProfileIntoFrozenBoundedCaches()
    {
        using var fixture = await DatabaseFixture.CreateAsync();
        await fixture.ExecuteAsync("""
            INSERT INTO protocol_profiles(id, name, version, packet_magic, party_marker, is_active)
            VALUES (2, 'inactive-profile', 999, X'AABBCC', 151, 0);
            INSERT INTO message_layouts(id, profile_id, name, max_payload_bytes)
            VALUES (999, 2, 'inactive-layout', 64);
            INSERT INTO opcodes(id, profile_id, family, kind, name, tag, layout_id)
            VALUES (999, 2, 1, 999, 'inactive-opcode', X'FFFF', 999);
            """);

        var repository = new GameDataRepository(fixture.DatabasePath, GameDataCacheLimits.Default);
        GameDataSnapshot snapshot = await repository.LoadAsync();

        Assert.Equal(1UL, snapshot.DataVersion);
        Assert.Equal(1U, snapshot.SchemaVersion);
        Assert.Equal(1U, snapshot.ProtocolProfileVersion);
        Assert.Equal("aion2-2026-07-10", snapshot.ProtocolProfileName);
        Assert.Equal(new byte[] { 0x06, 0x00, 0x36 }, snapshot.PacketMagic);
        Assert.Equal(new ushort[] { 13328 }, snapshot.ServerPorts);
        Assert.DoesNotContain(snapshot.Opcodes.Values, opcode => opcode.Name == "inactive-opcode");
        Assert.DoesNotContain(snapshot.MessageLayouts.Values, layout => layout.Name == "inactive-layout");
        Assert.IsAssignableFrom<FrozenDictionary<uint, ProtocolOpcode>>(snapshot.Opcodes);
        Assert.IsAssignableFrom<FrozenDictionary<uint, Boss>>(snapshot.Bosses);
        Assert.IsAssignableFrom<FrozenDictionary<uint, Dungeon>>(snapshot.Dungeons);
        Assert.IsAssignableFrom<FrozenDictionary<uint, Skill>>(snapshot.Skills);
        Assert.IsAssignableFrom<FrozenDictionary<uint, Buff>>(snapshot.Buffs);
        Assert.InRange(snapshot.TotalHotCacheEntries, 1, GameDataCacheLimits.Default.MaxTotalEntries);
    }

    [Fact]
    public async Task RepositoryRejectsAConfiguredCacheEntryBoundBeforeMaterializingTheSnapshot()
    {
        using var fixture = await DatabaseFixture.CreateAsync();
        var limits = GameDataCacheLimits.Default with { MaxOpcodes = 1 };
        var repository = new GameDataRepository(fixture.DatabasePath, limits);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync());

        Assert.Contains("opcode", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepositoryRejectsTheConfiguredTotalHotCacheEntryBound()
    {
        using var fixture = await DatabaseFixture.CreateAsync();
        var limits = GameDataCacheLimits.Default with { MaxTotalEntries = 1 };
        var repository = new GameDataRepository(fixture.DatabasePath, limits);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync());

        Assert.Contains("Hot game-data cache", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryConnectionUsesReadOnlySharedCache()
    {
        var repository = new GameDataRepository("aion.db", GameDataCacheLimits.Default);
        var settings = new SqliteConnectionStringBuilder(repository.ConnectionString);

        Assert.Equal(SqliteOpenMode.ReadOnly, settings.Mode);
        Assert.Equal(SqliteCacheMode.Shared, settings.Cache);
        Assert.True(settings.ForeignKeys);
    }

    private static async Task<HashSet<string>> ReadNamesAsync(SqliteConnection connection, string type)
        => (await ReadStringsAsync(connection,
            $"SELECT name FROM sqlite_schema WHERE type = '{type}' AND name NOT LIKE 'sqlite_%';"))
            .ToHashSet(StringComparer.Ordinal);

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private static async Task<string[]> ReadStringsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result.ToArray();
    }

    private static async Task<long[]> ReadInt64sAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<long>();
        while (await reader.ReadAsync()) result.Add(reader.GetInt64(0));
        return result.ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Namter.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Namter repository root.");
    }

    private sealed class DatabaseFixture : IDisposable
    {
        private readonly string directory;
        public string DatabasePath { get; }

        private DatabaseFixture(string directory)
        {
            this.directory = directory;
            DatabasePath = Path.Combine(directory, "aion.db");
        }

        public static async Task<DatabaseFixture> CreateAsync()
        {
            var fixture = new DatabaseFixture(Path.Combine(Path.GetTempPath(), $"namter-db-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(fixture.directory);
            await GameDataDatabaseBuilder.BuildAsync(
                fixture.DatabasePath,
                Path.Combine(RepositoryRoot, "db", "schema", "001_initial.sql"),
                Path.Combine(RepositoryRoot, "db", "seed", "golden_protocol.sql"));
            return fixture;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }
}
