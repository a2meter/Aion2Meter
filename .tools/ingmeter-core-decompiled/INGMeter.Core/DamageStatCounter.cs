namespace INGMeter.Core;

public sealed class DamageStatCounter
{
	public DamageStatDecision Record(DamageEvent e)
	{
		if (e.IsDot)
		{
			return new DamageStatDecision(CountForStats: false, 0);
		}
		if (e.HealAmount > 0 && e.Damage <= 0 && e.MultiHitDamage <= 0)
		{
			return new DamageStatDecision(CountForStats: false, 0);
		}
		return new DamageStatDecision(CountForStats: true, 0);
	}
}
