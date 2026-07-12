using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Channels;
using Namter.Core.Interop;
using Namter.Encounter;
using Namter.GameData;

namespace Namter.Cli.Commands;

internal static class PipelineRunner
{
    private const long MaxReplayFileBytes=512L*1024*1024;
    private const int MaxArtifactEvents=1_000_000;
    private const int MaxDiagnostics=10_000;
    internal static async Task<int> ReplayAsync(string[] files,string database,string output,int speed,TextWriter log,CancellationToken cancellationToken)
    {
        var allRecords=new List<EncounterRecord>();var allEvents=new List<CombatEvent>();var allDiagnostics=new List<NativeDiagnostic>();var hashes=new List<(string File,string Hash)>();
        GameDataSnapshot snapshot=await Load(database,cancellationToken); byte[] nativeSnapshot=ProtocolSnapshotCompiler.Compile(snapshot);
        foreach(string file in files)
        {
            if(new FileInfo(file).Length>MaxReplayFileBytes)throw new InvalidDataException($"Replay file exceeds the {MaxReplayFileBytes}-byte bound: {file}");byte[] pcap=await File.ReadAllBytesAsync(file,cancellationToken); hashes.Add((Path.GetFileName(file),CommandSupport.Sha256(pcap)));
            PipelineResult result=await RunOneAsync(NativeSourceKind.Pcap,pcap,snapshot,nativeSnapshot,$"pcap:{Path.GetFileName(file)}",speed,cancellationToken);
            if(allEvents.Count>MaxArtifactEvents-result.Events.Length)throw new InvalidDataException("Replay event-ledger bound exceeded.");allRecords.AddRange(result.Records);allEvents.AddRange(result.Events);allDiagnostics.AddRange(result.Diagnostics.Take(Math.Max(0,MaxDiagnostics-allDiagnostics.Count)));
        }
        WriteArtifacts(output,allEvents,allRecords,allDiagnostics,"pcap",speed,hashes,snapshot);await log.WriteLineAsync($"Replay complete: {files.Length} file(s), {allEvents.Count} event(s), {allRecords.Count} encounter(s).");return 0;
    }

    internal static async Task<int> CaptureAsync(NativeSourceKind kind,string database,string output,TextWriter log,CancellationToken cancellationToken)
    {
        GameDataSnapshot snapshot=await Load(database,cancellationToken);byte[] nativeSnapshot=ProtocolSnapshotCompiler.Compile(snapshot);PipelineResult result=await RunOneAsync(kind,ReadOnlyMemory<byte>.Empty,snapshot,nativeSnapshot,kind.ToString().ToLowerInvariant(),0,cancellationToken);
        WriteArtifacts(output,result.Events,result.Records,result.Diagnostics,kind.ToString().ToLowerInvariant(),0,[],snapshot);await log.WriteLineAsync($"Capture complete: {result.Events.Length} event(s), {result.Records.Length} encounter(s).");return 0;
    }

    private static async Task<GameDataSnapshot> Load(string database,CancellationToken ct)=>await new GameDataRepository(database,GameDataCacheLimits.Default).LoadAsync(ct);

