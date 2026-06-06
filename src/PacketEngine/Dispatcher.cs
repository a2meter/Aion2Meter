using System;
using System.Text;

namespace PacketEngine;

/// Core PE packet parser. Ported from A2Meter.Dps.Protocol.PacketDispatcher.
/// Calls EngineState query callbacks for skill resolution / boss checks,
/// and fires EngineState output callbacks on successful parse.
internal sealed class Dispatcher
{
    private const byte TAG_DAMAGE_1 = 4,  TAG_DAMAGE_2 = 56;
    private const byte TAG_DOT_1    = 5,  TAG_DOT_2    = 56;
    private const byte TAG_BATTLE_STATS_1     = 42, TAG_BATTLE_STATS_2 = 56;
    private const byte TAG_BATTLE_STATS_ALT_1 = 43;
    private const byte TAG_SELF_INFO_1  = 51, TAG_SELF_INFO_2  = 54;
    private const byte TAG_OTHER_INFO_1 = 68, TAG_OTHER_INFO_2 = 54;
    private const byte TAG_MOB_SPAWN_1  = 64, TAG_MOB_SPAWN_2  = 54;
    private const byte TAG_GUARD_1      = 3,  TAG_GUARD_2      = 54;
    private const byte TAG_ENTITY_REMOVED_1 = 33, TAG_ENTITY_REMOVED_2 = 141;
    private const byte TAG_CHAR_LOOKUP_1 = 79, TAG_CHAR_LOOKUP_2 = 54;

    private const uint SENTINEL_SKILL_CODE = 12_250_030u;
    private const int  MIN_PACKET_LENGTH   = 4;
    private const int  IGNORED_PACKET_LENGTH = 11;
    private const int  MOB_CODE_SEARCH_RANGE = 60;
    private const int  MOB_HP_SEARCH_RANGE   = 67;
    private const int  MAX_NAME_LENGTH       = 72;
    private const int  NAME_SCAN_WINDOW      = 10;

    private static readonly int[]  CategoryTrailingSize = new int[] { 0, 0, 0, 0, 8, 12, 10, 14 };
    private static readonly byte[] SummonBoundaryMarker = new byte[] { 255, 255, 255, 255, 255, 255, 255, 255 };
    private static readonly byte[] SummonActorHeader    = new byte[] { 7, 2, 6 };
    private static readonly byte[] CP_PACKET_MARKER     = new byte[] { 0, 146 };
    private static readonly byte[] EntityCpPacketMarker  = new byte[] { 47, 42, 56 };
    private static readonly byte[] EntityCpTailMarker    = new byte[] { 14, 85, 54 };

    private readonly EngineState _state;

    public Dispatcher(EngineState state) => _state = state;

