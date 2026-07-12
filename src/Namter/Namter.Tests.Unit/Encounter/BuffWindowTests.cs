using Namter.Encounter;

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

    private static BuffEvent Buff(long ms, uint owner, uint target, uint id, uint duration, BuffOperation operation) =>
        new(EncounterReducerTests.P(ms), owner, target, id, duration, operation, RawAction: 0x7f);
}
