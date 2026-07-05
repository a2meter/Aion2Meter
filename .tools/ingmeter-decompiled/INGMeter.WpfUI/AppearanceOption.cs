namespace INGMeter.WpfUI;

public sealed record AppearanceOption(string DisplayName, AppearanceSelection Selection)
{
	public string ThemeName => Selection.ResourceThemeName;
}
