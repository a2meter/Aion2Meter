using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Namter.GameData;

public enum GameDataCheckStatus
{
    UpdateAvailable,
    UpToDate,
    InvalidManifest,
    InvalidSignature,
    IncompatibleMinimumAppVersion,
    IncompatibleSchemaVersion,
    Cancelled,
    TransportFailed,
}

public enum GameDataStageStatus
{
    Staged,
    Cancelled,
    InvalidManifest,
    InvalidSignature,
    IncompatibleMinimumAppVersion,
    IncompatibleSchemaVersion,
    DownloadFailed,
    CompressedSizeMismatch,
    Sha256Mismatch,
    DecompressionFailed,
    UncompressedSizeMismatch,
    SqliteIntegrityFailed,
    RequiredTableMissing,
    DatabaseVersionMismatch,
    UnsafeFilesystem,
}

public enum GameDataActivationStatus
{
    Activated,
    DeferredEncounterActive,
    NoStagedUpdate,
    StagedDatabaseInvalid,
    ActiveDatabaseInvalid,
    ActivationFailedRolledBack,
    ActivationFailedRollbackFailed,
    UnsafeFilesystem,
}

public enum GameDataRollbackStatus
{
    RolledBack,
    DeferredEncounterActive,
    NoBackup,
    BackupInvalid,
    RollbackFailedRestoredCurrent,
    RollbackFailedRestoreFailed,
    UnsafeFilesystem,
}

public sealed record GameDataCheckResult(GameDataCheckStatus Status, GameDataManifest? Manifest = null, string? Detail = null);
public sealed record GameDataStageResult(GameDataStageStatus Status, string? Detail = null);
public sealed record GameDataActivationResult(GameDataActivationStatus Status, string? Detail = null);
public sealed record GameDataRollbackResult(GameDataRollbackStatus Status, string? Detail = null);

public sealed class GameDataUpdater
{
    private const int BufferSize = 64 * 1024;
    private static readonly string[] RequiredTables =
    [
        "metadata", "protocol_profiles", "protocol_profile_ports", "opcodes", "message_layouts", "message_fields",
        "bosses", "dungeons", "dungeon_bosses", "mobs", "skills", "buffs",
    ];

    private readonly string dataDirectory;
    private readonly string activePath;
    private readonly string updateDirectory;
    private readonly string partPath;
    private readonly string candidatePath;
    private readonly string backupDirectory;
    private readonly string backupPath;
    private readonly string operationBackupPath;
    private readonly string failedPath;
    private readonly Version appVersion;
    private readonly uint supportedSchemaVersion;
    private readonly byte[] trustedPublicKeySpki;
    private readonly IGameDataTransport transport;
    private readonly Func<bool> isEncounterActive;
    private readonly Func<string, CancellationToken, Task> reopenAndRebuild;
    private readonly SemaphoreSlim gate = new(1, 1);
    private GameDataManifest? stagedManifest;

    public GameDataUpdater(
        string dataDirectory,
        Version appVersion,
        uint supportedSchemaVersion,
        ReadOnlySpan<byte> trustedPublicKeySpki,
        IGameDataTransport transport,
        Func<bool> isEncounterActive,
        Func<string, CancellationToken, Task>? reopenAndRebuild = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(appVersion);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(isEncounterActive);
        if (supportedSchemaVersion == 0) throw new ArgumentOutOfRangeException(nameof(supportedSchemaVersion));
        if (trustedPublicKeySpki.IsEmpty) throw new ArgumentException("A trusted public key is required.", nameof(trustedPublicKeySpki));

        this.dataDirectory = Path.GetFullPath(dataDirectory);
        activePath = Path.Combine(this.dataDirectory, "aion.db");
        updateDirectory = Path.Combine(this.dataDirectory, ".update");
        partPath = Path.Combine(updateDirectory, "aion.db.br.part");
        candidatePath = Path.Combine(updateDirectory, "aion.db.candidate");
        operationBackupPath = Path.Combine(updateDirectory, "aion.operation-backup.db");
        failedPath = Path.Combine(updateDirectory, "aion.failed.db");
        backupDirectory = Path.Combine(this.dataDirectory, "backup");
        backupPath = Path.Combine(backupDirectory, "aion.previous.db");
        this.appVersion = appVersion;
        this.supportedSchemaVersion = supportedSchemaVersion;
        this.trustedPublicKeySpki = trustedPublicKeySpki.ToArray();
        this.transport = transport;
        this.isEncounterActive = isEncounterActive;
        this.reopenAndRebuild = reopenAndRebuild ?? (async (path, cancellationToken) =>
        {
            _ = await new GameDataRepository(path, GameDataCacheLimits.Default).LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        });
    }

