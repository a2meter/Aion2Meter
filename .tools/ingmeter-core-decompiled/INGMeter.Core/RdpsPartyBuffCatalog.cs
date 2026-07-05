using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace INGMeter.Core;

public sealed class RdpsPartyBuffCatalog
{
	private sealed class CatalogPayload
	{
		public List<EffectPayload> Effects { get; set; } = new List<EffectPayload>();
	}

	private sealed class EffectPayload
	{
		public int SkillId { get; set; }

		public string SkillName { get; set; } = "";

		public string JobText { get; set; } = "";

		public int LevelCode { get; set; }

		public int Level { get; set; }

		public double PveDamageAmpPercent { get; set; }

		public string ExclusiveGroup { get; set; } = "";

		public string EffectScope { get; set; } = "";

		public string SourceRestriction { get; set; } = "";

		public string EffectKind { get; set; } = "";

		public string Description { get; set; } = "";
	}

	private const string ResourceName = "INGMeter.assets.rdps_party_buff_effects.json";

	private static readonly Lazy<RdpsPartyBuffCatalog> SharedInstance = new Lazy<RdpsPartyBuffCatalog>(LoadFromEmbeddedResource);

	private readonly Dictionary<int, RdpsPartyBuffEffect> _effectsByLevelCode;

	private readonly Dictionary<(int SkillId, int Level), RdpsPartyBuffEffect> _effectsBySkillAndLevel;

	private readonly HashSet<int> _effectSkillIds;

	public static RdpsPartyBuffCatalog Empty { get; } = new RdpsPartyBuffCatalog(Array.Empty<RdpsPartyBuffEffect>());

	public static RdpsPartyBuffCatalog Shared => SharedInstance.Value;

	public IReadOnlyList<RdpsPartyBuffEffect> Effects { get; }

	private RdpsPartyBuffCatalog(IReadOnlyList<RdpsPartyBuffEffect> effects)
	{
		Effects = effects;
		_effectsByLevelCode = (from effect in effects
			group effect by effect.LevelCode).ToDictionary((IGrouping<int, RdpsPartyBuffEffect> group) => group.Key, (IGrouping<int, RdpsPartyBuffEffect> group) => group.First());
		_effectsBySkillAndLevel = (from effect in effects
			group effect by (NormalizeSkillId(effect.SkillId), Level: effect.Level)).ToDictionary((IGrouping<(int, int Level), RdpsPartyBuffEffect> group) => group.Key, (IGrouping<(int, int Level), RdpsPartyBuffEffect> group) => group.First());
		_effectSkillIds = (from effect in effects
			select NormalizeSkillId(effect.SkillId) into skillId
			where skillId > 0
			select skillId).ToHashSet();
	}

