using System;
using System.Linq;
using INGMeter.Core;

namespace INGMeter.App;

public class UiSkillState
{
	private readonly DamageStatCounter _statCounter = new DamageStatCounter();

	public int SkillCode { get; }

	public long TotalDamage { get; private set; }

	public long TotalHealing { get; private set; }

	public long SelfHealing { get; private set; }

	public long OtherHealing { get; private set; }

	public int HitCount { get; private set; }

	public int HealCount { get; private set; }

	public int CritCount { get; private set; }

	public int BackCount { get; private set; }

	public int DoubleCount { get; private set; }

	public int PerfectCount { get; private set; }

	public int ParryCount { get; private set; }

	public int EvadeCount { get; private set; }

	public int SmiteCount { get; private set; }

	public int MultiEventCount { get; private set; }

	public int NormalHitCount { get; private set; }

	public int MinDamage { get; private set; } = int.MaxValue;

	public int MaxDamage { get; private set; }

	public int MinHeal { get; private set; } = int.MaxValue;

	public int MaxHeal { get; private set; }

	public int SkillLevel { get; private set; }

	public int BaseSkillLevel { get; private set; }

	public UiSkillState(int skillCode)
	{
		SkillCode = skillCode;
	}

	public static UiSkillState FromSkillStats(SkillStats stats)
	{
		return new UiSkillState(stats.SkillCode)
		{
			TotalDamage = stats.TotalDamage,
			TotalHealing = stats.TotalHealing,
			SelfHealing = stats.SelfHealing,
			OtherHealing = stats.OtherHealing,
			HitCount = stats.HitCount,
			HealCount = stats.HealCount,
			CritCount = stats.CritCount,
			BackCount = stats.BackCount,
			DoubleCount = stats.DoubleCount,
			PerfectCount = stats.PerfectCount,
			ParryCount = stats.ParryCount,
			EvadeCount = stats.EvadeCount,
			SmiteCount = stats.SmiteCount,
			MultiEventCount = stats.MultiEventCount,
			NormalHitCount = stats.NormalHitCount,
			MinDamage = ((stats.MinDamage > 0) ? stats.MinDamage : int.MaxValue),
			MaxDamage = stats.MaxDamage,
			MinHeal = ((stats.MinHeal > 0) ? stats.MinHeal : int.MaxValue),
			MaxHeal = stats.MaxHeal,
			SkillLevel = stats.SkillLevel,
			BaseSkillLevel = stats.BaseSkillLevel
		};
	}

	public UiSkillState CloneDamageStatsOnly()
	{
		UiSkillState uiSkillState = new UiSkillState(SkillCode);
		uiSkillState.MergeDamageStatsFrom(this);
		return uiSkillState;
	}

	public void MergeDamageStatsFrom(UiSkillState other)
	{
		TotalDamage += other.TotalDamage;
		HitCount += other.HitCount;
		CritCount += other.CritCount;
		BackCount += other.BackCount;
		DoubleCount += other.DoubleCount;
		PerfectCount += other.PerfectCount;
		ParryCount += other.ParryCount;
		EvadeCount += other.EvadeCount;
		SmiteCount += other.SmiteCount;
		MultiEventCount += other.MultiEventCount;
		NormalHitCount += other.NormalHitCount;
		if (other.MinDamage < MinDamage)
		{
			MinDamage = other.MinDamage;
		}
		if (other.MaxDamage > MaxDamage)
		{
			MaxDamage = other.MaxDamage;
		}
		if (other.SkillLevel > 0)
		{
			SkillLevel = other.SkillLevel;
		}
		if (other.BaseSkillLevel > 0)
		{
			BaseSkillLevel = other.BaseSkillLevel;
		}
	}

	public void MergeHealingFrom(UiSkillState other)
	{
		TotalHealing += other.TotalHealing;
		SelfHealing += other.SelfHealing;
		OtherHealing += other.OtherHealing;
		HealCount += other.HealCount;
		if (other.MinHeal < MinHeal)
		{
			MinHeal = other.MinHeal;
		}
		if (other.MaxHeal > MaxHeal)
		{
			MaxHeal = other.MaxHeal;
		}
		if (other.SkillLevel > 0)
		{
			SkillLevel = other.SkillLevel;
		}
		if (other.BaseSkillLevel > 0)
		{
			BaseSkillLevel = other.BaseSkillLevel;
		}
	}

	public void MergeFrom(UiSkillState other)
	{
		MergeDamageStatsFrom(other);
		MergeHealingFrom(other);
	}

	public void Apply(DamageEvent e)
	{
		Apply(e, IsDefaultSelfHealingEvent(e));
	}

	public void Apply(DamageEvent e, bool isSelfHealing)
	{
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
			HealCount++;
			if (e.HealAmount < MinHeal)
			{
				MinHeal = e.HealAmount;
			}
			if (e.HealAmount > MaxHeal)
			{
				MaxHeal = e.HealAmount;
			}
		}
		if (e.SkillLevel > 0)
		{
			SkillLevel = e.SkillLevel;
		}
		if (e.BaseSkillLevel > 0)
		{
			BaseSkillLevel = e.BaseSkillLevel;
		}
		if (damage > 0)
		{
			if (damage < MinDamage)
			{
				MinDamage = damage;
			}
			if (damage > MaxDamage)
			{
				MaxDamage = damage;
			}
		}
		DamageStatDecision damageStatDecision = _statCounter.Record(e);
		if (damageStatDecision.RetroactivePlainHitsToRemove > 0)
		{
			int num = Math.Min(damageStatDecision.RetroactivePlainHitsToRemove, HitCount);
			HitCount -= num;
			NormalHitCount = Math.Max(0, NormalHitCount - num);
		}
		if (!damageStatDecision.CountForStats)
		{
			return;
		}
		HitCount++;
		if (e.IsCrit)
		{
			CritCount++;
		}
		bool flag = false;
		if (e.Specials != null)
		{
			if (e.Specials.Contains(SpecialDamage.BACK))
			{
				BackCount++;
				flag = true;
			}
			if (e.Specials.Contains(SpecialDamage.DOUBLE))
			{
				DoubleCount++;
				flag = true;
			}
			if (e.Specials.Contains(SpecialDamage.PERFECT))
			{
				PerfectCount++;
				flag = true;
			}
			if (e.Specials.Contains(SpecialDamage.PARRY))
			{
				ParryCount++;
				flag = true;
			}
			if (e.Specials.Contains(SpecialDamage.IMMUNE))
			{
				EvadeCount++;
				flag = true;
			}
			if (e.Specials.Contains(SpecialDamage.SMITE))
			{
				SmiteCount++;
				flag = true;
			}
		}
		if (!flag && IsNoDamageAvoidance(e))
		{
			EvadeCount++;
			flag = true;
		}
		if (!flag && !e.IsCrit)
		{
			NormalHitCount++;
		}
		if (e.MultiHitDamage > 0)
		{
			MultiEventCount++;
		}
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

	private static bool IsNoDamageAvoidance(DamageEvent e)
	{
		if (!e.IsDot && e.Damage <= 0 && e.MultiHitDamage <= 0 && e.HealAmount <= 0)
		{
			if (e.Specials != null)
			{
				return !e.Specials.Contains(SpecialDamage.PARRY);
			}
			return true;
		}
		return false;
	}
}
