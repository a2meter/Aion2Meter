using System.Buffers.Binary;
using System.Collections.Immutable;

namespace Namter.GameData;

public static class ProtocolSnapshotCompiler
{
    public const ushort FormatVersion = 1;
    public const ushort HeaderSize = 28;
    public const int MaxPacketMagicLength = 32;
    public const int MaxServerPorts = 256;
    public const int MaxOpcodes = 4096;
    public const int MaxLayouts = 1024;
    public const int MaxFieldsPerLayout = 256;
    public const int MaxTagLength = 32;
    public const uint MaxPayloadBytes = 16 * 1024 * 1024;
    public const int MaxSnapshotBytes = 16 * 1024 * 1024;

    private const int CrcOffset = 12;

    public static byte[] Compile(GameDataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("NMPS"u8);
        writer.Write(FormatVersion);
        writer.Write(HeaderSize);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(snapshot.DataVersion);
        writer.Write(snapshot.ProtocolProfileVersion);

        writer.Write(checked((ushort)snapshot.PacketMagic.Length));
        writer.Write(snapshot.PacketMagic.AsSpan());

        ushort[] ports = snapshot.ServerPorts.Distinct().Order().ToArray();
        writer.Write(checked((ushort)ports.Length));
        foreach (ushort port in ports) writer.Write(port);

        ProtocolOpcode[] opcodes = snapshot.Opcodes.Values.OrderBy(static value => value.Kind).ToArray();
        writer.Write(checked((uint)opcodes.Length));
        foreach (ProtocolOpcode opcode in opcodes)
        {
            writer.Write(opcode.Kind);
            writer.Write(checked((ushort)opcode.Tag.Length));
            writer.Write(opcode.Tag.AsSpan());
            writer.Write(opcode.LayoutId);
        }

        ProtocolMessageLayout[] layouts = snapshot.MessageLayouts.Values.OrderBy(static value => value.Id).ToArray();
        writer.Write(checked((uint)layouts.Length));
        foreach (ProtocolMessageLayout layout in layouts)
        {
            writer.Write(layout.Id);
            writer.Write(layout.MaxPayloadBytes);
            ProtocolFieldDescriptor[] fields = layout.Fields.ToArray();
            writer.Write(checked((ushort)fields.Length));
            writer.Write(layout.ParserStrategy);
            foreach (ProtocolFieldDescriptor field in fields)
            {
                writer.Write(field.Kind);
                writer.Write(field.Flags);
                writer.Write(field.Offset);
                writer.Write(field.Size);
                writer.Write(field.MaxCount);
            }
        }

        writer.Flush();
        byte[] result = stream.ToArray();
        if (result.Length > MaxSnapshotBytes)
        {
            throw new InvalidDataException($"Protocol snapshot exceeds {MaxSnapshotBytes} bytes.");
        }
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4), checked((uint)result.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(CrcOffset, 4), ComputeCrc32WithZeroedChecksum(result));
        return result;
    }

    public static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes) crc = UpdateCrc32(crc, value);
        return ~crc;
    }

    public static uint ComputeCrc32WithZeroedChecksum(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        for (var index = 0; index < bytes.Length; index++)
        {
            byte value = index is >= CrcOffset and < CrcOffset + sizeof(uint) ? (byte)0 : bytes[index];
            crc = UpdateCrc32(crc, value);
        }
        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            uint mask = 0U - (crc & 1U);
            crc = (crc >> 1) ^ (0xEDB88320U & mask);
        }
        return crc;
    }

    private static void ValidateSnapshot(GameDataSnapshot snapshot)
    {
        if (snapshot.DataVersion == 0)
        {
            throw new InvalidDataException("Data version must be positive.");
        }
        if (snapshot.ProtocolProfileVersion == 0)
        {
            throw new InvalidDataException("Protocol profile version must be positive.");
        }
        if (snapshot.PacketMagic.Length is < 1 or > MaxPacketMagicLength)
        {
            throw new InvalidDataException("Packet magic length is out of bounds.");
        }
        if (snapshot.ServerPorts.Length is < 1 or > MaxServerPorts || snapshot.ServerPorts.Any(static port => port == 0))
        {
            throw new InvalidDataException("Server-port data is out of bounds.");
        }
        if (snapshot.Opcodes.Count is < 1 or > MaxOpcodes)
        {
            throw new InvalidDataException("Opcode count is out of bounds.");
        }
        if (snapshot.Opcodes.Values.Select(static opcode => opcode.Kind).Distinct().Count() != snapshot.Opcodes.Count ||
            snapshot.Opcodes.Any(static pair => pair.Key != pair.Value.Kind))
        {
            throw new InvalidDataException("Opcode wire kinds must be unique and match their dictionary keys.");
        }
        ProtocolOpcode[] wireOpcodes = snapshot.Opcodes.Values.ToArray();
        for (var index = 0; index < wireOpcodes.Length; index++)
        {
            for (var previous = 0; previous < index; previous++)
            {
                if (wireOpcodes[index].Tag.AsSpan().SequenceEqual(wireOpcodes[previous].Tag.AsSpan()))
                {
                    throw new InvalidDataException("Opcode wire tags must be unique.");
                }
            }
        }
        if (snapshot.MessageLayouts.Count > MaxLayouts)
        {
            throw new InvalidDataException("Message-layout count is out of bounds.");
        }

        foreach (ProtocolOpcode opcode in snapshot.Opcodes.Values)
        {
            if (opcode.Id == 0 || opcode.Kind == 0 || opcode.Tag.Length is < 1 or > MaxTagLength)
            {
                throw new InvalidDataException($"Opcode {opcode.Id} is out of bounds.");
            }
            if (opcode.LayoutId != 0 && !snapshot.MessageLayouts.ContainsKey(opcode.LayoutId))
            {
                throw new InvalidDataException($"Opcode {opcode.Id} references missing layout {opcode.LayoutId}.");
            }
            bool knownKind = ProtocolFieldContract.TryGetExactMask(opcode.Kind, out _);
            if ((knownKind && opcode.LayoutId == 0) || (!knownKind && opcode.LayoutId != 0))
            {
                throw new InvalidDataException(
                    knownKind
                        ? $"Known opcode {opcode.Id} requires a typed layout."
                        : $"Unknown opcode {opcode.Id} cannot reference a typed layout.");
            }
            if (knownKind && !ProtocolFieldContract.HasExactFields(
                    opcode.Kind,
                    snapshot.MessageLayouts[opcode.LayoutId].Fields))
            {
                throw new InvalidDataException($"Opcode {opcode.Id} layout fields do not match kind {opcode.Kind}.");
            }
        }

        foreach (ProtocolMessageLayout layout in snapshot.MessageLayouts.Values)
        {
            if (layout.Id == 0 || layout.MaxPayloadBytes is 0 or > MaxPayloadBytes || layout.ParserStrategy > 1 ||
                layout.Fields.Length > MaxFieldsPerLayout)
            {
                throw new InvalidDataException($"Message layout {layout.Id} is out of bounds.");
            }
            var seenKinds = new HashSet<ushort>();
            bool? sequentialMode = null;
            ulong sequentialMaximum = 0;
            foreach (ProtocolFieldDescriptor field in layout.Fields)
            {
                if (!seenKinds.Add(field.Kind) || !IsValidFieldEncoding(field))
                {
                    throw new InvalidDataException($"Field {field.Kind} in layout {layout.Id} is out of bounds.");
                }
                bool sequential = field.Flags >= 3;
                if (sequentialMode.HasValue && sequentialMode.Value != sequential)
                {
                    throw new InvalidDataException($"Layout {layout.Id} mixes absolute and sequential fields.");
                }
                sequentialMode = sequential;
                ulong encodedSize = MaximumEncodedSize(field);
                ulong end;
                if (sequential)
                {
                    sequentialMaximum = checked(sequentialMaximum + field.Offset + encodedSize);
                    end = sequentialMaximum;
                }
                else
                {
                    end = (ulong)field.Offset + encodedSize;
                }
                if (end > layout.MaxPayloadBytes)
                {
                    throw new InvalidDataException($"Field {field.Kind} exceeds layout {layout.Id}'s payload bound.");
                }
            }
        }
    }

    private static bool IsValidFieldEncoding(ProtocolFieldDescriptor field)
    {
        if (field.Kind is 0 or > 26 || field.Flags > 5 || field.Size == 0 || field.MaxCount == 0)
        {
            return false;
        }
        ushort encoding = field.Flags >= 3 ? checked((ushort)(field.Flags - 3)) : field.Flags;
        if (encoding == 2)
        {
            return field.Kind == 26 && field.Size == 1;
        }
        if (field.Kind == 26)
        {
            return false;
        }
        if (encoding == 1)
        {
            return field.Size == 1 && field.MaxCount is >= 1 and <= 5;
        }
        if (field.MaxCount != 1)
        {
            return false;
        }
        return field.Kind switch
        {
            <= 10 or 18 or 19 => field.Size is 1 or 2 or 4,
            11 or 12 => field.Size is 1 or 2,
            >= 13 and <= 17 => field.Size is 1 or 2 or 4 or 8,
            >= 20 and <= 25 => field.Size == 1,
            _ => false,
        };
    }

    private static ulong MaximumEncodedSize(ProtocolFieldDescriptor field)
    {
        ushort encoding = field.Flags >= 3 ? checked((ushort)(field.Flags - 3)) : field.Flags;
        return encoding switch
        {
            1 => field.MaxCount,
            2 => checked(5UL + field.MaxCount),
            _ => field.Size,
        };
    }
}

