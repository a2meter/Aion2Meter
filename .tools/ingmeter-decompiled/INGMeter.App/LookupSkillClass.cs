using System;
using System.Collections.Generic;
using INGMeter.Core;

namespace INGMeter.App;

public sealed class LookupSkillClass
{
	public string Key { get; }

	public int JobCode { get; }

	public string Name { get; }

	public int DisplayOrder { get; }

	public IReadOnlyDictionary<string, IReadOnlyList<LookupSkillInfo>> SkillsByCategory { get; }

	public IReadOnlyList<LookupSkillInfo> AllSkills { get; }

	public JobClass Job
	{
		get
		{
			if (!Enum.IsDefined(typeof(JobClass), JobCode))
			{
				return JobClass.None;
			}
			return (JobClass)JobCode;
		}
	}

	public LookupSkillClass(string key, int jobCode, string name, int displayOrder, IReadOnlyDictionary<string, IReadOnlyList<LookupSkillInfo>> skillsByCategory, IReadOnlyList<LookupSkillInfo> allSkills)
	{
		Key = key;
		JobCode = jobCode;
		Name = name;
		DisplayOrder = displayOrder;
		SkillsByCategory = skillsByCategory;
		AllSkills = allSkills;
	}

	public IReadOnlyList<LookupSkillInfo> GetSkills(string categoryKey)
	{
		if (!SkillsByCategory.TryGetValue(categoryKey, out IReadOnlyList<LookupSkillInfo> value))
		{
			return Array.Empty<LookupSkillInfo>();
		}
		return value;
	}
}
