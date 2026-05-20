using System.Collections.Generic;

namespace A2Meter.Dps;

/// Wire shape sent to the JS UI as { type:"dps-update", data:<this> }.
/// Matches the original A2Viewer/A2Power schema so existing web assets render unchanged.
internal sealed class DpsSnapshot
{
    public double ElapsedSeconds { get; set; }
    public double WallElapsedSeconds { get; set; }
    public long   TotalPartyDamage { get; set; }
    public MobTarget? Target { get; set; }
    public List<ActorDps> Players { get; set; } = new();
}

internal sealed class ActorDps
{
    public int    EntityId { get; set; }
    public string Name { get; set; } = "";
    public int    JobCode { get; set; } = -1;
    public int    ServerId { get; set; }
    public string ServerName { get; set; } = "";
    public int    CombatScore { get; set; }
    public int    CombatPower { get; set; }
    public long   TotalDamage { get; set; }
    public long   Dps { get; set; }
    public long   PartyDps { get; set; }
    public long   WallDps { get; set; }
    public double DamagePercent { get; set; }
    public double BossHpPercent { get; set; }
    public double CritRate { get; set; }
    public long   HealTotal { get; set; }
    public bool   IsUploader { get; set; }
    public long   Hits { get; set; }
    public long   DotDamage { get; set; }
    public List<SkillDps>? TopSkills { get; set; }
    public Dictionary<string, int>? SkillLevels { get; set; }
    public List<BuffUptimeDto>? Buffs { get; set; }
}

internal sealed class SkillDps
{
    public string Name { get; set; } = "";
    public long   Total { get; set; }
    public long   Hits { get; set; }
    public long   MaxHit { get; set; }
    public double CritRate { get; set; }
    public double BackRate { get; set; }      // 후방
    public double StrongRate { get; set; }    // 강타
    public double PerfectRate { get; set; }   // 완벽
    public double MultiHitRate { get; set; }  // 다단
    public double DodgeRate { get; set; }     // 회피
    public double BlockRate { get; set; }     // 막기
    public int[]? Specs { get; set; }         // 특화 tiers (1-based, sorted ascending)
    public List<long>? HitLog { get; set; }   // per-hit damage values (regular + DoT ticks)
}

internal sealed class MobTarget
{
    public int    EntityId { get; set; }
    public string Name { get; set; } = "";
    public long   MaxHp { get; set; }
    public long   CurrentHp { get; set; }
    public long   TotalDamageReceived { get; set; }
    public bool   IsBoss { get; set; }

    // ── Per-mob tracking state (matches A2Power MobInfo) ──
    /// True when self has dealt at least one hit to this mob.
    public bool   HasSelfParticipation { get; set; }
    /// UTC timestamp of the last self hit on this mob.
    public DateTime LastSelfHitAt { get; set; } = DateTime.MinValue;
    /// True when the mob death has been confirmed (HP=0 or entity removed).
    public bool   DeathConfirmed { get; set; }

    // ── HP sample tracking for cumulative damage death detection (A2Power) ──
    /// Last server-reported HP value from BossHp packet.
    public long   HpAtLastSample { get; set; }
    /// TotalDamageReceived at the time HpAtLastSample was recorded.
    public long   DamageAtLastHpSample { get; set; }
    /// First BossHp sample below MaxHp (used for MaxHp correction).
    public long   FirstBossHpSample { get; set; }
    /// Whether FirstBossHpSample has been set this session.
    public bool   FirstBossHpSet { get; set; }
}

/// Party roster member — schema matches the original A2Viewer.Packet.PartyMember
/// so the protocol parser can populate it directly.
internal sealed class PartyMember
{
    public uint   CharacterId { get; set; }
    public int    ServerId { get; set; }
    public string ServerName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public int    JobCode { get; set; }
    public string JobName { get; set; } = "";
    public int    Level { get; set; }
    public int    CombatPower { get; set; }
    /// True when this member is the local player (from UserInfo isSelf=1).
    public bool   IsSelf { get; set; }
    /// True when confirmed via actual party packet (PartyList/PartyUpdate/PartyAccept),
    /// not just seen nearby via UserInfo.
    public bool   IsPartyMember { get; set; }
    /// True when this member was detected via a character lookup packet (0x4F 0x36).
    public bool   IsLookup { get; set; }
}

/// JSON-friendly buff uptime DTO stored inside ActorDps / CombatRecord.
internal sealed class BuffUptimeDto
{
    public string Name { get; set; } = "";
    public int    BuffId { get; set; }
    public double Uptime { get; set; }
}

/// Equipment item extracted from character lookup packet (0x4F 0x36).
internal sealed class EquipmentItem
{
    public uint ItemId { get; set; }
    public int  EnchantLevel { get; set; }   // enchant + exceed combined
    public int  GemSlotCount { get; set; }
    public List<EquipmentStat>? SubStats { get; set; }
}

/// Sub-stat entry on an equipment item.
internal sealed class EquipmentStat
{
    public int StatId { get; set; }
    public int Value { get; set; }   // integer stats: raw value; percentages: ×100 (15.9% → 1591)
}

/// Per-second DPS snapshot for timeline replay (matches A2Power TimelineEntry).
internal sealed class TimelineEntry
{
    public int T { get; set; }
    public List<TimelinePlayer> Players { get; set; } = new();
}

internal sealed class TimelinePlayer
{
    public int  EntityId { get; set; }
    public long Damage { get; set; }
    public long Dps { get; set; }
}

/// Per-hit detail log for combat replay (matches A2Power HitLogEntry).
internal sealed class HitLogEntry
{
    public double  T { get; set; }
    public int     EntityId { get; set; }
    public string  SkillName { get; set; } = "";
    public string? SkillIcon { get; set; }
    public string? SkillType { get; set; }
    public long    Damage { get; set; }
    public uint    Flags { get; set; }
}
