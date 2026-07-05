using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace INGMeter.Core;

public sealed class MeterEngine : IDisposable
{
	private readonly record struct SessionCombatLogEntry(DateTime TimestampUtc, bool IsDot, int ActorId, string ActorName, int TargetId, string TargetName, int SkillId, int Damage, int MultiDamage, int Heal, string Specials, int SkillLevel, int BaseSkillLevel);

	private readonly record struct SessionBuffLogEntry(DateTime TimestampUtc, string Kind, int TargetId, int OwnerId, int BuffId, int SkillId, uint DurationMs, ulong StartedAtMs, ulong ExpiresAtMs, int SkillLevel, int BaseSkillLevel);

	private readonly record struct EncounterBuffStateKey(int TargetId, int BuffKey);

	private sealed record EncounterBuffInfo(string Name, string Type, bool IconView);

	private sealed record CompactDamageReplayEntry(long OffsetMs, DateTime TimestampUtc, bool IsDot, int ActorId, string ActorName, int TargetId, string TargetName, int SkillId, int Damage, int MultiDamage, int Heal, string SpecialsText, int SkillLevel, int BaseSkillLevel);

	private sealed record CompactEncounterReplayData(EncounterLogRecordMeta? Meta, DateTime StartUtc, DateTime EndUtc, int BossActorId, string BossName, int BossMobCode, int BossMaxHp, IReadOnlyList<CompactDamageReplayEntry> DamageEvents);

	private sealed class LoadedLogTargetInfo
	{
		public int TargetId { get; init; }

		public string Name { get; set; } = "";

		public long Damage { get; set; }

		public int Hits { get; set; }
	}

	private readonly record struct IndexedSessionCombatLogEntry(int Index, SessionCombatLogEntry Row);

	private sealed class EncounterHealingRows
	{
		public List<IndexedSessionCombatLogEntry> Rows { get; } = new List<IndexedSessionCombatLogEntry>();
	}

	private sealed record SupportDamageEvent(DateTime TimestampUtc, int ActorId, int TargetId, int Damage, bool IsCrit);

	private sealed record SupportBuffWindow(int SkillId, int LevelCode, string SkillName, double Percent, double Multiplier, string ExclusiveGroup, RdpsEffectScope EffectScope, RdpsSourceRestriction SourceRestriction, RdpsEffectKind EffectKind, int OwnerId, int TargetId, DateTime Start, DateTime End) : IRdpsSupportWindow;

	private sealed class SupportAccumulator
	{
		public double AddedDamage { get; set; }

		public double ReducedDamage { get; set; }
	}

	public const int EncounterLogVersion = 4;

	public const string EncounterLogFormat = "compact-json-v4";

	private const int EncounterReplayFrameIntervalMs = 100;

	private static readonly JsonSerializerOptions EncounterRecordJsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	private static readonly IReadOnlyDictionary<int, int> SpiritBasicSkillByMobCode = new Dictionary<int, int>
	{
		[2920112] = 100011,
		[2920132] = 100021,
		[2920152] = 100031,
		[2920166] = 100041,
		[2920181] = 100051
	};

	private readonly NameCache _names = new NameCache();

	private readonly EncounterLogStore _encounterLogStore = new EncounterLogStore();

	private readonly object _sessionCombatLogLock = new object();

	private readonly List<SessionCombatLogEntry> _sessionCombatLog = new List<SessionCombatLogEntry>();

	private readonly List<SessionBuffLogEntry> _sessionBuffLog = new List<SessionBuffLogEntry>();

	private readonly Dictionary<EncounterBuffStateKey, SessionBuffLogEntry> _activeBuffLog = new Dictionary<EncounterBuffStateKey, SessionBuffLogEntry>();

	private readonly CombatAggregator _agg;

	private readonly WebUploader _webUploader;

	private readonly object _combatPowerLock = new object();

	private readonly Dictionary<string, int> _combatPowerByCharacter = new Dictionary<string, int>(StringComparer.Ordinal);

	private readonly object _charNoLock = new object();

	private readonly Dictionary<string, int> _charNoByCharacter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<int, int> _charNoByActorId = new Dictionary<int, int>();

	private readonly object _stigmaSkillLevelLock = new object();

	private readonly Dictionary<int, StigmaSkillLevelEvent> _stigmaSkillLevelByCode = new Dictionary<int, StigmaSkillLevelEvent>();

	private readonly Dictionary<(int ProviderId, int SkillCode), StigmaSkillLevelEvent> _stigmaSkillLevelByProviderAndCode = new Dictionary<(int, int), StigmaSkillLevelEvent>();

	private PacketProcessorBridge? _bridge;

	private readonly object _bridgeLock = new object();

	private bool _nativeLookupTraceEnabled;

	public Func<int, string>? ResolveMobName;

	public Func<int, bool>? ResolveMobBossStatus;

	public Func<int, bool>? IsBossMobExcluded;

	public Func<int, string?>? ResolveSkillName;

	public Func<int, bool>? ContainsSkillCode;

	private string? _packetLocalPlayerName;

	private int _packetLocalPlayerServerId;

	private volatile bool _isLogViewing;

	private volatile bool _suppressUploadsForCurrentSession;

	private const string EncounterLogTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

	private const int EncounterBuffLeadSeconds = 120;

	private const int EncounterBuffTailSeconds = 10;

	private static readonly TimeSpan SessionBuffLogIdleRetention = TimeSpan.FromSeconds(180L);

	private static readonly TimeSpan SessionBuffLogActiveGrace = TimeSpan.FromSeconds(60L);

	private const int SessionBuffLogPruneInterval = 512;

	private int _sessionBuffLogAppendCount;

	private static readonly Lazy<IReadOnlyDictionary<int, EncounterBuffInfo>> EncounterBuffCatalog = new Lazy<IReadOnlyDictionary<int, EncounterBuffInfo>>(LoadEncounterBuffCatalog);

	private static readonly string[] EncounterSpecialFlagOrder = new string[8] { "BACK", "PARRY", "PERFECT", "DOUBLE", "SMITE", "POWER_SHARD", "CRITICAL", "IMMUNE" };

	public bool BossOnlyMeasurement
	{
		get
		{
			return _agg.BossOnlyMeasurement;
		}
		set
		{
			_agg.BossOnlyMeasurement = value;
		}
	}

	public int CurrentContentCode
	{
		get
		{
			return _webUploader.CurrentContentCode;
		}
		set
		{
			_webUploader.CurrentContentCode = value;
		}
	}

	public int ServerPort { get; set; } = 13328;

	public NameCache Names => _names;

	public CombatAggregator Aggregator => _agg;

	public int? LockedCombatPort { get; private set; }

	public int CurrentMapId => _names.CurrentMapId;

	public string? LocalPlayerName => _names.LocalPlayerName;

	public bool IsLocalPlayerLinked => _names.LocalPlayerActorId.HasValue;

	public int? LocalPlayerActorId => _names.LocalPlayerActorId;

	public int LocalPlayerServerId => _packetLocalPlayerServerId;

	public bool MapChangeAutoReset { get; set; }

	public bool SaveEncounterLogs { get; set; } = true;

	public bool HasExtractedNames => _names.Count > 0;

	public string EncounterLogDirectory => EncounterLogStore.RootDirectory;

	public bool IsLogViewing => _isLogViewing;

	public bool SuppressUploadsForCurrentSession => _suppressUploadsForCurrentSession;

	public bool IsDllActive => _bridge != null;

	public CombatSnapshot? LatestSnapshot => _agg.LatestSnapshot;

	public event Action<DamageEvent>? DamageEventParsed;

	public event Action<BuffEvent>? BuffEventParsed;

	public event Action<ExtendedUserInfoEvent>? ExtendedUserInfoReceived;

	public event Action<LocalUserInfoObservedEvent>? LocalUserInfoObserved;

	public event Action<int>? UserInfoResolved;

	public event Action<AbyssArtifactStateEvent>? AbyssArtifactStateReceived;

	public event Action<ZoneEntryEvent>? ZoneEntryReceived;

	public event Action<StigmaSkillLevelEvent>? StigmaSkillLevelReceived;

	public event Action<LocalPlayerStateEvent>? LocalPlayerStateReceived;

	public event Action<MobSpawnObservedEvent>? MobSpawnObserved;

	public event Action<NativePacketInfo>? NativePacketInfoReceived;

	public event Action<int, string, DateTime, DateTime, int, int>? BossDefeated
	{
		add
		{
			CombatAggregator agg = _agg;
			agg.OnBossDefeated = (Action<int, string, DateTime, DateTime, int, int>)Delegate.Combine(agg.OnBossDefeated, value);
		}
		remove
		{
			CombatAggregator agg = _agg;
			agg.OnBossDefeated = (Action<int, string, DateTime, DateTime, int, int>)Delegate.Remove(agg.OnBossDefeated, value);
		}
	}

	public event Action<int, string, DateTime, DateTime, int, int>? BossEnded
	{
		add
		{
			CombatAggregator agg = _agg;
			agg.OnBossEnded = (Action<int, string, DateTime, DateTime, int, int>)Delegate.Combine(agg.OnBossEnded, value);
		}
		remove
		{
			CombatAggregator agg = _agg;
			agg.OnBossEnded = (Action<int, string, DateTime, DateTime, int, int>)Delegate.Remove(agg.OnBossEnded, value);
		}
	}

	public event Action<int, string>? BossConfirmed
	{
		add
		{
			CombatAggregator agg = _agg;
			agg.OnBossConfirmed = (Action<int, string>)Delegate.Combine(agg.OnBossConfirmed, value);
		}
		remove
		{
			CombatAggregator agg = _agg;
			agg.OnBossConfirmed = (Action<int, string>)Delegate.Remove(agg.OnBossConfirmed, value);
		}
	}

	public event Action<int, string>? BossHpReset
	{
		add
		{
			CombatAggregator agg = _agg;
			agg.OnBossHpReset = (Action<int, string>)Delegate.Combine(agg.OnBossHpReset, value);
		}
		remove
		{
			CombatAggregator agg = _agg;
			agg.OnBossHpReset = (Action<int, string>)Delegate.Remove(agg.OnBossHpReset, value);
		}
	}

	public event Action? AutoReset
	{
		add
		{
			CombatAggregator agg = _agg;
			agg.OnAutoReset = (Action)Delegate.Combine(agg.OnAutoReset, value);
		}
		remove
		{
			CombatAggregator agg = _agg;
			agg.OnAutoReset = (Action)Delegate.Remove(agg.OnAutoReset, value);
		}
	}

	public event Action? LocalPlayerSpawned
	{
		add
		{
			_names.LocalPlayerSpawned += value;
		}
		remove
		{
			_names.LocalPlayerSpawned -= value;
		}
	}

	public event Action<string, int>? LocalPlayerChanged;

	public event Action<string, int, int>? LocalPlayerIdentified;

	public event Action<DamageEvent, string>? PacketLogEvent
	{
		add
		{
			_agg.PacketLogEvent += value;
		}
		remove
		{
			_agg.PacketLogEvent -= value;
		}
	}

	public event Action<int, int>? SummonMerged
	{
		add
		{
			_agg.SummonMerged += value;
		}
		remove
		{
			_agg.SummonMerged -= value;
		}
	}

	private event Action<string, string, int>? _inspectCharacterDetected;

	public event Action<string, string, int>? InspectCharacterDetected
	{
		add
		{
			_inspectCharacterDetected += value;
		}
		remove
		{
			_inspectCharacterDetected -= value;
		}
	}

	public event Action<string>? UploadSuccess
	{
		add
		{
			_webUploader.UploadSuccess += value;
		}
		remove
		{
			_webUploader.UploadSuccess -= value;
		}
	}

	public bool IsExcludedBossMobCode(int mobCode)
	{
		return IsBossMobExcluded?.Invoke(mobCode) ?? false;
	}

	public void SetLocalPlayer(string name, int? actorId = null)
	{
		_names.SetLocalPlayer(name, actorId);
	}

	private bool ApplyPacketLocalPlayer(string nickname, int actorId, int serverId, out string localName)
	{
		localName = StripServerSuffix(nickname);
		if (actorId <= 0 || string.IsNullOrWhiteSpace(localName))
		{
			return false;
		}
		bool flag = HasDifferentLocalPlayerIdentity(localName, serverId);
		SetLocalPlayer(localName, actorId);
		_packetLocalPlayerName = localName;
		if (serverId > 0)
		{
			_packetLocalPlayerServerId = serverId;
		}
		else if (flag)
		{
			_packetLocalPlayerServerId = 0;
		}
		return flag;
	}

