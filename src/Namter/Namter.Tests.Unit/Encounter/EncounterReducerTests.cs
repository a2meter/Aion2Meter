using System.Collections.Frozen;
using System.Collections.Immutable;
using Namter.Encounter;
using Namter.GameData;

namespace Namter.Tests.Unit.Encounter;

public sealed class EncounterReducerTests
{
    [Fact]
    public void AuthoritativeBossDamageStartsAndSeparatesTotalsWithSummonAttribution()
    {
        EncounterReducer reducer = CreateReducer();
        Assert.Equal(EncounterState.Idle, reducer.State);

        reducer.Apply(Mob(1_000, actor: 900, owner: 0, mob: 42, boss: 42, hp: 1000, max: 1000));
        reducer.Apply(Actor(1_001, 10, "Player"));
        reducer.Apply(Mob(1_002, actor: 11, owner: 10, mob: 777));
        EncounterUpdate started = reducer.Apply(Damage(1_003, 11, 900, damage: 100, multi: 20, healing: 5, mask: 0x81));
        reducer.Apply(Damage(1_004, 10, 900, damage: 7, multi: 3, healing: 2, dot: true));

        Assert.Equal(EncounterState.Active, started.State);
        EncounterSnapshot snapshot = Assert.IsType<EncounterSnapshot>(reducer.Current);
        ParticipantRecord participant = Assert.Single(snapshot.Participants);
        Assert.Equal((uint)10, participant.ActorId);
        Assert.Equal("Player", participant.Name);
        Assert.Equal((100UL, 23UL, 7UL, 7UL),
            (participant.Damage, participant.MultiDamage, participant.DotDamage, participant.Healing));
        Assert.Equal(2, snapshot.Events.Length);
        Assert.Equal(0x81U, snapshot.Events[0].SpecialMask);
        Assert.Equal((uint)11, snapshot.Events[0].SourceActorId);
        Assert.Equal((uint)10, snapshot.Events[0].AttributedActorId);
    }

    [Fact]
    public void BossHpEnrichesIdentityAndDeathFinalizesWithoutLaterMutation()
    {
        EncounterReducer reducer = CreateReducer();
        reducer.Apply(Mob(10, 900, 0, 42, 42, 1000, 1000, "Observed"));
        reducer.Apply(Damage(11, 1, 900, 10));
        reducer.Apply(new BossHpEvent(P(12), 900, 42, 500, 1200));
        EncounterUpdate completed = reducer.Apply(new BossHpEvent(P(13), 900, 42, 0, 1200));
        EncounterRecord record = Assert.IsType<EncounterRecord>(completed.FinalRecord);

        Assert.Equal(EncounterState.Completed, completed.State);
        Assert.Equal(EncounterCompletionReason.BossDeath, record.CompletionReason);
        Assert.Equal((500UL, 1200UL), (record.Encounter.LastHp, record.Encounter.MaxHp));
        Assert.Equal("Known Boss", record.Encounter.Name);
        Assert.Same(record, reducer.Apply(Damage(14, 1, 900, 999)).FinalRecord);
        Assert.Equal(10UL, Assert.Single(record.Participants).Damage);
    }

    [Fact]
    public void PlayerAndAddTargetsNeverEnterBossDamageTotals()
    {
        EncounterReducer reducer = CreateReducer();
        reducer.Apply(Mob(1, 900, 0, 42, 42));
        reducer.Apply(Damage(2, 1, 900, 10));
        reducer.Apply(Actor(3, 2, "Target player"));
        reducer.Apply(Mob(4, 901, 0, 777));
        reducer.Apply(Damage(5, 1, 2, 100));
        reducer.Apply(Damage(6, 1, 901, 200));

        Assert.Equal(10UL, Assert.Single(reducer.Current!.Participants).Damage);
        Assert.Equal(3, reducer.Current.Events.Length); // evidence retained, totals filtered
    }

    [Theory]
    [InlineData("content")]
    [InlineData("remove")]
    [InlineData("combat")]
    public void AuthoritativeLifecycleSignalsComplete(string signal)
    {
        EncounterReducer reducer = CreateReducer();
        reducer.Apply(new ContentEvent(P(1), 600153, 7, 1, "Dungeon"));
        reducer.Apply(Mob(2, 900, 0, 42, 42));
        reducer.Apply(Damage(3, 1, 900, 10));
        EncounterUpdate update = signal switch
        {
            "content" => reducer.Apply(new ContentEvent(P(4), 0, 0, 0, "")),
            "remove" => reducer.Apply(new EntityRemovedEvent(P(4), 900)),
            _ => reducer.Apply(new CombatStateEvent(P(4), 900, 0)),
        };
        Assert.NotNull(update.FinalRecord);
        Assert.True(update.FinalRecord!.IsComplete);
    }

