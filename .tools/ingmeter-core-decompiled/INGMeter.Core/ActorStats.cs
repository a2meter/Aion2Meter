using System;
using System.Collections.Generic;

namespace INGMeter.Core;

public sealed record ActorStats(int ActorId, string Name, string ServerName, JobClass Job, long TotalDamage, double Dps, int Hits, int CritHits, double CritRate, int MultiEvents, bool IsMonster, DateTime FirstHitUtc = default(DateTime), DateTime LastHitUtc = default(DateTime), IReadOnlyList<SkillStats>? Skills = null, long TotalHealing = 0L, double Hps = 0.0, int HealHits = 0, long SelfHealing = 0L, long OtherHealing = 0L);
