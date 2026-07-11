using Microsoft.Data.Sqlite;

namespace Namter.GameData.Builder;

public static class GameDataDatabaseBuilder
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            (string output, string schema, string seed) = ParseArguments(args);
            await BuildAsync(output, schema, seed).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    public static async Task BuildAsync(
        string outputPath,
        string schemaPath,
        string seedPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedPath);

        string output = Path.GetFullPath(outputPath);
        string schema = Path.GetFullPath(schemaPath);
        string seed = Path.GetFullPath(seedPath);
        string? outputDirectory = Path.GetDirectoryName(output);
        if (outputDirectory is null) throw new ArgumentException("Output path must have a parent directory.", nameof(outputPath));
        Directory.CreateDirectory(outputDirectory);
        string temporary = Path.Combine(outputDirectory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");

        try
        {
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = temporary,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                ForeignKeys = true,
                Pooling = false,
            }.ToString();
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(connection, null, "PRAGMA foreign_keys = ON; PRAGMA journal_mode = DELETE;", cancellationToken)
                    .ConfigureAwait(false);
                await using (var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    await ExecuteAsync(connection, transaction, await File.ReadAllTextAsync(schema, cancellationToken).ConfigureAwait(false), cancellationToken)
                        .ConfigureAwait(false);
                    await ExecuteAsync(connection, transaction, await File.ReadAllTextAsync(seed, cancellationToken).ConfigureAwait(false), cancellationToken)
                        .ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }

                string integrity = await ExecuteScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(integrity, "ok", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"SQLite integrity check failed: {integrity}");
                }
                await using var foreignKeyCheck = connection.CreateCommand();
                foreignKeyCheck.CommandText = "PRAGMA foreign_key_check;";
                await using var reader = await foreignKeyCheck.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidDataException("SQLite foreign-key validation failed.");
                }
            }

            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ExecuteScalarStringAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static (string Output, string Schema, string Seed) ParseArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || args[index] is not ("--output" or "--schema" or "--seed") ||
                !values.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException(
                    "Usage: namter-gamedata-builder --output <path> --schema <path> --seed <path>");
            }
        }
        if (values.Count != 3)
        {
            throw new ArgumentException(
                "Usage: namter-gamedata-builder --output <path> --schema <path> --seed <path>");
        }
        return (values["--output"], values["--schema"], values["--seed"]);
    }
}
