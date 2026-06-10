using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using A2Meter.Dps;

namespace A2Meter.Dps.Protocol;

/// Ported from A2Viewer.Packet.PartyStreamParser. The native engine fast-path
/// is removed; this class parses the framed party-protocol packets entirely
/// in C#. Korean log strings are kept as-is for parity with the original.
internal sealed class PartyStreamParser
{
    private sealed class MemberHint
    {
        public uint CharacterId;
        public int  JobCode;
        public int  CombatPower;
    }

    private static byte[] Magic => ProtocolOpcodeConfig.PacketMagic;

    private readonly List<byte> _buffer = new();
    private readonly Dictionary<string, MemberHint> _memberHints = new(StringComparer.Ordinal);

    private bool _justLeft;
    private bool _boardRefreshing;
    private int  _boardRefreshGracePackets;
    private bool _pendingPartyLeft;
    private int  _pendingPartyLeftGracePackets;
    private int  _lastDungeonId;

    public event Action<List<PartyMember>>? PartyList;
    public event Action<List<PartyMember>>? PartyUpdate;
    public event Action<PartyMember>?       PartyRequest;
    public event Action<PartyMember>?       PartyAccept;
    public event Action?                    PartyLeft;
    public event Action?                    PartyEjected;
    public event Action<int, int>?          DungeonDetected;
    public event Action<string, int, int>?  CombatPowerDetected;

    public void Feed(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;

        // ProtocolPipeline feeds this parser from StreamProcessor's message
        // hook. In that path the bytes are already framed, so waiting for the
        // next sync marker leaves party roster packets buffered until overflow.
        if (LooksLikeDispatchedFrame(data))
        {
            try { ProcessPacket(data.ToArray()); }
            catch { /* never let one bad packet kill the stream */ }
            return;
        }

        _buffer.AddRange(data.ToArray());
        Flush();
    }

    public void Reset()
    {
        ClearBoardRefresh();
        _buffer.Clear();
        _pendingPartyLeft = false;
        _pendingPartyLeftGracePackets = 0;
        _justLeft = false;
        _lastDungeonId = 0;
    }

    public void Dispose() { /* nothing native to release */ }

    private void Flush()
    {
        while (true)
        {
            int idx = ProtocolUtils.IndexOf(_buffer, Magic);
            if (idx < 0) break;
            if (idx > 0)
            {
                var range = _buffer.GetRange(0, idx + 3);
                _buffer.RemoveRange(0, idx + 3);
                if (range.Count > 3)
                {
                    try { ProcessPacket(range.ToArray()); }
                    catch { /* never let one bad packet kill the stream */ }
                }
            }
            else
            {
                _buffer.RemoveRange(0, 3);
            }
        }
        if (_buffer.Count > 524288)
        {
            Console.Error.WriteLine($"[party] 버퍼 오버플로우 ({_buffer.Count}B) → 클리어");
            _buffer.Clear();
        }
    }

    private void ProcessPacket(byte[] packet)
    {
        ScanDungeonIdRaw(packet);
        ScanCombatPowerRaw(packet);
        ScanBoardRefreshRaw(packet);
        ScanPartyLeftRaw(packet);

        bool parsed = false;
        const int MaxIters = 2048;
        Span<byte> span = packet.AsSpan();
        for (int i = 0; i < MaxIters; i++)
        {
            if (span.Length <= 3) break;

            var (declaredLen, varintLen) = ReadVarInt(span);
            if (varintLen < 0) return;

            if (span.Length == declaredLen)
            {
                if (varintLen + 1 < span.Length && span[varintLen] == 0xFF && span[varintLen + 1] == 0xFF)
                {
                    if (span.Length < 10) return;
                    span = span.Slice(10, span.Length - 10);
                    continue;
                }
                if (!(parsed | ParsePerfectPacket(span.Slice(0, span.Length - 3))))
                    ScanPartyUpdateRaw(packet);
                return;
            }

            int payloadLen = declaredLen - 3;
            if (payloadLen > span.Length)
            {
                if (span.Length >= 10 && span[2] == 0xFF && span[3] == 0xFF)
                    span = span.Slice(10, span.Length - 10);
                else
                    span = span.Slice(1, span.Length - 1);
                continue;
            }
            if (payloadLen <= 0)
            {
                span = span.Slice(1, span.Length - 1);
                continue;
            }
            var slice = span.Slice(0, payloadLen);
            if (slice.Length > 3) parsed |= ParsePerfectPacket(slice);
            span = span.Slice(payloadLen, span.Length - payloadLen);
        }
        if (!parsed) ScanPartyUpdateRaw(packet);
    }

    // ─── opcode scans ────────────────────────────────────────────────

