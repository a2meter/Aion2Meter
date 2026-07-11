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
    public void CompileIsIndependentOfSurrogateIdsAndInsertionOrder()
    {
        GameDataSnapshot first = CreateSnapshot(reverseInsertionOrder: false, swapSurrogateIds: false);
        GameDataSnapshot second = CreateSnapshot(reverseInsertionOrder: true, swapSurrogateIds: true);

        Assert.Equal(ProtocolSnapshotCompiler.Compile(first), ProtocolSnapshotCompiler.Compile(second));
    }

    [Fact]
    public void CompileRejectsDuplicateWireKindsEvenWhenDictionaryKeysDiffer()
    {
        GameDataSnapshot snapshot = CreateSnapshot(reverseInsertionOrder: false);
        var opcodes = new Dictionary<ushort, ProtocolOpcode>
        {
            [10] = new ProtocolOpcode(10, 7, "first", [0x01], 0),
            [20] = new ProtocolOpcode(20, 7, "second", [0x02], 0),
        }.ToFrozenDictionary();
        snapshot = snapshot with { Opcodes = opcodes };

        Assert.Throws<InvalidDataException>(() => ProtocolSnapshotCompiler.Compile(snapshot));
    }

    [Fact]
    public void CompileRejectsDuplicateWireTagsAcrossDifferentKinds()
    {
        GameDataSnapshot snapshot = CreateSnapshot(reverseInsertionOrder: false);
        ProtocolOpcode damage = snapshot.Opcodes[1];
        ProtocolOpcode dot = snapshot.Opcodes[2] with { Tag = damage.Tag };
        snapshot = snapshot with
        {
            Opcodes = new Dictionary<ushort, ProtocolOpcode> { [1] = damage, [2] = dot }.ToFrozenDictionary(),
        };

        Assert.Throws<InvalidDataException>(() => ProtocolSnapshotCompiler.Compile(snapshot));
    }

    [Fact]
    public void CompileAllowsUnknownKindsOnlyWithoutTypedLayouts()
    {
        GameDataSnapshot snapshot = CreateSnapshot(reverseInsertionOrder: false);
        var unknownWithoutLayout = new ProtocolOpcode(77, 999, "unknown", [0x55], 0);
        snapshot = snapshot with
        {
            Opcodes = new Dictionary<ushort, ProtocolOpcode> { [999] = unknownWithoutLayout }.ToFrozenDictionary(),
            MessageLayouts = FrozenDictionary<uint, ProtocolMessageLayout>.Empty,
        };
        _ = ProtocolSnapshotCompiler.Compile(snapshot);

        snapshot = snapshot with
        {
            Opcodes = new Dictionary<ushort, ProtocolOpcode>
            {
                [999] = unknownWithoutLayout with { LayoutId = 1 },
            }.ToFrozenDictionary(),
            MessageLayouts = CreateSnapshot(false).MessageLayouts,
        };
        Assert.Throws<InvalidDataException>(() => ProtocolSnapshotCompiler.Compile(snapshot));

        snapshot = CreateSnapshot(false) with
        {
            Opcodes = new Dictionary<ushort, ProtocolOpcode>
            {
                [1] = CreateSnapshot(false).Opcodes[1] with { LayoutId = 0 },
            }.ToFrozenDictionary(),
            MessageLayouts = FrozenDictionary<uint, ProtocolMessageLayout>.Empty,
        };
        Assert.Throws<InvalidDataException>(() => ProtocolSnapshotCompiler.Compile(snapshot));
    }

    [Fact]
    public void CompilePreservesSnapshotFieldOrderForSequentialCommands()
    {
        GameDataSnapshot snapshot = CreateSnapshot(reverseInsertionOrder: false);
        ProtocolMessageLayout layout = snapshot.MessageLayouts[1];
        ImmutableArray<ProtocolFieldDescriptor> fields = layout.Fields;
        layout = layout with { Fields = [fields[1], fields[0], .. fields[2..]] };
        snapshot = snapshot with
        {
            MessageLayouts = new Dictionary<uint, ProtocolMessageLayout>
            {
                [1] = layout,
                [2] = snapshot.MessageLayouts[2],
            }.ToFrozenDictionary(),
        };

        byte[] bytes = ProtocolSnapshotCompiler.Compile(snapshot);
        var reader = new SnapshotReader(bytes);
        reader.Skip(ProtocolSnapshotCompiler.HeaderSize);
        reader.Skip(reader.ReadUInt16());
        int portCount = reader.ReadUInt16();
        reader.Skip(portCount * 2);
        int opcodeCount = checked((int)reader.ReadUInt32());
        for (var index = 0; index < opcodeCount; index++)
        {
            reader.Skip(2);
            reader.Skip(reader.ReadUInt16());
            reader.Skip(4);
        }
        Assert.Equal(2U, reader.ReadUInt32());
        Assert.Equal(1U, reader.ReadUInt32());
        reader.Skip(4);
        Assert.Equal(9, reader.ReadUInt16());
        reader.Skip(2);
        Assert.Equal(2, reader.ReadUInt16());
    }

    [Fact]
    public void CompileRejectsZeroDataVersion()
    {
        GameDataSnapshot snapshot = CreateSnapshot(reverseInsertionOrder: false) with { DataVersion = 0 };

        Assert.Throws<InvalidDataException>(() => ProtocolSnapshotCompiler.Compile(snapshot));
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
        Assert.Equal(9, reader.ReadUInt16());
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

    private static GameDataSnapshot CreateSnapshot(bool reverseInsertionOrder, bool swapSurrogateIds = false)
    {
        var opcodes = new Dictionary<ushort, ProtocolOpcode>();
        var layouts = new Dictionary<uint, ProtocolMessageLayout>();
        var opcodeValues = new[]
        {
            new ProtocolOpcode(swapSurrogateIds ? 100U : 900U, 2, "dot", [0x05, 0x38], 2),
            new ProtocolOpcode(swapSurrogateIds ? 900U : 100U, 1, "damage", [0x04, 0x38], 1),
        };
        var layoutValues = new[]
        {
            new ProtocolMessageLayout(2, "dot", 128, DamageFields()),
            new ProtocolMessageLayout(1, "damage", 64, DamageFields()),
        };
        foreach (var opcode in reverseInsertionOrder ? opcodeValues : opcodeValues.Reverse()) opcodes.Add(opcode.Kind, opcode);
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

    private static ImmutableArray<ProtocolFieldDescriptor> DamageFields() =>
    [
        new(1, 0, 0, 4, 1),
        new(2, 0, 4, 4, 1),
        new(4, 0, 8, 4, 1),
        new(13, 0, 12, 8, 1),
        new(14, 0, 20, 8, 1),
        new(15, 0, 28, 8, 1),
        new(18, 0, 36, 4, 1),
        new(22, 0, 40, 1, 1),
        new(23, 0, 41, 1, 1),
    ];

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