    [Fact]
    public void IdleTimeoutAdvancesOnlyFromCaptureTimeAndEofFlushFinalizes()
    {
        EncounterReducer reducer = CreateReducer(idleMs: 100);
        reducer.Apply(Mob(1_000, 900, 0, 42, 42));
        reducer.Apply(Damage(1_001, 1, 900, 10));
        reducer.Apply(Actor(1_050, 2, "Later"));
        Assert.Equal(EncounterState.Active, reducer.State);
        EncounterUpdate timeout = reducer.AdvanceTo(1_101);
        Assert.Equal(EncounterCompletionReason.IdleTimeout, timeout.FinalRecord!.CompletionReason);

        EncounterReducer eofReducer = CreateReducer();
        eofReducer.Apply(Mob(2_000, 900, 0, 42, 42));
        eofReducer.Apply(Damage(2_001, 1, 900, 1));
        EncounterRecord eof = eofReducer.CompleteInput(2_002).FinalRecord!;
        Assert.Equal(EncounterCompletionReason.EndOfInput, eof.CompletionReason);
        Assert.True(eof.IsComplete);
    }

    [Fact]
    public void IncompleteAndOutOfOrderInputAreDiagnosedAndOverflowSafe()
    {
        EncounterReducer reducer = CreateReducer();
        reducer.Apply(Mob(10, 900, 0, 42, 42));
        reducer.Apply(Damage(11, 1, 900, ulong.MaxValue));
        EncounterUpdate outOfOrder = reducer.Apply(Damage(9, 1, 900, 1));
        reducer.MarkIncomplete("capture queue overflow");
        EncounterRecord record = reducer.CompleteInput(12).FinalRecord!;

        Assert.Contains(outOfOrder.Diagnostics, d => d.Code == EncounterDiagnosticCode.OutOfOrderEvent);
        Assert.False(record.IsComplete);
        Assert.Contains("out-of-order event", record.Provenance.IncompleteReasons);
        Assert.Contains("capture queue overflow", record.Provenance.IncompleteReasons);
        Assert.Equal(ulong.MaxValue, Assert.Single(record.Participants).Damage);
    }

    [Fact]
    public void SnapshotIsPinnedAndUnknownIdentityEnrichmentDoesNotRewriteEvents()
    {
        GameDataSnapshot snapshot = Snapshot();
        EncounterReducer reducer = CreateReducer(snapshot: snapshot);
        reducer.Apply(Mob(1, 900, 0, 42, 42));
        reducer.Apply(Damage(2, 5, 900, 1));
        Assert.Equal("", reducer.Current!.Events[0].ActorName);
        reducer.Apply(Actor(3, 5, "Later Name"));
        Assert.Equal("", reducer.Current.Events[0].ActorName);
        Assert.Equal("Later Name", Assert.Single(reducer.Current.Participants).Name);
        Assert.Same(snapshot, reducer.PinnedGameData);
    }

    [Fact]
    public void BossCombatStateStartsAndPartyIdentityEnrichesParticipant()
    {
        EncounterReducer reducer = CreateReducer();
        reducer.Apply(Mob(1, 900, 0, 42, 42));
        reducer.Apply(new PartyEvent(P(2), 77, 5, 600153, 7, 1, "Party Name"));
        Assert.Equal(EncounterState.Active, reducer.Apply(new CombatStateEvent(P(3), 900, 1)).State);
        reducer.Apply(Damage(4, 5, 900, 1));
        Assert.Equal("Party Name", Assert.Single(reducer.Current!.Participants).Name);
        Assert.Equal((600153U, 7U), (reducer.Current.Encounter.ContentId, reducer.Current.Encounter.DungeonId));
    }

