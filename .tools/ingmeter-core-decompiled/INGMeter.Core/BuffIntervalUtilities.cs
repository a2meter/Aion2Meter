using System;
using System.Collections.Generic;

namespace INGMeter.Core;

public static class BuffIntervalUtilities
{
	public static bool HasInterval(uint durationMs, ulong expiresAtMs)
	{
		if (durationMs == 0)
		{
			return LooksLikeUnixMilliseconds(expiresAtMs);
		}
		return true;
	}

	public static bool OverlapsWindow(DateTime timestampUtc, uint durationMs, ulong startedAtMs, ulong expiresAtMs, DateTime windowStart, DateTime windowEnd)
	{
		(DateTime, DateTime) interval = GetInterval(timestampUtc, durationMs, startedAtMs, expiresAtMs);
		if (interval.Item2 > windowStart)
		{
			return interval.Item1 < windowEnd;
		}
		return false;
	}

	public static (DateTime Start, DateTime End) GetInterval(DateTime timestampUtc, uint durationMs, ulong startedAtMs, ulong expiresAtMs)
	{
		DateTime dateTime = (LooksLikeUnixMilliseconds(startedAtMs) ? DateTimeOffset.FromUnixTimeMilliseconds((long)startedAtMs).UtcDateTime : timestampUtc);
		DateTime dateTime2 = (LooksLikeUnixMilliseconds(expiresAtMs) ? DateTimeOffset.FromUnixTimeMilliseconds((long)expiresAtMs).UtcDateTime : dateTime.AddMilliseconds(durationMs));
		if (dateTime2 <= dateTime && durationMs != 0)
		{
			dateTime2 = dateTime.AddMilliseconds(durationMs);
		}
		return (Start: dateTime, End: dateTime2);
	}

	public static bool LooksLikeUnixMilliseconds(ulong value)
	{
		if (value >= 1600000000000L)
		{
			return value <= 4102444800000L;
		}
		return false;
	}

	public static double SumMergedSeconds(IReadOnlyList<(DateTime Start, DateTime End)> intervals)
	{
		if (intervals.Count == 0)
		{
			return 0.0;
		}
		DateTime dateTime = intervals[0].Start;
		DateTime dateTime2 = intervals[0].End;
		double num = 0.0;
		for (int i = 1; i < intervals.Count; i++)
		{
			(DateTime, DateTime) tuple = intervals[i];
			if (tuple.Item1 <= dateTime2)
			{
				if (tuple.Item2 > dateTime2)
				{
					dateTime2 = tuple.Item2;
				}
			}
			else
			{
				num += Math.Max(0.0, (dateTime2 - dateTime).TotalSeconds);
				(dateTime, dateTime2) = tuple;
			}
		}
		return num + Math.Max(0.0, (dateTime2 - dateTime).TotalSeconds);
	}

	public static int CountMerged(IReadOnlyList<(DateTime Start, DateTime End)> intervals)
	{
		if (intervals.Count == 0)
		{
			return 0;
		}
		int num = 1;
		DateTime item = intervals[0].End;
		for (int i = 1; i < intervals.Count; i++)
		{
			(DateTime, DateTime) tuple = intervals[i];
			if (tuple.Item1 <= item)
			{
				if (tuple.Item2 > item)
				{
					item = tuple.Item2;
				}
			}
			else
			{
				num++;
				item = tuple.Item2;
			}
		}
		return num;
	}
}
