using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace INGMeter.Core;

public sealed class CombatAggregator
{
	private sealed class ActorState
	{
		public JobClass Job;

		public long TotalDamage;

		public long TotalHealing;

		public long SelfHealing;

		public long OtherHealing;

		public int HitEvents;

		public int HealEvents;

		public int CritEvents;

		public int MultiEvents;

		public DateTime First;

		public DateTime Last;

		public bool IsMonster;

		public readonly Dictionary<int, (long Dmg, DateTime First, DateTime Last)> PerTarget = new Dictionary<int, (long, DateTime, DateTime)>();

		public readonly Dictionary<int, Dictionary<int, SkillAgg>> PerTargetSkills = new Dictionary<int, Dictionary<int, SkillAgg>>();
	}

	private sealed class SkillAgg
	{
		public long TotalDamage;

		public long TotalHealing;

		public long SelfHealing;

		public long OtherHealing;

		public int HitCount;

		public int HealCount;

		public int CritCount;

		public int NormalHitCount;

		public int BackCount;

		public int DoubleCount;

		public int PerfectCount;

		public int ParryCount;

		public int EvadeCount;

		public int SmiteCount;

		public int MultiEventCount;

		public int MaxDamage;

		public int MinDamage = int.MaxValue;

		public int MaxHeal;

		public int MinHeal = int.MaxValue;

		public int SkillLevel;

		public int BaseSkillLevel;

		public readonly DamageStatCounter StatCounter = new DamageStatCounter();
	}

	private sealed class SkillStatsBuilder
	{
		public long TotalDamage;

		public long TotalHealing;

		public long SelfHealing;

		public long OtherHealing;

		public int HitCount;

		public int HealCount;

		public int CritCount;

		public int NormalHitCount;

		public int BackCount;

		public int DoubleCount;

		public int PerfectCount;

		public int ParryCount;

		public int EvadeCount;

		public int SmiteCount;

		public int MultiEventCount;

		public int MaxDamage;

		public int MinDamage = int.MaxValue;

		public int MaxHeal;

		public int MinHeal = int.MaxValue;

		public int SkillLevel;

		public int BaseSkillLevel;

		public int SkillCode { get; }

		public SkillStatsBuilder(int skillCode)
		{
			SkillCode = skillCode;
		}

		public void AddDamageFrom(SkillAgg agg)
		{
			TotalDamage += agg.TotalDamage;
			HitCount += agg.HitCount;
			CritCount += agg.CritCount;
			NormalHitCount += agg.NormalHitCount;
			BackCount += agg.BackCount;
			DoubleCount += agg.DoubleCount;
			PerfectCount += agg.PerfectCount;
			ParryCount += agg.ParryCount;
			EvadeCount += agg.EvadeCount;
			SmiteCount += agg.SmiteCount;
			MultiEventCount += agg.MultiEventCount;
			if (agg.MaxDamage > MaxDamage)
			{
				MaxDamage = agg.MaxDamage;
			}
			if (agg.MinDamage > 0 && agg.MinDamage < MinDamage)
			{
				MinDamage = agg.MinDamage;
			}
			ApplyLevels(agg);
		}

		public void AddHealingFrom(SkillAgg agg)
		{
			TotalHealing += agg.TotalHealing;
			SelfHealing += agg.SelfHealing;
			OtherHealing += agg.OtherHealing;
			HealCount += agg.HealCount;
			if (agg.MaxHeal > MaxHeal)
			{
				MaxHeal = agg.MaxHeal;
			}
			if (agg.MinHeal > 0 && agg.MinHeal < MinHeal)
			{
				MinHeal = agg.MinHeal;
			}
			ApplyLevels(agg);
		}

		public SkillStats ToSkillStats()
		{
			return new SkillStats(SkillCode, TotalDamage, HitCount, CritCount, NormalHitCount, BackCount, DoubleCount, PerfectCount, ParryCount, MultiEventCount, MaxDamage, (MinDamage != int.MaxValue) ? MinDamage : 0, SkillLevel, BaseSkillLevel, EvadeCount, TotalHealing, HealCount, MaxHeal, (MinHeal != int.MaxValue) ? MinHeal : 0, SmiteCount, SelfHealing, OtherHealing);
		}

		private void ApplyLevels(SkillAgg agg)
		{
			if (SkillLevel <= 0 && agg.SkillLevel > 0)
			{
				SkillLevel = agg.SkillLevel;
			}
			if (BaseSkillLevel <= 0 && agg.BaseSkillLevel > 0)
			{
				BaseSkillLevel = agg.BaseSkillLevel;
			}
		}
	}

	private sealed class EntityHpState
	{
		public int CurrentHp = -1;

		public int MaxObservedHp;

		public DateTime LastUpdateUtc;
	}

	private sealed class PendingEffectiveHeal
	{
		public DamageEvent SourceEvent { get; }

		public int RawAmount { get; }

		public int AppliedAmount { get; set; }

		public DateTime CreatedUtc { get; }

		public PendingEffectiveHeal(DamageEvent sourceEvent, int rawAmount, DateTime createdUtc)
		{
			SourceEvent = sourceEvent;
			RawAmount = rawAmount;
			CreatedUtc = createdUtc;
		}
	}

	private sealed class TargetState
	{
		public DateTime FirstHit;

		public DateTime LastHit;

		public long TotalDamage;

		public int HitCount;

		public bool IsBossEstimated;

		public bool IsBossConfirmed;

		public bool IsDefeatedEventFired;

		public bool IsEndedEventFired;

		public bool SuppressUpload;

		public int MaxHp;

		public int CurrentHp = -1;

		public DateTime LastHpUpdateUtc;

		public int MobCode;

		public bool SawFullHpForCurrentEncounter;

		public bool SawNonFullHpAfterDamage;

		public bool SawDeathOrZeroHpForCurrentEncounter;

		public DateTime DeathOrZeroHpObservedUtc;

		public bool PendingHpReset;

		public int PendingHpResetCurrentHp = -1;

		public DateTime PendingHpResetDetectedUtc;
	}

	private readonly record struct TargetSelectionCandidate(int TargetId, int Priority, long TotalDamage, int HitCount, TimeSpan Duration, bool IsBossActive, bool IsBossConfirmed);

	private readonly ConcurrentDictionary<int, ActorState> _actors = new ConcurrentDictionary<int, ActorState>();

	private readonly ConcurrentDictionary<int, TargetState> _targets = new ConcurrentDictionary<int, TargetState>();

	private readonly ConcurrentDictionary<int, byte> _removedEntityIds = new ConcurrentDictionary<int, byte>();

	private readonly NameCache _names;

	private readonly ConcurrentDictionary<int, List<DamageEvent>> _pendingEvents = new ConcurrentDictionary<int, List<DamageEvent>>();

	private readonly ConcurrentDictionary<int, DateTime> _pendingStartTime = new ConcurrentDictionary<int, DateTime>();

	private readonly ConcurrentDictionary<int, List<DamageEvent>> _pendingBossTargetEvents = new ConcurrentDictionary<int, List<DamageEvent>>();

	private readonly ConcurrentDictionary<int, DateTime> _pendingBossTargetStartTime = new ConcurrentDictionary<int, DateTime>();

	private readonly ConcurrentDictionary<int, EntityHpState> _entityHp = new ConcurrentDictionary<int, EntityHpState>();

	private readonly ConcurrentDictionary<int, List<PendingEffectiveHeal>> _pendingEffectiveHealsByTarget = new ConcurrentDictionary<int, List<PendingEffectiveHeal>>();

	private readonly ConcurrentDictionary<int, List<PendingEffectiveHeal>> _pendingEffectiveSelfHealsByActor = new ConcurrentDictionary<int, List<PendingEffectiveHeal>>();

	private readonly ConcurrentDictionary<int, byte> _confirmedActors = new ConcurrentDictionary<int, byte>();

	private readonly ConcurrentDictionary<int, JobClass> _knownActorJobs = new ConcurrentDictionary<int, JobClass>();

	private readonly ConcurrentDictionary<string, JobClass> _knownCharacterJobs = new ConcurrentDictionary<string, JobClass>(StringComparer.OrdinalIgnoreCase);

	private static readonly TimeSpan BossHpResetMinDamageGap = TimeSpan.FromSeconds(5L);

	private static readonly TimeSpan PendingBossTargetMaxAge = TimeSpan.FromSeconds(5L);

	private static readonly TimeSpan EffectiveHealMaxAge = TimeSpan.FromSeconds(3L);

	private const int PendingBossTargetMaxEventsPerTarget = 128;

	private const int MaxPendingEffectiveHealsPerTarget = 16;

	private const double BossHpResetFullToleranceRatio = 0.001;

	private const double BossUploadDamageRatio = 0.98;

	private const double BossUploadDamageUpperRatio = 1.1;

	private const double BossOpeningHpToleranceRatio = 0.002;

	private const string TrainingDummyName = "??덉졃????됰땾?袁⑦돩";

	private static readonly HashSet<int> DpsExcludedHealingBaseSkillCodes = new HashSet<int>
	{
		1225, 1617, 1677, 1709, 1710, 1712, 1729, 1741, 1780, 1812,
		1817
	};

	private static readonly HashSet<int> DamageEncodedHealingBaseSkillCodes = new HashSet<int> { 1677, 1709, 1729, 1780, 1817 };

	public Action<int, string, DateTime, DateTime, int, int>? OnBossDefeated;

	public Action<int, string, DateTime, DateTime, int, int>? OnBossEnded;

	public Action<int, string>? OnBossConfirmed;

	public Action<int, string>? OnBossHpReset;

	public Action? OnAutoReset;

	private readonly ConcurrentDictionary<int, int> _confirmedBossMaxHpById = new ConcurrentDictionary<int, int>();

	private readonly ConcurrentDictionary<int, int> _confirmedBossMobCodeById = new ConcurrentDictionary<int, int>();

	private readonly ConcurrentDictionary<int, byte> _uploadSuppressedBossIds = new ConcurrentDictionary<int, byte>();

	private DateTime? _sessionStart;

	private DateTime _lastEvent;

	private CombatSnapshot? _latest;

	public bool BossOnlyMeasurement { get; set; } = true;

	public bool ExcludeDotFromHits { get; set; }

	public Func<int, string>? GetSkillName { get; set; }

	public CombatSnapshot? LatestSnapshot => Volatile.Read(in _latest);

	public event Action<DamageEvent>? DamageAdded;

	public event Action<DamageEvent, string>? PacketLogEvent;

	public event Action<int, int>? SummonMerged;

	public void ConfirmBossTarget(int id, string name, int hp = 0, int mobCode = 0, bool suppressUpload = false)
	{
		if (id <= 0)
		{
			return;
		}
		_removedEntityIds.TryRemove(id, out var value);
		if (IsTrainingDummyName(name) || IsTrainingDummyName(_names.GetOrFallback(id)))
		{
			suppressUpload = true;
		}
		if (hp > 0)
		{
			_confirmedBossMaxHpById[id] = hp;
		}
		else if (!_confirmedBossMaxHpById.ContainsKey(id))
		{
			_confirmedBossMaxHpById[id] = hp;
		}
		if (mobCode > 0)
		{
			_confirmedBossMobCodeById[id] = mobCode;
		}
		if (suppressUpload)
		{
			_uploadSuppressedBossIds[id] = 1;
		}
		else
		{
			_uploadSuppressedBossIds.TryRemove(id, out value);
		}
		if (!string.IsNullOrEmpty(name))
		{
			_names.RegisterMonster(id, name);
		}
		if (_targets.TryGetValue(id, out TargetState value2))
		{
			value2.IsBossConfirmed = true;
			if (hp > 0)
			{
				value2.MaxHp = hp;
				if (value2.CurrentHp < 0)
				{
					value2.CurrentHp = hp;
				}
			}
			if (mobCode > 0)
			{
				value2.MobCode = mobCode;
			}
			value2.SuppressUpload = suppressUpload;
			if (suppressUpload)
			{
				value2.PendingHpReset = false;
				value2.PendingHpResetCurrentHp = -1;
				value2.PendingHpResetDetectedUtc = default(DateTime);
			}
		}
		CommitPendingBossTargetEvents(id);
	}