    public async Task<GameDataCheckResult> CheckAsync(
        Uri manifestUri,
        DataVersion current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifestUri);
        try
        {
            await using Stream stream = await transport.OpenReadAsync(manifestUri, cancellationToken).ConfigureAwait(false);
            byte[] json = await ReadBoundedAsync(stream, GameDataManifest.MaximumJsonBytes, cancellationToken).ConfigureAwait(false);
            GameDataManifest manifest = GameDataManifest.Parse(json);
            GameDataStageStatus preliminary = ValidateManifest(manifest);
            return preliminary switch
            {
                GameDataStageStatus.InvalidSignature => new(GameDataCheckStatus.InvalidSignature),
                GameDataStageStatus.IncompatibleMinimumAppVersion => new(GameDataCheckStatus.IncompatibleMinimumAppVersion, manifest),
                GameDataStageStatus.IncompatibleSchemaVersion => new(GameDataCheckStatus.IncompatibleSchemaVersion, manifest),
                GameDataStageStatus.Staged when manifest.DataVersion <= current.Value => new(GameDataCheckStatus.UpToDate, manifest),
                _ => new(GameDataCheckStatus.UpdateAvailable, manifest),
            };
        }
        catch (OperationCanceledException)
        {
            return new(GameDataCheckStatus.Cancelled);
        }
        catch (InvalidDataException exception)
        {
            return new(GameDataCheckStatus.InvalidManifest, Detail: exception.Message);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or KeyNotFoundException)
        {
            return new(GameDataCheckStatus.TransportFailed, Detail: exception.Message);
        }
    }

    public async Task<GameDataStageResult> StageAsync(GameDataManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                EnsureSafeDirectories();
                CleanupTransient(includeCandidate: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(GameDataStageStatus.UnsafeFilesystem, exception.Message);
            }

            GameDataStageStatus preliminary = ValidateManifest(manifest);
            if (preliminary != GameDataStageStatus.Staged) return new(preliminary);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                try
                {
                    await using Stream source = await transport.OpenReadAsync(manifest.ArchiveUri, cancellationToken)
                        .ConfigureAwait(false);
                    await using var destination = new FileStream(
                        partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long total = 0;
                    while (true)
                    {
                        int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (read == 0) break;
                        if (read > manifest.CompressedSize - total)
                            throw new StageValidationException(GameDataStageStatus.CompressedSizeMismatch);
                        total += read;
                        hash.AppendData(buffer, 0, read);
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                    if (total != manifest.CompressedSize)
                        throw new StageValidationException(GameDataStageStatus.CompressedSizeMismatch);
                    byte[] actualHash = hash.GetHashAndReset();
                    byte[] expectedHash = Convert.FromHexString(manifest.Sha256);
                    if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                        throw new StageValidationException(GameDataStageStatus.Sha256Mismatch);
                }
                catch (StageValidationException exception)
                {
                    return CleanupAndReturn(exception.Status);
                }
                catch (OperationCanceledException)
                {
                    return CleanupAndReturn(GameDataStageStatus.Cancelled);
                }
                catch (Exception exception) when (exception is IOException or HttpRequestException or KeyNotFoundException)
                {
                    return CleanupAndReturn(GameDataStageStatus.DownloadFailed, exception.Message);
                }

                GameDataStageResult decompressed = await DecompressAsync(manifest, buffer, cancellationToken).ConfigureAwait(false);
                if (decompressed.Status != GameDataStageStatus.Staged) return decompressed;

                ValidationResult validation;
                try
                {
                    validation = await ValidateDatabaseAsync(candidatePath, manifest, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return CleanupAndReturn(GameDataStageStatus.Cancelled);
                }
                if (validation.Status != GameDataStageStatus.Staged)
                    return CleanupAndReturn(validation.Status, validation.Detail);

                SafeDelete(partPath);
                stagedManifest = manifest;
                return new(GameDataStageStatus.Staged);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<GameDataActivationResult> ActivateWhenIdleAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (stagedManifest is null || !File.Exists(candidatePath)) return new(GameDataActivationStatus.NoStagedUpdate);
            if (isEncounterActive()) return new(GameDataActivationStatus.DeferredEncounterActive);
            try
            {
                EnsureSafeDirectories();
                ValidationResult candidate = await ValidateDatabaseAsync(candidatePath, stagedManifest, cancellationToken)
                    .ConfigureAwait(false);
                if (candidate.Status != GameDataStageStatus.Staged)
                {
                    CleanupTransient(includeCandidate: true);
                    stagedManifest = null;
                    return new(GameDataActivationStatus.StagedDatabaseInvalid, candidate.Detail);
                }

                if (File.Exists(activePath))
                {
                    ValidationResult active = await ValidateDatabaseAsync(activePath, manifest: null, cancellationToken)
                        .ConfigureAwait(false);
                    if (active.Status != GameDataStageStatus.Staged)
                        return new(GameDataActivationStatus.ActiveDatabaseInvalid, active.Detail);
                }

                SafeDelete(operationBackupPath);
                SafeDelete(failedPath);
                if (File.Exists(activePath))
                    File.Replace(candidatePath, activePath, operationBackupPath, ignoreMetadataErrors: true);
                else
                    File.Move(candidatePath, activePath);

                try
                {
                    await reopenAndRebuild(activePath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception activationException)
                {
                    if (!File.Exists(operationBackupPath))
                    {
                        SafeDelete(activePath);
                        stagedManifest = null;
                        CleanupTransient(includeCandidate: true);
                        return new(GameDataActivationStatus.ActivationFailedRolledBack, activationException.Message);
                    }
                    try
                    {
                        File.Replace(operationBackupPath, activePath, failedPath, ignoreMetadataErrors: true);
                        SafeDelete(failedPath);
                        await reopenAndRebuild(activePath, CancellationToken.None).ConfigureAwait(false);
                        stagedManifest = null;
                        CleanupTransient(includeCandidate: true);
                        return new(GameDataActivationStatus.ActivationFailedRolledBack, activationException.Message);
                    }
                    catch (Exception rollbackException)
                    {
                        return new(GameDataActivationStatus.ActivationFailedRollbackFailed,
                            $"Activation: {activationException.Message}; rollback: {rollbackException.Message}");
                    }
                }

                if (File.Exists(operationBackupPath)) File.Move(operationBackupPath, backupPath, overwrite: true);
                CleanupTransient(includeCandidate: true);
                stagedManifest = null;
                return new(GameDataActivationStatus.Activated);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(GameDataActivationStatus.UnsafeFilesystem, exception.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<GameDataRollbackResult> RollbackAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (isEncounterActive()) return new(GameDataRollbackStatus.DeferredEncounterActive);
            if (!File.Exists(backupPath)) return new(GameDataRollbackStatus.NoBackup);
            try
            {
                EnsureSafeDirectories();
                ValidationResult backup = await ValidateDatabaseAsync(backupPath, manifest: null, cancellationToken)
                    .ConfigureAwait(false);
                if (backup.Status != GameDataStageStatus.Staged)
                    return new(GameDataRollbackStatus.BackupInvalid, backup.Detail);

                SafeDelete(operationBackupPath);
                SafeDelete(failedPath);
                File.Replace(backupPath, activePath, operationBackupPath, ignoreMetadataErrors: true);
                try
                {
                    await reopenAndRebuild(activePath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                {
                    try
                    {
                        File.Replace(operationBackupPath, activePath, failedPath, ignoreMetadataErrors: true);
                        File.Move(failedPath, backupPath, overwrite: true);
                        await reopenAndRebuild(activePath, CancellationToken.None).ConfigureAwait(false);
                        return new(GameDataRollbackStatus.RollbackFailedRestoredCurrent, rollbackException.Message);
                    }
                    catch (Exception restoreException)
                    {
                        return new(GameDataRollbackStatus.RollbackFailedRestoreFailed,
                            $"Rollback: {rollbackException.Message}; restore: {restoreException.Message}");
                    }
                }
                File.Move(operationBackupPath, backupPath, overwrite: true);
                return new(GameDataRollbackStatus.RolledBack);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(GameDataRollbackStatus.UnsafeFilesystem, exception.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private GameDataStageStatus ValidateManifest(GameDataManifest manifest)
    {
        try
        {
            manifest.ValidateFields();
        }
        catch (InvalidDataException)
        {
            return GameDataStageStatus.InvalidManifest;
        }
        if (!manifest.Verify(trustedPublicKeySpki)) return GameDataStageStatus.InvalidSignature;
        if (manifest.SchemaVersion != supportedSchemaVersion) return GameDataStageStatus.IncompatibleSchemaVersion;
        if (manifest.MinimumAppVersion > appVersion) return GameDataStageStatus.IncompatibleMinimumAppVersion;
        return GameDataStageStatus.Staged;
    }

    private async Task<GameDataStageResult> DecompressAsync(
        GameDataManifest manifest,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = new FileStream(
                partPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var brotli = new BrotliStream(source, CompressionMode.Decompress, leaveOpen: false);
            await using var destination = new FileStream(
                candidatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            long total = 0;
            while (true)
            {
                int read = await brotli.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (read > manifest.UncompressedSize - total)
                    throw new StageValidationException(GameDataStageStatus.UncompressedSizeMismatch);
                total += read;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (total != manifest.UncompressedSize)
                throw new StageValidationException(GameDataStageStatus.UncompressedSizeMismatch);
            return new(GameDataStageStatus.Staged);
        }
        catch (StageValidationException exception)
        {
            return CleanupAndReturn(exception.Status);
        }
        catch (OperationCanceledException)
        {
            return CleanupAndReturn(GameDataStageStatus.Cancelled);
        }
        catch (InvalidDataException exception)
        {
            return CleanupAndReturn(GameDataStageStatus.DecompressionFailed, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return CleanupAndReturn(GameDataStageStatus.DecompressionFailed, exception.Message);
        }
        catch (IOException exception)
        {
            return CleanupAndReturn(GameDataStageStatus.DecompressionFailed, exception.Message);
        }
    }

    private async Task<ValidationResult> ValidateDatabaseAsync(
        string path,
        GameDataManifest? manifest,
        CancellationToken cancellationToken)
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

            await using (var integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check;";
                object? value = await integrity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (!string.Equals(Convert.ToString(value), "ok", StringComparison.Ordinal))
                    return new(GameDataStageStatus.SqliteIntegrityFailed, Convert.ToString(value));
            }

            await using (var foreignKeys = connection.CreateCommand())
            {
                foreignKeys.CommandText = "PRAGMA foreign_key_check;";
                await using SqliteDataReader reader = await foreignKeys.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    return new(GameDataStageStatus.SqliteIntegrityFailed, "SQLite foreign-key validation failed.");
            }

            var tables = new HashSet<string>(StringComparer.Ordinal);
            await using (var schema = connection.CreateCommand())
            {
                schema.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table';";
                await using SqliteDataReader reader = await schema.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) tables.Add(reader.GetString(0));
            }
            string? missing = RequiredTables.FirstOrDefault(table => !tables.Contains(table));
            if (missing is not null) return new(GameDataStageStatus.RequiredTableMissing, missing);

            await using var versions = connection.CreateCommand();
            versions.CommandText = """
                SELECT m.data_version, m.schema_version, p.version
                FROM metadata m
                CROSS JOIN protocol_profiles p
                WHERE m.singleton_id = 1 AND p.is_active = 1;
                """;
            await using SqliteDataReader versionReader = await versions.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await versionReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return new(GameDataStageStatus.DatabaseVersionMismatch, "Required version rows are missing.");
            ulong dataVersion = checked((ulong)versionReader.GetInt64(0));
            uint schemaVersion = checked((uint)versionReader.GetInt64(1));
            uint profileVersion = checked((uint)versionReader.GetInt64(2));
            if (await versionReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return new(GameDataStageStatus.DatabaseVersionMismatch, "Multiple active version rows exist.");
            if (manifest is not null && (dataVersion != manifest.DataVersion
                || schemaVersion != manifest.SchemaVersion
                || profileVersion != manifest.ProtocolProfileVersion))
                return new(GameDataStageStatus.DatabaseVersionMismatch, "Database versions do not match the signed manifest.");
            if (manifest is null && schemaVersion != supportedSchemaVersion)
                return new(GameDataStageStatus.DatabaseVersionMismatch, "Database schema is incompatible with this application.");

            _ = await new GameDataRepository(path, GameDataCacheLimits.Default).LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            return new(GameDataStageStatus.Staged);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidDataException or OverflowException)
        {
            return new(GameDataStageStatus.SqliteIntegrityFailed, exception.Message);
        }
    }

    private void EnsureSafeDirectories()
    {
        EnsureNoReparseAncestors(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        EnsureNotReparsePoint(dataDirectory);
        Directory.CreateDirectory(updateDirectory);
        EnsureNotReparsePoint(updateDirectory);
        Directory.CreateDirectory(backupDirectory);
        EnsureNotReparsePoint(backupDirectory);
        EnsureSafeFile(activePath);
        EnsureSafeFile(partPath);
        EnsureSafeFile(candidatePath);
        EnsureSafeFile(backupPath);
        EnsureSafeFile(operationBackupPath);
        EnsureSafeFile(failedPath);
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Updater directory is a reparse point: {path}");
    }

    private static void EnsureNoReparseAncestors(string path)
    {
        for (DirectoryInfo? directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            if (!directory.Exists) continue;
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Updater path contains a reparse-point directory: {directory.FullName}");
        }
    }

    private static void EnsureSafeFile(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Updater file is a reparse point: {path}");
    }

    private void CleanupTransient(bool includeCandidate)
    {
        SafeDelete(partPath);
        if (includeCandidate) SafeDelete(candidatePath);
        SafeDelete(operationBackupPath);
        SafeDelete(failedPath);
        if (includeCandidate) stagedManifest = null;
    }

    private GameDataStageResult CleanupAndReturn(GameDataStageStatus status, string? detail = null)
    {
        CleanupTransient(includeCandidate: true);
        return new(status, detail);
    }

    private static void SafeDelete(string path)
    {
        EnsureSafeFile(path);
        if (File.Exists(path)) File.Delete(path);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Min(BufferSize, maximumBytes + 1));
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBytes + 1 - checked((int)output.Length))), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) return output.ToArray();
                output.Write(buffer, 0, read);
                if (output.Length > maximumBytes) throw new InvalidDataException("The game-data manifest exceeds its size limit.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed record ValidationResult(GameDataStageStatus Status, string? Detail = null);

    private sealed class StageValidationException(GameDataStageStatus status) : Exception
    {
        public GameDataStageStatus Status { get; } = status;
    }
}
