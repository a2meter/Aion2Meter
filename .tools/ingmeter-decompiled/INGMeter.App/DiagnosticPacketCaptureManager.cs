using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using INGMeter.Capture;
using INGMeter.Core;

namespace INGMeter.App;

internal sealed class DiagnosticPacketCaptureManager : IDisposable
{
	private sealed record RequestResponse([property: JsonPropertyName("requests")] List<PacketCaptureRequest>? Requests);

	private sealed record PacketCaptureRequest([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("mob_code")] int MobCode, [property: JsonPropertyName("pre_seconds")] int PreSeconds, [property: JsonPropertyName("capture_seconds")] int CaptureSeconds, [property: JsonPropertyName("remaining_samples")] int RemainingSamples)
	{
		public PacketCaptureRequest Normalized()
		{
			return this with
			{
				PreSeconds = Math.Clamp(PreSeconds, 0, 15),
				CaptureSeconds = Math.Clamp((CaptureSeconds <= 0) ? 10 : CaptureSeconds, 1, 600),
				RemainingSamples = Math.Clamp(RemainingSamples, 0, 100)
			};
		}
	}

	private sealed record PacketSnapshot(long Index, DateTime TimestampUtc, int SrcPort, int DstPort, uint Seq, bool IsPsh, string Device, string SrcIp, string DstIp, byte[] Payload);

	private sealed record CaptureTrigger(DateTime TimestampUtc, object Payload);

	private sealed class CaptureSession
	{
		public PacketCaptureRequest Request { get; }

		public CaptureTrigger Trigger { get; }

		public DateTime EndUtc { get; }

		public List<PacketSnapshot> Packets { get; }

		private long PayloadBytes { get; set; }

		public CaptureSession(PacketCaptureRequest request, CaptureTrigger trigger, DateTime endUtc, List<PacketSnapshot> packets)
		{
			Request = request;
			Trigger = trigger;
			EndUtc = endUtc;
			Packets = packets;
			PayloadBytes = ((IEnumerable<PacketSnapshot>)packets).Sum((Func<PacketSnapshot, long>)((PacketSnapshot p) => p.Payload.Length));
		}

		public bool TryAdd(PacketSnapshot packet)
		{
			if (Packets.Count >= 60000 || PayloadBytes + packet.Payload.Length > 33554432)
			{
				return false;
			}
			Packets.Add(packet);
			PayloadBytes += packet.Payload.Length;
			return true;
		}
	}

	private const string RequestsPath = "/aion2data/packet_capture_requests.php";

	private const string UploadPath = "/aion2data/packet_capture_upload.php";

	private const string ApiKey = "ing_meter_secret_2026";

	private const int MaxRingBytes = 8388608;

	private const int MaxCapturePayloadBytes = 33554432;

	private const int MaxUploadPackets = 60000;

	private const int MaxCaptureSeconds = 600;

