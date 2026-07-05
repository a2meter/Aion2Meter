using System;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using INGMeter.Core;

namespace INGMeter.App;

public static class AuthManager
{
	private const string AuthServerPath = "/auth/auth_check.php";

	private const string ProgramName = "AionIngMeter";

	private static readonly HttpClient _httpClient = new HttpClient();

	public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

	public static async Task<bool> CheckStartupAsync()
	{
		if (await CheckAuthorizationAsync())
		{
			return true;
		}
		ThemedMessageBox.Show("인증되지 않은 환경이거나 사용이 제한된 프로그램입니다.\n관리자에게 문의해주세요.", "INGMeter 인증 실패", MessageBoxButton.OK, MessageBoxImage.Hand);
		return false;
	}

	private static async Task<bool> CheckAuthorizationAsync()
	{
		_ = 1;
		try
		{
			_httpClient.Timeout = TimeSpan.FromSeconds(10L);
			string machineName = Environment.MachineName;
			string text = Environment.OSVersion.ToString();
			string userName = Environment.UserName;
			int processorCount = Environment.ProcessorCount;
			string systemDirectory = Environment.SystemDirectory;
			string text2 = Environment.Version.ToString();
			string stringToEscape = "AionIngMeter_v" + CurrentVersion;
			string stringToEscape2 = ComputeSHA256Hash(machineName + text + userName + processorCount + systemDirectory + text2);
			string requestUri = $"{WebEndpoint.Url("/auth/auth_check.php")}?program_name={Uri.EscapeDataString(stringToEscape)}&machine_name={Uri.EscapeDataString(machineName)}&os_version={Uri.EscapeDataString(text)}&user_name={Uri.EscapeDataString(userName)}&processor_count={processorCount}&system_directory={Uri.EscapeDataString(systemDirectory)}&clr_version={Uri.EscapeDataString(text2)}&fingerprint={Uri.EscapeDataString(stringToEscape2)}&user_note={Uri.EscapeDataString(stringToEscape)}";
			HttpResponseMessage httpResponseMessage = await _httpClient.GetAsync(requestUri);
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				return true;
			}
			string text3 = (await httpResponseMessage.Content.ReadAsStringAsync()).Trim();
			return text3.Equals("ALLOW", StringComparison.OrdinalIgnoreCase) || text3.Equals("LIMITED", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return true;
		}
	}

	private static string ComputeSHA256Hash(string rawData)
	{
		byte[] array = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
		StringBuilder stringBuilder = new StringBuilder(array.Length * 2);
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}
}
