using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace INGMeter.Core;

public sealed class NameCache
{
	private readonly record struct SummonOwnerLink(int OwnerId, int ActorSessionId);

	private readonly ConcurrentDictionary<int, string> _map = new ConcurrentDictionary<int, string>();

	private readonly ConcurrentDictionary<int, string> _sourceByActor = new ConcurrentDictionary<int, string>();

	private readonly ConcurrentDictionary<int, DateTime> _actorSeenAtUtc = new ConcurrentDictionary<int, DateTime>();

	private readonly ConcurrentDictionary<int, int> _actorSessionIds = new ConcurrentDictionary<int, int>();

	private readonly ConcurrentDictionary<int, SummonOwnerLink> _summonToOwner = new ConcurrentDictionary<int, SummonOwnerLink>();

	private readonly ConcurrentDictionary<int, byte> _knownSummonIds = new ConcurrentDictionary<int, byte>();

	private readonly ConcurrentDictionary<string, string> _serverByName = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly ConcurrentDictionary<string, DateTime> _serverSeenAtUtcByName = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _monsterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly ConcurrentDictionary<int, byte> _isMonsterId = new ConcurrentDictionary<int, byte>();

	private int _currentMapId;

	private string? _localPlayerName;

	private int? _localPlayerActorId;

	private readonly ConcurrentDictionary<int, int> _summonMobCode = new ConcurrentDictionary<int, int>();

	public int CurrentMapId
	{
		get
		{
			return _currentMapId;
		}
		set
		{
			_currentMapId = value;
		}
	}

	public int Count => _map.Count;

	public string? LocalPlayerName => _localPlayerName;

	public int? LocalPlayerActorId
	{
		get
		{
			return _localPlayerActorId;
		}
		set
		{
			_localPlayerActorId = value;
		}
	}

	public bool SuppressLocalPlayerAutoLink { get; set; }

	public event Action<int, string, string, byte[]?>? NameMapped;

	public event Action<int, int>? SummonMapped;

	public event Action? LocalPlayerSpawned;

	public bool SetLocalPlayer(string name, int? actorId = null, byte[]? rawPacket = null)
	{
		bool result = (_localPlayerName != null && !string.Equals(_localPlayerName, name, StringComparison.OrdinalIgnoreCase)) || (actorId.HasValue && LocalPlayerActorId.HasValue && LocalPlayerActorId.Value != actorId.Value);
		_localPlayerName = name;
		if (actorId.HasValue && actorId.Value >= 1)
		{
			BeginEntitySession(actorId.Value);
			TouchActor(actorId.Value);
			LocalPlayerActorId = actorId.Value;
			SetActorSource(actorId.Value, "LocalPlayer");
			if (!_map.TryGetValue(actorId.Value, out string value) || value != name)
			{
				_map[actorId.Value] = name;
				this.NameMapped?.Invoke(actorId.Value, name, "LocalPlayer", rawPacket);
			}
		}
		else if (!SuppressLocalPlayerAutoLink)
		{
			KeyValuePair<int, string> keyValuePair = _map.FirstOrDefault<KeyValuePair<int, string>>((KeyValuePair<int, string> x) => HasTrustedLocalPlayerSource(x.Key) && (string.Equals(x.Value, name, StringComparison.OrdinalIgnoreCase) || x.Value.StartsWith(name + "[", StringComparison.OrdinalIgnoreCase)));
			if (keyValuePair.Key != 0)
			{
				Console.WriteLine($"[NameCache] SetLocalPlayer Match FOUND! MapKey={keyValuePair.Key}, InputName={name}, MapValue={keyValuePair.Value}");
				LocalPlayerActorId = keyValuePair.Key;
			}
			else
			{
				Console.WriteLine("[NameCache] SetLocalPlayer Match FAILED! InputName='" + name + "'. Dump:");
				foreach (KeyValuePair<int, string> item in _map.Take(10))
				{
					Console.WriteLine($"  -> {_map.Count} items. Example: {item.Key} = '{item.Value}'");
				}
			}
		}
		return result;
	}

