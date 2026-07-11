using System.Security.Cryptography;
using Namter.GameData;
using Namter.GameData.Builder;
using Namter.GameData.Publisher;

namespace Namter.Tests.Unit.GameData;

public sealed class GameDataPublisherTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task PublisherCreatesVerifiableStaticArtifactsConsumableByRuntimeUpdater()
    {
        using var fixture = await PublisherFixture.CreateAsync();

        PublishResult published = await fixture.PublishAsync();

        Assert.Equal(PublishStatus.Published, published.Status);
        byte[] manifestBytes = await File.ReadAllBytesAsync(Path.Combine(fixture.Output, "manifest.json"));
        GameDataManifest manifest = GameDataManifest.Parse(manifestBytes);
        Assert.True(manifest.Verify(fixture.Signer.ExportSubjectPublicKeyInfo()));
        Assert.Equal(2UL, manifest.DataVersion);
        Assert.Equal(new Uri("https://cdn.example/data/aion.db.br"), manifest.ArchiveUri);

        var transport = new MemoryTransport(manifest.ArchiveUri,
            await File.ReadAllBytesAsync(Path.Combine(fixture.Output, "aion.db.br")));
        var updater = new GameDataUpdater(fixture.RuntimeData, new Version(1, 0), 1,
            fixture.Signer.ExportSubjectPublicKeyInfo(), transport, static () => false);
        Assert.Equal(GameDataStageStatus.Staged, (await updater.StageAsync(manifest, default)).Status);
    }

    [Fact]
    public async Task PublisherRefusesNonHttpsUnlessTestPolicyExplicitlyAllowsIt()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        PublishOptions insecure = fixture.Options with { ArchiveUri = new Uri("http://localhost/aion.db.br") };

        Assert.Equal(PublishStatus.InsecureArchiveUri, (await GameDataPublisher.PublishAsync(insecure)).Status);
        Assert.Equal(PublishStatus.Published, (await GameDataPublisher.PublishAsync(
            insecure with { Policy = fixture.Options.Policy with { AllowInsecureArchiveUri = true } })).Status);
    }

    [Fact]
    public async Task PublisherRefusesOutputInsideSourceTree()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        string inside = Path.Combine(fixture.SourceRoot, "artifacts");

        PublishResult result = await GameDataPublisher.PublishAsync(fixture.Options with { OutputDirectory = inside });

        Assert.Equal(PublishStatus.OutputInsideSourceTree, result.Status);
        Assert.False(Directory.Exists(inside));
    }

    [Fact]
    public async Task PublisherDoesNotOverwriteWithoutForceAndForceReplacesBothArtifacts()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        Assert.Equal(PublishStatus.Published, (await fixture.PublishAsync()).Status);
        await File.WriteAllTextAsync(Path.Combine(fixture.Output, "manifest.json"), "sentinel");

        Assert.Equal(PublishStatus.OutputExists, (await fixture.PublishAsync()).Status);
        Assert.Equal("sentinel", await File.ReadAllTextAsync(Path.Combine(fixture.Output, "manifest.json")));
        Assert.Equal(PublishStatus.Published, (await GameDataPublisher.PublishAsync(
            fixture.Options with { Force = true })).Status);
        Assert.NotEqual("sentinel", await File.ReadAllTextAsync(Path.Combine(fixture.Output, "manifest.json")));
    }

    [Fact]
    public async Task PublisherNeverCopiesPrivateKeyIntoOutput()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        string pem = await File.ReadAllTextAsync(fixture.PrivateKeyPath);

        Assert.Equal(PublishStatus.Published, (await fixture.PublishAsync()).Status);

        Assert.DoesNotContain(Directory.EnumerateFiles(fixture.Output), path =>
            string.Equals(Path.GetFileName(path), Path.GetFileName(fixture.PrivateKeyPath), StringComparison.OrdinalIgnoreCase));
        Assert.All(Directory.EnumerateFiles(fixture.Output), path =>
            Assert.DoesNotContain(pem, File.ReadAllText(path), StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublisherRejectsInputMissingRequiredTableWithoutWritingArtifacts()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={fixture.Options.InputPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE mobs;";
            await command.ExecuteNonQueryAsync();
        }

        PublishResult result = await fixture.PublishAsync();

        Assert.Equal(PublishStatus.InputInvalid, result.Status);
        Assert.False(Directory.Exists(fixture.Output));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Namter.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    private sealed class PublisherFixture : IDisposable
    {
        private readonly string directory;
        public ECDsa Signer { get; }
        public string SourceRoot { get; }
        public string Output { get; }
        public string RuntimeData { get; }
        public string PrivateKeyPath { get; }
        public PublishOptions Options { get; }

        private PublisherFixture(string directory, ECDsa signer)
        {
            this.directory = directory;
            Signer = signer;
            SourceRoot = Path.Combine(directory, "source");
            Output = Path.Combine(directory, "published");
            RuntimeData = Path.Combine(directory, "runtime-data");
            PrivateKeyPath = Path.Combine(directory, "publisher-key.pem");
            Options = new PublishOptions(
                Path.Combine(directory, "input.db"), Output, new Uri("https://cdn.example/data/aion.db.br"),
                2, new Version(1, 0), PrivateKeyPath, false, new PublisherPolicy(SourceRoot, false));
        }

        public static async Task<PublisherFixture> CreateAsync()
        {
            string directory = Path.Combine(Path.GetTempPath(), $"namter-publisher-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var fixture = new PublisherFixture(directory, ECDsa.Create(ECCurve.NamedCurves.nistP256));
            Directory.CreateDirectory(fixture.SourceRoot);
            Directory.CreateDirectory(fixture.RuntimeData);
            await GameDataDatabaseBuilder.BuildAsync(
                fixture.Options.InputPath,
                Path.Combine(RepositoryRoot, "db", "schema", "001_initial.sql"),
                Path.Combine(RepositoryRoot, "db", "seed", "golden_protocol.sql"));
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={fixture.Options.InputPath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE metadata SET data_version = 2 WHERE singleton_id = 1;";
                await command.ExecuteNonQueryAsync();
            }
            await File.WriteAllTextAsync(fixture.PrivateKeyPath, fixture.Signer.ExportECPrivateKeyPem());
            return fixture;
        }

        public Task<PublishResult> PublishAsync() => GameDataPublisher.PublishAsync(Options);

        public void Dispose()
        {
            Signer.Dispose();
            Directory.Delete(directory, true);
        }
    }

    private sealed class MemoryTransport(Uri uri, byte[] archive) : IGameDataTransport
    {
        public ValueTask<Stream> OpenReadAsync(Uri requested, CancellationToken cancellationToken)
        {
            Assert.Equal(uri, requested);
            return ValueTask.FromResult<Stream>(new MemoryStream(archive, writable: false));
        }
    }
}
