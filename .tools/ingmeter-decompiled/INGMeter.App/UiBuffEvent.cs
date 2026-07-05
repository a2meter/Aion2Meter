using System;

namespace INGMeter.App;

public sealed record UiBuffEvent(DateTime TimestampUtc, string Kind, int ActorId, int TargetId, int OwnerId, int BuffId, int SkillId, uint DurationMs, ulong StartedAtMs, ulong ExpiresAtMs, int SkillLevel = 0, int BaseSkillLevel = 0);
