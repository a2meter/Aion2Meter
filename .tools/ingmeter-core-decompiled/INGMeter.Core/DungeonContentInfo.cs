namespace INGMeter.Core;

public sealed record DungeonContentInfo
{
	public int Code { get; init; }

	public string Category { get; init; } = "";

	public string Name { get; init; } = "";

	public string? Difficulty { get; init; }

	public int? Stage { get; init; }

	public string DisplayName
	{
		get
		{
			int? stage = Stage;
			if (stage.HasValue)
			{
				int valueOrDefault = stage.GetValueOrDefault();
				if (valueOrDefault > 0)
				{
					return $"{Category} {Name} {valueOrDefault}단계";
				}
			}
			if (!string.IsNullOrWhiteSpace(Difficulty))
			{
				return $"{Category} {Difficulty} {Name}";
			}
			if (!string.IsNullOrWhiteSpace(Category))
			{
				return Category + " " + Name;
			}
			return Name;
		}
	}
}