	private bool TryResolveCurrentLocalIdentity(string localName, out int actorId, out int serverId)
	{
		actorId = 0;
		serverId = 0;
		int? localPlayerActorId = _names.LocalPlayerActorId;
		if (localPlayerActorId.HasValue)
		{
			int valueOrDefault = localPlayerActorId.GetValueOrDefault();
			if (valueOrDefault > 0)
			{
				if (!_names.TryGet(valueOrDefault, out string name) || string.IsNullOrWhiteSpace(name))
				{
					return false;
				}
				var (value, num) = SplitLogCharacterIdentity(name);
				if (num <= 0 || !string.Equals(StripServerSuffix(value), StripServerSuffix(localName), StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
				actorId = valueOrDefault;
				serverId = num;
				return true;
			}
		}
		return false;
	}

	private bool HasDifferentLocalPlayerIdentity(string localName, int serverId)
	{
		if (!string.IsNullOrWhiteSpace(_packetLocalPlayerName))
		{
			if (!string.Equals(_packetLocalPlayerName, localName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (serverId > 0 && _packetLocalPlayerServerId > 0)
			{
				return serverId != _packetLocalPlayerServerId;
			}
			return false;
		}
		string text = StripServerSuffix(_names.LocalPlayerName ?? "");
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		return !string.Equals(text, localName, StringComparison.OrdinalIgnoreCase);
	}

	public IReadOnlyList<string> GetExtractedNames()
	{
		return _names.GetExtractedNames();
	}

	public bool TryGetActorName(int actorId, out string? name)
	{
		return _names.TryGet(actorId, out name);
	}

	public void TryRelinkLocalPlayer()
	{
		_names.TryRelinkLocalPlayerActorId();
	}

	public static bool IsEncounterParticipantActor(ActorStats actor)
	{
		if (actor.TotalDamage > 0 && !actor.IsMonster && IsResolvedCharacterName(actor.Name))
		{
			return IsResolvedServerName(actor.ServerName);
		}
		return false;
	}

	private static bool IsLocalEncounterParticipantActor(ActorStats actor)
	{
		if (actor.TotalDamage > 0)
		{
			return !actor.IsMonster;
		}
		return false;
	}

	private static bool IsResolvedCharacterName(string? name)
	{
		int result;
		if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("Actor ", StringComparison.OrdinalIgnoreCase) && !name.Equals("Unknown Player", StringComparison.OrdinalIgnoreCase))
		{
			return !int.TryParse(name, out result);
		}
		return false;
	}

	private static bool IsResolvedServerName(string? serverName)
	{
		if (!string.IsNullOrWhiteSpace(serverName))
		{
			return !serverName.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public void RememberCombatPower(string characterName, int serverId, int combatPower)
	{
		if (combatPower <= 0 || serverId <= 0 || string.IsNullOrWhiteSpace(characterName) || characterName.StartsWith("Actor ", StringComparison.Ordinal) || int.TryParse(characterName, out var _))
		{
			return;
		}
		lock (_combatPowerLock)
		{
			_combatPowerByCharacter[GetCombatPowerKey(characterName, serverId)] = combatPower;
		}
	}

	private void RememberCombatPower(ExtendedUserInfoEvent info)
	{
		if (info.CombatPower > 0)
		{
			RememberCombatPower(info.Nickname, info.ServerId, info.CombatPower);
		}
	}

	private void RememberJobClass(ExtendedUserInfoEvent info)
	{
		if (info.JobCode > 0 && info.ServerId > 0 && !string.IsNullOrWhiteSpace(info.Nickname))
		{
			string serverName = GetServerName(info.ServerId);
			if (!string.IsNullOrWhiteSpace(serverName))
			{
				_agg.SetCharacterJobClass(info.Nickname, serverName, info.JobCode);
			}
			if (info.EntityId >= 100)
			{
				_agg.SetActorJobClass(info.EntityId, info.JobCode);
			}
		}
	}

	public bool TryGetCombatPower(string characterName, string serverName, out int combatPower)
	{
		int aion2ServerId = PartyTracker.GetAion2ServerId(serverName);
		return TryGetCombatPower(characterName, aion2ServerId, out combatPower);
	}

	public bool TryGetCombatPower(string characterName, int serverId, out int combatPower)
	{
		combatPower = 0;
		if (serverId <= 0 || string.IsNullOrWhiteSpace(characterName) || characterName.StartsWith("Actor ", StringComparison.Ordinal) || int.TryParse(characterName, out var _))
		{
			return false;
		}
		lock (_combatPowerLock)
		{
			return _combatPowerByCharacter.TryGetValue(GetCombatPowerKey(characterName, serverId), out combatPower) && combatPower > 0;
		}
	}

	private static string GetCombatPowerKey(string characterName, int serverId)
	{
		return $"{serverId}:{characterName.Trim()}";
	}

	private void RememberCharacterNo(string characterName, int actorId, int serverId, int charNo)
	{
		if (charNo <= 0 || serverId <= 0 || !IsResolvedCharacterName(characterName))
		{
			return;
		}
		string characterIdentityKey = GetCharacterIdentityKey(characterName, serverId);
		lock (_charNoLock)
		{
			_charNoByCharacter[characterIdentityKey] = charNo;
			if (actorId > 0)
			{
				_charNoByActorId[actorId] = charNo;
			}
		}
	}

	private void RememberCharacterNo(ExtendedUserInfoEvent info)
	{
		if (info.Value1 != 0 && info.Value1 <= int.MaxValue)
		{
			int source = info.Source;
			bool flag = ((source == 1 || (uint)(source - 4) <= 1u) ? true : false);
			if (flag || info.EntityId == 2)
			{
				RememberCharacterNo(info.Nickname, 0, info.ServerId, (int)info.Value1);
			}
		}
	}

	public bool TryGetCharNo(int actorId, string characterName, int serverId, out int charNo)
	{
		charNo = 0;
		if (actorId > 0)
		{
			lock (_charNoLock)
			{
				if (_charNoByActorId.TryGetValue(actorId, out charNo) && charNo > 0)
				{
					return true;
				}
			}
		}
		if (serverId <= 0 || !IsResolvedCharacterName(characterName))
		{
			return false;
		}
		lock (_charNoLock)
		{
			return _charNoByCharacter.TryGetValue(GetCharacterIdentityKey(characterName, serverId), out charNo) && charNo > 0;
		}
	}

	public bool TryGetCharNo(int actorId, string characterName, string serverName, out int charNo)
	{
		int aion2ServerId = PartyTracker.GetAion2ServerId(serverName);
		return TryGetCharNo(actorId, characterName, aion2ServerId, out charNo);
	}

	private static string GetCharacterIdentityKey(string characterName, int serverId)
	{
		return $"{serverId}:{StripServerSuffix(characterName).Trim()}";
	}

	private void RememberStigmaSkillLevel(StigmaSkillLevelEvent info)
	{
		if (info.EffectiveLevel <= 0)
		{
			return;
		}
		lock (_stigmaSkillLevelLock)
		{
			if (info.OwnerId > 0)
			{
				RememberStigmaSkillLevelForProvider(info.OwnerId, info);
				int num = _names.ResolveActorId(info.OwnerId);
				if (num > 0 && num != info.OwnerId)
				{
					RememberStigmaSkillLevelForProvider(num, info);
				}
			}
			if (info.OwnerId <= 0 || IsLocalPlayerActor(info.OwnerId))
			{
				RememberStigmaSkillLevelCode(info.SkillCode, info);
				RememberStigmaSkillLevelCode(info.BaseSkillCode, info);
				RememberStigmaSkillLevelCode(GetBaseSkillCode(info.SkillCode), info);
				RememberStigmaSkillLevelCode(GetBaseSkillCode(info.BaseSkillCode), info);
			}
		}
	}

	private void RememberStigmaSkillLevelCode(int skillCode, StigmaSkillLevelEvent info)
	{
		if (skillCode > 0)
		{
			_stigmaSkillLevelByCode[skillCode] = info;
		}
	}

	private void RememberStigmaSkillLevelForProvider(int providerId, StigmaSkillLevelEvent info)
	{
		if (providerId <= 0)
		{
			return;
		}
		Span<int> output = stackalloc int[8];
		int num = BuildSkillLevelLookupCodes(output, info.SkillCode, info.BaseSkillCode);
		for (int i = 0; i < num; i++)
		{
			int num2 = output[i];
			if (num2 > 0)
			{
				_stigmaSkillLevelByProviderAndCode[(providerId, num2)] = info;
			}
		}
	}

	public bool TryGetStigmaSkillLevelForProvider(int providerId, int skillCode, out int level)
	{
		level = 0;
		if (providerId <= 0 || skillCode <= 0)
		{
			return false;
		}
		int num = _names.ResolveActorId(providerId);
		lock (_stigmaSkillLevelLock)
		{
			Span<int> output = stackalloc int[4];
			int num2 = BuildSkillLevelLookupCodes(output, skillCode);
			for (int i = 0; i < num2; i++)
			{
				int item = output[i];
				if ((_stigmaSkillLevelByProviderAndCode.TryGetValue((providerId, item), out StigmaSkillLevelEvent value) || (num > 0 && num != providerId && _stigmaSkillLevelByProviderAndCode.TryGetValue((num, item), out value))) && value.EffectiveLevel > 0)
				{
					level = value.EffectiveLevel;
					return true;
				}
			}
			if (!IsLocalPlayerActor(providerId) && (num <= 0 || !IsLocalPlayerActor(num)))
			{
				return false;
			}
			for (int j = 0; j < num2; j++)
			{
				int key = output[j];
				if (_stigmaSkillLevelByCode.TryGetValue(key, out StigmaSkillLevelEvent value2) && value2.EffectiveLevel > 0)
				{
					level = value2.EffectiveLevel;
					return true;
				}
			}
		}
		return false;
	}

	private bool TryGetLocalStigmaSkillLevelForDamage(int actorId, int displaySkillCode, int skillCode, int rawSkillCode, out int skillLevel, out int baseSkillLevel)
	{
		skillLevel = 0;
		baseSkillLevel = 0;
		if (!IsLocalPlayerActor(actorId))
		{
			return false;
		}
		lock (_stigmaSkillLevelLock)
		{
			Span<int> output = stackalloc int[12];
			int num = BuildSkillLevelLookupCodes(output, displaySkillCode, skillCode, rawSkillCode);
			for (int i = 0; i < num; i++)
			{
				int num2 = output[i];
				if (_stigmaSkillLevelByProviderAndCode.TryGetValue((actorId, num2), out StigmaSkillLevelEvent value))
				{
					skillLevel = value.EffectiveLevel;
					baseSkillLevel = value.BaseSkillLevel;
					return skillLevel > 0;
				}
				if (_stigmaSkillLevelByCode.TryGetValue(num2, out StigmaSkillLevelEvent value2))
				{
					skillLevel = value2.EffectiveLevel;
					baseSkillLevel = value2.BaseSkillLevel;
					return skillLevel > 0;
				}
			}
		}
		return false;
	}

	private bool TryGetStigmaSkillLevelForBuff(int providerId, int skillId, int buffId, out int skillLevel, out int baseSkillLevel)
	{
		skillLevel = 0;
		baseSkillLevel = 0;
		if (providerId <= 0)
		{
			return false;
		}
		int num = _names.ResolveActorId(providerId);
		lock (_stigmaSkillLevelLock)
		{
			Span<int> output = stackalloc int[8];
			int num2 = BuildSkillLevelLookupCodes(output, skillId, buffId);
			for (int i = 0; i < num2; i++)
			{
				int item = output[i];
				if (_stigmaSkillLevelByProviderAndCode.TryGetValue((providerId, item), out StigmaSkillLevelEvent value) || (num > 0 && num != providerId && _stigmaSkillLevelByProviderAndCode.TryGetValue((num, item), out value)))
				{
					skillLevel = value.EffectiveLevel;
					baseSkillLevel = value.BaseSkillLevel;
					return skillLevel > 0;
				}
			}
		}
		return false;
	}

	private static int BuildSkillLevelLookupCodes(Span<int> output, int firstSkillCode, int secondSkillCode = 0, int thirdSkillCode = 0)
	{
		int count = 0;
		AddSkillLevelLookupCodes(output, ref count, firstSkillCode);
		AddSkillLevelLookupCodes(output, ref count, secondSkillCode);
		AddSkillLevelLookupCodes(output, ref count, thirdSkillCode);
		return count;
	}

	private static void AddSkillLevelLookupCodes(Span<int> output, ref int count, int skillCode)
	{
		if (skillCode > 0)
		{
			AddSkillLevelLookupCode(output, ref count, skillCode);
			if (skillCode >= 100000000)
			{
				int skillCode2 = skillCode / 10;
				AddSkillLevelLookupCode(output, ref count, skillCode2);
				AddSkillLevelLookupCode(output, ref count, GetBaseSkillCode(skillCode2));
			}
			AddSkillLevelLookupCode(output, ref count, GetBaseSkillCode(skillCode));
		}
	}

	private static void AddSkillLevelLookupCode(Span<int> output, ref int count, int skillCode)
	{
		if (skillCode > 0 && !ContainsSkillLevelLookupCode(output.Slice(0, count), skillCode) && count < output.Length)
		{
			output[count++] = skillCode;
		}
	}

	private static bool ContainsSkillLevelLookupCode(ReadOnlySpan<int> codes, int skillCode)
	{
		for (int i = 0; i < codes.Length; i++)
		{
			if (codes[i] == skillCode)
			{
				return true;
			}
		}
		return false;
	}

	private bool IsLocalPlayerActor(int actorId)
	{
		if (_names.LocalPlayerActorId == actorId)
		{
			return true;
		}
		string localPlayerName = _names.LocalPlayerName;
		if (string.IsNullOrWhiteSpace(localPlayerName))
		{
			return false;
		}
		return string.Equals(StripServerSuffix(_names.GetOrFallback(actorId)), StripServerSuffix(localPlayerName), StringComparison.OrdinalIgnoreCase);
	}

	private static int GetBaseSkillCode(int skillCode)
	{
		int num = Math.Abs(skillCode);
		if (num < 10000000)
		{
			return num;
		}
		return num / 10000 * 10000;
	}

	private static (string CharacterName, int ServerId) SplitLogCharacterIdentity(string fullName)
	{
		if (string.IsNullOrWhiteSpace(fullName))
		{
			return (CharacterName: "", ServerId: 0);
		}
		string text = fullName.Trim();
		string serverName = "";
		int num = text.LastIndexOf('[');
		int num2 = text.LastIndexOf(']');
		if (num > 0 && num2 > num)
		{
			serverName = text.Substring(num + 1, num2 - num - 1).Trim();
			text = text.Substring(0, num).Trim();
		}
		int aion2ServerId = PartyTracker.GetAion2ServerId(serverName);
		return (CharacterName: text, ServerId: aion2ServerId);
	}

	private bool TryGetCombatPowerForLog(string actorName, out int combatPower)
	{
		var (characterName, serverId) = SplitLogCharacterIdentity(actorName);
		return TryGetCombatPower(characterName, serverId, out combatPower);
	}

	private void TryRememberCombatPowerFromLog(string actorName, string combatPowerText)
	{
		if (int.TryParse(combatPowerText, out var result) && result > 0)
		{
			var (characterName, serverId) = SplitLogCharacterIdentity(actorName);
			RememberCombatPower(characterName, serverId, result);
		}
	}

	public void DisableUploadsForCurrentSession()
	{
		_suppressUploadsForCurrentSession = true;
	}

	public MeterEngine()
	{
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
		_agg = new CombatAggregator(_names);
		_agg.DamageAdded += delegate(DamageEvent e)
		{
			this.DamageEventParsed?.Invoke(e);
			if (!_isLogViewing)
			{
				string orFallback = _names.GetOrFallback(e.ActorId);
				string orFallback2 = _names.GetOrFallback(e.TargetId);
				string specials = ((e.Specials == null) ? "" : string.Join("|", e.Specials));
				lock (_sessionCombatLogLock)
				{
					_sessionCombatLog.Add(new SessionCombatLogEntry(e.TimestampUtc, e.IsDot, e.ActorId, orFallback, e.TargetId, orFallback2, e.SkillCodeRaw, e.Damage, e.MultiHitDamage, e.HealAmount, specials, e.SkillLevel, e.BaseSkillLevel));
				}
			}
		};
		CombatAggregator agg = _agg;
		agg.OnBossEnded = (Action<int, string, DateTime, DateTime, int, int>)Delegate.Combine(agg.OnBossEnded, new Action<int, string, DateTime, DateTime, int, int>(SaveEncounterLog));
		_webUploader = new WebUploader(this);
		TryInitializeBridge(ServerPort);
	}

	private void PublishBuffEvent(BuffEvent buff)
	{
		BuffEvent buffEvent = EnrichBuffEventWithSkillLevel(buff);
		this.BuffEventParsed?.Invoke(buffEvent);
		if (_isLogViewing)
		{
			return;
		}
		lock (_sessionCombatLogLock)
		{
			SessionBuffLogEntry sessionBuffLogEntry = new SessionBuffLogEntry(buffEvent.TimestampUtc, buffEvent.Kind ?? "", buffEvent.TargetId, buffEvent.OwnerId, buffEvent.BuffId, buffEvent.SkillId, buffEvent.DurationMs, buffEvent.StartedAtMs, buffEvent.ExpiresAtMs, buffEvent.SkillLevel, buffEvent.BaseSkillLevel);
			_sessionBuffLog.Add(sessionBuffLogEntry);
			UpdateActiveBuffLog(sessionBuffLogEntry);
			if (++_sessionBuffLogAppendCount >= 512)
			{
				_sessionBuffLogAppendCount = 0;
				PruneSessionBuffLog(DateTime.UtcNow);
			}
		}
	}

	private BuffEvent EnrichBuffEventWithSkillLevel(BuffEvent buff)
	{
		if (buff.SkillLevel > 0)
		{
			return buff;
		}
		int providerId = ((buff.OwnerId > 0) ? buff.OwnerId : buff.TargetId);
		if (!TryGetStigmaSkillLevelForBuff(providerId, buff.SkillId, buff.BuffId, out var skillLevel, out var baseSkillLevel))
		{
			return buff;
		}
		return buff with
		{
			SkillLevel = skillLevel,
			BaseSkillLevel = baseSkillLevel
		};
	}

	private void UpdateActiveBuffLog(SessionBuffLogEntry entry)
	{
		int num = ((entry.BuffId > 0) ? entry.BuffId : entry.SkillId);
		if (num <= 0)
		{
			return;
		}
		int num2 = ((entry.TargetId > 0) ? _names.ResolveActorId(entry.TargetId) : 0);
		int num3 = ((entry.OwnerId > 0) ? _names.ResolveActorId(entry.OwnerId) : 0);
		int num4 = ((num2 > 0) ? num2 : num3);
		if (num4 <= 0)
		{
			return;
		}
		EncounterBuffStateKey key = new EncounterBuffStateKey(num4, num);
		SessionBuffLogEntry value = entry with
		{
			TargetId = ((num2 > 0) ? num2 : entry.TargetId),
			OwnerId = ((num3 > 0) ? num3 : entry.OwnerId)
		};
		if (!BuffIntervalUtilities.HasInterval(value.DurationMs, value.ExpiresAtMs))
		{
			_activeBuffLog.Remove(key);
			return;
		}
		(DateTime, DateTime) interval = BuffIntervalUtilities.GetInterval(value.TimestampUtc, value.DurationMs, value.StartedAtMs, value.ExpiresAtMs);
		if (interval.Item2 <= interval.Item1 || interval.Item2 <= DateTime.UtcNow)
		{
			_activeBuffLog.Remove(key);
			return;
		}
		_activeBuffLog[key] = value;
		PruneActiveBuffLog(DateTime.UtcNow);
	}

	private void PruneActiveBuffLog(DateTime utcNow)
	{
		foreach (EncounterBuffStateKey item in _activeBuffLog.Keys.ToList())
		{
			SessionBuffLogEntry sessionBuffLogEntry = _activeBuffLog[item];
			if (!BuffIntervalUtilities.HasInterval(sessionBuffLogEntry.DurationMs, sessionBuffLogEntry.ExpiresAtMs) || BuffIntervalUtilities.GetInterval(sessionBuffLogEntry.TimestampUtc, sessionBuffLogEntry.DurationMs, sessionBuffLogEntry.StartedAtMs, sessionBuffLogEntry.ExpiresAtMs).End <= utcNow)
			{
				_activeBuffLog.Remove(item);
			}
		}
	}

	private void PruneSessionBuffLog(DateTime utcNow)
	{
		if (_sessionBuffLog.Count == 0)
		{
			return;
		}
		DateTime cutoffUtc = utcNow - SessionBuffLogIdleRetention;
		if (_sessionCombatLog.Count > 0)
		{
			List<SessionCombatLogEntry> sessionCombatLog = _sessionCombatLog;
			DateTime timestampUtc = sessionCombatLog[sessionCombatLog.Count - 1].TimestampUtc;
			if (utcNow - timestampUtc <= SessionBuffLogActiveGrace)
			{
				DateTime dateTime = _sessionCombatLog[0].TimestampUtc.AddSeconds(-120.0);
				if (dateTime < cutoffUtc)
				{
					cutoffUtc = dateTime;
				}
			}
		}
		_sessionBuffLog.RemoveAll((SessionBuffLogEntry entry) => IsExpiredSessionBuffLogEntry(entry, cutoffUtc));
	}

	private static bool IsExpiredSessionBuffLogEntry(SessionBuffLogEntry entry, DateTime cutoffUtc)
	{
		if (entry.TimestampUtc >= cutoffUtc)
		{
			return false;
		}
		if (!BuffIntervalUtilities.HasInterval(entry.DurationMs, entry.ExpiresAtMs))
		{
			return true;
		}
		return BuffIntervalUtilities.GetInterval(entry.TimestampUtc, entry.DurationMs, entry.StartedAtMs, entry.ExpiresAtMs).End < cutoffUtc;
	}

	private void TryInitializeBridge(int serverPort)
	{
		try
		{
			PacketProcessorBridge packetProcessorBridge = new PacketProcessorBridge(serverPort, tcpReorder: true, (int code) => ResolveSkillName?.Invoke(code), (int code) => ContainsSkillCode?.Invoke(code) ?? false, IsKnownStigmaSkillCode);
			packetProcessorBridge.TraceLookupCallbacks = _nativeLookupTraceEnabled;
			packetProcessorBridge.OnDamage += delegate(DateTime timestampUtc, int actorId, int targetId, int skillCode, int rawSkillCode, byte dmgType, int damage, uint flags, int multiCount, int multiDmg, int heal, bool isDot)
			{
				IReadOnlyList<SpecialDamage> specials = ConvertSpecialFlags(flags);
				int num = ResolveDisplaySkillCode(actorId, skillCode, rawSkillCode);
				TryGetLocalStigmaSkillLevelForDamage(actorId, num, skillCode, rawSkillCode, out var skillLevel, out var baseSkillLevel);
				DamageEvent e = new DamageEvent(isDot, actorId, targetId, num, dmgType, damage + multiDmg, (int)flags, 0, 0, specials, timestampUtc, multiCount, multiDmg, heal)
				{
					SkillLevel = skillLevel,
					BaseSkillLevel = baseSkillLevel
				};
				_agg.OnDamage(e);
			};
			packetProcessorBridge.OnUserInfo += delegate(int entityId, string nickname, int serverId, int jobCode, int extra, int characterNumber)
			{
				if (string.IsNullOrWhiteSpace(nickname))
				{
					return;
				}
				string serverName = GetServerName(serverId);
				string name = (string.IsNullOrEmpty(serverName) ? nickname : (nickname.Contains("[" + serverName + "]") ? nickname : (nickname + "[" + serverName + "]")));
				bool flag = false;
				string localName = "";
				int num;
				int num2;
				if (extra == 1)
				{
					num = ((jobCode > 0) ? 1 : 0);
					if (num != 0)
					{
						num2 = ((serverId > 0) ? 1 : 0);
						goto IL_0067;
					}
				}
				else
				{
					num = 0;
				}
				num2 = 0;
				goto IL_0067;
				IL_0067:
				bool flag2 = (byte)num2 != 0;
				if (num != 0)
				{
					this.LocalUserInfoObserved?.Invoke(new LocalUserInfoObservedEvent(DateTime.UtcNow, entityId, nickname, serverId, jobCode, extra, characterNumber));
				}
				if (flag2)
				{
					flag = ApplyPacketLocalPlayer(nickname, entityId, serverId, out localName);
				}
				RememberCharacterNo(nickname, entityId, serverId, characterNumber);
				_agg.ClearMonsterId(entityId);
				_names.ClearSummonMobCode(entityId);
				_agg.SetActorJobClass(entityId, jobCode);
				_agg.SetCharacterJobClass(nickname, serverName, jobCode);
				string source = (flag2 ? "DLL/LocalUserInfo" : "DLL/UserInfo");
				_names.Set(entityId, name, source);
				this.UserInfoResolved?.Invoke(entityId);
				if (ServerPort > 0)
				{
					int? lockedCombatPort = LockedCombatPort;
					int valueOrDefault = lockedCombatPort.GetValueOrDefault();
					if (!lockedCombatPort.HasValue)
					{
						valueOrDefault = ServerPort;
						int? lockedCombatPort2 = valueOrDefault;
						LockedCombatPort = lockedCombatPort2;
					}
				}
				if (flag)
				{
					this.LocalPlayerChanged?.Invoke(localName, entityId);
				}
				if (flag2 && serverId > 0 && !string.IsNullOrWhiteSpace(localName))
				{
					this.LocalPlayerIdentified?.Invoke(localName, entityId, serverId);
				}
			};
			packetProcessorBridge.OnExtendedUserInfo += delegate(ExtendedUserInfoEvent info)
			{
				RememberJobClass(info);
				RememberCombatPower(info);
				RememberCharacterNo(info);
				this.ExtendedUserInfoReceived?.Invoke(info);
				if (info.EntityId == 7 && info.Source == 3 && info.ServerId > 0 && info.JobCode > 0 && !string.IsNullOrWhiteSpace(info.Nickname))
				{
					string serverName = GetServerName(info.ServerId);
					if (!string.IsNullOrWhiteSpace(serverName))
					{
						this._inspectCharacterDetected?.Invoke(info.Nickname, serverName, info.JobCode);
					}
				}
			};
			packetProcessorBridge.OnZoneEntry += delegate(ZoneEntryEvent entry)
			{
				this.ZoneEntryReceived?.Invoke(entry);
			};
			packetProcessorBridge.OnStigmaSkillLevel += delegate(StigmaSkillLevelEvent info)
			{
				RememberStigmaSkillLevel(info);
				this.StigmaSkillLevelReceived?.Invoke(info);
			};
			packetProcessorBridge.OnLocalPlayerState += delegate(LocalPlayerStateEvent info)
			{
				this.LocalPlayerStateReceived?.Invoke(info);
			};
			packetProcessorBridge.OnMobSpawn += delegate(int mobId, int mobCode, int hp)
			{
				bool num = IsExcludedBossMobCode(mobCode);
				string text = ResolveMobName?.Invoke(mobCode) ?? $"Mob_{mobCode}";
				bool num2 = !num && (ResolveMobBossStatus?.Invoke(mobCode) ?? false);
				_names.Set(mobId, text, "DLL/MobSpawn");
				if (SpiritBasicSkillByMobCode.ContainsKey(mobCode))
				{
					_names.SetSummonMobCode(mobId, mobCode);
				}
				else
				{
					_names.ClearSummonMobCode(mobId);
				}
				_names.RegisterMonster(mobId, text);
				_agg.SetMonsterId(mobId);
				if (num2)
				{
					_agg.ConfirmBossTarget(mobId, text, hp, mobCode, IsTrainingDummy(text));
				}
			};
			packetProcessorBridge.OnMobSpawnInfo += delegate(MobSpawnObservedEvent info)
			{
				this.MobSpawnObserved?.Invoke(info);
			};
			packetProcessorBridge.OnEntityRemoved += delegate(int entityId)
			{
				_agg.HandleEntityRemoved(entityId);
				if (!_agg.IsConfirmedBossTarget(entityId))
				{
					_names.RemoveEntitySessionState(entityId);
				}
			};
			packetProcessorBridge.OnEntityUInt += delegate(int entityId, uint value)
			{
				_agg.UpdateBossCurrentHp(entityId, value);
				_agg.UpdateEntityHp(entityId, value);
			};
			packetProcessorBridge.OnSummon += delegate(int ownerId, int petId)
			{
				_names.SetSummonOwner(petId, ownerId);
			};
			packetProcessorBridge.OnBuff += delegate(BuffEvent buff)
			{
				PublishBuffEvent(buff);
			};
			packetProcessorBridge.OnAbyssArtifactState += delegate(AbyssArtifactStateEvent info)
			{
				this.AbyssArtifactStateReceived?.Invoke(info);
			};
			packetProcessorBridge.OnLog += delegate
			{
				_ = 2;
			};
			packetProcessorBridge.OnNativeInfo += delegate(NativePacketInfo info)
			{
				this.NativePacketInfoReceived?.Invoke(info);
			};
			packetProcessorBridge.Start();
			lock (_bridgeLock)
			{
				_bridge = packetProcessorBridge;
			}
			int combatPort = packetProcessorBridge.GetCombatPort();
			string combatDevice = packetProcessorBridge.GetCombatDevice();
			this.NativePacketInfoReceived?.Invoke(new NativePacketInfo(DateTime.UtcNow, "DllStart", $"PacketProcessor.dll started. port={combatPort}, device={combatDevice}", (combatPort > 0) ? combatPort : 0, 0, 0, 0L));
		}
		catch (Exception)
		{
			_bridge = null;
		}
	}

	private static IReadOnlyList<SpecialDamage> ConvertSpecialFlags(uint flags)
	{
		List<SpecialDamage> list = new List<SpecialDamage>();
		if ((flags & 1) != 0)
		{
			list.Add(SpecialDamage.BACK);
		}
		if ((flags & 4) != 0)
		{
			list.Add(SpecialDamage.PARRY);
		}
		if ((flags & 8) != 0)
		{
			list.Add(SpecialDamage.PERFECT);
		}
		if ((flags & 0x10) != 0)
		{
			list.Add(SpecialDamage.DOUBLE);
		}
		if ((flags & 0x40) != 0)
		{
			list.Add(SpecialDamage.SMITE);
		}
		if ((flags & 0x80) != 0)
		{
			list.Add(SpecialDamage.POWER_SHARD);
		}
		if ((flags & 0x100) != 0)
		{
			list.Add(SpecialDamage.CRITICAL);
		}
		if ((flags & 0x200) != 0)
		{
			list.Add(SpecialDamage.IMMUNE);
		}
		return list;
	}

	private int ResolveDisplaySkillCode(int actorId, int skillCode, int rawSkillCode)
	{
		int num = ((rawSkillCode > 0) ? rawSkillCode : skillCode);
		if (_names.TryGetSummonMobCode(actorId, out var code) && IsSpiritBasicLikeSkill(num) && SpiritBasicSkillByMobCode.TryGetValue(code, out var value))
		{
			return value;
		}
		if (skillCode > 0 && num > 0 && skillCode != num)
		{
			string text = ResolveSkillName?.Invoke(num) ?? string.Empty;
			if ((ResolveSkillName?.Invoke(skillCode) ?? string.Empty).StartsWith("소환:", StringComparison.Ordinal) && text.Contains("정령:", StringComparison.Ordinal))
			{
				return skillCode;
			}
		}
		return num;
	}

	private static bool IsSpiritBasicLikeSkill(int skillCode)
	{
		if (skillCode < 100000 || skillCode > 100999)
		{
			if (skillCode >= 1000000)
			{
				return skillCode <= 1009999;
			}
			return false;
		}
		return true;
	}

	private static string GetServerName(int serverId)
	{
		return serverId switch
		{
			1001 => "시엘", 
			1002 => "네자칸", 
			1003 => "바이젤", 
			1004 => "카이시넬", 
			1005 => "유스티엘", 
			1006 => "아리엘", 
			1007 => "프레기온", 
			1008 => "메스람타에다", 
			1009 => "히타니에", 
			1010 => "나니아", 
			1011 => "타하바타", 
			1012 => "루터스", 
			1013 => "페르노스", 
			1014 => "다미누", 
			1015 => "카사카", 
			1016 => "바카르마", 
			1017 => "챈가룽", 
			1018 => "코치룽", 
			1019 => "이슈타르", 
			1020 => "티아마트", 
			1021 => "포에타", 
			2001 => "이스라펠", 
			2002 => "지켈", 
			2003 => "트리니엘", 
			2004 => "루미엘", 
			2005 => "마르쿠탄", 
			2006 => "아스펠", 
			2007 => "에레슈키갈", 
			2008 => "브리트라", 
			2009 => "네몬", 
			2010 => "하달", 
			2011 => "루드라", 
			2012 => "울고른", 
			2013 => "무닌", 
			2014 => "오다르", 
			2015 => "젠카카", 
			2016 => "크로메데", 
			2017 => "콰이링", 
			2018 => "바바룽", 
			2019 => "파프니르", 
			2020 => "인드나흐", 
			2021 => "이스할겐", 
			_ => "", 
		};
	}

	private static bool IsTrainingDummy(string mobName)
	{
		return mobName.Contains("훈련용 허수아비", StringComparison.Ordinal);
	}

	public void OnTcpPayload(int srcPort, int dstPort, ReadOnlySpan<byte> payload, DateTime tsUtc, uint seqNum = 0u, bool isPsh = false)
	{
		OnTcpPayload(srcPort, dstPort, payload.ToArray(), tsUtc, seqNum, isPsh);
	}

	public void SetNativeLookupTraceEnabled(bool enabled)
	{
		_nativeLookupTraceEnabled = enabled;
		lock (_bridgeLock)
		{
			if (_bridge != null)
			{
				_bridge.TraceLookupCallbacks = enabled;
			}
		}
	}

	public void OnTcpPayload(int srcPort, int dstPort, byte[] payload, DateTime tsUtc, uint seqNum = 0u, bool isPsh = false)
	{
		if (payload.Length != 0 && _bridge != null)
		{
			_bridge.Enqueue(srcPort, dstPort, payload, "WinDivert", seqNum, tsUtc);
		}
	}

	public async Task LoadLogFile(string path)
	{
		bool previousBossOnlyMeasurement = _agg.BossOnlyMeasurement;
		bool previousSuppressLocalPlayerAutoLink = _names.SuppressLocalPlayerAutoLink;
		_isLogViewing = true;
		_agg.BossOnlyMeasurement = false;
		_names.SuppressLocalPlayerAutoLink = true;
		try
		{
			ResetSession(startNewLog: false);
			_suppressUploadsForCurrentSession = true;
			Dictionary<int, LoadedLogTargetInfo> loadedTargets = new Dictionary<int, LoadedLogTargetInfo>();
			if (EncounterLogStore.IsRecordFile(path))
			{
				if (!ReplayEncounterRecordFile(path, loadedTargets))
				{
					PromoteLoadedLogTopTargetAsBoss(loadedTargets, path);
				}
				BuildSnapshotNow();
				return;
			}
			using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using StreamReader reader = new StreamReader(fs, Encoding.UTF8);
			bool typedLog = (await reader.ReadLineAsync())?.StartsWith("EventType,", StringComparison.OrdinalIgnoreCase) ?? false;
			bool hasBossMetadata = false;
			while (true)
			{
				string text = await reader.ReadLineAsync();
				if (text == null)
				{
					break;
				}
				if (string.IsNullOrWhiteSpace(text))
				{
					continue;
				}
				List<string> list = SplitCsvLine(text);
				if (list.Count == 0)
				{
					continue;
				}
				try
				{
					if (typedLog)
					{
						string text2 = list[0];
						if (text2.Equals("Boss", StringComparison.OrdinalIgnoreCase))
						{
							hasBossMetadata |= TryReplayBossLogRow(list);
						}
						else if (text2.Equals("Buff", StringComparison.OrdinalIgnoreCase))
						{
							TryReplayBuffLogRow(list, typed: true);
						}
						else
						{
							TryReplayDamageLogRow(list, typed: true, loadedTargets);
						}
					}
					else if (list.Count >= 9 && IsBuffLogKind(list[1]))
					{
						TryReplayBuffLogRow(list, typed: false);
					}
					else
					{
						TryReplayDamageLogRow(list, typed: false, loadedTargets);
					}
				}
				catch
				{
				}
			}
			if (!hasBossMetadata)
			{
				PromoteLoadedLogTopTargetAsBoss(loadedTargets, path);
			}
			BuildSnapshotNow();
		}
		catch (Exception ex)
		{
			Console.WriteLine("[LoadLogFile] Critical Error: " + ex.Message);
			try
			{
				File.AppendAllText(RuntimePaths.GetLogFilePath("error_log.txt"), $"[{DateTime.Now}] LoadLogFile Crash: {ex}\n");
			}
			catch
			{
			}
			throw;
		}
		finally
		{
			_agg.BossOnlyMeasurement = previousBossOnlyMeasurement;
			_names.SuppressLocalPlayerAutoLink = previousSuppressLocalPlayerAutoLink;
			_isLogViewing = false;
		}
	}

	public async Task ReplayEncounterRecordFileAsync(string path, double speed, Func<CombatSnapshot?, EncounterReplayProgress, Task>? onProgress, CancellationToken cancellationToken)
	{
		if (!EncounterLogStore.IsRecordFile(path))
		{
			throw new InvalidDataException("Replay is supported only for saved encounter records (.inglog).");
		}
		CompactEncounterReplayData replay = ReadCompactEncounterReplayData(path);
		double replaySpeed = Math.Clamp(speed, 0.1, 120.0);
		TimeSpan timeSpan;
		if (!(replay.EndUtc > replay.StartUtc))
		{
			long milliseconds;
			if (replay.DamageEvents.Count <= 0)
			{
				milliseconds = 0L;
			}
			else
			{
				IReadOnlyList<CompactDamageReplayEntry> damageEvents = replay.DamageEvents;
				milliseconds = damageEvents[damageEvents.Count - 1].OffsetMs;
			}
			timeSpan = TimeSpan.FromMilliseconds(milliseconds);
		}
		else
		{
			timeSpan = replay.EndUtc - replay.StartUtc;
		}
		TimeSpan duration = timeSpan;
		if (duration <= TimeSpan.Zero)
		{
			duration = TimeSpan.FromMilliseconds(Math.Max(1, replay.DamageEvents.Count));
		}
		bool previousBossOnlyMeasurement = _agg.BossOnlyMeasurement;
		bool previousSuppressLocalPlayerAutoLink = _names.SuppressLocalPlayerAutoLink;
		_isLogViewing = true;
		_agg.BossOnlyMeasurement = false;
		_names.SuppressLocalPlayerAutoLink = true;
		try
		{
			ResetSession(startNewLog: false);
			_suppressUploadsForCurrentSession = true;
			ApplyEncounterRecordParticipants(replay.Meta);
			ApplyEncounterReplayBossMetadata(replay);
			Dictionary<int, LoadedLogTargetInfo> loadedTargets = new Dictionary<int, LoadedLogTargetInfo>();
			int total = replay.DamageEvents.Count;
			int played = 0;
			int nextEventIndex = 0;
			await ReportEncounterReplayProgress(replay, onProgress, duration, 0, total, TimeSpan.Zero, complete: false, cancellationToken);
			Stopwatch clock = Stopwatch.StartNew();
			DateTime lastProgressUtc = DateTime.UtcNow;
			while (nextEventIndex < total)
			{
				cancellationToken.ThrowIfCancellationRequested();
				long dueOffsetMs = (long)Math.Floor(clock.Elapsed.TotalMilliseconds * replaySpeed);
				bool flag = false;
				while (nextEventIndex < total && replay.DamageEvents[nextEventIndex].OffsetMs <= dueOffsetMs)
				{
					CompactDamageReplayEntry compactDamageReplayEntry = replay.DamageEvents[nextEventIndex++];
					ReplayDamageLogEntry(compactDamageReplayEntry.TimestampUtc, compactDamageReplayEntry.IsDot, compactDamageReplayEntry.ActorId, compactDamageReplayEntry.ActorName, compactDamageReplayEntry.TargetId, compactDamageReplayEntry.TargetName, compactDamageReplayEntry.SkillId, compactDamageReplayEntry.Damage, compactDamageReplayEntry.MultiDamage, compactDamageReplayEntry.Heal, compactDamageReplayEntry.SpecialsText, compactDamageReplayEntry.SkillLevel, compactDamageReplayEntry.BaseSkillLevel);
					RememberLoadedLogTarget(loadedTargets, compactDamageReplayEntry.TargetId, compactDamageReplayEntry.TargetName, compactDamageReplayEntry.Damage);
					played++;
					flag = true;
				}
				DateTime utcNow = DateTime.UtcNow;
				if ((utcNow - lastProgressUtc).TotalMilliseconds >= 100.0)
				{
					if (flag)
					{
						BuildSnapshotNow();
					}
					lastProgressUtc = utcNow;
					TimeSpan position = TimeSpan.FromMilliseconds(Math.Min(dueOffsetMs, (long)duration.TotalMilliseconds));
					await ReportEncounterReplayProgress(replay, onProgress, duration, played, total, position, complete: false, cancellationToken);
				}
				if (nextEventIndex >= total)
				{
					break;
				}
				long offsetMs = replay.DamageEvents[nextEventIndex].OffsetMs;
				await Task.Delay((int)Math.Clamp(Math.Max(1.0, (double)(offsetMs - dueOffsetMs) / replaySpeed), 1.0, 100.0), cancellationToken);
			}
			if (replay.BossActorId <= 0)
			{
				PromoteLoadedLogTopTargetAsBoss(loadedTargets, path);
			}
			BuildSnapshotNow();
			await ReportEncounterReplayProgress(replay, onProgress, duration, played, total, duration, complete: true, cancellationToken);
		}
		finally
		{
			_agg.BossOnlyMeasurement = previousBossOnlyMeasurement;
			_names.SuppressLocalPlayerAutoLink = previousSuppressLocalPlayerAutoLink;
			_isLogViewing = false;
		}
	}

	private async Task ReportEncounterReplayProgress(CompactEncounterReplayData replay, Func<CombatSnapshot?, EncounterReplayProgress, Task>? onProgress, TimeSpan duration, int played, int total, TimeSpan position, bool complete, CancellationToken cancellationToken)
	{
		if (onProgress != null)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await onProgress(BuildEncounterReplaySnapshot(replay), new EncounterReplayProgress(played, total, position, duration, complete));
		}
	}

	private CombatSnapshot? BuildEncounterReplaySnapshot(CompactEncounterReplayData replay)
	{
		if (replay.BossActorId > 0)
		{
			return BuildSnapshotForTarget(replay.BossActorId) ?? LatestSnapshot;
		}
		return LatestSnapshot;
	}

	private CompactEncounterReplayData ReadCompactEncounterReplayData(string path)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(EncounterLogStore.ReadRecordJson(path));
		JsonElement rootElement = jsonDocument.RootElement;
		if (!rootElement.TryGetProperty("log", out var value) || value.ValueKind != JsonValueKind.Object)
		{
			throw new InvalidDataException("Saved encounter record does not contain a combat log.");
		}
		EncounterLogRecordMeta encounterLogRecordMeta = null;
		if (rootElement.TryGetProperty("meta", out var value2) && value2.ValueKind == JsonValueKind.Object)
		{
			try
			{
				encounterLogRecordMeta = value2.Deserialize<EncounterLogRecordMeta>(EncounterRecordJsonOptions);
			}
			catch
			{
				encounterLogRecordMeta = null;
			}
		}
		if (!TryReadLogDateTime(value, "s", out var value3))
		{
			value3 = encounterLogRecordMeta?.StartUtc ?? DateTime.UtcNow;
		}
		if (!TryReadLogDateTime(value, "e", out var value4))
		{
			value4 = encounterLogRecordMeta?.EndUtc ?? value3;
		}
		List<(int, string)> actors = ReadCompactEntityList(value, "a");
		List<(int, string)> targets = ReadCompactEntityList(value, "t");
		List<int> skills = ReadCompactIntList(value, "sk");
		List<string> list = ReadCompactStringList(value, "sf");
		if (list.Count == 0)
		{
			list = EncounterSpecialFlagOrder.ToList();
		}
		int num = encounterLogRecordMeta?.BossActorId ?? 0;
		string bossName = encounterLogRecordMeta?.BossName ?? "";
		int bossMobCode = encounterLogRecordMeta?.BossMobCode ?? 0;
		int bossMaxHp = encounterLogRecordMeta?.BossMaxHp ?? 0;
		if (num <= 0 && TryReadCompactBossMetadata(value, out int bossActorId, out string bossName2, out int bossMobCode2, out int bossMaxHp2))
		{
			num = bossActorId;
			bossName = bossName2;
			bossMobCode = bossMobCode2;
			bossMaxHp = bossMaxHp2;
		}
		return new CompactEncounterReplayData(encounterLogRecordMeta, value3, value4, num, bossName, bossMobCode, bossMaxHp, ReadCompactDamageReplayEntries(value, value3, actors, targets, skills, list));
	}

	private void ApplyEncounterReplayBossMetadata(CompactEncounterReplayData replay)
	{
		if (replay.BossActorId > 0)
		{
			string name = (string.IsNullOrWhiteSpace(replay.BossName) ? $"Actor {replay.BossActorId}" : replay.BossName);
			_names.Set(replay.BossActorId, name, "Record/Boss");
			_agg.ConfirmBossTarget(replay.BossActorId, name, replay.BossMaxHp, replay.BossMobCode, suppressUpload: true);
		}
	}

	private bool ReplayEncounterRecordFile(string path, Dictionary<int, LoadedLogTargetInfo> loadedTargets)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(EncounterLogStore.ReadRecordJson(path));
		JsonElement rootElement = jsonDocument.RootElement;
		if (!rootElement.TryGetProperty("log", out var value) || value.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		EncounterLogRecordMeta encounterLogRecordMeta = null;
		if (rootElement.TryGetProperty("meta", out var value2) && value2.ValueKind == JsonValueKind.Object)
		{
			try
			{
				encounterLogRecordMeta = value2.Deserialize<EncounterLogRecordMeta>(EncounterRecordJsonOptions);
			}
			catch
			{
				encounterLogRecordMeta = null;
			}
		}
		bool flag = false;
		if (encounterLogRecordMeta != null && encounterLogRecordMeta.BossActorId > 0)
		{
			string name = (string.IsNullOrWhiteSpace(encounterLogRecordMeta.BossName) ? $"Actor {encounterLogRecordMeta.BossActorId}" : encounterLogRecordMeta.BossName);
			_names.Set(encounterLogRecordMeta.BossActorId, name, "Record/Boss");
			_agg.ConfirmBossTarget(encounterLogRecordMeta.BossActorId, name, encounterLogRecordMeta.BossMaxHp, encounterLogRecordMeta.BossMobCode, suppressUpload: true);
			flag = true;
		}
		ApplyEncounterRecordParticipants(encounterLogRecordMeta);
		return ReplayCompactEncounterLog(value, encounterLogRecordMeta, loadedTargets) || flag;
	}

	private void ApplyEncounterRecordParticipants(EncounterLogRecordMeta? meta)
	{
		if (meta?.Participants == null || meta.Participants.Count == 0)
		{
			return;
		}
		foreach (EncounterLogParticipantMeta participant in meta.Participants)
		{
			if (participant.ActorId > 0)
			{
				if (!string.IsNullOrWhiteSpace(participant.Name))
				{
					string text = participant.Name.Trim();
					string text2 = participant.ServerName?.Trim() ?? "";
					string name = (string.IsNullOrWhiteSpace(text2) ? text : (text + "[" + text2 + "]"));
					_names.Set(participant.ActorId, name, "Record/Participant");
				}
				if (participant.Job != JobClass.None)
				{
					_agg.SetActorJobClass(participant.ActorId, participant.Job);
				}
			}
		}
	}

	private bool ReplayCompactEncounterLog(JsonElement log, EncounterLogRecordMeta? meta, Dictionary<int, LoadedLogTargetInfo> loadedTargets)
	{
		if (!TryReadLogDateTime(log, "s", out var value))
		{
			value = meta?.StartUtc ?? DateTime.UtcNow;
		}
		if (!TryReadLogDateTime(log, "e", out var value2))
		{
			value2 = meta?.EndUtc ?? value;
		}
		bool flag = false;
		if (meta == null || meta.BossActorId <= 0)
		{
			flag = TryReplayCompactBossMetadata(log);
		}
		List<(int, string)> actors = ReadCompactEntityList(log, "a");
		List<(int, string)> targets = ReadCompactEntityList(log, "t");
		List<int> skills = ReadCompactIntList(log, "sk");
		List<string> list = ReadCompactStringList(log, "sf");
		if (list.Count == 0)
		{
			list = EncounterSpecialFlagOrder.ToList();
		}
		List<CompactDamageReplayEntry> list2 = ReadCompactDamageReplayEntries(log, value, actors, targets, skills, list);
		foreach (CompactDamageReplayEntry item in list2)
		{
			ReplayDamageLogEntry(item.TimestampUtc, item.IsDot, item.ActorId, item.ActorName, item.TargetId, item.TargetName, item.SkillId, item.Damage, item.MultiDamage, item.Heal, item.SpecialsText, item.SkillLevel, item.BaseSkillLevel);
			RememberLoadedLogTarget(loadedTargets, item.TargetId, item.TargetName, item.Damage);
		}
		ReplayCompactBuffWindows(log, value);
		ReplayCompactBuffUptimes(log, actors, value, value2);
		return list2.Count > 0 || ((object)meta != null && meta.BossActorId > 0) || flag;
	}

	private static List<CompactDamageReplayEntry> ReadCompactDamageReplayEntries(JsonElement log, DateTime startUtc, IReadOnlyList<(int Id, string Name)> actors, IReadOnlyList<(int Id, string Name)> targets, IReadOnlyList<int> skills, IReadOnlyList<string> flags)
	{
		List<CompactDamageReplayEntry> list = new List<CompactDamageReplayEntry>();
		if (!log.TryGetProperty("ev", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 9)
			{
				long val = ReadArrayInt64(item, 0);
				bool isDot = ReadArrayInt(item, 1) != 0;
				int index = ReadArrayInt(item, 2);
				int index2 = ReadArrayInt(item, 3);
				int num = ReadArrayInt(item, 4);
				if (TryGetIndexedEntity(actors, index, out int id, out string name) && TryGetIndexedEntity(targets, index2, out int id2, out string name2))
				{
					list.Add(new CompactDamageReplayEntry(Math.Max(0L, val), startUtc.AddMilliseconds(Math.Max(0L, val)), isDot, id, name, id2, name2, (num >= 0 && num < skills.Count) ? skills[num] : 0, ReadArrayInt(item, 5), ReadArrayInt(item, 6), ReadArrayInt(item, 7), BuildSpecialTextFromMask(ReadArrayInt(item, 8), flags), (item.GetArrayLength() > 9) ? ReadArrayInt(item, 9) : 0, (item.GetArrayLength() > 10) ? ReadArrayInt(item, 10) : 0));
				}
			}
		}
		return list.OrderBy((CompactDamageReplayEntry x) => x.OffsetMs).ToList();
	}

	private bool TryReplayCompactBossMetadata(JsonElement log)
	{
		if (!TryReadCompactBossMetadata(log, out int bossActorId, out string bossName, out int bossMobCode, out int bossMaxHp))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(bossName))
		{
			bossName = $"Actor {bossActorId}";
		}
		_names.Set(bossActorId, bossName, "CompactLog/Boss");
		_agg.ConfirmBossTarget(bossActorId, bossName, bossMaxHp, bossMobCode, suppressUpload: true);
		return true;
	}

	private static bool TryReadCompactBossMetadata(JsonElement log, out int bossActorId, out string bossName, out int bossMobCode, out int bossMaxHp)
	{
		bossActorId = 0;
		bossName = "";
		bossMobCode = 0;
		bossMaxHp = 0;
		if (!log.TryGetProperty("b", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 4)
		{
			return false;
		}
		bossActorId = ReadArrayInt(value, 0);
		if (bossActorId <= 0)
		{
			bossActorId = 0;
			return false;
		}
		bossName = ReadArrayString(value, 1);
		bossMobCode = ReadArrayInt(value, 2);
		bossMaxHp = ReadArrayInt(value, 3);
		return true;
	}

	private void ReplayCompactBuffUptimes(JsonElement log, IReadOnlyList<(int Id, string Name)> actors, DateTime startUtc, DateTime endUtc)
	{
		if (!log.TryGetProperty("bf", out var value) || value.ValueKind != JsonValueKind.Array || !log.TryGetProperty("bu", out var value2) || value2.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		List<(int, int)> list = new List<(int, int)>();
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 2)
			{
				list.Add((ReadArrayInt(item, 0), ReadArrayInt(item, 1)));
			}
		}
		long num = Math.Max(1L, (long)Math.Round((endUtc - startUtc).TotalMilliseconds));
		foreach (JsonElement item2 in value2.EnumerateArray())
		{
			if (item2.ValueKind != JsonValueKind.Array || item2.GetArrayLength() < 4)
			{
				continue;
			}
			int index = ReadArrayInt(item2, 0);
			int num2 = ReadArrayInt(item2, 1);
			int num3 = Math.Max(0, ReadArrayInt(item2, 2));
			int num4 = Math.Max(1, ReadArrayInt(item2, 3));
			if (num3 <= 0 || !TryGetIndexedEntity(actors, index, out int id, out string _) || num2 < 0 || num2 >= list.Count)
			{
				continue;
			}
			(int, int) tuple = list[num2];
			int num5 = ((num4 > 1) ? 1 : 0);
			int num6 = Math.Max(1, (num3 - num5 * (num4 - 1)) / num4);
			long num7 = 0L;
			for (int i = 0; i < num4; i++)
			{
				if (num7 >= num)
				{
					break;
				}
				DateTime dateTime = startUtc.AddMilliseconds(num7);
				int num8 = (int)Math.Min(num6, Math.Max(1L, num - num7));
				DateTime dateTime2 = dateTime.AddMilliseconds(num8);
				ulong startedAtMs = (ulong)new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
				ulong expiresAtMs = (ulong)new DateTimeOffset(dateTime2).ToUnixTimeMilliseconds();
				Action<BuffEvent>? action = this.BuffEventParsed;
				if (action != null)
				{
					DateTime timestampUtc = dateTime;
					int targetId = id;
					int ownerId = id;
					var (buffId, _) = tuple;
					int skillId;
					if (tuple.Item2 <= 0)
					{
						(skillId, _) = tuple;
					}
					else
					{
						skillId = tuple.Item2;
					}
					action(new BuffEvent(timestampUtc, "BuffApplied", targetId, ownerId, buffId, skillId, (uint)num8, startedAtMs, expiresAtMs));
				}
				num7 += num8 + num5;
			}
		}
	}

	private void ReplayCompactBuffWindows(JsonElement log, DateTime startUtc)
	{
		if (!log.TryGetProperty("bw", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 6)
			{
				int num = ReadArrayInt(item, 0);
				int ownerId = ReadArrayInt(item, 1);
				int num2 = ReadArrayInt(item, 2);
				int num3 = ReadArrayInt(item, 3);
				long num4 = ReadArrayInt64(item, 4);
				long num5 = ReadArrayInt64(item, 5);
				string text = ((item.GetArrayLength() >= 7) ? ReadArrayString(item, 6) : "BuffApplied");
				int skillLevel = ((item.GetArrayLength() >= 8) ? ReadArrayInt(item, 7) : 0);
				int baseSkillLevel = ((item.GetArrayLength() >= 9) ? ReadArrayInt(item, 8) : 0);
				if (num > 0 && (num2 > 0 || num3 > 0) && num5 > num4)
				{
					DateTime dateTime = startUtc.AddMilliseconds(Math.Max(0L, num4));
					DateTime dateTime2 = startUtc.AddMilliseconds(Math.Max(0L, num5));
					ulong startedAtMs = (ulong)new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
					ulong expiresAtMs = (ulong)new DateTimeOffset(dateTime2).ToUnixTimeMilliseconds();
					this.BuffEventParsed?.Invoke(new BuffEvent(dateTime, string.IsNullOrWhiteSpace(text) ? "BuffApplied" : text, num, ownerId, num2, num3, (uint)Math.Max(1L, (long)Math.Round((dateTime2 - dateTime).TotalMilliseconds)), startedAtMs, expiresAtMs, skillLevel, baseSkillLevel));
				}
			}
		}
	}

	private static bool TryReadLogDateTime(JsonElement item, string propertyName, out DateTime value)
	{
		value = default(DateTime);
		if (!item.TryGetProperty(propertyName, out var value2))
		{
			return false;
		}
		if (value2.ValueKind == JsonValueKind.String && DateTime.TryParse(value2.GetString(), out var result))
		{
			value = result.Kind switch
			{
				DateTimeKind.Local => result.ToUniversalTime(), 
				DateTimeKind.Utc => result, 
				_ => DateTime.SpecifyKind(result, DateTimeKind.Utc), 
			};
			return true;
		}
		return false;
	}

	private static List<(int Id, string Name)> ReadCompactEntityList(JsonElement log, string propertyName)
	{
		List<(int, string)> list = new List<(int, string)>();
		if (!log.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 2)
			{
				list.Add((ReadArrayInt(item, 0), ReadArrayString(item, 1)));
			}
		}
		return list;
	}

	private static List<int> ReadCompactIntList(JsonElement log, string propertyName)
	{
		List<int> list = new List<int>();
		if (!log.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			list.Add(ReadJsonInt(item));
		}
		return list;
	}

	private static List<string> ReadCompactStringList(JsonElement log, string propertyName)
	{
		List<string> list = new List<string>();
		if (!log.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.String)
			{
				list.Add(item.GetString() ?? "");
			}
		}
		return list;
	}

