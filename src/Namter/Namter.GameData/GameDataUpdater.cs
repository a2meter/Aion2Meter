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
    InvalidArchiveUri,
    IncompatibleMinimumAppVersion,
    IncompatibleSchemaVersion,
    Cancelled,
    TransportFailed,
    InvalidManifestUri,
}

public enum GameDataStageStatus
{
    Staged,
    Cancelled,
    InvalidManifest,
    InvalidSignature,
    InvalidArchiveUri,
    IncompatibleMinimumAppVersion,
    IncompatibleSchemaVersion,
    DownloadFailed,
    CompressedSizeMismatch,
    Sha256Mismatch,
    DecompressionFailed,
    UncompressedSizeMismatch,
    SqliteIntegrityFailed,
    RequiredTableMissing,
    RequiredSchemaInvalid,
    DatabaseVersionMismatch,
    UnsafeFilesystem,
    RecoveryRequired,
    CleanupFailed,
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
    BackupPromotionFailedRolledBack,
    BackupPromotionFailedRestoreFailed,
    RecoveryRequired,
    Cancelled,
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
    BackupPromotionFailedRestored,
    BackupPromotionFailedRestoreFailed,
    RecoveryRequired,
    Cancelled,
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
    private readonly IGameDataFileSystem fileSystem;
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
        : this(dataDirectory, appVersion, supportedSchemaVersion, trustedPublicKeySpki, transport,
            isEncounterActive, reopenAndRebuild, PhysicalGameDataFileSystem.Instance)
    {
    }

