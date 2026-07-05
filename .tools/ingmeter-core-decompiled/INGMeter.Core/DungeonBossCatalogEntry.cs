namespace INGMeter.Core;

public sealed record DungeonBossCatalogEntry(int DungeonCode, string Category, string Difficulty, int? Stage, string Name)
{
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
					if (string.IsNullOrWhiteSpace(Category))
					{
						return $"{Name} {valueOrDefault}단계";
					}
					return $"{Category} {Name} {valueOrDefault}단계";
				}
			}
			if (!string.IsNullOrWhiteSpace(Difficulty))
			{
				if (!string.IsNullOrWhiteSpace(Category))
				{
					return $"{Category} {Difficulty} {Name}";
				}
				return Difficulty + " " + Name;
			}
			if (!string.IsNullOrWhiteSpace(Category))
			{
				return Category + " " + Name;
			}
			return Name;
		}
	}
}
