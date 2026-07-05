using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace INGMeter.Core;

public sealed class PacketProcessorBridge : IDisposable
{
	private readonly nint _handle;

	private readonly nint _authBlock;

	private readonly Func<int, string?>? _getSkillName;

	private readonly Func<int, bool>? _containsSkillCode;

	private readonly Func<int, bool>? _isStigmaSkillCode;

	private readonly ConcurrentDictionary<int, GCHandle> _skillNamePins = new ConcurrentDictionary<int, GCHandle>();

	private readonly PacketProcessorNative.OnUserInfoDelegate _onUser;

	private readonly PacketProcessorNative.OnUserInfoExDelegate _onUserEx;

	private readonly PacketProcessorNative.OnMobSpawnDelegate _onMob;

	private readonly PacketProcessorNative.OnSummonDelegate _onSummon;

	private readonly PacketProcessorNative.OnDamageRecordDelegate _onDamage;

	private readonly PacketProcessorNative.OnEntityRemovedDelegate _onRemoved;

	private readonly PacketProcessorNative.OnBuffAppliedDelegate _onBuffApplied;

	private readonly PacketProcessorNative.OnBuffStateDelegate _onBuffState;

	private readonly PacketProcessorNative.GetSkillNameDelegate _getSkillNameCallback;

	private readonly PacketProcessorNative.ContainsSkillCodeDelegate _containsSkillCodeCallback;

	private readonly PacketProcessorNative.OnEntityPairDelegate _onEntityPair;

	private readonly PacketProcessorNative.OnExtendedUserInfoDelegate _onExtendedUser;

	private readonly PacketProcessorNative.OnEntityUIntDelegate _onEntityUInt;

	private readonly PacketProcessorNative.OnEntityGaugeDelegate _onEntityGauge;

	private readonly PacketProcessorNative.OnEntityTripleDelegate _onEntityTriple;

	private readonly PacketProcessorNative.OnEntityStateDelegate _onEntityState;

	private readonly PacketProcessorNative.OnAbyssArtifactStateDelegate _onAbyssArtifactState;

	private readonly PacketProcessorNative.OnZoneEntryDelegate _onZoneEntry;

	private readonly PacketProcessorNative.OnStigmaSkillLevelDelegate _onStigmaSkillLevel;

	private readonly PacketProcessorNative.IsStigmaSkillCodeDelegate _isStigmaSkillCodeCallback;

	private readonly PacketProcessorNative.OnLocalPlayerStateDelegate _onLocalPlayerState;

	private readonly ConcurrentDictionary<int, int> _mobEntityInfo = new ConcurrentDictionary<int, int>();

	private bool _disposed;

	private bool _userInfoExRegistered;

	public bool TraceLookupCallbacks { get; set; }

	public event Action<DateTime, int, int, int, int, byte, int, uint, int, int, int, bool>? OnDamage;

	public event Action<int, string, int, int, int, int>? OnUserInfo;

	public event Action<ExtendedUserInfoEvent>? OnExtendedUserInfo;

	public event Action<int, int>? OnSummon;

	public event Action<int, int, int>? OnMobSpawn;

	public event Action<MobSpawnObservedEvent>? OnMobSpawnInfo;

	public event Action<int, uint>? OnEntityUInt;

	public event Action<int>? OnEntityRemoved;

	public event Action<BuffEvent>? OnBuff;

	public event Action<AbyssArtifactStateEvent>? OnAbyssArtifactState;

	public event Action<ZoneEntryEvent>? OnZoneEntry;

	public event Action<StigmaSkillLevelEvent>? OnStigmaSkillLevel;

	public event Action<LocalPlayerStateEvent>? OnLocalPlayerState;

	public event Action<int, string>? OnLog;

	public event Action<NativePacketInfo>? OnNativeInfo;

