using System;

namespace INGMeter.App;

public sealed record CharacterConsentState(string CharacterName, int ServerId, string ServerName, int CharNo, bool? PublicConsent, DateTime LastSeenUtc)
{
	public string Key => $"{ServerId}:{CharacterName.Trim()}";
}