    internal GameDataUpdater(
        string dataDirectory,
        Version appVersion,
        uint supportedSchemaVersion,
        ReadOnlySpan<byte> trustedPublicKeySpki,
        IGameDataTransport transport,
        Func<bool> isEncounterActive,
        Func<string, CancellationToken, Task>? reopenAndRebuild,
        IGameDataFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(appVersion);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(isEncounterActive);
        ArgumentNullException.ThrowIfNull(fileSystem);
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
        this.fileSystem = fileSystem;
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
        if (!manifestUri.IsAbsoluteUri || manifestUri.Scheme != Uri.UriSchemeHttps)
            return new(GameDataCheckStatus.InvalidManifestUri);
        try
        {
            await using Stream stream = await transport.OpenReadAsync(manifestUri, cancellationToken).ConfigureAwait(false);
            byte[] json = await ReadBoundedAsync(stream, GameDataManifest.MaximumJsonBytes, cancellationToken).ConfigureAwait(false);
            GameDataManifest manifest = GameDataManifest.Parse(json);
            GameDataStageStatus preliminary = ValidateManifest(manifest);
            return preliminary switch
            {
                GameDataStageStatus.InvalidSignature => new(GameDataCheckStatus.InvalidSignature),
                GameDataStageStatus.InvalidArchiveUri => new(GameDataCheckStatus.InvalidArchiveUri, manifest),
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
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(GameDataStageStatus.Cancelled);
        }
        try
        {
            try
            {
                EnsureSafeDirectories();
                RecoveryResult recovery = await ReconcileRecoveryAsync(preserveCandidate: false, cancellationToken)
                    .ConfigureAwait(false);
                if (!recovery.Success) return new(GameDataStageStatus.RecoveryRequired, recovery.Detail);
                CleanupStageTransient(includeCandidate: true);
            }
            catch (OperationCanceledException)
            {
                return new(GameDataStageStatus.Cancelled);
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
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(GameDataActivationStatus.Cancelled);
        }
        try
        {
            try
            {
                EnsureSafeDirectories();
                RecoveryResult recovery = await ReconcileRecoveryAsync(
                    preserveCandidate: stagedManifest is not null, cancellationToken).ConfigureAwait(false);
                if (!recovery.Success) return new(GameDataActivationStatus.RecoveryRequired, recovery.Detail);
                if (stagedManifest is null || !fileSystem.FileExists(candidatePath))
                    return new(GameDataActivationStatus.NoStagedUpdate);
                if (isEncounterActive()) return new(GameDataActivationStatus.DeferredEncounterActive);
                ValidationResult candidate = await ValidateDatabaseAsync(candidatePath, stagedManifest, cancellationToken)
                    .ConfigureAwait(false);
                if (candidate.Status != GameDataStageStatus.Staged)
                {
                    CleanupStageTransient(includeCandidate: true);
                    stagedManifest = null;
                    return new(GameDataActivationStatus.StagedDatabaseInvalid, candidate.Detail);
                }

                if (fileSystem.FileExists(activePath))
                {
                    ValidationResult active = await ValidateDatabaseAsync(activePath, manifest: null, cancellationToken)
                        .ConfigureAwait(false);
                    if (active.Status != GameDataStageStatus.Staged)
                        return new(GameDataActivationStatus.ActiveDatabaseInvalid, active.Detail);
                }

                if (fileSystem.FileExists(activePath))
                    fileSystem.ReplaceFile(candidatePath, activePath, operationBackupPath);
                else
                    fileSystem.MoveFile(candidatePath, activePath, overwrite: false);

                try
                {
                    await reopenAndRebuild(activePath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception activationException)
                {
                    return await RestoreActivationAsync(
                        activationException, promotionFailure: false).ConfigureAwait(false);
                }

                if (fileSystem.FileExists(operationBackupPath))
                {
                    try
                    {
                        fileSystem.MoveFile(operationBackupPath, backupPath, overwrite: true);
                    }
                    catch (Exception promotionException) when (promotionException is IOException or UnauthorizedAccessException)
                    {
                        return await RestoreActivationAsync(
                            promotionException, promotionFailure: true).ConfigureAwait(false);
                    }
                }
                CleanupStageTransient(includeCandidate: true);
                stagedManifest = null;
                return new(GameDataActivationStatus.Activated);
            }
            catch (OperationCanceledException)
            {
                return new(GameDataActivationStatus.Cancelled);
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
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(GameDataRollbackStatus.Cancelled);
        }
        try
        {
            try
            {
                EnsureSafeDirectories();
                RecoveryResult recovery = await ReconcileRecoveryAsync(preserveCandidate: false, cancellationToken)
                    .ConfigureAwait(false);
                if (!recovery.Success) return new(GameDataRollbackStatus.RecoveryRequired, recovery.Detail);
                if (isEncounterActive()) return new(GameDataRollbackStatus.DeferredEncounterActive);
                if (!fileSystem.FileExists(backupPath)) return new(GameDataRollbackStatus.NoBackup);
                ValidationResult backup = await ValidateDatabaseAsync(backupPath, manifest: null, cancellationToken)
                    .ConfigureAwait(false);
                if (backup.Status != GameDataStageStatus.Staged)
                    return new(GameDataRollbackStatus.BackupInvalid, backup.Detail);

                fileSystem.ReplaceFile(backupPath, activePath, operationBackupPath);
                try
                {
                    await reopenAndRebuild(activePath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                {
                    return await RestoreRollbackAsync(
                        rollbackException, promotionFailure: false).ConfigureAwait(false);
                }
                try
                {
                    fileSystem.MoveFile(operationBackupPath, backupPath, overwrite: true);
                }
                catch (Exception promotionException) when (promotionException is IOException or UnauthorizedAccessException)
                {
                    return await RestoreRollbackAsync(
                        promotionException, promotionFailure: true).ConfigureAwait(false);
                }
                return new(GameDataRollbackStatus.RolledBack);
            }
            catch (OperationCanceledException)
            {
                return new(GameDataRollbackStatus.Cancelled);
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
        if (manifest.ArchiveUri.Scheme != Uri.UriSchemeHttps) return GameDataStageStatus.InvalidArchiveUri;
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

            string? schemaError = await GameDataSchemaValidator.ValidateAsync(
                connection, manifest?.SchemaVersion ?? supportedSchemaVersion, cancellationToken).ConfigureAwait(false);
            if (schemaError is not null) return new(GameDataStageStatus.RequiredSchemaInvalid, schemaError);

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

    private async Task<GameDataActivationResult> RestoreActivationAsync(Exception cause, bool promotionFailure)
    {
        if (!fileSystem.FileExists(operationBackupPath))
        {
            try
            {
                SafeDelete(activePath);
                CleanupStageTransient(includeCandidate: true);
                stagedManifest = null;
                return new(GameDataActivationStatus.ActivationFailedRolledBack, cause.Message);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
                return new(GameDataActivationStatus.RecoveryRequired,
                    $"Activation failed: {cause.Message}; cleanup failed: {cleanupException.Message}");
            }
        }

        try
        {
            fileSystem.ReplaceFile(operationBackupPath, activePath, failedPath);
            await reopenAndRebuild(activePath, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception restoreException)
        {
            return new(
                promotionFailure
                    ? GameDataActivationStatus.BackupPromotionFailedRestoreFailed
                    : GameDataActivationStatus.ActivationFailedRollbackFailed,
                $"Cause: {cause.Message}; restore: {restoreException.Message}");
        }

        try
        {
            SafeDelete(failedPath);
            CleanupStageTransient(includeCandidate: true);
            stagedManifest = null;
        }
        catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
        {
            return new(GameDataActivationStatus.RecoveryRequired,
                $"Database restored but recovery cleanup failed: {cleanupException.Message}");
        }
        return new(
            promotionFailure
                ? GameDataActivationStatus.BackupPromotionFailedRolledBack
                : GameDataActivationStatus.ActivationFailedRolledBack,
            cause.Message);
    }

    private async Task<GameDataRollbackResult> RestoreRollbackAsync(Exception cause, bool promotionFailure)
    {
        try
        {
            fileSystem.ReplaceFile(operationBackupPath, activePath, failedPath);
            await reopenAndRebuild(activePath, CancellationToken.None).ConfigureAwait(false);
            fileSystem.MoveFile(failedPath, backupPath, overwrite: true);
        }
        catch (Exception restoreException)
        {
            return new(
                promotionFailure
                    ? GameDataRollbackStatus.BackupPromotionFailedRestoreFailed
                    : GameDataRollbackStatus.RollbackFailedRestoreFailed,
                $"Cause: {cause.Message}; restore: {restoreException.Message}");
        }
        return new(
            promotionFailure
                ? GameDataRollbackStatus.BackupPromotionFailedRestored
                : GameDataRollbackStatus.RollbackFailedRestoredCurrent,
            cause.Message);
    }

    private async Task<RecoveryResult> ReconcileRecoveryAsync(
        bool preserveCandidate,
        CancellationToken cancellationToken)
    {
        bool hasOperationBackup = fileSystem.FileExists(operationBackupPath);
        bool hasFailed = fileSystem.FileExists(failedPath);
        bool hasPart = fileSystem.FileExists(partPath);
        bool hasOrphanCandidate = fileSystem.FileExists(candidatePath) && !preserveCandidate;
        if (!hasOperationBackup && !hasFailed && !hasPart && !hasOrphanCandidate)
            return new(true);

        bool activeExists = fileSystem.FileExists(activePath);
        bool activeValid = activeExists && await ValidateAndReopenAsync(activePath, cancellationToken).ConfigureAwait(false);

        if (hasOperationBackup)
        {
            bool operationValid = (await ValidateDatabaseAsync(operationBackupPath, manifest: null, cancellationToken)
                .ConfigureAwait(false)).Status == GameDataStageStatus.Staged;
            if (activeValid)
            {
                try
                {
                    if (operationValid) fileSystem.MoveFile(operationBackupPath, backupPath, overwrite: true);
                    else SafeDelete(operationBackupPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return new(false, $"Could not promote interrupted operation backup: {exception.Message}");
                }
            }
            else if (operationValid)
            {
                try
                {
                    if (activeExists) fileSystem.ReplaceFile(operationBackupPath, activePath, failedPath);
                    else fileSystem.MoveFile(operationBackupPath, activePath, overwrite: false);
                    activeExists = true;
                    activeValid = await ValidateAndReopenAsync(activePath, cancellationToken).ConfigureAwait(false);
                    if (!activeValid) return new(false, "Restored operation backup could not be reopened.");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return new(false, $"Could not restore interrupted operation backup: {exception.Message}");
                }
            }
            else
            {
                return new(false, "Neither active database nor interrupted operation backup is valid.");
            }
        }

        if (hasFailed || fileSystem.FileExists(failedPath))
        {
            bool failedValid = (await ValidateDatabaseAsync(failedPath, manifest: null, cancellationToken)
                .ConfigureAwait(false)).Status == GameDataStageStatus.Staged;
            try
            {
                if (activeValid && failedValid && !fileSystem.FileExists(backupPath))
                    fileSystem.MoveFile(failedPath, backupPath, overwrite: false);
                else if (activeValid || (!activeExists && !failedValid))
                    SafeDelete(failedPath);
                else if (!activeValid && failedValid)
                {
                    if (activeExists) fileSystem.ReplaceFile(failedPath, activePath, operationBackupPath);
                    else fileSystem.MoveFile(failedPath, activePath, overwrite: false);
                    activeExists = true;
                    activeValid = await ValidateAndReopenAsync(activePath, cancellationToken).ConfigureAwait(false);
                    if (!activeValid) return new(false, "Fallback recovery database could not be reopened.");
                }
                else return new(false, "Interrupted recovery state is inconsistent.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(false, $"Could not reconcile interrupted recovery file: {exception.Message}");
            }
        }

        bool consistent = activeValid || !activeExists;
        if (!consistent) return new(false, "Active database is invalid and no valid recovery database exists.");
        try
        {
            SafeDelete(partPath);
            if (!preserveCandidate)
            {
                SafeDelete(candidatePath);
                stagedManifest = null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(false, $"Could not clean reconciled transient state: {exception.Message}");
        }
        return new(true);
    }

    private async Task<bool> ValidateAndReopenAsync(string path, CancellationToken cancellationToken)
    {
        ValidationResult validation = await ValidateDatabaseAsync(path, manifest: null, cancellationToken)
            .ConfigureAwait(false);
        if (validation.Status != GameDataStageStatus.Staged) return false;
        try
        {
            await reopenAndRebuild(path, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureSafeDirectories()
    {
        EnsureNoReparseAncestors(dataDirectory);
        fileSystem.CreateDirectory(dataDirectory);
        EnsureNotReparsePoint(dataDirectory);
        fileSystem.CreateDirectory(updateDirectory);
        EnsureNotReparsePoint(updateDirectory);
        fileSystem.CreateDirectory(backupDirectory);
        EnsureNotReparsePoint(backupDirectory);
        EnsureSafeFile(activePath);
        EnsureSafeFile(partPath);
        EnsureSafeFile(candidatePath);
        EnsureSafeFile(backupPath);
        EnsureSafeFile(operationBackupPath);
        EnsureSafeFile(failedPath);
    }

    private void EnsureNotReparsePoint(string path)
    {
        if ((fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
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

    private void EnsureSafeFile(string path)
    {
        if (fileSystem.FileExists(path) && (fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Updater file is a reparse point: {path}");
    }

    private void CleanupStageTransient(bool includeCandidate)
    {
        var failures = new List<Exception>();
        TryDelete(partPath, failures);
        if (includeCandidate) TryDelete(candidatePath, failures);
        if (includeCandidate) stagedManifest = null;
        if (failures.Count != 0)
            throw new IOException("One or more game-data transient files could not be removed.", new AggregateException(failures));
    }

    private void TryDelete(string path, List<Exception> failures)
    {
        try
        {
            SafeDelete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures.Add(exception);
        }
    }

    private GameDataStageResult CleanupAndReturn(GameDataStageStatus status, string? detail = null)
    {
        try
        {
            CleanupStageTransient(includeCandidate: true);
            return new(status, detail);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(GameDataStageStatus.CleanupFailed,
                $"Original status {status}; cleanup: {exception.Message}");
        }
    }

    private void SafeDelete(string path)
    {
        EnsureSafeFile(path);
        if (fileSystem.FileExists(path)) fileSystem.DeleteFile(path);
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
    private sealed record RecoveryResult(bool Success, string? Detail = null);

    private sealed class StageValidationException(GameDataStageStatus status) : Exception
    {
        public GameDataStageStatus Status { get; } = status;
    }
}
