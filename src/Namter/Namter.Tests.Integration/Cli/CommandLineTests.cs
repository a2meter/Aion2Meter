using Namter.Cli;
using Namter.GameData.Builder;
using Namter.Cli.Commands;
using Namter.Encounter;
using System.Collections.Immutable;

namespace Namter.Tests.Integration.Cli;

public sealed class CommandLineTests
{
    [Fact]
    public async Task Help_is_stable_and_successful()
    {
        using var output = new StringWriter();
        int exit = await CliApplication.RunAsync(["--help"], output, output);
        Assert.Equal(0, exit);
        Assert.Contains("namter replay --input", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("replay", "--input", "x", "--data", "x", "--output", "x", "--speed", "2")]
    [InlineData("capture", "--backend", "auto", "--data", "x", "--output", "x")]
    [InlineData("compare", "--actual", "x", "--expected", "x")]
    public async Task Invalid_commands_have_usage_exit(params string[] args)
    {
        int exit = await CliApplication.RunAsync(args, TextWriter.Null, TextWriter.Null);
        Assert.Equal((int)CliExitCode.Usage, exit);
    }

    [Fact]
    public void Deployment_environment_only_overrides_remote_values()
    {
        var environment = new Dictionary<string, string?>
        {
            ["NAMTER_GAMEDATA_MANIFEST_URI"] = "https://localhost/manifest.json",
            ["NAMTER_GAMEDATA_PUBLIC_KEY_SPKI"] = "AQID",
            ["NAMTER_MANAGED_QUEUE_CAPACITY"] = "999999",
        };
        CliConfiguration config = CliConfiguration.Load(environment.GetValueOrDefault);
        Assert.Equal(new Uri("https://localhost/manifest.json"), config.GameDataManifestUri);
        Assert.Equal("AQID", config.GameDataPublicKeySpki);
        Assert.Equal(1024, config.ManagedQueueCapacity);
    }

    [Fact]
    public void Replay_speed_changes_only_due_time_from_capture_timestamps()
    {
        const ulong first = 10_000_000_000;
        Assert.Equal(TimeSpan.Zero, ReplaySchedulingPolicy.DueTime(first, first + 5_000_000_000, 0));
        Assert.Equal(TimeSpan.FromSeconds(5), ReplaySchedulingPolicy.DueTime(first, first + 5_000_000_000, 1));
        Assert.Equal(TimeSpan.FromMilliseconds(500), ReplaySchedulingPolicy.DueTime(first, first + 5_000_000_000, 10));
    }

    [Fact]
    public async Task Bootstrap_seed_compiles_native_accepts_and_replay_writes_deterministic_atomic_artifacts()
    {
        string root = Path.Combine(Path.GetTempPath(), "namter-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string repository = FindRepositoryRoot();
            string database = Path.Combine(root, "aion.db");
            await GameDataDatabaseBuilder.BuildAsync(database, Path.Combine(repository, "db", "schema", "001_initial.sql"), Path.Combine(repository, "db", "seed", "golden_protocol.sql"));
            string pcap = Path.Combine(root, "empty.pcap");
            await File.WriteAllBytesAsync(pcap,
            [
                0xd4, 0xc3, 0xb2, 0xa1, 0x02, 0x00, 0x04, 0x00,
                0, 0, 0, 0, 0, 0, 0, 0, 0xff, 0xff, 0, 0,
                0x65, 0, 0, 0,
            ]);
            string first = Path.Combine(root, "first"), second = Path.Combine(root, "second");
            using var errors = new StringWriter();
            int firstExit = await CliApplication.RunAsync(["replay","--input",pcap,"--data",database,"--output",first,"--speed","0"], TextWriter.Null, errors);
            Assert.True(firstExit == 0, errors.ToString());
            Assert.Equal(0, await CliApplication.RunAsync(["replay","--input",pcap,"--data",database,"--output",second,"--speed","0"], TextWriter.Null, TextWriter.Null));
            foreach (string name in new[] { "event-ledger.json", "diagnostics.json", "metadata.json" })
                Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(first,name)), await File.ReadAllBytesAsync(Path.Combine(second,name)));
            Assert.Empty(Directory.GetFiles(first, "*.tmp", SearchOption.AllDirectories));
            string dataDir=Path.Combine(root,"data");Directory.CreateDirectory(Path.Combine(dataDir,"backup"));File.Copy(database,Path.Combine(dataDir,"aion.db"));File.Copy(database,Path.Combine(dataDir,"backup","aion.previous.db"));
            Assert.Equal(0,await CliApplication.RunAsync(["data","status","--data-dir",dataDir],TextWriter.Null,TextWriter.Null));
            Assert.Equal(0,await CliApplication.RunAsync(["data","check","--data-dir",dataDir],TextWriter.Null,TextWriter.Null));
            Assert.Equal(0,await CliApplication.RunAsync(["data","rollback","--data-dir",dataDir],TextWriter.Null,TextWriter.Null));
            string activeDb=Path.Combine(dataDir,"aion.db"),operationBackup=Path.Combine(dataDir,".update","aion.operation-backup.db");File.Copy(activeDb,operationBackup,true);File.Delete(activeDb);Assert.Equal(0,await CliApplication.RunAsync(["data","check","--data-dir",dataDir],TextWriter.Null,TextWriter.Null));Assert.True(File.Exists(activeDb));
            string truncated=Path.Combine(root,"truncated.pcap");await File.WriteAllBytesAsync(truncated,[..await File.ReadAllBytesAsync(pcap),1]);string incomplete=Path.Combine(root,"incomplete");Assert.Equal(0,await CliApplication.RunAsync(["replay","--input",truncated,"--data",database,"--output",incomplete,"--speed","0"],TextWriter.Null,TextWriter.Null));string incompleteMetadata=await File.ReadAllTextAsync(Path.Combine(incomplete,"metadata.json"));Assert.Contains("\"isComplete\":false",incompleteMetadata,StringComparison.Ordinal);Assert.Contains("native:IncompleteStream",incompleteMetadata,StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Absent_npcap_guidance_is_dedicated_manual_and_non_automatic()
    {
        using var error = new StringWriter();
        int exit = await CaptureCommand.WriteNpcapNotInstalledAsync(error);
        Assert.Equal((int)CliExitCode.NpcapNotInstalled, exit);
        string text = error.ToString();
        Assert.Contains("NotInstalled", text, StringComparison.Ordinal);
        Assert.Contains("https://npcap.com/#download", text, StringComparison.Ordinal);
        Assert.Contains("retry", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No installer, browser, download, HTTP request, or backend fallback was started", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_rejects_invalid_database_with_data_exit_code()
    {
        string root=Path.Combine(Path.GetTempPath(),"namter-invalid-db-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{string db=Path.Combine(root,"aion.db"),pcap=Path.Combine(root,"empty.pcap");await File.WriteAllTextAsync(db,"not sqlite");await File.WriteAllBytesAsync(pcap,[0xd4,0xc3,0xb2,0xa1,2,0,4,0,0,0,0,0,0,0,0,0,0xff,0xff,0,0,0x65,0,0,0]);int exit=await CliApplication.RunAsync(["replay","--input",pcap,"--data",db,"--output",Path.Combine(root,"out")],TextWriter.Null,TextWriter.Null);Assert.Equal((int)CliExitCode.InvalidData,exit);}finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void Artifact_transaction_removes_stale_files_rolls_back_failure_and_rejects_unrelated_content()
    {
        string root=Path.Combine(Path.GetTempPath(),"namter-artifacts-"+Guid.NewGuid().ToString("N"));
        try{Directory.CreateDirectory(root);ArtifactSetPublisher.Publish(root,new Dictionary<string,byte[]>{{"metadata.json",[1]},{"encounters/encounter-0001.json",[2]}});ArtifactSetPublisher.Publish(root,new Dictionary<string,byte[]>{{"metadata.json",[3]}});Assert.False(File.Exists(Path.Combine(root,"encounters","encounter-0001.json")));byte[] before=File.ReadAllBytes(Path.Combine(root,"metadata.json"));Assert.Throws<IOException>(()=>ArtifactSetPublisher.Publish(root,new Dictionary<string,byte[]>{{"metadata.json",[4]}},()=>throw new IOException("injected")));Assert.Equal(before,File.ReadAllBytes(Path.Combine(root,"metadata.json")));Directory.Delete(root,true);Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"user.txt"),"keep");Assert.Throws<InvalidDataException>(()=>ArtifactSetPublisher.Publish(root,new Dictionary<string,byte[]>{{"metadata.json",[5]}}));Assert.Equal("keep",File.ReadAllText(Path.Combine(root,"user.txt")));}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [Fact]
    public void Incomplete_reasons_mark_final_record_and_preserve_deterministic_counts()
    {
        var provenance=new DataProvenance("1",1,1,1,1,"p","pcap","c",true,ImmutableArray<IncompleteReasonRecord>.Empty);var record=new EncounterRecord(Guid.Empty,new EncounterIdentity(1,2,3,4,"boss",1,2),1,2,true,EncounterCompletionReason.EndOfInput,[],[],[],[],[],provenance);ImmutableArray<IncompleteReasonRecord> reasons=[new(IncompleteReasonCode.ExternalIncomplete,"managed:event-channel-overflow",2)];EncounterRecord changed=PipelineRunner.ApplyReasons(record,reasons);Assert.False(changed.IsComplete);Assert.False(changed.Provenance.IsComplete);Assert.Equal(2UL,Assert.Single(changed.Provenance.IncompleteReasons).Count);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) { if (File.Exists(Path.Combine(current.FullName, "Namter.slnx"))) return current.FullName; current = current.Parent; }
        throw new DirectoryNotFoundException("Namter repository root was not found.");
    }
}
