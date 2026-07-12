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
        var allRecords=new List<EncounterRecord>();var allEvents=new List<CombatEvent>();var allDiagnostics=new List<NativeDiagnostic>();var allReasons=new List<IncompleteReasonRecord>();var hashes=new List<(string File,string Hash)>();
        GameDataSnapshot snapshot=await Load(database,cancellationToken); byte[] nativeSnapshot=ProtocolSnapshotCompiler.Compile(snapshot);
        foreach(string file in files)
        {
            if(new FileInfo(file).Length>MaxReplayFileBytes)throw new InvalidDataException($"Replay file exceeds the {MaxReplayFileBytes}-byte bound: {file}");byte[] pcap=await File.ReadAllBytesAsync(file,cancellationToken); hashes.Add((Path.GetFileName(file),CommandSupport.Sha256(pcap)));
            PipelineResult result=await RunOneAsync(NativeSourceKind.Pcap,pcap,snapshot,nativeSnapshot,$"pcap:{Path.GetFileName(file)}",speed,cancellationToken);
            if(allEvents.Count>MaxArtifactEvents-result.Events.Length)throw new InvalidDataException("Replay event-ledger bound exceeded.");allRecords.AddRange(result.Records);allEvents.AddRange(result.Events);allDiagnostics.AddRange(result.Diagnostics.Take(Math.Max(0,MaxDiagnostics-allDiagnostics.Count)));allReasons.AddRange(result.IncompleteReasons);
        }
        WriteArtifacts(output,allEvents,allRecords,allDiagnostics,MergeReasons(allReasons),"pcap",speed,hashes,snapshot);await log.WriteLineAsync($"Replay complete: {files.Length} file(s), {allEvents.Count} event(s), {allRecords.Count} encounter(s).");return 0;
    }

    internal static async Task<int> CaptureAsync(NativeSourceKind kind,string database,string output,TextWriter log,CancellationToken cancellationToken)
    {
        GameDataSnapshot snapshot=await Load(database,cancellationToken);byte[] nativeSnapshot=ProtocolSnapshotCompiler.Compile(snapshot);PipelineResult result=await RunOneAsync(kind,ReadOnlyMemory<byte>.Empty,snapshot,nativeSnapshot,kind.ToString().ToLowerInvariant(),0,cancellationToken);
        WriteArtifacts(output,result.Events,result.Records,result.Diagnostics,result.IncompleteReasons,kind.ToString().ToLowerInvariant(),0,[],snapshot);await log.WriteLineAsync($"Capture complete: {result.Events.Length} event(s), {result.Records.Length} encounter(s).");return 0;
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
        var consumeTask=ConsumeAsync(channel.Reader,snapshot,captureId,cancellationToken);
        await sourceTask;
        channel.Writer.TryComplete();ConsumeResult consumed=await consumeTask;NativeDiagnostics finalDiagnostics=core.GetDiagnostics();
        lock(diagnosticGate){ImmutableArray<NativeDiagnostic> raw=finalDiagnostics.ManagedDiagnostics;ImmutableArray<NativeDiagnostic> retained=raw.Take(MaxDiagnostics).ToImmutableArray();bool callbackIncomplete=diagnostics.Any(x=>x.Incomplete||x.Code is NativeDiagnosticCode.IncompleteStream or NativeDiagnosticCode.CaptureQueueOverflow or NativeDiagnosticCode.CaptureBackendFailed);var reasons=BuildReasons(raw,finalDiagnostics,Volatile.Read(ref overflow)!=0,callbackIncomplete);return new(consumed.Events,consumed.Records.Select(x=>ApplyReasons(x,reasons)).ToImmutableArray(),retained,reasons);}
    }

    private static async Task<ConsumeResult> ConsumeAsync(ChannelReader<CombatEvent> reader,GameDataSnapshot snapshot,string captureId,CancellationToken cancellationToken)
    {
        var events=new List<CombatEvent>();var records=new List<EncounterRecord>();EncounterReducer reducer=NewReducer(snapshot,captureId,0);long lastMs=0;int index=0;
        await foreach(CombatEvent value in reader.ReadAllAsync(cancellationToken)){if(events.Count>=MaxArtifactEvents)throw new InvalidDataException("Event-ledger bound exceeded.");events.Add(value);lastMs=checked((long)(value.Provenance.LastTimestampNs/1_000_000));EncounterUpdate update=reducer.Apply(value);if(update.FinalRecord is not null){records.Add(update.FinalRecord);reducer=NewReducer(snapshot,captureId,++index);}}
        EncounterUpdate final=reducer.CompleteInput(lastMs);if(final.FinalRecord is not null)records.Add(final.FinalRecord);return new(events.ToImmutableArray(),records.ToImmutableArray());
    }
    private static EncounterReducer NewReducer(GameDataSnapshot snapshot,string captureId,int index)=>new(snapshot,new EncounterReducerOptions(30_000,CommandSupport.StableGuid($"{captureId}\0{index}"),typeof(PipelineRunner).Assembly.GetName().Version?.ToString()??"0",1,"native",captureId));
    internal static EncounterRecord ApplyReasons(EncounterRecord r,ImmutableArray<IncompleteReasonRecord> reasons){if(reasons.IsEmpty)return r;var p=r.Provenance;return r with{IsComplete=false,Provenance=p with{IsComplete=false,IncompleteReasons=MergeReasons(p.IncompleteReasons.Concat(reasons))}};}
    private static ImmutableArray<IncompleteReasonRecord> BuildReasons(ImmutableArray<NativeDiagnostic> diagnostics,NativeDiagnostics final,bool channelOverflow,bool callbackIncomplete){var values=new List<IncompleteReasonRecord>();foreach(var group in diagnostics.Where(x=>x.Incomplete||x.Code is NativeDiagnosticCode.IncompleteStream or NativeDiagnosticCode.CaptureQueueOverflow or NativeDiagnosticCode.CaptureBackendFailed).GroupBy(x=>(x.Code,x.Message)))values.Add(new(IncompleteReasonCode.ExternalIncomplete,$"native:{group.Key.Code}:{group.Key.Message}",checked((ulong)group.Count())));if(final.SuppressedManagedDiagnosticCount!=0)values.Add(new(IncompleteReasonCode.ExternalIncomplete,"managed:diagnostic-retention-overflow",final.SuppressedManagedDiagnosticCount));if(callbackIncomplete&&!values.Any())values.Add(new(IncompleteReasonCode.ExternalIncomplete,"managed:callback-reported-incomplete",1));if(final.DroppedCaptureCount!=0)values.Add(new(IncompleteReasonCode.ExternalIncomplete,"native:dropped-capture-records",final.DroppedCaptureCount));if(final.BackendDropped!=0)values.Add(new(IncompleteReasonCode.ExternalIncomplete,"backend:dropped-records",final.BackendDropped));if(final.BackendInterfaceDropped!=0)values.Add(new(IncompleteReasonCode.ExternalIncomplete,"backend:interface-dropped-records",final.BackendInterfaceDropped));if(final.Incomplete&&!values.Any())values.Add(new(IncompleteReasonCode.ExternalIncomplete,"native:incomplete-without-detail",1));if(channelOverflow)values.Add(new(IncompleteReasonCode.ExternalIncomplete,"managed:event-channel-overflow",1));return MergeReasons(values);}
    private static ImmutableArray<IncompleteReasonRecord> MergeReasons(IEnumerable<IncompleteReasonRecord> values)=>values.GroupBy(x=>(x.Code,x.Message)).OrderBy(x=>x.Key.Code).ThenBy(x=>x.Key.Message,StringComparer.Ordinal).Select(x=>new IncompleteReasonRecord(x.Key.Code,x.Key.Message,x.Aggregate(0UL,(sum,item)=>checked(sum+item.Count)))).ToImmutableArray();

    private static void WriteArtifacts(string output,IReadOnlyList<CombatEvent> events,IReadOnlyList<EncounterRecord> records,IReadOnlyList<NativeDiagnostic> diagnostics,ImmutableArray<IncompleteReasonRecord> incompleteReasons,string backend,int speed,IReadOnlyList<(string File,string Hash)> inputs,GameDataSnapshot snapshot)
    {
        byte[] ledger=WriteEventLedger(events);var files=new Dictionary<string,byte[]>(StringComparer.Ordinal){{"event-ledger.json",ledger},{"diagnostics.json",JsonSerializer.SerializeToUtf8Bytes(diagnostics,JsonOptions)}};var recordHashes=new List<string>();for(int i=0;i<records.Count;i++){byte[] bytes=EncounterRecordWriter.Write(records[i]);recordHashes.Add(CommandSupport.Sha256(bytes));files.Add($"encounters/encounter-{i:D4}.json",bytes);}
        byte[] metadata=CommandSupport.Json(w=>{w.WriteStartObject();w.WriteString("format","namter-replay-artifacts-v1");w.WriteString("backend",backend);w.WriteNumber("speed",speed);w.WriteNumber("dataVersion",snapshot.DataVersion);w.WriteNumber("schemaVersion",snapshot.SchemaVersion);w.WriteNumber("protocolProfileVersion",snapshot.ProtocolProfileVersion);w.WriteNumber("eventCount",events.Count);w.WriteNumber("encounterCount",records.Count);w.WriteBoolean("isComplete",incompleteReasons.IsEmpty);w.WriteStartArray("incompleteReasons");foreach(var reason in incompleteReasons){w.WriteStartObject();w.WriteString("code",reason.Code.ToString());w.WriteString("message",reason.Message);w.WriteNumber("count",reason.Count);w.WriteEndObject();}w.WriteEndArray();w.WriteString("eventLedgerSha256",CommandSupport.Sha256(ledger));w.WriteStartArray("inputs");foreach(var input in inputs){w.WriteStartObject();w.WriteString("file",input.File);w.WriteString("sha256",input.Hash);w.WriteEndObject();}w.WriteEndArray();w.WriteStartArray("encounterSha256");foreach(string h in recordHashes)w.WriteStringValue(h);w.WriteEndArray();w.WriteEndObject();});files.Add("metadata.json",metadata);ArtifactSetPublisher.Publish(output,files);
    }
    private static byte[] WriteEventLedger(IReadOnlyList<CombatEvent> events)=>CommandSupport.Json(w=>{w.WriteStartObject();w.WriteString("format","namter-event-ledger-v1");w.WriteStartArray("events");foreach(CombatEvent e in events){w.WriteStartObject();w.WriteString("kind",e.GetType().Name);w.WriteNumber("firstTimestampNs",e.Provenance.FirstTimestampNs);w.WriteNumber("lastTimestampNs",e.Provenance.LastTimestampNs);w.WriteNumber("epoch",e.Provenance.Epoch);w.WriteNumber("firstFileOffset",e.Provenance.FirstFileOffset);w.WriteNumber("lastFileOffset",e.Provenance.LastFileOffset);w.WritePropertyName("value");JsonSerializer.Serialize(w,e,e.GetType(),JsonOptions);w.WriteEndObject();}w.WriteEndArray();w.WriteEndObject();});
    private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=false};
    private sealed record PipelineResult(ImmutableArray<CombatEvent> Events,ImmutableArray<EncounterRecord> Records,ImmutableArray<NativeDiagnostic> Diagnostics,ImmutableArray<IncompleteReasonRecord> IncompleteReasons);
    private sealed record ConsumeResult(ImmutableArray<CombatEvent> Events,ImmutableArray<EncounterRecord> Records);
}
