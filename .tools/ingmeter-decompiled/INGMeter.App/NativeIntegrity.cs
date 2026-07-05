using System;
using System.IO;
using System.Security.Cryptography;

namespace INGMeter.App;

internal static class NativeIntegrity
{
	private const string ParserFileName = "INGParser.dll";

	private const string ExpectedParserSha256 = "43D48E29D7485C1CC852B1A293B90B907381CE7B3F86A12773A4F1A111A14B3C";

	public static bool VerifyParser(out string detail)
	{
		string text = Path.Combine(AppContext.BaseDirectory, "INGParser.dll");
		if (!File.Exists(text))
		{
			detail = "INGParser.dll is missing: " + text;
			return false;
		}
		try
		{
			using FileStream source = File.OpenRead(text);
			string text2 = Convert.ToHexString(SHA256.HashData(source));
			if (string.Equals(text2, "43D48E29D7485C1CC852B1A293B90B907381CE7B3F86A12773A4F1A111A14B3C", StringComparison.OrdinalIgnoreCase))
			{
				detail = "";
				return true;
			}
			detail = $"{"INGParser.dll"} hash mismatch. expected={"43D48E29D7485C1CC852B1A293B90B907381CE7B3F86A12773A4F1A111A14B3C"}, actual={text2}";
			return false;
		}
		catch (Exception value)
		{
			detail = $"{"INGParser.dll"} hash check failed: {value}";
			return false;
		}
	}
}
