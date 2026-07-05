using System;
using System.Linq;

namespace INGMeter.Core;

public sealed record EncounterLogIndexItem
{
	public string Id { get; init; } = "";

	public string FileName { get; init; } = "";

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

	public string TopActorName { get; init; } = "";

	public string TopActorServer { get; init; } = "";

	public double TopDps { get; init; }

	public long TopDamage { get; init; }

	public string LocalPlayerName { get; init; } = "";

	public string LocalPlayerServer { get; init; } = "";

	public double LocalPlayerDps { get; init; }

	public long LocalPlayerDamage { get; init; }

	public int EventCount { get; init; }

	public long OriginalBytes { get; init; }

	public long StoredBytes { get; init; }

	public string AppVersion { get; init; } = "";

	public static EncounterLogIndexItem FromMeta(EncounterLogRecordMeta meta, string fileName, long originalBytes, long storedBytes)
	{
		EncounterLogParticipantMeta encounterLogParticipantMeta = (from x in meta.Participants
			orderby x.Dps descending, x.Damage descending
			select x).FirstOrDefault();
		return new EncounterLogIndexItem
		{
			Id = meta.Id,
			FileName = fileName,
			StartUtc = meta.StartUtc,
			EndUtc = meta.EndUtc,
			DurationMs = meta.DurationMs,
			BossActorId = meta.BossActorId,
			BossName = meta.BossName,
			BossMobCode = meta.BossMobCode,
			BossMaxHp = meta.BossMaxHp,
			ContentCode = meta.ContentCode,
			TotalDamage = meta.TotalDamage,
			ParticipantCount = meta.ParticipantCount,
			TopActorName = (encounterLogParticipantMeta?.Name ?? ""),
			TopActorServer = (encounterLogParticipantMeta?.ServerName ?? ""),
			TopDps = (encounterLogParticipantMeta?.Dps ?? 0.0),
			TopDamage = (encounterLogParticipantMeta?.Damage ?? 0),
			LocalPlayerName = meta.LocalPlayerName,
			LocalPlayerServer = meta.LocalPlayerServer,
			LocalPlayerDps = meta.LocalPlayerDps,
			LocalPlayerDamage = meta.LocalPlayerDamage,
			EventCount = meta.EventCount,
			OriginalBytes = originalBytes,
			StoredBytes = storedBytes,
			AppVersion = meta.AppVersion
		};
	}
}
