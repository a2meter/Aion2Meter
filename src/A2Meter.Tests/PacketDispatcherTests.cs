using System.Text;
using A2Meter.Api;
using A2Meter.Dps.Protocol;
using Xunit;

namespace A2Meter.Tests;

public sealed class PacketDispatcherTests
{
    [Fact]
    public void CharacterLookupSelfFlagDoesNotRaiseUserInfoAsSelf()
    {
        var dispatcher = new PacketDispatcher(new SkillDatabase(new GameDataSnapshot()))
        {
            GetServerName = ServerMap.GetName,
        };
        (int EntityId, string Nickname, int ServerId, int JobCode, int IsSelf)? userInfo = null;
        (int EntityId, string Nickname, int ServerId, int JobCode, int Level, int CombatPower)? lookup = null;

        dispatcher.UserInfo += (entityId, nickname, serverId, jobCode, isSelf) =>
            userInfo = (entityId, nickname, serverId, jobCode, isSelf);
        dispatcher.CharacterLookup += (entityId, nickname, serverId, jobCode, level, combatPower) =>
            lookup = (entityId, nickname, serverId, jobCode, level, combatPower);

        byte[] packet = CreateCharacterLookupPacket(
            entityId: 3377,
            name: "SelfPlayer",
            jobCode: 37,
            isSelf: 1,
            level: 55);

        dispatcher.Dispatch(packet, 0, packet.Length);

        Assert.Equal((3377, "SelfPlayer", 0, 37, 0), userInfo);
        Assert.Equal((3377, "SelfPlayer", 0, 37, 55, 0), lookup);
    }

    [Fact]
    public void SelfInfoRaisesUserInfoAsSelf()
    {
        var dispatcher = new PacketDispatcher(new SkillDatabase(new GameDataSnapshot()))
        {
            GetServerName = ServerMap.GetName,
        };
        (int EntityId, string Nickname, int ServerId, int JobCode, int IsSelf)? userInfo = null;

        dispatcher.UserInfo += (entityId, nickname, serverId, jobCode, isSelf) =>
            userInfo = (entityId, nickname, serverId, jobCode, isSelf);

        byte[] packet = CreateSelfInfoPacket(
            entityId: 5390,
            name: "남힐",
            serverId: 1002,
            jobCode: 29);

        dispatcher.Dispatch(packet, 0, packet.Length);

        Assert.Equal((5390, "남힐[네자칸]", 1002, 29, 1), userInfo);
    }

    [Fact]
    public void SelfInfoWithoutNameMarkerRaisesUserInfoAsSelf()
    {
        var dispatcher = new PacketDispatcher(new SkillDatabase(new GameDataSnapshot()))
        {
            GetServerName = ServerMap.GetName,
        };
        (int EntityId, string Nickname, int ServerId, int JobCode, int IsSelf)? userInfo = null;

        dispatcher.UserInfo += (entityId, nickname, serverId, jobCode, isSelf) =>
            userInfo = (entityId, nickname, serverId, jobCode, isSelf);

        byte[] packet = CreateSelfInfoWithoutNameMarkerPacket(
            entityId: 5390,
            name: "남힐",
            serverId: 1002,
            jobCode: 30,
            extra: 1);

        dispatcher.Dispatch(packet, 0, packet.Length);

        Assert.Equal((5390, "남힐[네자칸]", 1002, 30, 1), userInfo);
    }

    private static byte[] CreateCharacterLookupPacket(int entityId, string name, int jobCode, int isSelf, int level)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        Assert.InRange(nameBytes.Length, 1, 127);

        var packet = new List<byte>
        {
            0x00,
            ProtocolOpcodeConfig.CharLookup.A,
            ProtocolOpcodeConfig.CharLookup.B,
            0x00,
            0x00,
            0x07,
            (byte)nameBytes.Length,
        };
        packet.AddRange(nameBytes);
        packet.Add((byte)jobCode);
        packet.AddRange(new byte[] { 0x00, 0x00, 0x00 });
        packet.Add(0x01);
        packet.Add((byte)isSelf);
        packet.Add((byte)level);
        packet.AddRange(new byte[7]);
        packet.AddRange(BitConverter.GetBytes(entityId));
        packet.AddRange(new byte[] { 0x00, 0x00 });
        return packet.ToArray();
    }

    private static byte[] CreateSelfInfoPacket(int entityId, string name, int serverId, int jobCode)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        Assert.InRange(nameBytes.Length, 1, 127);
        var packet = new List<byte>
        {
            0x00,
            ProtocolOpcodeConfig.SelfInfo.A,
            ProtocolOpcodeConfig.SelfInfo.B,
        };
        AddVarint(packet, entityId);
        packet.Add(0x07);
        packet.Add((byte)nameBytes.Length);
        packet.AddRange(nameBytes);
        packet.AddRange(BitConverter.GetBytes((ushort)serverId));
        packet.Add((byte)jobCode);
        return packet.ToArray();
    }

    private static byte[] CreateSelfInfoWithoutNameMarkerPacket(int entityId, string name, int serverId, int jobCode, int extra)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        Assert.InRange(nameBytes.Length, 1, 127);

        var packet = new List<byte>
        {
            0x00,
            ProtocolOpcodeConfig.SelfInfo.A,
            ProtocolOpcodeConfig.SelfInfo.B,
        };
        AddVarint(packet, entityId);
        packet.AddRange(new byte[] { 0x5F, 0xB1, 0xEB, 0x0A, 0x37 });
        packet.Add((byte)nameBytes.Length);
        packet.AddRange(nameBytes);
        packet.AddRange(BitConverter.GetBytes((ushort)serverId));
        packet.AddRange(BitConverter.GetBytes(jobCode));
        packet.Add((byte)extra);
        return packet.ToArray();
    }

    private static void AddVarint(List<byte> packet, int value)
    {
        var v = (uint)value;
        while (v >= 0x80)
        {
            packet.Add((byte)((v & 0x7F) | 0x80));
            v >>= 7;
        }
        packet.Add((byte)v);
    }
}
