using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using INGMeter.Core;

namespace INGMeter.App;

public sealed class LookupSkillCatalog
{
	private sealed class CatalogPayload
	{
		public List<LookupSkillCategoryPayload> Categories { get; set; } = new List<LookupSkillCategoryPayload>();

		public List<ClassPayload> Classes { get; set; } = new List<ClassPayload>();
	}

	private sealed class LookupSkillCategoryPayload
	{
		public string Key { get; set; } = "";

		public string Name { get; set; } = "";

		public string SourceCategory { get; set; } = "";
	}

	private sealed class ClassPayload
	{
		public string Key { get; set; } = "";

		public int JobCode { get; set; }

		public string Name { get; set; } = "";

		public int DisplayOrder { get; set; }

		public Dictionary<string, List<SkillPayload>> Skills { get; set; } = new Dictionary<string, List<SkillPayload>>(StringComparer.OrdinalIgnoreCase);
	}

	private sealed class SkillPayload
	{
		public int Id { get; set; }

		public string Name { get; set; } = "";

		public string Category { get; set; } = "";

		public string CategoryName { get; set; } = "";

		public string SourceCategory { get; set; } = "";

		public int NeedLevel { get; set; }

		public string Icon { get; set; } = "";

		public int SortOrder { get; set; }
	}

	private const string ResourceName = "INGMeter.assets.lookup_skill_catalog.json";

	private readonly Dictionary<string, LookupSkillClass> _classesByKey;

	private readonly Dictionary<int, LookupSkillClass> _classesByJobCode;

	public static LookupSkillCatalog Empty { get; } = new LookupSkillCatalog(Array.Empty<LookupSkillCategory>(), Array.Empty<LookupSkillClass>());

	public IReadOnlyList<LookupSkillCategory> Categories { get; }

	public IReadOnlyList<LookupSkillClass> Classes { get; }

	private LookupSkillCatalog(IReadOnlyList<LookupSkillCategory> categories, IReadOnlyList<LookupSkillClass> classes)
	{
		Categories = categories;
		Classes = classes;
		_classesByKey = classes.ToDictionary<LookupSkillClass, string>((LookupSkillClass c) => c.Key, StringComparer.OrdinalIgnoreCase);
		_classesByJobCode = classes.ToDictionary((LookupSkillClass c) => c.JobCode);
	}

	public static LookupSkillCatalog LoadFromEmbeddedResource()
	{
		try
		{
			using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("INGMeter.assets.lookup_skill_catalog.json");
			if (stream == null)
			{
				return Empty;
			}
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};
			CatalogPayload catalogPayload = JsonSerializer.Deserialize<CatalogPayload>(stream, options);
			if (catalogPayload == null || catalogPayload.Classes.Count == 0)
			{
				return Empty;
			}
			List<LookupSkillCategory> list = (from c in catalogPayload.Categories
				where !string.IsNullOrWhiteSpace(c.Key)
				select new LookupSkillCategory(c.Key.Trim(), c.Name.Trim(), c.SourceCategory.Trim())).ToList();
			Dictionary<string, int> categoryOrder = list.Select((LookupSkillCategory category, int index) => new { category.Key, index }).ToDictionary(x => x.Key, x => x.index, StringComparer.OrdinalIgnoreCase);
			List<LookupSkillClass> classes = (from c in catalogPayload.Classes
				where !string.IsNullOrWhiteSpace(c.Key)
				orderby c.DisplayOrder
				select ConvertClass(c, categoryOrder)).ToList();
			return new LookupSkillCatalog(list, classes);
		}
		catch
		{
			return Empty;
		}
	}

	public LookupSkillClass? FindClassByKey(string? key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return null;
		}
		if (!_classesByKey.TryGetValue(key.Trim(), out LookupSkillClass value))
		{
			return null;
		}
		return value;
	}

	public LookupSkillClass? FindClassByJob(JobClass job)
	{
		if (!_classesByJobCode.TryGetValue((int)job, out LookupSkillClass value))
		{
			return null;
		}
		return value;
	}

	public LookupSkillClass? InferClassFromSkillIds(IEnumerable<int> skillIds)
	{
		IReadOnlySet<int> ids = (skillIds as IReadOnlySet<int>) ?? skillIds.ToHashSet();
		if (ids.Count == 0)
		{
			return null;
		}
		return (from c in Classes
			select new
			{
				Class = c,
				Count = c.AllSkills.Count((LookupSkillInfo skill) => ids.Contains(skill.Id))
			} into x
			where x.Count > 0
			orderby x.Count descending, x.Class.DisplayOrder
			select x).FirstOrDefault()?.Class;
	}

	private static LookupSkillClass ConvertClass(ClassPayload payload, IReadOnlyDictionary<string, int> categoryOrder)
	{
		Dictionary<string, IReadOnlyList<LookupSkillInfo>> dictionary = new Dictionary<string, IReadOnlyList<LookupSkillInfo>>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, List<SkillPayload>> skill in payload.Skills)
		{
			dictionary[skill.Key] = (from s in (from s in skill.Value
					orderby s.SortOrder, s.NeedLevel
					select s).ThenBy<SkillPayload, string>((SkillPayload s) => s.Name, StringComparer.Ordinal)
				select new LookupSkillInfo(s.Id, s.Name.Trim(), s.Category.Trim(), s.CategoryName.Trim(), s.SourceCategory.Trim(), s.NeedLevel, s.Icon.Trim(), s.SortOrder)).ToList();
		}
		List<LookupSkillInfo> allSkills = dictionary.OrderBy((KeyValuePair<string, IReadOnlyList<LookupSkillInfo>> pair) => (!categoryOrder.TryGetValue(pair.Key, out var value)) ? int.MaxValue : value).ThenBy<KeyValuePair<string, IReadOnlyList<LookupSkillInfo>>, string>((KeyValuePair<string, IReadOnlyList<LookupSkillInfo>> pair) => pair.Key, StringComparer.OrdinalIgnoreCase).SelectMany((KeyValuePair<string, IReadOnlyList<LookupSkillInfo>> pair) => pair.Value)
			.ToList();
		return new LookupSkillClass(payload.Key.Trim(), payload.JobCode, payload.Name.Trim(), payload.DisplayOrder, dictionary, allSkills);
	}
}