	public PacketProcessorBridge(int serverPort = 0, bool tcpReorder = true, Func<int, string?>? getSkillName = null, Func<int, bool>? containsSkillCode = null, Func<int, bool>? isStigmaSkillCode = null)
	{
		_getSkillName = getSkillName;
		_containsSkillCode = containsSkillCode;
		_isStigmaSkillCode = isStigmaSkillCode;
		_onUser = UserInfoCallback;
		_onUserEx = UserInfoExCallback;
		_onMob = MobSpawnCallback;
		_onSummon = SummonCallback;
		_onDamage = DamageCallback;
		_onRemoved = EntityRemovedCallback;
		_onBuffApplied = BuffAppliedCallback;
		_onBuffState = BuffStateCallback;
		_getSkillNameCallback = GetSkillNameCallback;
		_containsSkillCodeCallback = ContainsSkillCodeCallback;
		_onEntityPair = EntityPairCallback;
		_onExtendedUser = ExtendedUserInfoCallback;
		_onEntityUInt = EntityUIntCallback;
		_onEntityGauge = EntityGaugeCallback;
		_onEntityTriple = EntityTripleCallback;
		_onEntityState = EntityStateCallback;
		_onAbyssArtifactState = AbyssArtifactStateCallback;
		_onZoneEntry = ZoneEntryCallback;
		_onStigmaSkillLevel = StigmaSkillLevelCallback;
		_isStigmaSkillCodeCallback = IsStigmaSkillCodeCallback;
		_onLocalPlayerState = LocalPlayerStateCallback;
		_authBlock = PacketProcessorNative.CreateAuthBlock(1095782449u);
		uint num = PacketProcessorNative.CreateAuthNonce();
		PacketProcessorNative.Callbacks callbacks = new PacketProcessorNative.Callbacks
		{
			onUserInfo = _onUser,
			onMobSpawn = _onMob,
			onSummon = _onSummon,
			onDamage = _onDamage,
			onEntityRemoved = _onRemoved,
			onBuffApplied = _onBuffApplied,
			onBuffState = _onBuffState,
			getSkillName = _getSkillNameCallback,
			containsSkillCode = _containsSkillCodeCallback,
			onEntityPair = _onEntityPair,
			onExtendedUserInfo = _onExtendedUser,
			onEntityUInt = _onEntityUInt,
			onEntityGauge = _onEntityGauge,
			onEntityTriple = _onEntityTriple,
			onEntityState = _onEntityState,
			onAbyssArtifactState = _onAbyssArtifactState,
			userdata = _authBlock
		};
		PacketProcessorNative.Config config = new PacketProcessorNative.Config
		{
			serverPort = serverPort,
			tcpReorder = (tcpReorder ? 1 : 0),
			workerCount = 0,
			maxBufferSize = 8388608,
			maxReorderBytes = 524288,
			authNonce = num
		};
		config.authToken = PacketProcessorNative.ComputeAuthToken(num, in config, in callbacks);
		_handle = PacketProcessorNative.PacketProcessor_Create(ref config, ref callbacks);
		if (_handle == IntPtr.Zero)
		{
			PacketProcessorNative.FreeAuthBlock(_authBlock);
			throw new InvalidOperationException("PacketProcessor_Create failed. INGParser.dll was not loaded or returned null.");
		}
		try
		{
			PacketProcessorNative.PacketProcessor_SetUserInfoExCallback(_handle, _onUserEx, IntPtr.Zero);
			_userInfoExRegistered = true;
		}
		catch (EntryPointNotFoundException)
		{
		}
		try
		{
			PacketProcessorNative.PacketProcessor_SetZoneEntryCallback(_handle, _onZoneEntry, IntPtr.Zero);
			PacketProcessorNative.PacketProcessor_SetStigmaSkillCodePredicate(_handle, _isStigmaSkillCodeCallback, IntPtr.Zero);
			PacketProcessorNative.PacketProcessor_SetStigmaSkillLevelCallback(_handle, _onStigmaSkillLevel, IntPtr.Zero);
		}
		catch (EntryPointNotFoundException)
		{
		}
		try
		{
			PacketProcessorNative.PacketProcessor_SetLocalPlayerStateCallback(_handle, _onLocalPlayerState, IntPtr.Zero);
		}
		catch (EntryPointNotFoundException)
		{
		}
	}

