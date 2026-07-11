using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
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
        Assert.True(P256Signature.IsCanonical(Convert.FromBase64String(manifest.Signature)));
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

    [Fact]
    public async Task PublisherRejectsInputMissingEssentialIndex()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={fixture.Options.InputPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP INDEX idx_mobs_code;";
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal(PublishStatus.InputInvalid, (await fixture.PublishAsync()).Status);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task CliSourceTreeGuardIsIndependentOfCurrentWorkingDirectory()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        string unrelated = Path.Combine(Path.GetTempPath(), $"namter-cwd-{Guid.NewGuid():N}");
        string output = Path.Combine(RepositoryRoot, $"publisher-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(unrelated);
        string original = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(unrelated);
            int result = await Program.Main([
                "--input", fixture.Options.InputPath,
                "--output", output,
                "--archive-uri", "https://cdn.example/aion.db.br",
                "--data-version", "2",
                "--minimum-app-version", "1.0",
                "--private-key", fixture.PrivateKeyPath,
            ]);

            Assert.Equal((int)PublishStatus.OutputInsideSourceTree, result);
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
            Directory.Delete(unrelated, true);
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public async Task ForcedPublishSecondCommitFailureRestoresOriginalPair()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        Assert.Equal(PublishStatus.Published, (await fixture.PublishAsync()).Status);
        byte[] archive = await File.ReadAllBytesAsync(Path.Combine(fixture.Output, "aion.db.br"));
        byte[] manifest = await File.ReadAllBytesAsync(Path.Combine(fixture.Output, "manifest.json"));
        var fileSystem = new PublisherFaultingFileSystem
        {
            Fail = (operation, _, destination) => operation == nameof(IGameDataFileSystem.ReplaceFile)
                && destination == Path.Combine(fixture.Output, "manifest.json"),
        };

        PublishResult result = await GameDataPublisher.PublishAsync(
            fixture.Options with { Force = true }, fileSystem, default);

        Assert.Equal(PublishStatus.CommitFailedRestored, result.Status);
        Assert.Equal(archive, await File.ReadAllBytesAsync(Path.Combine(fixture.Output, "aion.db.br")));
        Assert.Equal(manifest, await File.ReadAllBytesAsync(Path.Combine(fixture.Output, "manifest.json")));
        Assert.Empty(Directory.EnumerateFiles(fixture.Output, "*.previous"));
    }

    [Fact]
    public async Task ForcedPublishRestoreFailureRetainsPreviousRecoveryArtifact()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        Assert.Equal(PublishStatus.Published, (await fixture.PublishAsync()).Status);
        var fileSystem = new PublisherFaultingFileSystem
        {
            Fail = (operation, source, destination) => operation == nameof(IGameDataFileSystem.ReplaceFile)
                && (destination == Path.Combine(fixture.Output, "manifest.json")
                    || (source.EndsWith(".previous", StringComparison.Ordinal) && destination == Path.Combine(fixture.Output, "aion.db.br"))),
        };

        PublishResult result = await GameDataPublisher.PublishAsync(
            fixture.Options with { Force = true }, fileSystem, default);

        Assert.Equal(PublishStatus.RecoveryRequired, result.Status);
        Assert.NotEmpty(Directory.EnumerateFiles(fixture.Output, "*.previous"));
    }

    [Fact]
    public async Task ForcedPublishRecoveryCleanupFailureRetainsRemainingPreviousArtifact()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        Assert.Equal(PublishStatus.Published, (await fixture.PublishAsync()).Status);
        var fileSystem = new PublisherFaultingFileSystem
        {
            Fail = (operation, source, _) => operation == nameof(IGameDataFileSystem.DeleteFile)
                && source.EndsWith(".previous", StringComparison.Ordinal),
        };

        PublishResult result = await GameDataPublisher.PublishAsync(
            fixture.Options with { Force = true }, fileSystem, default);

        Assert.Equal(PublishStatus.RecoveryRequired, result.Status);
        Assert.NotEmpty(Directory.EnumerateFiles(fixture.Output, "*.previous"));
    }

    [Fact]
    public async Task FirstPublishSecondCommitFailureDeletesNewArchiveAndRestoresEmptyOutput()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        var fileSystem = new PublisherFaultingFileSystem
        {
            Fail = (operation, _, destination) => operation == nameof(IGameDataFileSystem.MoveFile)
                && destination == Path.Combine(fixture.Output, "manifest.json"),
        };

        PublishResult result = await GameDataPublisher.PublishAsync(fixture.Options, fileSystem, default);

        Assert.Equal(PublishStatus.CommitFailedRestored, result.Status);
        Assert.Empty(Directory.EnumerateFiles(fixture.Output));
    }

    [Fact]
    public async Task FirstPublishCompensationDeleteFailureRetainsRecoveryArtifact()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        string archive = Path.Combine(fixture.Output, "aion.db.br");
        var fileSystem = new PublisherFaultingFileSystem
        {
            Fail = (operation, source, destination) =>
                (operation == nameof(IGameDataFileSystem.MoveFile)
                    && destination == Path.Combine(fixture.Output, "manifest.json"))
                || (operation == nameof(IGameDataFileSystem.DeleteFile) && source == archive),
        };

        PublishResult result = await GameDataPublisher.PublishAsync(fixture.Options, fileSystem, default);

        Assert.Equal(PublishStatus.RecoveryRequired, result.Status);
        Assert.NotEmpty(Directory.EnumerateFiles(fixture.Output, "*.previous"));
    }

    [Fact]
    public async Task PublisherSignsTheImmutableSnapshotEvenIfOriginalIsReplacedAfterCapture()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        byte[] captured = await File.ReadAllBytesAsync(fixture.Options.InputPath);
        string replacement = await fixture.CreateDatabaseAsync(3);
        var hooks = new PublisherTestHooks
        {
            SnapshotCaptured = _ =>
            {
                File.Move(replacement, fixture.Options.InputPath, overwrite: true);
                return Task.CompletedTask;
            },
        };

        Assert.Equal(PublishStatus.Published, (await GameDataPublisher.PublishAsync(
            fixture.Options, PhysicalGameDataFileSystem.Instance, default, hooks)).Status);

        await using var archive = File.OpenRead(Path.Combine(fixture.Output, "aion.db.br"));
        await using var brotli = new System.IO.Compression.BrotliStream(archive, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        await brotli.CopyToAsync(output);
        Assert.Equal(captured, output.ToArray());
    }

    [Fact]
    public async Task SnapshotCleanupFailureReturnsCleanupFailedAndRetainsSnapshot()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        string? snapshot = null;
        var hooks = new PublisherTestHooks
        {
            SnapshotCaptured = path =>
            {
                snapshot = path;
                return Task.CompletedTask;
            },
        };
        var fileSystem = new PublisherFaultingFileSystem
        {
            Fail = (operation, source, _) => operation == nameof(IGameDataFileSystem.DeleteFile)
                && source.Contains("namter-publisher-snapshot-", StringComparison.Ordinal),
        };

        PublishResult result = await GameDataPublisher.PublishAsync(fixture.Options, fileSystem, default, hooks);

        Assert.Equal(PublishStatus.CleanupFailed, result.Status);
        Assert.NotNull(snapshot);
        Assert.True(File.Exists(snapshot));
        File.Delete(snapshot);
    }

    [Fact]
    public async Task InvalidPathIsMappedToResultInsteadOfEscaping()
    {
        using var fixture = await PublisherFixture.CreateAsync();

        PublishResult result = await GameDataPublisher.PublishAsync(fixture.Options with { InputPath = "\0" });

        Assert.Equal(PublishStatus.InvalidArguments, result.Status);
    }

    [Fact]
    public void CanonicalPathIdentityRecognizesDotAndParentAliases()
    {
        string alias = Path.Combine(RepositoryRoot, ".", "src", "..", "publisher-output");

        Assert.True(WindowsPathIdentity.IsSameOrDescendant(alias, RepositoryRoot));
    }

    [Fact]
    public void WindowsPathIdentityRecognizesShortPathAliasWhenAvailable()
    {
        if (!OperatingSystem.IsWindows()) return;
        var buffer = new StringBuilder(32768);
        uint length = GetShortPathName(RepositoryRoot, buffer, checked((uint)buffer.Capacity));
        if (length == 0 || length >= buffer.Capacity) return;
        string shortPath = buffer.ToString();
        if (string.Equals(shortPath, RepositoryRoot, StringComparison.OrdinalIgnoreCase)) return;

        Assert.True(WindowsPathIdentity.IsSameOrDescendant(
            Path.Combine(shortPath, "publisher-output"), RepositoryRoot));
    }

    [Fact]
    public async Task PublisherRejectsNonNistP256PrivateKeyWhenPlatformSupportsIt()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        ECDsa? other = null;
        try
        {
            other = ECDsa.Create(ECCurve.CreateFromValue("1.3.132.0.10"));
            if (other.KeySize != 256) return;
            await File.WriteAllTextAsync(fixture.PrivateKeyPath, other.ExportECPrivateKeyPem());

            Assert.Equal(PublishStatus.InvalidPrivateKey, (await fixture.PublishAsync()).Status);
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or CryptographicException)
        {
        }
        finally
        {
            other?.Dispose();
        }
    }

    [Fact]
    public async Task PublisherRejectsOutputDirectorySymbolicLinkWhenPlatformPermitsCreation()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        string target = Path.Combine(Path.GetTempPath(), $"namter-publisher-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(target);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(fixture.Output, target);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            Assert.Equal(PublishStatus.UnsafeFilesystem, (await fixture.PublishAsync()).Status);
        }
        finally
        {
            if (Directory.Exists(fixture.Output)) Directory.Delete(fixture.Output);
            Directory.Delete(target, true);
        }
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

        public async Task<string> CreateDatabaseAsync(ulong version)
        {
            string path = Path.Combine(directory, $"replacement-{version}-{Guid.NewGuid():N}.db");
            await GameDataDatabaseBuilder.BuildAsync(path,
                Path.Combine(RepositoryRoot, "db", "schema", "001_initial.sql"),
                Path.Combine(RepositoryRoot, "db", "seed", "golden_protocol.sql"));
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE metadata SET data_version = {version} WHERE singleton_id = 1;";
            await command.ExecuteNonQueryAsync();
            return path;
        }

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

    private sealed class PublisherFaultingFileSystem : IGameDataFileSystem
    {
        public Func<string, string, string?, bool>? Fail { get; set; }
        public bool FileExists(string path) => PhysicalGameDataFileSystem.Instance.FileExists(path);
        public bool DirectoryExists(string path) => PhysicalGameDataFileSystem.Instance.DirectoryExists(path);
        public FileAttributes GetAttributes(string path) => PhysicalGameDataFileSystem.Instance.GetAttributes(path);
        public void CreateDirectory(string path) => PhysicalGameDataFileSystem.Instance.CreateDirectory(path);
        public void DeleteFile(string path)
        {
            ThrowIf(nameof(DeleteFile), path, null);
            PhysicalGameDataFileSystem.Instance.DeleteFile(path);
        }
        public void MoveFile(string source, string destination, bool overwrite)
        {
            ThrowIf(nameof(MoveFile), source, destination);
            PhysicalGameDataFileSystem.Instance.MoveFile(source, destination, overwrite);
        }
        public void ReplaceFile(string source, string destination, string? backup)
        {
            ThrowIf(nameof(ReplaceFile), source, destination);
            PhysicalGameDataFileSystem.Instance.ReplaceFile(source, destination, backup);
        }
        private void ThrowIf(string operation, string source, string? destination)
        {
            if (Fail?.Invoke(operation, source, destination) == true) throw new IOException($"Injected {operation} failure.");
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint bufferLength);
}
