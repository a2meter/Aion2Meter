using System;

namespace INGMeter.Core;

public sealed record TargetInfo(int TargetId, string Name, long TotalDamage, DateTime FirstHit, DateTime LastHit, bool IsBoss, int MobCode = 0, int MaxHp = 0);
