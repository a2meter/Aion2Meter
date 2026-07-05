namespace INGMeter.App;

public class DpsGraphRow
{
	public int Second { get; set; }

	public string TimeRange { get; set; } = "";

	public long Dps { get; set; }

	public long Damage { get; set; }

	public string DpsText { get; set; } = "";

	public string DamageText { get; set; } = "";

	public int HitCount { get; set; }
}
