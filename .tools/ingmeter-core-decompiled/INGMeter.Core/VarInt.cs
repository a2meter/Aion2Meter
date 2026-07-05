using System;
using System.Collections.Generic;

namespace INGMeter.Core;

public static class VarInt
{
	public static bool TryRead(ReadOnlySpan<byte> data, int offset, out int value, out int length)
	{
		value = 0;
		length = 0;
		int num = 0;
		for (int i = 0; i < 5; i++)
		{
			int num2 = offset + i;
			if ((uint)num2 >= (uint)data.Length)
			{
				return false;
			}
			byte b = data[num2];
			value |= (b & 0x7F) << num;
			length++;
			if ((b & 0x80) == 0)
			{
				return true;
			}
			num += 7;
		}
		return false;
	}

	public static byte[] Write(int value)
	{
		List<byte> list = new List<byte>(5);
		uint num;
		for (num = (uint)value; num > 127; num >>= 7)
		{
			list.Add((byte)((num & 0x7F) | 0x80));
		}
		list.Add((byte)num);
		return list.ToArray();
	}
}