	public void Set(int actorId, string name, string source = "Observed", byte[]? rawPacket = null)
	{
		if (actorId < 1)
		{
			return;
		}
		bool flag = source.Equals("DLL/MobSpawn", StringComparison.OrdinalIgnoreCase);
		bool flag2 = flag && IsKnownSummon(actorId);
		if (IsEntitySessionSource(source) && !flag2)
		{
			BeginEntitySession(actorId);
		}
		TouchActor(actorId);
		string text = name;
		string text2 = null;
		int num = name.IndexOf('[');
		int num2 = name.IndexOf(']');
		if (num > 0 && num2 > num)
		{
			text = name.Substring(0, num).Trim();
			text2 = name.Substring(num + 1, num2 - num - 1).Trim();
			RememberServerName(text, text2);
		}
		if (!flag && _map.TryGetValue(actorId, out string value))
		{
			bool flag3 = value.Contains('[') && value.Contains(']');
			bool flag4 = text2 != null;
			bool flag5 = IsFallbackName(value);
			bool flag6 = IsFallbackName(name);
			if ((flag3 && !flag4) || ((!flag5 || flag6) && !flag3 && !flag4 && value.Length > text.Length))
			{
				return;
			}
		}
		if (!flag && text2 == null && _serverByName.TryGetValue(text, out string value2))
		{
			name = text + "[" + value2 + "]";
		}
		if (!_map.TryGetValue(actorId, out string value3) || value3 != name)
		{
			_map[actorId] = name;
			this.NameMapped?.Invoke(actorId, name, source, rawPacket);
		}
		SetActorSource(actorId, source);
		if (!SuppressLocalPlayerAutoLink && _localPlayerName != null && IsLocalPlayerNameMatch(text, name, _localPlayerName))
		{
			Console.WriteLine($"[NameCache] Set Match FOUND! LocalName='{_localPlayerName}', Base='{text}', Full='{name}'");
			bool flag7 = IsTrustedLocalPlayerSource(source);
			if (flag7)
			{
				LocalPlayerActorId = actorId;
			}
			if (CanAutoLinkLocalActor(actorId) && flag7)
			{
				this.LocalPlayerSpawned?.Invoke();
			}
		}
	}

	private bool CanAutoLinkLocalActor(int actorId)
	{
		if (LocalPlayerActorId.HasValue)
		{
			return LocalPlayerActorId.Value == actorId;
		}
		return true;
	}

	private void TouchActor(int actorId)
	{
		if (actorId > 0)
		{
			_actorSeenAtUtc[actorId] = DateTime.UtcNow;
		}
	}

	private int GetActorSessionId(int actorId)
	{
		if (!_actorSessionIds.TryGetValue(actorId, out var value))
		{
			return 0;
		}
		return value;
	}

	private void BeginEntitySession(int actorId)
	{
		if (actorId >= 1)
		{
			_actorSessionIds.AddOrUpdate(actorId, 1, (int _, int sessionId) => (sessionId == int.MaxValue) ? 1 : (sessionId + 1));
			ClearNonPlayerActorState(actorId);
		}
	}

	private void ClearNonPlayerActorState(int actorId)
	{
		_summonToOwner.TryRemove(actorId, out var _);
		_knownSummonIds.TryRemove(actorId, out var value2);
		_summonMobCode.TryRemove(actorId, out var _);
		_isMonsterId.TryRemove(actorId, out value2);
	}

	private void RememberServerName(string baseName, string serverName)
	{
		_serverByName[baseName] = serverName;
		_serverSeenAtUtcByName[baseName] = DateTime.UtcNow;
	}

	private void RemoveActorSessionState(int actorId)
	{
		_map.TryRemove(actorId, out string value);
		_sourceByActor.TryRemove(actorId, out value);
		_actorSeenAtUtc.TryRemove(actorId, out var _);
		_actorSessionIds.TryRemove(actorId, out var value3);
		_summonToOwner.TryRemove(actorId, out var _);
		_isMonsterId.TryRemove(actorId, out var value5);
		_knownSummonIds.TryRemove(actorId, out value5);
		_summonMobCode.TryRemove(actorId, out value3);
	}

	public void RemoveEntitySessionState(int actorId)
	{
		if (actorId < 1)
		{
			return;
		}
		if (LocalPlayerActorId == actorId)
		{
			ClearNonPlayerActorState(actorId);
		}
		else
		{
			RemoveActorSessionState(actorId);
		}
		foreach (KeyValuePair<int, SummonOwnerLink> item in _summonToOwner)
		{
			if (item.Value.OwnerId == actorId)
			{
				RemoveActorSessionState(item.Key);
			}
		}
	}

