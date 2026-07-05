using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace INGMeter.Core;

internal static class PacketProcessorNative
{
	public struct Config
	{
		public int serverPort;

		public int tcpReorder;

		public int workerCount;

		public int maxBufferSize;

		public int maxReorderBytes;

		public uint authNonce;

		public uint authToken;
	}

	private struct AuthBlock
	{
		public uint magic;

		public uint version;

		public uint hostClass;

		public uint hostFingerprint;
	}

	public struct DamageRecord
	{
		public int actorId;

		public int targetId;

		public int skillCode;

		public int rawSkillCode;

		public byte damageType;

		private byte _pad1;

		private byte _pad2;

		private byte _pad3;

		public int damage;

		public uint specialFlags;

		public int multiHitCount;

		public int multiHitDamage;

		public int healAmount;

		public int isDot;

		public ulong timestampMs;
	}

	public struct Callbacks
	{
		public OnUserInfoDelegate onUserInfo;

		public OnMobSpawnDelegate onMobSpawn;

		public OnSummonDelegate onSummon;

		public OnDamageRecordDelegate onDamage;

		public OnEntityRemovedDelegate onEntityRemoved;

		public OnBuffAppliedDelegate onBuffApplied;

		public OnBuffStateDelegate onBuffState;

		public GetSkillNameDelegate getSkillName;

		public ContainsSkillCodeDelegate containsSkillCode;

		public OnEntityPairDelegate onEntityPair;

		public OnExtendedUserInfoDelegate onExtendedUserInfo;

		public OnEntityUIntDelegate onEntityUInt;

		public OnEntityGaugeDelegate onEntityGauge;

		public OnEntityTripleDelegate onEntityTriple;

		public OnEntityStateDelegate onEntityState;

		public OnAbyssArtifactStateDelegate onAbyssArtifactState;

