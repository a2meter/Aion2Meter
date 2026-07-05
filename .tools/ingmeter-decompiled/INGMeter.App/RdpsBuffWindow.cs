using System;
using INGMeter.Core;

namespace INGMeter.App;

internal sealed record RdpsBuffWindow(int SkillId, int LevelCode, string SkillName, int Level, int DisplaySkillLevel, double Percent, double Multiplier, string ExclusiveGroup, RdpsEffectScope EffectScope, RdpsSourceRestriction SourceRestriction, RdpsEffectKind EffectKind, int OwnerId, int TargetId, string ProviderName, string TargetName, string IconPath, string Description, DateTime Start, DateTime End) : IRdpsSupportWindow
{
	public RdpsBuffRowKey RowKey => new RdpsBuffRowKey(EffectScope, SkillId, LevelCode, OwnerId, TargetId);
}
