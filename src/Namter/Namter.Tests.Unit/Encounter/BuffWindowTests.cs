using Namter.Encounter;
using Namter.GameData;
using System.Collections.Frozen;

namespace Namter.Tests.Unit.Encounter;

public sealed class BuffWindowTests
{
    [Fact]
    public void ApplyRefreshRemoveAndEncounterEndCloseDeterministicWindows()
    {
        EncounterReducer reducer = EncounterReducerTests.CreateReducer();
        reducer.Apply(EncounterReducerTests.Mob(1, 900, 0, 42, 42));
        reducer.Apply(EncounterReducerTests.Damage(2, 1, 900, 1));
        reducer.Apply(Buff(3, 1, 1, 8, 100, BuffOperation.Apply));
        reducer.Apply(Buff(50, 1, 1, 8, 100, BuffOperation.Refresh));
        reducer.Apply(Buff(80, 1, 1, 8, 0, BuffOperation.Remove));
        reducer.Apply(Buff(90, 1, 1, 8, 100, BuffOperation.Apply));
        EncounterRecord record = reducer.CompleteInput(120).FinalRecord!;

        Assert.Collection(record.BuffWindows,
            first => { Assert.Equal((3L, 80L), (first.StartTimestampMs, first.EndTimestampMs)); Assert.Equal(50, first.LastRefreshTimestampMs); },
            second => { Assert.Equal((90L, 120L), (second.StartTimestampMs, second.EndTimestampMs)); Assert.Equal(BuffWindowEnd.EncounterEnd, second.EndReason); });
        Assert.All(record.BuffWindows, w => Assert.Equal("Known Buff", w.Name));
        Assert.Equal((107UL, 2U), (Assert.Single(record.BuffUptimes).TotalDurationMs, Assert.Single(record.BuffUptimes).WindowCount));
    }

    [Fact]
    public void OpenBuffStateIsBoundedBeforeFinalization()
    {
        EncounterReducer reducer = new(EncounterReducerTests.Snapshot(), new EncounterReducerOptions(
            100, Guid.NewGuid(), "1", 1, "pcap", "capture", MaxBuffWindows: 1));
        reducer.Apply(EncounterReducerTests.Mob(1, 900, 0, 42, 42));
        reducer.Apply(EncounterReducerTests.Damage(2, 1, 900, 1));
        reducer.Apply(Buff(3, 1, 1, 8, 100, BuffOperation.Apply));
        reducer.Apply(Buff(4, 2, 2, 9, 100, BuffOperation.Apply));

        Assert.False(reducer.Current!.Provenance.IsComplete);
        Assert.Contains(reducer.Current.Provenance.IncompleteReasons, r => r.Code == IncompleteReasonCode.CapacityExceeded);
    }

    [Fact]
    public void PreservedAndPreCombatBuffCollectionsDiagnoseCapacityLoss()
    {
        EncounterReducer preserved = new(EncounterReducerTests.Snapshot(), new EncounterReducerOptions(
            100_000, Guid.NewGuid(), "1", 1, "pcap", "capture", MaxBuffWindows: 1,
            PreserveBuffObservations: true));
        preserved.Apply(EncounterReducerTests.Mob(0, 900, 0, 42, 42));
        preserved.Apply(EncounterReducerTests.Damage(1, 1, 900, 1));
        preserved.Apply(Buff(2, 1, 1, 8, 100, BuffOperation.Apply));
        EncounterUpdate preservedOverflow = preserved.Apply(Buff(3, 2, 2, 9, 100, BuffOperation.Apply));
        Assert.Contains(preservedOverflow.Diagnostics, d => d.Code == EncounterDiagnosticCode.CapacityExceeded);
        Assert.False(preserved.Current!.Provenance.IsComplete);

        EncounterReducer pending = new(EncounterReducerTests.Snapshot(), new EncounterReducerOptions(
            100_000, Guid.NewGuid(), "1", 1, "pcap", "capture", MaxBuffWindows: 1,
            RequireCombatStart: true, PreserveBuffObservations: true, CarryInitialBuffState: true));
        pending.Apply(EncounterReducerTests.Mob(0, 900, 0, 42, 42));
        pending.Apply(EncounterReducerTests.Actor(1, 7, "Player"));
        pending.Apply(EncounterReducerTests.Damage(2, 7, 900, 1));
        pending.Apply(Buff(3, 1, 7, 8, 100, BuffOperation.Apply));
        EncounterUpdate pendingOverflow = pending.Apply(Buff(4, 2, 7, 9, 100, BuffOperation.Apply));
        Assert.Contains(pendingOverflow.Diagnostics, d => d.Code == EncounterDiagnosticCode.CapacityExceeded);
        pending.Apply(new CombatStateEvent(EncounterReducerTests.P(5), 900, 1));
        Assert.False(pending.Current!.Provenance.IsComplete);
        Assert.Contains(pending.Current.Provenance.IncompleteReasons, r => r.Code == IncompleteReasonCode.CapacityExceeded);
    }

