namespace INGMeter.App;

public class BuffUptimeRow
{
	public string IconPath { get; set; } = "";

	public string Name { get; set; } = "";

	public string LevelText { get; set; } = "";

	public int ApplyCount { get; set; }

	public double UptimeSeconds { get; set; }

	public string UptimeText { get; set; } = "";

	public string UptimePercentText { get; set; } = "";

	public double UptimePercentValue { get; set; }

	public bool IsConsumable { get; set; }

	public bool IsOwnSkill { get; set; }
}