	private static bool TryGetIndexedEntity(IReadOnlyList<(int Id, string Name)> items, int index, out int id, out string name)
	{
		if (index >= 0 && index < items.Count)
		{
			(id, name) = items[index];
			return id > 0;
		}
		id = 0;
		name = "";
		return false;
	}

	private static int ReadArrayInt(JsonElement array, int index)
	{
		if (index < 0 || index >= array.GetArrayLength())
		{
			return 0;
		}
		return ReadJsonInt(array[index]);
	}

	private static long ReadArrayInt64(JsonElement array, int index)
	{
		if (index < 0 || index >= array.GetArrayLength())
		{
			return 0L;
		}
		return ReadJsonInt64(array[index]);
	}

	private static string ReadArrayString(JsonElement array, int index)
	{
		if (index < 0 || index >= array.GetArrayLength() || array[index].ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return array[index].GetString() ?? "";
	}

	private static int ReadJsonInt(JsonElement item)
	{
		if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var value))
		{
			return value;
		}
		if (item.ValueKind != JsonValueKind.String || !int.TryParse(item.GetString(), out value))
		{
			return 0;
		}
		return value;
	}

	private static long ReadJsonInt64(JsonElement item)
	{
		if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var value))
		{
			return value;
		}
		if (item.ValueKind != JsonValueKind.String || !long.TryParse(item.GetString(), out value))
		{
			return 0L;
		}
		return value;
	}

	private static string BuildSpecialTextFromMask(int mask, IReadOnlyList<string> flags)
	{
		if (mask <= 0 || flags.Count == 0)
		{
			return "";
		}
		List<string> list = new List<string>();
		for (int i = 0; i < flags.Count && i < 30; i++)
		{
			if ((mask & (1 << i)) != 0 && !string.IsNullOrWhiteSpace(flags[i]))
			{
				list.Add(flags[i]);
			}
		}
		return string.Join('|', list);
	}

	private bool TryReplayDamageLogRow(IReadOnlyList<string> parts, bool typed, Dictionary<int, LoadedLogTargetInfo>? loadedTargets = null)
	{
		if (typed)
		{
			if (parts.Count < 12 || !TryParseLogTimestamp(parts[1], out var timestampUtc))
			{
				return false;
			}
			bool isDot = parts[2].Equals("True", StringComparison.OrdinalIgnoreCase);
			if (!int.TryParse(parts[3], out var result))
			{
				return false;
			}
			string actorName = parts[4];
			if (!int.TryParse(parts[5], out var result2))
			{
				return false;
			}
			string targetName = parts[6];
			if (!int.TryParse(parts[7], out var result3))
			{
				return false;
			}
			if (!int.TryParse(parts[8], out var result4))
			{
				return false;
			}
			if (!int.TryParse(parts[9], out var result5))
			{
				return false;
			}
			if (!int.TryParse(parts[10], out var result6))
			{
				return false;
			}
			if (parts.Count > 24)
			{
				TryRememberCombatPowerFromLog(actorName, parts[24]);
			}
			int result7;
			int skillLevel = ((parts.Count > 25 && int.TryParse(parts[25], out result7)) ? result7 : 0);
			int result8;
			int baseSkillLevel = ((parts.Count > 26 && int.TryParse(parts[26], out result8)) ? result8 : 0);
			ReplayDamageLogEntry(timestampUtc, isDot, result, actorName, result2, targetName, result3, result4, result5, result6, parts[11], skillLevel, baseSkillLevel);
			RememberLoadedLogTarget(loadedTargets, result2, targetName, result4);
			return true;
		}
		if (parts.Count < 9 || !TryParseLogTimestamp(parts[0], out var timestampUtc2))
		{
			return false;
		}
		bool flag = parts.Count >= 11;
		int num = (flag ? 2 : 0);
		bool isDot2 = parts[1].Equals("True", StringComparison.OrdinalIgnoreCase);
		if (!int.TryParse(parts[2], out var result9))
		{
			return false;
		}
		string actorName2 = (flag ? parts[3] : "");
		if (!int.TryParse(parts[flag ? 4 : 3], out var result10))
		{
			return false;
		}
		string targetName2 = (flag ? parts[3 + num] : "");
		if (!int.TryParse(parts[4 + num], out var result11))
		{
			return false;
		}
		if (!int.TryParse(parts[5 + num], out var result12))
		{
			return false;
		}
		if (!int.TryParse(parts[6 + num], out var result13))
		{
			return false;
		}
		if (!int.TryParse(parts[7 + num], out var result14))
		{
			return false;
		}
		ReplayDamageLogEntry(timestampUtc2, isDot2, result9, actorName2, result10, targetName2, result11, result12, result13, result14, parts[8 + num]);
		RememberLoadedLogTarget(loadedTargets, result10, targetName2, result12);
		return true;
	}

	private void ReplayDamageLogEntry(DateTime timestampUtc, bool isDot, int actor, string actorName, int target, string targetName, int skill, int damage, int multiDamage, int heal, string specialsText, int skillLevel = 0, int baseSkillLevel = 0)
	{
		if (!string.IsNullOrEmpty(actorName))
		{
			_names.Set(actor, actorName, "DamagePacket/Actor");
		}
		if (!string.IsNullOrEmpty(targetName))
		{
			_names.Set(target, targetName, "DamagePacket/Target");
		}
		List<SpecialDamage> list = new List<SpecialDamage>();
		string[] array = specialsText.Split('|', StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			if (Enum.TryParse<SpecialDamage>(text, ignoreCase: true, out var result))
			{
				list.Add(result);
			}
			else if (text.Equals("CRIT", StringComparison.OrdinalIgnoreCase))
			{
				list.Add(SpecialDamage.CRITICAL);
			}
		}
		DamageEvent e = new DamageEvent(isDot, actor, target, skill, 0, damage, 0, 0, 0, list, timestampUtc, 0, multiDamage, heal)
		{
			SkillLevel = skillLevel,
			BaseSkillLevel = baseSkillLevel
		};
		_agg.ReplayRecordedDamage(e);
	}

	private static void RememberLoadedLogTarget(Dictionary<int, LoadedLogTargetInfo>? loadedTargets, int targetId, string targetName, int damage)
	{
		if (loadedTargets != null && targetId > 0)
		{
			if (!loadedTargets.TryGetValue(targetId, out LoadedLogTargetInfo value))
			{
				value = (loadedTargets[targetId] = new LoadedLogTargetInfo
				{
					TargetId = targetId
				});
			}
			if (!string.IsNullOrWhiteSpace(targetName))
			{
				value.Name = targetName;
			}
			if (damage > 0)
			{
				value.Damage += damage;
			}
			value.Hits++;
		}
	}

	private bool TryReplayBossLogRow(IReadOnlyList<string> parts)
	{
		if (parts.Count < 24)
		{
			return false;
		}
		if (!int.TryParse(parts[20], out var result) || result <= 0)
		{
			return false;
		}
		string text = parts[21];
		int.TryParse(parts[22], out var result2);
		int.TryParse(parts[23], out var result3);
		if (!string.IsNullOrWhiteSpace(text))
		{
			_names.Set(result, text, "Log/Boss");
		}
		_agg.ConfirmBossTarget(result, string.IsNullOrWhiteSpace(text) ? $"Actor {result}" : text, result3, result2, suppressUpload: true);
		return true;
	}

	private void PromoteLoadedLogTopTargetAsBoss(Dictionary<int, LoadedLogTargetInfo> loadedTargets, string path)
	{
		if (loadedTargets.Count == 0)
		{
			return;
		}
		LoadedLogTargetInfo loadedLogTargetInfo = (from x in loadedTargets.Values
			orderby x.Damage descending, x.Hits descending
			select x).FirstOrDefault();
		if (loadedLogTargetInfo != null && loadedLogTargetInfo.TargetId > 0)
		{
			string text = ((!string.IsNullOrWhiteSpace(loadedLogTargetInfo.Name)) ? loadedLogTargetInfo.Name : Path.GetFileNameWithoutExtension(path));
			if (string.IsNullOrWhiteSpace(text))
			{
				text = $"Actor {loadedLogTargetInfo.TargetId}";
			}
			_names.Set(loadedLogTargetInfo.TargetId, text, "Log/InferredBoss");
			_agg.ConfirmBossTarget(loadedLogTargetInfo.TargetId, text, 0, 0, suppressUpload: true);
		}
	}

	private bool TryReplayBuffLogRow(IReadOnlyList<string> parts, bool typed)
	{
		int result = 0;
		int result2 = 0;
		DateTime timestampUtc;
		string text;
		int result3;
		int result4;
		int result5;
		int result6;
		uint result7;
		ulong result8;
		ulong result9;
		if (typed)
		{
			if (parts.Count < 20 || !TryParseLogTimestamp(parts[1], out timestampUtc))
			{
				return false;
			}
			text = parts[12];
			if (!int.TryParse(parts[13], out result3))
			{
				return false;
			}
			if (!int.TryParse(parts[14], out result4))
			{
				return false;
			}
			if (!int.TryParse(parts[15], out result5))
			{
				return false;
			}
			if (!int.TryParse(parts[16], out result6))
			{
				return false;
			}
			if (!uint.TryParse(parts[17], out result7))
			{
				return false;
			}
			if (!ulong.TryParse(parts[18], out result8))
			{
				return false;
			}
			if (!ulong.TryParse(parts[19], out result9))
			{
				return false;
			}
			if (parts.Count > 25)
			{
				int.TryParse(parts[25], out result);
			}
			if (parts.Count > 26)
			{
				int.TryParse(parts[26], out result2);
			}
		}
		else
		{
			if (parts.Count < 9 || !TryParseLogTimestamp(parts[0], out timestampUtc))
			{
				return false;
			}
			text = parts[1];
			if (!int.TryParse(parts[2], out result3))
			{
				return false;
			}
			if (!int.TryParse(parts[3], out result4))
			{
				return false;
			}
			if (!int.TryParse(parts[4], out result5))
			{
				return false;
			}
			if (!int.TryParse(parts[5], out result6))
			{
				return false;
			}
			if (!uint.TryParse(parts[6], out result7))
			{
				return false;
			}
			if (!ulong.TryParse(parts[7], out result8))
			{
				return false;
			}
			if (!ulong.TryParse(parts[8], out result9))
			{
				return false;
			}
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "BuffApplied";
		}
		this.BuffEventParsed?.Invoke(new BuffEvent(timestampUtc, text, result3, result4, result5, result6, result7, result8, result9, result, result2));
		return true;
	}

	private static bool IsBuffLogKind(string value)
	{
		if (!value.Equals("Buff", StringComparison.OrdinalIgnoreCase) && !value.Equals("BuffApplied", StringComparison.OrdinalIgnoreCase))
		{
			return value.Equals("BuffState", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool TryParseLogTimestamp(string value, out DateTime timestampUtc)
	{
		if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
		{
			timestampUtc = DateTime.SpecifyKind(result, DateTimeKind.Utc);
			return true;
		}
		if (DateTime.TryParse(value, out var result2))
		{
			timestampUtc = result2.Kind switch
			{
				DateTimeKind.Local => result2.ToUniversalTime(), 
				DateTimeKind.Utc => result2, 
				_ => DateTime.SpecifyKind(result2, DateTimeKind.Utc), 
			};
			return true;
		}
		timestampUtc = default(DateTime);
		return false;
	}

	private static List<string> SplitCsvLine(string line)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		for (int i = 0; i < line.Length; i++)
		{
			char c = line[i];
			switch (c)
			{
			case '"':
				if (flag && i + 1 < line.Length && line[i + 1] == '"')
				{
					stringBuilder.Append('"');
					i++;
				}
				else
				{
					flag = !flag;
				}
				continue;
			case ',':
				if (!flag)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Clear();
					continue;
				}
				break;
			}
			stringBuilder.Append(c);
		}
		list.Add(stringBuilder.ToString());
		return list;
	}

	public IReadOnlyList<TargetInfo> GetAllTargets()
	{
		return _agg.GetAllTargets();
	}

	public bool IsConfirmedBossTarget(int targetId)
	{
		return _agg.IsConfirmedBossTarget(targetId);
	}

	public bool HasOtherConfirmedBossTargetWithDamage(int excludedTargetId)
	{
		return _agg.HasOtherConfirmedBossTargetWithDamage(excludedTargetId);
	}

	public bool IsUploadSuppressedTarget(int targetId)
	{
		return _agg.IsUploadSuppressedTarget(targetId);
	}

	public CombatSnapshot? BuildSnapshotForTarget(int targetId)
	{
		CombatSnapshot combatSnapshot = _agg.BuildSnapshotForTarget(targetId);
		if (!(combatSnapshot == null))
		{
			return ApplyEncounterWindowHealing(combatSnapshot);
		}
		return null;
	}

	private CombatSnapshot ApplyEncounterWindowHealing(CombatSnapshot snapshot)
	{
		if (snapshot.TopTargetId <= 0 || snapshot.Actors.Count == 0)
		{
			return snapshot;
		}
		DateTime dateTime = ((snapshot.SessionStartUtc.Kind == DateTimeKind.Utc) ? snapshot.SessionStartUtc : snapshot.SessionStartUtc.ToUniversalTime());
		DateTime dateTime2 = ((snapshot.LastEventUtc.Kind == DateTimeKind.Utc) ? snapshot.LastEventUtc : snapshot.LastEventUtc.ToUniversalTime());
		Dictionary<int, EncounterHealingRows> dictionary = new Dictionary<int, EncounterHealingRows>();
		Dictionary<string, EncounterHealingRows> dictionary2 = new Dictionary<string, EncounterHealingRows>(StringComparer.OrdinalIgnoreCase);
		int num = 0;
		lock (_sessionCombatLogLock)
		{
			if (_sessionCombatLog.Count == 0)
			{
				return snapshot;
			}
			for (int i = 0; i < _sessionCombatLog.Count; i++)
			{
				SessionCombatLogEntry row = _sessionCombatLog[i];
				if (row.Heal > 0 && !(row.TimestampUtc < dateTime) && !(row.TimestampUtc > dateTime2))
				{
					num++;
					IndexedSessionCombatLogEntry row2 = new IndexedSessionCombatLogEntry(i, row);
					AddEncounterHealingRow(dictionary, row.ActorId, row2);
					int num2 = _names.ResolveActorId(row.ActorId);
					if (num2 > 0 && num2 != row.ActorId)
					{
						AddEncounterHealingRow(dictionary, num2, row2);
					}
					string text = BuildEncounterCharacterKey(row.ActorName ?? "");
					if (!string.IsNullOrWhiteSpace(text))
					{
						AddEncounterHealingRow(dictionary2, text, row2);
					}
				}
			}
		}
		if (num == 0)
		{
			return snapshot with
			{
				Actors = (from actor in snapshot.Actors
					where actor.TotalDamage > 0
					select actor with
					{
						TotalHealing = 0L,
						SelfHealing = 0L,
						OtherHealing = 0L,
						Hps = 0.0,
						HealHits = 0,
						Skills = StripHealingFromSkills(actor.Skills)
					}).ToList()
			};
		}
		double num3 = Math.Max(1.0, snapshot.TopTargetDuration.TotalSeconds);
		List<ActorStats> list = new List<ActorStats>(snapshot.Actors.Count);
		foreach (ActorStats actor in snapshot.Actors)
		{
			List<SessionCombatLogEntry> encounterHealingRowsForActor = GetEncounterHealingRowsForActor(actor, dictionary, dictionary2);
			IReadOnlyDictionary<int, SkillStats> readOnlyDictionary = BuildHealingSkillStats(encounterHealingRowsForActor);
			long num4 = readOnlyDictionary.Values.Sum((SkillStats skill) => skill.TotalHealing);
			long selfHealing = readOnlyDictionary.Values.Sum((SkillStats skill) => skill.SelfHealing);
			long otherHealing = readOnlyDictionary.Values.Sum((SkillStats skill) => skill.OtherHealing);
			int healHits = readOnlyDictionary.Values.Sum((SkillStats skill) => skill.HealCount);
			if (actor.TotalDamage > 0 || num4 > 0)
			{
				list.Add(actor with
				{
					TotalHealing = num4,
					SelfHealing = selfHealing,
					OtherHealing = otherHealing,
					Hps = (double)num4 / num3,
					HealHits = healHits,
					Skills = MergeEncounterWindowHealingIntoSkills(actor.Skills, readOnlyDictionary)
				});
			}
		}
		list.Sort(delegate(ActorStats a, ActorStats b)
		{
			int num5 = b.Dps.CompareTo(a.Dps);
			return (num5 == 0) ? b.Hps.CompareTo(a.Hps) : num5;
		});
		return snapshot with
		{
			Actors = list
		};
	}

	private static void AddEncounterHealingRow<TKey>(Dictionary<TKey, EncounterHealingRows> rowsByKey, TKey key, IndexedSessionCombatLogEntry row) where TKey : notnull
	{
		if (key is int)
		{
			int num = (int)((((object)key) is int) ? ((object)key) : null);
			if (num <= 0)
			{
				return;
			}
		}
		if (!rowsByKey.TryGetValue(key, out EncounterHealingRows value))
		{
			value = (rowsByKey[key] = new EncounterHealingRows());
		}
		value.Rows.Add(row);
	}

	private static string? BuildEncounterCharacterKey(string fullName)
	{
		var (name, server) = SplitEncounterActorName(fullName);
		return BuildEncounterCharacterKey(name, server);
	}

	private static string? BuildEncounterCharacterKey(string name, string server)
	{
		if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(server))
		{
			return null;
		}
		return name.Trim() + "\u001f" + server.Trim();
	}

	private static List<SessionCombatLogEntry> GetEncounterHealingRowsForActor(ActorStats actor, IReadOnlyDictionary<int, EncounterHealingRows> rowsByActorId, IReadOnlyDictionary<string, EncounterHealingRows> rowsByCharacter)
	{
		List<SessionCombatLogEntry> list = new List<SessionCombatLogEntry>();
		HashSet<int> seen = null;
		if (rowsByActorId.TryGetValue(actor.ActorId, out EncounterHealingRows value))
		{
			AddEncounterHealingRows(list, value, ref seen);
		}
		string text = BuildEncounterCharacterKey(actor.Name, actor.ServerName);
		if (!string.IsNullOrWhiteSpace(text) && rowsByCharacter.TryGetValue(text, out EncounterHealingRows value2))
		{
			AddEncounterHealingRows(list, value2, ref seen);
		}
		return list;
	}

	private static void AddEncounterHealingRows(List<SessionCombatLogEntry> target, EncounterHealingRows source, ref HashSet<int>? seen)
	{
		foreach (IndexedSessionCombatLogEntry row in source.Rows)
		{
			if (seen == null)
			{
				seen = new HashSet<int>();
			}
			if (seen.Add(row.Index))
			{
				target.Add(row.Row);
			}
		}
	}

	private static (string Name, string Server) SplitEncounterActorName(string fullName)
	{
		string text = fullName;
		string text2 = "";
		int num = fullName.IndexOf('[');
		int num2 = fullName.IndexOf(']');
		if (num > 0 && num2 > num)
		{
			text = fullName.Substring(0, num).Trim();
			text2 = fullName.Substring(num + 1, num2 - num - 1).Trim();
		}
		return (Name: text.Trim(), Server: text2.Trim());
	}

	private IReadOnlyDictionary<int, SkillStats> BuildHealingSkillStats(IReadOnlyList<SessionCombatLogEntry> rows)
	{
		if (rows.Count == 0)
		{
			return new Dictionary<int, SkillStats>();
		}
		return (from row in rows
			group row by row.SkillId).ToDictionary((IGrouping<int, SessionCombatLogEntry> group) => group.Key, delegate(IGrouping<int, SessionCombatLogEntry> group)
		{
			List<SessionCombatLogEntry> list = group.ToList();
			long num = ((IEnumerable<SessionCombatLogEntry>)list).Sum((Func<SessionCombatLogEntry, long>)((SessionCombatLogEntry row) => row.Heal));
			long num2 = list.Where(IsSelfHealingRow).Sum((Func<SessionCombatLogEntry, long>)((SessionCombatLogEntry row) => row.Heal));
			long otherHealing = num - num2;
			return new SkillStats(group.Key, 0L, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, list.Select((SessionCombatLogEntry row) => row.SkillLevel).FirstOrDefault((int level) => level > 0), list.Select((SessionCombatLogEntry row) => row.BaseSkillLevel).FirstOrDefault((int level) => level > 0), 0, num, list.Count, list.Max((SessionCombatLogEntry row) => row.Heal), list.Min((SessionCombatLogEntry row) => row.Heal), 0, num2, otherHealing);
		});
	}

	private bool IsSelfHealingRow(SessionCombatLogEntry row)
	{
		if (row.TargetId <= 0)
		{
			return false;
		}
		int num = _names.ResolveActorId(row.ActorId);
		int num2 = _names.ResolveActorId(row.TargetId);
		if (num > 0 && num == num2)
		{
			return true;
		}
		if (row.Damage <= 0)
		{
			return row.MultiDamage > 0;
		}
		return true;
	}

	private static IReadOnlyList<SkillStats> StripHealingFromSkills(IReadOnlyList<SkillStats>? skills)
	{
		if (skills == null || skills.Count == 0)
		{
			return Array.Empty<SkillStats>();
		}
		return (from skill in skills
			where skill.TotalDamage > 0 || skill.HitCount > 0
			select skill with
			{
				TotalHealing = 0L,
				HealCount = 0,
				MaxHeal = 0,
				MinHeal = 0,
				SelfHealing = 0L,
				OtherHealing = 0L
			}).ToList();
	}

	private static IReadOnlyList<SkillStats> MergeEncounterWindowHealingIntoSkills(IReadOnlyList<SkillStats>? skills, IReadOnlyDictionary<int, SkillStats> healingBySkill)
	{
		Dictionary<int, SkillStats> dictionary = StripHealingFromSkills(skills).ToDictionary((SkillStats skill) => skill.SkillCode);
		foreach (KeyValuePair<int, SkillStats> item in healingBySkill)
		{
			if (dictionary.TryGetValue(item.Key, out var value))
			{
				SkillStats value2 = item.Value;
				dictionary[item.Key] = value with
				{
					TotalHealing = value2.TotalHealing,
					SelfHealing = value2.SelfHealing,
					OtherHealing = value2.OtherHealing,
					HealCount = value2.HealCount,
					MaxHeal = value2.MaxHeal,
					MinHeal = value2.MinHeal,
					SkillLevel = ((value.SkillLevel > 0) ? value.SkillLevel : value2.SkillLevel),
					BaseSkillLevel = ((value.BaseSkillLevel > 0) ? value.BaseSkillLevel : value2.BaseSkillLevel)
				};
			}
			else
			{
				dictionary[item.Key] = item.Value;
			}
		}
		return (from skill in dictionary.Values
			orderby skill.TotalDamage descending, skill.TotalHealing descending, skill.SkillCode
			select skill).ToList();
	}

	public DateTime? GetLastBossEventTime(int targetId)
	{
		return _agg.GetLastBossEventTime(targetId);
	}

	public void BuildSnapshotNow()
	{
		_agg.BuildSnapshotParallel();
	}

	public bool TryBuildSnapshotNow()
	{
		_agg.BuildSnapshotParallel();
		return true;
	}

	public void ResetSession(bool startNewLog = true)
	{
		if (!_isLogViewing)
		{
			_agg.FlushPendingBossDefeatsOnSessionEnd();
		}
		_suppressUploadsForCurrentSession = false;
		LockedCombatPort = null;
		lock (_sessionCombatLogLock)
		{
			_sessionCombatLog.Clear();
			_sessionBuffLog.Clear();
			_activeBuffLog.Clear();
		}
		lock (_bridgeLock)
		{
			try
			{
				_bridge?.Reset();
			}
			catch
			{
			}
		}
		_agg.Reset();
	}

	private void SaveEncounterLog(int bossActorId, string bossName, DateTime firstHit, DateTime lastHit, int mobCode, int maxHp)
	{
		try
		{
			if (!SaveEncounterLogs || _isLogViewing)
			{
				return;
			}
			string text = "";
			if (mobCode > 0)
			{
				string text2 = $"Mob_{mobCode}";
				text = (string.Equals(bossName, text2, StringComparison.OrdinalIgnoreCase) ? text2 : (ResolveMobName?.Invoke(mobCode) ?? ""));
				if (string.IsNullOrWhiteSpace(text) || text.StartsWith("Mob_", StringComparison.OrdinalIgnoreCase))
				{
					text = text2;
				}
			}
			else if (string.IsNullOrWhiteSpace(text))
			{
				text = (string.IsNullOrWhiteSpace(bossName) ? $"Actor {bossActorId}" : bossName);
			}
			(string, int)? tuple = BuildEncounterLogJson(firstHit, lastHit, bossActorId, text, mobCode, maxHp);
			if (tuple.HasValue && tuple.Value.Item2 != 0)
			{
				_encounterLogStore.SaveRecord(BuildEncounterLogMeta(firstHit, lastHit, bossActorId, text, mobCode, maxHp, tuple.Value.Item2), tuple.Value.Item1);
			}
		}
		catch
		{
		}
	}

	private EncounterLogRecordMeta BuildEncounterLogMeta(DateTime firstHit, DateTime lastHit, int bossActorId, string bossName, int bossMobCode, int bossMaxHp, int eventCount)
	{
		DateTime dateTime = ((firstHit.Kind == DateTimeKind.Utc) ? firstHit : firstHit.ToUniversalTime());
		DateTime dateTime2 = ((lastHit.Kind == DateTimeKind.Utc) ? lastHit : lastHit.ToUniversalTime());
		if (dateTime2 < dateTime)
		{
			dateTime2 = dateTime;
		}
		CombatSnapshot combatSnapshot = ((bossActorId > 0) ? BuildSnapshotForTarget(bossActorId) : null);
		List<EncounterLogParticipantMeta> list = (from a in (combatSnapshot?.Actors ?? Array.Empty<ActorStats>()).Where(IsLocalEncounterParticipantActor)
			orderby a.TotalDamage descending
			select new EncounterLogParticipantMeta
			{
				ActorId = a.ActorId,
				Name = a.Name,
				ServerName = a.ServerName,
				Job = a.Job,
				Damage = a.TotalDamage,
				Dps = a.Dps,
				Hits = a.Hits,
				Healing = a.TotalHealing,
				SelfHealing = a.SelfHealing,
				OtherHealing = a.OtherHealing,
				Hps = a.Hps,
				HealHits = a.HealHits
			}).ToList();
		EncounterLogParticipantMeta encounterLogParticipantMeta = FindLocalPlayerParticipant(list);
		long totalDamage = ((list.Count > 0) ? list.Sum((EncounterLogParticipantMeta x) => x.Damage) : (combatSnapshot?.TopTargetDamage ?? 0));
		return new EncounterLogRecordMeta
		{
			Id = $"{dateTime:yyyyMMddHHmmssfff}-{bossActorId}-{bossMobCode}",
			StartUtc = dateTime,
			EndUtc = dateTime2,
			DurationMs = Math.Max(0L, (long)Math.Round((dateTime2 - dateTime).TotalMilliseconds)),
			BossActorId = bossActorId,
			BossName = bossName,
			BossMobCode = bossMobCode,
			BossMaxHp = bossMaxHp,
			ContentCode = CurrentContentCode,
			TotalDamage = totalDamage,
			ParticipantCount = list.Count,
			EventCount = eventCount,
			AppVersion = GetAppVersion(),
			LocalPlayerName = (encounterLogParticipantMeta?.Name ?? ""),
			LocalPlayerServer = (encounterLogParticipantMeta?.ServerName ?? ""),
			LocalPlayerDps = (encounterLogParticipantMeta?.Dps ?? 0.0),
			LocalPlayerDamage = (encounterLogParticipantMeta?.Damage ?? 0),
			Participants = list
		};
	}

	private EncounterLogParticipantMeta? FindLocalPlayerParticipant(IReadOnlyList<EncounterLogParticipantMeta> participants)
	{
		if (participants.Count == 0)
		{
			return null;
		}
		int? localActorId = _names.LocalPlayerActorId;
		if (localActorId.HasValue)
		{
			EncounterLogParticipantMeta encounterLogParticipantMeta = participants.FirstOrDefault((EncounterLogParticipantMeta x) => x.ActorId == localActorId.Value);
			if (encounterLogParticipantMeta != null)
			{
				return encounterLogParticipantMeta;
			}
		}
		string localPlayerName = _names.LocalPlayerName;
		if (string.IsNullOrWhiteSpace(localPlayerName))
		{
			return null;
		}
		string localBaseName = StripServerSuffix(localPlayerName);
		return participants.FirstOrDefault((EncounterLogParticipantMeta x) => string.Equals(StripServerSuffix(x.Name), localBaseName, StringComparison.OrdinalIgnoreCase));
	}

	private static string StripServerSuffix(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		int num = value.IndexOf('[');
		return ((num > 0) ? value.Substring(0, num) : value).Trim();
	}

	private static string GetAppVersion()
	{
		return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "";
	}

	public (string Csv, int LineCount)? BuildEncounterLogCsv(DateTime firstHit, DateTime lastHit, int bossActorId = 0, string bossName = "", int bossMobCode = 0, int bossMaxHp = 0)
	{
		DateTime dateTime = ((firstHit.Kind == DateTimeKind.Utc) ? firstHit : firstHit.ToUniversalTime());
		DateTime dateTime2 = ((lastHit.Kind == DateTimeKind.Utc) ? lastHit : lastHit.ToUniversalTime());
		_agg.FlushPendingPlayerEvents();
		HashSet<int> encounterParticipantActorIds = GetEncounterParticipantActorIds(bossActorId);
		DateTime buffWindowStart = dateTime.AddSeconds(-120.0);
		DateTime buffWindowEnd = dateTime2.AddSeconds(10.0);
		List<SessionCombatLogEntry> list;
		List<SessionBuffLogEntry> list2;
		lock (_sessionCombatLogLock)
		{
			list = BuildEncounterDamageSlice(dateTime, dateTime2, bossActorId, encounterParticipantActorIds);
			list2 = BuildEncounterBuffSlice(buffWindowStart, buffWindowEnd, dateTime, dateTime2);
		}
		if (list.Count == 0)
		{
			return null;
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("EventType,TimestampUtc,IsDot,ActorId,ActorName,TargetId,TargetName,SkillId,Damage,MultiDamage,Heal,Specials,BuffKind,BuffTargetId,BuffOwnerId,BuffId,BuffSkillId,DurationMs,StartedAtMs,ExpiresAtMs,BossId,BossName,BossMobCode,BossMaxHp,ActorCombatPower,SkillLevel,BaseSkillLevel");
		List<(DateTime, string[])> list3 = new List<(DateTime, string[])>(list.Count + list2.Count);
		if (bossActorId > 0)
		{
			AppendCsvRow(stringBuilder, new string[27]
			{
				"Boss",
				dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				bossActorId.ToString(),
				bossName ?? "",
				bossMobCode.ToString(),
				bossMaxHp.ToString(),
				"",
				"",
				""
			});
		}
		foreach (SessionCombatLogEntry item in list)
		{
			int combatPower;
			string text = (TryGetCombatPowerForLog(item.ActorName ?? "", out combatPower) ? combatPower.ToString() : "");
			list3.Add((item.TimestampUtc, new string[27]
			{
				"Damage",
				item.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"),
				item.IsDot.ToString(),
				item.ActorId.ToString(),
				item.ActorName ?? "",
				item.TargetId.ToString(),
				item.TargetName ?? "",
				item.SkillId.ToString(),
				item.Damage.ToString(),
				item.MultiDamage.ToString(),
				item.Heal.ToString(),
				item.Specials ?? "",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				text,
				(item.SkillLevel > 0) ? item.SkillLevel.ToString() : "",
				(item.BaseSkillLevel > 0) ? item.BaseSkillLevel.ToString() : ""
			}));
		}
		foreach (SessionBuffLogEntry item2 in list2)
		{
			list3.Add((item2.TimestampUtc, new string[27]
			{
				"Buff",
				item2.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"),
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				"",
				item2.Kind ?? "BuffApplied",
				item2.TargetId.ToString(),
				item2.OwnerId.ToString(),
				item2.BuffId.ToString(),
				item2.SkillId.ToString(),
				item2.DurationMs.ToString(),
				item2.StartedAtMs.ToString(),
				item2.ExpiresAtMs.ToString(),
				"",
				"",
				"",
				"",
				"",
				(item2.SkillLevel > 0) ? item2.SkillLevel.ToString() : "",
				(item2.BaseSkillLevel > 0) ? item2.BaseSkillLevel.ToString() : ""
			}));
		}
		foreach (var item3 in list3.OrderBy<(DateTime, string[]), DateTime>(((DateTime TimestampUtc, string[] Fields) x) => x.TimestampUtc))
		{
			AppendCsvRow(stringBuilder, item3.Item2);
		}
		return (stringBuilder.ToString(), list3.Count + ((bossActorId > 0) ? 1 : 0));
	}

	private static void AppendCsvRow(StringBuilder sb, IReadOnlyList<string> fields)
	{
		for (int i = 0; i < fields.Count; i++)
		{
			if (i > 0)
			{
				sb.Append(',');
			}
			sb.Append(EscapeCsvField(fields[i]));
		}
		sb.AppendLine();
	}

	private static string EscapeCsvField(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "";
		}
		if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
		{
			return "\"" + value.Replace("\"", "\"\"") + "\"";
		}
		return value;
	}

	public (string Json, int EventCount)? BuildEncounterLogJson(DateTime firstHit, DateTime lastHit, int bossActorId = 0, string bossName = "", int bossMobCode = 0, int bossMaxHp = 0)
	{
		DateTime dateTime = ((firstHit.Kind == DateTimeKind.Utc) ? firstHit : firstHit.ToUniversalTime());
		DateTime dateTime2 = ((lastHit.Kind == DateTimeKind.Utc) ? lastHit : lastHit.ToUniversalTime());
		_agg.FlushPendingPlayerEvents();
		HashSet<int> encounterParticipantActorIds = GetEncounterParticipantActorIds(bossActorId);
		DateTime buffWindowStart = dateTime.AddSeconds(-120.0);
		DateTime buffWindowEnd = dateTime2.AddSeconds(10.0);
		List<SessionCombatLogEntry> list;
		List<SessionBuffLogEntry> buffSlice;
		lock (_sessionCombatLogLock)
		{
			list = BuildEncounterDamageSlice(dateTime, dateTime2, bossActorId, encounterParticipantActorIds);
			buffSlice = BuildEncounterBuffSlice(buffWindowStart, buffWindowEnd, dateTime, dateTime2);
		}
		if (list.Count == 0)
		{
			return null;
		}
		Dictionary<(int, string), int> dictionary = new Dictionary<(int, string), int>();
		Dictionary<int, int> dictionary2 = new Dictionary<int, int>();
		List<object[]> list2 = new List<object[]>();
		Dictionary<(int, string), int> dictionary3 = new Dictionary<(int, string), int>();
		List<object[]> list3 = new List<object[]>();
		Dictionary<int, int> dictionary4 = new Dictionary<int, int>();
		List<int> list4 = new List<int>();
		List<object[]> list5 = new List<object[]>(list.Count);
		foreach (SessionCombatLogEntry item in list)
		{
			(int, string) key = (item.ActorId, item.ActorName ?? "");
			if (!dictionary.TryGetValue(key, out var value))
			{
				value = (dictionary[key] = list2.Count);
				list2.Add(new object[2]
				{
					item.ActorId,
					item.ActorName ?? ""
				});
			}
			dictionary2.TryAdd(item.ActorId, value);
			dictionary2.TryAdd(_names.ResolveActorId(item.ActorId), value);
			(int, string) key2 = (item.TargetId, item.TargetName ?? "");
			if (!dictionary3.TryGetValue(key2, out var value2))
			{
				value2 = (dictionary3[key2] = list3.Count);
				list3.Add(new object[2]
				{
					item.TargetId,
					item.TargetName ?? ""
				});
			}
			if (!dictionary4.TryGetValue(item.SkillId, out var value3))
			{
				value3 = list4.Count;
				dictionary4[item.SkillId] = value3;
				list4.Add(item.SkillId);
			}
			long num = (long)Math.Round((item.TimestampUtc - dateTime).TotalMilliseconds);
			if (num < 0)
			{
				num = 0L;
			}
			list5.Add(new object[11]
			{
				num,
				item.IsDot ? 1 : 0,
				value,
				value2,
				value3,
				item.Damage,
				item.MultiDamage,
				item.Heal,
				BuildEncounterSpecialMask(item.Specials),
				item.SkillLevel,
				item.BaseSkillLevel
			});
		}
		List<object[]> list6 = new List<object[]>();
		List<object[]> list7 = BuildEncounterBuffUptimeRows(buffSlice, dictionary2, dateTime, dateTime2, list6);
		List<object[]> bw = BuildEncounterBuffWindowRows(buffSlice, dateTime, dateTime2);
		return (JsonSerializer.Serialize(new
		{
			v = 4,
			fmt = "compact-json-v4",
			s = dateTime,
			e = dateTime2,
			d = (long)Math.Round((dateTime2 - dateTime).TotalMilliseconds),
			c = list.Count,
			bc = list7.Count,
			b = new object[4]
			{
				bossActorId,
				bossName ?? "",
				bossMobCode,
				bossMaxHp
			},
			sf = EncounterSpecialFlagOrder,
			a = list2,
			t = list3,
			sk = list4,
			ev = list5,
			bf = list6,
			bu = list7,
			bw = bw
		}), list.Count + list7.Count);
	}

	private HashSet<int>? GetEncounterParticipantActorIds(int bossActorId)
	{
		if (bossActorId <= 0)
		{
			return null;
		}
		CombatSnapshot combatSnapshot = BuildSnapshotForTarget(bossActorId);
		if (combatSnapshot == null)
		{
			return null;
		}
		HashSet<int> hashSet = (from a in combatSnapshot.Actors.Where(IsLocalEncounterParticipantActor)
			select a.ActorId).ToHashSet();
		if (hashSet.Count <= 0)
		{
			return null;
		}
		return hashSet;
	}

	private List<SessionCombatLogEntry> BuildEncounterDamageSlice(DateTime utcFirstHit, DateTime utcLastHit, int bossActorId, HashSet<int>? includedActorIds)
	{
		if (_sessionCombatLog.Count == 0 || utcLastHit < utcFirstHit)
		{
			return new List<SessionCombatLogEntry>(0);
		}
		int num = 0;
		int num2 = _sessionCombatLog.Count - 1;
		HashSet<int> hashSet = ((bossActorId > 0) ? new HashSet<int>() : null);
		if (bossActorId > 0)
		{
			num = -1;
			num2 = -1;
			for (int i = 0; i < _sessionCombatLog.Count; i++)
			{
				SessionCombatLogEntry row = _sessionCombatLog[i];
				if (!(row.TimestampUtc < utcFirstHit) && !(row.TimestampUtc > utcLastHit) && IsBossTargetDamageRow(row, bossActorId))
				{
					hashSet?.Add(row.ActorId);
					int num3 = _names.ResolveActorId(row.ActorId);
					if (num3 > 0)
					{
						hashSet?.Add(num3);
					}
					if (num < 0)
					{
						num = i;
					}
					num2 = i;
				}
			}
			if (num < 0)
			{
				return new List<SessionCombatLogEntry>(0);
			}
		}
		List<SessionCombatLogEntry> list = new List<SessionCombatLogEntry>(Math.Max(0, num2 - num + 1));
		for (int j = num; j <= num2; j++)
		{
			SessionCombatLogEntry row2 = _sessionCombatLog[j];
			if (!(row2.TimestampUtc < utcFirstHit) && !(row2.TimestampUtc > utcLastHit))
			{
				SessionCombatLogEntry? sessionCombatLogEntry = PrepareEncounterLogDamageRow(row2, bossActorId, includedActorIds, hashSet);
				if (sessionCombatLogEntry.HasValue)
				{
					list.Add(sessionCombatLogEntry.GetValueOrDefault());
				}
			}
		}
		return list;
	}

	private bool IsBossTargetDamageRow(SessionCombatLogEntry row, int bossActorId)
	{
		if (bossActorId <= 0 || (row.Damage <= 0 && row.MultiDamage <= 0))
		{
			return false;
		}
		int num = _names.ResolveActorId(row.TargetId);
		if (row.TargetId != bossActorId)
		{
			return num == bossActorId;
		}
		return true;
	}

	private SessionCombatLogEntry? PrepareEncounterLogDamageRow(SessionCombatLogEntry row, int bossActorId, HashSet<int>? includedActorIds, HashSet<int>? bossDamageActorIds)
	{
		bool flag = true;
		if (bossActorId > 0)
		{
			int num = _names.ResolveActorId(row.TargetId);
			flag = row.TargetId == bossActorId || num == bossActorId;
			if (!flag && (row.Heal <= 0 || !ContainsActorId(bossDamageActorIds, row.ActorId)))
			{
				return null;
			}
		}
		if (includedActorIds == null || includedActorIds.Count == 0)
		{
			if (!flag)
			{
				return null;
			}
			return row;
		}
		if (!includedActorIds.Contains(row.ActorId))
		{
			int num2 = _names.ResolveActorId(row.ActorId);
			if (num2 == row.ActorId || !includedActorIds.Contains(num2))
			{
				return null;
			}
		}
		if (bossActorId > 0 && !flag && (row.Damage > 0 || row.MultiDamage > 0))
		{
			return row with
			{
				IsDot = false,
				TargetId = row.ActorId,
				TargetName = (row.ActorName ?? ""),
				Damage = 0,
				MultiDamage = 0,
				Specials = ""
			};
		}
		return row;
	}

	private bool ContainsActorId(HashSet<int>? actorIds, int actorId)
	{
		if (actorIds == null || actorIds.Count == 0 || actorId <= 0)
		{
			return false;
		}
		if (!actorIds.Contains(actorId))
		{
			return actorIds.Contains(_names.ResolveActorId(actorId));
		}
		return true;
	}

	private List<SessionBuffLogEntry> BuildEncounterBuffSlice(DateTime buffWindowStart, DateTime buffWindowEnd, DateTime encounterStart, DateTime encounterEnd)
	{
		PruneActiveBuffLog(DateTime.UtcNow);
		List<SessionBuffLogEntry> result = new List<SessionBuffLogEntry>();
		HashSet<(DateTime TimestampUtc, string Kind, int BuffId, int SkillId, int OwnerId, int TargetId)> seen = new HashSet<(DateTime, string, int, int, int, int)>();
		foreach (SessionBuffLogEntry item4 in _sessionBuffLog)
		{
			AddBuffCandidate(item4);
		}
		foreach (SessionBuffLogEntry value in _activeBuffLog.Values)
		{
			AddBuffCandidate(value);
		}
		return result;
		void AddBuffCandidate(SessionBuffLogEntry buff)
		{
			if (IsEncounterBuffCandidate(buff, buffWindowStart, buffWindowEnd, encounterStart, encounterEnd))
			{
				int item = ((buff.OwnerId > 0) ? _names.ResolveActorId(buff.OwnerId) : 0);
				int item2 = ((buff.TargetId > 0) ? _names.ResolveActorId(buff.TargetId) : 0);
				(DateTime, string, int, int, int, int) item3 = (buff.TimestampUtc, buff.Kind, buff.BuffId, buff.SkillId, item, item2);
				if (seen.Add(item3))
				{
					result.Add(buff);
				}
			}
		}
	}

	private static bool IsEncounterBuffCandidate(SessionBuffLogEntry buff, DateTime buffWindowStart, DateTime buffWindowEnd, DateTime encounterStart, DateTime encounterEnd)
	{
		if (buff.TimestampUtc >= buffWindowStart && buff.TimestampUtc <= buffWindowEnd)
		{
			return true;
		}
		if (!BuffIntervalUtilities.HasInterval(buff.DurationMs, buff.ExpiresAtMs))
		{
			return false;
		}
		(DateTime, DateTime) interval = BuffIntervalUtilities.GetInterval(buff.TimestampUtc, buff.DurationMs, buff.StartedAtMs, buff.ExpiresAtMs);
		if (interval.Item2 > encounterStart)
		{
			return interval.Item1 < encounterEnd;
		}
		return false;
	}

	private static int BuildEncounterSpecialMask(string specials)
	{
		if (string.IsNullOrWhiteSpace(specials))
		{
			return 0;
		}
		int num = 0;
		string[] array = specials.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string a in array)
		{
			for (int j = 0; j < EncounterSpecialFlagOrder.Length; j++)
			{
				if (string.Equals(a, EncounterSpecialFlagOrder[j], StringComparison.OrdinalIgnoreCase))
				{
					num |= 1 << j;
					break;
				}
			}
		}
		return num;
	}

	private List<object[]> BuildEncounterBuffWindowRows(IReadOnlyList<SessionBuffLogEntry> buffSlice, DateTime windowStart, DateTime windowEnd)
	{
		if (buffSlice.Count == 0 || windowEnd <= windowStart)
		{
			return new List<object[]>(0);
		}
		List<object[]> list = new List<object[]>();
		foreach (SessionBuffLogEntry item in from b in buffSlice
			where BuffIntervalUtilities.HasInterval(b.DurationMs, b.ExpiresAtMs)
			group b by new
			{
				TimestampUtc = b.TimestampUtc,
				Kind = b.Kind,
				BuffId = b.BuffId,
				SkillId = b.SkillId,
				OwnerId = ((b.OwnerId > 0) ? _names.ResolveActorId(b.OwnerId) : 0),
				TargetId = ((b.TargetId > 0) ? _names.ResolveActorId(b.TargetId) : 0)
			} into g
			select g.First())
		{
			int num = ((item.TargetId > 0) ? _names.ResolveActorId(item.TargetId) : 0);
			int num2 = ((item.OwnerId > 0) ? _names.ResolveActorId(item.OwnerId) : 0);
			int num3 = ((item.BuffId > 0) ? item.BuffId : item.SkillId);
			int num4 = ((item.SkillId > 0) ? item.SkillId : num3);
			if (num <= 0 || num3 <= 0)
			{
				continue;
			}
			(DateTime, DateTime) interval = BuffIntervalUtilities.GetInterval(item.TimestampUtc, item.DurationMs, item.StartedAtMs, item.ExpiresAtMs);
			if (!(interval.Item2 <= windowStart) && !(interval.Item1 >= windowEnd))
			{
				DateTime dateTime = ((interval.Item1 < windowStart) ? windowStart : interval.Item1);
				DateTime dateTime2 = ((interval.Item2 > windowEnd) ? windowEnd : interval.Item2);
				if (!(dateTime2 <= dateTime))
				{
					list.Add(new object[9]
					{
						num,
						num2,
						num3,
						num4,
						(long)Math.Round((dateTime - windowStart).TotalMilliseconds),
						(long)Math.Round((dateTime2 - windowStart).TotalMilliseconds),
						string.IsNullOrWhiteSpace(item.Kind) ? "BuffApplied" : item.Kind,
						item.SkillLevel,
						item.BaseSkillLevel
					});
				}
			}
		}
		return (from row in list
			orderby (long)row[4], (int)row[0], (int)row[2]
			select row).ToList();
	}

	private List<object[]> BuildEncounterBuffUptimeRows(IReadOnlyList<SessionBuffLogEntry> buffSlice, IReadOnlyDictionary<int, int> actorIdToIndex, DateTime windowStart, DateTime windowEnd, List<object[]> buffs)
	{
		if (buffSlice.Count == 0 || actorIdToIndex.Count == 0 || windowEnd <= windowStart)
		{
			return new List<object[]>(0);
		}
		double num = Math.Max(1.0, (windowEnd - windowStart).TotalSeconds);
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		List<object[]> list = new List<object[]>();
		foreach (KeyValuePair<int, int> item in actorIdToIndex.OrderBy((KeyValuePair<int, int> x) => x.Value))
		{
			int actorId = item.Key;
			int value = item.Value;
			if (actorId <= 0)
			{
				continue;
			}
			foreach (IGrouping<int, SessionBuffLogEntry> item2 in from b in DeduplicateSessionBuffEvents(buffSlice.Where((SessionBuffLogEntry b) => IsEncounterBuffRelatedToActor(b, actorId)))
				group b by (b.BuffId <= 0) ? b.SkillId : b.BuffId)
			{
				int key = item2.Key;
				if (key <= 0 || !IsUploadVisibleBuff(key))
				{
					continue;
				}
				item2.Where((SessionBuffLogEntry b) => b.Kind.Equals("BuffApplied", StringComparison.OrdinalIgnoreCase)).ToList();
				List<SessionBuffLogEntry> source = item2.ToList();
				List<(DateTime, DateTime)> list2 = (from b in source
					where BuffIntervalUtilities.HasInterval(b.DurationMs, b.ExpiresAtMs)
					select BuffIntervalUtilities.GetInterval(b.TimestampUtc, b.DurationMs, b.StartedAtMs, b.ExpiresAtMs) into x
					where x.End > windowStart && x.Start < windowEnd
					select (Start: (x.Start < windowStart) ? windowStart : x.Start, End: (x.End > windowEnd) ? windowEnd : x.End) into x
					where x.End > x.Start
					orderby x.Start
					select x).ToList();
				if (list2.Count == 0)
				{
					continue;
				}
				int num2 = (int)Math.Round(BuffIntervalUtilities.SumMergedSeconds(list2) * 1000.0);
				if (num2 <= 0)
				{
					continue;
				}
				int num3 = BuffIntervalUtilities.CountMerged(list2);
				if (!dictionary.TryGetValue(key, out var value2))
				{
					value2 = (dictionary[key] = buffs.Count);
					EncounterBuffCatalog.Value.TryGetValue(key, out EncounterBuffInfo value3);
					int skillId = source.FirstOrDefault((SessionBuffLogEntry x) => x.SkillId > 0).SkillId;
					buffs.Add(new object[4]
					{
						key,
						skillId,
						value3?.Name ?? "",
						value3?.Type ?? ""
					});
				}
				list.Add(new object[5]
				{
					value,
					value2,
					num2,
					num3,
					Math.Round((double)num2 / 10.0 / num, 1)
				});
			}
		}
		return (from r in list
			orderby (int)r[0], (int)r[2] descending, (int)r[1]
			select r).ToList();
	}

	private static List<SessionBuffLogEntry> DeduplicateSessionBuffEvents(IEnumerable<SessionBuffLogEntry> buffEvents)
	{
		List<SessionBuffLogEntry> list = new List<SessionBuffLogEntry>();
		HashSet<(DateTime, string, int, int, int, int)> hashSet = new HashSet<(DateTime, string, int, int, int, int)>();
		foreach (SessionBuffLogEntry buffEvent in buffEvents)
		{
			(DateTime, string, int, int, int, int) item = (buffEvent.TimestampUtc, buffEvent.Kind, buffEvent.BuffId, buffEvent.SkillId, buffEvent.OwnerId, buffEvent.TargetId);
			if (hashSet.Add(item))
			{
				list.Add(buffEvent);
			}
		}
		return list;
	}

	private bool IsEncounterBuffRelatedToActor(SessionBuffLogEntry buff, int actorId)
	{
		if (actorId <= 0)
		{
			return false;
		}
		int num = ((buff.OwnerId > 0) ? _names.ResolveActorId(buff.OwnerId) : 0);
		int num2 = ((buff.TargetId > 0) ? _names.ResolveActorId(buff.TargetId) : 0);
		if (num != actorId)
		{
			return num2 == actorId;
		}
		return true;
	}

	private static bool IsUploadVisibleBuff(int buffId)
	{
		IReadOnlyDictionary<int, EncounterBuffInfo> value = EncounterBuffCatalog.Value;
		if (value.Count == 0)
		{
			return true;
		}
		if (value.TryGetValue(buffId, out var value2) && value2.IconView)
		{
			return value2.Type.Equals("Buff", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static IReadOnlyDictionary<int, EncounterBuffInfo> LoadEncounterBuffCatalog()
	{
		try
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < assemblies.Length; i++)
			{
				using Stream stream = OpenBuffCatalogResource(assemblies[i]);
				if (stream != null)
				{
					return ParseEncounterBuffCatalog(stream);
				}
			}
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "buffs_ko.json");
			if (File.Exists(path))
			{
				using (FileStream stream2 = File.OpenRead(path))
				{
					return ParseEncounterBuffCatalog(stream2);
				}
			}
		}
		catch
		{
		}
		return new Dictionary<int, EncounterBuffInfo>();
	}

	private static Stream? OpenBuffCatalogResource(Assembly assembly)
	{
		try
		{
			return assembly.GetManifestResourceStream("INGMeter.assets.buffs_ko.json");
		}
		catch
		{
			return null;
		}
	}

	private static IReadOnlyDictionary<int, EncounterBuffInfo> ParseEncounterBuffCatalog(Stream stream)
	{
		Dictionary<int, EncounterBuffInfo> dictionary = new Dictionary<int, EncounterBuffInfo>();
		using JsonDocument jsonDocument = JsonDocument.Parse(stream);
		if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
		{
			return dictionary;
		}
		foreach (JsonProperty item in jsonDocument.RootElement.EnumerateObject())
		{
			if (int.TryParse(item.Name, out var result) && item.Value.ValueKind == JsonValueKind.Object)
			{
				JsonElement value = item.Value;
				JsonElement value2;
				bool iconView = !value.TryGetProperty("icon_view", out value2) || value2.ValueKind != JsonValueKind.False;
				dictionary[result] = new EncounterBuffInfo(GetJsonString(value, "name"), GetJsonString(value, "type"), iconView);
			}
		}
		return dictionary;
	}

	private static string GetJsonString(JsonElement item, string name)
	{
		if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	public void Dispose()
	{
		lock (_bridgeLock)
		{
			_bridge?.Dispose();
			_bridge = null;
		}
	}

	public IReadOnlyDictionary<int, EncounterSupportMetrics> BuildEncounterSupportMetrics(CombatSnapshot snapshot)
	{
		if (snapshot.Actors.Count == 0)
		{
			return new Dictionary<int, EncounterSupportMetrics>();
		}
		RdpsPartyBuffCatalog shared = RdpsPartyBuffCatalog.Shared;
		if (shared.Effects.Count == 0)
		{
			return new Dictionary<int, EncounterSupportMetrics>();
		}
		DateTime startUtc = ((snapshot.SessionStartUtc.Kind == DateTimeKind.Utc) ? snapshot.SessionStartUtc : snapshot.SessionStartUtc.ToUniversalTime());
		DateTime endUtc = ((snapshot.LastEventUtc.Kind == DateTimeKind.Utc) ? snapshot.LastEventUtc : snapshot.LastEventUtc.ToUniversalTime());
		if (endUtc <= startUtc)
		{
			endUtc = startUtc.AddSeconds(1.0);
		}
		Dictionary<int, ActorStats> dictionary = (from actor in snapshot.Actors
			select (Actor: actor, ResolvedId: ResolveSupportActorId(actor.ActorId)) into x
			where x.ResolvedId > 0 && x.Actor.TotalDamage > 0
			group x by x.ResolvedId).ToDictionary((IGrouping<int, (ActorStats Actor, int ResolvedId)> group) => group.Key, (IGrouping<int, (ActorStats Actor, int ResolvedId)> group) => group.First().Actor);
		if (dictionary.Count == 0)
		{
			return new Dictionary<int, EncounterSupportMetrics>();
		}
		HashSet<int> participantIds = dictionary.Keys.ToHashSet();
		int bossTargetId = ResolveSupportActorId(snapshot.TopTargetId);
		Dictionary<int, JobClass> actorJobs = dictionary.ToDictionary((KeyValuePair<int, ActorStats> pair) => pair.Key, (KeyValuePair<int, ActorStats> pair) => pair.Value.Job);
		List<SessionCombatLogEntry> list;
		List<SessionBuffLogEntry> buffEvents;
		lock (_sessionCombatLogLock)
		{
			list = _sessionCombatLog.Where((SessionCombatLogEntry row) => row.Damage > 0 && row.TimestampUtc >= startUtc && row.TimestampUtc <= endUtc).Where(delegate(SessionCombatLogEntry row)
			{
				int item = ResolveSupportActorId(row.ActorId);
				if (!participantIds.Contains(item))
				{
					return false;
				}
				return bossTargetId <= 0 || ResolveSupportActorId(row.TargetId) == bossTargetId;
			}).ToList();
			buffEvents = BuildEncounterBuffSlice(startUtc.AddSeconds(-120.0), endUtc.AddSeconds(10.0), startUtc, endUtc);
		}
		Dictionary<int, SupportAccumulator> dictionary2 = dictionary.Keys.ToDictionary((int actorId) => actorId, (int _) => new SupportAccumulator());
		if (list.Count == 0)
		{
			return dictionary.Values.ToDictionary((ActorStats actor) => actor.ActorId, (ActorStats actor) => new EncounterSupportMetrics(actor.ActorId, 0.0, 0.0, actor.Dps, actor.Dps));
		}
		List<SupportDamageEvent> list2 = (from row in list
			select new SupportDamageEvent(row.TimestampUtc, ResolveSupportActorId(row.ActorId), ResolveSupportActorId(row.TargetId), row.Damage, HasSupportSpecial(row.Specials, "CRITICAL")) into row
			where row.ActorId > 0 && participantIds.Contains(row.ActorId) && row.TargetId > 0
			orderby row.TimestampUtc
			select row).ToList();
		HashSet<int> damageTargetIds = (from row in list2
			select row.TargetId into id
			where id > 0
			select id).ToHashSet();
		List<SupportBuffWindow> list3 = BuildSupportBuffWindows(shared, buffEvents, startUtc, endUtc, participantIds, damageTargetIds, actorJobs);
		if (list3.Count > 0 && list2.Count > 0)
		{
			AccumulateSupportDamage(dictionary2, list2, list3, participantIds);
		}
		double num = Math.Max(1.0, snapshot.TopTargetDuration.TotalSeconds);
		if (num <= 1.0)
		{
			num = Math.Max(1.0, (endUtc - startUtc).TotalSeconds);
		}
		Dictionary<int, EncounterSupportMetrics> dictionary3 = new Dictionary<int, EncounterSupportMetrics>();
		foreach (KeyValuePair<int, ActorStats> item2 in dictionary)
		{
			ActorStats value = item2.Value;
			SupportAccumulator value2;
			SupportAccumulator obj = (dictionary2.TryGetValue(item2.Key, out value2) ? value2 : new SupportAccumulator());
			double num2 = obj.AddedDamage / num;
			double num3 = obj.ReducedDamage / num;
			double num4 = Math.Max(0.0, value.Dps - num3);
			double rdps = Math.Max(0.0, num4 + num2);
			dictionary3[value.ActorId] = new EncounterSupportMetrics(value.ActorId, num2, num3, num4, rdps);
		}
		return dictionary3;
	}

	private void AccumulateSupportDamage(IReadOnlyDictionary<int, SupportAccumulator> accumulators, IReadOnlyList<SupportDamageEvent> damageEvents, IReadOnlyList<SupportBuffWindow> windows, IReadOnlySet<int> participantIds)
	{
		Dictionary<int, List<SupportBuffWindow>> dictionary = (from window in windows
			where window.EffectScope != RdpsEffectScope.TargetDebuff
			group window by window.TargetId).ToDictionary((IGrouping<int, SupportBuffWindow> group) => group.Key, (IGrouping<int, SupportBuffWindow> group) => group.ToList());
		Dictionary<int, List<SupportBuffWindow>> dictionary2 = (from window in windows
			where window.EffectScope == RdpsEffectScope.TargetDebuff
			group window by window.TargetId).ToDictionary((IGrouping<int, SupportBuffWindow> group) => group.Key, (IGrouping<int, SupportBuffWindow> group) => group.ToList());
		foreach (SupportDamageEvent damageEvent in damageEvents)
		{
			if (!participantIds.Contains(damageEvent.ActorId))
			{
				continue;
			}
			List<SupportBuffWindow> list = null;
			if (dictionary.TryGetValue(damageEvent.ActorId, out var value))
			{
				foreach (SupportBuffWindow item in value)
				{
					if (item.Start <= damageEvent.TimestampUtc && item.End >= damageEvent.TimestampUtc)
					{
						(list ?? (list = new List<SupportBuffWindow>())).Add(item);
					}
				}
			}
			if (dictionary2.TryGetValue(damageEvent.TargetId, out var value2))
			{
				foreach (SupportBuffWindow item2 in value2)
				{
					if (item2.Start <= damageEvent.TimestampUtc && item2.End >= damageEvent.TimestampUtc && (item2.SourceRestriction != RdpsSourceRestriction.OwnerOnly || item2.OwnerId == damageEvent.ActorId))
					{
						(list ?? (list = new List<SupportBuffWindow>())).Add(item2);
					}
				}
			}
			if (list == null || list.Count == 0)
			{
				continue;
			}
			IReadOnlyList<SupportBuffWindow> readOnlyList = RdpsSupportRules.FilterWindowsForDamageEvent(list, damageEvent.IsCrit);
			if (readOnlyList.Count == 0)
			{
				continue;
			}
			IReadOnlyList<RdpsSupportGroup<SupportBuffWindow>> readOnlyList2 = RdpsSupportRules.SelectEffectiveGroups(readOnlyList);
			if (readOnlyList2.Count == 0)
			{
				continue;
			}
			double num = readOnlyList2.Aggregate(1.0, (double num6, RdpsSupportGroup<SupportBuffWindow> group) => num6 * group.Multiplier);
			if (num <= 1.0)
			{
				continue;
			}
			double num2 = (double)damageEvent.Damage - (double)damageEvent.Damage / num;
			if (num2 <= 0.0)
			{
				continue;
			}
			double num3 = readOnlyList2.Sum((RdpsSupportGroup<SupportBuffWindow> group) => Math.Log(group.Multiplier));
			foreach (RdpsSupportGroup<SupportBuffWindow> item3 in readOnlyList2)
			{
				double num4 = ((num3 > 0.0) ? (Math.Log(item3.Multiplier) / num3) : (1.0 / (double)readOnlyList2.Count));
				foreach (RdpsSupportSourceShare<SupportBuffWindow> source in item3.Sources)
				{
					int ownerId = source.Window.OwnerId;
					if (ownerId > 0 && ownerId != damageEvent.ActorId)
					{
						double num5 = num2 * num4 * source.Share;
						if (accumulators.TryGetValue(ownerId, out SupportAccumulator value3))
						{
							value3.AddedDamage += num5;
						}
						if (accumulators.TryGetValue(damageEvent.ActorId, out SupportAccumulator value4))
						{
							value4.ReducedDamage += num5;
						}
					}
				}
			}
		}
	}

	private List<SupportBuffWindow> BuildSupportBuffWindows(RdpsPartyBuffCatalog catalog, IReadOnlyList<SessionBuffLogEntry> buffEvents, DateTime windowStart, DateTime windowEnd, HashSet<int> participantIds, HashSet<int> damageTargetIds, IReadOnlyDictionary<int, JobClass> actorJobs)
	{
		List<SupportBuffWindow> list = new List<SupportBuffWindow>();
		foreach (SessionBuffLogEntry buffEvent in buffEvents)
		{
			if (!IsSupportBuffWindowEvent(buffEvent) || !BuffIntervalUtilities.HasInterval(buffEvent.DurationMs, buffEvent.ExpiresAtMs) || !TryResolveSupportBuffEffect(catalog, buffEvent, out RdpsPartyBuffEffect effect) || effect == null)
			{
				continue;
			}
			int num = ResolveSupportActorId(buffEvent.OwnerId);
			int num2 = ResolveSupportActorId(buffEvent.TargetId);
			(DateTime, DateTime) interval = BuffIntervalUtilities.GetInterval(buffEvent.TimestampUtc, buffEvent.DurationMs, buffEvent.StartedAtMs, buffEvent.ExpiresAtMs);
			if (interval.Item2 <= windowStart || interval.Item1 >= windowEnd)
			{
				continue;
			}
			DateTime dateTime = ((interval.Item1 < windowStart) ? windowStart : interval.Item1);
			DateTime dateTime2 = ((interval.Item2 > windowEnd) ? windowEnd : interval.Item2);
			if (dateTime2 <= dateTime)
			{
				continue;
			}
			if (effect.EffectScope == RdpsEffectScope.TargetDebuff)
			{
				if (num > 0 && num2 > 0 && num != num2 && participantIds.Contains(num) && damageTargetIds.Contains(num2) && RdpsSupportRules.IsEffectOwnerJob(num, effect, actorJobs))
				{
					list.Add(CreateSupportBuffWindow(catalog, effect, buffEvent, num, num2, dateTime, dateTime2));
				}
			}
			else
			{
				if ((num <= 0 && num2 <= 0) || (num > 0 && !participantIds.Contains(num)) || (num2 > 0 && !participantIds.Contains(num2)))
				{
					continue;
				}
				if (num > 0 && num2 > 0 && num != num2 && participantIds.Contains(num) && participantIds.Contains(num2))
				{
					list.Add(CreateSupportBuffWindow(catalog, effect, buffEvent, num, num2, dateTime, dateTime2));
				}
				int providerId = RdpsSupportRules.ResolvePartyBuffProviderId(effect, num, num2, participantIds, actorJobs);
				if (providerId <= 0)
				{
					continue;
				}
				foreach (int item in participantIds.Where((int id) => id > 0 && id != providerId))
				{
					list.Add(CreateSupportBuffWindow(catalog, effect, buffEvent, providerId, item, dateTime, dateTime2));
				}
			}
		}
		return list;
	}

	private SupportBuffWindow CreateSupportBuffWindow(RdpsPartyBuffCatalog catalog, RdpsPartyBuffEffect effect, SessionBuffLogEntry buff, int ownerId, int targetId, DateTime start, DateTime end)
	{
		effect = ResolveSupportEffectForProvider(catalog, effect, ownerId, buff.BuffId);
		return new SupportBuffWindow(effect.SkillId, effect.LevelCode, effect.SkillName, effect.PveDamageAmpPercent, effect.Multiplier, effect.ExclusiveGroup, effect.EffectScope, effect.SourceRestriction, effect.EffectKind, ownerId, targetId, start, end);
	}

	private RdpsPartyBuffEffect ResolveSupportEffectForProvider(RdpsPartyBuffCatalog catalog, RdpsPartyBuffEffect effect, int providerId, int buffId)
	{
		if (TryGetStigmaSkillLevelForBuff(providerId, effect.SkillId, buffId, out var skillLevel, out var _) && catalog.TryGetEffectForSkillLevel(effect.SkillId, skillLevel, out RdpsPartyBuffEffect effect2) && effect2 != null)
		{
			return effect2;
		}
		return effect;
	}

	private static bool TryResolveSupportBuffEffect(RdpsPartyBuffCatalog catalog, SessionBuffLogEntry buff, out RdpsPartyBuffEffect? effect)
	{
		if (catalog.TryGetEffectForBuffCode(buff.SkillId, out effect))
		{
			if (buff.SkillLevel > 0 && effect != null && catalog.TryGetEffectForSkillLevel(effect.SkillId, buff.SkillLevel, out RdpsPartyBuffEffect effect2) && effect2 != null)
			{
				effect = effect2;
			}
			return true;
		}
		if (catalog.TryGetEffectForBuffCode(buff.BuffId, out effect))
		{
			if (buff.SkillLevel > 0 && effect != null && catalog.TryGetEffectForSkillLevel(effect.SkillId, buff.SkillLevel, out RdpsPartyBuffEffect effect3) && effect3 != null)
			{
				effect = effect3;
			}
			return true;
		}
		effect = null;
		return false;
	}

	private int ResolveSupportActorId(int actorId)
	{
		if (actorId <= 0)
		{
			return 0;
		}
		return _names.ResolveActorId(actorId);
	}

	private static bool IsSupportBuffWindowEvent(SessionBuffLogEntry buff)
	{
		if (!buff.Kind.Equals("BuffApplied", StringComparison.OrdinalIgnoreCase) && !buff.Kind.Equals("BuffState", StringComparison.OrdinalIgnoreCase))
		{
			return buff.Kind.Equals("Buff", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool HasSupportSpecial(string specials, string special)
	{
		if (string.IsNullOrWhiteSpace(specials))
		{
			return false;
		}
		return specials.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any((string token) => token.Equals(special, StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsKnownStigmaSkillCode(int skillCode)
	{
		return RdpsSkillCatalog.Shared.IsStigmaSkillCode(skillCode, RdpsPartyBuffCatalog.Shared);
	}
}
