namespace INGMeter.Core;

public sealed record EncounterLogParticipantMeta
{
	public int ActorId { get; init; }

	public string Name { get; init; } = "";

	public string ServerName { get; init; } = "";

	public JobClass Job { get; init; }

	public long Damage { get; init; }

	public double Dps { get; init; }

	public int Hits { get; init; }

	public long Healing { get; init; }

	public long SelfHealing { get; init; }

	public long OtherHealing { get; init; }

	public double Hps { get; init; }

	public int HealHits { get; init; }
}