	public void Start()
	{
		PacketProcessorNative.PacketProcessor_Start(_handle);
	}

	public void Stop()
	{
		PacketProcessorNative.PacketProcessor_Stop(_handle);
	}

	public void Reset()
	{
		PacketProcessorNative.PacketProcessor_Reset(_handle);
	}

	public int GetCombatPort()
	{
		return PacketProcessorNative.PacketProcessor_GetCombatPort(_handle);
	}

	public string GetCombatDevice()
	{
		return PacketProcessorNative.GetCombatDevice(_handle);
	}

	public void Enqueue(int srcPort, int dstPort, byte[] data, string? device, uint seq, DateTime timestampUtc)
	{
		if (!_disposed && data.Length != 0)
		{
			long num = new DateTimeOffset(timestampUtc).ToUnixTimeMilliseconds();
			PacketProcessorNative.PacketProcessor_Enqueue(_handle, srcPort, dstPort, data, data.Length, device, seq, (ulong)((num <= 0) ? 0 : num));
		}
	}

	private void DamageCallback(nint damagePtr, nint userdata)
	{
		try
		{
			PacketProcessorNative.DamageRecord damageRecord = Marshal.PtrToStructure<PacketProcessorNative.DamageRecord>(damagePtr);
			DateTime dateTime = FromUnixMilliseconds(damageRecord.timestampMs);
			int arg = ((damageRecord.rawSkillCode > 0) ? damageRecord.rawSkillCode : damageRecord.skillCode);
			this.OnDamage?.Invoke(dateTime, damageRecord.actorId, damageRecord.targetId, damageRecord.skillCode, arg, damageRecord.damageType, damageRecord.damage, damageRecord.specialFlags, damageRecord.multiHitCount, damageRecord.multiHitDamage, damageRecord.healAmount, damageRecord.isDot != 0);
			this.OnNativeInfo?.Invoke(new NativePacketInfo(dateTime, "DamageRecord", RawCallback("onDamage", userdata, ("damageRecordPtr", Ptr(damagePtr)), ("actorId", damageRecord.actorId), ("targetId", damageRecord.targetId), ("skillCode", damageRecord.skillCode), ("rawSkillCode", damageRecord.rawSkillCode), ("damageType", damageRecord.damageType), ("damage", damageRecord.damage), ("specialFlags", damageRecord.specialFlags), ("specialFlagsHex", $"0x{damageRecord.specialFlags:X}"), ("multiHitCount", damageRecord.multiHitCount), ("multiHitDamage", damageRecord.multiHitDamage), ("healAmount", damageRecord.healAmount), ("isDot", damageRecord.isDot), ("timestampMs", damageRecord.timestampMs)), damageRecord.actorId, damageRecord.targetId, damageRecord.skillCode, damageRecord.damage, RawCallback("onDamage", userdata, ("damageRecordPtr", Ptr(damagePtr)), ("actorId", damageRecord.actorId), ("targetId", damageRecord.targetId), ("skillCode", damageRecord.skillCode), ("rawSkillCode", damageRecord.rawSkillCode), ("damageType", damageRecord.damageType), ("damage", damageRecord.damage), ("specialFlags", damageRecord.specialFlags), ("specialFlagsHex", $"0x{damageRecord.specialFlags:X}"), ("multiHitCount", damageRecord.multiHitCount), ("multiHitDamage", damageRecord.multiHitDamage), ("healAmount", damageRecord.healAmount), ("isDot", damageRecord.isDot), ("timestampMs", damageRecord.timestampMs))));
		}
		catch (Exception ex)
		{
			this.OnLog?.Invoke(4, "Damage callback failed: " + ex.Message);
		}
	}