	public void SetMonsterId(int entityId)
	{
		if (entityId > 0)
		{
			_removedEntityIds.TryRemove(entityId, out var _);
			_knownActorJobs.TryRemove(entityId, out var _);
			ActorState orAdd = _actors.GetOrAdd(entityId, (int _) => new ActorState
			{
				First = DateTime.UtcNow
			});
			lock (orAdd)
			{
				orAdd.IsMonster = true;
				orAdd.Job = JobClass.None;
			}
			_names.RegisterMonster(entityId, "");
		}
	}

	public void ClearMonsterId(int entityId)
	{
		if (entityId <= 0)
		{
			return;
		}
		_names.UnregisterMonster(entityId);
		if (_actors.TryGetValue(entityId, out ActorState value))
		{
			ActorState value2 = value;
			bool lockTaken = false;
			bool isMonster;
			try
			{
				Monitor.Enter(value2, ref lockTaken);
				isMonster = value.IsMonster;
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(value2);
				}
			}
			if (isMonster)
			{
				_actors.TryRemove(entityId, out value2);
				_knownActorJobs.TryRemove(entityId, out var _);
			}
		}
		_pendingEvents.TryRemove(entityId, out List<DamageEvent> _);
		_pendingStartTime.TryRemove(entityId, out var _);
	}

	public void SetActorJobClass(int entityId, int jobCode)
	{
		if (entityId <= 0)
		{
			return;
		}
		JobClass jobClass = NormalizeActorJobClass(jobCode);
		if (jobClass == JobClass.None)
		{
			return;
		}
		if (_actors.TryGetValue(entityId, out ActorState value))
		{
			lock (value)
			{
				if (value.IsMonster)
				{
					return;
				}
				value.Job = jobClass;
			}
		}
		_knownActorJobs[entityId] = jobClass;
	}

	public void SetActorJobClass(int entityId, JobClass job)
	{
		if (entityId <= 0 || job == JobClass.None)
		{
			return;
		}
		if (_actors.TryGetValue(entityId, out ActorState value))
		{
			lock (value)
			{
				if (!value.IsMonster)
				{
					value.Job = job;
				}
			}
		}
		_knownActorJobs[entityId] = job;
	}

	public void SetCharacterJobClass(string characterName, string serverName, int jobCode)
	{
		if (!string.IsNullOrWhiteSpace(characterName) && !string.IsNullOrWhiteSpace(serverName))
		{
			JobClass jobClass = NormalizeActorJobClass(jobCode);
			if (jobClass != JobClass.None)
			{
				_knownCharacterJobs[CharacterJobKey(characterName, serverName)] = jobClass;
			}
		}
	}

	private bool TryGetKnownCharacterJob(string characterName, string serverName, out JobClass job)
	{
		job = JobClass.None;
		if (!string.IsNullOrWhiteSpace(characterName) && !string.IsNullOrWhiteSpace(serverName) && _knownCharacterJobs.TryGetValue(CharacterJobKey(characterName, serverName), out job))
		{
			return job != JobClass.None;
		}
		return false;
	}

	private static string CharacterJobKey(string characterName, string serverName)
	{
		return serverName.Trim() + "\u001f" + characterName.Trim();
	}

	private static (string Name, string ServerName) SplitCharacterDisplayName(string fullName)
	{
		if (string.IsNullOrWhiteSpace(fullName))
		{
			return (Name: "", ServerName: "");
		}
		int num = fullName.IndexOf('[');
		int num2 = fullName.IndexOf(']');
		if (num <= 0 || num2 <= num)
		{
			return (Name: fullName.Trim(), ServerName: "");
		}
		return (Name: fullName.Substring(0, num).Trim(), ServerName: fullName.Substring(num + 1, num2 - num - 1).Trim());
	}

	private static JobClass NormalizeActorJobClass(int jobCode)
	{
		switch (jobCode)
		{
		case 5:
		case 6:
		case 7:
		case 8:
			return JobClass.Gladiator;
		case 9:
		case 10:
		case 11:
		case 12:
			return JobClass.Templar;
		case 13:
		case 14:
		case 15:
		case 16:
			return JobClass.Ranger;
		case 17:
		case 18:
		case 19:
		case 20:
			return JobClass.Assassin;
		case 21:
		case 22:
		case 23:
		case 24:
			return JobClass.Spiritmaster;
		case 25:
		case 26:
		case 27:
		case 28:
			return JobClass.Sorcerer;
		case 29:
		case 30:
		case 31:
		case 32:
			return JobClass.Cleric;
		case 33:
		case 34:
		case 35:
		case 36:
			return JobClass.Chanter;
		case 37:
		case 38:
		case 39:
		case 40:
			return JobClass.Brawler;
		default:
			return JobClass.None;
		}
	}

	public CombatAggregator(NameCache names)
	{
		_names = names;
		_names.SummonMapped += MergeSummonData;
	}

	private void MergeSummonData(int summonId, int ownerId)
	{
		MergeActorData(summonId, ownerId);
	}

	private void MergeActorData(int summonId, int ownerId)
	{
		if (_pendingEvents.TryRemove(summonId, out List<DamageEvent> value))
		{
			_pendingStartTime.TryRemove(summonId, out var _);
			_confirmedActors[ownerId] = 1;
			using List<DamageEvent>.Enumerator enumerator = value.GetEnumerator();
			while (enumerator.MoveNext())
			{
				DamageEvent e = enumerator.Current with
				{
					ActorId = ownerId
				};
				ProcessDamageInternal(e);
			}
		}
		if (!_actors.TryRemove(summonId, out ActorState summonState))
		{
			this.SummonMerged?.Invoke(summonId, ownerId);
			return;
		}
		ActorState orAdd = _actors.GetOrAdd(ownerId, (int _) => new ActorState
		{
			First = summonState.First,
			Last = summonState.Last
		});
		lock (orAdd)
		{
			orAdd.TotalDamage += summonState.TotalDamage;
			orAdd.TotalHealing += summonState.TotalHealing;
			orAdd.SelfHealing += summonState.SelfHealing;
			orAdd.OtherHealing += summonState.OtherHealing;
			orAdd.HitEvents += summonState.HitEvents;
			orAdd.HealEvents += summonState.HealEvents;
			orAdd.CritEvents += summonState.CritEvents;
			orAdd.MultiEvents += summonState.MultiEvents;
			if (summonState.First < orAdd.First && summonState.First != default(DateTime))
			{
				orAdd.First = summonState.First;
			}
			if (summonState.Last > orAdd.Last)
			{
				orAdd.Last = summonState.Last;
			}
			foreach (KeyValuePair<int, (long, DateTime, DateTime)> item in summonState.PerTarget)
			{
				if (orAdd.PerTarget.TryGetValue(item.Key, out (long, DateTime, DateTime) value3))
				{
					orAdd.PerTarget[item.Key] = (value3.Item1 + item.Value.Item1, (item.Value.Item2 < value3.Item2) ? item.Value.Item2 : value3.Item2, (item.Value.Item3 > value3.Item3) ? item.Value.Item3 : value3.Item3);
				}
				else
				{
					orAdd.PerTarget[item.Key] = item.Value;
				}
			}
			foreach (KeyValuePair<int, Dictionary<int, SkillAgg>> perTargetSkill in summonState.PerTargetSkills)
			{
				if (!orAdd.PerTargetSkills.TryGetValue(perTargetSkill.Key, out Dictionary<int, SkillAgg> value4))
				{
					orAdd.PerTargetSkills[perTargetSkill.Key] = perTargetSkill.Value;
					continue;
				}
				foreach (KeyValuePair<int, SkillAgg> item2 in perTargetSkill.Value)
				{
					if (value4.TryGetValue(item2.Key, out var value5))
					{
						value5.TotalDamage += item2.Value.TotalDamage;
						value5.TotalHealing += item2.Value.TotalHealing;
						value5.SelfHealing += item2.Value.SelfHealing;
						value5.OtherHealing += item2.Value.OtherHealing;
						value5.HitCount += item2.Value.HitCount;
						value5.HealCount += item2.Value.HealCount;
						value5.CritCount += item2.Value.CritCount;
						value5.NormalHitCount += item2.Value.NormalHitCount;
						value5.BackCount += item2.Value.BackCount;
						value5.DoubleCount += item2.Value.DoubleCount;
						value5.PerfectCount += item2.Value.PerfectCount;
						value5.ParryCount += item2.Value.ParryCount;
						value5.EvadeCount += item2.Value.EvadeCount;
						value5.SmiteCount += item2.Value.SmiteCount;
						value5.MultiEventCount += item2.Value.MultiEventCount;
						if (item2.Value.MaxDamage > value5.MaxDamage)
						{
							value5.MaxDamage = item2.Value.MaxDamage;
						}
						if (item2.Value.MinDamage < value5.MinDamage)
						{
							value5.MinDamage = item2.Value.MinDamage;
						}
						if (item2.Value.MaxHeal > value5.MaxHeal)
						{
							value5.MaxHeal = item2.Value.MaxHeal;
						}
						if (item2.Value.MinHeal < value5.MinHeal)
						{
							value5.MinHeal = item2.Value.MinHeal;
						}
					}
					else
					{
						value4[item2.Key] = item2.Value;
					}
				}
			}
		}
		this.SummonMerged?.Invoke(summonId, ownerId);
	}

	public void TriggerAutoReset()
	{
		OnAutoReset?.Invoke();
	}

	public void FlushPendingBossDefeatsOnSessionEnd()
	{
		foreach (KeyValuePair<int, TargetState> target in _targets)
		{
			int key = target.Key;
			TargetState value = target.Value;
			if (value.IsBossConfirmed && value.TotalDamage > 0 && value.HitCount > 0)
			{
				double totalSeconds = (value.LastHit - value.FirstHit).TotalSeconds;
				bool flag = IsCompleteBossFightForUpload(value, totalSeconds);
				Console.WriteLine($"[FlushPendingBossDefeatsOnSessionEnd] BossId={key}(MobCode={value.MobCode}), Duration={totalSeconds:F1}s, TotalDmg={value.TotalDamage}, MaxHp={value.MaxHp}, uploadReady={flag}, sawFull={value.SawFullHpForCurrentEncounter}, sawDeath={value.SawDeathOrZeroHpForCurrentEncounter}");
				RaiseBossEndedIfNeeded(key, value);
				if (!value.IsDefeatedEventFired && !value.SuppressUpload && flag)
				{
					OnBossDefeated?.Invoke(key, _names.GetOrFallback(key), value.FirstHit, value.LastHit, value.MobCode, value.MaxHp);
					value.IsDefeatedEventFired = true;
				}
			}
		}
	}

	public void Reset()
	{
		_actors.Clear();
		_targets.Clear();
		_confirmedActors.Clear();
		_pendingEvents.Clear();
		_pendingStartTime.Clear();
		_pendingBossTargetEvents.Clear();
		_pendingBossTargetStartTime.Clear();
		_removedEntityIds.Clear();
		_pendingEffectiveHealsByTarget.Clear();
		_pendingEffectiveSelfHealsByActor.Clear();
		_sessionStart = null;
		_lastEvent = default(DateTime);
		Volatile.Write(ref _latest, null);
	}

	private void MarkSessionEvent(DateTime timestampUtc)
	{
		lock (this)
		{
			DateTime? sessionStart = _sessionStart;
			if (!sessionStart.HasValue)
			{
				_sessionStart = timestampUtc;
			}
			_lastEvent = timestampUtc;
		}
	}

	private bool IsBossMeasurementTarget(int targetId)
	{
		if (targetId <= 0)
		{
			return false;
		}
		if (_confirmedBossMaxHpById.ContainsKey(targetId))
		{
			return true;
		}
		if (_targets.TryGetValue(targetId, out TargetState value))
		{
			return value.IsBossConfirmed;
		}
		return false;
	}

	private bool ShouldBufferPotentialBossTarget(DamageEvent e)
	{
		if (e.TargetId <= 0 || e.TargetId == e.ActorId)
		{
			return false;
		}
		string orFallback = _names.GetOrFallback(e.TargetId);
		return !_names.IsMonster(e.TargetId, orFallback);
	}

	private void BufferPotentialBossTargetDamage(DamageEvent e)
	{
		List<DamageEvent> orAdd = _pendingBossTargetEvents.GetOrAdd(e.TargetId, delegate
		{
			_pendingBossTargetStartTime[e.TargetId] = DateTime.UtcNow;
			return new List<DamageEvent>();
		});
		lock (orAdd)
		{
			if (orAdd.Count >= 128)
			{
				orAdd.RemoveAt(0);
			}
			orAdd.Add(e);
		}
		PruneExpiredPendingBossTargetEvents(DateTime.UtcNow);
		this.PacketLogEvent?.Invoke(e, $"Boss-only target pending(target={e.TargetId},skill={e.SkillCodeRaw})");
	}

	private void PruneExpiredPendingBossTargetEvents(DateTime nowUtc)
	{
		KeyValuePair<int, DateTime>[] array = _pendingBossTargetStartTime.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			KeyValuePair<int, DateTime> keyValuePair = array[i];
			if (!(nowUtc - keyValuePair.Value < PendingBossTargetMaxAge))
			{
				_pendingBossTargetEvents.TryRemove(keyValuePair.Key, out List<DamageEvent> _);
				_pendingBossTargetStartTime.TryRemove(keyValuePair.Key, out var _);
			}
		}
	}

	private void CommitPendingBossTargetEvents(int targetId)
	{
		if (targetId <= 0 || !IsBossMeasurementTarget(targetId) || !_pendingBossTargetEvents.TryRemove(targetId, out List<DamageEvent> value))
		{
			return;
		}
		_pendingBossTargetStartTime.TryRemove(targetId, out var _);
		List<DamageEvent> list;
		lock (value)
		{
			list = value.OrderBy((DamageEvent e) => e.TimestampUtc).ToList();
		}
		foreach (DamageEvent item in list)
		{
			int num = _names.ResolveActorId(item.ActorId);
			if (num != item.ActorId && num == item.TargetId)
			{
				this.PacketLogEvent?.Invoke(item, $"Summon owner self-effect excluded(actor={item.ActorId},owner={num},skill={item.SkillCodeRaw},value={item.Damage})");
			}
			else
			{
				DamageEvent damageEvent = ((num != item.ActorId) ? item with
				{
					ActorId = num
				} : item);
				MarkSessionEvent(damageEvent.TimestampUtc);
				ProcessDamageInternal(damageEvent);
			}
		}
	}

	public void UpdateBossCurrentHp(int entityId, long hpValue)
	{
		if (entityId <= 0 || hpValue < 0)
		{
			return;
		}
		int currentHp = (int)((hpValue > int.MaxValue) ? int.MaxValue : hpValue);
		if (!_targets.TryGetValue(entityId, out TargetState value))
		{
			if (!_confirmedBossMaxHpById.TryGetValue(entityId, out var knownMaxHp))
			{
				return;
			}
			value = _targets.GetOrAdd(entityId, (int _) => new TargetState
			{
				FirstHit = DateTime.UtcNow,
				LastHit = DateTime.UtcNow,
				IsBossConfirmed = true,
				MaxHp = knownMaxHp,
				CurrentHp = currentHp,
				MobCode = (_confirmedBossMobCodeById.TryGetValue(entityId, out var value4) ? value4 : 0),
				SuppressUpload = _uploadSuppressedBossIds.ContainsKey(entityId)
			});
		}
		DateTime utcNow = DateTime.UtcNow;
		lock (value)
		{
			if (!value.IsBossConfirmed)
			{
				if (!_confirmedBossMaxHpById.TryGetValue(entityId, out var value2))
				{
					return;
				}
				value.IsBossConfirmed = true;
				if (value2 > 0)
				{
					value.MaxHp = value2;
				}
				if (_confirmedBossMobCodeById.TryGetValue(entityId, out var value3))
				{
					value.MobCode = value3;
				}
				value.SuppressUpload = _uploadSuppressedBossIds.ContainsKey(entityId);
			}
			if (value.MaxHp > 0 && (double)currentHp > (double)value.MaxHp * 1.05)
			{
				if (value.PendingHpReset)
				{
					ClearPendingHpReset(value);
				}
				return;
			}
			if (value.TotalDamage > 0 && value.HitCount > 0 && !IsFullBossHp(value.MaxHp, currentHp))
			{
				value.SawNonFullHpAfterDamage = true;
			}
			if (IsFullBossHp(value.MaxHp, currentHp) || LooksLikeOpeningHpAfterObservedDamage(value, currentHp))
			{
				value.SawFullHpForCurrentEncounter = true;
			}
			if (IsDeadBossHp(value.MaxHp, currentHp))
			{
				value.SawDeathOrZeroHpForCurrentEncounter = true;
				value.DeathOrZeroHpObservedUtc = utcNow;
			}
			if (!value.SuppressUpload && LooksLikeBossHpReset(value, currentHp))
			{
				value.PendingHpReset = true;
				value.PendingHpResetCurrentHp = currentHp;
				value.PendingHpResetDetectedUtc = utcNow;
			}
			value.CurrentHp = currentHp;
			value.LastHpUpdateUtc = utcNow;
		}
	}

	public void UpdateEntityHp(int entityId, long hpValue)
	{
		if (entityId <= 0 || hpValue < 0)
		{
			return;
		}
		int num = (int)((hpValue > int.MaxValue) ? int.MaxValue : hpValue);
		DateTime utcNow = DateTime.UtcNow;
		int num2 = 0;
		EntityHpState orAdd = _entityHp.GetOrAdd(entityId, (int _) => new EntityHpState());
		lock (orAdd)
		{
			if (orAdd.CurrentHp >= 0 && num > orAdd.CurrentHp)
			{
				num2 = num - orAdd.CurrentHp;
			}
			orAdd.CurrentHp = num;
			if (num > orAdd.MaxObservedHp)
			{
				orAdd.MaxObservedHp = num;
			}
			orAdd.LastUpdateUtc = utcNow;
		}
		if (num2 > 0)
		{
			if (!TryApplyEffectiveHealDelta(entityId, num2, utcNow))
			{
				TryApplySelfEffectiveHealDelta(entityId, num2, utcNow);
			}
		}
		else
		{
			PrunePendingEffectiveHeals(entityId, utcNow);
			PrunePendingEffectiveSelfHeals(entityId, utcNow);
		}
	}

	private void QueuePendingEffectiveHeal(DamageEvent e, int rawAmount)
	{
		if (rawAmount <= 0 || e.ActorId < 1 || e.ActorId > 99999 || !CanTrackEffectiveHealingTarget(e.TargetId))
		{
			return;
		}
		DateTime utcNow = DateTime.UtcNow;
		List<PendingEffectiveHeal> orAdd = _pendingEffectiveHealsByTarget.GetOrAdd(e.TargetId, (int _) => new List<PendingEffectiveHeal>());
		lock (orAdd)
		{
			PrunePendingEffectiveHealsLocked(orAdd, utcNow);
			if (orAdd.Count >= 16)
			{
				orAdd.RemoveAt(0);
			}
			orAdd.Add(new PendingEffectiveHeal(e, rawAmount, utcNow));
		}
		QueuePendingEffectiveSelfHealCandidate(e, rawAmount, utcNow);
		this.PacketLogEvent?.Invoke(e, $"Effective heal pending(skill={e.SkillCodeRaw},raw={rawAmount})");
	}

	private void QueuePendingEffectiveSelfHealCandidate(DamageEvent e, int rawAmount, DateTime now)
	{
		int actorId = ((e.ActorId > 0) ? _names.ResolveActorId(e.ActorId) : 0);
		int num = ((e.TargetId > 0) ? _names.ResolveActorId(e.TargetId) : 0);
		if (actorId <= 0 || num <= 0 || actorId == num)
		{
			return;
		}
		List<PendingEffectiveHeal> orAdd = _pendingEffectiveSelfHealsByActor.GetOrAdd(actorId, (int _) => new List<PendingEffectiveHeal>());
		lock (orAdd)
		{
			PrunePendingEffectiveHealsLocked(orAdd, now);
			if (!orAdd.Any((PendingEffectiveHeal p) => p.SourceEvent.ActorId == actorId && p.SourceEvent.SkillCodeRaw == e.SkillCodeRaw && p.SourceEvent.TimestampUtc == e.TimestampUtc))
			{
				if (orAdd.Count >= 16)
				{
					orAdd.RemoveAt(0);
				}
				orAdd.Add(new PendingEffectiveHeal(e with
				{
					ActorId = actorId,
					TargetId = actorId
				}, rawAmount, now));
			}
		}
	}

	private bool TryApplyEffectiveHealDelta(int targetId, int hpDelta, DateTime now)
	{
		if (!_pendingEffectiveHealsByTarget.TryGetValue(targetId, out List<PendingEffectiveHeal> value))
		{
			return false;
		}
		List<(DamageEvent, int)> list = new List<(DamageEvent, int)>();
		bool flag = false;
		lock (value)
		{
			PrunePendingEffectiveHealsLocked(value, now);
			if (value.Count > 0)
			{
				bool num = value.Select((PendingEffectiveHeal p) => p.SourceEvent.ActorId).Distinct().Count() == 1;
				bool flag2 = value.Select((PendingEffectiveHeal p) => GetSkillFamilyCode(p.SourceEvent.SkillCodeRaw)).Distinct().Count() == 1;
				if (num && flag2)
				{
					int num2 = hpDelta;
					while (num2 > 0 && value.Count > 0)
					{
						PendingEffectiveHeal pendingEffectiveHeal = value[0];
						int val = pendingEffectiveHeal.RawAmount - pendingEffectiveHeal.AppliedAmount;
						int num3 = Math.Min(num2, val);
						if (num3 > 0)
						{
							pendingEffectiveHeal.AppliedAmount += num3;
							list.Add((pendingEffectiveHeal.SourceEvent with
							{
								Damage = 0,
								MultiHitDamage = 0,
								HealAmount = num3
							}, hpDelta));
							num2 -= num3;
						}
						if (pendingEffectiveHeal.AppliedAmount < pendingEffectiveHeal.RawAmount)
						{
							break;
						}
						value.RemoveAt(0);
					}
				}
				else
				{
					flag = true;
					value.Clear();
				}
			}
			if (value.Count == 0)
			{
				_pendingEffectiveHealsByTarget.TryRemove(targetId, out List<PendingEffectiveHeal> _);
			}
		}
		foreach (var (damageEvent, value3) in list)
		{
			if (RecordHealingInternal(damageEvent))
			{
				this.PacketLogEvent?.Invoke(damageEvent, $"Effective heal confirmed(delta={value3},heal={damageEvent.HealAmount})");
				this.DamageAdded?.Invoke(damageEvent);
			}
		}
		if (list.Count == 0 && flag)
		{
			this.PacketLogEvent?.Invoke(new DamageEvent(IsDot: false, 0, targetId, 0, 0, 0, 0, 0, 0, Array.Empty<SpecialDamage>(), now), $"Effective heal ambiguous(target={targetId},delta={hpDelta})");
		}
		return true;
	}

	private bool TryApplySelfEffectiveHealDelta(int actorId, int hpDelta, DateTime now)
	{
		if (!_pendingEffectiveSelfHealsByActor.TryGetValue(actorId, out List<PendingEffectiveHeal> value))
		{
			return false;
		}
		PendingEffectiveHeal pendingEffectiveHeal = null;
		lock (value)
		{
			PrunePendingEffectiveHealsLocked(value, now);
			int num = value.FindIndex((PendingEffectiveHeal p) => hpDelta <= Math.Max(1, p.RawAmount));
			if (num >= 0)
			{
				pendingEffectiveHeal = value[num];
				value.RemoveAt(num);
			}
			if (value.Count == 0)
			{
				_pendingEffectiveSelfHealsByActor.TryRemove(actorId, out List<PendingEffectiveHeal> _);
			}
		}
		if (pendingEffectiveHeal == null)
		{
			return false;
		}
		DamageEvent damageEvent = pendingEffectiveHeal.SourceEvent with
		{
			ActorId = actorId,
			TargetId = actorId,
			Damage = 0,
			MultiHitDamage = 0,
			HealAmount = hpDelta
		};
		if (!RecordHealingInternal(damageEvent))
		{
			return false;
		}
		this.PacketLogEvent?.Invoke(damageEvent, $"Effective self heal inferred(delta={hpDelta},skill={damageEvent.SkillCodeRaw})");
		this.DamageAdded?.Invoke(damageEvent);
		return true;
	}

	private void PrunePendingEffectiveHeals(int targetId, DateTime now)
	{
		if (!_pendingEffectiveHealsByTarget.TryGetValue(targetId, out List<PendingEffectiveHeal> value))
		{
			return;
		}
		lock (value)
		{
			PrunePendingEffectiveHealsLocked(value, now);
			if (value.Count == 0)
			{
				_pendingEffectiveHealsByTarget.TryRemove(targetId, out List<PendingEffectiveHeal> _);
			}
		}
	}

	private void PrunePendingEffectiveSelfHeals(int actorId, DateTime now)
	{
		if (!_pendingEffectiveSelfHealsByActor.TryGetValue(actorId, out List<PendingEffectiveHeal> value))
		{
			return;
		}
		lock (value)
		{
			PrunePendingEffectiveHealsLocked(value, now);
			if (value.Count == 0)
			{
				_pendingEffectiveSelfHealsByActor.TryRemove(actorId, out List<PendingEffectiveHeal> _);
			}
		}
	}

	private static void PrunePendingEffectiveHealsLocked(List<PendingEffectiveHeal> list, DateTime now)
	{
		list.RemoveAll((PendingEffectiveHeal p) => p.AppliedAmount >= p.RawAmount || now - p.CreatedUtc > EffectiveHealMaxAge);
	}

	private bool CanTrackEffectiveHealingTarget(int targetId)
	{
		if (targetId < 1 || targetId > 99999)
		{
			return false;
		}
		if (_confirmedBossMaxHpById.ContainsKey(targetId))
		{
			return false;
		}
		if (_targets.TryGetValue(targetId, out TargetState value) && (value.IsBossConfirmed || value.IsBossEstimated))
		{
			return false;
		}
		string orFallback = _names.GetOrFallback(targetId);
		return !_names.IsMonster(targetId, orFallback);
	}

	private static bool LooksLikeBossHpReset(TargetState ts, int currentHp)
	{
		if (ts.MaxHp <= 0 || ts.TotalDamage <= 0 || ts.HitCount <= 0)
		{
			return false;
		}
		if (ts.SawNonFullHpAfterDamage)
		{
			return IsFullBossHp(ts.MaxHp, currentHp);
		}
		return false;
	}

	private static bool IsFullBossHp(int maxHp, int currentHp)
	{
		if (maxHp <= 0 || currentHp <= 0)
		{
			return false;
		}
		int num = Math.Max(1, (int)Math.Ceiling((double)maxHp * 0.001));
		if (currentHp >= maxHp - num)
		{
			return currentHp <= maxHp + num;
		}
		return false;
	}

	private static bool IsDeadBossHp(int maxHp, int currentHp)
	{
		if (maxHp <= 0 || currentHp < 0)
		{
			return false;
		}
		int num = Math.Max(1, (int)Math.Ceiling((double)maxHp * 0.001));
		return currentHp <= num;
	}

	private static bool LooksLikeOpeningHpAfterObservedDamage(TargetState ts, int currentHp)
	{
		if (ts.MaxHp <= 0 || currentHp <= 0 || ts.TotalDamage <= 0)
		{
			return false;
		}
		long num = currentHp + ts.TotalDamage;
		long num2 = Math.Max(1L, (long)Math.Ceiling((double)ts.MaxHp * 0.002));
		return Math.Abs(num - ts.MaxHp) <= num2;
	}

	private static bool LooksLikeResetOpeningHpAfterDamage(int maxHp, int currentHp, long damage)
	{
		if (maxHp <= 0 || currentHp <= 0 || damage <= 0)
		{
			return false;
		}
		long num = currentHp + damage;
		long num2 = Math.Max(1L, (long)Math.Ceiling((double)maxHp * 0.002));
		return Math.Abs(num - maxHp) <= num2;
	}

	private static bool IsCompleteBossFightForUpload(TargetState ts, double durationSeconds)
	{
		if (durationSeconds < 20.0)
		{
			return false;
		}
		return IsCompleteBossFightForLocalRecord(ts);
	}

	private static bool IsCompleteBossFightForLocalRecord(TargetState ts)
	{
		if (!ts.IsBossConfirmed || ts.MaxHp <= 0 || ts.TotalDamage <= 0 || ts.HitCount <= 0)
		{
			return false;
		}
		if (ts.SawFullHpForCurrentEncounter && HasObservedBossEnd(ts))
		{
			return IsBossDamageWithinCompletionRange(ts.TotalDamage, ts.MaxHp);
		}
		return false;
	}

	private static bool IsBossDamageWithinCompletionRange(long totalDamage, int maxHp)
	{
		if ((double)totalDamage >= (double)maxHp * 0.98)
		{
			return (double)totalDamage <= (double)maxHp * 1.1;
		}
		return false;
	}

	private static bool HasObservedDeathOrZeroHpForCurrentEncounter(TargetState ts)
	{
		if (ts.SawDeathOrZeroHpForCurrentEncounter && ts.DeathOrZeroHpObservedUtc != default(DateTime) && ts.DeathOrZeroHpObservedUtc >= ts.FirstHit)
		{
			return ts.DeathOrZeroHpObservedUtc >= ts.LastHit;
		}
		return false;
	}

	private static bool HasObservedBossEnd(TargetState ts)
	{
		if (!ts.IsDefeatedEventFired)
		{
			return HasObservedDeathOrZeroHpForCurrentEncounter(ts);
		}
		return true;
	}

	private static void ClearPendingHpReset(TargetState ts)
	{
		ts.PendingHpReset = false;
		ts.PendingHpResetCurrentHp = -1;
		ts.PendingHpResetDetectedUtc = default(DateTime);
	}

	private bool TryApplyPendingHpReset(int targetId, TargetState ts, DateTime resetUtc)
	{
		int currentHp;
		lock (ts)
		{
			if (!ts.PendingHpReset)
			{
				return false;
			}
			if (ts.SuppressUpload)
			{
				ClearPendingHpReset(ts);
				return false;
			}
			DateTime pendingHpResetDetectedUtc = ts.PendingHpResetDetectedUtc;
			currentHp = ((ts.PendingHpResetCurrentHp >= 0) ? ts.PendingHpResetCurrentHp : ((ts.CurrentHp >= 0) ? ts.CurrentHp : ts.MaxHp));
			if (!(pendingHpResetDetectedUtc != default(DateTime)) || !(resetUtc >= pendingHpResetDetectedUtc))
			{
				return false;
			}
			ClearPendingHpReset(ts);
		}
		ResetTargetEncounter(targetId, currentHp, resetUtc);
		return true;
	}

	private bool TryApplyOpeningHitHpReset(int targetId, TargetState ts, DateTime resetUtc, long openingDamage)
	{
		int currentHp;
		lock (ts)
		{
			if (!ts.IsBossConfirmed || ts.SuppressUpload || ts.TotalDamage <= 0 || ts.HitCount <= 0)
			{
				return false;
			}
			currentHp = ts.CurrentHp;
			int maxHp = ts.MaxHp;
			DateTime lastHit = ts.LastHit;
			if (resetUtc - lastHit < BossHpResetMinDamageGap)
			{
				return false;
			}
			if (!LooksLikeResetOpeningHpAfterDamage(maxHp, currentHp, openingDamage))
			{
				return false;
			}
			ClearPendingHpReset(ts);
		}
		ResetTargetEncounter(targetId, currentHp, resetUtc);
		return true;
	}

	private void ResetTargetEncounter(int targetId, int currentHp, DateTime resetUtc)
	{
		CommitPendingBossTargetEvents(targetId);
		if (_targets.TryGetValue(targetId, out TargetState value) && value.IsBossConfirmed && value.TotalDamage > 0 && value.HitCount > 0 && HasObservedBossEnd(value))
		{
			RaiseBossEndedIfNeeded(targetId, value);
		}
		RemovePendingEventsForTarget(targetId);
		KeyValuePair<int, ActorState>[] array = _actors.ToArray();
		foreach (KeyValuePair<int, ActorState> keyValuePair in array)
		{
			ActorState value2 = keyValuePair.Value;
			lock (value2)
			{
				value2.PerTarget.Remove(targetId);
				value2.PerTargetSkills.Remove(targetId);
				RecalculateActorStateFromTargets(value2, resetUtc);
			}
		}
		TargetState orAdd = _targets.GetOrAdd(targetId, (int _) => new TargetState());
		lock (orAdd)
		{
			orAdd.FirstHit = resetUtc;
			orAdd.LastHit = resetUtc;
			orAdd.TotalDamage = 0L;
			orAdd.HitCount = 0;
			orAdd.IsBossConfirmed = true;
			orAdd.IsBossEstimated = false;
			orAdd.IsDefeatedEventFired = false;
			orAdd.IsEndedEventFired = false;
			orAdd.SawFullHpForCurrentEncounter = IsFullBossHp(orAdd.MaxHp, currentHp);
			orAdd.SawNonFullHpAfterDamage = false;
			orAdd.SawDeathOrZeroHpForCurrentEncounter = false;
			orAdd.DeathOrZeroHpObservedUtc = default(DateTime);
			orAdd.PendingHpReset = false;
			orAdd.PendingHpResetCurrentHp = -1;
			orAdd.PendingHpResetDetectedUtc = default(DateTime);
			orAdd.CurrentHp = currentHp;
			orAdd.LastHpUpdateUtc = resetUtc;
			if (_confirmedBossMaxHpById.TryGetValue(targetId, out var value3) && value3 > 0)
			{
				orAdd.MaxHp = value3;
				orAdd.SawFullHpForCurrentEncounter = IsFullBossHp(orAdd.MaxHp, currentHp);
			}
			if (_confirmedBossMobCodeById.TryGetValue(targetId, out var value4))
			{
				orAdd.MobCode = value4;
			}
			orAdd.SuppressUpload = _uploadSuppressedBossIds.ContainsKey(targetId);
		}
		_lastEvent = resetUtc;
		DateTime? sessionStart = _sessionStart;
		if (!sessionStart.HasValue)
		{
			_sessionStart = resetUtc;
		}
		BuildSnapshotParallel();
	}

	private void RemovePendingEventsForTarget(int targetId)
	{
		KeyValuePair<int, List<DamageEvent>>[] array = _pendingEvents.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			KeyValuePair<int, List<DamageEvent>> keyValuePair = array[i];
			List<DamageEvent> value = keyValuePair.Value;
			lock (value)
			{
				value.RemoveAll((DamageEvent e) => e.TargetId == targetId);
				if (value.Count == 0)
				{
					_pendingEvents.TryRemove(keyValuePair.Key, out List<DamageEvent> _);
					_pendingStartTime.TryRemove(keyValuePair.Key, out var _);
				}
			}
		}
	}

	private static void RecalculateActorStateFromTargets(ActorState st, DateTime fallbackUtc)
	{
		st.TotalDamage = st.PerTarget.Values.Sum(((long Dmg, DateTime First, DateTime Last) x) => x.Dmg);
		st.TotalHealing = 0L;
		st.SelfHealing = 0L;
		st.OtherHealing = 0L;
		st.HitEvents = 0;
		st.CritEvents = 0;
		st.MultiEvents = 0;
		st.HealEvents = 0;
		foreach (Dictionary<int, SkillAgg> value in st.PerTargetSkills.Values)
		{
			foreach (SkillAgg value2 in value.Values)
			{
				st.HitEvents += value2.HitCount;
				st.CritEvents += value2.CritCount;
				st.MultiEvents += value2.MultiEventCount;
				st.TotalHealing += value2.TotalHealing;
				st.SelfHealing += value2.SelfHealing;
				st.OtherHealing += value2.OtherHealing;
				st.HealEvents += value2.HealCount;
			}
		}
		if (st.PerTarget.Count == 0)
		{
			st.First = fallbackUtc;
			st.Last = fallbackUtc;
			return;
		}
		st.First = st.PerTarget.Values.Min(((long Dmg, DateTime First, DateTime Last) x) => x.First);
		st.Last = st.PerTarget.Values.Max(((long Dmg, DateTime First, DateTime Last) x) => x.Last);
	}

	public void HandleEntityRemoved(int entityId)
	{
		if (entityId > 0)
		{
			_removedEntityIds[entityId] = 1;
		}
		if (_targets.TryGetValue(entityId, out TargetState value) && value.IsBossConfirmed)
		{
			if (value.IsDefeatedEventFired)
			{
				return;
			}
			double totalSeconds = (value.LastHit - value.FirstHit).TotalSeconds;
			value.SawDeathOrZeroHpForCurrentEncounter = true;
			value.DeathOrZeroHpObservedUtc = DateTime.UtcNow;
			bool flag = IsCompleteBossFightForUpload(value, totalSeconds);
			Console.WriteLine($"[HandleEntityRemoved] BossId={entityId}(MobCode={value.MobCode}), Duration={totalSeconds:F1}s, TotalDmg={value.TotalDamage}, MaxHp={value.MaxHp}, uploadReady={flag}, sawFull={value.SawFullHpForCurrentEncounter}, sawDeath={value.SawDeathOrZeroHpForCurrentEncounter}");
			if (flag)
			{
				RaiseBossEndedIfNeeded(entityId, value);
				if (!value.SuppressUpload)
				{
					OnBossDefeated?.Invoke(entityId, _names.GetOrFallback(entityId), value.FirstHit, value.LastHit, value.MobCode, value.MaxHp);
				}
				value.IsDefeatedEventFired = true;
			}
		}
		ClearMonsterId(entityId);
	}

	public void OnDamage(DamageEvent e)
	{
		int num = _names.ResolveActorId(e.ActorId);
		if (num != e.ActorId && num == e.TargetId)
		{
			this.PacketLogEvent?.Invoke(e, $"Summon owner self-effect excluded(actor={e.ActorId},owner={num},skill={e.SkillCodeRaw},value={e.Damage})");
			return;
		}
		if (num != e.ActorId)
		{
			e = e with
			{
				ActorId = num
			};
		}
		if (ShouldIgnorePersonalTargetDamage(e))
		{
			this.PacketLogEvent?.Invoke(e, $"Personal target non-local damage excluded(actor={e.ActorId},target={e.TargetId})");
		}
		else if (e.TargetId > 0 && _removedEntityIds.ContainsKey(e.TargetId))
		{
			this.PacketLogEvent?.Invoke(e, $"Removed target damage excluded(target={e.TargetId})");
		}
		else
		{
			if (!TryNormalizeNonHealingMetadata(ref e))
			{
				return;
			}
			if (IsDamageEncodedHealingSkill(e.SkillCodeRaw))
			{
				int num2 = Math.Max(e.Damage, e.MultiHitDamage);
				if (num2 > 0)
				{
					QueuePendingEffectiveHeal(e, num2);
					return;
				}
				if (e.HealAmount > 0)
				{
					this.PacketLogEvent?.Invoke(e, $"Effective heal metadata excluded(skill={e.SkillCodeRaw},heal={e.HealAmount})");
					return;
				}
			}
			bool num3 = e.HealAmount > 0;
			bool flag = e.Damage > 0 || e.MultiHitDamage > 0;
			if (num3)
			{
				bool flag2 = RecordHealingInternal(e);
				if (!flag)
				{
					if (flag2)
					{
						this.PacketLogEvent?.Invoke(e, $"Pure heal recorded(heal={e.HealAmount})");
						this.DamageAdded?.Invoke(e);
					}
					return;
				}
			}
			bool flag3 = IsBossMeasurementTarget(e.TargetId);
			if (BossOnlyMeasurement && !flag3)
			{
				if (ShouldBufferPotentialBossTarget(e))
				{
					BufferPotentialBossTargetDamage(e);
					return;
				}
				this.PacketLogEvent?.Invoke(e, $"Boss-only non-boss target excluded(target={e.TargetId},skill={e.SkillCodeRaw})");
				return;
			}
			MarkSessionEvent(e.TimestampUtc);
			int num4 = _names.ResolveActorId(e.ActorId);
			if (num4 != e.ActorId)
			{
				e = e with
				{
					ActorId = num4
				};
			}
			if (!flag3 && ShouldBuffer(e.ActorId))
			{
				List<DamageEvent> orAdd = _pendingEvents.GetOrAdd(e.ActorId, delegate
				{
					_pendingStartTime[e.ActorId] = DateTime.UtcNow;
					return new List<DamageEvent>();
				});
				int count;
				lock (orAdd)
				{
					orAdd.Add(e);
					count = orAdd.Count;
				}
				if (count >= 3)
				{
					TryCommitPendingActor(e.ActorId, force: false);
				}
			}
			else
			{
				ProcessDamageInternal(e);
			}
		}
	}

	internal void ReplayRecordedDamage(DamageEvent e)
	{
		if (!TryNormalizeNonHealingMetadata(ref e))
		{
			return;
		}
		bool num = e.HealAmount > 0;
		bool flag = HasDamageComponent(e);
		if (num && !flag)
		{
			if (RecordHealingInternal(e))
			{
				this.DamageAdded?.Invoke(e);
			}
		}
		else
		{
			OnDamage(e);
		}
	}

	private bool TryNormalizeNonHealingMetadata(ref DamageEvent e)
	{
		if (e.HealAmount <= 0 || !IsGodstoneDotDamageSkill(e.SkillCodeRaw))
		{
			return true;
		}
		if (!HasDamageComponent(e))
		{
			this.PacketLogEvent?.Invoke(e, $"Godstone dot heal metadata excluded(skill={e.SkillCodeRaw},heal={e.HealAmount})");
			return false;
		}
		this.PacketLogEvent?.Invoke(e, $"Godstone dot heal metadata stripped(skill={e.SkillCodeRaw},heal={e.HealAmount})");
		e = e with
		{
			HealAmount = 0
		};
		return true;
	}

	private bool RecordHealingInternal(DamageEvent e)
	{
		if (e.HealAmount <= 0 || e.ActorId < 1 || e.ActorId > 99999)
		{
			return false;
		}
		IReadOnlyList<int> readOnlyList = ResolveHealingMeasurementTargets(e);
		if (readOnlyList.Count == 0)
		{
			this.PacketLogEvent?.Invoke(e, $"Boss heal excluded(no boss damage,heal={e.HealAmount})");
			return false;
		}
		MarkSessionEvent(e.TimestampUtc);
		ActorState orAdd = _actors.GetOrAdd(e.ActorId, delegate
		{
			string orFallback = _names.GetOrFallback(e.ActorId);
			bool flag2 = _names.IsMonster(e.ActorId, orFallback);
			_knownActorJobs.TryGetValue(e.ActorId, out var value3);
			return new ActorState
			{
				First = e.TimestampUtc,
				IsMonster = flag2,
				Job = ((!flag2) ? value3 : JobClass.None)
			};
		});
		if (orAdd.IsMonster)
		{
			return false;
		}
		bool flag = IsSelfHealingEvent(e);
		lock (orAdd)
		{
			if (orAdd.First == default(DateTime) || e.TimestampUtc < orAdd.First)
			{
				orAdd.First = e.TimestampUtc;
			}
			if (e.TimestampUtc > orAdd.Last)
			{
				orAdd.Last = e.TimestampUtc;
			}
			orAdd.TotalHealing += e.HealAmount;
			if (flag)
			{
				orAdd.SelfHealing += e.HealAmount;
			}
			else
			{
				orAdd.OtherHealing += e.HealAmount;
			}
			orAdd.HealEvents++;
			foreach (int item in readOnlyList)
			{
				if (!orAdd.PerTargetSkills.TryGetValue(item, out Dictionary<int, SkillAgg> value))
				{
					value = new Dictionary<int, SkillAgg>();
					orAdd.PerTargetSkills[item] = value;
				}
				if (!value.TryGetValue(e.SkillCodeRaw, out var value2))
				{
					value2 = new SkillAgg();
					value[e.SkillCodeRaw] = value2;
				}
				value2.TotalHealing += e.HealAmount;
				if (flag)
				{
					value2.SelfHealing += e.HealAmount;
				}
				else
				{
					value2.OtherHealing += e.HealAmount;
				}
				value2.HealCount++;
				if (e.HealAmount > value2.MaxHeal)
				{
					value2.MaxHeal = e.HealAmount;
				}
				if (e.HealAmount > 0 && e.HealAmount < value2.MinHeal)
				{
					value2.MinHeal = e.HealAmount;
				}
				if (e.SkillLevel > 0)
				{
					value2.SkillLevel = e.SkillLevel;
				}
				if (e.BaseSkillLevel > 0)
				{
					value2.BaseSkillLevel = e.BaseSkillLevel;
				}
			}
		}
		return true;
	}

	private IReadOnlyList<int> ResolveHealingMeasurementTargets(DamageEvent e)
	{
		if (!BossOnlyMeasurement)
		{
			if (e.TargetId <= 0)
			{
				return Array.Empty<int>();
			}
			return new int[1] { e.TargetId };
		}
		if (HasDamageComponent(e) && IsBossMeasurementTarget(e.TargetId))
		{
			return new int[1] { e.TargetId };
		}
		int num = _names.ResolveActorId(e.ActorId);
		if (!_actors.TryGetValue(num, out ActorState value) && (num == e.ActorId || !_actors.TryGetValue(e.ActorId, out value)))
		{
			return Array.Empty<int>();
		}
		List<int> list = new List<int>();
		HashSet<int> hashSet = new HashSet<int>();
		lock (value)
		{
			foreach (KeyValuePair<int, (long, DateTime, DateTime)> item in value.PerTarget)
			{
				if (item.Value.Item1 > 0 && IsBossMeasurementTarget(item.Key) && _targets.TryGetValue(item.Key, out TargetState value2) && IsHealInsideBossWindow(e.TimestampUtc, value2) && hashSet.Add(item.Key))
				{
					list.Add(item.Key);
				}
			}
			return list;
		}
	}

	private static bool IsHealInsideBossWindow(DateTime healUtc, TargetState ts)
	{
		if (!ts.IsBossConfirmed || ts.FirstHit == default(DateTime) || healUtc < ts.FirstHit)
		{
			return false;
		}
		DateTime observedBossEndUtc = GetObservedBossEndUtc(ts);
		if (!(observedBossEndUtc == default(DateTime)))
		{
			return healUtc <= observedBossEndUtc;
		}
		return true;
	}

	private static DateTime GetObservedBossEndUtc(TargetState ts)
	{
		if (ts.DeathOrZeroHpObservedUtc != default(DateTime))
		{
			return ts.DeathOrZeroHpObservedUtc;
		}
		if (ts.IsEndedEventFired)
		{
			return ts.LastHit;
		}
		return default(DateTime);
	}

	private bool IsSelfHealingEvent(DamageEvent e)
	{
		if (e.TargetId <= 0)
		{
			return false;
		}
		int num = _names.ResolveActorId(e.ActorId);
		int num2 = _names.ResolveActorId(e.TargetId);
		if (num > 0 && num == num2)
		{
			return true;
		}
		if (HasDamageComponent(e))
		{
			return IsEnemyOrUnknownHealingTarget(num2);
		}
		return false;
	}

	private bool IsEnemyOrUnknownHealingTarget(int targetId)
	{
		if (targetId <= 0)
		{
			return false;
		}
		if (_knownActorJobs.ContainsKey(targetId) || _confirmedActors.ContainsKey(targetId))
		{
			return false;
		}
		string orFallback = _names.GetOrFallback(targetId);
		if (_names.IsMonster(targetId, orFallback) || _confirmedBossMaxHpById.ContainsKey(targetId))
		{
			return true;
		}
		if (_targets.TryGetValue(targetId, out TargetState value) && value.TotalDamage > 0)
		{
			return true;
		}
		if (!int.TryParse(orFallback, out var _))
		{
			return orFallback.StartsWith("Actor ", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool HasDamageComponent(DamageEvent e)
	{
		if (e.Damage <= 0)
		{
			return e.MultiHitDamage > 0;
		}
		return true;
	}

	private bool ShouldIgnorePersonalTargetDamage(DamageEvent e)
	{
		if (e.TargetId <= 0 || e.TargetId == e.ActorId)
		{
			return false;
		}
		if (!IsTrainingDummyTarget(e.TargetId))
		{
			return false;
		}
		if (!HasLocalPlayerIdentity())
		{
			return false;
		}
		return !IsLocalPlayerActor(e.ActorId);
	}

	private bool IsTrainingDummyTarget(int targetId)
	{
		return IsTrainingDummyName(_names.GetOrFallback(targetId));
	}

	private static bool IsTrainingDummyName(string? name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			return name.Contains("??덉졃????됰땾?袁⑦돩", StringComparison.Ordinal);
		}
		return false;
	}

	private bool HasLocalPlayerIdentity()
	{
		if (!_names.LocalPlayerActorId.HasValue)
		{
			return !string.IsNullOrWhiteSpace(_names.LocalPlayerName);
		}
		return true;
	}

	public bool IsUploadSuppressedTarget(int targetId)
	{
		if (_uploadSuppressedBossIds.ContainsKey(targetId))
		{
			return true;
		}
		if (_targets.TryGetValue(targetId, out TargetState value))
		{
			return value.SuppressUpload;
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
		return IsSamePlayerName(_names.GetOrFallback(actorId), localPlayerName);
	}

	private static bool IsSamePlayerName(string actorName, string localName)
	{
		string text = StripServerSuffix(actorName);
		string text2 = StripServerSuffix(localName);
		if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2))
		{
			return string.Equals(text, text2, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static string StripServerSuffix(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		return SplitCharacterDisplayName(value).Name;
	}

	private static int GetTargetSelectionPriority(TargetState ts)
	{
		if (ts.IsBossConfirmed)
		{
			return 2;
		}
		return ts.IsBossEstimated ? 1 : 0;
	}

	private bool ShouldBuffer(int actorId)
	{
		if (_actors.ContainsKey(actorId))
		{
			return false;
		}
		if (_confirmedActors.ContainsKey(actorId))
		{
			return false;
		}
		if (_targets.TryGetValue(actorId, out TargetState value) && value.IsBossConfirmed)
		{
			return false;
		}
		string orFallback = _names.GetOrFallback(actorId);
		if (_names.IsMonster(actorId, orFallback))
		{
			return false;
		}
		if (IsPlayerOrLocal(orFallback, actorId))
		{
			return false;
		}
		if (_confirmedBossMaxHpById.ContainsKey(actorId))
		{
			return false;
		}
		return true;
	}

	private void ProcessDamageInternal(DamageEvent e)
	{
		if (!string.IsNullOrEmpty(e.FilterReason))
		{
			this.PacketLogEvent?.Invoke(e, e.FilterReason);
			return;
		}
		if (ShouldIgnorePersonalTargetDamage(e))
		{
			this.PacketLogEvent?.Invoke(e, $"Personal target non-local damage excluded(actor={e.ActorId},target={e.TargetId})");
			return;
		}
		bool flag = IsBossMeasurementTarget(e.TargetId);
		if (BossOnlyMeasurement && !flag)
		{
			this.PacketLogEvent?.Invoke(e, $"Boss-only non-boss target excluded(target={e.TargetId},skill={e.SkillCodeRaw})");
			return;
		}
		ActorState orAdd = _actors.GetOrAdd(e.ActorId, delegate
		{
			string orFallback2 = _names.GetOrFallback(e.ActorId);
			bool flag7 = _names.IsMonster(e.ActorId, orFallback2);
			_knownActorJobs.TryGetValue(e.ActorId, out var value8);
			return new ActorState
			{
				First = e.TimestampUtc,
				IsMonster = flag7,
				Job = ((!flag7) ? value8 : JobClass.None)
			};
		});
		if (_names.IsMonster(e.ActorId, _names.GetOrFallback(e.ActorId)))
		{
			lock (orAdd)
			{
				orAdd.IsMonster = true;
				orAdd.Job = JobClass.None;
			}
		}
		if (orAdd.IsMonster)
		{
			this.PacketLogEvent?.Invoke(e, $"IsMonster=true ??嶺뚮????{e.ActorId})");
			return;
		}
		lock (orAdd)
		{
			if (e.Specials != null && e.Specials.Contains(SpecialDamage.UNKNOWN))
			{
				this.PacketLogEvent?.Invoke(e, "UNKNOWN special damage");
				return;
			}
			if (e.ActorId < 1 || e.ActorId > 99999)
			{
				this.PacketLogEvent?.Invoke(e, $"ActorId ?筌??????怨룰텥?{e.ActorId})");
				return;
			}
			if (e.TargetId != 0 && (e.TargetId < 1 || e.TargetId > 99999))
			{
				this.PacketLogEvent?.Invoke(e, $"TargetId ?筌??????怨룰텥?{e.TargetId})");
				return;
			}
			if (e.SkillCodeRaw < 0)
			{
				this.PacketLogEvent?.Invoke(e, $"???熬곥굥?珥놡 ?????{e.SkillCodeRaw})");
				return;
			}
			if (!e.IsDot && e.Type == 51)
			{
				this.PacketLogEvent?.Invoke(e, "??? ??????嶺뚮????Type=51)");
				return;
			}
			if (IsDpsExcludedHealingSkill(e.SkillCodeRaw))
			{
				this.PacketLogEvent?.Invoke(e, $"????????ш끽維\u0080?????嶺뚮????skill={e.SkillCodeRaw})");
				return;
			}
			bool flag2 = e.Damage > 0 || e.MultiHitDamage > 0;
			if (e.HealAmount > 0 && !flag2)
			{
				this.PacketLogEvent?.Invoke(e, $"??嶺????????heal={e.HealAmount})");
				return;
			}
			long num = e.Damage;
			if (e.SkillCodeRaw < 1000 && num < 100 && num > 0)
			{
				this.PacketLogEvent?.Invoke(e, "?????熬곥굥????????꿔꺂???(skill<1000,dmg<100)");
				return;
			}
			if (e.MultiHitDamage > e.Damage && e.Damage != 0 && e.Damage != 1064)
			{
				this.PacketLogEvent?.Invoke(e, $"?꿔꺂????????살퓢?癰귥쥙?\u0080?嚥??? ??縕???dmg={e.Damage},multi={e.MultiHitDamage})");
				return;
			}
			long num2 = num;
			this.PacketLogEvent?.Invoke(e, "");
			bool flag3 = false;
			bool flag4 = false;
			bool flag5 = true;
			if (e.TargetId != 0 && e.TargetId != e.ActorId)
			{
				string orFallback = _names.GetOrFallback(e.TargetId);
				bool num3 = !_targets.ContainsKey(e.TargetId);
				TargetState orAdd2 = _targets.GetOrAdd(e.TargetId, (int _) => new TargetState
				{
					FirstHit = e.TimestampUtc,
					LastHit = e.TimestampUtc,
					TotalDamage = 0L
				});
				if (TryApplyPendingHpReset(e.TargetId, orAdd2, e.TimestampUtc))
				{
					flag4 = true;
				}
				else if (TryApplyOpeningHitHpReset(e.TargetId, orAdd2, e.TimestampUtc, num2))
				{
					flag4 = true;
				}
				if (num3 && !orAdd2.IsBossConfirmed && _confirmedBossMaxHpById.TryGetValue(e.TargetId, out var value))
				{
					orAdd2.IsBossConfirmed = true;
					if (value > 0)
					{
						orAdd2.MaxHp = value;
						if (orAdd2.CurrentHp < 0)
						{
							orAdd2.CurrentHp = value;
						}
					}
					if (_confirmedBossMobCodeById.TryGetValue(e.TargetId, out var value2))
					{
						orAdd2.MobCode = value2;
					}
					orAdd2.SuppressUpload = _uploadSuppressedBossIds.ContainsKey(e.TargetId);
					OnBossConfirmed?.Invoke(e.TargetId, orFallback);
				}
				if (!orAdd2.IsBossConfirmed && _confirmedBossMaxHpById.TryGetValue(e.TargetId, out var value3))
				{
					orAdd2.IsBossConfirmed = true;
					if (value3 > 0)
					{
						orAdd2.MaxHp = value3;
						if (orAdd2.CurrentHp < 0)
						{
							orAdd2.CurrentHp = value3;
						}
					}
					if (_confirmedBossMobCodeById.TryGetValue(e.TargetId, out var value4))
					{
						orAdd2.MobCode = value4;
					}
					orAdd2.SuppressUpload = _uploadSuppressedBossIds.ContainsKey(e.TargetId);
					OnBossConfirmed?.Invoke(e.TargetId, orFallback);
					foreach (KeyValuePair<int, TargetState> target in _targets)
					{
						if (target.Key != e.TargetId && target.Value.IsBossEstimated && !target.Value.IsBossConfirmed)
						{
							target.Value.IsBossEstimated = false;
						}
					}
				}
				if (BossOnlyMeasurement && !orAdd2.IsBossConfirmed && !_confirmedBossMaxHpById.ContainsKey(e.TargetId))
				{
					flag3 = true;
				}
				if (!flag3)
				{
					if (orAdd2.TotalDamage <= 0 && orAdd2.HitCount <= 0)
					{
						orAdd2.FirstHit = e.TimestampUtc;
					}
					orAdd2.TotalDamage += num2;
					if (num2 > 0 && orAdd2.IsBossConfirmed && orAdd2.CurrentHp > 0 && !IsFullBossHp(orAdd2.MaxHp, orAdd2.CurrentHp))
					{
						orAdd2.SawNonFullHpAfterDamage = true;
					}
					if (!orAdd2.SawFullHpForCurrentEncounter && orAdd2.LastHpUpdateUtc != default(DateTime) && LooksLikeOpeningHpAfterObservedDamage(orAdd2, orAdd2.CurrentHp))
					{
						orAdd2.SawFullHpForCurrentEncounter = true;
					}
					orAdd2.LastHit = e.TimestampUtc;
					orAdd2.HitCount++;
				}
				if (flag4)
				{
					OnBossHpReset?.Invoke(e.TargetId, _names.GetOrFallback(e.TargetId));
				}
				if (flag3)
				{
					this.PacketLogEvent?.Invoke(e, $"????????堉??DPS ??癲ル슢????target={e.TargetId},skill={e.SkillCodeRaw})");
					return;
				}
				if (orAdd.PerTarget.TryGetValue(e.TargetId, out (long, DateTime, DateTime) value5))
				{
					orAdd.PerTarget[e.TargetId] = (value5.Item1 + num2, value5.Item2, e.TimestampUtc);
				}
				else
				{
					orAdd.PerTarget[e.TargetId] = (num2, e.TimestampUtc, e.TimestampUtc);
				}
				if (!orAdd.PerTargetSkills.TryGetValue(e.TargetId, out Dictionary<int, SkillAgg> value6))
				{
					value6 = new Dictionary<int, SkillAgg>();
					orAdd.PerTargetSkills[e.TargetId] = value6;
				}
				if (!value6.TryGetValue(e.SkillCodeRaw, out var value7))
				{
					value7 = new SkillAgg();
					value6[e.SkillCodeRaw] = value7;
				}
				DamageStatDecision damageStatDecision = value7.StatCounter.Record(e);
				if (damageStatDecision.RetroactivePlainHitsToRemove > 0)
				{
					int num4 = Math.Min(damageStatDecision.RetroactivePlainHitsToRemove, value7.HitCount);
					value7.HitCount -= num4;
					value7.NormalHitCount = Math.Max(0, value7.NormalHitCount - num4);
					orAdd.HitEvents = Math.Max(0, orAdd.HitEvents - num4);
				}
				flag5 = damageStatDecision.CountForStats;
				value7.TotalDamage += num2;
				if (e.SkillLevel > 0)
				{
					value7.SkillLevel = e.SkillLevel;
				}
				if (e.BaseSkillLevel > 0)
				{
					value7.BaseSkillLevel = e.BaseSkillLevel;
				}
				if (flag5)
				{
					value7.HitCount++;
					if (e.IsCrit)
					{
						value7.CritCount++;
					}
					bool flag6 = false;
					if (e.Specials != null)
					{
						if (e.Specials.Contains(SpecialDamage.BACK))
						{
							value7.BackCount++;
							flag6 = true;
						}
						if (e.Specials.Contains(SpecialDamage.DOUBLE))
						{
							value7.DoubleCount++;
							flag6 = true;
						}
						if (e.Specials.Contains(SpecialDamage.PERFECT))
						{
							value7.PerfectCount++;
							flag6 = true;
						}
						if (e.Specials.Contains(SpecialDamage.PARRY))
						{
							value7.ParryCount++;
							flag6 = true;
						}
						if (e.Specials.Contains(SpecialDamage.IMMUNE))
						{
							value7.EvadeCount++;
							flag6 = true;
						}
						if (e.Specials.Contains(SpecialDamage.SMITE))
						{
							value7.SmiteCount++;
							flag6 = true;
						}
					}
					if (!flag6 && IsNoDamageAvoidance(e))
					{
						value7.EvadeCount++;
						flag6 = true;
					}
					if (!flag6 && !e.IsCrit)
					{
						value7.NormalHitCount++;
					}
					if (e.MultiHitDamage > 0)
					{
						value7.MultiEventCount++;
					}
				}
				if (num2 > value7.MaxDamage)
				{
					value7.MaxDamage = (int)num2;
				}
				if (num2 > 0 && num2 < value7.MinDamage)
				{
					value7.MinDamage = (int)num2;
				}
			}
			orAdd.TotalDamage += num2;
			if (flag5)
			{
				orAdd.HitEvents++;
			}
			if (flag5 && e.IsCrit)
			{
				orAdd.CritEvents++;
			}
			if (flag5 && (e.MultiHitCount > 0 || e.MultiHitDamage > 0))
			{
				orAdd.MultiEvents++;
			}
			orAdd.Last = e.TimestampUtc;
		}
		this.DamageAdded?.Invoke(e);
	}

	private static bool IsDpsExcludedHealingSkill(int rawSkillCode)
	{
		if (rawSkillCode <= 0)
		{
			return false;
		}
		return ContainsBaseSkill(DpsExcludedHealingBaseSkillCodes, rawSkillCode);
	}

	private static bool IsDamageEncodedHealingSkill(int rawSkillCode)
	{
		if (rawSkillCode <= 0)
		{
			return false;
		}
		return ContainsBaseSkill(DamageEncodedHealingBaseSkillCodes, rawSkillCode);
	}

	private static bool IsGodstoneDotDamageSkill(int rawSkillCode)
	{
		int num;
		for (num = Math.Abs(rawSkillCode); num >= 10000000; num /= 10)
		{
		}
		if ((uint)(num - 3001015) <= 1u || (uint)(num - 3001115) <= 1u || (uint)(num - 3001215) <= 1u)
		{
			return true;
		}
		return false;
	}

	private static bool ContainsBaseSkill(HashSet<int> baseSkillCodes, int rawSkillCode)
	{
		return baseSkillCodes.Contains(GetSkillFamilyCode(rawSkillCode));
	}

	private static int GetSkillFamilyCode(int rawSkillCode)
	{
		int num;
		for (num = Math.Abs(rawSkillCode); num >= 10000; num /= 10)
		{
		}
		return num;
	}

	private static bool IsNoDamageAvoidance(DamageEvent e)
	{
		if (!e.IsDot && e.Damage <= 0 && e.MultiHitDamage <= 0 && e.HealAmount <= 0)
		{
			if (e.Specials != null)
			{
				return !e.Specials.Contains(SpecialDamage.PARRY);
			}
			return true;
		}
		return false;
	}

	public IReadOnlyList<TargetInfo> GetAllTargets()
	{
		KeyValuePair<int, TargetState>[] array = _targets.ToArray();
		List<TargetInfo> list = new List<TargetInfo>();
		KeyValuePair<int, TargetState>[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			KeyValuePair<int, TargetState> keyValuePair = array2[i];
			TargetState value = keyValuePair.Value;
			if (value.IsBossConfirmed)
			{
				list.Add(new TargetInfo(keyValuePair.Key, _names.GetOrFallback(keyValuePair.Key), value.TotalDamage, value.FirstHit, value.LastHit, value.IsBossConfirmed, value.MobCode, value.MaxHp));
			}
		}
		list.Sort((TargetInfo a, TargetInfo b) => b.TotalDamage.CompareTo(a.TotalDamage));
		return list;
	}

	public bool IsConfirmedBossTarget(int targetId)
	{
		if (targetId > 0 && _targets.TryGetValue(targetId, out TargetState value))
		{
			return value.IsBossConfirmed;
		}
		return false;
	}

	public bool HasOtherConfirmedBossTargetWithDamage(int excludedTargetId)
	{
		foreach (KeyValuePair<int, TargetState> target in _targets)
		{
			TargetState value = target.Value;
			if (target.Key != excludedTargetId && value.IsBossConfirmed && value.TotalDamage > 0)
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasMergeableCharacterIdentity(ActorStats actor)
	{
		string text = actor.Name?.Trim() ?? "";
		string value = actor.ServerName?.Trim() ?? "";
		if (actor.IsMonster || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		if (int.TryParse(text, out var _))
		{
			return false;
		}
		if (!text.StartsWith("Actor ", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("Mob ", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("Mob_", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("Boss ", StringComparison.OrdinalIgnoreCase))
		{
			return !text.Equals("Unknown Player", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static string BuildCharacterIdentityKey(ActorStats actor)
	{
		return actor.ServerName.Trim() + "\u001f" + actor.Name.Trim();
	}

	private static List<ActorStats> MergeDuplicateCharacterStats(IEnumerable<ActorStats> actors, double? fixedDurationSeconds = null)
	{
		List<ActorStats> list = new List<ActorStats>();
		foreach (IGrouping<string, ActorStats> item in actors.GroupBy<ActorStats, string>((ActorStats actor) => (!HasMergeableCharacterIdentity(actor)) ? $"#{actor.ActorId}" : BuildCharacterIdentityKey(actor), StringComparer.OrdinalIgnoreCase))
		{
			List<ActorStats> list2 = item.ToList();
			if (list2.Count == 1)
			{
				list.Add(list2[0]);
				continue;
			}
			ActorStats actorStats = (from actor in list2
				orderby actor.TotalDamage descending, (!(actor.FirstHitUtc == default(DateTime))) ? actor.FirstHitUtc : DateTime.MaxValue
				select actor).First();
			long num = list2.Sum((ActorStats actor) => actor.TotalDamage);
			long num2 = list2.Sum((ActorStats actor) => actor.TotalHealing);
			long selfHealing = list2.Sum((ActorStats actor) => actor.SelfHealing);
			long otherHealing = list2.Sum((ActorStats actor) => actor.OtherHealing);
			int num3 = list2.Sum((ActorStats actor) => actor.Hits);
			int healHits = list2.Sum((ActorStats actor) => actor.HealHits);
			int num4 = list2.Sum((ActorStats actor) => actor.CritHits);
			int multiEvents = list2.Sum((ActorStats actor) => actor.MultiEvents);
			DateTime dateTime = (from actor in list2
				where actor.FirstHitUtc != default(DateTime)
				select actor.FirstHitUtc).DefaultIfEmpty(actorStats.FirstHitUtc).Min();
			DateTime dateTime2 = (from actor in list2
				where actor.LastHitUtc != default(DateTime)
				select actor.LastHitUtc).DefaultIfEmpty(actorStats.LastHitUtc).Max();
			double num5 = Math.Max(1.0, fixedDurationSeconds ?? (dateTime2 - dateTime).TotalSeconds);
			JobClass job = ((actorStats.Job != JobClass.None) ? actorStats.Job : list2.Select((ActorStats actor) => actor.Job).FirstOrDefault((JobClass jobClass) => jobClass != JobClass.None));
			list.Add(actorStats with
			{
				TotalDamage = num,
				TotalHealing = num2,
				SelfHealing = selfHealing,
				OtherHealing = otherHealing,
				Dps = (double)num / num5,
				Hps = (double)num2 / num5,
				Hits = num3,
				HealHits = healHits,
				CritHits = num4,
				CritRate = ((num3 <= 0) ? 0.0 : ((double)num4 / (double)num3)),
				MultiEvents = multiEvents,
				Job = job,
				FirstHitUtc = dateTime,
				LastHitUtc = dateTime2,
				Skills = MergeDuplicateSkillStats(list2.SelectMany((ActorStats actor) => actor.Skills ?? Array.Empty<SkillStats>()))
			});
		}
		return list;
	}

	private static IReadOnlyList<SkillStats> MergeDuplicateSkillStats(IEnumerable<SkillStats> skills)
	{
		return (from skill in (from skill in skills
				group skill by skill.SkillCode).Select(delegate(IGrouping<int, SkillStats> @group)
			{
				List<SkillStats> list = @group.ToList();
				int num = (from skill in list
					where skill.MinDamage > 0
					select skill.MinDamage).DefaultIfEmpty(0).Min();
				return new SkillStats(@group.Key, list.Sum((SkillStats skill) => skill.TotalDamage), TotalHealing: list.Sum((SkillStats skill) => skill.TotalHealing), SelfHealing: list.Sum((SkillStats skill) => skill.SelfHealing), OtherHealing: list.Sum((SkillStats skill) => skill.OtherHealing), HitCount: list.Sum((SkillStats skill) => skill.HitCount), HealCount: list.Sum((SkillStats skill) => skill.HealCount), CritCount: list.Sum((SkillStats skill) => skill.CritCount), NormalHitCount: list.Sum((SkillStats skill) => skill.NormalHitCount), BackCount: list.Sum((SkillStats skill) => skill.BackCount), DoubleCount: list.Sum((SkillStats skill) => skill.DoubleCount), PerfectCount: list.Sum((SkillStats skill) => skill.PerfectCount), ParryCount: list.Sum((SkillStats skill) => skill.ParryCount), MultiEventCount: list.Sum((SkillStats skill) => skill.MultiEventCount), MaxDamage: (list.Count != 0) ? list.Max((SkillStats skill) => skill.MaxDamage) : 0, MinDamage: num, MaxHeal: (list.Count != 0) ? list.Max((SkillStats skill) => skill.MaxHeal) : 0, MinHeal: (from skill in list
					where skill.MinHeal > 0
					select skill.MinHeal).DefaultIfEmpty(0).Min(), SkillLevel: list.Select((SkillStats skill) => skill.SkillLevel).FirstOrDefault((int level) => level > 0), BaseSkillLevel: list.Select((SkillStats skill) => skill.BaseSkillLevel).FirstOrDefault((int level) => level > 0), EvadeCount: list.Sum((SkillStats skill) => skill.EvadeCount), SmiteCount: list.Sum((SkillStats skill) => skill.SmiteCount));
			})
			orderby skill.TotalDamage descending
			select skill).ToList();
	}

	private static SkillStatsBuilder GetOrAddSkillStatsBuilder(Dictionary<int, SkillStatsBuilder> builders, int skillCode)
	{
		if (!builders.TryGetValue(skillCode, out SkillStatsBuilder value))
		{
			value = (builders[skillCode] = new SkillStatsBuilder(skillCode));
		}
		return value;
	}

	public CombatSnapshot? BuildSnapshotForTarget(int targetId)
	{
		DateTime? sessionStart = _sessionStart;
		if (!sessionStart.HasValue)
		{
			return null;
		}
		if (!_targets.TryGetValue(targetId, out TargetState value) || value == null)
		{
			return null;
		}
		double num = Math.Max(1.0, (value.LastHit - value.FirstHit).TotalSeconds);
		KeyValuePair<int, ActorState>[] array = _actors.ToArray();
		List<ActorStats> list = new List<ActorStats>();
		KeyValuePair<int, ActorState>[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			KeyValuePair<int, ActorState> keyValuePair = array2[i];
			ActorState value2 = keyValuePair.Value;
			(long, DateTime, DateTime) value3 = default((long, DateTime, DateTime));
			Dictionary<int, SkillStatsBuilder> dictionary = new Dictionary<int, SkillStatsBuilder>();
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			long num5;
			long num6;
			long num7;
			int num8;
			bool isMonster;
			JobClass jobClass;
			lock (value2)
			{
				if (!value2.PerTarget.TryGetValue(targetId, out value3))
				{
					value3 = (0L, value2.First, value2.Last);
				}
				num5 = 0L;
				num6 = 0L;
				num7 = 0L;
				num8 = 0;
				isMonster = value2.IsMonster;
				jobClass = value2.Job;
				if (value3.Item1 <= 0)
				{
					continue;
				}
				if (value2.PerTargetSkills.TryGetValue(targetId, out Dictionary<int, SkillAgg> value4))
				{
					foreach (KeyValuePair<int, SkillAgg> item3 in value4)
					{
						num2 += item3.Value.HitCount;
						num3 += item3.Value.CritCount;
						num4 += item3.Value.MultiEventCount;
						GetOrAddSkillStatsBuilder(dictionary, item3.Key).AddDamageFrom(item3.Value);
						if (item3.Value.TotalHealing > 0)
						{
							num5 += item3.Value.TotalHealing;
							num6 += item3.Value.SelfHealing;
							num7 += item3.Value.OtherHealing;
							num8 += item3.Value.HealCount;
							GetOrAddSkillStatsBuilder(dictionary, item3.Key).AddHealingFrom(item3.Value);
						}
					}
				}
				goto IL_0211;
			}
			IL_0211:
			(string Name, string ServerName) tuple = SplitCharacterDisplayName(_names.GetOrFallback(keyValuePair.Key));
			string item = tuple.Name;
			string item2 = tuple.ServerName;
			double dps = (double)value3.Item1 / num;
			if (jobClass == JobClass.None && TryGetKnownCharacterJob(item, item2, out var job))
			{
				jobClass = job;
			}
			List<SkillStats> skills = (from builder in dictionary.Values
				select builder.ToSkillStats() into skill
				orderby skill.TotalDamage descending, skill.TotalHealing descending
				select skill).ToList();
			list.Add(new ActorStats(keyValuePair.Key, item, item2, jobClass, value3.Item1, dps, num2, num3, (num2 <= 0) ? 0.0 : ((double)num3 / (double)num2), num4, isMonster, value3.Item2, value3.Item3, skills, num5, (double)num5 / num, num8, num6, num7));
		}
		list = MergeDuplicateCharacterStats(list, num);
		list.Sort(delegate(ActorStats a, ActorStats b)
		{
			int num9 = b.Dps.CompareTo(a.Dps);
			return (num9 == 0) ? b.Hps.CompareTo(a.Hps) : num9;
		});
		string orFallback = _names.GetOrFallback(targetId);
		int topTargetMaxHp = 0;
		int topTargetCurrentHp = -1;
		if (targetId != 0 && _targets.TryGetValue(targetId, out TargetState value5) && value5 != null)
		{
			topTargetMaxHp = value5.MaxHp;
			topTargetCurrentHp = value5.CurrentHp;
		}
		return new CombatSnapshot(value.FirstHit, value.LastHit, value.LastHit - value.FirstHit, list, targetId, orFallback, value.TotalDamage, value.HitCount, value.LastHit - value.FirstHit, value.IsBossEstimated || value.IsBossConfirmed, value.IsBossConfirmed, topTargetMaxHp, topTargetCurrentHp);
	}

	public void BuildSnapshotParallel()
	{
		DateTime? sessionStart = _sessionStart;
		if (!sessionStart.HasValue)
		{
			return;
		}
		DateTime value = _sessionStart.Value;
		DateTime lastEvent = _lastEvent;
		CommitExpiredPendingEvents();
		KeyValuePair<int, ActorState>[] array = _actors.ToArray();
		List<ActorStats> list = new List<ActorStats>(array.Length);
		foreach (KeyValuePair<int, ActorState> keyValuePair in array)
		{
			keyValuePair.Deconstruct(out var key, out var value2);
			int actorId = key;
			ActorState actorState = value2;
			(string Name, string ServerName) tuple = SplitCharacterDisplayName(_names.GetOrFallback(actorId));
			string item = tuple.Name;
			string item2 = tuple.ServerName;
			bool isMonster = false;
			value2 = actorState;
			bool lockTaken = false;
			long totalDamage;
			long totalHealing;
			long selfHealing;
			long otherHealing;
			int hitEvents;
			int healEvents;
			int critEvents;
			int multiEvents;
			JobClass jobClass;
			DateTime first;
			DateTime last;
			try
			{
				Monitor.Enter(value2, ref lockTaken);
				totalDamage = actorState.TotalDamage;
				totalHealing = actorState.TotalHealing;
				selfHealing = actorState.SelfHealing;
				otherHealing = actorState.OtherHealing;
				hitEvents = actorState.HitEvents;
				healEvents = actorState.HealEvents;
				critEvents = actorState.CritEvents;
				multiEvents = actorState.MultiEvents;
				jobClass = actorState.Job;
				first = actorState.First;
				last = actorState.Last;
				isMonster = actorState.IsMonster;
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(value2);
				}
			}
			if (totalDamage > 0 || totalHealing > 0)
			{
				if (jobClass == JobClass.None && TryGetKnownCharacterJob(item, item2, out var job))
				{
					jobClass = job;
				}
				double num = Math.Max(1.0, (last - first).TotalSeconds);
				double dps = (double)totalDamage / num;
				double hps = (double)totalHealing / num;
				double critRate = ((hitEvents <= 0) ? 0.0 : ((double)critEvents / (double)hitEvents));
				list.Add(new ActorStats(actorId, item, item2, jobClass, totalDamage, dps, hitEvents, critEvents, critRate, multiEvents, isMonster, first, last, null, totalHealing, hps, healEvents, selfHealing, otherHealing));
			}
		}
		List<ActorStats> actors = (from x in MergeDuplicateCharacterStats(list)
			orderby x.Dps descending, x.Hps descending
			select x).ToList();
		int num2 = 0;
		long topTargetDamage = 0L;
		int topTargetHits = 0;
		TimeSpan topTargetDuration = TimeSpan.Zero;
		bool isBossActive = false;
		bool isBossConfirmed = false;
		DateTime utcNow = DateTime.UtcNow;
		List<int> list2 = new List<int>();
		TargetSelectionCandidate? targetSelectionCandidate = null;
		foreach (KeyValuePair<int, TargetState> target in _targets)
		{
			TargetState value3 = target.Value;
			bool flag = value3.SuppressUpload && IsTrainingDummyName(_names.GetOrFallback(target.Key));
			if (!flag && value3.IsBossConfirmed && (utcNow - value3.LastHit).TotalSeconds > 10.0)
			{
				if (value3.IsDefeatedEventFired)
				{
					continue;
				}
				double totalSeconds = (value3.LastHit - value3.FirstHit).TotalSeconds;
				bool flag2 = IsCompleteBossFightForUpload(value3, totalSeconds);
				Console.WriteLine($"[BossDeathFallback] BossId={target.Key}(MobCode={value3.MobCode}), Idle={(utcNow - value3.LastHit).TotalSeconds:F1}s, Duration={totalSeconds:F1}s, TotalDmg={value3.TotalDamage}, MaxHp={value3.MaxHp}, uploadReady={flag2}, sawFull={value3.SawFullHpForCurrentEncounter}, sawDeath={value3.SawDeathOrZeroHpForCurrentEncounter}");
				if (flag2)
				{
					RaiseBossEndedIfNeeded(target.Key, value3);
					if (!value3.SuppressUpload)
					{
						OnBossDefeated?.Invoke(target.Key, _names.GetOrFallback(target.Key), value3.FirstHit, value3.LastHit, value3.MobCode, value3.MaxHp);
					}
					value3.IsDefeatedEventFired = true;
				}
			}
			else if (!flag && (utcNow - value3.LastHit).TotalSeconds > 30.0)
			{
				if (!value3.IsBossConfirmed)
				{
					list2.Add(target.Key);
				}
			}
			else
			{
				if (target.Key == 0)
				{
					continue;
				}
				TargetSelectionCandidate value4 = new TargetSelectionCandidate(target.Key, GetTargetSelectionPriority(value3), value3.TotalDamage, value3.HitCount, value3.LastHit - value3.FirstHit, value3.IsBossEstimated || value3.IsBossConfirmed, value3.IsBossConfirmed);
				if (!targetSelectionCandidate.HasValue)
				{
					targetSelectionCandidate = value4;
					continue;
				}
				TargetSelectionCandidate value5 = targetSelectionCandidate.Value;
				if (value4.Priority > value5.Priority || (value4.Priority == value5.Priority && value4.TotalDamage > value5.TotalDamage))
				{
					targetSelectionCandidate = value4;
				}
			}
		}
		foreach (int item3 in list2)
		{
			_targets.TryRemove(item3, out TargetState _);
		}
		if (targetSelectionCandidate.HasValue)
		{
			TargetSelectionCandidate value7 = targetSelectionCandidate.Value;
			num2 = value7.TargetId;
			topTargetDamage = value7.TotalDamage;
			topTargetHits = value7.HitCount;
			topTargetDuration = value7.Duration;
			isBossActive = value7.IsBossActive;
			isBossConfirmed = value7.IsBossConfirmed;
		}
		string topTargetName = ((num2 != 0) ? _names.GetOrFallback(num2) : "");
		int topTargetMaxHp = 0;
		int topTargetCurrentHp = -1;
		if (num2 != 0 && _targets.TryGetValue(num2, out TargetState value8) && value8 != null)
		{
			topTargetMaxHp = value8.MaxHp;
			topTargetCurrentHp = value8.CurrentHp;
		}
		CombatSnapshot value9 = new CombatSnapshot(value, lastEvent, lastEvent - value, actors, num2, topTargetName, topTargetDamage, topTargetHits, topTargetDuration, isBossActive, isBossConfirmed, topTargetMaxHp, topTargetCurrentHp);
		Volatile.Write(ref _latest, value9);
	}

	private void RaiseBossEndedIfNeeded(int targetId, TargetState ts)
	{
		if (!ts.IsEndedEventFired && IsCompleteBossFightForLocalRecord(ts))
		{
			ts.IsEndedEventFired = true;
			OnBossEnded?.Invoke(targetId, _names.GetOrFallback(targetId), ts.FirstHit, ts.LastHit, ts.MobCode, ts.MaxHp);
		}
	}

	private bool IsPlayerOrLocal(string name, int id)
	{
		if (name.Contains("[") || id == _names.LocalPlayerActorId)
		{
			return true;
		}
		if (_actors.TryGetValue(id, out ActorState value) && value.Job != JobClass.None)
		{
			return true;
		}
		return false;
	}

	private void CommitExpiredPendingEvents()
	{
		DateTime now = DateTime.UtcNow;
		foreach (int item in (from kv in _pendingStartTime
			where (now - kv.Value).TotalSeconds >= 5.0
			select kv.Key).ToList())
		{
			TryCommitPendingActor(item, force: true);
		}
	}

	public void FlushPendingPlayerEvents()
	{
		int[] array = _pendingEvents.Keys.ToArray();
		foreach (int actorId in array)
		{
			TryCommitPendingActor(actorId, force: true);
		}
	}

	private void TryCommitPendingActor(int actorId, bool force)
	{
		if (!_pendingEvents.TryGetValue(actorId, out List<DamageEvent> value))
		{
			return;
		}
		DateTime value3;
		if (_names.IsKnownSummon(actorId))
		{
			if (force)
			{
				_pendingEvents.TryRemove(actorId, out List<DamageEvent> _);
				_pendingStartTime.TryRemove(actorId, out value3);
			}
			return;
		}
		List<DamageEvent> list;
		lock (value)
		{
			if (!force && value.Count < 3)
			{
				return;
			}
			list = new List<DamageEvent>(value);
		}
		bool flag = list.Any((DamageEvent e) => IsPlayerSkill(e.SkillCodeRaw));
		string orFallback = _names.GetOrFallback(actorId);
		if (_names.IsMonster(actorId, orFallback))
		{
			flag = false;
		}
		if (flag)
		{
			_confirmedActors[actorId] = 1;
			if (!_pendingEvents.TryRemove(actorId, out List<DamageEvent> value4))
			{
				return;
			}
			_pendingStartTime.TryRemove(actorId, out value3);
			{
				foreach (DamageEvent item in value4)
				{
					ProcessDamageInternal(item);
				}
				return;
			}
		}
		if ((force || list.Count >= 3) && _pendingEvents.TryRemove(actorId, out List<DamageEvent> value5) && value5.Count > 0)
		{
			_pendingStartTime.TryRemove(actorId, out value3);
			this.PacketLogEvent?.Invoke(value5[0], $"???熬곥굥????????嚥▲굧?????꿔꺂?????녿쫯????嶺뚮????{actorId})");
		}
	}

	private static bool IsPlayerSkill(int skillCode)
	{
		if ((skillCode < 11000000 || skillCode > 19999999) && (skillCode < 3000000 || skillCode > 3999999))
		{
			if (skillCode >= 100000)
			{
				return skillCode <= 199999;
			}
			return false;
		}
		return true;
	}

	public DateTime? GetLastBossEventTime(int targetId)
	{
		if (_targets.TryGetValue(targetId, out TargetState value) && value.IsBossConfirmed)
		{
			return value.LastHit;
		}
		return null;
	}
}
