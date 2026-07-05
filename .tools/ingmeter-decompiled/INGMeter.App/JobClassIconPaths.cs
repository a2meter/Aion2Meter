using System;
using INGMeter.Core;

namespace INGMeter.App;

internal static class JobClassIconPaths
{
	private const string BaseUri = "pack://siteoforigin:,,,/assets/classimg/";

	public static string? For(JobClass job)
	{
		return job switch
		{
			JobClass.Gladiator => Icon("검성"), 
			JobClass.Templar => Icon("수호성"), 
			JobClass.Assassin => Icon("살성"), 
			JobClass.Ranger => Icon("궁성"), 
			JobClass.Sorcerer => Icon("마도성"), 
			JobClass.Spiritmaster => Icon("정령성"), 
			JobClass.Cleric => Icon("치유성"), 
			JobClass.Chanter => Icon("호법성"), 
			_ => null, 
		};
	}

	private static string Icon(string name)
	{
		return "pack://siteoforigin:,,,/assets/classimg/" + Uri.EscapeDataString(name) + ".png";
	}
}