	private void MobSpawnCallback(int mobId, int mobCode, int hp, int extra1, int extra2, int rawHp, int stateMarker, nint userdata)
	{
		_mobEntityInfo[mobId] = mobCode;
		this.OnMobSpawnInfo?.Invoke(new MobSpawnObservedEvent(DateTime.UtcNow, mobId, mobCode, hp, rawHp, extra1, extra2, stateMarker));
		this.OnMobSpawn?.Invoke(mobId, mobCode, hp);
		Emit("MobSpawn", RawCallback("onMobSpawn", userdata, ("mobId", mobId), ("mobCode", mobCode), ("rawHp", rawHp), ("scaledHp", hp), ("extra1", extra1), ("extra2", HexByte(extra2)), ("stateMarker", HexByte(stateMarker))), mobId, mobCode, 0, hp);
	}

	private void SummonCallback(int actorId, int petId, nint userdata)
	{
		this.OnSummon?.Invoke(actorId, petId);
		Emit("Summon", RawCallback("onSummon", userdata, ("actorId", actorId), ("petId", petId)), actorId, petId, 0, 0L);
	}

	private void UserInfoCallback(int entityId, nint nicknamePtr, int serverId, int jobCode, int extra, nint userdata)
	{
		if (!_userInfoExRegistered)
		{
			HandleUserInfoCallback(entityId, nicknamePtr, serverId, jobCode, extra, 0, userdata);
		}
	}

	private void UserInfoExCallback(int entityId, nint nicknamePtr, int serverId, int jobCode, int extra, int characterNumber, nint userdata)
	{
		HandleUserInfoCallback(entityId, nicknamePtr, serverId, jobCode, extra, characterNumber, userdata);
	}

	private void HandleUserInfoCallback(int entityId, nint nicknamePtr, int serverId, int jobCode, int extra, int characterNumber, nint userdata)
	{
		string text = ReadUtf8(nicknamePtr, $"#{entityId}");
		this.OnUserInfo?.Invoke(entityId, text, serverId, jobCode, extra, characterNumber);
		Emit("UserInfo", RawCallback("onUserInfo", userdata, ("entityId", entityId), ("nicknamePtr", Ptr(nicknamePtr)), ("nickname", Quote(text)), ("serverId", serverId), ("jobCode", jobCode), ("extra", extra), ("characterNumber", characterNumber)), entityId, serverId, 0, 0L);
	}

	private void EntityRemovedCallback(int entityId, nint userdata)
	{
		this.OnEntityRemoved?.Invoke(entityId);
		Emit("EntityRemoved", RawCallback("onEntityRemoved", userdata, ("entityId", entityId)), entityId, 0, 0, 0L);
		_mobEntityInfo.TryRemove(entityId, out var _);
	}

	private void BuffAppliedCallback(int targetId, int ownerId, int buffId, int skillId, uint duration, ulong startedAtMs, ulong expiresAtMs, nint userdata)
	{
		this.OnBuff?.Invoke(new BuffEvent(DateTime.UtcNow, "BuffApplied", targetId, ownerId, buffId, skillId, duration, startedAtMs, expiresAtMs));
		Emit("BuffApplied", RawCallback("onBuffApplied", userdata, ("targetId", targetId), ("ownerId", ownerId), ("buffId", buffId), ("skillId", skillId), ("duration", duration), ("startedAtMs", startedAtMs), ("expiresAtMs", expiresAtMs)), targetId, ownerId, skillId, duration);
	}

