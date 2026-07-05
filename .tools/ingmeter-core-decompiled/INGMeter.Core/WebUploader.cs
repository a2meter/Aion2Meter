using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace INGMeter.Core;

public class WebUploader
{
	private sealed record PreparedCombatLog(string Body, string Encoding, int OriginalBytes, int StoredBytes);

	private sealed record UploadResponse(int StatusCode, bool IsSuccessStatusCode, string Body);

	private sealed record BossUploadServerResult(bool? Success, string Status, string Message, bool? CombatLogSaved, bool? CombatLogReceived)
	{
		public bool IsIgnored => Status.StartsWith("ignored_", StringComparison.OrdinalIgnoreCase);

		public bool IsAccepted
		{
			get
			{
				if (Success != false)
				{
					return !IsIgnored;
				}
				return false;
			}
		}
	}

	private sealed record AbyssArtifactUploadState(int AreaCode, int ArtifactId, int OwnerSide, int OwnerServerId, int MatchServer1Id, int MatchServer2Id, DateTime TimestampUtc);

	private readonly HttpClient _http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(15L)
	};

	private readonly MeterEngine _engine;

	private readonly object _abyssArtifactLock = new object();

	private readonly object _bossUploadLock = new object();

	private readonly Dictionary<string, AbyssArtifactUploadState> _pendingAbyssArtifactStates = new Dictionary<string, AbyssArtifactUploadState>();

	private readonly HashSet<string> _activeBossUploadKeys = new HashSet<string>(StringComparer.Ordinal);

	private bool _abyssArtifactFlushQueued;

	private static readonly string AppVersion = ResolveAppVersion();

	private const int BossUploadMaxAttempts = 3;

	private const int BossUploadMaxParticipants = 10;

	private static readonly TimeSpan[] BossUploadRetryDelays = new TimeSpan[3]
	{
		TimeSpan.Zero,
		TimeSpan.FromSeconds(10L),
		TimeSpan.FromMinutes(1L)
	};

	public string EndpointUrl { get; set; } = WebEndpoint.Url("/aion2data/aion2_upload.php");

	public string AbyssArtifactEndpointUrl { get; set; } = WebEndpoint.Url("/aion2data/aion2_abyss_artifacts.php");

	public int CurrentContentCode { get; set; }

	private static string UploadLogPath => RuntimePaths.GetLogFilePath("upload_error.log");

	public event Action<string>? UploadSuccess;

	public WebUploader(MeterEngine engine)
	{
		_engine = engine;
		_engine.BossDefeated += OnBossDefeated;
		_engine.AbyssArtifactStateReceived += OnAbyssArtifactState;
	}

	private void OnAbyssArtifactState(AbyssArtifactStateEvent state)
	{
		if (string.IsNullOrWhiteSpace(AbyssArtifactEndpointUrl) || !IsValidAionServerId(state.MatchServer1Id) || !IsValidAionServerId(state.MatchServer2Id) || state.MatchServer1Id == state.MatchServer2Id || state.AreaCode <= 0 || state.ArtifactId <= 0 || state.OwnerSide < 0 || state.OwnerSide > 2)
		{
			return;
		}
		AbyssArtifactUploadState abyssArtifactUploadState = new AbyssArtifactUploadState(state.AreaCode, state.ArtifactId, state.OwnerSide, state.OwnerServerId, state.MatchServer1Id, state.MatchServer2Id, state.TimestampUtc);
		lock (_abyssArtifactLock)
		{
			_pendingAbyssArtifactStates[AbyssArtifactKey(abyssArtifactUploadState)] = abyssArtifactUploadState;
			if (_abyssArtifactFlushQueued)
			{
				return;
			}
			_abyssArtifactFlushQueued = true;
		}
		Task.Run((Func<Task?>)FlushAbyssArtifactStatesAsync);
	}

	private async Task FlushAbyssArtifactStatesAsync()
	{
		_ = 1;
		try
		{
			await Task.Delay(1000);
			AbyssArtifactUploadState[] array;
			lock (_abyssArtifactLock)
			{
				array = _pendingAbyssArtifactStates.Values.ToArray();
				_pendingAbyssArtifactStates.Clear();
				_abyssArtifactFlushQueued = false;
			}
			if (array.Length != 0)
			{
				await UploadAbyssArtifactStatesAsync(array);
			}
		}
		catch (Exception value)
		{
			lock (_abyssArtifactLock)
			{
				_abyssArtifactFlushQueued = false;
			}
			try
			{
				File.AppendAllText(UploadLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] abyss artifact upload failed: {value}{Environment.NewLine}");
			}
			catch
			{
			}
		}
	}

	private async Task UploadAbyssArtifactStatesAsync(IReadOnlyCollection<AbyssArtifactUploadState> states)
	{
		AbyssArtifactUploadState abyssArtifactUploadState = states.First();
		string content = JsonSerializer.Serialize(new
		{
			api_key = "ing_meter_secret_2026",
			app_version = AppVersion,
			observed_at = states.Max((AbyssArtifactUploadState s) => s.TimestampUtc).ToString("O"),
			area_code = abyssArtifactUploadState.AreaCode,
			match_server1_id = abyssArtifactUploadState.MatchServer1Id,
			match_server2_id = abyssArtifactUploadState.MatchServer2Id,
			states = (from s in states
				orderby s.AreaCode, s.ArtifactId
				select new
				{
					area_code = s.AreaCode,
					artifact_id = s.ArtifactId,
					owner_side = s.OwnerSide,
					owner_server_id = s.OwnerServerId,
					match_server1_id = s.MatchServer1Id,
					match_server2_id = s.MatchServer2Id
				}).ToArray()
		});
		using StringContent content2 = new StringContent(content, Encoding.UTF8, "application/json");
		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(8L));
		using HttpResponseMessage response = await _http.PostAsync(WebEndpoint.Route(AbyssArtifactEndpointUrl), content2, cts.Token);
		string value = await response.Content.ReadAsStringAsync();
		if (!response.IsSuccessStatusCode)
		{
			try
			{
				File.AppendAllText(UploadLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] abyss status={(int)response.StatusCode}, body={value}{Environment.NewLine}");
				return;
			}
			catch
			{
				return;
			}
		}
	}

	private void OnBossDefeated(int bossActorId, string bossName, DateTime firstHit, DateTime lastHit, int mobCode, int maxHp)
	{
		if (_engine.IsExcludedBossMobCode(mobCode))
		{
			Console.WriteLine($"[WebUploader] 제외된 보스 몹코드라 업로드를 스킵합니다. mobCode={mobCode}, boss={bossName}");
			return;
		}
		if (_engine.IsLogViewing || _engine.SuppressUploadsForCurrentSession)
		{
			Console.WriteLine("[WebUploader] 로그 보기 중 발생한 보스 처치 이벤트라 업로드를 스킵합니다.");
			return;
		}
		CombatSnapshot preservedSnapshot = _engine.BuildSnapshotForTarget(bossActorId);
		if (preservedSnapshot == null || preservedSnapshot.Actors.Count == 0)
		{
			return;
		}
		string finalBossName = ResolveBossNameForUpload(bossActorId, bossName, mobCode);
		(string Json, int EventCount)? preservedEncounterLog = _engine.BuildEncounterLogJson(firstHit, lastHit, bossActorId, finalBossName, mobCode, maxHp);
		Task.Run(async delegate
		{
			_ = 1;
			try
			{
				if (!_engine.IsLogViewing && !_engine.SuppressUploadsForCurrentSession)
				{
					await Task.Delay(1500);
					if (!_engine.IsLogViewing && !_engine.SuppressUploadsForCurrentSession)
					{
						CombatSnapshot combatSnapshot = _engine.BuildSnapshotForTarget(bossActorId) ?? preservedSnapshot;
						if (!(combatSnapshot == null) && combatSnapshot.Actors.Count != 0)
						{
							DateTime valueOrDefault = _engine.GetLastBossEventTime(bossActorId).GetValueOrDefault(lastHit);
							(string, int)? encounterLog = _engine.BuildEncounterLogJson(firstHit, valueOrDefault, bossActorId, finalBossName, mobCode, maxHp) ?? preservedEncounterLog;
							double totalSeconds = (valueOrDefault - firstHit).TotalSeconds;
							if (totalSeconds < 20.0)
							{
								Console.WriteLine($"[WebUploader] 보스 전투 시간({totalSeconds:F1}초)이 20초 미만이라 업로드를 스킵합니다. (보스: {bossName})");
							}
							else
							{
								await UploadBossLogAsync(bossActorId, bossName, mobCode, maxHp, firstHit, valueOrDefault, combatSnapshot, encounterLog);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("[WebUploader] 보스 전송 실패: " + ex.Message);
				try
				{
					File.AppendAllText(UploadLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 웹 업로드 실패: {ex}\n");
				}
				catch
				{
				}
			}
		});
	}

	private async Task UploadBossLogAsync(int bossActorId, string bossName, int mobCode, int maxHp, DateTime firstHit, DateTime lastHit, CombatSnapshot snap, (string Json, int EventCount)? encounterLog)
	{
		if (string.IsNullOrWhiteSpace(EndpointUrl))
		{
			return;
		}
		List<ActorStats> validActors = (from a in snap.Actors.Where(MeterEngine.IsEncounterParticipantActor)
			orderby a.TotalDamage descending, a.Dps descending
			select a).Take(10).ToList();
		if (validActors.Count == 0)
		{
			Console.WriteLine("[WebUploader] 유효한 참가자 정보가 없어 보스 전투 기록을 전송하지 않습니다.");
			return;
		}
		long validTotalDamage = validActors.Sum((ActorStats a) => a.TotalDamage);
		long validTotalHealing = validActors.Sum((ActorStats a) => a.TotalHealing);
		IReadOnlyDictionary<int, EncounterSupportMetrics> supportMetrics = _engine.BuildEncounterSupportMetrics(snap);
		string uploaderName = _engine.LocalPlayerName ?? "Unknown Player";
		ActorStats actorStats = snap.Actors.FirstOrDefault((ActorStats a) => a.Name == uploaderName);
		string uploaded_by = ((actorStats != null && !string.IsNullOrWhiteSpace(actorStats.ServerName)) ? (uploaderName + "[" + actorStats.ServerName + "]") : uploaderName);
		string finalBossName = ResolveBossNameForUpload(bossActorId, bossName, mobCode);
		if (!encounterLog.HasValue)
		{
			try
			{
				File.AppendAllText(UploadLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] memory combat log empty, boss={finalBossName}, firstHit={firstHit:O}, lastHit={lastHit:O}{Environment.NewLine}");
			}
			catch
			{
			}
		}
		PreparedCombatLog preparedCombatLog = PrepareEncounterLog(encounterLog?.Json);
		long num = Math.Max(0L, (long)Math.Round((lastHit - firstHit).TotalMilliseconds));
		int duration_sec = ((num > 0) ? Math.Max(1, (int)Math.Round((double)num / 1000.0)) : 0);
		double uploadDurationSeconds = ((num > 0) ? Math.Max(1.0, (double)num / 1000.0) : 0.0);
		string text = JsonSerializer.Serialize(new
		{
			api_key = "ing_meter_secret_2026",
			app_version = AppVersion,
			boss_actor_id = bossActorId,
			boss_mob_code = mobCode,
			boss_name = finalBossName,
			content_code = ((CurrentContentCode > 0) ? new int?(CurrentContentCode) : ((int?)null)),
			boss_max_hp = maxHp,
			end_time = ToUtcIsoString(lastHit),
			duration_sec = duration_sec,
			duration_ms = num,
			total_damage = validTotalDamage,
			total_healing = validTotalHealing,
			combat_log_format = ((preparedCombatLog != null) ? "compact-json-v4" : null),
			combat_log_encoding = preparedCombatLog?.Encoding,
			combat_log_json = preparedCombatLog?.Body,
			combat_log_event_count = (encounterLog?.EventCount ?? 0),
			combat_log_original_bytes = (preparedCombatLog?.OriginalBytes ?? 0),
			combat_log_stored_bytes = (preparedCombatLog?.StoredBytes ?? 0),
			uploaded_by = uploaded_by,
			participants = validActors.Select(delegate(ActorStats a)
			{
				int aion2ServerId = PartyTracker.GetAion2ServerId(a.ServerName);
				int combat_score = 0;
				int charNo = 0;
				if (aion2ServerId > 0 && !string.IsNullOrWhiteSpace(a.Name))
				{
					if (_engine.TryGetCombatPower(a.Name, aion2ServerId, out var combatPower))
					{
						combat_score = combatPower;
					}
					_engine.TryGetCharNo(a.ActorId, a.Name, aion2ServerId, out charNo);
				}
				supportMetrics.TryGetValue(a.ActorId, out EncounterSupportMetrics value3);
				string race = ((aion2ServerId >= 1000 && aion2ServerId < 2000) ? "Elyos" : ((aion2ServerId >= 2000 && aion2ServerId < 3000) ? "Asmodian" : "Unknown"));
				return new
				{
					server_name = a.ServerName,
					server_id = aion2ServerId,
					character_name = a.Name,
					char_no = charNo,
					combat_score = combat_score,
					job_class = NormalizeUploadJobClass(a.Job),
					race = race,
					damage = a.TotalDamage,
					healing = a.TotalHealing,
					self_healing = a.SelfHealing,
					other_healing = a.OtherHealing,
					dps = ((uploadDurationSeconds > 0.0) ? ((double)a.TotalDamage / uploadDurationSeconds) : a.Dps),
					hps = ((uploadDurationSeconds > 0.0) ? ((double)a.TotalHealing / uploadDurationSeconds) : a.Hps),
					ndps = value3?.Ndps,
					rdps = value3?.Rdps,
					rdps_added = value3?.AddedDps,
					rdps_reduced = value3?.ReducedDps,
					damage_percent = ((validTotalDamage > 0) ? ((double)a.TotalDamage / (double)validTotalDamage * 100.0) : 0.0),
					healing_percent = ((validTotalHealing > 0) ? ((double)a.TotalHealing / (double)validTotalHealing * 100.0) : 0.0),
					hits = a.Hits,
					heal_hits = a.HealHits,
					crits = a.CritHits,
					crit_rate = a.CritRate,
					multi_events = a.MultiEvents,
					skills = a.Skills?.Select((SkillStats s) => new
					{
						code = s.SkillCode,
						dmg = s.TotalDamage,
						heal = s.TotalHealing,
						self_heal = s.SelfHealing,
						other_heal = s.OtherHealing,
						hit = s.HitCount,
						heal_hit = s.HealCount,
						crit = s.CritCount,
						normal = s.NormalHitCount,
						back = s.BackCount,
						dbl = s.DoubleCount,
						perfect = s.PerfectCount,
						parry = s.ParryCount,
						evade = s.EvadeCount,
						multi = s.MultiEventCount,
						max = s.MaxDamage,
						min = s.MinDamage,
						max_heal = s.MaxHeal,
						min_heal = s.MinHeal,
						level = s.SkillLevel,
						base_level = s.BaseSkillLevel
					}).ToArray()
				};
			}).ToArray()
		});
		int payloadBytes = Encoding.UTF8.GetByteCount(text);
		string uploadKey = BuildBossUploadKey(bossActorId, mobCode, maxHp, firstHit);
		if (!TryBeginBossUpload(uploadKey))
		{
			AppendUploadRetryLog(finalBossName, payloadBytes, 0, "duplicate in-flight upload skipped");
			return;
		}
		UploadResponse uploadResponse;
		try
		{
			uploadResponse = await PostBossPayloadWithRetryAsync(WebEndpoint.Route(EndpointUrl), text, payloadBytes, finalBossName);
		}
		finally
		{
			EndBossUpload(uploadKey);
		}
		if (!uploadResponse.IsSuccessStatusCode)
		{
			try
			{
				File.AppendAllText(UploadLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] status={uploadResponse.StatusCode}, bytes={payloadBytes}, boss={finalBossName}, body={uploadResponse.Body}{Environment.NewLine}");
			}
			catch
			{
			}
		}
		if (!uploadResponse.IsSuccessStatusCode)
		{
			Console.WriteLine($"[WebUploader] 서버 응답 오류 {uploadResponse.StatusCode}: {uploadResponse.Body}");
			return;
		}
		BossUploadServerResult bossUploadServerResult = ParseBossUploadServerResult(uploadResponse.Body);
		if (!bossUploadServerResult.IsAccepted)
		{
			string value = (string.IsNullOrWhiteSpace(bossUploadServerResult.Status) ? "unknown" : bossUploadServerResult.Status);
			string value2 = (string.IsNullOrWhiteSpace(bossUploadServerResult.Message) ? uploadResponse.Body : bossUploadServerResult.Message);
			Console.WriteLine($"[WebUploader] 업로드 저장 제외: {finalBossName} | status={value} | {value2}");
			try
			{
				File.AppendAllText(UploadLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] upload ignored, status={value}, bytes={payloadBytes}, boss={finalBossName}, body={uploadResponse.Body}{Environment.NewLine}");
				return;
			}
			catch
			{
				return;
			}
		}
		Console.WriteLine($"[WebUploader] 업로드 성공: {finalBossName} | {(int)(lastHit - firstHit).TotalSeconds}초 | {validActors.Count}명 | 응답: {uploadResponse.Body}");
		if (bossUploadServerResult.CombatLogSaved == false || bossUploadServerResult.CombatLogReceived == false)
		{
			try
			{
				File.AppendAllText(UploadLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] combat log not saved, boss={finalBossName}, bytes={payloadBytes}, response={uploadResponse.Body}{Environment.NewLine}");
			}
			catch
			{
			}
		}
		this.UploadSuccess?.Invoke(finalBossName);
	}

	private static BossUploadServerResult ParseBossUploadServerResult(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return new BossUploadServerResult(null, "", "", null, null);
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(body);
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
			{
				return new BossUploadServerResult(null, "", "", null, null);
			}
			JsonElement rootElement = jsonDocument.RootElement;
			return new BossUploadServerResult(TryGetBool(rootElement, "success"), TryGetString(rootElement, "status"), TryGetString(rootElement, "message", "error"), TryGetBool(rootElement, "combat_log_saved"), TryGetBool(rootElement, "combat_log_received"));
		}
		catch
		{
			return new BossUploadServerResult(null, "", "", null, null);
		}
	}

	private static string TryGetString(JsonElement root, params string[] propertyNames)
	{
		foreach (string propertyName in propertyNames)
		{
			if (root.TryGetProperty(propertyName, out var value))
			{
				if (value.ValueKind != JsonValueKind.String)
				{
					return value.ToString();
				}
				return value.GetString() ?? "";
			}
		}
		return "";
	}

	private static bool? TryGetBool(JsonElement root, string propertyName)
	{
		if (!root.TryGetProperty(propertyName, out var value))
		{
			return null;
		}
		switch (value.ValueKind)
		{
		case JsonValueKind.True:
			return true;
		case JsonValueKind.False:
			return false;
		case JsonValueKind.String:
		{
			if (bool.TryParse(value.GetString(), out var result))
			{
				return result;
			}
			break;
		}
		case JsonValueKind.Number:
		{
			if (value.TryGetInt32(out var value2))
			{
				return value2 != 0;
			}
			break;
		}
		}
		return null;
	}

	private string ResolveBossNameForUpload(int bossActorId, string bossName, int mobCode)
	{
		if (mobCode > 0)
		{
			string text = $"Mob_{mobCode}";
			if (string.Equals(bossName, text, StringComparison.OrdinalIgnoreCase))
			{
				return text;
			}
			string text2 = _engine.ResolveMobName?.Invoke(mobCode) ?? "";
			if (!IsResolvedMobName(text2))
			{
				return text;
			}
			return text2;
		}
		if (!string.IsNullOrWhiteSpace(bossName))
		{
			return bossName;
		}
		return $"Actor {bossActorId}";
	}

	private static bool IsResolvedMobName(string name)
	{
		if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("Mob_", StringComparison.OrdinalIgnoreCase))
		{
			return !name.StartsWith("Mob ", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static int NormalizeUploadJobClass(JobClass job)
	{
		if (job < JobClass.Gladiator || job > JobClass.Brawler)
		{
			return 0;
		}
		return (int)job;
	}

	private static string BuildBossUploadKey(int bossActorId, int mobCode, int maxHp, DateTime firstHit)
	{
		return $"{Math.Max(0, mobCode)}:{Math.Max(0, bossActorId)}:{Math.Max(0, maxHp)}:{firstHit.ToUniversalTime().Ticks}";
	}

	private bool TryBeginBossUpload(string key)
	{
		lock (_bossUploadLock)
		{
			return _activeBossUploadKeys.Add(key);
		}
	}

	private void EndBossUpload(string key)
	{
		lock (_bossUploadLock)
		{
			_activeBossUploadKeys.Remove(key);
		}
	}

	private async Task<UploadResponse> PostBossPayloadWithRetryAsync(string url, string json, int payloadBytes, string bossName)
	{
		Exception lastException = null;
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			TimeSpan timeSpan = ((attempt <= BossUploadRetryDelays.Length) ? BossUploadRetryDelays[attempt - 1] : BossUploadRetryDelays[^1]);
			if (timeSpan > TimeSpan.Zero)
			{
				await Task.Delay(timeSpan);
			}
			try
			{
				using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
				using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10L));
				using HttpResponseMessage response = await _http.PostAsync(url, content, cts.Token);
				string text = await response.Content.ReadAsStringAsync();
				int statusCode = (int)response.StatusCode;
				if (response.IsSuccessStatusCode || !IsTransientUploadStatus(statusCode) || attempt >= 3)
				{
					return new UploadResponse(statusCode, response.IsSuccessStatusCode, text);
				}
				AppendUploadRetryLog(bossName, payloadBytes, attempt, $"status={statusCode}, body={text}");
			}
			catch (Exception ex) when (IsTransientUploadException(ex))
			{
				lastException = ex;
				if (attempt >= 3)
				{
					return new UploadResponse(0, IsSuccessStatusCode: false, ex.Message);
				}
				AppendUploadRetryLog(bossName, payloadBytes, attempt, ex.Message);
			}
		}
		throw lastException ?? new HttpRequestException("Boss upload failed after retries.");
	}

	private static bool IsTransientUploadStatus(int statusCode)
	{
		if (statusCode != 408 && statusCode != 429 && statusCode != 500 && statusCode != 502 && statusCode != 503 && statusCode != 504)
		{
			if (statusCode >= 521)
			{
				return statusCode <= 524;
			}
			return false;
		}
		return true;
	}

	private static bool IsTransientUploadException(Exception ex)
	{
		if (ex is HttpRequestException || ex is TaskCanceledException || ex is TimeoutException)
		{
			return true;
		}
		return false;
	}

	private static void AppendUploadRetryLog(string bossName, int payloadBytes, int attempt, string reason)
	{
		try
		{
			string value = ((attempt <= 0) ? "upload skipped" : $"retry upload attempt={attempt + 1}/{3}");
			File.AppendAllText(UploadLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {value}, bytes={payloadBytes}, boss={bossName}, reason={reason}{Environment.NewLine}");
		}
		catch
		{
		}
	}

	private static bool IsValidAionServerId(int serverId)
	{
		if (serverId < 1001 || serverId > 1021)
		{
			if (serverId >= 2001)
			{
				return serverId <= 2021;
			}
			return false;
		}
		return true;
	}

	private static string AbyssArtifactKey(AbyssArtifactUploadState state)
	{
		int value = Math.Min(state.MatchServer1Id, state.MatchServer2Id);
		int value2 = Math.Max(state.MatchServer1Id, state.MatchServer2Id);
		return $"{state.AreaCode}:{value}:{value2}:{state.ArtifactId}";
	}

	private static PreparedCombatLog? PrepareEncounterLog(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}
		byte[] bytes = Encoding.UTF8.GetBytes(json);
		using MemoryStream memoryStream = new MemoryStream();
		using (GZipStream gZipStream = new GZipStream(memoryStream, CompressionLevel.SmallestSize, leaveOpen: true))
		{
			gZipStream.Write(bytes, 0, bytes.Length);
		}
		byte[] array = memoryStream.ToArray();
		return new PreparedCombatLog(Convert.ToBase64String(array), "gzip+base64", bytes.Length, array.Length);
	}

	private static string ResolveAppVersion()
	{
		try
		{
			Version version = Assembly.GetEntryAssembly()?.GetName().Version ?? Assembly.GetExecutingAssembly().GetName().Version;
			if (version != null)
			{
				return version.ToString(3);
			}
		}
		catch
		{
		}
		return "0.0.0";
	}

	private static string ToUtcIsoString(DateTime value)
	{
		return ((value.Kind == DateTimeKind.Utc) ? value : value.ToUniversalTime()).ToString("O");
	}
}
