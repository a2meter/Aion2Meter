using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Immutable;
using Namter.Core.Interop;
using Namter.GameData;

namespace Namter.Tests.Unit.GameData;

public sealed class ProtocolSnapshotCompilerTests
{
    [Fact]
    public void CompileProducesDeterministicCanonicalLittleEndianBytes()
    {
        GameDataSnapshot first = CreateSnapshot(reverseInsertionOrder: false);
        GameDataSnapshot second = CreateSnapshot(reverseInsertionOrder: true);

        byte[] firstBytes = ProtocolSnapshotCompiler.Compile(first);
        byte[] secondBytes = ProtocolSnapshotCompiler.Compile(second);

        Assert.Equal(firstBytes, secondBytes);
        Assert.Equal("NMPS"u8.ToArray(), firstBytes[..4]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(firstBytes.AsSpan(4, 2)));
        Assert.Equal(ProtocolSnapshotCompiler.HeaderSize,
            BinaryPrimitives.ReadUInt16LittleEndian(firstBytes.AsSpan(6, 2)));
        Assert.Equal(checked((uint)firstBytes.Length), BinaryPrimitives.ReadUInt32LittleEndian(firstBytes.AsSpan(8, 4)));
        Assert.Equal(0x0102030405060708UL, BinaryPrimitives.ReadUInt64LittleEndian(firstBytes.AsSpan(16, 8)));
        Assert.Equal(0x01020304U, BinaryPrimitives.ReadUInt32LittleEndian(firstBytes.AsSpan(24, 4)));
        Assert.Equal(ProtocolSnapshotCompiler.ComputeCrc32WithZeroedChecksum(firstBytes),
            BinaryPrimitives.ReadUInt32LittleEndian(firstBytes.AsSpan(12, 4)));
    }

    [Fact]
    public void CompileUsesNumericOrderingForPortsOpcodesLayoutsAndFields()
    {
        byte[] bytes = ProtocolSnapshotCompiler.Compile(CreateSnapshot(reverseInsertionOrder: true));
        var reader = new SnapshotReader(bytes);
        reader.Skip(ProtocolSnapshotCompiler.HeaderSize);
        Assert.Equal(new byte[] { 0x06, 0x00, 0x36 }, reader.ReadBytes(reader.ReadUInt16()));
        Assert.Equal(2, reader.ReadUInt16());
        Assert.Equal(80, reader.ReadUInt16());
        Assert.Equal(13328, reader.ReadUInt16());
        Assert.Equal(2U, reader.ReadUInt32());
        Assert.Equal(1, reader.ReadUInt16());
        reader.Skip(reader.ReadUInt16());
        Assert.Equal(1U, reader.ReadUInt32());
        Assert.Equal(2, reader.ReadUInt16());
        reader.Skip(reader.ReadUInt16());
        Assert.Equal(2U, reader.ReadUInt32());
        Assert.Equal(2U, reader.ReadUInt32());
        Assert.Equal(1U, reader.ReadUInt32());
        reader.Skip(4);
        Assert.Equal(2, reader.ReadUInt16());
        reader.Skip(2);
        Assert.Equal(1, reader.ReadUInt16());
        reader.Skip(14);
        Assert.Equal(2, reader.ReadUInt16());
    }

    [Fact]
    public void CompileRejectsUnboundedFieldDescriptors()
    {
        GameDataSnapshot snapshot = CreateSnapshot(reverseInsertionOrder: false);
        ProtocolMessageLayout layout = snapshot.MessageLayouts[1] with
        {
            Fields = [new ProtocolFieldDescriptor(1, 0, 63, 4, 1)],
        };
        snapshot = snapshot with
        {
            MessageLayouts = new Dictionary<uint, ProtocolMessageLayout> { [1] = layout }.ToFrozenDictionary(),
        };

        Assert.Throws<InvalidDataException>(() => ProtocolSnapshotCompiler.Compile(snapshot));
    }

    [Fact]
    public void Crc32MatchesTheStandardIeeeCheckVector()
    {
        Assert.Equal(0xCBF43926U, ProtocolSnapshotCompiler.ComputeCrc32("123456789"u8));
    }

    [Fact]
    public async Task CompiledManagedSnapshotIsAcceptedByNativeCore()
    {
        byte[] bytes = ProtocolSnapshotCompiler.Compile(CreateSnapshot(reverseInsertionOrder: true));
        await using var core = new NativeCore();

        core.SetProtocolSnapshot(bytes);
    }

    private static GameDataSnapshot CreateSnapshot(bool reverseInsertionOrder)
    {
        var opcodes = new Dictionary<uint, ProtocolOpcode>();
        var layouts = new Dictionary<uint, ProtocolMessageLayout>();
        var opcodeValues = new[]
        {
            new ProtocolOpcode(2, 2, "dot", [0x05, 0x38], 2),
            new ProtocolOpcode(1, 1, "damage", [0x04, 0x38], 1),
        };
        var layoutValues = new[]
        {
            new ProtocolMessageLayout(2, "dot", 128,
                [new ProtocolFieldDescriptor(4, 0, 12, 4, 1)]),
            new ProtocolMessageLayout(1, "damage", 64,
                [new ProtocolFieldDescriptor(2, 0, 8, 4, 1), new ProtocolFieldDescriptor(1, 0, 4, 4, 1)]),
        };
        foreach (var opcode in reverseInsertionOrder ? opcodeValues : opcodeValues.Reverse()) opcodes.Add(opcode.Id, opcode);
        foreach (var layout in reverseInsertionOrder ? layoutValues : layoutValues.Reverse()) layouts.Add(layout.Id, layout);

        return new GameDataSnapshot(
            0x0102030405060708UL,
            1,
            0x01020304U,
            "test-profile",
            [0x06, 0x00, 0x36],
            reverseInsertionOrder ? [13328, 80] : [80, 13328],
            opcodes.ToFrozenDictionary(),
            layouts.ToFrozenDictionary(),
            FrozenDictionary<uint, Boss>.Empty,
            FrozenDictionary<uint, Dungeon>.Empty,
            FrozenDictionary<uint, Skill>.Empty,
            FrozenDictionary<uint, Buff>.Empty);
    }

    private ref struct SnapshotReader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> bytes = bytes;
        private int offset;

        public void Skip(int count) => offset += count;
        public ushort ReadUInt16() { ushort value = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]); offset += 2; return value; }
        public uint ReadUInt32() { uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]); offset += 4; return value; }
        public byte[] ReadBytes(int count) { byte[] value = bytes.Slice(offset, count).ToArray(); offset += count; return value; }
    }
}