		public nint userdata;
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnUserInfoDelegate(int entityId, nint nickname, int serverId, int jobCode, int extra, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnUserInfoExDelegate(int entityId, nint nickname, int serverId, int jobCode, int extra, int characterNumber, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnMobSpawnDelegate(int mobId, int mobCode, int hp, int extra1, int extra2, int rawHp, int stateMarker, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnSummonDelegate(int actorId, int petId, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnDamageRecordDelegate(nint damageRecord, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnEntityRemovedDelegate(int entityId, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnBuffAppliedDelegate(int targetId, int ownerId, int buffId, int skillId, uint duration, ulong startedAtMs, ulong expiresAtMs, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnBuffStateDelegate(int targetId, int buffId, int skillId, uint duration, ulong startedAtMs, ulong expiresAtMs, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate nint GetSkillNameDelegate(int skillCode, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate int ContainsSkillCodeDelegate(int skillCode, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnEntityPairDelegate(int firstId, int secondId, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnExtendedUserInfoDelegate(int entityId, int slot, int mode, uint value1, int serverId, nint nickname, int jobCode, int level, int gearScore, int combatPower, int source, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnEntityUIntDelegate(int entityId, uint value, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnEntityGaugeDelegate(int entityId, uint current, uint maximum, int state, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnEntityTripleDelegate(int entityId, int value1, int value2, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnEntityStateDelegate(int entityId, int state, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnAbyssArtifactStateDelegate(int areaCode, int artifactId, int ownerSide, int ownerServerId, int matchServer1Id, int matchServer2Id, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnZoneEntryDelegate(int contentCode, int kind, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnStigmaSkillLevelDelegate(int ownerId, int skillCode, int baseSkillCode, int effectiveLevel, int baseSkillLevel, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate int IsStigmaSkillCodeDelegate(int skillCode, nint userdata);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void OnLocalPlayerStateDelegate(int kind, long value, long maxValue, long bonusValue, int entityId, int serverId, int characterNumber, nint context, nint userdata);

	private const string DLL = "INGParser.dll";

	private const uint AuthSaltA = 2404199313u;

	private const uint AuthSaltB = 3345042991u;

	private const uint AuthSaltC = 1029285813u;

	private const uint AuthSaltD = 3068314513u;

	private const uint AuthBlockMagic = 1229866829u;

	private const uint AuthBlockVersion = 2u;

	public const uint AuthHostApp = 1095782449u;

	public static uint CreateAuthNonce()
	{
		Span<byte> obj = stackalloc byte[4];
		RandomNumberGenerator.Fill(obj);
		uint num = BitConverter.ToUInt32(obj);
		if (num != 0)
		{
			return num;
		}
		return 1u;
	}

	public static nint CreateAuthBlock(uint hostClass)
	{
		AuthBlock structure = new AuthBlock
		{
			magic = 1229866829u,
			version = 2u,
			hostClass = hostClass,
			hostFingerprint = CreateHostFingerprint(hostClass)
		};
		nint num = Marshal.AllocHGlobal(Marshal.SizeOf<AuthBlock>());
		Marshal.StructureToPtr(structure, num, fDeleteOld: false);
		return num;
	}

	public static void FreeAuthBlock(nint ptr)
	{
		if (ptr != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(ptr);
		}
	}

	public static uint ComputeAuthToken(uint nonce, in Config config, in Callbacks callbacks)
	{
		AuthBlock authBlock = ((callbacks.userdata == IntPtr.Zero) ? default(AuthBlock) : Marshal.PtrToStructure<AuthBlock>(callbacks.userdata));
		return RotateLeft(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(MixAuth(nonce ^ 0x8F4D2B91u, 3345042991uL), (uint)config.serverPort), (uint)config.tcpReorder), (uint)config.maxBufferSize), (uint)config.maxReorderBytes), PointerOf(callbacks.onUserInfo)), PointerOf(callbacks.onMobSpawn)), PointerOf(callbacks.onSummon)), PointerOf(callbacks.onDamage)), PointerOf(callbacks.onEntityRemoved)), PointerOf(callbacks.onBuffApplied)), PointerOf(callbacks.onBuffState)), PointerOf(callbacks.getSkillName)), PointerOf(callbacks.containsSkillCode)), PointerOf(callbacks.onEntityPair)), PointerOf(callbacks.onExtendedUserInfo)), PointerOf(callbacks.onEntityUInt)), PointerOf(callbacks.onEntityGauge)), PointerOf(callbacks.onEntityTriple)), PointerOf(callbacks.onEntityState)), PointerOf(callbacks.onAbyssArtifactState)), (ulong)((IntPtr)callbacks.userdata).ToInt64()), authBlock.magic), authBlock.version), authBlock.hostClass), authBlock.hostFingerprint) ^ 0x3D59A7B5, 11) ^ 0xB6E2C391u;
	}

	private static uint CreateHostFingerprint(uint hostClass)
	{
		Assembly entryAssembly = Assembly.GetEntryAssembly();
		InlineArray6<string> buffer = default(InlineArray6<string>);
		buffer[0] = "INGMeter.NativeHost.v2";
		buffer[1] = hostClass.ToString("X8");
		buffer[2] = entryAssembly?.GetName().Name ?? "";
		buffer[3] = entryAssembly?.ManifestModule.ModuleVersionId.ToString("N") ?? "";
		buffer[4] = typeof(PacketProcessorNative).Assembly.GetName().Name ?? "";
		buffer[5] = typeof(PacketProcessorNative).Assembly.ManifestModule.ModuleVersionId.ToString("N");
		string s = string.Join("|", (ReadOnlySpan<string?>)buffer);
		byte[] value = SHA256.HashData(Encoding.UTF8.GetBytes(s));
		uint num = BitConverter.ToUInt32(value, 0) ^ BitConverter.ToUInt32(value, 4) ^ BitConverter.ToUInt32(value, 8) ^ BitConverter.ToUInt32(value, 12);
		if (num != 0)
		{
			return num;
		}
		return 1u;
	}

	private static uint MixAuth(uint state, ulong value)
	{
		state ^= (uint)(int)value;
		state = RotateLeft(state + 2654435769u, 5);
		return (uint)((int)state * -2048144789 + ((int)(value >> 32) ^ -1028477387));
	}

	private static ulong PointerOf(Delegate? callback)
	{
		if ((object)callback == null)
		{
			return 0uL;
		}
		return (ulong)((IntPtr)Marshal.GetFunctionPointerForDelegate(callback)).ToInt64();
	}

	private static uint RotateLeft(uint value, int bits)
	{
		return (value << bits) | (value >> 32 - bits);
	}

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern nint PacketProcessor_Create(ref Config cfg, ref Callbacks cbs);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void PacketProcessor_Destroy(nint handle);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void PacketProcessor_Start(nint handle);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void PacketProcessor_Stop(nint handle);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void PacketProcessor_Enqueue(nint handle, int srcPort, int dstPort, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] byte[] data, int dataLen, [MarshalAs(UnmanagedType.LPStr)] string? deviceName, uint seqNum, ulong timestampMs);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern int PacketProcessor_GetCombatPort(nint handle);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint PacketProcessor_GetCombatDevice(nint handle);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void PacketProcessor_Reset(nint handle);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void PacketProcessor_SetUserInfoExCallback(nint handle, OnUserInfoExDelegate callback, nint userdata);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void PacketProcessor_SetZoneEntryCallback(nint handle, OnZoneEntryDelegate callback, nint userdata);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void PacketProcessor_SetStigmaSkillLevelCallback(nint handle, OnStigmaSkillLevelDelegate callback, nint userdata);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void PacketProcessor_SetStigmaSkillCodePredicate(nint handle, IsStigmaSkillCodeDelegate callback, nint userdata);

	[DllImport("INGParser.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void PacketProcessor_SetLocalPlayerStateCallback(nint handle, OnLocalPlayerStateDelegate callback, nint userdata);

	public static string GetCombatDevice(nint handle)
	{
		nint num = PacketProcessor_GetCombatDevice(handle);
		object obj;
		if (num != IntPtr.Zero)
		{
			obj = Marshal.PtrToStringUTF8(num);
			if (obj == null)
			{
				return "";
			}
		}
		else
		{
			obj = "";
		}
		return (string)obj;
	}
}