    private static async Task<PipelineResult> RunOneAsync(NativeSourceKind kind,ReadOnlyMemory<byte> source,GameDataSnapshot snapshot,byte[] nativeSnapshot,string captureId,int speed,CancellationToken cancellationToken)
    {
        CliConfiguration config=CliConfiguration.Load();var channel=Channel.CreateBounded<CombatEvent>(new BoundedChannelOptions(config.ManagedQueueCapacity){SingleReader=true,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait});var diagnostics=new List<NativeDiagnostic>();object diagnosticGate=new();int overflow=0;
        var pacer=new ReplayPacer(speed);
        void OnEvent(NativeEvent value){if(value.Kind==NativeEventKind.SourceStarted)return;if(!pacer.Wait(value.FirstTimestampNs,cancellationToken))return;CombatEvent mapped=CombatEventMapper.Map(value);if(!channel.Writer.TryWrite(mapped))Interlocked.Exchange(ref overflow,1);}
        void OnDiagnostic(NativeDiagnostic value){lock(diagnosticGate){if(diagnostics.Count<MaxDiagnostics)diagnostics.Add(value);}}
        var nativeConfig=new NativeCoreConfig(config.NativeQueueCapacity,config.MaxLiveFlows,config.MaxOutOfOrderBytesPerFlow,config.MaxFrameBytes,config.MaxDecompressedBytes);
        await using var core=new NativeCore(OnEvent,OnDiagnostic,nativeConfig);core.SetProtocolSnapshot(nativeSnapshot);
        using var sourceCts=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);Task sourceTask=kind==NativeSourceKind.Pcap?core.ReplayAsync(source,sourceCts.Token):core.CaptureAsync(kind,source,sourceCts.Token);
        var consumeTask=ConsumeAsync(channel.Reader,snapshot,captureId,()=>Volatile.Read(ref overflow)!=0,cancellationToken);
        await sourceTask;
        channel.Writer.TryComplete();ConsumeResult consumed=await consumeTask;NativeDiagnostics finalDiagnostics=core.GetDiagnostics();
        if(finalDiagnostics.Incomplete||Volatile.Read(ref overflow)!=0||finalDiagnostics.DroppedCaptureCount!=0)consumed=consumed.MarkIncomplete("capture pipeline reported dropped or incomplete input");
        lock(diagnosticGate)return new(consumed.Events,consumed.Records,diagnostics.Concat(finalDiagnostics.ManagedDiagnostics).Distinct().ToImmutableArray());
    }

    private static async Task<ConsumeResult> ConsumeAsync(ChannelReader<CombatEvent> reader,GameDataSnapshot snapshot,string captureId,Func<bool> overflow,CancellationToken cancellationToken)
    {
        var events=new List<CombatEvent>();var records=new List<EncounterRecord>();EncounterReducer reducer=NewReducer(snapshot,captureId,0);long lastMs=0;int index=0;
        await foreach(CombatEvent value in reader.ReadAllAsync(cancellationToken)){if(events.Count>=MaxArtifactEvents)throw new InvalidDataException("Event-ledger bound exceeded.");events.Add(value);lastMs=checked((long)(value.Provenance.LastTimestampNs/1_000_000));EncounterUpdate update=reducer.Apply(value);if(update.FinalRecord is not null){EncounterRecord record=update.FinalRecord;if(overflow())record=WithIncomplete(record,"managed event channel overflowed");records.Add(record);reducer=NewReducer(snapshot,captureId,++index);}}
        EncounterUpdate final=reducer.CompleteInput(lastMs);if(final.FinalRecord is not null){EncounterRecord record=final.FinalRecord;if(overflow())record=WithIncomplete(record,"managed event channel overflowed");records.Add(record);}return new(events.ToImmutableArray(),records.ToImmutableArray());
    }
    private static EncounterReducer NewReducer(GameDataSnapshot snapshot,string captureId,int index)=>new(snapshot,new EncounterReducerOptions(30_000,CommandSupport.StableGuid($"{captureId}\0{index}"),typeof(PipelineRunner).Assembly.GetName().Version?.ToString()??"0",1,"native",captureId));
    private static EncounterRecord WithIncomplete(EncounterRecord r,string reason){var p=r.Provenance;var reasons=p.IncompleteReasons.Add(new(IncompleteReasonCode.ExternalIncomplete,reason,1));return r with{IsComplete=false,Provenance=p with{IsComplete=false,IncompleteReasons=reasons}};}

    private static void WriteArtifacts(string output,IReadOnlyList<CombatEvent> events,IReadOnlyList<EncounterRecord> records,IReadOnlyList<NativeDiagnostic> diagnostics,string backend,int speed,IReadOnlyList<(string File,string Hash)> inputs,GameDataSnapshot snapshot)
    {
        byte[] ledger=WriteEventLedger(events);CommandSupport.AtomicWrite(Path.Combine(output,"event-ledger.json"),ledger);string encountersDir=Path.Combine(output,"encounters");Directory.CreateDirectory(encountersDir);var recordHashes=new List<string>();for(int i=0;i<records.Count;i++){byte[] bytes=EncounterRecordWriter.Write(records[i]);recordHashes.Add(CommandSupport.Sha256(bytes));CommandSupport.AtomicWrite(Path.Combine(encountersDir,$"encounter-{i:D4}.json"),bytes);}byte[] diagnosticBytes=JsonSerializer.SerializeToUtf8Bytes(diagnostics,JsonOptions);CommandSupport.AtomicWrite(Path.Combine(output,"diagnostics.json"),diagnosticBytes);
        byte[] metadata=CommandSupport.Json(w=>{w.WriteStartObject();w.WriteString("format","namter-replay-artifacts-v1");w.WriteString("backend",backend);w.WriteNumber("speed",speed);w.WriteNumber("dataVersion",snapshot.DataVersion);w.WriteNumber("schemaVersion",snapshot.SchemaVersion);w.WriteNumber("protocolProfileVersion",snapshot.ProtocolProfileVersion);w.WriteNumber("eventCount",events.Count);w.WriteNumber("encounterCount",records.Count);w.WriteString("eventLedgerSha256",CommandSupport.Sha256(ledger));w.WriteStartArray("inputs");foreach(var input in inputs){w.WriteStartObject();w.WriteString("file",input.File);w.WriteString("sha256",input.Hash);w.WriteEndObject();}w.WriteEndArray();w.WriteStartArray("encounterSha256");foreach(string h in recordHashes)w.WriteStringValue(h);w.WriteEndArray();w.WriteEndObject();});CommandSupport.AtomicWrite(Path.Combine(output,"metadata.json"),metadata);
    }
    private static byte[] WriteEventLedger(IReadOnlyList<CombatEvent> events)=>CommandSupport.Json(w=>{w.WriteStartObject();w.WriteString("format","namter-event-ledger-v1");w.WriteStartArray("events");foreach(CombatEvent e in events){w.WriteStartObject();w.WriteString("kind",e.GetType().Name);w.WriteNumber("firstTimestampNs",e.Provenance.FirstTimestampNs);w.WriteNumber("lastTimestampNs",e.Provenance.LastTimestampNs);w.WriteNumber("epoch",e.Provenance.Epoch);w.WriteNumber("firstFileOffset",e.Provenance.FirstFileOffset);w.WriteNumber("lastFileOffset",e.Provenance.LastFileOffset);w.WritePropertyName("value");JsonSerializer.Serialize(w,e,e.GetType(),JsonOptions);w.WriteEndObject();}w.WriteEndArray();w.WriteEndObject();});
    private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=false};
    private sealed record PipelineResult(ImmutableArray<CombatEvent> Events,ImmutableArray<EncounterRecord> Records,ImmutableArray<NativeDiagnostic> Diagnostics);
    private sealed record ConsumeResult(ImmutableArray<CombatEvent> Events,ImmutableArray<EncounterRecord> Records){internal ConsumeResult MarkIncomplete(string reason)=>this with{Records=Records.Select(x=>WithIncomplete(x,reason)).ToImmutableArray()};}
}
