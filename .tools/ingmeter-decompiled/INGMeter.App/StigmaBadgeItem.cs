using System.Globalization;

namespace INGMeter.App;

public sealed class StigmaBadgeItem
{
	public string Name { get; init; } = "";

	public int Level { get; init; }

	public string BackgroundBrush { get; init; } = "#1f2937";

	public string BorderBrush { get; init; } = "#374151";

	public string ForegroundBrush { get; init; } = "#e5e7eb";

	public string DisplayText => $"{Name} {Level}";

	public string LevelText => Level.ToString(CultureInfo.InvariantCulture);

	public string Tooltip => $"Lv{Level} {Name}";
}
