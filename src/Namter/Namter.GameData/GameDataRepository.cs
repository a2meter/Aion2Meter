using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Data;
using Microsoft.Data.Sqlite;

namespace Namter.GameData;

public sealed class GameDataRepository
{
    private readonly GameDataCacheLimits limits;

    public GameDataRepository(string databasePath, GameDataCacheLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        this.limits = limits;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();
    }

    public string ConnectionString { get; }

    public async Task<GameDataSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        (ulong dataVersion, uint schemaVersion) = await ReadMetadataAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        uint activeProfileId = await ReadActiveProfileIdAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        CacheCounts counts = await ReadCacheCountsAsync(connection, transaction, activeProfileId, cancellationToken)
            .ConfigureAwait(false);
        ValidateCacheCounts(counts);
        ActiveProfile profile = await ReadActiveProfileAsync(connection, transaction, activeProfileId, cancellationToken)
            .ConfigureAwait(false);
        ImmutableArray<ushort> ports = await ReadPortsAsync(connection, transaction, activeProfileId, cancellationToken)
            .ConfigureAwait(false);
        FrozenDictionary<ushort, ProtocolOpcode> opcodes = await ReadOpcodesAsync(
            connection, transaction, activeProfileId, counts.Opcodes, cancellationToken).ConfigureAwait(false);
        FrozenDictionary<uint, ProtocolMessageLayout> layouts = await ReadLayoutsAsync(
            connection, transaction, activeProfileId, counts.Layouts, counts.LayoutFields, cancellationToken).ConfigureAwait(false);
        FrozenDictionary<uint, Boss> bosses = await ReadBossesAsync(
            connection, transaction, counts.Bosses, cancellationToken)
            .ConfigureAwait(false);
        FrozenDictionary<uint, Dungeon> dungeons = await ReadNamedCodeMapAsync(
            connection, transaction, "dungeons", counts.Dungeons, static (code, name) => new Dungeon(code, name), cancellationToken)
            .ConfigureAwait(false);
        FrozenDictionary<uint, Skill> skills = await ReadNamedCodeMapAsync(
            connection, transaction, "skills", counts.Skills, static (code, name) => new Skill(code, name), cancellationToken)
            .ConfigureAwait(false);
        FrozenDictionary<uint, Buff> buffs = await ReadBuffsAsync(
            connection, transaction, counts.Buffs, cancellationToken)
            .ConfigureAwait(false);
        FrozenDictionary<ushort, ushort> jobAliases = await ReadJobAliasesAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        var snapshot = new GameDataSnapshot(
            dataVersion,
            schemaVersion,
            profile.Version,
            profile.Name,
            profile.PacketMagic,
            ports,
            opcodes,
            layouts,
            bosses,
            dungeons,
            skills,
            buffs,
            jobAliases);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    private static async Task<CacheCounts> ReadCacheCountsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint profileId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT
                (SELECT COUNT(*) FROM opcodes WHERE profile_id = $profile),
                (SELECT COUNT(DISTINCT kind) FROM opcodes WHERE profile_id = $profile),
                (SELECT COUNT(*) FROM message_layouts WHERE profile_id = $profile),
                (SELECT COUNT(*) FROM message_fields f
                    JOIN message_layouts l ON l.id = f.layout_id
                    WHERE l.profile_id = $profile),
                (SELECT COUNT(*) FROM bosses),
                (SELECT COUNT(*) FROM dungeons),
                (SELECT COUNT(*) FROM skills),
                (SELECT COUNT(*) FROM buffs);
            """);
        command.Parameters.AddWithValue("$profile", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("Could not count hot game-data rows.");
        }
        int opcodeCount = checked((int)reader.GetInt64(0));
        int distinctOpcodeKinds = checked((int)reader.GetInt64(1));
        if (opcodeCount != distinctOpcodeKinds)
        {
            throw new InvalidDataException("The active protocol profile contains duplicate wire opcode kinds.");
        }
        return new CacheCounts(
            opcodeCount,
            checked((int)reader.GetInt64(2)),
            checked((int)reader.GetInt64(3)),
            checked((int)reader.GetInt64(4)),
            checked((int)reader.GetInt64(5)),
            checked((int)reader.GetInt64(6)),
            checked((int)reader.GetInt64(7)));
    }

    private void ValidateCacheCounts(CacheCounts counts)
    {
        EnsureWithinLimit("opcode", counts.Opcodes, limits.MaxOpcodes);
        EnsureWithinLimit("message layout", counts.Layouts, limits.MaxLayouts);
        EnsureWithinLimit("message-layout field", counts.LayoutFields, limits.MaxLayoutFields);
        EnsureWithinLimit("bosses", counts.Bosses, limits.MaxBosses);
        EnsureWithinLimit("dungeons", counts.Dungeons, limits.MaxDungeons);
        EnsureWithinLimit("skills", counts.Skills, limits.MaxSkills);
        EnsureWithinLimit("buffs", counts.Buffs, limits.MaxBuffs);

        int total = 0;
        total = checked(total + counts.Opcodes);
        total = checked(total + counts.Layouts);
        total = checked(total + counts.LayoutFields);
        total = checked(total + counts.Bosses);
        total = checked(total + counts.Dungeons);
        total = checked(total + counts.Skills);
        total = checked(total + counts.Buffs);
        if (total > limits.MaxTotalEntries)
        {
            throw new InvalidDataException(
                $"Hot game-data cache has {total} entries; configured maximum is {limits.MaxTotalEntries}.");
        }
    }

    private static async Task<(ulong DataVersion, uint SchemaVersion)> ReadMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT data_version, schema_version FROM metadata WHERE singleton_id = 1;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The game-data metadata row is missing.");
        }
        long dataVersion = reader.GetInt64(0);
        long schemaVersion = reader.GetInt64(1);
        if (dataVersion <= 0 || schemaVersion <= 0 || schemaVersion > uint.MaxValue)
        {
            throw new InvalidDataException("The game-data versions are out of range.");
        }
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The game-data metadata table contains multiple singleton rows.");
        }
        return (checked((ulong)dataVersion), checked((uint)schemaVersion));
    }

    private static async Task<uint> ReadActiveProfileIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT id FROM protocol_profiles WHERE is_active = 1 LIMIT 2;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("No active protocol profile exists.");
        }
        uint profileId = checked((uint)reader.GetInt64(0));
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("More than one active protocol profile exists.");
        }
        return profileId;
    }

    private static async Task<ActiveProfile> ReadActiveProfileAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint profileId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT name, version, packet_magic FROM protocol_profiles WHERE id = $profile;");
        command.Parameters.AddWithValue("$profile", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The active protocol profile disappeared within its read transaction.");
        }
        return new ActiveProfile(
            reader.GetString(0),
            checked((uint)reader.GetInt64(1)),
            ((byte[])reader[2]).ToImmutableArray());
    }

    private static async Task<ImmutableArray<ushort>> ReadPortsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint profileId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT port FROM protocol_profile_ports WHERE profile_id = $profile ORDER BY port;");
        command.Parameters.AddWithValue("$profile", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var builder = ImmutableArray.CreateBuilder<ushort>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            builder.Add(checked((ushort)reader.GetInt64(0)));
        }
        if (builder.Count is 0 or > ProtocolSnapshotCompiler.MaxServerPorts)
        {
            throw new InvalidDataException("The active protocol profile has an invalid server-port count.");
        }
        return builder.ToImmutable();
    }

    private static async Task<FrozenDictionary<ushort, ProtocolOpcode>> ReadOpcodesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint profileId,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT id, kind, name, tag, COALESCE(layout_id, 0)
            FROM opcodes
            WHERE profile_id = $profile
            ORDER BY kind
            LIMIT $limit;
            """);
        command.Parameters.AddWithValue("$profile", profileId);
        command.Parameters.AddWithValue("$limit", checked(expectedCount + 1));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var values = new List<ProtocolOpcode>(expectedCount);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(new ProtocolOpcode(
                checked((uint)reader.GetInt64(0)),
                checked((ushort)reader.GetInt64(1)),
                reader.GetString(2),
                ((byte[])reader[3]).ToImmutableArray(),
                checked((uint)reader.GetInt64(4))));
        }
        EnsureExpectedCount("opcode", values.Count, expectedCount);
        return values.ToFrozenDictionary(static value => value.Kind);
    }

    private async Task<FrozenDictionary<uint, ProtocolMessageLayout>> ReadLayoutsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint profileId,
        int expectedLayoutCount,
        int expectedFieldCount,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT id, name, max_payload_bytes, parser_strategy
            FROM message_layouts
            WHERE profile_id = $profile
            ORDER BY id
            LIMIT $limit;
            """);
        command.Parameters.AddWithValue("$profile", profileId);
        command.Parameters.AddWithValue("$limit", checked(expectedLayoutCount + 1));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var layouts = new List<(uint Id, string Name, uint MaxPayloadBytes, ushort ParserStrategy)>(expectedLayoutCount);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            layouts.Add((checked((uint)reader.GetInt64(0)), reader.GetString(1), checked((uint)reader.GetInt64(2)), checked((ushort)reader.GetInt64(3))));
        }
        EnsureExpectedCount("message layout", layouts.Count, expectedLayoutCount);

        var result = new Dictionary<uint, ProtocolMessageLayout>(expectedLayoutCount);
        int remainingFieldCount = expectedFieldCount;
        foreach ((uint id, string name, uint maxPayloadBytes, ushort parserStrategy) in layouts)
        {
            ImmutableArray<ProtocolFieldDescriptor> fields = await ReadFieldsAsync(
                connection, transaction, id, remainingFieldCount, cancellationToken).ConfigureAwait(false);
            remainingFieldCount = checked(remainingFieldCount - fields.Length);
            result.Add(id, new ProtocolMessageLayout(id, name, maxPayloadBytes, fields, parserStrategy));
        }
        EnsureExpectedCount("message-layout field", remainingFieldCount, 0);
        return result.ToFrozenDictionary();
    }

    private async Task<ImmutableArray<ProtocolFieldDescriptor>> ReadFieldsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint layoutId,
        int remainingFieldCount,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT kind, flags, byte_offset, byte_size, max_count
            FROM message_fields
            WHERE layout_id = $layout
            ORDER BY field_order
            LIMIT $limit;
            """);
        command.Parameters.AddWithValue("$layout", layoutId);
        command.Parameters.AddWithValue("$limit", checked(remainingFieldCount + 1));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var builder = ImmutableArray.CreateBuilder<ProtocolFieldDescriptor>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            builder.Add(new ProtocolFieldDescriptor(
                checked((ushort)reader.GetInt64(0)),
                checked((ushort)reader.GetInt64(1)),
                checked((uint)reader.GetInt64(2)),
                checked((uint)reader.GetInt64(3)),
                checked((uint)reader.GetInt64(4))));
        }
        return builder.ToImmutable();
    }

    private static async Task<FrozenDictionary<uint, T>> ReadNamedCodeMapAsync<T>(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        int expectedCount,
        Func<uint, string, T> factory,
        CancellationToken cancellationToken)
        where T : notnull
    {
        await using var command = CreateCommand(connection, transaction,
            $"SELECT code, name FROM {table} ORDER BY code LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", checked(expectedCount + 1));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<uint, T>(expectedCount);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            uint code = checked((uint)reader.GetInt64(0));
            values.Add(code, factory(code, reader.GetString(1)));
        }
        EnsureExpectedCount(table, values.Count, expectedCount);
        return values.ToFrozenDictionary();
    }

    private static async Task<FrozenDictionary<uint, Boss>> ReadBossesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT code, name, max_hp, content_code, dungeon_code FROM bosses ORDER BY code LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", checked(expectedCount + 1));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<uint, Boss>(expectedCount);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            uint code = checked((uint)reader.GetInt64(0));
            values.Add(code, new Boss(code, reader.GetString(1), checked((ulong)reader.GetInt64(2)),
                checked((uint)reader.GetInt64(3)), checked((uint)reader.GetInt64(4))));
        }
        EnsureExpectedCount("bosses", values.Count, expectedCount);
        return values.ToFrozenDictionary();
    }

    private static async Task<FrozenDictionary<uint, Buff>> ReadBuffsAsync(
        SqliteConnection connection, SqliteTransaction transaction, int expectedCount,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT code, name, track_uptime, use_target_uptime, include_owner FROM buffs ORDER BY code LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", checked(expectedCount + 1));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<uint, Buff>(expectedCount);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            uint code = checked((uint)reader.GetInt64(0));
            values.Add(code, new(code, reader.GetString(1), reader.GetInt64(2) != 0,
                reader.GetInt64(3) != 0, reader.GetInt64(4) != 0));
        }
        EnsureExpectedCount("buffs", values.Count, expectedCount);
        return values.ToFrozenDictionary();
    }

    private static async Task<FrozenDictionary<ushort, ushort>> ReadJobAliasesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT raw_code, canonical_code FROM job_aliases ORDER BY raw_code LIMIT 65;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<ushort, ushort>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (values.Count >= 64) throw new InvalidDataException("The job-alias cache exceeds 64 entries.");
            values.Add(checked((ushort)reader.GetInt64(0)), checked((ushort)reader.GetInt64(1)));
        }
        return values.ToFrozenDictionary();
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void EnsureWithinLimit(string category, int actual, int maximum)
    {
        if (actual > maximum)
        {
            throw new InvalidDataException(
                $"The {category} cache has more than its configured maximum of {maximum} entries.");
        }
    }

    private static void EnsureExpectedCount(string category, int actual, int expected)
    {
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"The {category} cache changed within its read transaction: expected {expected}, read {actual}.");
        }
    }

    private readonly record struct ActiveProfile(
        string Name,
        uint Version,
        ImmutableArray<byte> PacketMagic);

    private readonly record struct CacheCounts(
        int Opcodes,
        int Layouts,
        int LayoutFields,
        int Bosses,
        int Dungeons,
        int Skills,
        int Buffs);
}
