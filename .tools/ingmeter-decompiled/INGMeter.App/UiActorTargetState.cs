using System;
using System.Collections.Generic;
using INGMeter.Core;

namespace INGMeter.App;

public class UiActorTargetState
{
	public int TargetId { get; }

	public DateTime FirstUtc { get; private set; }

	public DateTime LastUtc { get; private set; }

	public long TotalDamage { get; private set; }

	public long TotalHealing { get; private set; }

	public long SelfHealing { get; private set; }

	public long OtherHealing { get; private set; }

	public int DamageEventCount { get; private set; }

	public int HealingEventCount { get; private set; }

	public Dictionary<int, UiSkillState> Skills { get; } = new Dictionary<int, UiSkillState>();

	public UiActorTargetState(int targetId, DateTime firstUtc)
	{
		TargetId = targetId;
		FirstUtc = firstUtc;
		LastUtc = firstUtc;
	}

	public void Apply(DamageEvent e)
	{
		Apply(e, IsDefaultSelfHealingEvent(e));
	}

	public void Apply(DamageEvent e, bool isSelfHealing)
	{
		if (DamageEventCount == 0)
		{
			FirstUtc = e.TimestampUtc;
			LastUtc = e.TimestampUtc;
		}
		else
		{
			if (e.TimestampUtc < FirstUtc)
			{
				FirstUtc = e.TimestampUtc;
			}
			if (e.TimestampUtc > LastUtc)
			{
				LastUtc = e.TimestampUtc;
			}
		}
		TotalDamage += e.Damage;
		if (e.HealAmount > 0)
		{
			TotalHealing += e.HealAmount;
			if (isSelfHealing)
			{
				SelfHealing += e.HealAmount;
			}
			else
			{
				OtherHealing += e.HealAmount;
			}
			HealingEventCount++;
		}
		DamageEventCount++;
		if (!Skills.TryGetValue(e.SkillCodeRaw, out UiSkillState value))
		{
			value = new UiSkillState(e.SkillCodeRaw);
			Skills[e.SkillCodeRaw] = value;
		}
		value.Apply(e, isSelfHealing);
	}

	public void MergeFrom(UiActorTargetState other)
	{
		if (DamageEventCount == 0 || other.FirstUtc < FirstUtc)
		{
			FirstUtc = other.FirstUtc;
		}
		if (DamageEventCount == 0 || other.LastUtc > LastUtc)
		{
			LastUtc = other.LastUtc;
		}
		TotalDamage += other.TotalDamage;
		TotalHealing += other.TotalHealing;
		SelfHealing += other.SelfHealing;
		OtherHealing += other.OtherHealing;
		DamageEventCount += other.DamageEventCount;
		HealingEventCount += other.HealingEventCount;
		foreach (UiSkillState value2 in other.Skills.Values)
		{
			if (!Skills.TryGetValue(value2.SkillCode, out UiSkillState value))
			{
				value = new UiSkillState(value2.SkillCode);
				Skills[value2.SkillCode] = value;
			}
			value.MergeFrom(value2);
		}
	}

	public UiActorTargetState Clone()
	{
		UiActorTargetState uiActorTargetState = new UiActorTargetState(TargetId, FirstUtc)
		{
			LastUtc = LastUtc,
			TotalDamage = TotalDamage,
			TotalHealing = TotalHealing,
			SelfHealing = SelfHealing,
			OtherHealing = OtherHealing,
			DamageEventCount = DamageEventCount,
			HealingEventCount = HealingEventCount
		};
		foreach (UiSkillState value in Skills.Values)
		{
			uiActorTargetState.Skills[value.SkillCode] = value.CloneDamageStatsOnly();
		}
		return uiActorTargetState;
	}

	private static bool IsDefaultSelfHealingEvent(DamageEvent e)
	{
		if (e.TargetId <= 0 || e.ActorId != e.TargetId)
		{
			if (e.HealAmount > 0)
			{
				if (e.Damage <= 0)
				{
					return e.MultiHitDamage > 0;
				}
				return true;
			}
			return false;
		}
		return true;
	}
}