	private void BuffStateCallback(int targetId, int buffId, int skillId, uint duration, ulong startedAtMs, ulong expiresAtMs, nint userdata)
	{
		int buffId2 = ((skillId > 0) ? skillId : buffId);
		int skillId2 = DecodeBuffSourceSkillId(buffId2);
		this.OnBuff?.Invoke(new BuffEvent(DateTime.UtcNow, "BuffState", targetId, buffId, buffId2, skillId2, duration, startedAtMs, expiresAtMs));
		Emit("BuffState", RawCallback("onBuffState", userdata, ("targetId", targetId), ("buffId", buffId), ("skillId", skillId), ("duration", duration), ("startedAtMs", startedAtMs), ("expiresAtMs", expiresAtMs)), targetId, buffId, skillId, duration);
	}

	private static int DecodeBuffSourceSkillId(int buffId)
	{
		if (buffId <= 0)
		{
			return 0;
		}
		int num = buffId % 100;
		int num2 = buffId % 10;
		if (num >= 11 && num <= 19)
		{
			return buffId / 10 + num2 - 1;
		}
		if (num >= 21 && num <= 29)
		{
			return buffId / 10 + num2 - 1;
		}
		if (buffId > 20000 && num2 == 1)
		{
			return buffId / 10;
		}
		return 0;
	}

	private nint GetSkillNameCallback(int skillCode, nint userdata)
	{
		try
		{
			string name = _getSkillName?.Invoke(skillCode) ?? "";
			if (string.IsNullOrWhiteSpace(name) || name == skillCode.ToString())
			{
				name = $"skill_{skillCode}";
			}
			GCHandle orAdd = _skillNamePins.GetOrAdd(skillCode, (int _) => GCHandle.Alloc(Encoding.UTF8.GetBytes(name + "\0"), GCHandleType.Pinned));
			TraceLookup("GetSkillName", RawCallback("getSkillName", userdata, ("skillCode", skillCode), ("returnPtr", Ptr(orAdd.AddrOfPinnedObject())), ("returnText", Quote(name))), skillCode, name.Length);
			return orAdd.AddrOfPinnedObject();
		}
		catch
		{
			TraceLookup("GetSkillName", RawCallback("getSkillName", userdata, ("skillCode", skillCode), ("returnPtr", Ptr(IntPtr.Zero)), ("returnText", Quote("<error>"))), skillCode, 0L);
			return IntPtr.Zero;
		}
	}

	private int ContainsSkillCodeCallback(int skillCode, nint userdata)
	{
		bool flag = _containsSkillCode?.Invoke(skillCode) ?? false;
		TraceLookup("ContainsSkillCode", RawCallback("containsSkillCode", userdata, ("skillCode", skillCode), ("return", flag ? 1 : 0)), skillCode, flag ? 1 : 0);
		return flag ? 1 : 0;
	}

	private void EntityPairCallback(int firstId, int secondId, nint userdata)
	{
		Emit("EntityPair", RawCallback("onEntityPair", userdata, ("firstId", firstId), ("secondId", secondId)), firstId, secondId, 0, 0L);
	}

	private void ExtendedUserInfoCallback(int entityId, int slot, int mode, uint value1, int serverId, nint nickname, int jobCode, int level, int gearScore, int combatPower, int source, nint userdata)
	{
		string text = ReadUtf8(nickname, "");
		this.OnExtendedUserInfo?.Invoke(new ExtendedUserInfoEvent(DateTime.UtcNow, entityId, slot, mode, value1, serverId, text, jobCode, level, gearScore, combatPower, source));
		if (entityId >= 100 && serverId > 0 && jobCode > 0 && !string.IsNullOrWhiteSpace(text))
		{
			this.OnUserInfo?.Invoke(entityId, text, serverId, jobCode, 0, 0);
		}
		string summary = BuildExtendedUserInfoRaw(entityId, text, slot, mode, value1, serverId, jobCode, level, gearScore, combatPower, source, nickname, userdata);
		Emit("ExtendedUserInfo", summary, entityId, serverId, jobCode, combatPower);
	}

