namespace INGMeter.App;

public class PacketLogEntry
{
	public string Time { get; set; } = "";

	public string Status { get; set; } = "";

	public string Kind { get; set; } = "";

	public string FilterReason { get; set; } = "";

	public bool IsExcluded { get; set; }

	public bool IsNative { get; set; }

	public string ActorName { get; set; } = "";

	public string TargetName { get; set; } = "";

	public string SkillCode { get; set; } = "";

	public string SkillName { get; set; } = "";

	public string Damage { get; set; } = "";

	public string MultiDamage { get; set; } = "";

	public string HealAmount { get; set; } = "";

	public string TypeInfo { get; set; } = "";

	public string Specials { get; set; } = "";

	public string IsDot { get; set; } = "";

	public string Flag { get; set; } = "";

	public byte[]? RawPacket { get; set; }

	public int SwitchVar { get; set; }

	public int RawSkillCode { get; set; }

	public int RawDamage { get; set; }

	public int RawMultiDamage { get; set; }

	public int RawHealAmount { get; set; }

	public string Detail { get; set; } = "";
}
