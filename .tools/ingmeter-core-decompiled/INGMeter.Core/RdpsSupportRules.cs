using System;
using System.Collections.Generic;
using System.Linq;

namespace INGMeter.Core;

public static class RdpsSupportRules
{
	public static int ResolvePartyBuffProviderId(RdpsPartyBuffEffect effect, int ownerId, int targetId, IReadOnlySet<int> participantIds, IReadOnlyDictionary<int, JobClass> actorJobs)
	{
		JobClass ownerJob = RdpsPartyBuffCatalog.GetOwnerJob(effect);
		if (ownerJob == JobClass.None)
		{
			return 0;
		}
		if (ownerId > 0 && participantIds.Contains(ownerId) && HasActorJob(ownerId, ownerJob, actorJobs))
		{
			return ownerId;
		}
		if (targetId > 0 && participantIds.Contains(targetId) && HasActorJob(targetId, ownerJob, actorJobs))
		{
			return targetId;
		}
		return 0;
	}

	public static bool IsEffectOwnerJob(int ownerId, RdpsPartyBuffEffect effect, IReadOnlyDictionary<int, JobClass> actorJobs)
	{
		JobClass ownerJob = RdpsPartyBuffCatalog.GetOwnerJob(effect);
		if (ownerJob != JobClass.None && actorJobs.TryGetValue(ownerId, out var value))
		{
			return value == ownerJob;
		}
		return true;
	}

	public static IReadOnlyList<TWindow> FilterWindowsForDamageEvent<TWindow>(IReadOnlyList<TWindow> windows, bool isCrit) where TWindow : IRdpsSupportWindow
	{
		bool flag = windows.Any((TWindow window) => window.SkillId == 12120000);
		List<TWindow> list = new List<TWindow>(windows.Count);
		foreach (TWindow window in windows)
		{
			if (window.EffectKind != RdpsEffectKind.TargetOutgoingDamageDown && (window.EffectKind != RdpsEffectKind.CriticalDamageTaken || isCrit) && !(window.SkillId == 11800000 && flag))
			{
				list.Add(window);
			}
		}
		return list;
	}

	public static IReadOnlyList<RdpsSupportGroup<TWindow>> SelectEffectiveGroups<TWindow>(IEnumerable<TWindow> windows) where TWindow : IRdpsSupportWindow
	{
		List<RdpsSupportGroup<TWindow>> source = (from @group in (from window in windows
				group window by new { window.SkillId, window.TargetId, window.EffectKind }).Select(BuildEffectiveGroup)
			where @group != null
			select (@group)).ToList();
		List<RdpsSupportGroup<TWindow>> list = source.Where((RdpsSupportGroup<TWindow> group) => string.IsNullOrWhiteSpace(group.ExclusiveGroup)).ToList();
		list.AddRange(from @group in source
			where !string.IsNullOrWhiteSpace(@group.ExclusiveGroup)
			group @group by @group.ExclusiveGroup into @group
			select (from effect in @group
				orderby effect.Percent descending, string.Equals(effect.SkillName, "불패의 진언", StringComparison.Ordinal) ? 1 : 0 descending, effect.LatestStart descending
				select effect).First());
		return list;
	}

	private static RdpsSupportGroup<TWindow>? BuildEffectiveGroup<TWindow>(IEnumerable<TWindow> group) where TWindow : IRdpsSupportWindow
	{
		List<TWindow> list = (from window in @group
			orderby window.Percent descending, window.Start descending
			select window).ToList();
		if (list.Count == 0)
		{
			return null;
		}
		TWindow best = list[0];
		List<TWindow> list2 = (from window in list
			where window.LevelCode == best.LevelCode && AreNearlyEqual(window.Percent, best.Percent)
			group window by window.OwnerId into ownerGroup
			select ownerGroup.OrderByDescending((TWindow window) => window.Start).First() into window
			orderby window.OwnerId
			select window).ToList();
		if (list2.Count == 0)
		{
			list2.Add(best);
		}
		double sourceShare = 1.0 / (double)list2.Count;
		return new RdpsSupportGroup<TWindow>(best.Multiplier, best.Percent, best.SkillName, best.ExclusiveGroup, best.EffectKind, list2.Max((TWindow window) => window.Start), list2.Select((TWindow window) => new RdpsSupportSourceShare<TWindow>(window, sourceShare)).ToList());
	}

	private static bool HasActorJob(int actorId, JobClass requiredJob, IReadOnlyDictionary<int, JobClass> actorJobs)
	{
		if (actorJobs.TryGetValue(actorId, out var value))
		{
			return value == requiredJob;
		}
		return false;
	}

	private static bool AreNearlyEqual(double left, double right)
	{
		return Math.Abs(left - right) < 0.0001;
	}
}