    public void Dispatch(byte[] data, int offset, int length)
    {
        if (length < MIN_PACKET_LENGTH || length == IGNORED_PACKET_LENGTH) return;

        int p = offset;
        int limit = offset + length;
        ProtocolUtils.ReadVarint(data, ref p, limit);
        int afterVarint = p - offset;
        if (afterVarint <= 0) afterVarint = 1;

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
                any |= TryParseSummon(data, offset, length, afterVarint + 2);
            }
            any |= HasEntityMarker(data, offset, length);
            any |= TryParseDamage(data, offset, length);
            any |= TryParseDot(data, offset, length);
            any |= TryParseBossHp(data, offset, length);
            any |= TryParseBattleStats(data, offset, length);
            any |= TryParseCharacterLookup(data, offset, length);
        }
    }

    // ─── damage ──────────────────────────────────────────────────────

    private bool TryParseDamage(byte[] data, int offset, int length)
    {
        int end = offset + length;
        int p = LocateDamageHeader(data, offset, end);
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

        int skillCode = _state.QueryResolveSkill(data, ref p, end, out _);
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

        _state.FireDamage((int)actorId, (int)targetId, skillCode, (int)(damageType & 0xFF),
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

        _state.FireDamage((int)actorId, (int)targetId, (int)skillCode, 0, (int)damage, 0u,
                          0, 0, (int)healAmount, hasExtra ? 1 : 0);
        return true;
    }

    private uint DeriveSkillCodeFromMobCode(uint rawMobCode)
    {
        uint candidate = rawMobCode / 1000;
        if (_state.QueryIsMobBoss((int)candidate) && IsSkillCodeInRange((int)candidate))
            return candidate;
        return rawMobCode / 100;
    }

    private bool IsRecoveryMob(uint skillCode)
    {
        var name = _state.QueryGetSkillName((int)skillCode);
        if (string.IsNullOrEmpty(name)) return false;
        var lower = name.ToLowerInvariant();
        return lower.Contains("recuperation") || lower.Contains("recovery") || lower.Contains("restoration");
    }

    private static bool IsSkillCodeInRange(int code)
    {
        uint c = (uint)code;
        if (c >= 11_000_000 && c < 20_000_000) return true;
        if (c >=  1_000_000 && c < 10_000_000) return true;
        if (c >=    100_000 && c <    200_000)  return true;
        if (c >= 29_000_000 && c < 31_000_000)  return true;
        return false;
    }

    // ─── mob spawn / summon ──────────────────────────────────────────

    private bool TryParseMobInfo(byte[] data, int offset, int length, int relativeStart)
    {
        int end = offset + length;
        int p = offset + relativeStart;
        if (p >= end) return false;

        uint mobEntityId = ProtocolUtils.ReadVarint(data, ref p, end);
        if (mobEntityId == uint.MaxValue) return false;

        int rel = p - offset;
        int lim = Math.Min(rel + MOB_CODE_SEARCH_RANGE, length - 2);
        int relMobCode = ScanMobCodeMarker(data, offset, rel, lim, end);
        if (relMobCode < 0) return false;

        int relMcode = relMobCode - 2;
        if (rel > relMcode - 3) return false;

        int absMcode = offset + relMcode - 3;
        if (absMcode < offset || absMcode + 3 > end) return false;
        uint mobCode = (uint)(data[absMcode] | (data[absMcode + 1] << 8) | (data[absMcode + 2] << 16));
        int isBoss = _state.QueryIsMobBoss((int)mobCode) ? 1 : 0;
        _state.FireMobSpawn((int)mobEntityId, (int)mobCode, 0, isBoss);

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
                    _state.FireMobSpawn((int)mobEntityId, (int)mobCode, (int)hpCur, isBoss);
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
        int p = offset + relativeStart;
        if (p >= end) return false;

        uint petId = ProtocolUtils.ReadVarint(data, ref p, end);
        if (petId == uint.MaxValue) return false;

        var span = new ReadOnlySpan<byte>(data, offset, length);
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
                _state.FireSummon(actorId, (int)petId);
                return true;
            }
            cursor = boundary + 1;
        }
        return false;
    }

    // ─── battle stats / buffs ───────────────────────────────────────

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
        if (!_state.QueryIsKnownBuffCode(buffId)) return true;

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
        _state.FireBuff((int)entityId, buffId, type, duration, timestamp, casterId);
        return true;
    }

    // ─── combat power ───────────────────────────────────────────────

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

            int cp = data[sync - 9] | (data[sync - 8] << 8) | (data[sync - 7] << 16) | (data[sync - 6] << 24);
            if (cp < 10000 || cp > 999999) continue;

            int level = data[sync - 13] | (data[sync - 12] << 8) | (data[sync - 11] << 16) | (data[sync - 10] << 24);
            if (level < 1000 || level > 5000) continue;
            if ((data[sync - 17] | (data[sync - 16] << 8) | (data[sync - 15] << 16) | (data[sync - 14] << 24)) != 0) continue;

            int classId = data[sync - 21] | (data[sync - 20] << 8) | (data[sync - 19] << 16) | (data[sync - 18] << 24);
            if (classId < 1 || classId > 55) continue;

            var found = FindNickAndServerInCpPacket(data, offset, sync);
            if (found.HasValue)
            {
                _state.FireCombatPowerByName(found.Value.Nick, found.Value.ServerId, cp);
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
                    _state.FireCombatPower((int)entityId, cp);
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

    // ─── boss hp / entity removal ───────────────────────────────────

    private bool TryParseBossHp(byte[] data, int offset, int length)
    {
        int end = offset + length;
        for (int i = offset; i < end - 10; i++)
        {
            if (data[i] != 141) continue;
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

            _state.FireBossHp((int)entityId, hp);
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

        _state.FireEntityRemoved((int)entityId);
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

    // ─── user info ──────────────────────────────────────────────────

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
                        int jobCode  = (after + 3 <= end) ? data[after + 2] : -1;
                        string nick = AppendServerName(nameTuple.Value.Name, serverId);
                        _state.FireUserInfo((int)entityId, nick, serverId, jobCode, 1);
                        return true;
                    }
                }
            }
        }

        // Other-player info.
        int other = IndexOfTag(data, offset, end, TAG_OTHER_INFO_1, TAG_OTHER_INFO_2);
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

        int serverId2 = FindServerId(data, after2, end);
        string nick2 = AppendServerName(name2.Value.Name, serverId2);
        _state.FireUserInfo((int)entityId2, nick2, serverId2, jobCode2, 0);
        return true;
    }

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

    private static int FindServerId(byte[] data, int from, int end)
    {
        int scanStart = Math.Min(from + 75, end);
        int scanEnd   = Math.Min(from + 108, end) - 1;
        for (int i = scanStart; i < scanEnd; i++)
        {
            int id = data[i] | (data[i + 1] << 8);
            if (!ServerMap.IsValidServerId(id)) continue;
            for (int j = 6; j <= 12 && i + j + 1 < end; j++)
                if ((data[i + j] | (data[i + j + 1] << 8)) == id) return id;
            return id;
        }
        return -1;
    }

    private static string AppendServerName(string name, int serverId)
    {
        if (serverId <= 0) return name;
        var sn = ServerMap.GetName(serverId);
        return string.IsNullOrEmpty(sn) ? name : $"{name}[{sn}]";
    }

    // ─── character lookup ───────────────────────────────────────────

    private bool TryParseCharacterLookup(byte[] data, int offset, int length)
    {
        int end = offset + length;
        int idx = IndexOfTag(data, offset, end, TAG_CHAR_LOOKUP_1, TAG_CHAR_LOOKUP_2);
        if (idx < 0) return false;

        int p = idx + 2 + 2;
        if (p >= end || data[p] != 7) return false;
        p++;

        uint nameLen = ProtocolUtils.ReadVarint(data, ref p, end);
        if (nameLen == uint.MaxValue || nameLen < 1 || nameLen > MAX_NAME_LENGTH || p + (int)nameLen > end)
            return false;
        string name = ProtocolUtils.DecodeGameString(data, p, (int)nameLen);
        if (string.IsNullOrEmpty(name)) return false;
        p += (int)nameLen;

        if (p + 20 > end) return false;
        int jobCode = data[p]; p++;
        p += 3;
        p++;
        p++;
        int level = data[p]; p++;
        p += 7;
        int entityId = data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24); p += 4;
        int serverId = data[p] | (data[p + 1] << 8);

        int combatPower = 0;
        int cpPos = offset + length - 9;
        if (cpPos > p && cpPos + 5 <= end && data[cpPos] == 0x14)
        {
            combatPower = data[cpPos + 1] | (data[cpPos + 2] << 8)
                        | (data[cpPos + 3] << 16) | (data[cpPos + 4] << 24);
        }

        // CharacterLookup is not a standard PE callback — fire UserInfo + CombatPower instead.
        string nick = AppendServerName(name, serverId);
        _state.FireUserInfo(entityId, nick, serverId, jobCode, 0);
        if (combatPower > 0)
            _state.FireCombatPower(entityId, combatPower);
        return true;
    }

    // ─── byte-level helpers ─────────────────────────────────────────

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
        if (damageType == 3) flags |= 0x100;
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
}