    [Fact]
    public void DurationExpiryAndRefreshUseOnlyTheMonotonicCaptureClock()
    {
        EncounterReducer reducer = EncounterReducerTests.CreateReducer(idleMs: 100_000);
        reducer.Apply(EncounterReducerTests.Mob(0, 900, 0, 42, 42));
        reducer.Apply(EncounterReducerTests.Damage(1, 1, 900, 1));
        reducer.Apply(Buff(100, 1, 1, 8, 4_600, BuffOperation.Apply));
        reducer.Apply(Buff(4_000, 1, 1, 8, 9_600, BuffOperation.Refresh));
        reducer.AdvanceTo(13_599);
        Assert.Empty(reducer.Current!.BuffWindows);
        reducer.AdvanceTo(13_600);
        BuffWindowRecord window = Assert.Single(reducer.Current!.BuffWindows);
        Assert.Equal((100L, 13_600L, BuffWindowEnd.Expired), (window.StartTimestampMs, window.EndTimestampMs, window.EndReason));
        Assert.Equal(13_500UL, Assert.Single(reducer.Current.BuffUptimes).TotalDurationMs);
    }

    [Fact]
    public void RefreshAfterExpiryCreatesASeparateWindow()
    {
        EncounterReducer reducer = EncounterReducerTests.CreateReducer(idleMs: 100_000);
        reducer.Apply(EncounterReducerTests.Mob(0, 900, 0, 42, 42));
        reducer.Apply(EncounterReducerTests.Damage(1, 1, 900, 1));
        reducer.Apply(Buff(100, 1, 1, 8, 100, BuffOperation.Apply));
        reducer.Apply(Buff(201, 1, 1, 8, 100, BuffOperation.Refresh));
        EncounterRecord record = reducer.CompleteInput(400).FinalRecord!;
        Assert.Collection(record.BuffWindows,
            w => Assert.Equal((100L, 200L), (w.StartTimestampMs, w.EndTimestampMs)),
            w => Assert.Equal((201L, 301L), (w.StartTimestampMs, w.EndTimestampMs)));
        Assert.Equal(200UL, Assert.Single(record.BuffUptimes).TotalDurationMs);
    }

    [Fact]
    public void PreservedObservationsKeepWindowsButUnionUptimeByTargetAndBuff()
    {
        EncounterReducer reducer = new(EncounterReducerTests.Snapshot(), new EncounterReducerOptions(
            100_000, Guid.NewGuid(), "1", 1, "pcap", "capture", PreserveBuffObservations: true));
        reducer.Apply(EncounterReducerTests.Mob(0, 900, 0, 42, 42));
        reducer.Apply(EncounterReducerTests.Damage(1, 1, 900, 1));
        reducer.Apply(EncounterReducerTests.Actor(2, 7, "Player"));
        reducer.Apply(Buff(100, 1, 7, 8, 100, BuffOperation.Apply));
        reducer.Apply(Buff(150, 1, 9, 8, 100, BuffOperation.Refresh));
        EncounterRecord record = reducer.CompleteInput(300).FinalRecord!;

        Assert.Equal(2, record.BuffWindows.Length);
        BuffUptimeRecord uptime = Assert.Single(record.BuffUptimes);
        Assert.Equal((1U, 8U, 150UL, 2U),
            (uptime.OwnerId, uptime.BuffId, uptime.TotalDurationMs, uptime.WindowCount));
    }

