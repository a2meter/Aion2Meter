using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Namter.GameData;
using Namter.GameData.Builder;

namespace Namter.Tests.Unit.GameData;

public sealed class GameDataUpdaterTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CanonicalManifestExcludesSignatureAndUsesDeclaredPropertyOrder()
    {
        var manifest = new GameDataManifest(
            2, 1, 7, new Version(1, 2, 3), new Uri("https://updates.example/aion.db.br"),
            123, 456, new string('a', 64), "br", DateTimeOffset.Parse("2026-07-11T01:02:03+00:00"), "secret");

        string json = System.Text.Encoding.UTF8.GetString(manifest.GetCanonicalUnsignedBytes());

        Assert.Equal("{\"DataVersion\":2,\"SchemaVersion\":1,\"ProtocolProfileVersion\":7," +
            "\"MinimumAppVersion\":\"1.2.3\",\"ArchiveUri\":\"https://updates.example/aion.db.br\"," +
            "\"CompressedSize\":123,\"UncompressedSize\":456,\"Sha256\":\"" + new string('a', 64) +
            "\",\"Compression\":\"br\",\"CreatedUtc\":\"2026-07-11T01:02:03.0000000+00:00\"}", json);
        Assert.DoesNotContain("Signature", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameVersionCheckDoesNotRequestArchive()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        GameDataManifest manifest = fixture.CreateManifest(await fixture.ReadDatabaseAsync(), 1);
        fixture.Transport.Add(fixture.ManifestUri, manifest.ToJsonBytes());

        GameDataCheckResult result = await fixture.Updater.CheckAsync(fixture.ManifestUri, new DataVersion(1), default);

        Assert.Equal(GameDataCheckStatus.UpToDate, result.Status);
        Assert.Equal(new[] { fixture.ManifestUri }, fixture.Transport.Requests);
    }

    [Fact]
    public async Task NewerValidUpdateStagesAndLeavesActiveDatabaseUntouched()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] active = await File.ReadAllBytesAsync(fixture.ActivePath);
        byte[] candidate = await fixture.CreateDatabaseAsync(2);
        GameDataManifest manifest = fixture.CreateManifest(candidate, 2);
        fixture.Transport.Add(manifest.ArchiveUri, Compress(candidate));

        GameDataStageResult result = await fixture.Updater.StageAsync(manifest, default);

        Assert.Equal(GameDataStageStatus.Staged, result.Status);
        Assert.Equal(active, await File.ReadAllBytesAsync(fixture.ActivePath));
        Assert.Equal(candidate, await File.ReadAllBytesAsync(fixture.CandidatePath));
        Assert.False(File.Exists(fixture.PartPath));
    }

    [Fact]
    public async Task CancelledDownloadCleansTransientFilesAndPreservesActiveDatabase()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] candidate = await fixture.CreateDatabaseAsync(2);
        GameDataManifest manifest = fixture.CreateManifest(candidate, 2);
        fixture.Transport.Add(manifest.ArchiveUri, new CancellingStream(Compress(candidate), 8));

        await fixture.AssertStageFailureAsync(manifest, GameDataStageStatus.Cancelled);
    }

    [Fact]
    public async Task CompressedLengthMismatchCleansTransientFilesAndPreservesActiveDatabase()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] candidate = await fixture.CreateDatabaseAsync(2);
        GameDataManifest manifest = fixture.CreateManifest(candidate, 2) with { CompressedSize = Compress(candidate).Length + 1 };
        manifest = fixture.Sign(manifest);
        fixture.Transport.Add(manifest.ArchiveUri, Compress(candidate));

        await fixture.AssertStageFailureAsync(manifest, GameDataStageStatus.CompressedSizeMismatch);
    }

    [Fact]
    public async Task Sha256MismatchCleansTransientFilesAndPreservesActiveDatabase()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] candidate = await fixture.CreateDatabaseAsync(2);
        GameDataManifest manifest = fixture.CreateManifest(candidate, 2) with { Sha256 = new string('0', 64) };
        manifest = fixture.Sign(manifest);
        fixture.Transport.Add(manifest.ArchiveUri, Compress(candidate));

        await fixture.AssertStageFailureAsync(manifest, GameDataStageStatus.Sha256Mismatch);
    }

    [Fact]
    public async Task InvalidSignatureDoesNotRequestArchiveAndPreservesActiveDatabase()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] candidate = await fixture.CreateDatabaseAsync(2);
        GameDataManifest manifest = fixture.CreateManifest(candidate, 2) with { Signature = Convert.ToBase64String(new byte[64]) };

        await fixture.AssertStageFailureAsync(manifest, GameDataStageStatus.InvalidSignature);
        Assert.Empty(fixture.Transport.Requests);
    }

    [Fact]
    public async Task BrotliFailureCleansTransientFilesAndPreservesActiveDatabase()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] invalidArchive = "not-brotli"u8.ToArray();
        GameDataManifest manifest = fixture.CreateManifestFromArchive(invalidArchive, 4096, 2);
        fixture.Transport.Add(manifest.ArchiveUri, invalidArchive);

        await fixture.AssertStageFailureAsync(manifest, GameDataStageStatus.DecompressionFailed);
    }

    [Fact]
    public async Task UncompressedLengthMismatchCleansTransientFilesAndPreservesActiveDatabase()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] candidate = await fixture.CreateDatabaseAsync(2);
        byte[] archive = Compress(candidate);
        GameDataManifest manifest = fixture.CreateManifestFromArchive(archive, candidate.Length + 1, 2);
        fixture.Transport.Add(manifest.ArchiveUri, archive);

        await fixture.AssertStageFailureAsync(manifest, GameDataStageStatus.UncompressedSizeMismatch);
    }

    [Fact]
    public async Task SQLiteIntegrityFailureCleansTransientFilesAndPreservesActiveDatabase()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] invalidDatabase = new byte[4096];
        RandomNumberGenerator.Fill(invalidDatabase);
        GameDataManifest manifest = fixture.CreateManifest(invalidDatabase, 2);
        fixture.Transport.Add(manifest.ArchiveUri, Compress(invalidDatabase));

        await fixture.AssertStageFailureAsync(manifest, GameDataStageStatus.SqliteIntegrityFailed);
    }

    [Fact]
    public async Task MissingRequiredTableCleansTransientFilesAndPreservesActiveDatabase()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] candidate = await fixture.CreateDatabaseAsync(2, "DROP TABLE buffs;");
        GameDataManifest manifest = fixture.CreateManifest(candidate, 2);
        fixture.Transport.Add(manifest.ArchiveUri, Compress(candidate));

        await fixture.AssertStageFailureAsync(manifest, GameDataStageStatus.RequiredTableMissing);
    }

    [Fact]
    public async Task ForeignKeyIntegrityFailureCleansTransientFilesAndPreservesActiveDatabase()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] candidate = await fixture.CreateDatabaseAsync(2,
            "PRAGMA foreign_keys = OFF; INSERT INTO dungeon_bosses(dungeon_id, boss_id, encounter_order) VALUES (999, 999, 999);");
        GameDataManifest manifest = fixture.CreateManifest(candidate, 2);
        fixture.Transport.Add(manifest.ArchiveUri, Compress(candidate));

        await fixture.AssertStageFailureAsync(manifest, GameDataStageStatus.SqliteIntegrityFailed);
    }

    [Fact]
    public async Task IncompatibleSchemaAndMinimumAppVersionAreRejectedBeforeArchiveRequest()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] candidate = await fixture.CreateDatabaseAsync(2);
        GameDataManifest schema = fixture.Sign(fixture.CreateManifest(candidate, 2) with { SchemaVersion = 2 });
        GameDataManifest app = fixture.Sign(fixture.CreateManifest(candidate, 2) with { MinimumAppVersion = new Version(99, 0) });

        Assert.Equal(GameDataStageStatus.IncompatibleSchemaVersion, (await fixture.Updater.StageAsync(schema, default)).Status);
        Assert.Equal(GameDataStageStatus.IncompatibleMinimumAppVersion, (await fixture.Updater.StageAsync(app, default)).Status);
        Assert.Empty(fixture.Transport.Requests);
        fixture.AssertNoTransientFiles();
    }

    [Fact]
    public async Task ActivationAtomicallyReplacesDatabaseAndRetainsExactlyOnePreviousValidatedBackup()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] version1 = await File.ReadAllBytesAsync(fixture.ActivePath);
        byte[] version2 = await fixture.CreateDatabaseAsync(2);
        await fixture.StageAsync(version2, 2);

        GameDataActivationResult first = await fixture.Updater.ActivateWhenIdleAsync(default);

        Assert.Equal(GameDataActivationStatus.Activated, first.Status);
        Assert.Equal(version2, await File.ReadAllBytesAsync(fixture.ActivePath));
        Assert.Equal(version1, await File.ReadAllBytesAsync(fixture.BackupPath));
        Assert.False(File.Exists(fixture.CandidatePath));

        byte[] version3 = await fixture.CreateDatabaseAsync(3);
        await fixture.StageAsync(version3, 3);
        Assert.Equal(GameDataActivationStatus.Activated, (await fixture.Updater.ActivateWhenIdleAsync(default)).Status);
        Assert.Equal(version3, await File.ReadAllBytesAsync(fixture.ActivePath));
        Assert.Equal(version2, await File.ReadAllBytesAsync(fixture.BackupPath));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.BackupPath)!));
    }

    [Fact]
    public async Task ActivationDefersWhileEncounterIsActive()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] active = await File.ReadAllBytesAsync(fixture.ActivePath);
        byte[] version2 = await fixture.CreateDatabaseAsync(2);
        await fixture.StageAsync(version2, 2);
        fixture.EncounterActive = true;

        GameDataActivationResult result = await fixture.Updater.ActivateWhenIdleAsync(default);

        Assert.Equal(GameDataActivationStatus.DeferredEncounterActive, result.Status);
        Assert.Equal(active, await File.ReadAllBytesAsync(fixture.ActivePath));
        Assert.Equal(version2, await File.ReadAllBytesAsync(fixture.CandidatePath));
    }

    [Fact]
    public async Task FailedReopenRollsBackToByteIdenticalActiveDatabase()
    {
        using var fixture = await UpdateFixture.CreateAsync(failReopenForVersion: 2);
        byte[] active = await File.ReadAllBytesAsync(fixture.ActivePath);
        byte[] version2 = await fixture.CreateDatabaseAsync(2);
        await fixture.StageAsync(version2, 2);

        GameDataActivationResult result = await fixture.Updater.ActivateWhenIdleAsync(default);

        Assert.Equal(GameDataActivationStatus.ActivationFailedRolledBack, result.Status);
        Assert.Equal(active, await File.ReadAllBytesAsync(fixture.ActivePath));
        Assert.False(File.Exists(fixture.CandidatePath));
        Assert.False(File.Exists(fixture.PartPath));
    }

    [Fact]
    public async Task ExplicitRollbackSwapsToValidatedBackupAndRetainsOnePreviousSnapshot()
    {
        using var fixture = await UpdateFixture.CreateAsync();
        byte[] version1 = await File.ReadAllBytesAsync(fixture.ActivePath);
        byte[] version2 = await fixture.CreateDatabaseAsync(2);
        await fixture.StageAsync(version2, 2);
        Assert.Equal(GameDataActivationStatus.Activated, (await fixture.Updater.ActivateWhenIdleAsync(default)).Status);

        GameDataRollbackResult result = await fixture.Updater.RollbackAsync(default);

        Assert.Equal(GameDataRollbackStatus.RolledBack, result.Status);
        Assert.Equal(version1, await File.ReadAllBytesAsync(fixture.ActivePath));
        Assert.Equal(version2, await File.ReadAllBytesAsync(fixture.BackupPath));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.BackupPath)!));
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true)) brotli.Write(bytes);
        return output.ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Namter.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    private sealed class UpdateFixture : IDisposable
    {
        private readonly string directory;
        private readonly ECDsa signer;
        public MemoryTransport Transport { get; } = new();
        public Uri ManifestUri { get; } = new("https://updates.example/manifest.json");
        public string ActivePath => Path.Combine(directory, "aion.db");
        public string CandidatePath => Path.Combine(directory, ".update", "aion.db.candidate");
        public string PartPath => Path.Combine(directory, ".update", "aion.db.br.part");
        public string BackupPath => Path.Combine(directory, "backup", "aion.previous.db");
        public bool EncounterActive { get; set; }
        public GameDataUpdater Updater { get; }

        private UpdateFixture(string directory, ECDsa signer, ulong? failReopenForVersion)
        {
            this.directory = directory;
            this.signer = signer;
            Updater = new GameDataUpdater(
                directory, new Version(1, 0), 1, signer.ExportSubjectPublicKeyInfo(), Transport,
                () => EncounterActive,
                async (path, cancellationToken) =>
                {
                    GameDataSnapshot snapshot = await new GameDataRepository(path, GameDataCacheLimits.Default).LoadAsync(cancellationToken);
                    if (snapshot.DataVersion == failReopenForVersion) throw new InvalidDataException("simulated cache rebuild failure");
                });
        }

        public static async Task<UpdateFixture> CreateAsync(ulong? failReopenForVersion = null)
        {
            string directory = Path.Combine(Path.GetTempPath(), $"namter-update-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var fixture = new UpdateFixture(directory, ECDsa.Create(ECCurve.NamedCurves.nistP256), failReopenForVersion);
            await BuildDatabaseAsync(fixture.ActivePath);
            return fixture;
        }

        public async Task<byte[]> CreateDatabaseAsync(ulong version, string? sql = null)
        {
            string path = Path.Combine(directory, $"source-{Guid.NewGuid():N}.db");
            await BuildDatabaseAsync(path);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"UPDATE metadata SET data_version = {version} WHERE singleton_id = 1;" + sql;
                await command.ExecuteNonQueryAsync();
            }
            byte[] bytes = await File.ReadAllBytesAsync(path);
            File.Delete(path);
            return bytes;
        }

        public Task<byte[]> ReadDatabaseAsync() => File.ReadAllBytesAsync(ActivePath);

        public GameDataManifest CreateManifest(byte[] database, ulong version)
        {
            byte[] archive = Compress(database);
            return CreateManifestFromArchive(archive, database.LongLength, version);
        }

        public GameDataManifest CreateManifestFromArchive(byte[] archive, long uncompressedSize, ulong version)
            => Sign(new GameDataManifest(
                version, 1, 1, new Version(1, 0), new Uri($"https://updates.example/{version}/aion.db.br"),
                archive.LongLength, uncompressedSize, Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant(),
                "br", DateTimeOffset.Parse("2026-07-11T00:00:00Z"), string.Empty));

        public GameDataManifest Sign(GameDataManifest manifest)
            => manifest with { Signature = Convert.ToBase64String(signer.SignData(
                manifest.GetCanonicalUnsignedBytes(), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };

        public async Task StageAsync(byte[] database, ulong version)
        {
            GameDataManifest manifest = CreateManifest(database, version);
            Transport.Add(manifest.ArchiveUri, Compress(database));
            Assert.Equal(GameDataStageStatus.Staged, (await Updater.StageAsync(manifest, default)).Status);
        }

        public async Task AssertStageFailureAsync(GameDataManifest manifest, GameDataStageStatus expected)
        {
            byte[] active = await File.ReadAllBytesAsync(ActivePath);
            Assert.Equal(expected, (await Updater.StageAsync(manifest, default)).Status);
            Assert.Equal(active, await File.ReadAllBytesAsync(ActivePath));
            AssertNoTransientFiles();
        }

        public void AssertNoTransientFiles()
        {
            Assert.False(File.Exists(PartPath));
            Assert.False(File.Exists(CandidatePath));
        }

        private static Task BuildDatabaseAsync(string path) => GameDataDatabaseBuilder.BuildAsync(
            path,
            Path.Combine(RepositoryRoot, "db", "schema", "001_initial.sql"),
            Path.Combine(RepositoryRoot, "db", "seed", "golden_protocol.sql"));

        public void Dispose()
        {
            signer.Dispose();
            Directory.Delete(directory, true);
        }
    }

    private sealed class MemoryTransport : IGameDataTransport
    {
        private readonly Dictionary<Uri, Func<Stream>> responses = new();
        public List<Uri> Requests { get; } = new();
        public void Add(Uri uri, byte[] bytes) => responses[uri] = () => new MemoryStream(bytes, writable: false);
        public void Add(Uri uri, Stream stream) => responses[uri] = () => stream;
        public ValueTask<Stream> OpenReadAsync(Uri uri, CancellationToken cancellationToken)
        {
            Requests.Add(uri);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(responses[uri]());
        }
    }

    private sealed class CancellingStream(byte[] bytes, int bytesBeforeCancellation) : MemoryStream(bytes, writable: false)
    {
        private int remaining = bytesBeforeCancellation;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (remaining <= 0) throw new OperationCanceledException(cancellationToken);
            int count = await base.ReadAsync(buffer[..Math.Min(buffer.Length, remaining)], cancellationToken);
            remaining -= count;
            return count;
        }
    }
}
