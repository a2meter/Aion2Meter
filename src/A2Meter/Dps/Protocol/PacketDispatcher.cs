using System;
using System.Collections.Generic;
using System.Text;

namespace A2Meter.Dps.Protocol;

/// Self-contained C# port of A2Viewer.Packet.PacketDispatcher.
/// All `_owner.FireXxx` calls in the original become events on this class.
/// All native engine paths and diagnostic trace plumbing are dropped.
internal sealed class PacketDispatcher
{
    // Tag bytes used by the original to anchor specific message types.
    private static byte TAG_DAMAGE_1 => ProtocolOpcodeConfig.Damage.A;
    private static byte TAG_DAMAGE_2 => ProtocolOpcodeConfig.Damage.B;
    private static byte TAG_DOT_1 => ProtocolOpcodeConfig.Dot.A;
    private static byte TAG_DOT_2 => ProtocolOpcodeConfig.Dot.B;
    private static byte TAG_BATTLE_STATS_1 => ProtocolOpcodeConfig.BattleStats.A;
    private static byte TAG_BATTLE_STATS_2 => ProtocolOpcodeConfig.BattleStats.B;
    private static byte TAG_BATTLE_STATS_ALT_1 => ProtocolOpcodeConfig.BattleStatsAlt.A;
    private static byte TAG_SELF_INFO_1 => ProtocolOpcodeConfig.SelfInfo.A;
    private static byte TAG_SELF_INFO_2 => ProtocolOpcodeConfig.SelfInfo.B;
    private static byte TAG_OTHER_INFO_1 => ProtocolOpcodeConfig.OtherInfo.A;
    private static byte TAG_OTHER_INFO_2 => ProtocolOpcodeConfig.OtherInfo.B;
    private static byte TAG_MOB_SPAWN_1 => ProtocolOpcodeConfig.MobSpawn.A;
    private static byte TAG_MOB_SPAWN_2 => ProtocolOpcodeConfig.MobSpawn.B;
    private static byte TAG_GUARD_1 => ProtocolOpcodeConfig.Guard.A;
    private static byte TAG_GUARD_2 => ProtocolOpcodeConfig.Guard.B;
    private static byte TAG_ENTITY_REMOVED_1 => ProtocolOpcodeConfig.EntityRemoved.A;
    private static byte TAG_ENTITY_REMOVED_2 => ProtocolOpcodeConfig.EntityRemoved.B;
    private static byte TAG_CHAR_LOOKUP_1 => ProtocolOpcodeConfig.CharLookup.A;
    private static byte TAG_CHAR_LOOKUP_2 => ProtocolOpcodeConfig.CharLookup.B;

    private const uint SENTINEL_SKILL_CODE = 12_250_030u;
    private const int  MIN_PACKET_LENGTH   = 4;
    private const int  IGNORED_PACKET_LENGTH = 11;
    private const int  MOB_CODE_SEARCH_RANGE = 60;
    private const int  MOB_HP_SEARCH_RANGE   = 67;
    private const int  MAX_NAME_LENGTH       = 72;
    private const int  NAME_SCAN_WINDOW      = 10;

    private static readonly int[]  CategoryTrailingSize = new int[8] { 0, 0, 0, 0, 8, 12, 10, 14 };
    private static readonly byte[] SummonBoundaryMarker = new byte[] { 255, 255, 255, 255, 255, 255, 255, 255 };
    private static readonly byte[] SummonActorHeader    = new byte[] { 7, 2, 6 };
    private static readonly byte[] CP_PACKET_MARKER     = new byte[] { 0, 146 };
    private static readonly byte[] EntityCpPacketMarker = new byte[] { 47, 42, 56 };
    private static readonly byte[] EntityCpTailMarker   = new byte[] { 14, 85, 54 };

    private readonly SkillDatabase _skillDb;
    private readonly Action<string>? _logSink;
    private bool _dumpUnparsed;

    public HashSet<int>? ValidServerIds { get; set; }
    public Func<int, string?>? GetServerName { get; set; }

    // Originals: PacketProcessor.FireXxx events.
    public event Action<int, int, int, byte, int, uint, int, int, int, int>? Damage;
        // (actorId, targetId, skillCode, damageType, damage, specialFlags,
        //  multiHitCount, multiHitDamage, healAmount, isDot)
    public event Action<int, int, int, int>? MobSpawn;       // (mobId, mobCode, hp, isBoss)
    public event Action<int, int>?           Summon;         // (actorId, petId)
    public event Action<int, string, int, int, int>? UserInfo;
        // (entityId, nickname, serverId, jobCode, isSelf)
    public event Action<int, int>?           CombatPower;        // (entityId, cp)
    public event Action<string, int, int>?   CombatPowerByName;  // (nick, serverId, cp)
    public event Action<int>?                EntityRemoved;
    public event Action<int, int>?           BossHp;             // (entityId, currentHp)
    public event Action<int, int, int, uint, long, int>? Buff;
        // (entityId, buffId, type, durationMs, timestamp, casterId)
#pragma warning disable CS0067 // Compatibility events whose packet forms are native-only for now.
    public event Action<int, int, uint, long, int>? BuffRefresh;
        // (entityId, buffId, durationMs, timestamp, casterId)
    public event Action<int, int>?           CombatState;
    public event Action<int, uint>?          RemainHp;
    public event Action<int, uint, uint, int>? NpcGroggy;
    public event Action<int, int, int>?      TargetOn;
    public event Action<int, int>?           TargetOff;
    public event Action<uint>?               ZoneMove;
#pragma warning restore CS0067
    public event Action<int, string, int, int, int, int>? CharacterLookup;
        // (entityId, nickname, serverId, jobCode, level, combatPower)
    public event Action<int, List<EquipmentItem>>? CharacterEquipment;
        // (entityId, items)

