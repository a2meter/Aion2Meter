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
        Assert.Equal((0UL, 1200UL), (record.Encounter.LastHp, record.Encounter.MaxHp));
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
        Assert.Contains(record.Provenance.IncompleteReasons, r => r.Code == IncompleteReasonCode.OutOfOrderEvent);
        Assert.Contains(record.Provenance.IncompleteReasons, r => r.Code == IncompleteReasonCode.ExternalIncomplete && r.Message == "capture queue overflow");
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
        string text = first.Apply(Damage(4, 1, 900, 1)).FinalRecord!.Id.ToString();
        Assert.Equal("0f4185de-f47c-865e-a04e-22576898556b", text);
        Assert.Equal('8', text[14]);
        Assert.Contains(text[19], "89ab");
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

    [Fact]
    public void CaptureClockAdvanceMakesAnEarlierLaterEventOutOfOrder()
    {
        EncounterReducer reducer = CreateReducer(idleMs: 1_000);
        reducer.Apply(Mob(1, 900, 0, 42, 42)); reducer.Apply(Damage(2, 1, 900, 1));
        reducer.AdvanceTo(100);
        EncounterUpdate ignored = reducer.Apply(Damage(90, 1, 900, 99));
        Assert.Contains(ignored.Diagnostics, d => d.Code == EncounterDiagnosticCode.OutOfOrderEvent);
        Assert.Equal(100, reducer.Current!.LastTimestampMs);
        Assert.Equal(1UL, Assert.Single(reducer.Current.Participants).Damage);
    }

    [Fact]
    public void ActiveBossIdentityIsPinnedAndConflictingKnownBossBecomesAdd()
    {
        GameDataSnapshot snapshot = Snapshot() with { Bosses = new Dictionary<uint, Boss>
        {
            [42] = new(42, "First"), [43] = new(43, "Second")
        }.ToFrozenDictionary() };
        EncounterReducer reducer = CreateReducer(snapshot: snapshot);
        reducer.Apply(Mob(1, 900, 0, 42, 42));
        reducer.Apply(new CombatStateEvent(P(2), 900, 1));
        EncounterUpdate collision = reducer.Apply(Mob(3, 901, 0, 43, 43));
        reducer.Apply(new BossHpEvent(P(4), 901, 43, 999, 1000));

        Assert.Equal((900U, 42U, "First"), (reducer.Current!.Encounter.BossActorId, reducer.Current.Encounter.BossCode, reducer.Current.Encounter.Name));
        Assert.Equal(EntityKind.Boss, reducer.Current.Entities.Single(e => e.ActorId == 900).Kind);
        Assert.Equal(EntityKind.Add, reducer.Current.Entities.Single(e => e.ActorId == 901).Kind);
        Assert.Contains(collision.Diagnostics, d => d.Code == EncounterDiagnosticCode.BossIdentityConflict);
    }

    [Fact]
    public void EntityRolePrecedenceAndSummonOwnerAreOrderIndependent()
    {
        EncounterReducer reducer = CreateReducer();
        reducer.Apply(Mob(1, 50, 10, 777, name: "Summon"));
        reducer.Apply(Actor(2, 50, "Player-shaped update"));
        reducer.Apply(new PartyEvent(P(3), 1, 50, 0, 0, 1, "Party-shaped update"));
        reducer.Apply(Mob(4, 900, 0, 42, 42)); reducer.Apply(Damage(5, 50, 900, 1));
        EntityRecord entity = reducer.Current!.Entities.Single(e => e.ActorId == 50);
        Assert.Equal((EntityKind.Summon, 10U), (entity.Kind, entity.OwnerActorId));
        Assert.Equal(10U, Assert.Single(reducer.Current.Participants).ActorId);
    }

    [Fact]
    public void ActivationDowngradesEarlierBossCandidateToAdd()
    {
        GameDataSnapshot snapshot = Snapshot() with { Bosses = new Dictionary<uint, Boss>
        {
            [42] = new(42, "First"), [43] = new(43, "Second")
        }.ToFrozenDictionary() };
        EncounterReducer reducer = CreateReducer(snapshot: snapshot);
        reducer.Apply(Mob(1, 901, 0, 43, 43));
        reducer.Apply(Mob(2, 900, 0, 42, 42));
        reducer.Apply(Damage(3, 1, 900, 1));
        Assert.Equal(EntityKind.Add, reducer.Current!.Entities.Single(e => e.ActorId == 901).Kind);
        Assert.Equal(EntityKind.Boss, reducer.Current.Entities.Single(e => e.ActorId == 900).Kind);
    }

    [Fact]
    public void IdleCandidatesAreSelectedByAuthoritativeDamageTargetInsteadOfObservationOrder()
    {
        EncounterReducer reducer = CreateReducer(snapshot: TwoBossSnapshot());
        reducer.Apply(Mob(1, 900, 0, 42, 42));
        reducer.Apply(Mob(2, 901, 0, 43, 43));
        reducer.Apply(Damage(3, 1, 900, 1));
        Assert.Equal((900U, 42U, "First"),
            (reducer.Current!.Encounter.BossActorId, reducer.Current.Encounter.BossCode, reducer.Current.Encounter.Name));
    }

    [Fact]
    public void IdleCandidatesAreSelectedByMatchingCombatStateActor()
    {
        EncounterReducer reducer = CreateReducer(snapshot: TwoBossSnapshot());
        reducer.Apply(new BossHpEvent(P(1), 900, 42, 100, 100));
        reducer.Apply(Mob(2, 901, 0, 43, 43));
        reducer.Apply(new CombatStateEvent(P(3), 901, 1));
        Assert.Equal((901U, 43U, "Second"),
            (reducer.Current!.Encounter.BossActorId, reducer.Current.Encounter.BossCode, reducer.Current.Encounter.Name));
    }

    [Fact]
    public void UnknownCombatActorNeverCreatesBosslessEncounter()
    {
        EncounterReducer reducer = CreateReducer();
        EncounterUpdate ignored = reducer.Apply(new CombatStateEvent(P(1), 777, 1));
        Assert.Equal(EncounterState.Idle, ignored.State);
        Assert.Null(reducer.CompleteInput(2).FinalRecord);
    }

    [Fact]
    public void IdleBossCandidateMapIsBoundedAndDiagnosesOverflow()
    {
        EncounterReducer reducer = new(TwoBossSnapshot(), new EncounterReducerOptions(100, Guid.Empty, "1", 1, "pcap", "c",
            MaxBossCandidates: 1));
        reducer.Apply(Mob(1, 900, 0, 42, 42));
        EncounterUpdate overflow = reducer.Apply(Mob(2, 901, 0, 43, 43));
        Assert.Contains(overflow.Diagnostics, d => d.Code == EncounterDiagnosticCode.CapacityExceeded);
        reducer.Apply(Damage(3, 1, 900, 1));
        Assert.Equal(900U, reducer.Current!.Encounter.BossActorId);
        Assert.Contains(reducer.Current.Provenance.IncompleteReasons, r => r.Code == IncompleteReasonCode.CapacityExceeded);
    }

    [Fact]
    public void DiagnosticsPerUpdateAreBoundedEvenWhenFinalizationDropsManyWindows()
    {
        EncounterReducer reducer = new(Snapshot(), new EncounterReducerOptions(100_000, Guid.Empty, "1", 1, "pcap", "c",
            MaxBuffWindows: 100, MaxDiagnosticsPerUpdate: 8));
        reducer.Apply(Mob(1, 900, 0, 42, 42)); reducer.Apply(Damage(2, 1, 900, 1));
        for (uint i = 0; i < 100; i++)
        {
            reducer.Apply(new BuffEvent(P(3 + i * 2), 1, 1, i, 10_000, BuffOperation.Apply, 0));
            reducer.Apply(new BuffEvent(P(4 + i * 2), 1, 1, i, 0, BuffOperation.Remove, 0));
        }
        for (uint i = 0; i < 100; i++)
            reducer.Apply(new BuffEvent(P(1_000), 2, 2, i, 10_000, BuffOperation.Apply, 0));
        EncounterUpdate completed = reducer.CompleteInput(1_001);
        Assert.Equal(8, completed.Diagnostics.Length);
        Assert.Equal(100, completed.FinalRecord!.BuffUptimes.Length);
        Assert.Contains(completed.FinalRecord!.Provenance.IncompleteReasons,
            r => r.Code == IncompleteReasonCode.CapacityExceeded && r.Count == 100);
    }

    [Fact]
    public void IncompleteReasonsAndMessagesAreBoundedWithDeterministicOverflowMarker()
    {
        EncounterReducer reducer = new(Snapshot(), new EncounterReducerOptions(100, Guid.Empty, "1", 1, "pcap", "c",
            MaxIncompleteReasons: 2, MaxIncompleteReasonUtf8Bytes: 8));
        reducer.Apply(Mob(1, 900, 0, 42, 42)); reducer.Apply(Damage(2, 1, 900, 1));
        for (int i = 0; i < 100; i++) reducer.MarkIncomplete($"{i:D8}-고유한-매우긴-사유");
        EncounterRecord record = reducer.CompleteInput(3).FinalRecord!;
        Assert.Equal(3, record.Provenance.IncompleteReasons.Length);
        Assert.Contains(record.Provenance.IncompleteReasons, r => r.Code == IncompleteReasonCode.ReasonLimitReached && r.Count == 98);
        Assert.All(record.Provenance.IncompleteReasons, r => Assert.True(System.Text.Encoding.UTF8.GetByteCount(r.Message) <= 8));
    }

    [Fact]
    public void ArithmeticAndCapacityOverflowSaturateAndUseLiveReasonCodes()
    {
        EncounterReducer reducer = new(Snapshot(), new EncounterReducerOptions(100, Guid.Empty, "1", 1, "pcap", "c", MaxEvents: 2));
        reducer.Apply(Mob(1, 900, 0, 42, 42));
        reducer.Apply(Damage(2, 1, 900, ulong.MaxValue - 1, healing: ulong.MaxValue));
        reducer.Apply(Damage(3, 1, 900, 2, healing: 1));
        reducer.Apply(Damage(4, 1, 900, 1));
        EncounterRecord record = reducer.CompleteInput(5).FinalRecord!;
        ParticipantRecord participant = Assert.Single(record.Participants);
        Assert.Equal((ulong.MaxValue, ulong.MaxValue), (participant.Damage, participant.Healing));
        Assert.Contains(record.Provenance.IncompleteReasons, r => r.Code == IncompleteReasonCode.ArithmeticOverflow);
        Assert.Contains(record.Provenance.IncompleteReasons, r => r.Code == IncompleteReasonCode.CapacityExceeded);
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

    private static GameDataSnapshot TwoBossSnapshot() => Snapshot() with { Bosses = new Dictionary<uint, Boss>
    {
        [42] = new(42, "First"), [43] = new(43, "Second")
    }.ToFrozenDictionary() };

    internal static EventProvenance P(long ms) => new(checked((ulong)ms * 1_000_000), checked((ulong)ms * 1_000_000), 1, 0, 0, 0, 0, 0, 0);
    internal static MobSpawnedEvent Mob(long ms, uint actor, uint owner, uint mob, uint boss = 0, ulong hp = 0, ulong max = 0, string name = "") => new(P(ms), actor, owner, mob, boss, hp, max, name, boss != 0);
    internal static ActorObservedEvent Actor(long ms, uint actor, string name) => new(P(ms), actor, 0, 0, 0, name, false);
    internal static DamageEvent Damage(long ms, uint actor, uint target, ulong damage, ulong multi = 0, ulong healing = 0, uint mask = 0, bool dot = false) => new(P(ms), actor, target, 3, damage, multi, healing, mask, 1, dot);
}