    [Fact]
    public void CombatStartCarriesOnlyLatestKnownPlayerBuffSinceBossDamageCandidate()
    {
        EncounterReducer reducer = new(EncounterReducerTests.Snapshot(), new EncounterReducerOptions(
            100_000, Guid.NewGuid(), "1", 1, "pcap", "capture", RequireCombatStart: true,
            PreserveBuffObservations: true, CarryInitialBuffState: true));
        reducer.Apply(EncounterReducerTests.Mob(1, 900, 0, 42, 42));
        reducer.Apply(EncounterReducerTests.Actor(2, 7, "Player"));
        reducer.Apply(Buff(3, 1, 7, 8, 100, BuffOperation.Apply));
        reducer.Apply(EncounterReducerTests.Damage(10, 7, 900, 1));
        reducer.Apply(Buff(11, 1, 900, 8, 100, BuffOperation.Apply));
        reducer.Apply(Buff(12, 1, 7, 8, 100, BuffOperation.Apply));
        reducer.Apply(Buff(13, 1, 7, 8, 100, BuffOperation.Refresh));
        reducer.Apply(Buff(14, 1, 77, 8, 100, BuffOperation.Apply));
        reducer.Apply(new CombatStateEvent(EncounterReducerTests.P(20), 900, 1));

        EncounterRecord record = reducer.CompleteInput(200).FinalRecord!;
        BuffWindowRecord carried = Assert.Single(record.BuffWindows);
        Assert.Equal((1U, 7U, 8U, 20L, 113L),
            (carried.OwnerId, carried.TargetId, carried.BuffId, carried.StartTimestampMs, carried.EndTimestampMs));
    }

    [Fact]
    public void BuffWindowsPreserveSummonOwnerAndApplyDatabaseTargetOwnerPolicy()
    {
        GameDataSnapshot snapshot = EncounterReducerTests.Snapshot() with
        {
            Buffs = new Dictionary<uint, Buff>
            {
                [8] = new(8, "Summon"),
                [9] = new(9, "Target-owned", TrackUptime: false, UseTargetUptime: true)
            }.ToFrozenDictionary()
        };
        EncounterReducer reducer = EncounterReducerTests.CreateReducer(snapshot: snapshot);
        reducer.Apply(EncounterReducerTests.Actor(1, 1, "Owner"));
        reducer.Apply(EncounterReducerTests.Mob(2, 2, 1, 777, name: "Summon"));
        reducer.Apply(EncounterReducerTests.Mob(3, 900, 0, 42, 42));
        reducer.Apply(EncounterReducerTests.Damage(4, 1, 900, 1));
        reducer.Apply(Buff(5, 2, 900, 8, 100, BuffOperation.Apply));
        reducer.Apply(Buff(6, 1, 7, 9, 100, BuffOperation.Apply));

        EncounterRecord record = reducer.CompleteInput(20).FinalRecord!;
        Assert.Contains(record.BuffWindows, x => x.OwnerId == 2 && x.TargetId == 900 && x.BuffId == 8);
        Assert.Contains(record.BuffWindows, x => x.OwnerId == 7 && x.TargetId == 7 && x.BuffId == 9);
    }

    private static BuffEvent Buff(long ms, uint owner, uint target, uint id, uint duration, BuffOperation operation) =>
        new(EncounterReducerTests.P(ms), owner, target, id, duration, operation, RawAction: 0x7f);
}