    public PacketDispatcher(SkillDatabase skillDb, Action<string>? logSink = null)
    {
        _skillDb = skillDb;
        _logSink = logSink;
    }

    public void EnableUnparsedDump(bool enabled) => _dumpUnparsed = enabled;

    public void Dispatch(byte[] data, int offset, int length)
    {
        if (length < MIN_PACKET_LENGTH || length == IGNORED_PACKET_LENGTH) return;

        int p = offset;
        int limit = offset + length;
        ProtocolUtils.ReadVarint(data, ref p, limit);
        int afterVarint = p - offset;
        if (afterVarint <= 0) afterVarint = 1;

        // The original ignores frames whose tag immediately following the varint
        // is the "guard" tag (0x03 0x36) — that branch never holds combat data.
        if (afterVarint >= length - 1 ||
            data[offset + afterVarint] != TAG_GUARD_1 ||
            data[offset + afterVarint + 1] != TAG_GUARD_2)
        {
            bool any = false;
            any |= TryParseUserInfo(data, offset, length);
            any |= TryScanCombatPower(data, offset, length);
            any |= TryScanEntityCombatPower(data, offset, length);
            any |= TryParseEntityRemoved(data, offset, length);
            if (afterVarint > 0 && afterVarint + 1 < length &&
                data[offset + afterVarint] == TAG_MOB_SPAWN_1 &&
                data[offset + afterVarint + 1] == TAG_MOB_SPAWN_2)
            {
                any |= TryParseMobInfo(data, offset, length, afterVarint + 2);
                any |= TryParseSummon (data, offset, length, afterVarint + 2);
            }
            any |= HasEntityMarker(data, offset, length);
            any |= TryParseDamage(data, offset, length);
            any |= TryParseDot   (data, offset, length);
            any |= TryParseBossHp(data, offset, length);
            any |= TryParseBattleStats(data, offset, length);
            any |= TryParseCharacterLookup(data, offset, length);

            if (_dumpUnparsed && !any && length > MIN_PACKET_LENGTH)
            {
                byte t1 = (byte)(afterVarint     < length ? data[offset + afterVarint]     : 0);
                byte t2 = (byte)(afterVarint + 1 < length ? data[offset + afterVarint + 1] : 0);
                _logSink?.Invoke($"[UNPARSE] tag=0x{t1:X2}{t2:X2} len={length} hex={HexDump(data, offset, Math.Min(length, 80))}");
            }
        }
    }

    // ─── damage ──────────────────────────────────────────────────────────

    public void DispatchSupplemental(byte[] data, int offset, int length)
    {
        if (length < MIN_PACKET_LENGTH || length == IGNORED_PACKET_LENGTH) return;

        int p = offset;
        int limit = offset + length;
        ProtocolUtils.ReadVarint(data, ref p, limit);
        int afterVarint = p - offset;
        if (afterVarint <= 0) afterVarint = 1;

        if (afterVarint < length - 1 &&
            data[offset + afterVarint] == TAG_GUARD_1 &&
            data[offset + afterVarint + 1] == TAG_GUARD_2)
        {
            return;
        }

        if (afterVarint > 0 && afterVarint + 1 < length &&
            data[offset + afterVarint] == TAG_MOB_SPAWN_1 &&
            data[offset + afterVarint + 1] == TAG_MOB_SPAWN_2)
        {
            TryParseMobInfo(data, offset, length, afterVarint + 2);
            TryParseSummon(data, offset, length, afterVarint + 2);
        }
        TryParseCharacterLookup(data, offset, length);
    }