	public void PruneForLocalPlayerChange(DateTime preserveSinceUtc)
	{
		HashSet<int> hashSet = new HashSet<int>();
		foreach (int key in _map.Keys)
		{
			if (_actorSeenAtUtc.TryGetValue(key, out var value) && value >= preserveSinceUtc)
			{
				hashSet.Add(key);
			}
			else
			{
				RemoveActorSessionState(key);
			}
		}
		foreach (KeyValuePair<int, SummonOwnerLink> item in _summonToOwner)
		{
			SummonOwnerLink value2;
			if (hashSet.Contains(item.Key))
			{
				value2 = item.Value;
				if (hashSet.Contains(value2.OwnerId))
				{
					continue;
				}
			}
			_summonToOwner.TryRemove(item.Key, out value2);
		}
		string value3;
		foreach (int key2 in _sourceByActor.Keys)
		{
			if (!hashSet.Contains(key2))
			{
				_sourceByActor.TryRemove(key2, out value3);
			}
		}
		DateTime value4;
		foreach (int key3 in _actorSeenAtUtc.Keys)
		{
			if (!hashSet.Contains(key3))
			{
				_actorSeenAtUtc.TryRemove(key3, out value4);
			}
		}
		int value5;
		foreach (int key4 in _actorSessionIds.Keys)
		{
			if (!hashSet.Contains(key4))
			{
				_actorSessionIds.TryRemove(key4, out value5);
			}
		}
		byte value6;
		foreach (int key5 in _isMonsterId.Keys)
		{
			if (!hashSet.Contains(key5))
			{
				_isMonsterId.TryRemove(key5, out value6);
			}
		}
		foreach (int key6 in _knownSummonIds.Keys)
		{
			if (!hashSet.Contains(key6))
			{
				_knownSummonIds.TryRemove(key6, out value6);
			}
		}
		foreach (int key7 in _summonMobCode.Keys)
		{
			if (!hashSet.Contains(key7))
			{
				_summonMobCode.TryRemove(key7, out value5);
			}
		}
		foreach (KeyValuePair<string, DateTime> item2 in _serverSeenAtUtcByName)
		{
			if (item2.Value < preserveSinceUtc)
			{
				_serverSeenAtUtcByName.TryRemove(item2.Key, out value4);
				_serverByName.TryRemove(item2.Key, out value3);
			}
		}
		_localPlayerName = null;
		_localPlayerActorId = null;
		_currentMapId = 0;
	}

	public bool TryGet(int actorId, out string? name)
	{
		return _map.TryGetValue(actorId, out name);
	}

	public string GetOrFallback(int actorId)
	{
		if (!_map.TryGetValue(actorId, out string value))
		{
			return actorId.ToString();
		}
		return value;
	}