	public static RdpsPartyBuffCatalog LoadFromEmbeddedResource()
	{
		try
		{
			using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("INGMeter.assets.rdps_party_buff_effects.json");
			if (stream == null)
			{
				return Empty;
			}
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};
			CatalogPayload catalogPayload = JsonSerializer.Deserialize<CatalogPayload>(stream, options);
			if (catalogPayload == null || catalogPayload.Effects.Count == 0)
			{
				return Empty;
			}
			return new RdpsPartyBuffCatalog((from effect in catalogPayload.Effects
				where effect.SkillId > 0 && effect.LevelCode > 0 && effect.PveDamageAmpPercent > 0.0
				select new RdpsPartyBuffEffect(effect.SkillId, effect.SkillName.Trim(), effect.JobText.Trim(), effect.LevelCode, effect.Level, effect.PveDamageAmpPercent, effect.ExclusiveGroup.Trim(), ParseEffectScope(effect.EffectScope), ParseSourceRestriction(effect.SourceRestriction), ParseEffectKind(effect.EffectKind), effect.Description.Trim())).ToList());
		}
		catch
		{
			return Empty;
		}
	}

	public bool TryGetExactEffect(int skillCode, out RdpsPartyBuffEffect? effect)
	{
		int key = Math.Abs(skillCode);
		return _effectsByLevelCode.TryGetValue(key, out effect);
	}

	public bool TryGetEffectForBuffCode(int skillCode, out RdpsPartyBuffEffect? effect)
	{
		int num = Math.Abs(skillCode);
		if (num <= 0)
		{
			effect = null;
			return false;
		}
		if (_effectsByLevelCode.TryGetValue(num, out effect))
		{
			return true;
		}
		if (num >= 10000000 && num < 100000000)
		{
			int num2 = checked(num * 10 + 1);
			if (_effectsByLevelCode.TryGetValue(num2, out effect) || TryGetNormalizedVariantEffect(num2, out effect))
			{
				return true;
			}
		}
		if (TryGetNormalizedVariantEffect(num, out effect))
		{
			return true;
		}
		effect = null;
		return false;
	}

	public bool TryGetEffectForSkillLevel(int skillCode, int level, out RdpsPartyBuffEffect? effect)
	{
		int num = NormalizeSkillId(skillCode);
		if (num <= 0 || level <= 0)
		{
			effect = null;
			return false;
		}
		return _effectsBySkillAndLevel.TryGetValue((num, level), out effect);
	}

	public bool ContainsEffectSkill(int skillCode)
	{
		int num = NormalizeSkillId(skillCode);
		if (num > 0)
		{
			return _effectSkillIds.Contains(num);
		}
		return false;
	}

	public static JobClass GetOwnerJob(RdpsPartyBuffEffect effect)
	{
		JobClass result;
		switch (effect.SkillId)
		{
		case 11780000:
			return JobClass.Gladiator;
		case 11800000:
			return JobClass.Gladiator;
		case 12780000:
			return JobClass.Templar;
		case 17410000:
			return JobClass.Cleric;
		case 18190000:
			return JobClass.Chanter;
		case 12120000:
			return JobClass.Templar;
		case 15320000:
			return JobClass.Sorcerer;
		case 17070000:
			return JobClass.Cleric;
		case 18780000:
			return JobClass.Chanter;
		default:
			{
				string jobText = effect.JobText;
				if (jobText == null)
				{
					goto IL_01f6;
				}
				int length = jobText.Length;
				if (length != 2)
				{
					if (length != 3)
					{
						goto IL_01f6;
					}
					char c = jobText[0];
					if ((uint)c <= 49688u)
					{
						if (c != '마')
						{
							if (c != '수' || !(jobText == "수호성"))
							{
								goto IL_01f6;
							}
							result = JobClass.Templar;
						}
						else
						{
							if (!(jobText == "마도성"))
							{
								goto IL_01f6;
							}
							result = JobClass.Sorcerer;
						}
					}
					else if (c != '정')
					{
						if (c != '치')
						{
							if (c != '호' || !(jobText == "호법성"))
							{
								goto IL_01f6;
							}
							result = JobClass.Chanter;
						}
						else
						{
							if (!(jobText == "치유성"))
							{
								goto IL_01f6;
							}
							result = JobClass.Cleric;
						}
					}
					else
					{
						if (!(jobText == "정령성"))
						{
							goto IL_01f6;
						}
						result = JobClass.Spiritmaster;
					}
				}
				else
				{
					char c = jobText[0];
					if (c != '검')
					{
						if (c != '궁')
						{
							if (c != '살' || !(jobText == "살성"))
							{
								goto IL_01f6;
							}
							result = JobClass.Assassin;
						}
						else
						{
							if (!(jobText == "궁성"))
							{
								goto IL_01f6;
							}
							result = JobClass.Ranger;
						}
					}
					else
					{
						if (!(jobText == "검성"))
						{
							goto IL_01f6;
						}
						result = JobClass.Gladiator;
					}
				}
				goto IL_01f8;
			}
			IL_01f8:
			return result;
			IL_01f6:
			result = JobClass.None;
			goto IL_01f8;
		}
	}

	private bool TryGetNormalizedVariantEffect(int code, out RdpsPartyBuffEffect? effect)
	{
		if (code < 100000000)
		{
			effect = null;
			return false;
		}
		int num = code % 1000 / 100;
		if (num <= 0)
		{
			effect = null;
			return false;
		}
		int num2 = code - num * 100;
		if (num2 == code)
		{
			effect = null;
			return false;
		}
		return _effectsByLevelCode.TryGetValue(num2, out effect);
	}

	private static int NormalizeSkillId(int skillCode)
	{
		int num = Math.Abs(skillCode);
		if (num >= 10000000 && num < 100000000)
		{
			return num / 10000 * 10000;
		}
		return num;
	}

	private static RdpsEffectScope ParseEffectScope(string? value)
	{
		if (!string.Equals(value, "target_debuff", StringComparison.OrdinalIgnoreCase))
		{
			return RdpsEffectScope.PartyBuff;
		}
		return RdpsEffectScope.TargetDebuff;
	}

	private static RdpsSourceRestriction ParseSourceRestriction(string? value)
	{
		if (!string.Equals(value, "owner_only", StringComparison.OrdinalIgnoreCase))
		{
			return RdpsSourceRestriction.AllAttackers;
		}
		return RdpsSourceRestriction.OwnerOnly;
	}

	private static RdpsEffectKind ParseEffectKind(string? value)
	{
		switch (value?.Trim().ToLowerInvariant())
		{
		case "critical_damage_taken":
		case "critical_damage_resistance_down":
			return RdpsEffectKind.CriticalDamageTaken;
		case "target_outgoing_damage_down":
			return RdpsEffectKind.TargetOutgoingDamageDown;
		default:
			return RdpsEffectKind.DamageTaken;
		}
	}
}
