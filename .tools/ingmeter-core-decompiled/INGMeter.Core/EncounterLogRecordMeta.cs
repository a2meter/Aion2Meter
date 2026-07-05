using System;
using System.Collections.Generic;

namespace INGMeter.Core;

public sealed record EncounterLogRecordMeta
{
	public string Id { get; init; } = "";

	public DateTime StartUtc { get; init; }

	public DateTime EndUtc { get; init; }

	public long DurationMs { get; init; }

	public int BossActorId { get; init; }

	public string BossName { get; init; } = "";

	public int BossMobCode { get; init; }

	public int BossMaxHp { get; init; }

	public int ContentCode { get; init; }

	public long TotalDamage { get; init; }

	public int ParticipantCount { get; init; }

	public int EventCount { get; init; }

	public string AppVersion { get; init; } = "";

	public string LocalPlayerName { get; init; } = "";

	public string LocalPlayerServer { get; init; } = "";

	public double LocalPlayerDps { get; init; }

	public long LocalPlayerDamage { get; init; }

	public List<EncounterLogParticipantMeta> Participants { get; init; } = new List<EncounterLogParticipantMeta>();
}
