using System.Collections.Immutable;
using System.Threading.Channels;
using Namter.Core.Interop;
using Namter.Encounter;
using Namter.GameData;

namespace Namter.Overlay;

/// Drives a live WinDivert/Npcap capture (or an offline PCAP replay) through a
/// single <see cref="EncounterReducer"/> and publishes an immutable
/// <see cref="MeterView"/> for the UI to render.
///
/// Threading contract: the native backend raises events on its own pump thread,
/// which only writes mapped events into a channel. Exactly one consumer thread
/// owns the reducer (which is intentionally unsynchronized) and publishes an
/// immutable view via <see cref="Volatile"/>. The UI thread only ever reads that
/// immutable reference, so no locking crosses the boundary.
internal sealed class LiveMeterEngine : IAsyncDisposable
{
    private const long PublishIntervalMs = 150;

    private readonly string _database;
    private readonly NativeSourceKind _kind;
    private readonly ReadOnlyMemory<byte> _replay;
    private readonly CancellationTokenSource _cts = new();

    private MeterView _latest = MeterView.Empty;
    private Task? _run;

    private readonly string? _packetLog;

    public LiveMeterEngine(string database, NativeSourceKind kind, ReadOnlyMemory<byte> replay = default, string? packetLogDirectory = null)
    {
        _database = database;
        _kind = kind;
        _replay = replay;
        _packetLog = packetLogDirectory;
    }

    /// Latest immutable view. Safe to read from any thread.
    public MeterView Latest => Volatile.Read(ref _latest);

    /// Non-null once the pipeline has terminated with an unexpected error.
    public string? FatalError { get; private set; }

    /// True once the source has finished on its own (a replay reaching EOF).
    public bool Finished { get; private set; }

    /// Completes when the pipeline task ends (used by the headless self-test).
    public Task Completion => _run ?? Task.CompletedTask;

    public void Start() => _run ??= Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            GameDataSnapshot snapshot = await new GameDataRepository(_database, GameDataCacheLimits.Default)
                .LoadAsync(ct).ConfigureAwait(false);
            byte[] nativeSnapshot = ProtocolSnapshotCompiler.Compile(snapshot);

