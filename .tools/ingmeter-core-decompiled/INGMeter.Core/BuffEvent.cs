using System;

namespace INGMeter.Core;

public sealed record BuffEvent(DateTime TimestampUtc, string Kind, int TargetId, int OwnerId, int BuffId, int SkillId, uint DurationMs, ulong StartedAtMs, ulong ExpiresAtMs, int SkillLevel = 0, int BaseSkillLevel = 0);
