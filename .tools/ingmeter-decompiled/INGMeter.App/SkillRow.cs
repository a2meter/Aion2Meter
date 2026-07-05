using System;
using System.Collections.Generic;

namespace INGMeter.App;

public class SkillRow
{
	public string IconPath { get; set; } = "";

	public string Name { get; set; } = "";

	public IReadOnlyList<TraitSlot> TraitSlots { get; set; } = Array.Empty<TraitSlot>();

	public string SkillCodesText { get; set; } = "";

	public int Hit { get; set; }

	public string Crit { get; set; } = "";

	public string Normal { get; set; } = "";

	public string Back { get; set; } = "";

	public string Double { get; set; } = "";

	public string Perfect { get; set; } = "";

	public string Parry { get; set; } = "";

	public string Smite { get; set; } = "";

	public string Evade { get; set; } = "";

	public string Multi { get; set; } = "";

	public string DPS { get; set; } = "";

	public string Min { get; set; } = "";

	public string Max { get; set; } = "";

	public string Avg { get; set; } = "";

	public string Damage { get; set; } = "";

	public string DamageWithShare { get; set; } = "";

	public string Heal { get; set; } = "";

	public string HealWithShare { get; set; } = "";

	public string SelfHeal { get; set; } = "";

	public string SelfHealWithShare { get; set; } = "";

	public string OtherHeal { get; set; } = "";

	public string OtherHealWithShare { get; set; } = "";

	public string Share { get; set; } = "";

	public string HealShare { get; set; } = "";

	public double ShareBarValue { get; set; }

	public string SpecialtyTooltip { get; set; } = "";

	public bool IsTotal { get; set; }

	public long RawDamage { get; set; }

	public long RawHealing { get; set; }

	public long RawSelfHealing { get; set; }

	public long RawOtherHealing { get; set; }
}
