using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Namter.GameData;

public sealed record GameDataSnapshot(
    ulong DataVersion,
    uint SchemaVersion,
    uint ProtocolProfileVersion,
    string ProtocolProfileName,
    ImmutableArray<byte> PacketMagic,
    ImmutableArray<ushort> ServerPorts,
    FrozenDictionary<ushort, ProtocolOpcode> Opcodes,
    FrozenDictionary<uint, ProtocolMessageLayout> MessageLayouts,
    FrozenDictionary<uint, Boss> Bosses,
    FrozenDictionary<uint, Dungeon> Dungeons,
    FrozenDictionary<uint, Skill> Skills,
    FrozenDictionary<uint, Buff> Buffs)
{
    public int TotalHotCacheEntries => checked(
        Opcodes.Count + MessageLayouts.Count + MessageLayouts.Values.Sum(static layout => layout.Fields.Length) +
        Bosses.Count + Dungeons.Count + Skills.Count + Buffs.Count);
}

public sealed record ProtocolOpcode(
    uint Id,
    ushort Kind,
    string Name,
    ImmutableArray<byte> Tag,
    uint LayoutId);

public sealed record ProtocolMessageLayout(
    uint Id,
    string Name,
    uint MaxPayloadBytes,
    ImmutableArray<ProtocolFieldDescriptor> Fields);

public readonly record struct ProtocolFieldDescriptor(
    ushort Kind,
    ushort Flags,
    uint Offset,
    uint Size,
    uint MaxCount);

public sealed record Boss(uint Code, string Name);
public sealed record Dungeon(uint Code, string Name);
public sealed record Skill(uint Code, string Name);
public sealed record Buff(uint Code, string Name);

public sealed record GameDataCacheLimits(
    int MaxOpcodes,
    int MaxLayouts,
    int MaxLayoutFields,
    int MaxBosses,
    int MaxDungeons,
    int MaxSkills,
    int MaxBuffs,
    int MaxTotalEntries)
{
    public static GameDataCacheLimits Default { get; } = new(
        MaxOpcodes: 512,
        MaxLayouts: 256,
        MaxLayoutFields: 4096,
        MaxBosses: 1024,
        MaxDungeons: 512,
        MaxSkills: 32768,
        MaxBuffs: 32768,
        MaxTotalEntries: 71936);

    internal void Validate()
    {
        if (MaxOpcodes <= 0 || MaxLayouts <= 0 || MaxLayoutFields <= 0 ||
            MaxBosses <= 0 || MaxDungeons <= 0 || MaxSkills <= 0 || MaxBuffs <= 0 ||
            MaxTotalEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(GameDataCacheLimits), "All cache bounds must be positive.");
        }
    }
}