            var channel = Channel.CreateBounded<CombatEvent>(new BoundedChannelOptions(4096)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });

            void OnEvent(NativeEvent value)
            {
                if (value.Kind == NativeEventKind.SourceStarted) return;
                CombatEvent mapped = CombatEventMapper.Map(value);
                if (mapped is ActorObservedEvent actor &&
                    snapshot.JobAliases.TryGetValue(actor.JobId, out ushort canonicalJob))
                    mapped = actor with { JobId = canonicalJob };
                if (channel.Writer.TryWrite(mapped)) return;
                try { channel.Writer.WriteAsync(mapped, ct).AsTask().GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { }
                catch (ChannelClosedException) { }
            }

            static void OnDiagnostic(NativeDiagnostic _) { }

            await using var core = new NativeCore(OnEvent, OnDiagnostic, new NativeCoreConfig());
            core.SetProtocolSnapshot(nativeSnapshot);
            if (_packetLog is not null)
            {
                Directory.CreateDirectory(_packetLog);
                core.SetPacketLog(_packetLog);
            }

            string backend = _replay.IsEmpty ? _kind.ToString().ToLowerInvariant() : "pcap";
            Task consume = ConsumeAsync(channel.Reader, snapshot, backend, ct);
            Task source = _replay.IsEmpty
                ? core.CaptureAsync(_kind, ReadOnlyMemory<byte>.Empty, ct)
                : core.ReplayAsync(_replay, ct);

            try { await source.ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            finally { channel.Writer.TryComplete(); }
            await consume.ConfigureAwait(false);
            Finished = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { FatalError = ex.Message; }
    }

    private async Task ConsumeAsync(ChannelReader<CombatEvent> reader, GameDataSnapshot snapshot, string backend, CancellationToken ct)
    {
        var identities = new Dictionary<uint, ActorObservedEvent>();
        string captureId = $"overlay:{backend}";
        int index = 0;
        EncounterReducer reducer = NewReducer(snapshot, captureId, index);
        long lastMs = 0, lastPublish = 0;
        bool sawDamage = false;

        EncounterReducer Reseed()
        {
            EncounterReducer next = NewReducer(snapshot, captureId, ++index);
            foreach (ActorObservedEvent seed in identities.Values.OrderBy(x => x.ActorId))
                next.Apply(seed);
            return next;
        }

        try
        {
            await foreach (CombatEvent value in reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                lastMs = (long)(value.Provenance.LastTimestampNs / 1_000_000UL);
                if (value is ActorObservedEvent identity) identities[identity.ActorId] = identity;

                EncounterUpdate update = reducer.Apply(value);
                if (update.FinalRecord is not null)
                {
                    Publish(BuildView(update.FinalRecord.Encounter, update.FinalRecord.StartTimestampMs,
                        update.FinalRecord.EndTimestampMs, update.FinalRecord.Participants, live: false));
                    reducer = Reseed();
                    lastPublish = 0;
                    sawDamage = false;
                    continue;
                }

                long now = Environment.TickCount64;
                if (now - lastPublish < PublishIntervalMs) continue;
                lastPublish = now;
                if (reducer.Current is not EncounterSnapshot snap) continue;

                // Wipe / re-pull: the boss HP returns to full after damage was dealt.
                // Treat that as a fresh pull and reset the meter.
                if (snap.Encounter.MaxHp is ulong maxHp && maxHp > 0 && snap.Encounter.LastHp is ulong curHp)
                {
                    if (curHp < maxHp) sawDamage = sawDamage || HasDamage(snap.Participants);
                    else if (sawDamage)
                    {
                        reducer = Reseed();
                        sawDamage = false;
                        Publish(MeterView.Empty);
                        continue;
                    }
                }

                Publish(BuildView(snap.Encounter, snap.StartTimestampMs, snap.LastTimestampMs, snap.Participants, live: true));
            }

            EncounterUpdate final = reducer.CompleteInput(lastMs);
            if (final.FinalRecord is not null)
                Publish(BuildView(final.FinalRecord.Encounter, final.FinalRecord.StartTimestampMs,
                    final.FinalRecord.EndTimestampMs, final.FinalRecord.Participants, live: false));
        }
        catch (OperationCanceledException) { }
    }

    private void Publish(MeterView view) => Volatile.Write(ref _latest, view);

    private static bool HasDamage(ImmutableArray<ParticipantRecord> participants)
    {
        foreach (ParticipantRecord p in participants)
            if (p.Damage + p.DotDamage > 0) return true;
        return false;
    }

    private static MeterView BuildView(EncounterIdentity id, long startMs, long lastMs, ImmutableArray<ParticipantRecord> participants, bool live)
    {
        long elapsed = Math.Max(0, lastMs - startMs);
        double seconds = Math.Max(1.0, elapsed / 1000.0);
        ulong maxHp = id.MaxHp ?? 0;

        var rows = ImmutableArray.CreateBuilder<MeterRow>(participants.Length);
        foreach (ParticipantRecord p in participants)
        {
            ulong damage = p.Damage + p.DotDamage;
            double dps = damage / seconds;
            double share = maxHp > 0 ? damage / (double)maxHp : 0.0;
            rows.Add(new MeterRow(p.ActorId, p.Name, p.JobId, p.IsSelf, damage, dps, share));
        }
        rows.Sort(static (a, b) => b.Damage.CompareTo(a.Damage));
        return new MeterView(id.Name, id.LastHp, id.MaxHp, elapsed, live, rows.ToImmutable());
    }

    private static EncounterReducer NewReducer(GameDataSnapshot snapshot, string captureId, int index) =>
        new(snapshot, new EncounterReducerOptions(
            IdleTimeoutMs: 30_000,
            RecordId: Guid.NewGuid(),
            AppVersion: typeof(LiveMeterEngine).Assembly.GetName().Version?.ToString() ?? "0",
            AbiVersion: 1,
            Backend: "namter-overlay",
            CaptureId: captureId,
            RequireCombatStart: true,
            PreserveBuffObservations: true,
            CarryInitialBuffState: index == 0));

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_run is not null)
        {
            try { await _run.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
    }
}