	private static bool IsLocalPlayerNameMatch(string baseName, string fullName, string localName)
	{
		string text = localName.Trim();
		int num = text.IndexOf('[');
		if (num > 0)
		{
			text = text.Substring(0, num).Trim();
		}
		string text2 = fullName.Trim();
		if (string.IsNullOrWhiteSpace(baseName) || string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		if (string.Equals(baseName, text, StringComparison.OrdinalIgnoreCase) || string.Equals(text2, text, StringComparison.OrdinalIgnoreCase) || text2.StartsWith(text + "[", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return false;
	}

	public IReadOnlyList<string> GetExtractedNames(int max = 500)
	{
		List<string> list = new List<string>();
		foreach (KeyValuePair<int, string> item in _map)
		{
			if (list.Count >= max)
			{
				break;
			}
			list.Add(item.Value);
		}
		return list;
	}

	public void SetSummonOwner(int summonId, int ownerId, byte[]? rawPacket = null)
	{
		if (summonId >= 1 && summonId <= 99999 && ownerId >= 1 && ownerId <= 99999 && LocalPlayerActorId != summonId)
		{
			TouchActor(summonId);
			TouchActor(ownerId);
			int actorSessionId = GetActorSessionId(summonId);
			if (!_summonToOwner.TryGetValue(summonId, out var value) || value.OwnerId != ownerId || value.ActorSessionId != actorSessionId)
			{
				_summonToOwner[summonId] = new SummonOwnerLink(ownerId, actorSessionId);
				_knownSummonIds[summonId] = 1;
				this.SummonMapped?.Invoke(summonId, ownerId);
				string orFallback = GetOrFallback(ownerId);
				string arg = $"(소환수:{summonId}) → {orFallback}({ownerId})";
				this.NameMapped?.Invoke(summonId, arg, "SummonLink", rawPacket);
			}
		}
	}

	public bool IsKnownSummon(int actorId)
	{
		int ownerId;
		if (_knownSummonIds.ContainsKey(actorId))
		{
			return TryResolveSummonOwner(actorId, out ownerId);
		}
		return false;
	}

	public bool IsSummon(int actorId)
	{
		return ResolveActorId(actorId) != actorId;
	}

	public int ResolveActorId(int actorId)
	{
		if (LocalPlayerActorId != actorId)
		{
			if (!TryResolveSummonOwner(actorId, out var ownerId))
			{
				return actorId;
			}
			return ownerId;
		}
		return actorId;
	}

	private bool TryResolveSummonOwner(int summonId, out int ownerId)
	{
		ownerId = 0;
		if (!_summonToOwner.TryGetValue(summonId, out var value))
		{
			return false;
		}
		if (value.ActorSessionId != GetActorSessionId(summonId))
		{
			return false;
		}
		ownerId = value.OwnerId;
		return true;
	}

	public void SetSummonMobCode(int summonId, int mobCode)
	{
		if (LocalPlayerActorId != summonId)
		{
			TouchActor(summonId);
			_summonMobCode[summonId] = mobCode;
		}
	}

	public void ClearSummonMobCode(int summonId)
	{
		_summonMobCode.TryRemove(summonId, out var _);
	}

	public bool TryGetSummonMobCode(int summonId, out int code)
	{
		return _summonMobCode.TryGetValue(summonId, out code);
	}

	public void TryRelinkLocalPlayerActorId()
	{
		if (!SuppressLocalPlayerAutoLink && !LocalPlayerActorId.HasValue && _localPlayerName != null)
		{
			KeyValuePair<int, string> keyValuePair = _map.FirstOrDefault<KeyValuePair<int, string>>((KeyValuePair<int, string> x) => HasTrustedLocalPlayerSource(x.Key) && (string.Equals(x.Value, _localPlayerName, StringComparison.OrdinalIgnoreCase) || x.Value.StartsWith(_localPlayerName + "[", StringComparison.OrdinalIgnoreCase) || x.Value.Equals(_localPlayerName + " ", StringComparison.OrdinalIgnoreCase)));
			if (keyValuePair.Key != 0)
			{
				Console.WriteLine($"[NameCache] Relink FOUND! MapKey={keyValuePair.Key}, InputName={_localPlayerName}, MapValue={keyValuePair.Value}");
				LocalPlayerActorId = keyValuePair.Key;
			}
		}
	}

	public void RegisterMonster(int actorId, string name)
	{
		if (actorId > 0 && LocalPlayerActorId == actorId)
		{
			ClearNonPlayerActorState(actorId);
			return;
		}
		if (actorId > 0)
		{
			TouchActor(actorId);
			_isMonsterId[actorId] = 1;
		}
		if (!string.IsNullOrWhiteSpace(name))
		{
			lock (_monsterNames)
			{
				_monsterNames.Add(name);
			}
		}
	}

	public void UnregisterMonster(int actorId)
	{
		if (actorId > 0)
		{
			_isMonsterId.TryRemove(actorId, out var _);
		}
	}

	public bool IsMonster(int actorId, string name)
	{
		if (_isMonsterId.ContainsKey(actorId))
		{
			return true;
		}
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		lock (_monsterNames)
		{
			if (_monsterNames.Contains(name))
			{
				return true;
			}
		}
		if (IsMonsterFallbackName(name))
		{
			return true;
		}
		return false;
	}

	private static bool IsMonsterFallbackName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		if (!name.StartsWith("Mob_", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Mob ", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Boss ", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("NPC_", StringComparison.OrdinalIgnoreCase))
		{
			return name.StartsWith("NPC ", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsFallbackName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return true;
		}
		if (int.TryParse(name, out var _))
		{
			return true;
		}
		if (!name.StartsWith("Actor ", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Mob_", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Boss ", StringComparison.OrdinalIgnoreCase))
		{
			return name.StartsWith("Mob ", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private bool HasTrustedLocalPlayerSource(int actorId)
	{
		if (_sourceByActor.TryGetValue(actorId, out string value))
		{
			return IsTrustedLocalPlayerSource(value);
		}
		return false;
	}

	private void SetActorSource(int actorId, string source)
	{
		if (IsTrustedLocalPlayerSource(source) || !_sourceByActor.ContainsKey(actorId))
		{
			_sourceByActor[actorId] = source;
		}
	}

	private static bool IsTrustedLocalPlayerSource(string? source)
	{
		if (string.IsNullOrWhiteSpace(source))
		{
			return false;
		}
		if (!source.StartsWith("Spawn", StringComparison.OrdinalIgnoreCase) && !source.Equals("DLL/LocalUserInfo", StringComparison.OrdinalIgnoreCase))
		{
			return source.StartsWith("LocalPlayer", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsEntitySessionSource(string? source)
	{
		if (string.IsNullOrWhiteSpace(source))
		{
			return false;
		}
		if (!source.Equals("DLL/MobSpawn", StringComparison.OrdinalIgnoreCase) && !source.Equals("DLL/UserInfo", StringComparison.OrdinalIgnoreCase) && !source.Equals("DLL/LocalUserInfo", StringComparison.OrdinalIgnoreCase))
		{
			return source.StartsWith("Spawn", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
