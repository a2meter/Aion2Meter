using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Namter.GameData.Publisher;

public enum PublishStatus
{
    Published = 0,
    InvalidArguments = 2,
    InputInvalid = 3,
    InputVersionMismatch = 4,
    InsecureArchiveUri = 5,
    OutputInsideSourceTree = 6,
    OutputExists = 7,
    InvalidPrivateKey = 8,
    UnsafeFilesystem = 9,
    Failed = 10,
    Cancelled = 11,
    CommitFailedRestored = 12,
    RecoveryRequired = 13,
}

public sealed record PublisherPolicy
{
    internal PublisherPolicy(string sourceRoot, bool allowInsecureArchiveUri)
    {
        SourceRoot = sourceRoot;
        AllowInsecureArchiveUri = allowInsecureArchiveUri;
    }
    public string SourceRoot { get; init; }
    internal bool AllowInsecureArchiveUri { get; init; }
    internal static PublisherPolicy Production(string sourceRoot) => new(sourceRoot, false);
}

public sealed record PublishOptions(
    string InputPath,
    string OutputDirectory,
    Uri ArchiveUri,
    ulong DataVersion,
    Version MinimumAppVersion,
    string PrivateKeyPath,
    bool Force,
    PublisherPolicy Policy);

public sealed record PublishResult(PublishStatus Status, string? Detail = null);

public static class GameDataPublisher
{
    private const int BufferSize = 64 * 1024;
    private static readonly string[] RequiredTables =
    [
        "metadata", "protocol_profiles", "protocol_profile_ports", "opcodes", "message_layouts", "message_fields",
        "bosses", "dungeons", "dungeon_bosses", "mobs", "skills", "buffs",
    ];

    public static Task<PublishResult> PublishAsync(
        PublishOptions options,
        CancellationToken cancellationToken = default)
        => PublishAsync(options, PhysicalGameDataFileSystem.Instance, cancellationToken);