    private static bool LooksLikeDispatchedFrame(ReadOnlySpan<byte> data)
    {
        if (data.Length <= 3) return false;

        var (declaredLen, varintLen) = ReadVarInt(data);
        if (varintLen < 0) return false;

        int msgLen = declaredLen + varintLen - 4;
        return msgLen > 0 && msgLen <= data.Length;
    }

    private void ScanDungeonIdRaw(byte[] packet)
    {
        for (int i = 0; i < packet.Length - 10; i++)
        {
            if (packet[i] != ProtocolOpcodeConfig.PartyUpdate || packet[i + 1] != ProtocolOpcodeConfig.PartyMarker) continue;

            int p = i + 2;
            if (p + 4 >= packet.Length || packet[p + 3] != 0) continue;
            p += 4;

            var (val, len) = ReadVarInt(packet.AsSpan(p));
            if (len < 0 || val < 1 || val > 200) continue;
            p += len + val;
            if (p + 5 > packet.Length) continue;

            int marker = packet[p];
            if (marker == 4 || marker == 8)
            {
                p++;
                int dungeonId = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(p));
                if (dungeonId >= 600000 && dungeonId < 700000 && dungeonId != _lastDungeonId)
                {
                    _lastDungeonId = dungeonId;
                    int stage = (p + 4 < packet.Length) ? packet[p + 4] : 0;
                    EmitDungeon(dungeonId, stage, "raw");
                }
            }
        }
    }

    private void ScanPartyUpdateRaw(byte[] packet)
    {
        for (int i = 0; i < packet.Length - 4; i++)
        {
            if (packet[i] != ProtocolOpcodeConfig.PartyUpdate || packet[i + 1] != ProtocolOpcodeConfig.PartyMarker) continue;

            int blockStart = -1;
            int blockLen = 0;
            for (int j = 1; j <= 3 && i - j >= 0; j++)
            {
                int val = 0, shift = 0;
                bool ok = true;
                for (int k = 0; k < j; k++)
                {
                    byte b = packet[i - j + k];
                    val |= (b & 0x7F) << shift;
                    if (k <  j - 1 && (b & 0x80) == 0) { ok = false; break; }
                    if (k == j - 1 && (b & 0x80) != 0) { ok = false; break; }
                    shift += 7;
                }
                if (!ok) continue;

                int payloadLen = val - 3;
                if (payloadLen > 30 && payloadLen < 500)
                {
                    blockStart = i - j;
                    blockLen   = Math.Min(payloadLen, packet.Length - blockStart);
                    break;
                }
            }
            if (blockStart < 0 || blockLen <= 0) continue;

            int dataOffset = i + 2;
            int to         = Math.Min(blockStart + blockLen, packet.Length);
            var members    = ScanMembersRaw(packet, dataOffset, to);
            if (members.Count > 0)
            {
                Console.Error.WriteLine($"[party] 02 97 raw스캔 {members.Count}명: {string.Join(", ", members.Select(m => $"{m.Nickname}({m.JobName} Lv{m.Level} CP{m.CombatPower})"))}");
                PartyUpdate?.Invoke(members);
                break;
            }
        }
    }

    private List<PartyMember> ScanMembersRaw(byte[] packet, int from, int to)
        => CollectPartyMembers(packet, from, to);

    private void ScanCombatPowerRaw(byte[] packet)
    {
        bool hasCpMarker = false;
        for (int i = 0; i < packet.Length - 1; i++)
            if (packet[i] == 0 && packet[i + 1] == 146) { hasCpMarker = true; break; }
        if (!hasCpMarker) return;

        for (int sync = packet.Length - 3; sync >= 21; sync--)
        {
            if (packet[sync] != 6 || packet[sync + 1] != 0 || packet[sync + 2] != 54) continue;
            if (sync < 21) break;

            bool zeros = true;
            for (int z = sync - 5; z < sync; z++) if (packet[z] != 0) { zeros = false; break; }
            if (!zeros) continue;

            int cp = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(sync - 9));
            if (cp < 10000 || cp > 999999) continue;

            int level = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(sync - 13));
            if (level < 1000 || level > 5000) continue;
            if (BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(sync - 17)) != 0) continue;

            int classId = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(sync - 21));
            if (classId < 1 || classId > 55) continue;

            for (int k = 0; k + 3 < sync - 21; k++)
            {
                int sid = packet[k] | (packet[k + 1] << 8);
                if (sid < 1001 || sid > 2021) continue;

                int nameLen = packet[k + 2];
                if (nameLen < 3 || nameLen > 48 || k + 3 + nameLen > sync - 21) continue;

                try
                {
                    string nick = Encoding.UTF8.GetString(packet, k + 3, nameLen);
                    if (string.IsNullOrEmpty(nick) || nick.Length < 2) continue;

                    bool clean = true;
                    foreach (char c in nick)
                    {
                        if ((c < '가' || c > '힣') && (c < 'a' || c > 'z') && (c < 'A' || c > 'Z') && (c < '0' || c > '9'))
                        { clean = false; break; }
                    }
                    if (!clean) continue;

                    RememberMemberHint(nick, sid, 0u, 0, cp);
                    Console.Error.WriteLine($"[party] CP 패킷감지: {nick}:{sid} CP={cp} (IL={level})");
                    CombatPowerDetected?.Invoke(nick, sid, cp);
                    return;
                }
                catch { }
            }
        }
    }

    private void ScanBoardRefreshRaw(byte[] packet)
    {
        for (int i = 0; i < packet.Length - 1; i++)
        {
            if (packet[i] == ProtocolOpcodeConfig.PartyBoardRefresh && packet[i + 1] == ProtocolOpcodeConfig.PartyMarker) { ArmBoardRefresh(); break; }
            if (packet[i] == ProtocolOpcodeConfig.PartyBoardControl && packet[i + 1] == ProtocolOpcodeConfig.PartyMarker && i + 3 < packet.Length && packet[i + 2] == 0 && packet[i + 3] == 0)
            { ArmBoardRefresh(); break; }
        }
    }

    private void ScanPartyLeftRaw(byte[] packet)
    {
        for (int i = 0; i < packet.Length - 1; i++)
        {
            if (packet[i] == ProtocolOpcodeConfig.PartyLeave && packet[i + 1] == ProtocolOpcodeConfig.PartyMarker && i + 3 < packet.Length && packet[i + 2] == 0 && packet[i + 3] == 0)
            { ArmPendingPartyLeft(); break; }
        }
    }

    // ─── perfect-frame parser ────────────────────────────────────────

    private bool ParsePerfectPacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 3) return false;
        int varintLen = ReadVarInt(packet).length;
        if (varintLen < 0) return false;

        int p = varintLen;
        if (p + 1 >= packet.Length) return false;

        byte op = packet[p];
        if (packet[p + 1] != ProtocolOpcodeConfig.PartyMarker) return false;

        int dataOff = p + 2;

        // Opcodes other than 0x2A advance the grace counters on every frame.
        if (op != ProtocolOpcodeConfig.PartyBoardRefresh) { AdvanceBoardRefresh(); AdvancePendingPartyLeft(); }

        op = NormalizePartyOpcode(op);

        switch (op)
        {
            case 19:  // 0x13 — board-refresh / control
                if (IsEmptyControlPacket(packet, dataOff))
                {
                    Console.Error.WriteLine("[party] 13 97 빈 패킷 -> 새로고침 후보");
                    ArmBoardRefresh();
                    return false;
                }
                Console.Error.WriteLine($"[party] 13 97 미지 제어패킷 ({packet.Length}B): {ProtocolUtils.HexDump(packet)}");
                return false;

            case 1:   // 0x01 — party list
            {
                if (_justLeft) { _justLeft = false; return false; }

                if (dataOff + 1 < packet.Length && packet[dataOff] == 0 && packet[dataOff + 1] == 0)
                {
                    if (dataOff + 6 <= packet.Length)
                    {
                        int dungeonOff = dataOff + 2;
                        int dungeonId = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(dungeonOff, packet.Length - dungeonOff));
                        if (dungeonId >= 600000 && dungeonId < 700000)
                        {
                            int stage = (dataOff + 6 < packet.Length) ? packet[dataOff + 6] : 0;
                            EmitDungeon(dungeonId, stage, "01 97");
                        }
                    }
                    if (ConsumeBoardRefresh()) { ClearPendingPartyLeft(); return false; }
                    if (ConsumePendingPartyLeft())
                    {
                        _justLeft = true;
                        _lastDungeonId = 0;
                        ClearBoardRefresh();
                        Console.Error.WriteLine("[party] 1D/01 시퀀스 해산 감지");
                        PartyLeft?.Invoke();
                        return false;
                    }
                    Console.Error.WriteLine("[party] 01 97 추방 (빈 목록)");
                    PartyEjected?.Invoke();
                    return false;
                }

                var members = ParsePartyMemberBlocks(packet, dataOff);
                if (members.Count > 0)
                {
                    ClearPendingPartyLeft();
                    ClearBoardRefresh();
                    Console.Error.WriteLine($"[party] 01 97 파티목록 {members.Count}명: {string.Join(", ", members.Select(m => $"{m.Nickname}({m.JobName} Lv{m.Level} CP{m.CombatPower})"))}");
                    PartyList?.Invoke(members);
                    return true;
                }
                Console.Error.WriteLine($"[party] 01 97 파싱실패 ({packet.Length}B): {ProtocolUtils.HexDump(packet)}");
                return false;
            }

            case 2:   // 0x02 — party update
            {
                TryParseDungeonId(packet, dataOff);
                var members = ParsePartyMemberBlocks(packet, dataOff);
                if (members.Count > 0)
                {
                    ClearPendingPartyLeft();
                    Console.Error.WriteLine($"[party] 02 97 업데이트 {members.Count}명: {string.Join(", ", members.Select(m => m.Nickname + "(" + m.JobName + ")"))}");
                    PartyUpdate?.Invoke(members);
                    return true;
                }
                Console.Error.WriteLine($"[party] 02 97 파싱실패 ({packet.Length}B): {ProtocolUtils.HexDump(packet)}");
                return false;
            }

            case 7:   // 0x07 — party request (to me)
            {
                Console.Error.WriteLine($"[party] 07 97 hex ({packet.Length}B): {ProtocolUtils.HexDump(packet)}");
                var req = ParsePartyRequest(packet, dataOff);
                if (req != null)
                {
                    ClearPendingPartyLeft();
                    RememberMemberHint(req);
                    Console.Error.WriteLine($"[party] 07 97 신청: {req.Nickname}({req.JobName} Lv{req.Level} CP{req.CombatPower})");
                    PartyRequest?.Invoke(req);
                    return true;
                }
                Console.Error.WriteLine("[party] 07 97 파싱실패");
                return false;
            }

            case 11:  // 0x0B — party accept
            {
                var acc = ParsePartyAcceptMember(packet, dataOff);
                if (acc != null)
                {
                    ClearPendingPartyLeft();
                    RememberMemberHint(acc);
                    Console.Error.WriteLine($"[party] 0B 97 수락: {acc.Nickname}({acc.JobName} Lv{acc.Level} CP{acc.CombatPower})");
                    PartyAccept?.Invoke(acc);
                    return true;
                }
                Console.Error.WriteLine($"[party] 0B 97 파싱실패 ({packet.Length}B): {ProtocolUtils.HexDump(packet)}");
                return false;
            }

            case 4:   // 0x04 — dungeon exit
                if (_lastDungeonId != 0)
                {
                    Console.Error.WriteLine("[party] 04 97 던전 퇴장 감지");
                    _lastDungeonId = 0;
                    DungeonDetected?.Invoke(0, 0);
                }
                return false;

            case 29:  // 0x1D — leave party
                if (IsEmptyControlPacket(packet, dataOff))
                {
                    Console.Error.WriteLine("[party] 1D 97 빈 패킷 -> 해산 후보");
                    ArmPendingPartyLeft();
                    return false;
                }
                ClearPendingPartyLeft();
                _justLeft = true;
                _lastDungeonId = 0;
                ClearBoardRefresh();
                Console.Error.WriteLine("[party] 1D 97 퇴장");
                PartyLeft?.Invoke();
                return false;

            case 42:  // 0x2A — board refresh
                ArmBoardRefresh();
                return false;

            default:
            {
                int hexFrom = dataOff;
                Console.Error.WriteLine($"[party] {op:X2} 97 미지 opcode ({packet.Length}B): {ProtocolUtils.HexDump(packet.Slice(hexFrom, packet.Length - hexFrom).ToArray(), 0, 64)}");
                return false;
            }
        }
    }

    private static byte NormalizePartyOpcode(byte op)
    {
        if (op == ProtocolOpcodeConfig.PartyBoardControl) return 19;
        if (op == ProtocolOpcodeConfig.PartyList) return 1;
        if (op == ProtocolOpcodeConfig.PartyUpdate) return 2;
        if (op == ProtocolOpcodeConfig.PartyRequest) return 7;
        if (op == ProtocolOpcodeConfig.PartyAccept) return 11;
        if (op == ProtocolOpcodeConfig.PartyDungeonExit) return 4;
        if (op == ProtocolOpcodeConfig.PartyLeave) return 29;
        if (op == ProtocolOpcodeConfig.PartyBoardRefresh) return 42;
        return op;
    }

    private void TryParseDungeonId(ReadOnlySpan<byte> packet, int dataOffset)
    {
        try
        {
            int p = dataOffset;
            if (p + 4 >= packet.Length) return;
            p += 3;
            if (packet[p] != 0) return;
            p++;

            var (val, len) = ReadVarInt(packet.Slice(p, packet.Length - p));
            if (len < 0 || val < 0) return;
            p += len + val;
            if (p >= packet.Length) return;
            p++;
            if (p + 4 > packet.Length) return;

            int dungeonId = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(p, packet.Length - p));
            p += 4;
            Console.Error.WriteLine($"[party] 02 97 dungeonId raw={dungeonId} (0x{dungeonId:X8}) last={_lastDungeonId}");
            if (dungeonId == 0)
            {
                if (_lastDungeonId != 0)
                {
                    _lastDungeonId = 0;
                    Console.Error.WriteLine("[party] 02 97 던전 퇴장 감지 (dungeonId=0)");
                    DungeonDetected?.Invoke(0, 0);
                }
            }
            else if (dungeonId >= 600000 && dungeonId < 700000)
            {
                int stage = (p < packet.Length) ? packet[p] : 0;
                EmitDungeon(dungeonId, stage, "02 97");
            }
        }
        catch { }
    }

    private void EmitDungeon(int dungeonId, int stage, string source)
    {
        var name = SkillDatabase.Shared.GetDungeonName(dungeonId);
        Console.Error.WriteLine("[party] " + source + " 던전감지: " + name);
        DungeonDetected?.Invoke(dungeonId, stage);
    }

    // ─── member-block parsers ────────────────────────────────────────

    private List<PartyMember> ParsePartyMemberBlocks(ReadOnlySpan<byte> packet, int dataOffset)
        => CollectPartyMembers(packet, dataOffset, packet.Length);

    private static PartyMember? ParsePartyRequest(ReadOnlySpan<byte> packet, int dataOffset)
    {
        int len = packet.Length;
        for (int i = 0; i <= 40; i++)
        {
            for (int j = 1; j <= 48; j++)
            {
                int posLen = len - j - 1 - i;
                if (posLen < dataOffset + 12 || packet[posLen] != j) continue;

                string nick;
                try { nick = Encoding.UTF8.GetString(packet.Slice(posLen + 1, j)); }
                catch { continue; }
                if (!IsValidNickname(nick)) continue;

                int posStats = posLen - 12;
                if (posStats < dataOffset) continue;

                uint jobCode = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(posStats, packet.Length - posStats));
                uint level   = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(posStats + 4, packet.Length - posStats - 4));
                if (level >= 1 && level <= 55)
                {
                    int posCp = posLen + 1 + j + 6;
                    uint cp = 0;
                    if (posCp + 4 <= len)
                        cp = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(posCp, packet.Length - posCp));

                    int sid = 0;
                    if (dataOffset + 11 < len)
                        sid = packet[dataOffset + 10] | (packet[dataOffset + 11] << 8);

                    return new PartyMember
                    {
                        ServerId    = sid,
                        ServerName  = ServerMap.GetName(sid),
                        Nickname    = nick,
                        JobCode     = (int)jobCode,
                        JobName     = JobMapping.GameToName.GetValueOrDefault((int)jobCode, "직업불명"),
                        Level       = (int)level,
                        CombatPower = (int)cp,
                    };
                }
            }
        }
        return null;
    }

    private static PartyMember? ParsePartyAcceptMember(ReadOnlySpan<byte> packet, int dataOffset)
    {
        if (dataOffset + 25 > packet.Length) return null;

        byte tag = packet[dataOffset];
        if (tag != 26 && tag != 58) return null;

        int p = dataOffset + 2;
        uint characterId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(p, packet.Length - p));

        int sid     = packet[dataOffset + 8] | (packet[dataOffset + 9] << 8);
        int nameLen = packet[dataOffset + 10];
        if (nameLen < 1 || nameLen > 48) return null;
        if (dataOffset + 11 + nameLen + 12 > packet.Length) return null;

        string nick;
        try { nick = Encoding.UTF8.GetString(packet.Slice(dataOffset + 11, nameLen)); }
        catch { return null; }
        if (!IsValidNickname(nick)) return null;

        int afterName = dataOffset + 11 + nameLen;
        uint jobCode = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(afterName, packet.Length - afterName));
        uint level   = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(afterName + 4, packet.Length - afterName - 4));
        if (level < 1 || level > 55) return null;

        uint cp;
        if (tag == 26)
        {
            cp = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(afterName + 8, packet.Length - afterName - 8));
        }
        else
        {
            cp = 0;
            int sidCheck1 = afterName + 13;
            int sidCheck2 = afterName + 15;
            int markerIdx = afterName + 17;
            int cpPos     = afterName + 18;
            if (cpPos < packet.Length && sidCheck2 + 1 < packet.Length
                && (packet[sidCheck1] | (packet[sidCheck1 + 1] << 8)) == sid
                && (packet[sidCheck2] | (packet[sidCheck2 + 1] << 8)) == sid
                && packet[markerIdx] == 4
                && TryReadCombatPower(packet, cpPos, packet.Length, out var cpVal))
            {
                cp = (uint)cpVal;
            }
        }
        if (cp > 9999999) return null;

        return new PartyMember
        {
            CharacterId = characterId,
            ServerId    = sid,
            ServerName  = ServerMap.GetName(sid),
            Nickname    = nick,
            JobCode     = (int)jobCode,
            JobName     = JobMapping.GameToName.GetValueOrDefault((int)jobCode, "직업불명"),
            Level       = (int)level,
            CombatPower = (int)cp,
        };
    }

    // ─── varint + nickname helpers ───────────────────────────────────

    private static (int value, int length) ReadVarInt(ReadOnlySpan<byte> buf)
    {
        int val = 0, shift = 0, used = 0;
        do
        {
            if (used >= buf.Length) return (-1, -1);
            byte b = buf[used++];
            val |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return (val, used);
            shift += 7;
        }
        while (shift < 32);
        return (-1, -1);
    }

    private static bool IsValidNickname(string? nick)
    {
        if (string.IsNullOrEmpty(nick)) return false;
        if (Regex.IsMatch(nick, "^[0-9]+$")) return false;
        return Regex.IsMatch(nick, "^[가-힣a-zA-Z0-9]+$");
    }

    // ─── party-member byte parsers ──────────────────────────────────

    private List<PartyMember> CollectPartyMembers(ReadOnlySpan<byte> packet, int from, int to)
    {
        var members = new List<PartyMember>();
        var seen    = new HashSet<string>(StringComparer.Ordinal);
        int lo = Math.Max(0, from);
        int hi = Math.Min(packet.Length, to);
        for (int i = lo; i < hi; i++)
        {
            if (TryParsePartyMember(packet, i, hi, out var member))
            {
                var key = GetMemberHintKey(member.Nickname, member.ServerId);
                if (!seen.Contains(key))
                {
                    member = ApplyMemberHint(member);
                    seen.Add(key);
                    RememberMemberHint(member);
                    members.Add(member);
                }
            }
        }
        return members;
    }

    private bool TryParsePartyMember(ReadOnlySpan<byte> packet, int offset, int limit, out PartyMember member)
    {
        member = new PartyMember();
        int nameLen = packet[offset];
        if (nameLen < 2 || nameLen > 48) return false;
        if (offset + 1 + nameLen > limit) return false;

        string nick;
        try { nick = Encoding.UTF8.GetString(packet.Slice(offset + 1, nameLen)); }
        catch { return false; }
        if (!IsValidNickname(nick)) return false;

        int afterName = offset + 1 + nameLen;

        uint characterId = 0;
        if (offset >= 8)
        {
            int p = offset - 8;
            characterId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(p, packet.Length - p));
        }

        int sidGuess = -1;
        if (offset >= 2)
        {
            int s = packet[offset - 2] | (packet[offset - 1] << 8);
            if (IsValidServerId(s)) sidGuess = s;
        }

        int jobCode = 0, level = 0, cp = 0;
        if (TryReadFixedStats(packet, afterName, limit, out var jc, out var lv, out var cpv, out var sidv))
        {
            jobCode = jc; level = lv; cp = cpv;
            if (sidGuess <= 0) sidGuess = sidv;
        }
        if (jobCode == 0 && TryReadLeadingJob(packet, afterName, limit, out var jc2, out var lv2))
        {
            jobCode = jc2;
            if (level == 0) level = lv2;
        }

        bool gotCp = TryReadServerAndCpAfterName(packet, afterName, limit, sidGuess, out var sidAfter, out var cpAfter, out var anchor);
        if (sidGuess <= 0) sidGuess = sidAfter;
        if (cp <= 0) cp = cpAfter;

        if ((jobCode == 0 || level == 0) && anchor >= 0
            && TryReadStatsFromAnchor(packet, anchor, limit, out var jc3, out var lv3, out var sid3))
        {
            if (jobCode == 0) jobCode = jc3;
            if (level   == 0) level   = lv3;
            if (sidGuess <= 0) sidGuess = sid3;
        }

        if (!IsValidServerId(sidGuess)) return false;
        if (!(characterId != 0 || jobCode != 0 || cp > 0 || gotCp)) return false;

        member = new PartyMember
        {
            CharacterId = characterId,
            ServerId    = sidGuess,
            ServerName  = ServerMap.GetName(sidGuess),
            Nickname    = nick,
            JobCode     = jobCode,
            JobName     = JobMapping.GameToName.GetValueOrDefault(jobCode, "직업불명"),
            Level       = level,
            CombatPower = cp,
        };
        return true;
    }

    private static bool TryReadFixedStats(ReadOnlySpan<byte> packet, int statsOffset, int limit,
                                          out int jobCode, out int level, out int combatPower, out int serverId)
    {
        jobCode = 0; level = 0; combatPower = 0; serverId = -1;
        if (statsOffset + 21 > limit) return false;

        int jc = (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(statsOffset, packet.Length - statsOffset));
        if (!IsPlausibleJobCode(jc)) return false;

        int lv = (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(statsOffset + 4, packet.Length - statsOffset - 4));
        if (lv < 1 || lv > 55) return false;

        int cpFixed = (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(statsOffset + 8, packet.Length - statsOffset - 8));
        if (cpFixed < 500 || cpFixed > 9999999) return false;

        int sidA = packet[statsOffset + 12] | (packet[statsOffset + 13] << 8);
        int sidB = packet[statsOffset + 14] | (packet[statsOffset + 15] << 8);
        int sid  = IsValidServerId(sidB) ? sidB : (IsValidServerId(sidA) ? sidA : -1);
        if (sid <= 0) return false;

        int cpFinal = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(statsOffset + 17, packet.Length - statsOffset - 17));
        jobCode = jc;
        level   = lv;
        combatPower = IsPlausibleCombatPower(cpFinal) ? cpFinal : 0;
        serverId = sid;
        return true;
    }

    private static bool TryReadLeadingJob(ReadOnlySpan<byte> packet, int statsOffset, int limit, out int jobCode, out int level)
    {
        jobCode = 0; level = 0;
        if (statsOffset >= limit) return false;

        if (statsOffset + 8 <= limit)
        {
            int jc = (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(statsOffset, packet.Length - statsOffset));
            int lv = (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(statsOffset + 4, packet.Length - statsOffset - 4));
            if (IsPlausibleJobCode(jc) && lv >= 1 && lv <= 55)
            {
                jobCode = jc; level = lv; return true;
            }
        }

        int jc1 = packet[statsOffset];
        if (!IsPlausibleJobCode(jc1))
        {
            int v = ReadVarInt(packet.Slice(statsOffset, packet.Length - statsOffset)).value;
            if (!IsPlausibleJobCode(v)) return false;
            jc1 = v;
        }
        jobCode = jc1;
        if (statsOffset + 1 < limit)
        {
            int lvByte = packet[statsOffset + 1];
            if (lvByte >= 1 && lvByte <= 55) level = lvByte;
        }
        return true;
    }

    private static bool TryReadServerAndCpAfterName(ReadOnlySpan<byte> packet, int nameEnd, int limit,
                                                    int preferredServerId,
                                                    out int serverId, out int combatPower, out int anchorPos)
    {
        serverId = -1; combatPower = 0; anchorPos = -1;
        int hi = Math.Min(limit - 2, nameEnd + 20);
        for (int i = nameEnd; i < hi; i++)
        {
            int sid = packet[i] | (packet[i + 1] << 8);
            if (!IsValidServerId(sid)) continue;

            int cpStart = -1;
            if (i + 2 < limit && packet[i + 2] == 4) cpStart = i + 3;
            else if (i + 4 < limit && (packet[i + 2] | (packet[i + 3] << 8)) == sid && packet[i + 4] == 4) cpStart = i + 5;

            if (cpStart >= 0 && (preferredServerId <= 0 || sid == preferredServerId))
            {
                serverId  = sid;
                anchorPos = i;
                if (TryReadCombatPower(packet, cpStart, limit, out var cp)) combatPower = cp;
                return true;
            }
        }
        if (preferredServerId > 0 && TryReadPackedCombatPowerAfterStats(packet, nameEnd, limit, out var cp2))
        {
            serverId    = preferredServerId;
            combatPower = cp2;
            return true;
        }
        return false;
    }

    private static bool TryReadStatsFromAnchor(ReadOnlySpan<byte> packet, int anchorPos, int limit,
                                               out int jobCode, out int level, out int serverId)
    {
        jobCode = 0; level = 0; serverId = -1;
        for (int back = 12; back <= 16; back++)
        {
            int p = anchorPos - back;
            if (p < 0 || p + 16 > limit) continue;

            int jc = (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(p, packet.Length - p));
            int lv = (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(p + 4, packet.Length - p - 4));
            int sidA = packet[p + 12] | (packet[p + 13] << 8);
            int sidB = (p + 15 < limit) ? (packet[p + 14] | (packet[p + 15] << 8)) : -1;
            int sid  = IsValidServerId(sidB) ? sidB : (IsValidServerId(sidA) ? sidA : -1);

            if (IsPlausibleJobCode(jc) && lv >= 1 && lv <= 55 && sid > 0)
            {
                jobCode = jc; level = lv; serverId = sid; return true;
            }
        }
        return false;
    }

    private static bool TryReadPackedCombatPowerAfterStats(ReadOnlySpan<byte> packet, int statsOffset, int limit, out int combatPower)
    {
        combatPower = 0;
        int lo = Math.Min(limit, statsOffset + 8);
        int hi = Math.Min(limit - 3, statsOffset + 24);
        for (int i = lo; i < hi; i++)
        {
            if (packet[i] == 4 && TryReadCombatPower(packet, i + 1, limit, out var cp))
            {
                combatPower = cp;
                return true;
            }
        }
        return false;
    }

    private static bool TryReadCombatPower(ReadOnlySpan<byte> packet, int cpOffset, int limit, out int combatPower)
    {
        combatPower = 0;
        if (cpOffset + 4 <= limit)
        {
            int v = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(cpOffset, packet.Length - cpOffset));
            if (IsPlausibleCombatPower(v)) { combatPower = v; return true; }
        }
        if (cpOffset + 3 <= limit)
        {
            int v = packet[cpOffset] | (packet[cpOffset + 1] << 8) | (packet[cpOffset + 2] << 16);
            if (IsPlausibleCombatPower(v)) { combatPower = v; return true; }
        }
        return false;
    }

    // ─── hint cache ──────────────────────────────────────────────────

    private PartyMember ApplyMemberHint(PartyMember member)
    {
        if (member.ServerId <= 0 || string.IsNullOrEmpty(member.Nickname)) return member;
        if (_memberHints.TryGetValue(GetMemberHintKey(member.Nickname, member.ServerId), out var hint))
        {
            if (member.CharacterId == 0)        member.CharacterId = hint.CharacterId;
            if (member.JobCode <= 0 && hint.JobCode > 0) member.JobCode = hint.JobCode;
            if (hint.CombatPower > member.CombatPower)   member.CombatPower = hint.CombatPower;
        }
        member.ServerName = ServerMap.GetName(member.ServerId);
        member.JobName    = JobMapping.GameToName.GetValueOrDefault(member.JobCode, "직업불명");
        return member;
    }

    private void RememberMemberHint(PartyMember member)
        => RememberMemberHint(member.Nickname, member.ServerId, member.CharacterId, member.JobCode, member.CombatPower);

    private void RememberMemberHint(string nickname, int serverId, uint characterId = 0, int jobCode = 0, int combatPower = 0)
    {
        if (serverId <= 0 || string.IsNullOrEmpty(nickname)) return;
        var key = GetMemberHintKey(nickname, serverId);
        if (!_memberHints.TryGetValue(key, out var hint))
        {
            hint = new MemberHint();
            _memberHints[key] = hint;
        }
        if (characterId != 0)           hint.CharacterId = characterId;
        if (jobCode > 0)                hint.JobCode     = jobCode;
        if (IsPlausibleCombatPower(combatPower) && combatPower > hint.CombatPower) hint.CombatPower = combatPower;
    }

    // ─── grace counters ──────────────────────────────────────────────

    private void ArmBoardRefresh()
    {
        _boardRefreshing = true;
        if (_boardRefreshGracePackets < 6) _boardRefreshGracePackets = 6;
    }

    private void ArmPendingPartyLeft()
    {
        _pendingPartyLeft = true;
        if (_pendingPartyLeftGracePackets < 12) _pendingPartyLeftGracePackets = 12;
    }

    private void AdvanceBoardRefresh()
    {
        if (!_boardRefreshing) return;
        if (_boardRefreshGracePackets > 0) _boardRefreshGracePackets--;
        if (_boardRefreshGracePackets <= 0) ClearBoardRefresh();
    }

    private void AdvancePendingPartyLeft()
    {
        if (!_pendingPartyLeft) return;
        if (_pendingPartyLeftGracePackets > 0) _pendingPartyLeftGracePackets--;
        if (_pendingPartyLeftGracePackets <= 0) ClearPendingPartyLeft();
    }

    private bool ConsumeBoardRefresh()
    {
        if (!_boardRefreshing) return false;
        ClearBoardRefresh();
        return true;
    }

    private bool ConsumePendingPartyLeft()
    {
        if (!_pendingPartyLeft) return false;
        ClearPendingPartyLeft();
        return true;
    }

    private void ClearBoardRefresh()
    {
        _boardRefreshing = false;
        _boardRefreshGracePackets = 0;
    }

    private void ClearPendingPartyLeft()
    {
        _pendingPartyLeft = false;
        _pendingPartyLeftGracePackets = 0;
    }

    // ─── util ────────────────────────────────────────────────────────

    private static bool IsEmptyControlPacket(ReadOnlySpan<byte> packet, int dataOffset)
    {
        if (dataOffset + 1 < packet.Length && packet[dataOffset] == 0 && packet[dataOffset + 1] == 0)
            return packet.Length <= dataOffset + 2;
        return false;
    }

    private static string GetMemberHintKey(string nickname, int serverId) => $"{serverId}:{nickname}";

    private static bool IsPlausibleJobCode(int jobCode)   => jobCode >= 1 && jobCode <= 40;
    private static bool IsValidServerId(int serverId)     => !string.IsNullOrEmpty(ServerMap.GetName(serverId));
    private static bool IsPlausibleCombatPower(int cp)    => cp >= 10000 && cp <= 999999;
}
