using System;

namespace INGMeter.Core;

public sealed record StigmaSkillLevelEvent(DateTime TimestampUtc, int OwnerId, int SkillCode, int BaseSkillCode, int EffectiveLevel, int BaseSkillLevel);
