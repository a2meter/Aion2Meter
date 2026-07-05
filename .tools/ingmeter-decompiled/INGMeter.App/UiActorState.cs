using System;
using System.Collections.Generic;
using System.Linq;
using INGMeter.Core;

namespace INGMeter.App;

public class UiActorState
{
	public int ActorId { get; }

	public DateTime FirstUtc { get; private set; }

	public DateTime LastUtc { get; private set; }

	public long TotalDamage { get; private set; }

	public long TotalHealing { get; private set; }

	public long SelfHealing { get; private set; }

	public long OtherHealing { get; private set; }

	public int DamageEventCount { get; private set; }

	public int HealingEventCount { get; private set; }

	public Dictionary<int, UiSkillState> Skills { get; } = new Dictionary<int, UiSkillState>();

	public Dictionary<int, UiActorTargetState> Targets { get; } = new Dictionary<int, UiActorTargetState>();

	public Queue<DamageEvent> Recent { get; } = new Queue<DamageEvent>();

	public Queue<UiBuffEvent> BuffEvents { get; } = new Queue<UiBuffEvent>();

	public UiActorState(int actorId, DateTime firstUtc)
	{
		ActorId = actorId;
		FirstUtc = firstUtc;
		LastUtc = firstUtc;
	}

	public void Apply(DamageEvent e)
	{
		Apply(e, IsDefaultSelfHealingEvent(e, ActorId));
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
		int damage = e.Damage;
		TotalDamage += damage;
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
		if (e.TargetId > 0)
		{
			if (!Targets.TryGetValue(e.TargetId, out UiActorTargetState value2))
			{
				value2 = new UiActorTargetState(e.TargetId, e.TimestampUtc);
				Targets[e.TargetId] = value2;
			}
			value2.Apply(e, isSelfHealing);
		}
		Recent.Enqueue(e);
	}

	public void TrimRecent(int limit)
	{
		while (Recent.Count > limit)
		{
			Recent.Dequeue();
		}
	}

	public void AddRecentOnly(DamageEvent e)
	{
		Recent.Enqueue(e);
	}

	public void RemoveTargetEvents(int targetId)
	{
		List<DamageEvent> list = (from e in Recent
			where e.TargetId != targetId
			orderby e.TimestampUtc
			select e).ToList();
		Recent.Clear();
		Skills.Clear();
		Targets.Clear();
		TotalDamage = 0L;
		TotalHealing = 0L;
		SelfHealing = 0L;
		OtherHealing = 0L;
		DamageEventCount = 0;
		HealingEventCount = 0;
		if (list.Count == 0)
		{
			FirstUtc = DateTime.UtcNow;
			LastUtc = FirstUtc;
			return;
		}
		foreach (DamageEvent item in list)
		{
			Apply(item);
		}
	}

	public void ApplyBuff(UiBuffEvent e)
	{
		BuffEvents.Enqueue(e);
	}

	public void TrimBuffEvents(int limit)
	{
		while (BuffEvents.Count > limit)
		{
			BuffEvents.Dequeue();
		}
	}

	public UiActorState CloneForTargetDetail(int targetId)
	{
		if (!Targets.TryGetValue(targetId, out UiActorTargetState value))
		{
			return new UiActorState(ActorId, FirstUtc);
		}
		UiActorState uiActorState = new UiActorState(ActorId, value.FirstUtc);
		uiActorState.FirstUtc = value.FirstUtc;
		uiActorState.LastUtc = value.LastUtc;
		uiActorState.TotalDamage = value.TotalDamage;
		uiActorState.TotalHealing = TotalHealing;
		uiActorState.SelfHealing = SelfHealing;
		uiActorState.OtherHealing = OtherHealing;
		uiActorState.DamageEventCount = value.DamageEventCount;
		uiActorState.HealingEventCount = HealingEventCount;
		uiActorState.Targets[targetId] = value.Clone();
		foreach (UiSkillState value3 in value.Skills.Values)
		{
			uiActorState.Skills[value3.SkillCode] = value3.CloneDamageStatsOnly();
		}
		foreach (UiSkillState item in Skills.Values.Where((UiSkillState s) => s.TotalHealing > 0))
		{
			if (!uiActorState.Skills.TryGetValue(item.SkillCode, out UiSkillState value2))
			{
				value2 = new UiSkillState(item.SkillCode);
				uiActorState.Skills[item.SkillCode] = value2;
			}
			value2.MergeHealingFrom(item);
		}
		return uiActorState;
	}

	public void MergeAggregateFrom(UiActorState source, IReadOnlySet<int> aliasIds, int selectedActorId)
	{
		if (DamageEventCount == 0 || source.FirstUtc < FirstUtc)
		{
			FirstUtc = source.FirstUtc;
		}
		if (DamageEventCount == 0 || source.LastUtc > LastUtc)
		{
			LastUtc = source.LastUtc;
		}
		TotalDamage += source.TotalDamage;
		TotalHealing += source.TotalHealing;
		SelfHealing += source.SelfHealing;
		OtherHealing += source.OtherHealing;
		DamageEventCount += source.DamageEventCount;
		HealingEventCount += source.HealingEventCount;
		foreach (UiSkillState value3 in source.Skills.Values)
		{
			if (!Skills.TryGetValue(value3.SkillCode, out UiSkillState value))
			{
				value = new UiSkillState(value3.SkillCode);
				Skills[value3.SkillCode] = value;
			}
			value.MergeFrom(value3);
		}
		foreach (KeyValuePair<int, UiActorTargetState> target in source.Targets)
		{
			int num = (aliasIds.Contains(target.Key) ? selectedActorId : target.Key);
			if (!Targets.TryGetValue(num, out UiActorTargetState value2))
			{
				value2 = new UiActorTargetState(num, target.Value.FirstUtc);
				Targets[num] = value2;
			}
			value2.MergeFrom(target.Value);
		}
	}

	private bool IsSelfHealingEvent(DamageEvent e)
	{
		return IsDefaultSelfHealingEvent(e, ActorId);
	}

	private static bool IsDefaultSelfHealingEvent(DamageEvent e, int actorId)
	{
		if (e.TargetId <= 0 || e.TargetId != actorId)
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
