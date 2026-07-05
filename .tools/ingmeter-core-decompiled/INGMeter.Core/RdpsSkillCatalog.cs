using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace INGMeter.Core;

public sealed class RdpsSkillCatalog
{
	private sealed class CatalogPayload
	{
		public List<SkillPayload> Skills { get; set; } = new List<SkillPayload>();
	}

	private sealed class SkillPayload
	{
		public int Id { get; set; }

		public string Name { get; set; } = "";

		public int JobId { get; set; }

		public string JobText { get; set; } = "";

		public string Category { get; set; } = "";

		public int MaxLevel { get; set; }

		public string Icon { get; set; } = "";

		public List<SpecializationPayload> Specializations { get; set; } = new List<SpecializationPayload>();
	}

	private sealed class SpecializationPayload
	{
		public int Index { get; set; }

		public int RequiredLevel { get; set; }

		public string Desc { get; set; } = "";
	}

	private const string ResourceName = "INGMeter.assets.rdps_skill_catalog.json";

	private static readonly Lazy<RdpsSkillCatalog> SharedInstance = new Lazy<RdpsSkillCatalog>(LoadFromEmbeddedResource);

	private readonly Dictionary<int, RdpsSkillInfo> _skillsById;

	public static RdpsSkillCatalog Empty { get; } = new RdpsSkillCatalog(Array.Empty<RdpsSkillInfo>());

	public static RdpsSkillCatalog Shared => SharedInstance.Value;

	public IReadOnlyList<RdpsSkillInfo> Skills { get; }

	private RdpsSkillCatalog(IReadOnlyList<RdpsSkillInfo> skills)
	{
		Skills = skills;
		_skillsById = skills.ToDictionary((RdpsSkillInfo skill) => skill.Id);
	}

	public static RdpsSkillCatalog LoadFromEmbeddedResource()
	{
		try
		{
			using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("INGMeter.assets.rdps_skill_catalog.json");
			if (stream == null)
			{
				return Empty;
			}
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};
			CatalogPayload catalogPayload = JsonSerializer.Deserialize<CatalogPayload>(stream, options);
			if (catalogPayload == null || catalogPayload.Skills.Count == 0)
			{
				return Empty;
			}
			return new RdpsSkillCatalog((from skill in catalogPayload.Skills
				where skill.Id > 0 && !string.IsNullOrWhiteSpace(skill.Name)
				select new RdpsSkillInfo(skill.Id, skill.Name.Trim(), skill.JobId, skill.JobText.Trim(), skill.Category.Trim(), skill.MaxLevel, skill.Icon.Trim(), (from spec in skill.Specializations
					where spec.Index >= 0 && !string.IsNullOrWhiteSpace(spec.Desc)
					orderby spec.Index
					select new RdpsSpecializationInfo(spec.Index, spec.Index + 1, spec.RequiredLevel, spec.Desc.Trim())).ToList())).ToList());
		}
		catch
		{
			return Empty;
		}
	}

	public bool TryGetSkill(int skillCode, out RdpsSkillInfo? skill)
	{
		int key = Math.Abs(skillCode);
		if (_skillsById.TryGetValue(key, out skill))
		{
			return true;
		}
		RdpsSkillCodeParts rdpsSkillCodeParts = ParseSkillCode(skillCode);
		if (rdpsSkillCodeParts.BaseSkillId > 0 && _skillsById.TryGetValue(rdpsSkillCodeParts.BaseSkillId, out skill))
		{
			return true;
		}
		skill = null;
		return false;
	}

	public string BuildSpecialtyTooltip(IReadOnlyList<int> skillCodes)
	{
		if (skillCodes.Count == 0)
		{
			return "";
		}
		List<string> list = new List<string>();
		foreach (int item in from code in skillCodes.Distinct()
			orderby code
			select code)
		{
			RdpsSkillCodeParts rdpsSkillCodeParts = ParseSkillCode(item);
			if (rdpsSkillCodeParts.BaseSkillId <= 0 || rdpsSkillCodeParts.TraitIndexes.Count == 0 || !TryGetSkill(item, out RdpsSkillInfo skill) || skill == null)
			{
				continue;
			}
			foreach (int traitIndex in rdpsSkillCodeParts.TraitIndexes)
			{
				string value = skill.Specializations.FirstOrDefault((RdpsSpecializationInfo x) => x.DisplayIndex == traitIndex)?.Description ?? "정보 없음";
				list.Add($"{skill.Name} 특화 {traitIndex}: {value}");
			}
		}
		if (list.Count == 0)
		{
			return string.Join(", ", from code in skillCodes.Distinct()
				orderby code
				select code);
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("적용 특화");
		foreach (string item2 in list.Distinct<string>(StringComparer.Ordinal))
		{
			stringBuilder.AppendLine(item2);
		}
		stringBuilder.Append("스킬 코드: ");
		stringBuilder.Append(string.Join(", ", from code in skillCodes.Distinct()
			orderby code
			select code));
		return stringBuilder.ToString();
	}

	public bool IsStigmaSkillCode(int skillCode, RdpsPartyBuffCatalog partyBuffCatalog)
	{
		if (!TryGetSkill(skillCode, out RdpsSkillInfo skill) || skill == null)
		{
			return false;
		}
		if (skill.Category.Equals("stigma", StringComparison.OrdinalIgnoreCase) || skill.Category.Equals("dp", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (!skill.Category.Equals("active", StringComparison.OrdinalIgnoreCase))
		{
			return partyBuffCatalog.ContainsEffectSkill(skillCode);
		}
		return false;
	}

	public bool CanResolveEffectBySkillLevel(int skillCode, RdpsPartyBuffCatalog partyBuffCatalog)
	{
		if (!TryGetSkill(skillCode, out RdpsSkillInfo skill) || skill == null)
		{
			return false;
		}
		if (!skill.Category.Equals("active", StringComparison.OrdinalIgnoreCase))
		{
			return partyBuffCatalog.ContainsEffectSkill(skillCode);
		}
		return false;
	}

	public static RdpsSkillCodeParts ParseSkillCode(int skillCode)
	{
		int num = Math.Abs(skillCode);
		if (num < 10000000)
		{
			return new RdpsSkillCodeParts(num, 0, 0, 0, Array.Empty<int>());
		}
		int baseSkillId = num / 10000 * 10000;
		int num2 = num % 10000;
		int traitDigits = num2 / 10 % 1000;
		int chargeStep = num2 % 10;
		int[] traitIndexes = (from value in (from ch in traitDigits.ToString("D3")
				select ch - 48 into value
				where value >= 1 && value <= 5
				select value).Distinct()
			orderby value
			select value).ToArray();
		return new RdpsSkillCodeParts(num, baseSkillId, traitDigits, chargeStep, traitIndexes);
	}
}
