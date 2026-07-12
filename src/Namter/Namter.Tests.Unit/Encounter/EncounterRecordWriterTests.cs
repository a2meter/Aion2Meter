using System.Text;
using Namter.Encounter;

namespace Namter.Tests.Unit.Encounter;

public sealed class EncounterRecordWriterTests
{
    [Fact]
    public void SameRecordSerializesByteIdenticallyWithStableOrderAndProvenance()
    {
        EncounterReducer reducer = EncounterReducerTests.CreateReducer();
        reducer.Apply(new ContentEvent(EncounterReducerTests.P(1_000), 600153, 7, 1, "Content"));
        reducer.Apply(EncounterReducerTests.Mob(1_001, 900, 0, 42, 42));
        reducer.Apply(EncounterReducerTests.Damage(1_002, 20, 900, 2));
        reducer.Apply(EncounterReducerTests.Damage(1_003, 10, 900, 1));
        EncounterRecord record = reducer.CompleteInput(1_004).FinalRecord!;

        byte[] first = EncounterRecordWriter.Write(record);
        byte[] second = EncounterRecordWriter.Write(record);
        Assert.Equal(first, second);
        string json = Encoding.UTF8.GetString(first);
        Assert.StartsWith("{\"id\":\"11111111-2222-3333-4444-555555555555\",\"startUtc\":", json, StringComparison.Ordinal);
        Assert.Contains("\"appVersion\":\"1.0.0\",\"abiVersion\":1,\"dataVersion\":9,\"schemaVersion\":2,\"profileVersion\":3,\"profileName\":\"profile\",\"backend\":\"pcap\",\"captureId\":\"fixture\"", json, StringComparison.Ordinal);
        Assert.True(json.IndexOf("\"actorId\":10", StringComparison.Ordinal) < json.IndexOf("\"actorId\":20", StringComparison.Ordinal));
    }
}
