using System;

namespace INGMeter.Core;

public static class WebEndpoint
{
	public const string DeveloperSecurityKey = "vksxkwl1";

	private const string Scheme = "https";

	private const string ProductionHost = "aion.ing";

	private const string TestHost = "test.aion.ing";

	private static volatile bool _useTestHost;

	public static bool UseTestHost => _useTestHost;

	public static string Host
	{
		get
		{
			if (!_useTestHost)
			{
				return "aion.ing";
			}
			return "test.aion.ing";
		}
	}

	public static string BaseUrl => "https://" + Host;

	public static bool IsDeveloperSecurityKey(string? value)
	{
		return string.Equals(value, "vksxkwl1", StringComparison.Ordinal);
	}

	public static void SetDeveloperSecurityKey(string? value)
	{
		_useTestHost = IsDeveloperSecurityKey(value);
	}

	public static string Url(string pathAndQuery)
	{
		if (string.IsNullOrWhiteSpace(pathAndQuery))
		{
			return BaseUrl;
		}
		if (Uri.TryCreate(pathAndQuery, UriKind.Absolute, out Uri result))
		{
			return Route(result);
		}
		string text = (pathAndQuery.StartsWith("/", StringComparison.Ordinal) ? pathAndQuery : ("/" + pathAndQuery));
		return BaseUrl + text;
	}

	public static string Route(string urlOrPath)
	{
		if (string.IsNullOrWhiteSpace(urlOrPath))
		{
			return urlOrPath;
		}
		if (!Uri.TryCreate(urlOrPath, UriKind.Absolute, out Uri result))
		{
			return Url(urlOrPath);
		}
		return Route(result);
	}

	private static string Route(Uri uri)
	{
		if (!IsAionIngHost(uri.Host))
		{
			return uri.ToString();
		}
		return new UriBuilder(uri)
		{
			Scheme = "https",
			Host = Host,
			Port = -1
		}.Uri.ToString();
	}

	private static bool IsAionIngHost(string host)
	{
		if (!string.Equals(host, "aion.ing", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(host, "test.aion.ing", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