    private bool TryParseDamage(byte[] data, int offset, int length)
    {
        int end = offset + length;
        int p   = LocateDamageHeader(data, offset, end);
        if (p < 0) return false;

        uint targetId = ProtocolUtils.ReadVarint(data, ref p, end);
        if (targetId == uint.MaxValue || p >= end) return false;

        uint flags1 = ProtocolUtils.ReadVarint(data, ref p, end);
        if (flags1 == uint.MaxValue) return false;

        uint category = flags1 & 0xF;
        if (category < 4 || category > 7) return true;

        SkipVarint(data, ref p, end);
        if (p >= end) return false;

        uint actorId = ProtocolUtils.ReadVarint(data, ref p, end);
        if (actorId == uint.MaxValue) return false;
        if (actorId == targetId) return true;
        if (end - p < 5) return false;

        int skillCode = _skillDb.ResolveFromPacketBytes(data, ref p, end);
        if (skillCode == 0) return false;

        uint damageType = ProtocolUtils.ReadVarint(data, ref p, end);
        if (damageType == uint.MaxValue) return false;

        int trailing = CategoryTrailingSize[category];
        byte flagByte = 0;
        int  flagAdjust = 0;
        if (end - p > 1 && (p + 2 >= end || data[p + 1] == 0))
        {
            flagByte = data[p];
            p += 2;
            flagAdjust = 1;
        }
        uint specialFlags = ComputeDamageFlags(flagByte, (byte)damageType);
        int trailerLen = trailing - flagAdjust * 2;
        if (trailerLen > 0 && p + trailerLen <= end) p += trailerLen;

        if (end - p < 1) return false;
        SkipVarint(data, ref p, end);
        uint damage = ProtocolUtils.ReadVarint(data, ref p, end);
        if (damage == uint.MaxValue) damage = 0;

        int multiHitCount = 0, multiHitDamage = 0;
        if (end - p >= 2)
        {
            var (count, dmg, endPos) = TryParseMultiHit(data, p, end, damage);
            if (count > 0 && dmg > 0)
            {
                multiHitCount  = count;
                multiHitDamage = dmg;
                int reduced = (int)damage - dmg;
                damage = reduced > 0 ? (uint)reduced : 0;
                p = endPos;
            }
        }

        uint healAmount = 0;
        if (end - p > 1 && data[p] == 3 && (p + 1 >= end || data[p + 1] == 0))
        {
            p += 2;
            if (p < end)
            {
                healAmount = ProtocolUtils.ReadVarint(data, ref p, end);
                if (healAmount == uint.MaxValue) healAmount = 0;
            }
        }

        Damage?.Invoke((int)actorId, (int)targetId, skillCode, (byte)(damageType & 0xFF),
                       (int)damage, specialFlags, multiHitCount, multiHitDamage, (int)healAmount, 0);
        return true;
    }

    private bool TryParseDot(byte[] data, int offset, int length)
    {
        int end = offset + length;
        int p = offset;
        if (!SkipVarintAndCheckTag(data, ref p, end, TAG_DOT_1, TAG_DOT_2)) return false;

        uint targetId = ProtocolUtils.ReadVarint(data, ref p, end);
        if (targetId == uint.MaxValue || p >= end) return false;

        bool hasExtra = (data[p++] & 2) != 0;

        uint actorId = ProtocolUtils.ReadVarint(data, ref p, end);
        if (actorId == uint.MaxValue || p >= end) return false;

        uint healAmount = ProtocolUtils.ReadVarint(data, ref p, end);
        if (healAmount == uint.MaxValue) return false;

        if (p + 4 > end) return false;
        uint rawMobCode = (uint)(data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24));
        p += 4;

        uint skillCode = DeriveSkillCodeFromMobCode(rawMobCode);
        uint damage = 0;
        if (hasExtra)
        {
            damage = ProtocolUtils.ReadVarint(data, ref p, end);
            if (damage == uint.MaxValue) return false;
        }

        if (!IsRecoveryMob(skillCode))
        {
            if (!hasExtra || skillCode == SENTINEL_SKILL_CODE) damage = 0;
            if (actorId == targetId) return true;
        }

