using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using INGMeter.Core;

namespace INGMeter.WpfUI;

public sealed class EncounterHistoryRow
{
	private static readonly CultureInfo KoreanCulture = new CultureInfo("ko-KR");

	public EncounterLogIndexItem Source { get; }

	public string FullPath { get; }

	public DateTime StartUtc { get; }

	public string DateGroup { get; }

	public string TimeText { get; }

	public string BossName { get; }

	public string BossSubText { get; }

	public string DungeonText { get; }

	public string CategoryText { get; }

	public string DurationText { get; }

	public string TotalDamageText { get; }

	public string ParticipantText { get; }

	public string TopDpsText { get; }

	public string TopActorText { get; }

	public string LocalPlayerDpsText { get; }

	public string SearchText { get; }

	public EncounterHistoryRow(EncounterLogIndexItem source, string fullPath, string dungeonText, string categoryText)
	{
		Source = source;
		FullPath = fullPath;
		StartUtc = ((source.StartUtc.Kind == DateTimeKind.Utc) ? source.StartUtc : DateTime.SpecifyKind(source.StartUtc, DateTimeKind.Utc));
		DateTime dateTime = StartUtc.ToLocalTime();
		DateGroup = dateTime.ToString("yyyy년 M월 d일 dddd", KoreanCulture);
		TimeText = dateTime.ToString("HH:mm", KoreanCulture);
		BossName = (string.IsNullOrWhiteSpace(source.BossName) ? "이름없는 보스" : source.BossName);
		BossSubText = ((source.BossMobCode > 0) ? $"Mob {source.BossMobCode}" : ((source.BossActorId > 0) ? $"Actor {source.BossActorId}" : ""));
		DungeonText = (string.IsNullOrWhiteSpace(dungeonText) ? "던전 정보 없음" : dungeonText);
		CategoryText = categoryText;
		DurationText = FormatDuration(source.DurationMs);
		TotalDamageText = ((source.TotalDamage > 0) ? FormatCompact(source.TotalDamage) : "-");
		ParticipantText = ((source.ParticipantCount > 0) ? $"{source.ParticipantCount}명" : "-");
		TopDpsText = ((source.TopDps > 0.0) ? source.TopDps.ToString("N0", KoreanCulture) : "-");
		TopActorText = (string.IsNullOrWhiteSpace(source.TopActorName) ? "" : (string.IsNullOrWhiteSpace(source.TopActorServer) ? source.TopActorName : (source.TopActorName + " [" + source.TopActorServer + "]")));
		LocalPlayerDpsText = ((source.LocalPlayerDps > 0.0) ? source.LocalPlayerDps.ToString("N0", KoreanCulture) : "-");
		InlineArray7<string> buffer = default(InlineArray7<string>);
		buffer[0] = BossName;
		buffer[1] = BossSubText;
		buffer[2] = DungeonText;
		buffer[3] = CategoryText;
		buffer[4] = TopActorText;
		buffer[5] = source.LocalPlayerName;
		buffer[6] = source.LocalPlayerServer;
		SearchText = string.Join(' ', (ReadOnlySpan<string?>)buffer);
	}

	private static string FormatDuration(long durationMs)
	{
		if (durationMs <= 0)
		{
			return "-";
		}
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(durationMs);
		if (timeSpan.TotalHours >= 1.0)
		{
			return $"{(int)timeSpan.TotalHours:0}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";
		}
		return $"{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";
	}

	private static string FormatCompact(long value)
	{
		if (value >= 100000000)
		{
			return $"{(double)value / 100000000.0:0.##}억";
		}
		if (value >= 10000)
		{
			return $"{(double)value / 10000.0:0.#}만";
		}
		return value.ToString("N0", KoreanCulture);
	}
}
