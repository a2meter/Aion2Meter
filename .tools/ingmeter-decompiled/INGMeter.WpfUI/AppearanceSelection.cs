namespace INGMeter.WpfUI;

public readonly record struct AppearanceSelection(MeterPalette Palette, MeterSkin Skin)
{
	public static AppearanceSelection Default => new AppearanceSelection(MeterPalette.BlueMist, MeterSkin.Default);

	public string ResourceThemeName
	{
		get
		{
			if (Skin != MeterSkin.Default)
			{
				return Skin.ToString();
			}
			return Palette.ToString();
		}
	}
}
