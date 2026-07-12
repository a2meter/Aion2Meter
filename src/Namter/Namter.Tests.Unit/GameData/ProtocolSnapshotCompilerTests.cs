using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Immutable;
using Namter.Core.Interop;
using Namter.GameData;

namespace Namter.Tests.Unit.GameData;

public sealed class ProtocolSnapshotCompilerTests
{
    public static IEnumerable<object[]> KnownFieldContracts()
    {
        yield return [1, new ushort[] { 1, 2, 4, 13, 14, 15, 18, 22, 23 }];
        yield return [2, new ushort[] { 1, 2, 4, 13, 14, 15, 18, 22, 23 }];
        yield return [3, new ushort[] { 2, 3, 5, 19, 21 }];
        yield return [4, new ushort[] { 2, 3, 5, 19, 21 }];
        yield return [5, new ushort[] { 1, 3, 11, 12, 24, 26 }];
        yield return [6, new ushort[] { 1, 3, 11, 12, 24, 26 }];
        yield return [7, new ushort[] { 1, 3, 6, 7, 16, 17, 25, 26 }];
        yield return [8, new ushort[] { 1, 7, 16, 17 }];
        yield return [10, new ushort[] { 1 }];
        yield return [11, new ushort[] { 1, 3, 11, 12, 24, 26 }];
        foreach (ushort kind in new ushort[] { 101, 102, 104, 105, 106, 107, 108 })
            yield return [kind, new ushort[] { 1, 8, 9, 10, 21, 26 }];
        yield return [103, new ushort[] { 8, 9, 20, 26 }];
        yield return [201, new ushort[] { 8, 9, 20, 26 }];
        yield return [202, new ushort[] { 1, 20 }];
    }

    [Theory]
    [MemberData(nameof(KnownFieldContracts))]
    public async Task CompileProducesNativeValidSnapshotsForEveryExactKnownFieldSet(
        ushort opcodeKind,
        ushort[] fieldKinds)
    {
        byte[] bytes = ProtocolSnapshotCompiler.Compile(CreateSingleOpcodeSnapshot(opcodeKind, fieldKinds));
        await using var core = new NativeCore();

        core.SetProtocolSnapshot(bytes);
    }

    [Theory]
    [MemberData(nameof(KnownFieldContracts))]
    public void CompileRejectsEachKnownFieldSetWhenOneRequiredFieldIsMissing(
        ushort opcodeKind,
        ushort[] fieldKinds)
    {
        Assert.Throws<InvalidDataException>(() => ProtocolSnapshotCompiler.Compile(
            CreateSingleOpcodeSnapshot(opcodeKind, fieldKinds[1..])));
    }

    [Theory]
    [MemberData(nameof(KnownFieldContracts))]
    public void CompileRejectsEachKnownFieldSetWhenOneDisallowedFieldIsExtra(
        ushort opcodeKind,
        ushort[] fieldKinds)
    {
        ushort extra = Enumerable.Range(1, 26).Select(static value => (ushort)value)
            .First(value => !fieldKinds.Contains(value));
        Assert.Throws<InvalidDataException>(() => ProtocolSnapshotCompiler.Compile(
            CreateSingleOpcodeSnapshot(opcodeKind, [.. fieldKinds, extra])));
    }

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
    public void CompileCarriesOnlySupportedDatabaseSelectedParserStrategies()
    {
        GameDataSnapshot snapshot = CreateSnapshot(reverseInsertionOrder: false);
        ProtocolMessageLayout selected = snapshot.MessageLayouts[1] with { ParserStrategy = 1 };
        snapshot = snapshot with
        {
            MessageLayouts = snapshot.MessageLayouts.ToDictionary().ToFrozenDictionary(
                pair => pair.Key, pair => pair.Key == 1 ? selected : pair.Value),
        };

        byte[] bytes = ProtocolSnapshotCompiler.Compile(snapshot);
        var reader = new SnapshotReader(bytes);
        reader.Skip(ProtocolSnapshotCompiler.HeaderSize);
        reader.Skip(reader.ReadUInt16());
        reader.Skip(reader.ReadUInt16() * 2);
        int opcodeCount = checked((int)reader.ReadUInt32());
        for (int index = 0; index < opcodeCount; index++)
        {
            reader.Skip(2);
            reader.Skip(reader.ReadUInt16());
            reader.Skip(4);
        }
        reader.Skip(4 + 4 + 4);
        reader.Skip(2);
        Assert.Equal(1, reader.ReadUInt16());

        snapshot = snapshot with
        {
            MessageLayouts = snapshot.MessageLayouts.ToDictionary().ToFrozenDictionary(
                pair => pair.Key, pair => pair.Key == 1 ? pair.Value with { ParserStrategy = 2 } : pair.Value),
        };
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
            FrozenDictionary<uint, Buff>.Empty,
            FrozenDictionary<ushort, ushort>.Empty);
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

    private static GameDataSnapshot CreateSingleOpcodeSnapshot(ushort opcodeKind, ushort[] fieldKinds)
    {
        uint offset = 0;
        var fields = ImmutableArray.CreateBuilder<ProtocolFieldDescriptor>(fieldKinds.Length);
        foreach (ushort kind in fieldKinds)
        {
            (ushort flags, uint size, uint maxCount) = kind switch
            {
                26 => ((ushort)2, 1U, 20U),
                <= 10 or 18 or 19 => ((ushort)0, 4U, 1U),
                11 or 12 => ((ushort)0, 2U, 1U),
                >= 13 and <= 17 => ((ushort)0, 8U, 1U),
                _ => ((ushort)0, 1U, 1U),
            };
            fields.Add(new ProtocolFieldDescriptor(kind, flags, offset, size, maxCount));
            offset += flags == 2 ? 25U : size;
        }
        var opcode = new ProtocolOpcode(opcodeKind, opcodeKind, $"kind-{opcodeKind}",
            [(byte)(opcodeKind & 0xff), (byte)(opcodeKind >> 8)], 1);
        var layout = new ProtocolMessageLayout(1, $"kind-{opcodeKind}", 512, fields.ToImmutable());
        return new GameDataSnapshot(
            1, 1, 1, "test", [0x06, 0x00, 0x36], [13328],
            new Dictionary<ushort, ProtocolOpcode> { [opcodeKind] = opcode }.ToFrozenDictionary(),
            new Dictionary<uint, ProtocolMessageLayout> { [1] = layout }.ToFrozenDictionary(),
            FrozenDictionary<uint, Boss>.Empty, FrozenDictionary<uint, Dungeon>.Empty,
            FrozenDictionary<uint, Skill>.Empty, FrozenDictionary<uint, Buff>.Empty,
            FrozenDictionary<ushort, ushort>.Empty);
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
