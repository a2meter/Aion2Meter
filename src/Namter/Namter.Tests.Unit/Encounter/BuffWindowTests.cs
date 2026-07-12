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
        reducer.Apply(new BuffEvent(EncounterReducerTests.P(3), 1, 1, 8, 100, (byte)BuffAction.Apply));
        reducer.Apply(new BuffEvent(EncounterReducerTests.P(50), 1, 1, 8, 100, (byte)BuffAction.Refresh));
        reducer.Apply(new BuffEvent(EncounterReducerTests.P(80), 1, 1, 8, 0, (byte)BuffAction.Remove));
        reducer.Apply(new BuffEvent(EncounterReducerTests.P(90), 1, 1, 8, 100, (byte)BuffAction.Apply));
        EncounterRecord record = reducer.CompleteInput(120).FinalRecord!;

        Assert.Collection(record.BuffWindows,
            first => { Assert.Equal((3L, 80L), (first.StartTimestampMs, first.EndTimestampMs)); Assert.Equal(50, first.LastRefreshTimestampMs); },
            second => { Assert.Equal((90L, 120L), (second.StartTimestampMs, second.EndTimestampMs)); Assert.Equal(BuffWindowEnd.EncounterEnd, second.EndReason); });
        Assert.All(record.BuffWindows, w => Assert.Equal("Known Buff", w.Name));
    }

    [Fact]
    public void OpenBuffStateIsBoundedBeforeFinalization()
    {
        EncounterReducer reducer = new(EncounterReducerTests.Snapshot(), new EncounterReducerOptions(
            100, Guid.NewGuid(), "1", 1, "pcap", "capture", MaxBuffWindows: 1));
        reducer.Apply(EncounterReducerTests.Mob(1, 900, 0, 42, 42));
        reducer.Apply(EncounterReducerTests.Damage(2, 1, 900, 1));
        reducer.Apply(new BuffEvent(EncounterReducerTests.P(3), 1, 1, 8, 100, (byte)BuffAction.Apply));
        reducer.Apply(new BuffEvent(EncounterReducerTests.P(4), 2, 2, 9, 100, (byte)BuffAction.Apply));

        Assert.False(reducer.Current!.Provenance.IsComplete);
        Assert.Contains("buff window capacity exceeded", reducer.Current.Provenance.IncompleteReasons);
    }
}