	private const int LocalUserInfoCaptureMobCode = 0;

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(60L)
	};

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly object _gate = new object();

	private readonly Queue<PacketSnapshot> _ring = new Queue<PacketSnapshot>();

	private readonly List<CaptureSession> _sessions = new List<CaptureSession>();

	private List<PacketCaptureRequest> _requests = new List<PacketCaptureRequest>();

	private long _ringBytes;

	private long _sequence;

	private bool _hasRequests;

	private bool _disposed;

	public async Task RefreshRequestsAsync()
	{
		if (_disposed)
		{
			return;
		}
		try
		{
			string content = JsonSerializer.Serialize(new
			{
				api_key = "ing_meter_secret_2026",
				app_version = GetAppVersion()
			});
			using StringContent content2 = new StringContent(content, Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await Http.PostAsync(WebEndpoint.Url("/aion2data/packet_capture_requests.php"), content2).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				ClearRequests();
				return;
			}
			List<PacketCaptureRequest> requests = (from r in JsonSerializer.Deserialize<RequestResponse>(await response.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false), JsonOptions)?.Requests?.Where((PacketCaptureRequest r) => r.Id > 0 && r.MobCode >= 0 && r.RemainingSamples > 0)
				select r.Normalized()).ToList() ?? new List<PacketCaptureRequest>();
			lock (_gate)
			{
				_requests = requests;
				_hasRequests = _requests.Count > 0;
				if (!_hasRequests)
				{
					_ring.Clear();
					_sessions.Clear();
					_ringBytes = 0L;
				}
			}
		}
		catch
		{
			ClearRequests();
		}
	}

	public void Observe(TcpPayload packet)
	{
		if (!_hasRequests || _disposed || packet.Payload.Length == 0)
		{
			return;
		}
		List<CaptureSession> list = new List<CaptureSession>();
		PacketSnapshot packetSnapshot = new PacketSnapshot(++_sequence, packet.TimestampUtc, packet.NormalizedSrcPort, packet.NormalizedDstPort, packet.SeqNum, packet.IsPsh, "WinDivert", packet.SrcIp.ToString(), packet.DstIp.ToString(), packet.Payload);
		lock (_gate)
		{
			if (!_hasRequests)
			{
				return;
			}
			_ring.Enqueue(packetSnapshot);
			_ringBytes += packetSnapshot.Payload.Length;
			PruneRingLocked(packet.TimestampUtc);
			foreach (CaptureSession session in _sessions)
			{
				if (packetSnapshot.TimestampUtc <= session.EndUtc)
				{
					session.TryAdd(packetSnapshot);
				}
			}
			list.AddRange(TakeCompletedSessionsLocked(packet.TimestampUtc));
		}
		UploadSessions(list);
	}

	public void OnMobSpawn(MobSpawnObservedEvent info)
	{
		StartCaptureSessions(info.MobCode, new CaptureTrigger(info.TimestampUtc, new
		{
			kind = "mob_spawn",
			mob_id = info.MobId,
			mob_code = info.MobCode,
			hp = info.Hp,
			raw_hp = info.RawHp,
			extra1 = info.Extra1,
			extra2 = info.Extra2,
			state_marker = info.StateMarker
		}));
	}

	public void OnLocalUserInfo(LocalUserInfoObservedEvent info)
	{
		StartCaptureSessions(0, new CaptureTrigger(info.TimestampUtc, new
		{
			kind = "local_user_info",
			entity_id = info.EntityId,
			nickname = info.Nickname,
			server_id = info.ServerId,
			job_code = info.JobCode,
			extra = info.Extra,
			character_number = info.CharacterNumber
		}));
	}

	private void StartCaptureSessions(int requestMobCode, CaptureTrigger trigger)
	{
		if (!_hasRequests || _disposed)
		{
			return;
		}
		List<CaptureSession> list = new List<CaptureSession>();
		List<CaptureSession> list2 = new List<CaptureSession>();
		lock (_gate)
		{
			if (!_hasRequests)
			{
				return;
			}
			foreach (PacketCaptureRequest request in _requests.Where((PacketCaptureRequest r) => r.MobCode == requestMobCode))
			{
				if (!_sessions.Any((CaptureSession s) => s.Request.Id == request.Id))
				{
					DateTime startUtc = trigger.TimestampUtc.AddSeconds(-request.PreSeconds);
					DateTime dateTime = trigger.TimestampUtc.AddSeconds(request.CaptureSeconds);
					List<PacketSnapshot> packets = TakeTailWithinByteLimit(_ring.Where((PacketSnapshot p) => p.TimestampUtc >= startUtc && p.TimestampUtc <= trigger.TimestampUtc).TakeLast(60000));
					CaptureSession item = new CaptureSession(request, trigger, dateTime, packets);
					if (dateTime <= DateTime.UtcNow)
					{
						list.Add(item);
						continue;
					}
					_sessions.Add(item);
					list2.Add(item);
				}
			}
		}
		foreach (CaptureSession item2 in list2)
		{
			CompleteLaterAsync(item2);
		}
		UploadSessions(list);
	}

	private async Task CompleteLaterAsync(CaptureSession session)
	{
		try
		{
			TimeSpan timeSpan = session.EndUtc - DateTime.UtcNow + TimeSpan.FromMilliseconds(500L);
			if (timeSpan > TimeSpan.Zero)
			{
				await Task.Delay(timeSpan).ConfigureAwait(continueOnCapturedContext: false);
			}
			CaptureSession captureSession = null;
			lock (_gate)
			{
				if (_sessions.Remove(session))
				{
					captureSession = session;
				}
			}
			if (captureSession != null)
			{
				UploadSessions(new CaptureSession[1] { captureSession });
			}
		}
		catch
		{
		}
	}

	private void UploadSessions(IEnumerable<CaptureSession> sessions)
	{
		foreach (CaptureSession session in sessions)
		{
			UploadSessionAsync(session);
		}
	}

	private async Task UploadSessionAsync(CaptureSession session)
	{
		if (session.Packets.Count == 0 || _disposed)
		{
			return;
		}
		try
		{
			string s = BuildJsonl(session.Packets);
			byte[] bytes = Encoding.UTF8.GetBytes(s);
			byte[] array = Gzip(bytes);
			string sha = Convert.ToHexString(SHA256.HashData(array)).ToLowerInvariant();
			string appVersion = GetAppVersion();
			string content = JsonSerializer.Serialize(new
			{
				api_key = "ing_meter_secret_2026",
				app_version = appVersion,
				request_id = session.Request.Id,
				mob_code = session.Request.MobCode,
				observed_at_utc = session.Trigger.TimestampUtc,
				packet_count = session.Packets.Count,
				original_bytes = bytes.Length,
				gzip_bytes = array.Length,
				sha256 = sha,
				payload_gzip_base64 = Convert.ToBase64String(array),
				trigger = session.Trigger.Payload
			});
			using StringContent content2 = new StringContent(content, Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await Http.PostAsync(WebEndpoint.Url("/aion2data/packet_capture_upload.php"), content2).ConfigureAwait(continueOnCapturedContext: false);
			if (response.IsSuccessStatusCode)
			{
				await RefreshRequestsAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch
		{
		}
	}

	private void PruneRingLocked(DateTime nowUtc)
	{
		int num = ((_requests.Count != 0) ? _requests.Max((PacketCaptureRequest r) => r.PreSeconds) : 0);
		DateTime dateTime = nowUtc.AddSeconds(-(num + 5));
		while (_ring.Count > 0 && (_ring.Peek().TimestampUtc < dateTime || _ringBytes > 8388608))
		{
			PacketSnapshot packetSnapshot = _ring.Dequeue();
			_ringBytes -= packetSnapshot.Payload.Length;
		}
	}

	private List<CaptureSession> TakeCompletedSessionsLocked(DateTime nowUtc)
	{
		List<CaptureSession> list = _sessions.Where((CaptureSession s) => s.EndUtc <= nowUtc).ToList();
		foreach (CaptureSession item in list)
		{
			_sessions.Remove(item);
		}
		return list;
	}

	private void ClearRequests()
	{
		lock (_gate)
		{
			_requests.Clear();
			_hasRequests = false;
			_ring.Clear();
			_sessions.Clear();
			_ringBytes = 0L;
		}
	}

	private static string BuildJsonl(IReadOnlyList<PacketSnapshot> packets)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (PacketSnapshot packet in packets)
		{
			stringBuilder.Append(JsonSerializer.Serialize(new
			{
				index = packet.Index,
				timestampUtc = packet.TimestampUtc,
				srcPort = packet.SrcPort,
				dstPort = packet.DstPort,
				seq = packet.Seq,
				isPsh = packet.IsPsh,
				device = packet.Device,
				srcIp = packet.SrcIp,
				dstIp = packet.DstIp,
				payloadBase64 = Convert.ToBase64String(packet.Payload)
			}, JsonOptions));
			stringBuilder.Append('\n');
		}
		return stringBuilder.ToString();
	}

	private static byte[] Gzip(byte[] rawBytes)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using (GZipStream gZipStream = new GZipStream(memoryStream, CompressionLevel.Fastest, leaveOpen: true))
		{
			gZipStream.Write(rawBytes, 0, rawBytes.Length);
		}
		return memoryStream.ToArray();
	}

	private static List<PacketSnapshot> TakeTailWithinByteLimit(IEnumerable<PacketSnapshot> packets)
	{
		List<PacketSnapshot> list = packets.ToList();
		List<PacketSnapshot> list2 = new List<PacketSnapshot>();
		long num = 0L;
		for (int num2 = list.Count - 1; num2 >= 0; num2--)
		{
			PacketSnapshot packetSnapshot = list[num2];
			if (num + packetSnapshot.Payload.Length > 33554432)
			{
				break;
			}
			list2.Add(packetSnapshot);
			num += packetSnapshot.Payload.Length;
		}
		list2.Reverse();
		return list2;
	}

	private static string GetAppVersion()
	{
		Assembly executingAssembly = Assembly.GetExecutingAssembly();
		return executingAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? executingAssembly.GetName().Version?.ToString(3) ?? "0.0.0";
	}

	public void Dispose()
	{
		_disposed = true;
		ClearRequests();
	}
}
