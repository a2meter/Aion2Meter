namespace INGMeter.WpfUI;

public sealed record MeterSkinProfile(MeterSkin Skin, bool UsesBloomLayoutFamily, bool UsesSoftDecoration, bool UsesNeonDecoration)
{
	public bool IsDefault => Skin == MeterSkin.Default;

	public bool IsAbyss => Skin == MeterSkin.Abyss;

	public bool IsAetherVeil => Skin == MeterSkin.AetherVeil;

	public bool UsesDecorativeSubTextBadge
	{
		get
		{
			MeterSkin skin = Skin;
			if ((uint)(skin - 1) <= 2u)
			{
				return true;
			}
			return false;
		}
	}

	public bool UsesAbyssDamageShareText => Skin == MeterSkin.Abyss;

	public bool UsesAetherVeilDamageShareText => Skin == MeterSkin.AetherVeil;
}