    [Fact]
    public void EmptyCallerIdUsesStableNonEmptyDeterministicId()
    {
        EncounterReducerOptions options = new(100, Guid.Empty, "1", 1, "pcap", "same capture");
        EncounterReducer first = new(Snapshot(), options);
        EncounterReducer second = new(Snapshot(), options);
        foreach (EncounterReducer reducer in new[] { first, second })
        {
            reducer.Apply(Mob(1, 900, 0, 42, 42));
            reducer.Apply(Damage(2, 1, 900, 1));
            reducer.CompleteInput(3);
        }
        Assert.NotEqual(Guid.Empty, first.Apply(Damage(4, 1, 900, 1)).FinalRecord!.Id);
        Assert.Equal(first.Apply(Damage(4, 1, 900, 1)).FinalRecord!.Id, second.Apply(Damage(4, 1, 900, 1)).FinalRecord!.Id);
    }

    [Fact]
    public void PlayerSummonOwnerBossAndAddRolesRemainDistinct()
    {
        EncounterReducer reducer = CreateReducer();
        reducer.Apply(Actor(1, 10, "Owner"));
        reducer.Apply(Mob(2, 11, 10, 777, name: "Summon"));
        reducer.Apply(Mob(3, 900, 0, 42, 42, name: "Boss"));
        reducer.Apply(Mob(4, 901, 0, 778, name: "Add"));
        reducer.Apply(Damage(5, 11, 900, 1));

        Assert.Collection(reducer.Current!.Entities,
            e => Assert.Equal((10U, EntityKind.Player), (e.ActorId, e.Kind)),
            e => Assert.Equal((11U, EntityKind.Summon), (e.ActorId, e.Kind)),
            e => Assert.Equal((900U, EntityKind.Boss), (e.ActorId, e.Kind)),
            e => Assert.Equal((901U, EntityKind.Add), (e.ActorId, e.Kind)));
        Assert.Equal(10U, reducer.Current.Entities[1].OwnerActorId);
    }

    [Fact]
    public void OutOfOrderClockAdvanceIsIgnoredAndDiagnosed()
    {
        EncounterReducer reducer = CreateReducer();
        reducer.Apply(Mob(10, 900, 0, 42, 42));
        reducer.Apply(Damage(20, 1, 900, 1));
        EncounterUpdate update = reducer.AdvanceTo(19);

        Assert.Contains(update.Diagnostics, d => d.Code == EncounterDiagnosticCode.OutOfOrderEvent);
        Assert.Equal(20, reducer.Current!.LastTimestampMs);
        Assert.False(reducer.Current.Provenance.IsComplete);
    }

    internal static EncounterReducer CreateReducer(long idleMs = 30_000, GameDataSnapshot? snapshot = null) =>
        new(snapshot ?? Snapshot(), new EncounterReducerOptions(
            IdleTimeoutMs: idleMs,
            RecordId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            AppVersion: "1.0.0", AbiVersion: 1, Backend: "pcap", CaptureId: "fixture"));

    internal static GameDataSnapshot Snapshot() => new(
        9, 2, 3, "profile", [6, 0, 54], [13328],
        new Dictionary<ushort, ProtocolOpcode>().ToFrozenDictionary(),
        new Dictionary<uint, ProtocolMessageLayout>().ToFrozenDictionary(),
        new Dictionary<uint, Boss> { [42] = new(42, "Known Boss") }.ToFrozenDictionary(),
        new Dictionary<uint, Dungeon> { [7] = new(7, "Known Dungeon") }.ToFrozenDictionary(),
        new Dictionary<uint, Skill>().ToFrozenDictionary(),
        new Dictionary<uint, Buff> { [8] = new(8, "Known Buff") }.ToFrozenDictionary());

    internal static EventProvenance P(long ms) => new(checked((ulong)ms * 1_000_000), checked((ulong)ms * 1_000_000), 1, 0, 0, 0, 0, 0, 0);
    internal static MobSpawnedEvent Mob(long ms, uint actor, uint owner, uint mob, uint boss = 0, ulong hp = 0, ulong max = 0, string name = "") => new(P(ms), actor, owner, mob, boss, hp, max, name, boss != 0);
    internal static ActorObservedEvent Actor(long ms, uint actor, string name) => new(P(ms), actor, 0, 0, 0, name, false);
    internal static DamageEvent Damage(long ms, uint actor, uint target, ulong damage, ulong multi = 0, ulong healing = 0, uint mask = 0, bool dot = false) => new(P(ms), actor, target, 3, damage, multi, healing, mask, 1, dot);
}
