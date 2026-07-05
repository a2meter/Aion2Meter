namespace INGMeter.Core;

public sealed record SkillStats(int SkillCode, long TotalDamage, int HitCount, int CritCount, int NormalHitCount, int BackCount, int DoubleCount, int PerfectCount, int ParryCount, int MultiEventCount, int MaxDamage, int MinDamage, int SkillLevel = 0, int BaseSkillLevel = 0, int EvadeCount = 0, long TotalHealing = 0L, int HealCount = 0, int MaxHeal = 0, int MinHeal = 0, int SmiteCount = 0, long SelfHealing = 0L, long OtherHealing = 0L);
