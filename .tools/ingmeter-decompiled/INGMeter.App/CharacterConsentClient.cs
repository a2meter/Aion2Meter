using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using INGMeter.Core;

namespace INGMeter.App;

internal static class CharacterConsentClient
{
	public readonly record struct ConsentResult(bool Success, bool? PublicConsent);

	private const string EndpointPath = "/aion2data/aion2_character_consent.php";

	private const string ApiKey = "ing_meter_secret_2026";

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(5L)
	};

	private static readonly string AppVersion = ResolveAppVersion();

	public static Task<ConsentResult> GetAsync(string characterName, int serverId, string serverName, int charNo, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SendAsync(new
		{
			api_key = "ing_meter_secret_2026",
			action = "get",
			app_version = AppVersion,
			character_name = characterName,
			server_id = serverId,
			server_name = serverName,
			char_no = ((charNo > 0) ? new int?(charNo) : ((int?)null))
		}, cancellationToken);
	}

	public static Task<ConsentResult> SetAsync(string characterName, int serverId, string serverName, int charNo, bool publicConsent, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SendAsync(new
		{
			api_key = "ing_meter_secret_2026",
			action = "set",
			app_version = AppVersion,
			character_name = characterName,
			server_id = serverId,
			server_name = serverName,
			char_no = ((charNo > 0) ? new int?(charNo) : ((int?)null)),
			public_consent = (publicConsent ? 1 : 0)
		}, cancellationToken);
	}

	private static async Task<ConsentResult> SendAsync(object payload, CancellationToken cancellationToken)
	{
		_ = 1;
		try
		{
			string content = JsonSerializer.Serialize(payload);
			using StringContent content2 = new StringContent(content, Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await Http.PostAsync(WebEndpoint.Url("/aion2data/aion2_character_consent.php"), content2, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				return new ConsentResult(Success: false, null);
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false));
			JsonElement rootElement = jsonDocument.RootElement;
			if (!rootElement.TryGetProperty("success", out var value) || !value.GetBoolean())
			{
				return new ConsentResult(Success: false, null);
			}
			bool? publicConsent = null;
			if (rootElement.TryGetProperty("public_consent", out var value2) && value2.ValueKind != JsonValueKind.Null)
			{
				if (value2.ValueKind == JsonValueKind.Number && value2.TryGetInt32(out var value3))
				{
					publicConsent = value3 == 1;
				}
				else if (value2.ValueKind == JsonValueKind.True || value2.ValueKind == JsonValueKind.False)
				{
					publicConsent = value2.GetBoolean();
				}
			}
			return new ConsentResult(Success: true, publicConsent);
		}
		catch
		{
			return new ConsentResult(Success: false, null);
		}
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
}
