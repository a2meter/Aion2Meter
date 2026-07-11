using Microsoft.Data.Sqlite;

namespace Namter.GameData;

internal static class GameDataSchemaValidator
{
    private static readonly IReadOnlyDictionary<string, ColumnSpec[]> Tables =
        new Dictionary<string, ColumnSpec[]>(StringComparer.Ordinal)
        {
            ["metadata"] = [C("singleton_id", "INTEGER", false, 1), C("data_version", "INTEGER"), C("schema_version", "INTEGER")],
            ["protocol_profiles"] = [C("id", "INTEGER", false, 1), C("name", "TEXT"), C("version", "INTEGER"), C("packet_magic", "BLOB"), C("party_marker", "INTEGER"), C("is_active", "INTEGER")],
            ["protocol_profile_ports"] = [C("profile_id", "INTEGER", true, 1), C("port", "INTEGER", true, 2)],
            ["message_layouts"] = [C("id", "INTEGER", false, 1), C("profile_id", "INTEGER"), C("name", "TEXT"), C("max_payload_bytes", "INTEGER")],
            ["message_fields"] = [C("id", "INTEGER", false, 1), C("layout_id", "INTEGER"), C("field_order", "INTEGER"), C("kind", "INTEGER"), C("flags", "INTEGER"), C("byte_offset", "INTEGER"), C("byte_size", "INTEGER"), C("max_count", "INTEGER")],
            ["opcodes"] = [C("id", "INTEGER", false, 1), C("profile_id", "INTEGER"), C("family", "INTEGER"), C("kind", "INTEGER"), C("name", "TEXT"), C("tag", "BLOB"), C("layout_id", "INTEGER", false)],
            ["bosses"] = NamedCodeColumns(),
            ["dungeons"] = NamedCodeColumns(),
            ["dungeon_bosses"] = [C("dungeon_id", "INTEGER", true, 1), C("boss_id", "INTEGER", true, 2), C("encounter_order", "INTEGER")],
            ["mobs"] = [C("id", "INTEGER", false, 1), C("code", "INTEGER"), C("name", "TEXT"), C("boss_id", "INTEGER", false)],
            ["skills"] = NamedCodeColumns(),
            ["buffs"] = NamedCodeColumns(),
        };

    private static readonly ForeignKeySpec[] ForeignKeys =
    [
        F("protocol_profile_ports", "profile_id", "protocol_profiles", "id", "CASCADE"),
        F("message_layouts", "profile_id", "protocol_profiles", "id", "CASCADE"),
        F("message_fields", "layout_id", "message_layouts", "id", "CASCADE"),
        F("opcodes", "profile_id", "protocol_profiles", "id", "CASCADE"),
        F("opcodes", "profile_id", "message_layouts", "profile_id", "NO ACTION"),
        F("opcodes", "layout_id", "message_layouts", "id", "NO ACTION"),
        F("dungeon_bosses", "dungeon_id", "dungeons", "id", "CASCADE"),
        F("dungeon_bosses", "boss_id", "bosses", "id", "CASCADE"),
        F("mobs", "boss_id", "bosses", "id", "NO ACTION"),
    ];

    private static readonly IndexSpec[] Indexes =
    [
        I("protocol_profiles", "idx_protocol_profiles_active", true, true, "is_active"),
        I("protocol_profiles", "idx_protocol_profiles_name", false, false, "name"),
        I("protocol_profile_ports", "idx_profile_ports_profile", false, false, "profile_id", "port"),
        I("message_layouts", "idx_message_layouts_profile_name", false, false, "profile_id", "name"),
        I("message_fields", "idx_message_fields_layout_order", false, false, "layout_id", "field_order"),
        I("opcodes", "idx_opcodes_profile_kind", false, false, "profile_id", "family", "kind"),
        I("opcodes", "idx_opcodes_profile_name", false, false, "profile_id", "name"),
        I("bosses", "idx_bosses_name", false, false, "name"),
        I("dungeons", "idx_dungeons_code", false, false, "code"),
        I("dungeons", "idx_dungeons_name", false, false, "name"),
        I("dungeon_bosses", "idx_dungeon_bosses_dungeon", false, false, "dungeon_id", "encounter_order"),
        I("dungeon_bosses", "idx_dungeon_bosses_boss", false, false, "boss_id"),
        I("mobs", "idx_mobs_code", false, false, "code"),
        I("mobs", "idx_mobs_name", false, false, "name"),
        I("skills", "idx_skills_code", false, false, "code"),
        I("skills", "idx_skills_name", false, false, "name"),
        I("buffs", "idx_buffs_code", false, false, "code"),
        I("buffs", "idx_buffs_name", false, false, "name"),
    ];

    public static async Task<string?> ValidateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach ((string table, ColumnSpec[] expected) in Tables)
        {
            var actual = new List<ColumnSpec>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{table}');";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                actual.Add(new ColumnSpec(reader.GetString(1), reader.GetString(2).ToUpperInvariant(),
                    reader.GetInt64(3) != 0, checked((int)reader.GetInt64(5))));
            }
            if (!actual.SequenceEqual(expected)) return $"Required columns differ for table {table}.";
        }

        var actualForeignKeys = new HashSet<ForeignKeySpec>();
        foreach (string table in Tables.Keys)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA foreign_key_list('{table}');";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                actualForeignKeys.Add(new(table, reader.GetString(3), reader.GetString(2), reader.GetString(4), reader.GetString(6)));
        }
        if (!actualForeignKeys.SetEquals(ForeignKeys)) return "Required foreign-key structure differs.";

        foreach (IndexSpec expected in Indexes)
        {
            bool found = false;
            await using (var list = connection.CreateCommand())
            {
                list.CommandText = $"PRAGMA index_list('{expected.Table}');";
                await using SqliteDataReader reader = await list.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!string.Equals(reader.GetString(1), expected.Name, StringComparison.Ordinal)) continue;
                    found = reader.GetInt64(2) != 0 == expected.Unique && reader.GetInt64(4) != 0 == expected.Partial;
                    break;
                }
            }
            if (!found) return $"Required index differs: {expected.Name}.";
            var columns = new List<string>();
            await using var info = connection.CreateCommand();
            info.CommandText = $"PRAGMA index_info('{expected.Name}');";
            await using SqliteDataReader infoReader = await info.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await infoReader.ReadAsync(cancellationToken).ConfigureAwait(false)) columns.Add(infoReader.GetString(2));
            if (!columns.SequenceEqual(expected.Columns)) return $"Required index columns differ: {expected.Name}.";
        }
        return null;
    }

    private static ColumnSpec C(string name, string type, bool notNull = true, int primaryKeyOrder = 0)
        => new(name, type, notNull, primaryKeyOrder);
    private static ColumnSpec[] NamedCodeColumns() => [C("id", "INTEGER", false, 1), C("code", "INTEGER"), C("name", "TEXT")];
    private static ForeignKeySpec F(string table, string from, string target, string to, string onDelete)
        => new(table, from, target, to, onDelete);
    private static IndexSpec I(string table, string name, bool unique, bool partial, params string[] columns)
        => new(table, name, unique, partial, columns);

    private sealed record ColumnSpec(string Name, string Type, bool NotNull, int PrimaryKeyOrder);
    private sealed record ForeignKeySpec(string Table, string From, string TargetTable, string To, string OnDelete);
    private sealed record IndexSpec(string Table, string Name, bool Unique, bool Partial, string[] Columns);
}