    internal static async Task<PublishResult> PublishAsync(
        PublishOptions options,
        IGameDataFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (string.IsNullOrWhiteSpace(options.InputPath)
            || string.IsNullOrWhiteSpace(options.OutputDirectory)
            || string.IsNullOrWhiteSpace(options.PrivateKeyPath)
            || options.ArchiveUri is null
            || !options.ArchiveUri.IsAbsoluteUri
            || options.DataVersion == 0
            || options.MinimumAppVersion is null
            || options.Policy is null
            || string.IsNullOrWhiteSpace(options.Policy.SourceRoot))
            return new(PublishStatus.InvalidArguments);

        if (!options.Policy.AllowInsecureArchiveUri && options.ArchiveUri.Scheme != Uri.UriSchemeHttps)
            return new(PublishStatus.InsecureArchiveUri);

        string input = Path.GetFullPath(options.InputPath);
        string output = Path.GetFullPath(options.OutputDirectory);
        string privateKey = Path.GetFullPath(options.PrivateKeyPath);
        string sourceRoot = Path.GetFullPath(options.Policy.SourceRoot);
        if (IsSameOrDescendant(output, sourceRoot)) return new(PublishStatus.OutputInsideSourceTree);

        string archivePath = Path.Combine(output, "aion.db.br");
        string manifestPath = Path.Combine(output, "manifest.json");
        string archiveTemporary = Path.Combine(output, $".aion.db.br.{Guid.NewGuid():N}.part");
        string manifestTemporary = Path.Combine(output, $".manifest.json.{Guid.NewGuid():N}.part");
        string archivePrevious = Path.Combine(output, $".aion.db.br.{Guid.NewGuid():N}.previous");
        string manifestPrevious = Path.Combine(output, $".manifest.json.{Guid.NewGuid():N}.previous");

        try
        {
            EnsureRegularFile(fileSystem, input);
            EnsureRegularFile(fileSystem, privateKey);
            EnsureRegularFile(fileSystem, archivePath);
            EnsureRegularFile(fileSystem, manifestPath);
            if (!fileSystem.FileExists(input)) return new(PublishStatus.InputInvalid, "Input database does not exist.");
            if (!fileSystem.FileExists(privateKey)) return new(PublishStatus.InvalidPrivateKey, "Private-key file does not exist.");
            EnsureNoReparseAncestors(output);
            if (fileSystem.DirectoryExists(output) && (fileSystem.GetAttributes(output) & FileAttributes.ReparsePoint) != 0)
                return new(PublishStatus.UnsafeFilesystem, "Output directory cannot be a reparse point.");
            if (!options.Force && (fileSystem.FileExists(archivePath) || fileSystem.FileExists(manifestPath)))
                return new(PublishStatus.OutputExists);

            InspectionResult inspection = await InspectDatabaseAsync(input, cancellationToken).ConfigureAwait(false);
            if (!inspection.Valid) return new(PublishStatus.InputInvalid, inspection.Detail);
            if (inspection.Snapshot!.DataVersion != options.DataVersion)
                return new(PublishStatus.InputVersionMismatch);

            ECDsa? signer = null;
            try
            {
                signer = ECDsa.Create();
                signer.ImportFromPem(await File.ReadAllTextAsync(privateKey, cancellationToken).ConfigureAwait(false));
                if (!P256Signature.IsExactCurve(signer))
                {
                    signer.Dispose();
                    return new(PublishStatus.InvalidPrivateKey, "The publisher requires an ECDSA P-256 private key.");
                }
                _ = signer.SignHash(new byte[32], DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch (Exception exception) when (exception is CryptographicException or ArgumentException)
            {
                signer?.Dispose();
                return new(PublishStatus.InvalidPrivateKey, exception.Message);
            }

            fileSystem.CreateDirectory(output);
            if ((fileSystem.GetAttributes(output) & FileAttributes.ReparsePoint) != 0)
            {
                signer.Dispose();
                return new(PublishStatus.UnsafeFilesystem, "Output directory cannot be a reparse point.");
            }

            try
            {
                await using (var source = new FileStream(
                    input, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var destination = new FileStream(
                    archiveTemporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
                await using (var brotli = new BrotliStream(destination, CompressionLevel.Optimal, leaveOpen: true))
                {
                    await source.CopyToAsync(brotli, BufferSize, cancellationToken).ConfigureAwait(false);
                }

                long compressedSize = new FileInfo(archiveTemporary).Length;
                long uncompressedSize = new FileInfo(input).Length;
                string sha256;
                await using (var archive = new FileStream(
                    archiveTemporary, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    sha256 = Convert.ToHexString(await SHA256.HashDataAsync(archive, cancellationToken).ConfigureAwait(false))
                        .ToLowerInvariant();
                }

                var unsigned = new GameDataManifest(
                    options.DataVersion,
                    inspection.Snapshot.SchemaVersion,
                    inspection.Snapshot.ProtocolProfileVersion,
                    options.MinimumAppVersion,
                    options.ArchiveUri,
                    compressedSize,
                    uncompressedSize,
                    sha256,
                    "br",
                    DateTimeOffset.UtcNow,
                    string.Empty);
                string signature = Convert.ToBase64String(P256Signature.Normalize(signer.SignData(
                    unsigned.GetCanonicalUnsignedBytes(), HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
                byte[] manifest = (unsigned with { Signature = signature }).ToJsonBytes();
                await File.WriteAllBytesAsync(manifestTemporary, manifest, cancellationToken).ConfigureAwait(false);

                ReplaceOrMove(fileSystem, archiveTemporary, archivePath, archivePrevious, options.Force);
                try
                {
                    ReplaceOrMove(fileSystem, manifestTemporary, manifestPath, manifestPrevious, options.Force);
                }
                catch (Exception commitException)
                {
                    try
                    {
                        if (fileSystem.FileExists(manifestPrevious))
                            RestorePrevious(fileSystem, manifestPath, manifestPrevious);
                        RestorePrevious(fileSystem, archivePath, archivePrevious);
                        DeleteRegularTemporary(fileSystem, archivePrevious);
                        DeleteRegularTemporary(fileSystem, manifestPrevious);
                        return new(PublishStatus.CommitFailedRestored, commitException.Message);
                    }
                    catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException)
                    {
                        return new(PublishStatus.RecoveryRequired,
                            $"Commit: {commitException.Message}; restore: {restoreException.Message}");
                    }
                }
                try
                {
                    DeleteRegularTemporary(fileSystem, archivePrevious);
                    DeleteRegularTemporary(fileSystem, manifestPrevious);
                }
                catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
                {
                    return new(PublishStatus.RecoveryRequired,
                        $"Artifacts committed but recovery cleanup failed: {cleanupException.Message}");
                }
                return new(PublishStatus.Published);
            }
            finally
            {
                signer.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            return new(PublishStatus.Cancelled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(PublishStatus.UnsafeFilesystem, exception.Message);
        }
        catch (Exception exception)
        {
            return new(PublishStatus.Failed, exception.Message);
        }
        finally
        {
            TryDeleteRegularTemporary(fileSystem, archiveTemporary);
            TryDeleteRegularTemporary(fileSystem, manifestTemporary);
        }
    }

    private static async Task<InspectionResult> InspectDatabaseAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                ForeignKeys = true,
                Pooling = false,
            }.ToString();
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            string? result = Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            if (!string.Equals(result, "ok", StringComparison.Ordinal)) return new(false, null, result);

            await using var foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            await using SqliteDataReader reader = await foreignKeys.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return new(false, null, "SQLite foreign-key validation failed.");

            var tables = new HashSet<string>(StringComparer.Ordinal);
            await using (var schema = connection.CreateCommand())
            {
                schema.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table';";
                await using SqliteDataReader schemaReader = await schema.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await schemaReader.ReadAsync(cancellationToken).ConfigureAwait(false)) tables.Add(schemaReader.GetString(0));
            }
            string? missing = RequiredTables.FirstOrDefault(table => !tables.Contains(table));
            if (missing is not null) return new(false, null, $"Required table is missing: {missing}.");

            string? schemaError = await GameDataSchemaValidator.ValidateAsync(connection, cancellationToken).ConfigureAwait(false);
            if (schemaError is not null) return new(false, null, schemaError);

            GameDataSnapshot snapshot = await new GameDataRepository(path, GameDataCacheLimits.Default)
                .LoadAsync(cancellationToken).ConfigureAwait(false);
            return new(true, snapshot);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidDataException or OverflowException)
        {
            return new(false, null, exception.Message);
        }
    }

    private static bool IsSameOrDescendant(string candidate, string root)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(candidate);
        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)) return true;
        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureRegularFile(IGameDataFileSystem fileSystem, string path)
    {
        if (fileSystem.FileExists(path) && (fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Publisher inputs cannot be reparse points.");
    }

    private static void EnsureNoReparseAncestors(string path)
    {
        for (DirectoryInfo? directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            if (!directory.Exists) continue;
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Publisher output ancestors cannot be reparse points.");
        }
    }

    private static void ReplaceOrMove(
        IGameDataFileSystem fileSystem,
        string source,
        string destination,
        string previous,
        bool force)
    {
        if (fileSystem.FileExists(destination))
        {
            if (!force) throw new IOException("Publisher output already exists.");
            fileSystem.ReplaceFile(source, destination, previous);
        }
        else
        {
            fileSystem.MoveFile(source, destination, overwrite: false);
        }
    }

    private static void RestorePrevious(IGameDataFileSystem fileSystem, string destination, string previous)
    {
        if (!fileSystem.FileExists(previous)) throw new IOException("Publisher recovery artifact is missing.");
        fileSystem.ReplaceFile(previous, destination, backup: null);
    }

    private static void DeleteRegularTemporary(IGameDataFileSystem fileSystem, string path)
    {
        if (!fileSystem.FileExists(path)) return;
        if ((fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Publisher recovery artifact cannot be a reparse point.");
        fileSystem.DeleteFile(path);
    }

    private static void TryDeleteRegularTemporary(IGameDataFileSystem fileSystem, string path)
    {
        try
        {
            DeleteRegularTemporary(fileSystem, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record InspectionResult(bool Valid, GameDataSnapshot? Snapshot, string? Detail = null);
}

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        PublishOptions? options = ParseArguments(args);
        if (options is null)
        {
            Console.Error.WriteLine("Usage: namter-data-publisher --input <aion.db> --output <directory> --archive-uri <https-uri> --data-version <ulong> --minimum-app-version <version> --private-key <pem-file> [--force]");
            return (int)PublishStatus.InvalidArguments;
        }

        PublishResult result = await GameDataPublisher.PublishAsync(options).ConfigureAwait(false);
        if (result.Status != PublishStatus.Published)
            Console.Error.WriteLine($"Publishing failed with status {result.Status}.");
        return (int)result.Status;
    }

    private static PublishOptions? ParseArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool force = false;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--force")
            {
                force = true;
                continue;
            }
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal)
                || args[index + 1].StartsWith("--", StringComparison.Ordinal)
                || !values.TryAdd(args[index], args[++index])) return null;
        }
        string[] required = ["--input", "--output", "--archive-uri", "--data-version", "--minimum-app-version", "--private-key"];
        if (values.Count != required.Length || required.Any(key => !values.ContainsKey(key))
            || !Uri.TryCreate(values["--archive-uri"], UriKind.Absolute, out Uri? archiveUri)
            || !ulong.TryParse(values["--data-version"], out ulong dataVersion)
            || !Version.TryParse(values["--minimum-app-version"], out Version? minimumAppVersion)) return null;

        return new PublishOptions(
            values["--input"], values["--output"], archiveUri, dataVersion, minimumAppVersion,
            values["--private-key"], force, PublisherPolicy.Production(FindSourceRoot()));
    }

    private static string FindSourceRoot()
    {
        string? configured = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "NamterRepositoryRoot")?.Value;
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Namter.slnx")) || Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
        return Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? AppContext.BaseDirectory;
    }
}
