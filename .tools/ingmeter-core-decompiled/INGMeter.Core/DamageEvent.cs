using System;
using System.Collections.Generic;
using System.Linq;

namespace INGMeter.Core;

public sealed record DamageEvent(bool IsDot, int ActorId, int TargetId, int SkillCodeRaw, int Type, int Damage, int Flag, int SwitchVar, int Unknown, IReadOnlyList<SpecialDamage> Specials, DateTime TimestampUtc, int MultiHitCount = 0, int MultiHitDamage = 0, int HealAmount = 0, bool IsMonsterOrigin = false, byte[]? RawPacket = null, string? FilterReason = null)
{
	public bool IsCrit
	{
		get
		{
			if (Specials != null)
			{
				return Specials.Contains(SpecialDamage.CRITICAL);
			}
			return false;
		}
	}

	public int SkillLevel { get; init; }

	public int BaseSkillLevel { get; init; }
}