	private static string BuildExtendedUserInfoRaw(int entityId, string name, int slot, int mode, uint value1, int serverId, int jobCode, int level, int gearScore, int combatPower, int source, nint nicknamePtr, nint userdata)
	{
		return RawCallback("onExtendedUserInfo", userdata, ("entityId", entityId), ("slot", slot), ("mode", mode), ("value1", value1), ("serverId", serverId), ("nicknamePtr", Ptr(nicknamePtr)), ("nickname", Quote(name)), ("jobCode", jobCode), ("level", level), ("gearScore", gearScore), ("combatPower", combatPower), ("source", source));
	}

	private void EntityUIntCallback(int entityId, uint value, nint userdata)
	{
		this.OnEntityUInt?.Invoke(entityId, value);
		Emit(KindForEntity(entityId, "UInt"), RawCallback("onEntityUInt", userdata, ("entityId", entityId), ("value", value)), entityId, 0, 0, value);
	}

	private void EntityGaugeCallback(int entityId, uint current, uint maximum, int state, nint userdata)
	{
		Emit(KindForEntity(entityId, "Gauge"), RawCallback("onEntityGauge", userdata, ("entityId", entityId), ("current", current), ("maximum", maximum), ("state", state)), entityId, state, 0, maximum);
	}

	private void EntityTripleCallback(int entityId, int value1, int value2, nint userdata)
	{
		Emit(KindForEntity(entityId, "Triple"), RawCallback("onEntityTriple", userdata, ("entityId", entityId), ("value1", value1), ("value2", value2)), entityId, value1, 0, value2);
	}

	private void EntityStateCallback(int entityId, int state, nint userdata)
	{
		Emit(KindForEntity(entityId, "State"), RawCallback("onEntityState", userdata, ("entityId", entityId), ("state", state)), entityId, state, 0, 0L);
	}

	private void AbyssArtifactStateCallback(int areaCode, int artifactId, int ownerSide, int ownerServerId, int matchServer1Id, int matchServer2Id, nint userdata)
	{
		this.OnAbyssArtifactState?.Invoke(new AbyssArtifactStateEvent(DateTime.UtcNow, areaCode, artifactId, ownerSide, ownerServerId, matchServer1Id, matchServer2Id));
		Emit("AbyssArtifactState", RawCallback("onAbyssArtifactState", userdata, ("areaCode", areaCode), ("artifactId", artifactId), ("ownerSide", ownerSide), ("ownerServerId", ownerServerId), ("matchServer1Id", matchServer1Id), ("matchServer2Id", matchServer2Id)), areaCode, artifactId, 0, ownerSide);
	}

	private void ZoneEntryCallback(int contentCode, int kind, nint userdata)
	{
		this.OnZoneEntry?.Invoke(new ZoneEntryEvent(DateTime.UtcNow, contentCode, kind));
		Emit("ZoneEntry", RawCallback("onZoneEntry", userdata, ("contentCode", contentCode), ("kind", kind)), contentCode, kind, 0, 0L);
	}

	private int IsStigmaSkillCodeCallback(int skillCode, nint userdata)
	{
		try
		{
			return (_isStigmaSkillCode?.Invoke(skillCode) ?? false) ? 1 : 0;
		}
		catch
		{
			return 0;
		}
	}

	private void StigmaSkillLevelCallback(int ownerId, int skillCode, int baseSkillCode, int effectiveLevel, int baseSkillLevel, nint userdata)
	{
		Func<int, bool>? isStigmaSkillCode = _isStigmaSkillCode;
		if (isStigmaSkillCode == null || !isStigmaSkillCode(skillCode))
		{
			Func<int, bool>? isStigmaSkillCode2 = _isStigmaSkillCode;
			if (isStigmaSkillCode2 == null || !isStigmaSkillCode2(baseSkillCode))
			{
				return;
			}
		}
		this.OnStigmaSkillLevel?.Invoke(new StigmaSkillLevelEvent(DateTime.UtcNow, ownerId, skillCode, baseSkillCode, effectiveLevel, baseSkillLevel));
		Emit("StigmaSkillLevel", RawCallback("onStigmaSkillLevel", userdata, ("ownerId", ownerId), ("skillCode", skillCode), ("baseSkillCode", baseSkillCode), ("effectiveLevel", effectiveLevel), ("baseSkillLevel", baseSkillLevel)), skillCode, baseSkillCode, skillCode, effectiveLevel);
	}

