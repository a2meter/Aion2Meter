namespace INGMeter.Core;

public sealed record RdpsPartyBuffEffect(int SkillId, string SkillName, string JobText, int LevelCode, int Level, double PveDamageAmpPercent, string ExclusiveGroup, RdpsEffectScope EffectScope, RdpsSourceRestriction SourceRestriction, RdpsEffectKind EffectKind, string Description)
{
	public double Multiplier => 1.0 + PveDamageAmpPercent / 100.0;
}
