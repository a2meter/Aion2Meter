using System;

namespace INGMeter.Core;

public interface IRdpsSupportWindow
{
	int SkillId { get; }

	int LevelCode { get; }

	string SkillName { get; }

	double Percent { get; }

	double Multiplier { get; }

	string ExclusiveGroup { get; }

	RdpsEffectKind EffectKind { get; }

	int OwnerId { get; }

	int TargetId { get; }

	DateTime Start { get; }
}
