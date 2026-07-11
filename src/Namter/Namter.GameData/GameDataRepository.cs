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
        ActiveProfile profile = await ReadActiveProfileAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        ImmutableArray<ushort> ports = await ReadPortsAsync(connection, transaction, profile.Id, cancellationToken)
            .ConfigureAwait(false);
        FrozenDictionary<uint, ProtocolOpcode> opcodes = await ReadOpcodesAsync(
            connection, transaction, profile.Id, cancellationToken).ConfigureAwait(false);
        FrozenDictionary<uint, ProtocolMessageLayout> layouts = await ReadLayoutsAsync(
            connection, transaction, profile.Id, cancellationToken).ConfigureAwait(false);
        FrozenDictionary<uint, Boss> bosses = await ReadNamedCodeMapAsync(
            connection, transaction, "bosses", limits.MaxBosses, static (code, name) => new Boss(code, name), cancellationToken)
            .ConfigureAwait(false);
        FrozenDictionary<uint, Dungeon> dungeons = await ReadNamedCodeMapAsync(
            connection, transaction, "dungeons", limits.MaxDungeons, static (code, name) => new Dungeon(code, name), cancellationToken)
            .ConfigureAwait(false);
        FrozenDictionary<uint, Skill> skills = await ReadNamedCodeMapAsync(
            connection, transaction, "skills", limits.MaxSkills, static (code, name) => new Skill(code, name), cancellationToken)
            .ConfigureAwait(false);
        FrozenDictionary<uint, Buff> buffs = await ReadNamedCodeMapAsync(
            connection, transaction, "buffs", limits.MaxBuffs, static (code, name) => new Buff(code, name), cancellationToken)
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
            buffs);
        if (snapshot.TotalHotCacheEntries > limits.MaxTotalEntries)
        {
            throw new InvalidDataException(
                $"Hot game-data cache has {snapshot.TotalHotCacheEntries} entries; configured maximum is {limits.MaxTotalEntries}.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return snapshot;
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
        if (dataVersion < 0 || schemaVersion <= 0 || schemaVersion > uint.MaxValue)
        {
            throw new InvalidDataException("The game-data versions are out of range.");
        }
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The game-data metadata table contains multiple singleton rows.");
        }
        return (checked((ulong)dataVersion), checked((uint)schemaVersion));
    }

    private static async Task<ActiveProfile> ReadActiveProfileAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT id, name, version, packet_magic FROM protocol_profiles WHERE is_active = 1 LIMIT 2;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("No active protocol profile exists.");
        }
        var profile = new ActiveProfile(
            checked((uint)reader.GetInt64(0)),
            reader.GetString(1),
            checked((uint)reader.GetInt64(2)),
            ((byte[])reader[3]).ToImmutableArray());
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("More than one active protocol profile exists.");
        }
        return profile;
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

    private async Task<FrozenDictionary<uint, ProtocolOpcode>> ReadOpcodesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint profileId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT id, kind, name, tag, COALESCE(layout_id, 0)
            FROM opcodes
            WHERE profile_id = $profile
            ORDER BY id
            LIMIT $limit;
            """);
        command.Parameters.AddWithValue("$profile", profileId);
        command.Parameters.AddWithValue("$limit", checked(limits.MaxOpcodes + 1));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var values = new List<ProtocolOpcode>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(new ProtocolOpcode(
                checked((uint)reader.GetInt64(0)),
                checked((ushort)reader.GetInt64(1)),
                reader.GetString(2),
                ((byte[])reader[3]).ToImmutableArray(),
                checked((uint)reader.GetInt64(4))));
        }
        EnsureWithinLimit("opcode", values.Count, limits.MaxOpcodes);
        return values.ToFrozenDictionary(static value => value.Id);
    }

    private async Task<FrozenDictionary<uint, ProtocolMessageLayout>> ReadLayoutsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint profileId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT id, name, max_payload_bytes
            FROM message_layouts
            WHERE profile_id = $profile
            ORDER BY id
            LIMIT $limit;
            """);
        command.Parameters.AddWithValue("$profile", profileId);
        command.Parameters.AddWithValue("$limit", checked(limits.MaxLayouts + 1));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var layouts = new List<(uint Id, string Name, uint MaxPayloadBytes)>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            layouts.Add((checked((uint)reader.GetInt64(0)), reader.GetString(1), checked((uint)reader.GetInt64(2))));
        }
        EnsureWithinLimit("message layout", layouts.Count, limits.MaxLayouts);

        var result = new Dictionary<uint, ProtocolMessageLayout>();
        var totalFieldCount = 0;
        foreach ((uint id, string name, uint maxPayloadBytes) in layouts)
        {
            ImmutableArray<ProtocolFieldDescriptor> fields = await ReadFieldsAsync(
                connection, transaction, id, cancellationToken).ConfigureAwait(false);
            totalFieldCount = checked(totalFieldCount + fields.Length);
            EnsureWithinLimit("message-layout field", totalFieldCount, limits.MaxLayoutFields);
            result.Add(id, new ProtocolMessageLayout(id, name, maxPayloadBytes, fields));
        }
        return result.ToFrozenDictionary();
    }

    private async Task<ImmutableArray<ProtocolFieldDescriptor>> ReadFieldsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint layoutId,
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
        command.Parameters.AddWithValue("$limit", checked(limits.MaxLayoutFields + 1));
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
        int limit,
        Func<uint, string, T> factory,
        CancellationToken cancellationToken)
        where T : notnull
    {
        await using var command = CreateCommand(connection, transaction,
            $"SELECT code, name FROM {table} ORDER BY code LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", checked(limit + 1));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<uint, T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            uint code = checked((uint)reader.GetInt64(0));
            values.Add(code, factory(code, reader.GetString(1)));
        }
        EnsureWithinLimit(table, values.Count, limit);
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

    private readonly record struct ActiveProfile(
        uint Id,
        string Name,
        uint Version,
        ImmutableArray<byte> PacketMagic);
}
