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
            ProtocolFieldDescriptor[] fields = layout.Fields.OrderBy(static value => value.Kind).ToArray();
            writer.Write(checked((ushort)fields.Length));
            writer.Write((ushort)0);
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
        }

        foreach (ProtocolMessageLayout layout in snapshot.MessageLayouts.Values)
        {
            if (layout.Id == 0 || layout.MaxPayloadBytes is 0 or > MaxPayloadBytes ||
                layout.Fields.Length > MaxFieldsPerLayout)
            {
                throw new InvalidDataException($"Message layout {layout.Id} is out of bounds.");
            }
            foreach (ProtocolFieldDescriptor field in layout.Fields)
            {
                if (field.Kind == 0 || field.Size == 0 || field.MaxCount == 0 || field.Offset > layout.MaxPayloadBytes)
                {
                    throw new InvalidDataException($"Field {field.Kind} in layout {layout.Id} is out of bounds.");
                }
                ulong end = (ulong)field.Offset + ((ulong)field.Size * field.MaxCount);
                if (end > layout.MaxPayloadBytes)
                {
                    throw new InvalidDataException($"Field {field.Kind} exceeds layout {layout.Id}'s payload bound.");
                }
            }
        }
    }
}
