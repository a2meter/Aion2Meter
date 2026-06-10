using System;
using A2Meter.Api;

namespace A2Meter.Dps.Protocol;

internal static class ProtocolOpcodeConfig
{
    public static readonly byte[] PacketMagic = { 0x06, 0x00, 0x36 };

    public static (byte A, byte B) Damage { get; private set; } = (0x04, 0x38);
    public static (byte A, byte B) Dot { get; private set; } = (0x05, 0x38);
    public static (byte A, byte B) BattleStats { get; private set; } = (0x2A, 0x38);
    public static (byte A, byte B) BattleStatsAlt { get; private set; } = (0x2B, 0x38);
    public static (byte A, byte B) SelfInfo { get; private set; } = (0x33, 0x36);
    public static (byte A, byte B) OtherInfo { get; private set; } = (0x45, 0x36);
    public static (byte A, byte B) MobSpawn { get; private set; } = (0x41, 0x36);
    public static (byte A, byte B) BossHp { get; private set; } = (0x01, 0x8D);
    public static (byte A, byte B) Guard { get; private set; } = (0x03, 0x36);
    public static (byte A, byte B) EntityRemoved { get; private set; } = (0x21, 0x8D);
    public static (byte A, byte B) CharLookup { get; private set; } = (0x4F, 0x36);

    public static byte PartyMarker { get; private set; } = 0x97;
    public static byte PartyList { get; private set; } = 0x01;
    public static byte PartyUpdate { get; private set; } = 0x02;
    public static byte PartyRequest { get; private set; } = 0x07;
    public static byte PartyAccept { get; private set; } = 0x0B;
    public static byte PartyDungeonExit { get; private set; } = 0x04;
    public static byte PartyLeave { get; private set; } = 0x1D;
    public static byte PartyBoardRefresh { get; private set; } = 0x2A;
    public static byte PartyBoardControl { get; private set; } = 0x13;

    public static void Configure(ProtocolOpcodeSnapshot? snapshot)
    {
        if (snapshot == null) return;

        ConfigurePacketMagic(snapshot.PacketMagic);

        Damage = Tag(snapshot, "damage", Damage);
        Dot = Tag(snapshot, "dot", Dot);
        BattleStats = Tag(snapshot, "battleStats", BattleStats);
        BattleStatsAlt = Tag(snapshot, "battleStatsAlt", BattleStatsAlt);
        SelfInfo = Tag(snapshot, "selfInfo", SelfInfo);
        OtherInfo = Tag(snapshot, "otherInfo", OtherInfo);
        MobSpawn = Tag(snapshot, "mobSpawn", MobSpawn);
        BossHp = Tag(snapshot, "bossHp", BossHp);
        Guard = Tag(snapshot, "guard", Guard);
        EntityRemoved = Tag(snapshot, "entityRemoved", EntityRemoved);
        CharLookup = Tag(snapshot, "charLookup", CharLookup);

        PartyMarker = Party(snapshot, "marker", PartyMarker);
        PartyList = Party(snapshot, "list", PartyList);
        PartyUpdate = Party(snapshot, "update", PartyUpdate);
        PartyRequest = Party(snapshot, "request", PartyRequest);
        PartyAccept = Party(snapshot, "accept", PartyAccept);
        PartyDungeonExit = Party(snapshot, "dungeonExit", PartyDungeonExit);
        PartyLeave = Party(snapshot, "leave", PartyLeave);
        PartyBoardRefresh = Party(snapshot, "boardRefresh", PartyBoardRefresh);
        PartyBoardControl = Party(snapshot, "boardControl", PartyBoardControl);
    }

    private static void ConfigurePacketMagic(int[]? values)
    {
        if (values == null) return;

        for (var i = 0; i < PacketMagic.Length && i < values.Length; i++)
            PacketMagic[i] = ToByte(values[i], PacketMagic[i]);
    }

    private static (byte A, byte B) Tag(ProtocolOpcodeSnapshot snapshot, string name, (byte A, byte B) fallback)
    {
        if (!snapshot.Tags.TryGetValue(name, out var value) || value.Length < 2) return fallback;
        return (ToByte(value[0], fallback.A), ToByte(value[1], fallback.B));
    }

    private static byte Party(ProtocolOpcodeSnapshot snapshot, string name, byte fallback)
    {
        if (!snapshot.Party.TryGetValue(name, out var value)) return fallback;
        return ToByte(value, fallback);
    }

    private static byte ToByte(int value, byte fallback)
        => value is >= 0 and <= 255 ? (byte)value : fallback;
}