	private void LocalPlayerStateCallback(int kind, long value, long maxValue, long bonusValue, int entityId, int serverId, int characterNumber, nint context, nint userdata)
	{
		string text = ReadUtf8(context, "");
		this.OnLocalPlayerState?.Invoke(new LocalPlayerStateEvent(DateTime.UtcNow, kind, value, maxValue, bonusValue, entityId, serverId, characterNumber, text));
		Emit("LocalPlayerState", RawCallback("onLocalPlayerState", userdata, ("kind", kind), ("value", value), ("maxValue", maxValue), ("bonusValue", bonusValue), ("entityId", entityId), ("serverId", serverId), ("characterNumber", characterNumber), ("context", Quote(text))), entityId, serverId, 0, value);
	}

	private void Emit(string kind, string summary, int primaryId = 0, int secondaryId = 0, int skillCode = 0, long value = 0L)
	{
		this.OnNativeInfo?.Invoke(new NativePacketInfo(DateTime.UtcNow, kind, summary, primaryId, secondaryId, skillCode, value, summary));
	}

	private void TraceLookup(string kind, string summary, int code, long value)
	{
		if (TraceLookupCallbacks)
		{
			Emit(kind, summary, 0, 0, code, value);
		}
	}

	private string KindForEntity(int entityId, string suffix)
	{
		return "Entity" + suffix;
	}

	private string MobDetail(int entityId)
	{
		if (!_mobEntityInfo.TryGetValue(entityId, out var value))
		{
			return "";
		}
		return $", mobCode={value}";
	}

	private static string RawCallback(string callbackName, nint userdata, params (string Name, object? Value)[] args)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(callbackName).Append('(');
		for (int i = 0; i < args.Length; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(", ");
			}
			stringBuilder.Append(args[i].Name).Append('=').Append(args[i].Value);
		}
		if (args.Length != 0)
		{
			stringBuilder.Append(", ");
		}
		stringBuilder.Append("userdata=").Append(Ptr(userdata)).Append(')');
		return stringBuilder.ToString();
	}

	private static string Ptr(nint ptr)
	{
		return $"0x{((IntPtr)ptr).ToInt64():X}";
	}

	private static string HexByte(int value)
	{
		return $"0x{value & 0xFF:X2}";
	}

	private static string Quote(string value)
	{
		return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
	}

	private static DateTime FromUnixMilliseconds(ulong timestampMs)
	{
		if (timestampMs == 0L || timestampMs > long.MaxValue)
		{
			return DateTime.UtcNow;
		}
		try
		{
			return DateTimeOffset.FromUnixTimeMilliseconds((long)timestampMs).UtcDateTime;
		}
		catch
		{
			return DateTime.UtcNow;
		}
	}

	private static string ReadUtf8(nint ptr, string fallback)
	{
		string text;
		if (ptr != IntPtr.Zero)
		{
			text = Marshal.PtrToStringUTF8(ptr);
			if (text == null)
			{
				return fallback;
			}
		}
		else
		{
			text = fallback;
		}
		return text;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		try
		{
			Stop();
		}
		catch
		{
		}
		try
		{
			PacketProcessorNative.PacketProcessor_Destroy(_handle);
		}
		catch
		{
		}
		PacketProcessorNative.FreeAuthBlock(_authBlock);
		foreach (GCHandle value in _skillNamePins.Values)
		{
			if (value.IsAllocated)
			{
				value.Free();
			}
		}
		_skillNamePins.Clear();
	}
}