        Damage?.Invoke((int)actorId, (int)targetId, (int)skillCode, 0, (int)damage, 0u,
                       0, 0, (int)healAmount, hasExtra ? 1 : 0);
        return true;
    }

    private uint DeriveSkillCodeFromMobCode(uint rawMobCode)
    {
        uint candidate = rawMobCode / 1000;
        if (_skillDb.IsMobBoss((int)candidate) && SkillDatabase.IsSkillCodeInRange((int)candidate))
            return candidate;
        return rawMobCode / 100;
    }

    private bool IsRecoveryMob(uint skillCode)
    {
        var name = _skillDb.GetSkillName((int)skillCode);
        if (string.IsNullOrEmpty(name)) return false;
        var lower = name.ToLowerInvariant();
        return lower.Contains("recuperation") || lower.Contains("recovery") || lower.Contains("restoration");
    }

    // ─── mob spawn / summon ──────────────────────────────────────────────

    private bool TryParseMobInfo(byte[] data, int offset, int length, int relativeStart)
    {
        int end = offset + length;
        int p   = offset + relativeStart;
        if (p >= end) return false;

        uint mobEntityId = ProtocolUtils.ReadVarint(data, ref p, end);
        if (mobEntityId == uint.MaxValue) return false;

        int rel  = p - offset;
        int lim  = Math.Min(rel + MOB_CODE_SEARCH_RANGE, length - 2);
        int relMobCode = ScanMobCodeMarker(data, offset, rel, lim, end);
        if (relMobCode < 0) return false;

        int relMcode = relMobCode - 2;
        if (rel > relMcode - 3) return false;

        int absMcode = offset + relMcode - 3;
        if (absMcode < offset || absMcode + 3 > end) return false;
        uint mobCode = (uint)(data[absMcode] | (data[absMcode + 1] << 8) | (data[absMcode + 2] << 16));
        int isBoss = _skillDb.IsMobBoss((int)mobCode) ? 1 : 0;
        MobSpawn?.Invoke((int)mobEntityId, (int)mobCode, 0, isBoss);

        int hpScanFrom = relMcode + 3;
        int hpScanTo   = Math.Min(relMcode + MOB_HP_SEARCH_RANGE, length - 2);
        for (int i = hpScanFrom; i < hpScanTo; i++)
        {
            int abs = offset + i;
            if (abs >= end) break;
            if (data[abs] != 1) continue;

            int p2 = abs + 1;
            if (p2 >= end) break;

            uint hpMax = ProtocolUtils.ReadVarint(data, ref p2, end);
            if (hpMax == uint.MaxValue || hpMax == 0 || p2 >= end) continue;

            uint hpCur = ProtocolUtils.ReadVarint(data, ref p2, end);
            if (hpCur != uint.MaxValue)
            {
                if ((int)hpCur >= (int)hpMax)
                    MobSpawn?.Invoke((int)mobEntityId, (int)mobCode, (int)hpCur, isBoss);
                break;
            }
        }
        return true;
    }

    private static int ScanMobCodeMarker(byte[] data, int offset, int from, int limit, int end)
    {
        for (int i = from; i < limit; i++)
        {
            int abs = offset + i + 2;
            if (abs < end && abs >= offset + 2 && data[abs - 2] == 0 && (data[abs - 1] & 0xBF) == 0 && data[abs] == 2)
                return i + 2;
        }
        return -1;
    }

    private bool TryParseSummon(byte[] data, int offset, int length, int relativeStart)
    {
        int end = offset + length;
        int p   = offset + relativeStart;
        if (p >= end) return false;

        uint petId = ProtocolUtils.ReadVarint(data, ref p, end);
        if (petId == uint.MaxValue) return false;

        var span = new ReadOnlySpan<byte>(data, offset, length);
        bool result = false;
        int cursor = 0;
        while (cursor < length)
        {
            int boundary = span.Slice(cursor).IndexOf(SummonBoundaryMarker);
            if (boundary == -1) break;
            boundary += cursor;
            int after = boundary + 8;
            if (after >= length) break;

            int hdr = span.Slice(after).IndexOf(SummonActorHeader);
            if (hdr == -1) { cursor = after; continue; }

            int actorPos = after + hdr;
            if (actorPos + 5 > length) break;
            ushort actorId = (ushort)(data[offset + actorPos + 3] | (data[offset + actorPos + 4] << 8));
            if (actorId > 99)
            {
                Summon?.Invoke(actorId, (int)petId);
                result = true;
                break;
            }
            cursor = boundary + 1;
        }
        return result;
    }

    // ─── battle stats / buffs ────────────────────────────────────────────

    private bool TryParseBattleStats(byte[] data, int offset, int length)
    {
        int end = offset + length;
        int idx = IndexOfTag(data, offset, end, TAG_BATTLE_STATS_1, TAG_BATTLE_STATS_2);
        if (idx < 0) idx = IndexOfTag(data, offset, end, TAG_BATTLE_STATS_ALT_1, TAG_BATTLE_STATS_2);
        if (idx < 0) return false;

        int p = idx + 2;
        if (p + 26 > end) return true;

        uint entityId = ProtocolUtils.ReadVarint(data, ref p, end);
        if (entityId == uint.MaxValue || p >= end) return true;
        if (p + 2 > end) return true;

        p++;
        byte type = data[p++];
        if (ProtocolUtils.ReadVarint(data, ref p, end) == uint.MaxValue || p >= end) return true;
        if (p + 4 > end) return true;

        int buffId = data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24);
        p += 4;
        if (!_skillDb.IsKnownBuffCode(buffId)) return true;

        if (p + 4 > end) return true;
        uint duration = (uint)(data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24));
        p += 4;
        if (duration != uint.MaxValue && duration < 100) return true;

        if (p + 4 > end) return true;
        p += 4;

        if (p + 8 > end) return true;
        long timestamp = BitConverter.ToInt64(data, p);
        p += 8;

        int casterId = 0;
        if (p < end)
        {
            uint c = ProtocolUtils.ReadVarint(data, ref p, end);
            if (c != uint.MaxValue) casterId = (int)c;
        }
        Buff?.Invoke((int)entityId, buffId, type, duration, timestamp, casterId);
        return true;
    }

    // ─── combat power ────────────────────────────────────────────────────

    private bool TryScanCombatPower(byte[] data, int offset, int length)
    {
        int end = offset + length;
        if (new ReadOnlySpan<byte>(data, offset, length).IndexOf(CP_PACKET_MARKER) < 0) return false;

        for (int i = end - 3; i >= offset; i--)
        {
            if (data[i] != 6 || data[i + 1] != 0 || data[i + 2] != 54) continue;
            int sync = i;
            if (sync - 21 < offset) break;

            bool zeros = true;
            for (int z = sync - 5; z < sync; z++) if (data[z] != 0) { zeros = false; break; }
            if (!zeros) continue;

            int cp = data[sync - 9]  | (data[sync - 8]  << 8) | (data[sync - 7]  << 16) | (data[sync - 6]  << 24);
            if (cp < 10000 || cp > 999999) continue;

            int level = data[sync - 13] | (data[sync - 12] << 8) | (data[sync - 11] << 16) | (data[sync - 10] << 24);
            if (level < 1000 || level > 5000) continue;
            if ((data[sync - 17] | (data[sync - 16] << 8) | (data[sync - 15] << 16) | (data[sync - 14] << 24)) != 0) continue;

            int classId = data[sync - 21] | (data[sync - 20] << 8) | (data[sync - 19] << 16) | (data[sync - 18] << 24);
            if (classId < 1 || classId > 55) continue;

            var found = FindNickAndServerInCpPacket(data, offset, sync);
            if (found.HasValue)
            {
                CombatPowerByName?.Invoke(found.Value.Nick, found.Value.ServerId, cp);
                return true;
            }
        }
        return false;
    }

    private bool TryScanEntityCombatPower(byte[] data, int offset, int length)
    {
        int end = offset + length;
        var span = new ReadOnlySpan<byte>(data, offset, length);
        int rel = span.IndexOf(EntityCpPacketMarker);
        while (rel >= 0)
        {
            int p = offset + rel + EntityCpPacketMarker.Length;
            uint entityId = ProtocolUtils.ReadVarint(data, ref p, end);
            if (entityId != uint.MaxValue && entityId != 0)
            {
                int tail = LastIndexOfPattern(data, p, end, EntityCpTailMarker);
                if (tail >= 0 && TryReadPackedCombatPower(data, tail + EntityCpTailMarker.Length, end, out int cp))
                {
                    CombatPower?.Invoke((int)entityId, cp);
                    return true;
                }
            }
            int next = rel + 1;
            if (next >= length) break;
            int more = span.Slice(next, span.Length - next).IndexOf(EntityCpPacketMarker);
            rel = more >= 0 ? next + more : -1;
        }
        return false;
    }

    private static bool TryReadPackedCombatPower(byte[] data, int p, int end, out int combatPower)
    {
        combatPower = 0;
        if (p >= end) return false;
        if (p + 4 <= end)
        {
            int v = data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24);
            if (v >= 10000 && v <= 999999) { combatPower = v; return true; }
        }
        if (p + 3 <= end)
        {
            int v = data[p] | (data[p + 1] << 8) | (data[p + 2] << 16);
            if (v >= 10000 && v <= 999999) { combatPower = v; return true; }
        }
        return false;
    }

    private static int LastIndexOfPattern(byte[] data, int from, int to, byte[] pattern)
    {
        for (int i = to - pattern.Length; i >= from; i--)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (data[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private static (string Nick, int ServerId)? FindNickAndServerInCpPacket(byte[] data, int from, int to)
    {
        for (int i = from; i + 3 < to; i++)
        {
            int sid = data[i] | (data[i + 1] << 8);
            if (sid < 1001 || sid > 2021) continue;

            int nameLen = data[i + 2];
            if (nameLen < 3 || nameLen > 48 || i + 3 + nameLen > to) continue;

            try
            {
                string name = Encoding.UTF8.GetString(data, i + 3, nameLen);
                if (string.IsNullOrEmpty(name) || name.Length < 2) continue;

                bool clean = true;
                foreach (char c in name)
                {
                    if ((c < '가' || c > '힣') && (c < 'a' || c > 'z') && (c < 'A' || c > 'Z') && (c < '0' || c > '9'))
                    { clean = false; break; }
                }
                if (!clean) continue;
                return (name, sid);
            }
            catch { }
        }
        return null;
    }

    // ─── boss hp / entity removal ────────────────────────────────────────

    private bool TryParseBossHp(byte[] data, int offset, int length)
    {
        int end = offset + length;
        for (int i = offset; i < end - 10; i++)
        {
            if (data[i] != ProtocolOpcodeConfig.BossHp.B) continue;
            int p = i + 1;
            uint entityId = ProtocolUtils.ReadVarint(data, ref p, end);
            if (entityId == uint.MaxValue || entityId == 0) continue;
            if (p + 7 > end) continue;
            if (data[p] != 2 || data[p + 1] != 1 || data[p + 2] != 0) continue;
            p += 3;

            int hp = data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24);
            p += 4;
            if (p + 4 > end) continue;
            if ((data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24)) != 0) continue;
            if (hp < 0) continue;

            BossHp?.Invoke((int)entityId, hp);
            RemainHp?.Invoke((int)entityId, (uint)hp);
            return true;
        }
        return false;
    }

    private bool TryParseEntityRemoved(byte[] data, int offset, int length)
    {
        int end = offset + length;
        int pos = offset;
        int consumed = 0;
        int shift = 0;
        while (pos < end)
        {
            byte b = data[pos++];
            consumed++;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 31 || pos >= end) return false;
        }
        int tagPos = offset + consumed;
        if (tagPos + 1 >= end) return false;
        if (data[tagPos] != TAG_ENTITY_REMOVED_1 || data[tagPos + 1] != TAG_ENTITY_REMOVED_2) return false;

        int p = tagPos + 2;
        if (p >= end) return false;
        uint entityId = ProtocolUtils.ReadVarint(data, ref p, end);
        if (entityId == uint.MaxValue) return false;

        EntityRemoved?.Invoke((int)entityId);
        TargetOff?.Invoke((int)entityId, 0);
        CombatState?.Invoke((int)entityId, 0);
        return true;
    }

    private static bool HasEntityMarker(byte[] data, int offset, int length)
    {
        int end = offset + length;
        if (length <= 0) return false;
        int pos = offset;
        int consumed = 0;
        int shift = 0;
        while (pos < end)
        {
            byte b = data[pos];
            int consumedNext = consumed + 1;
            if ((b & 0x80) == 0)
            {
                if (consumedNext > 0 &&
                    offset + consumed + 3 < end &&
                    offset + consumedNext < end - 1 &&
                    data[offset + consumedNext] == 0)
                {
                    return data[offset + consumedNext + 1] == 141;
                }
                return false;
            }
            shift += 7;
            if (shift > 31 || consumedNext >= length) return false;
            pos++;
            consumed = consumedNext;
        }
        return false;
    }

    // ─── user info ───────────────────────────────────────────────────────

    private bool TryParseUserInfo(byte[] data, int offset, int length)
    {
        int end = offset + length;

        // Self info first.
        int self = IndexOfTag(data, offset, end, TAG_SELF_INFO_1, TAG_SELF_INFO_2);
        if (self >= 0)
        {
            int p = self + 2;
            if (p < end)
            {
                uint entityId = ProtocolUtils.ReadVarint(data, ref p, end);
                if (entityId != uint.MaxValue)
                {
                    var nameTuple = ReadPlayerName(data, p, end);
                    if (nameTuple.HasValue)
                    {
                        int after = nameTuple.Value.AfterName;
                        int serverId = (after + 2 <= end) ? (data[after] | (data[after + 1] << 8)) : -1;
                        int jobCode  = (after + 3 <= end) ?  data[after + 2] : -1;
                        string nick = AppendServerName(nameTuple.Value.Name, serverId);
                        UserInfo?.Invoke((int)entityId, nick, serverId, jobCode, 1);
                        return true;
                    }

                    if (TryReadSelfInfoWithoutNameMarker(data, p, end, out var name, out var selfServerId, out var selfJobCode, out var extra))
                    {
                        string nick = AppendServerName(name, selfServerId);
                        UserInfo?.Invoke((int)entityId, nick, selfServerId, selfJobCode, extra == 1 ? 1 : 0);
                        return true;
                    }
                }
            }
        }

        // Other-player info.
        int other = IndexOfTag(data, offset, end, TAG_OTHER_INFO_1, TAG_OTHER_INFO_2);
        if (other < 0 && (TAG_OTHER_INFO_1 != 0x45 || TAG_OTHER_INFO_2 != 0x36))
            other = IndexOfTag(data, offset, end, 0x45, 0x36);
        if (other < 0) return false;

        int q = other + 2;
        if (q >= end) return false;
        uint entityId2 = ProtocolUtils.ReadVarint(data, ref q, end);
        if (entityId2 == uint.MaxValue) return false;

        if (q < end) ProtocolUtils.ReadVarint(data, ref q, end);
        if (q < end) ProtocolUtils.ReadVarint(data, ref q, end);

        var name2 = ReadPlayerName(data, q, end);
        if (!name2.HasValue) return false;
        int after2 = name2.Value.AfterName;

        int jobCode2 = -1;
        uint jc = ProtocolUtils.ReadVarint(data, ref after2, end);
        if (jc != uint.MaxValue) jobCode2 = (int)jc;

        int serverId2 = FindServerId(data, after2, end, ValidServerIds);
        string nick2 = AppendServerName(name2.Value.Name, serverId2);
        UserInfo?.Invoke((int)entityId2, nick2, serverId2, jobCode2, 0);
        return true;
    }

    private bool TryReadSelfInfoWithoutNameMarker(
        byte[] data,
        int from,
        int end,
        out string name,
        out int serverId,
        out int jobCode,
        out int extra)
    {
        name = "";
        serverId = -1;
        jobCode = -1;
        extra = 0;

        int limit = Math.Min(from + 16, end);
        for (int i = from; i < limit; i++)
        {
            int nameLen = data[i];
            if (nameLen < 1 || nameLen > MAX_NAME_LENGTH) continue;
            int nameStart = i + 1;
            int afterName = nameStart + nameLen;
            if (afterName + 7 > end) continue;

            string candidate = ProtocolUtils.DecodeGameString(data, nameStart, nameLen);
            if (string.IsNullOrEmpty(candidate) || ProtocolUtils.IsAllDigits(candidate)) continue;

            int sid = data[afterName] | (data[afterName + 1] << 8);
            if (!IsAllowedServerId(sid)) continue;

            int job = data[afterName + 2]
                | (data[afterName + 3] << 8)
                | (data[afterName + 4] << 16)
                | (data[afterName + 5] << 24);
            if (job <= 0 || job > 255) continue;

            int extraByte = data[afterName + 6];
            if (extraByte > 2) continue;

            name = candidate;
            serverId = sid;
            jobCode = job;
            extra = extraByte;
            return true;
        }

        return false;
    }

    private bool IsAllowedServerId(int serverId)
        => serverId > 0 && (ValidServerIds == null || ValidServerIds.Count == 0 || ValidServerIds.Contains(serverId));

    private static (string Name, int AfterName)? ReadPlayerName(byte[] data, int from, int end)
    {
        int limit = Math.Min(from + NAME_SCAN_WINDOW, end);
        for (int i = from; i < limit; i++)
        {
            if (data[i] != 7) continue;
            int p = i + 1;
            uint nameLen = ProtocolUtils.ReadVarint(data, ref p, end);
            if (nameLen == uint.MaxValue || nameLen < 1 || nameLen > MAX_NAME_LENGTH || p + (int)nameLen > end) return null;

            string name = ProtocolUtils.DecodeGameString(data, p, (int)nameLen);
            if (string.IsNullOrEmpty(name) || ProtocolUtils.IsAllDigits(name)) return null;
            return (name, p + (int)nameLen);
        }
        return null;
    }

    private static int FindServerId(byte[] data, int from, int end, HashSet<int>? validIds)
    {
        if (validIds == null || validIds.Count == 0) return -1;
        int scanStart = Math.Min(from + 75, end);
        int scanEnd   = Math.Min(from + 108, end) - 1;
        for (int i = scanStart; i < scanEnd; i++)
        {
            int id = data[i] | (data[i + 1] << 8);
            if (!validIds.Contains(id)) continue;
            for (int j = 6; j <= 12 && i + j + 1 < end; j++)
                if ((data[i + j] | (data[i + j + 1] << 8)) == id) return id;
            return id;
        }
        return -1;
    }

    private string AppendServerName(string name, int serverId)
    {
        if (serverId <= 0) return name;
        var sn = GetServerName?.Invoke(serverId);
        return string.IsNullOrEmpty(sn) ? name : $"{name}[{sn}]";
    }

    // ─── character lookup (캐릭터 조회) ────────────────────────────────

    private bool TryParseCharacterLookup(byte[] data, int offset, int length)
    {
        int end = offset + length;
        int idx = IndexOfTag(data, offset, end, TAG_CHAR_LOOKUP_1, TAG_CHAR_LOOKUP_2);
        if (idx < 0) return false;

        // tag(2) + padding(2) + nameMarker(0x07)
        int p = idx + 2 + 2;
        if (p >= end || data[p] != 7) return false;
        p++;

        // Name: varint length + UTF-8 bytes
        uint nameLen = ProtocolUtils.ReadVarint(data, ref p, end);
        if (nameLen == uint.MaxValue || nameLen < 1 || nameLen > MAX_NAME_LENGTH || p + (int)nameLen > end)
            return false;
        string name = ProtocolUtils.DecodeGameString(data, p, (int)nameLen);
        if (string.IsNullOrEmpty(name)) return false;
        p += (int)nameLen;

        // jobCode(1) + zeros(3) + 0x01(1) + isSelf(1) + level(1) + zeros(7) + entityId(4) + serverId(2)
        if (p + 20 > end) return false;
        int jobCode = data[p]; p++;
        p += 3; // zeros
        p++;    // 0x01
        p++; // local-like flag from lookup packet; not authoritative self info.
        int level = data[p]; p++;
        p += 7; // zeros
        int entityId = data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24); p += 4;
        int serverId = data[p] | (data[p + 1] << 8);

        // Combat power: at tail-9 position (marker 0x14 + LE32 + 5 trailing zeros)
        int combatPower = 0;
        int cpPos = offset + length - 9;
        if (cpPos > p && cpPos + 5 <= end && data[cpPos] == 0x14)
        {
            combatPower = data[cpPos + 1] | (data[cpPos + 2] << 8)
                        | (data[cpPos + 3] << 16) | (data[cpPos + 4] << 24);
        }

        string nick = AppendServerName(name, serverId);
        // CharacterLookup is an inspected/lookup identity packet. INGMeter does
        // not treat it as local-player proof even when this byte is 1.
        UserInfo?.Invoke(entityId, nick, serverId, jobCode, 0);
        CharacterLookup?.Invoke(entityId, nick, serverId, jobCode, level, combatPower);

        // Equipment scan: pattern [LE32 itemId (100M-900M)][9x 0x00][byte level 1-50]
        if (CharacterEquipment != null && length > 50)
        {
            var items = ScanEquipment(data, offset, length);
            if (items.Count > 0)
                CharacterEquipment.Invoke(entityId, items);
        }

        return true;
    }

    private static List<EquipmentItem> ScanEquipment(byte[] data, int offset, int length)
    {
        int pktEnd = offset + length;

        // Pass 1: locate all item positions.
        var positions = new List<int>();
        int scanEnd = pktEnd - 13;
        for (int i = offset; i < scanEnd; i++)
        {
            uint val = (uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24));
            if (val < 100_000_000u || val > 900_000_000u) continue;

            bool allZero = true;
            for (int j = i + 4; j < i + 13; j++)
            {
                if (data[j] != 0) { allZero = false; break; }
            }
            if (!allZero) continue;

            int enchLevel = data[i + 13];
            if (enchLevel < 1 || enchLevel > 50) continue;

            positions.Add(i);
            i += 13;
        }

        // Pass 2: for each item, determine block extent and parse sub-stats.
        var items = new List<EquipmentItem>();
        for (int idx = 0; idx < positions.Count; idx++)
        {
            int pos = positions[idx];
            int blockEnd = idx + 1 < positions.Count ? positions[idx + 1] : pktEnd;

            uint itemId = (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
            int enchLevel = data[pos + 13];

            var item = new EquipmentItem { ItemId = itemId, EnchantLevel = enchLevel };

            // Sub-stats: search for the stat count byte followed by valid stat entries.
            // Format: [count(1-12)] then count × [statId(LE16) value(LE16) 00 00]
            TryParseSubStats(data, pos + 14, blockEnd, item);

            items.Add(item);
        }
        return items;
    }

    private static void TryParseSubStats(byte[] data, int from, int to, EquipmentItem item)
    {
        // Scan for subStat block: a count byte (1-12) followed by count valid entries.
        // Each entry: [statId LE16 (1-1000)] [value LE16 (1-30000)] [00 00]
        // Heuristic: look for pattern with at least 3 consecutive valid entries.
        for (int p = from; p < to - 6; p++)
        {
            int count = data[p];
            if (count < 1 || count > 12) continue;

            int needed = count * 6;
            if (p + 1 + needed > to) continue;

            // Validate all entries.
            bool valid = true;
            int q = p + 1;
            for (int e = 0; e < count; e++)
            {
                int statId = data[q] | (data[q + 1] << 8);
                int value  = data[q + 2] | (data[q + 3] << 8);
                int pad1   = data[q + 4];
                int pad2   = data[q + 5];

                if (statId < 1 || statId > 1000 || value < 1 || value > 30000 || pad1 != 0 || pad2 != 0)
                { valid = false; break; }

                q += 6;
            }
            if (!valid || count < 3) continue;

            // Found valid subStat block.
            var stats = new List<EquipmentStat>(count + 2);
            q = p + 1;
            for (int e = 0; e < count; e++)
            {
                int statId = data[q] | (data[q + 1] << 8);
                int value  = data[q + 2] | (data[q + 3] << 8);
                stats.Add(new EquipmentStat { StatId = statId, Value = value });
                q += 6;
            }

            // Check for bonus stat: [00 00 00 01] [statId LE16] [value LE16]
            if (q + 8 <= to && data[q] == 0 && data[q + 1] == 0 && data[q + 2] == 0 && data[q + 3] == 1)
            {
                q += 4;
                int bonusId  = data[q] | (data[q + 1] << 8);
                int bonusVal = data[q + 2] | (data[q + 3] << 8);
                if (bonusId >= 1 && bonusId <= 1000 && bonusVal >= 1 && bonusVal <= 30000)
                    stats.Add(new EquipmentStat { StatId = bonusId, Value = bonusVal });
            }

            item.SubStats = stats;

            // Gem slot count: find pattern [count(1-8)] 04 BC 7A 1E in the block.
            for (int g = from; g < p - 4; g++)
            {
                if (data[g] >= 1 && data[g] <= 8
                    && data[g + 1] == 0x04 && data[g + 2] == 0xBC
                    && data[g + 3] == 0x7A && data[g + 4] == 0x1E)
                {
                    item.GemSlotCount = data[g];
                    break;
                }
            }

            return; // only one subStat block per item
        }
    }

    // ─── byte-level helpers ─────────────────────────────────────────────

    private static bool SkipVarintAndCheckTag(byte[] data, ref int pos, int end, byte tag1, byte tag2)
    {
        int shift = 0;
        while (pos < end)
        {
            if ((data[pos++] & 0x80) == 0)
            {
                if (pos + 2 > end) return false;
                if (data[pos] != tag1 || data[pos + 1] != tag2) return false;
                pos += 2;
                return true;
            }
            shift += 7;
            if (shift >= 32) return false;
        }
        return false;
    }

    private static (int Count, int Damage, int EndPos) TryParseMultiHit(byte[] data, int pos, int end, uint mainDamage)
    {
        var first = ParseMultiHitAt(data, pos, end, mainDamage);
        if (first.Count > 0 && first.Damage > 0) return first;
        if (pos + 1 < end && data[pos] == 1)
        {
            var second = ParseMultiHitAt(data, pos + 1, end, mainDamage);
            if (second.Count > 0 && second.Damage > 0) return second;
        }
        return (0, 0, pos);
    }

    private static (int Count, int Damage, int EndPos) ParseMultiHitAt(byte[] data, int pos, int end, uint mainDamage)
    {
        int origin = pos;
        uint expected = ProtocolUtils.ReadVarint(data, ref pos, end);
        if (expected == 0 || expected == uint.MaxValue || expected >= 100) return (0, 0, origin);

        int totalDamage = 0;
        uint actual = 0;
        for (uint k = 0; k < expected; k++)
        {
            if (pos >= end) break;
            uint hit = ProtocolUtils.ReadVarint(data, ref pos, end);
            if (hit == uint.MaxValue) break;
            totalDamage += (int)hit;
            actual++;
        }
        if (actual != expected || totalDamage <= 0) return (0, 0, origin);
        if (mainDamage != 0 && totalDamage < (int)mainDamage * 0.005) return (0, 0, origin);
        return ((int)actual, totalDamage, pos);
    }

    private static int LocateDamageHeader(byte[] data, int start, int end)
    {
        int pos = start;
        int shift = 0;
        int consumed = 0;
        while (pos < end)
        {
            int idx = pos++;
            consumed++;
            if ((data[idx] & 0x80) == 0)
            {
                if (consumed <= 0 || end - pos <= 1 || data[pos] != TAG_DAMAGE_1 || data[pos + 1] != TAG_DAMAGE_2)
                    break;
                return pos + 2;
            }
            shift += 7;
            if (shift > 31) break;
        }
        return -1;
    }

    private static uint ComputeDamageFlags(byte flagByte, byte damageType)
    {
        uint flags = (uint)(flagByte & 0x7F);
        if ((flagByte & 0x80) != 0) flags |= 0x80;
        if (damageType == 3)        flags |= 0x100;
        return flags;
    }

    private static void SkipVarint(byte[] data, ref int pos, int end)
    {
        while (pos < end && (data[pos++] & 0x80) != 0) { }
    }

    private static int IndexOfTag(byte[] data, int start, int end, byte b1, byte b2)
    {
        for (int i = start; i + 1 < end; i++)
            if (data[i] == b1 && data[i + 1] == b2) return i;
        return -1;
    }

    private static string HexDump(byte[] data, int offset, int length)
    {
        var sb = new StringBuilder(length * 3);
        for (int i = 0; i < length && offset + i < data.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[offset + i].ToString("X2"));
        }
        return sb.ToString();
    }
}