internal static class ProtocolFieldContract
{
    internal static bool HasExactFields(
        ushort opcodeKind,
        ImmutableArray<ProtocolFieldDescriptor> fields)
    {
        if (!TryGetExactMask(opcodeKind, out uint expected))
        {
            return false;
        }
        uint actual = 0;
        foreach (ProtocolFieldDescriptor field in fields)
        {
            if (field.Kind is 0 or > 26)
            {
                return false;
            }
            actual |= 1U << (field.Kind - 1);
        }
        return actual == expected;
    }

    internal static bool TryGetExactMask(ushort opcodeKind, out uint mask)
    {
        mask = opcodeKind switch
        {
            1 or 2 => Mask(1, 2, 4, 13, 14, 15, 18, 22, 23),
            3 or 4 => Mask(2, 3, 5, 19, 21),
            5 or 6 or 11 => Mask(1, 3, 11, 12, 24, 26),
            7 => Mask(1, 3, 6, 7, 16, 17, 25, 26),
            8 => Mask(1, 7, 16, 17),
            10 => Mask(1),
            101 or 102 or 104 or 105 or 106 or 107 or 108 => Mask(1, 8, 9, 10, 21, 26),
            103 or 201 => Mask(8, 9, 20, 26),
            202 => Mask(1, 20),
            203 => Mask(1, 2, 4, 21),
            _ => 0,
        };
        return mask != 0;
    }

    private static uint Mask(params int[] kinds)
    {
        uint result = 0;
        foreach (int kind in kinds)
        {
            result |= 1U << (kind - 1);
        }
        return result;
    }
}
