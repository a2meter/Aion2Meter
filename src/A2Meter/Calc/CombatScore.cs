using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using A2Meter.Api;
using A2Meter.Dps;

namespace A2Meter.Calc;

public class CombatScore
{
	private class ExtractedStats
	{
		public double Attack;

		public double Wmin;

		public double Wmax;

		public double CombatSpeed;

		public double WeaponAmp;

		public double PveAmp;

		public double NormalAmp;

		public double CritAmp;

		public double SkillDmg;

		public double Cooldown;

		public double StunHit;

		public double Perfect;

		public double MultiHit;

		public double AccMax;

		public JsonElement CritBreakdown;
	}

	private static readonly HashSet<int> EquipmentSlots = new HashSet<int>
	{
		1, 2, 3, 4, 5, 6, 7, 8, 9, 17,
		19
	};

	private static readonly HashSet<int> AccessorySlots = new HashSet<int> { 10, 11, 12, 13, 14, 15, 16, 22, 23, 24 };

	private static readonly HashSet<int> ArcanaSlots = new HashSet<int> { 41, 42, 43, 44, 45, 46 };

	private static readonly Dictionary<string, string> ArcanaTypeMap = new Dictionary<string, string>
	{
		["마법"] = "magic",
		["활력"] = "vitality",
		["순수"] = "purity",
		["광분"] = "frenzy"
	};

	public static async Task<CombatScoreResult?> QueryCombatScore(int serverId, string name, int race = 1)
	{
		CharacterData? data = await PlayncClient.FetchAll(name, serverId, race);
		if (data == null)
		{
			return null;
		}
		// Fetch packet-cached equipment (parsed from CharacterLookup packet) to use
		// as primary sub-stat source before relying solely on API item details.
		var packetEquip = Api.EquipmentCache.Instance.Get(name, serverId);
		FormulaConfig formulaConfig = LoadFormulaConfig();
		SupplementResult supplement = Supplement.CalcSupplement(data.StatData, data.ItemDetails);
		ExtractedStats extractedStats = ExtractStats(NativeCalcEngine.RunCalc(BuildJsInput(data, supplement, packetEquip)));
		double num = AccToDi(extractedStats.AccMax, formulaConfig);
		double num2 = CalcCritChance(extractedStats.CritBreakdown);
		Dictionary<string, double> dictionary = new Dictionary<string, double>();
		if (extractedStats.CombatSpeed > 0.0)
		{
			dictionary["combatSpeed"] = extractedStats.CombatSpeed;
		}
		if (extractedStats.WeaponAmp > 0.0)
		{
			dictionary["weaponDamageAmp"] = extractedStats.WeaponAmp * formulaConfig.WeaponAmpCoeff;
		}
		double num3 = extractedStats.PveAmp + extractedStats.NormalAmp;
		if (num3 > 0.0)
		{
			dictionary["damageAmp"] = num3;
		}
		if (num2 > 0.0)
		{
			double num4 = num2 / 100.0;
			dictionary["criticalDamageAmp"] = (1.0 - num4 + num4 * (formulaConfig.BaseCriticalDamage + extractedStats.CritAmp / 100.0) - 1.0) * 100.0;
		}
		if (extractedStats.SkillDmg > 0.0)
		{
			dictionary["skillDamage"] = extractedStats.SkillDmg;
		}
		if (extractedStats.Cooldown > 0.0)
		{
			dictionary["cooldownReduction"] = (100.0 / (100.0 - extractedStats.Cooldown) - 1.0) * 100.0 * formulaConfig.CooldownEfficiency;
		}
		if (extractedStats.StunHit > 0.0)
		{
			dictionary["stunHit"] = Math.Max(0.0, extractedStats.StunHit - formulaConfig.StunResistance);
		}
		if (extractedStats.Perfect > 0.0 && extractedStats.Wmin > 0.0 && extractedStats.Wmax > extractedStats.Wmin)
		{
			dictionary["perfect"] = extractedStats.Perfect * (extractedStats.Wmax - extractedStats.Wmin) / (extractedStats.Wmax + extractedStats.Wmin);
		}
		double[] poly;
		if (extractedStats.MultiHit > 0.0)
		{
			poly = formulaConfig.MultiHitPoly;
			double x = (double)formulaConfig.BaseMultiHitPct / 100.0;
			double x2 = ((double)formulaConfig.BaseMultiHitPct + extractedStats.MultiHit) / 100.0;
			dictionary["multiHit"] = ((1.0 + Fn(x2) / 100.0) / (1.0 + Fn(x) / 100.0) - 1.0) * 100.0;
		}
		if (num > 0.0)
		{
			dictionary["accuracy"] = num;
		}
		double num5 = dictionary.Values.Aggregate(1.0, (double acc, double v) => acc * (1.0 + v / 100.0));
		int score = (int)Math.Round(extractedStats.Attack * num5);

		// Extract skill levels into a simple dictionary (name -> level)
		Dictionary<string, int> skillLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonElement s in data.SkillList)
		{
			string skillName = s.GetString("skillName") ?? s.GetString("name") ?? "";
			int level = s.GetInt("skillLevel");
			if (level == 0)
			{
				level = s.GetInt("level_int");
			}
			if (!string.IsNullOrWhiteSpace(skillName) && level > 0)
			{
				if (!skillLevels.TryGetValue(skillName, out var existing) || level > existing)
				{
					skillLevels[skillName] = level;
				}
			}
		}

		bool hasJonggul = data.TitleList.Any((JsonElement t) => (t.GetString("name") ?? "").Contains("종족의 굴레"));
		bool hasNaked = data.TitleList.Any((JsonElement t) => (t.GetString("name") ?? "").Contains("입을 옷이 없네"));
		int combatPower = data.Profile.GetInt("combatPower");
		return new CombatScoreResult
		{
			CharacterId = data.CharacterId,
			ServerId = data.ServerId,
			ServerName = data.ServerName,
			Score = score,
			CombatPower = combatPower,
			ClassName = data.ClassName,
			SkillLevels = skillLevels,
			DpSkills = PlayncClient.ExtractEquippedDpSkills(data.SkillList),
			HasJonggul = hasJonggul,
			HasNaked = hasNaked
		};
		double Fn(double num7)
		{
			return poly[0] * num7 + poly[1] * Math.Pow(num7, 2.0) + poly[2] * Math.Pow(num7, 3.0) + poly[3] * Math.Pow(num7, 4.0);
		}
	}

	private static CalcInput BuildJsInput(CharacterData data, SupplementResult supplement, Api.CachedEquipment? packetEquip = null)
	{
		List<object> list = new List<object>();
		List<object> list2 = new List<object>();
		Dictionary<string, int> dictionary = new Dictionary<string, int>
		{
			["magic"] = 0,
			["vitality"] = 0,
			["purity"] = 0,
			["frenzy"] = 0
		};
		// Build a lookup from packet equipment: itemId → packet subStats.
		// Used to inject sub-stats from the network packet when API detail is empty.
		Dictionary<uint, List<object>>? packetSubLookup = null;
		if (packetEquip?.Items is { Count: > 0 })
		{
			packetSubLookup = new Dictionary<uint, List<object>>();
			foreach (var pItem in packetEquip.Items)
			{
				if (pItem.SubStats is { Count: > 0 } && !packetSubLookup.ContainsKey(pItem.ItemId))
					packetSubLookup[pItem.ItemId] = Api.EquipmentCache.ToCalcSubStats(pItem.SubStats);
			}
		}

		int key;
		JsonElement value;
		foreach (KeyValuePair<int, JsonElement> itemDetail in data.ItemDetails)
		{
			itemDetail.Deconstruct(out key, out value);
			int num = key;
			JsonElement jsonElement = value;
			int num2 = num;
			int valueOrDefault = data.SlotExceed.GetValueOrDefault(num2, 0);
			// Try to find matching packet sub-stats by item ID from the API response.
			List<object>? pktSubs = null;
			if (packetSubLookup != null)
			{
				string idStr = jsonElement.GetString("id") ?? "";
				if (uint.TryParse(idStr, out uint apiItemId) && packetSubLookup.TryGetValue(apiItemId, out var found))
					pktSubs = found;
			}
			object item = AdaptItem(jsonElement, num2, valueOrDefault, pktSubs);
			if (EquipmentSlots.Contains(num2) || ArcanaSlots.Contains(num2))
			{
				list.Add(item);
			}
			else if (AccessorySlots.Contains(num2))
			{
				list2.Add(item);
			}
			if (!ArcanaSlots.Contains(num2))
			{
				continue;
			}
			string text = jsonElement.GetString("name") ?? "";
			foreach (KeyValuePair<string, string> item3 in ArcanaTypeMap)
			{
				item3.Deconstruct(out var key2, out var value2);
				string value3 = key2;
				string text2 = value2;
				if (text.Contains(value3))
				{
					value2 = text2;
					key = dictionary[value2]++;
					break;
				}
			}
		}
		List<object> list3 = new List<object>();
		List<object> list4 = new List<object>();
		foreach (JsonElement skill in data.SkillList)
		{
			string text3 = skill.GetString("category") ?? "";
			object item2 = AdaptSkill(skill);
			if ((text3 == "Active" || text3 == "Passive") ? true : false)
			{
				list3.Add(item2);
			}
			else if (text3 == "Dp")
			{
				list4.Add(item2);
			}
		}
		string daevanionDataJson = "null";
		if (data.DaevanionDetails.Count > 0)
		{
			int num3 = data.DaevanionDetails.Keys.Min() - 41;
			Dictionary<int, JsonElement> dictionary2 = new Dictionary<int, JsonElement>();
			foreach (KeyValuePair<int, JsonElement> daevanionDetail in data.DaevanionDetails)
			{
				daevanionDetail.Deconstruct(out key, out value);
				int num4 = key;
				JsonElement value4 = value;
				dictionary2[num4 - num3] = value4;
			}
			daevanionDataJson = JsonSerializer.Serialize(dictionary2);
		}
		return new CalcInput
		{
			EquipmentJson = JsonSerializer.Serialize(list),
			AccessoriesJson = JsonSerializer.Serialize(list2),
			StatDataJson = data.StatData.GetRawText(),
			DaevanionDataJson = daevanionDataJson,
			TitlesJson = JsonSerializer.Serialize(data.TitleList.Select(AdaptTitle)),
			WingName = data.WingName,
			SkillsJson = JsonSerializer.Serialize(list3),
			StigmasJson = JsonSerializer.Serialize(list4),
			JobName = data.ClassName,
			CharacterDataJson = JsonSerializer.Serialize(new
			{
				pure_power = supplement.PurePower,
				pure_agility = supplement.PureAgility,
				intelligent_pet_critical_min = supplement.IntelligentPetCriticalMin,
				intelligent_pet_critical_max = supplement.IntelligentPetCriticalMax,
				wild_pet_accuracy_min = supplement.WildPetAccuracyMin,
				wild_pet_accuracy_max = supplement.WildPetAccuracyMax
			}),
			ArcanaSetCounts = dictionary
		};
	}

	private static object AdaptItem(JsonElement item, int slot, int exceedLevel, List<object>? packetSubStats = null)
	{
		var apiSubStats = AdaptStatArray(item, "subStats");
		// Use packet-parsed sub-stats when API response has none (faster, no extra HTTP call needed).
		var subStats = (apiSubStats.Count > 0) ? apiSubStats : (packetSubStats ?? apiSubStats);
		return new
		{
			slotPos = slot,
			name = (item.GetString("name") ?? ""),
			enchantLevel = item.GetInt("enchantLevel"),
			enhance_level = item.GetInt("enchantLevel"),
			exceedLevel = exceedLevel,
			exceed_level = exceedLevel,
			main_stats = AdaptStatArray(item, "mainStats"),
			sub_stats = subStats,
			magic_stone_stat = AdaptStatArray(item, "magicStoneStat")
		};
	}

	private static List<object> AdaptStatArray(JsonElement item, string prop)
	{
		List<object> list = new List<object>();
		if (!item.TryGetProperty(prop, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		foreach (JsonElement item2 in value.EnumerateArray())
		{
			list.Add(new
			{
				id = (item2.GetString("id") ?? ""),
				name = (item2.GetString("name") ?? ""),
				value = GetStatValue(item2, "value"),
				extra = GetStatValue(item2, "extra"),
				minValue = GetStatValue(item2, "minValue"),
				exceed = (item2.TryGetProperty("exceed", out var value2) && value2.ValueKind == JsonValueKind.True)
			});
		}
		return list;
	}

	private static object GetStatValue(JsonElement el, string prop)
	{
		if (!el.TryGetProperty(prop, out var value))
		{
			return 0;
		}
		if (value.ValueKind == JsonValueKind.Number)
		{
			if (value.TryGetInt32(out var value2))
			{
				return value2;
			}
			return value.GetDouble();
		}
		if (value.ValueKind == JsonValueKind.String)
		{
			string text = value.GetString() ?? "";
			if (int.TryParse(text, out var result))
			{
				return result;
			}
			return text;
		}
		return 0;
	}

	private static object AdaptSkill(JsonElement s)
	{
		int num = s.GetInt("skillLevel");
		if (num == 0)
		{
			num = s.GetInt("level_int");
		}
		return new
		{
			name = (s.GetString("skillName") ?? s.GetString("name") ?? ""),
			skillName = (s.GetString("skillName") ?? s.GetString("name") ?? ""),
			category = (s.GetString("category") ?? ""),
			level_int = num,
			level = num.ToString(),
			group = (s.GetString("category") ?? "Active").ToLower(),
			skillLevel = num
		};
	}

	private static object AdaptTitle(JsonElement t)
	{
		List<string> list = new List<string>();
		if (t.TryGetProperty("equipStatList", out var value) && value.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in value.EnumerateArray())
			{
				string text = item.GetString("desc");
				if (!string.IsNullOrEmpty(text))
				{
					list.Add(text);
				}
			}
		}
		return new
		{
			name = (t.GetString("name") ?? ""),
			equip_effects = list
		};
	}

	private static ExtractedStats ExtractStats(CalcResult r)
	{
		double attack = GetDouble(r.AttackPower, "finalAttack");
		if (r.IsAttackPowerOverCap && r.CappedAttackPower.HasValue)
		{
			attack = r.CappedAttackPower.Value;
		}
		return new ExtractedStats
		{
			Attack = attack,
			Wmin = r.WeaponMinAttack,
			Wmax = r.WeaponMaxAttack,
			CombatSpeed = GetDouble(r.CombatSpeed, "totalCombatSpeed"),
			WeaponAmp = GetDouble(GetSub(r.DamageAmplification, "weaponDamageAmp"), "totalPercent"),
			PveAmp = GetDouble(GetSub(r.DamageAmplification, "pveDamageAmp"), "totalPercent"),
			NormalAmp = GetDouble(GetSub(r.DamageAmplification, "damageAmp"), "totalPercent"),
			CritAmp = GetDouble(GetSub(r.DamageAmplification, "criticalDamageAmp"), "totalPercent"),
			CritBreakdown = ((r.CriticalHit.ValueKind == JsonValueKind.Object) ? GetSub(r.CriticalHit, "breakdown") : default(JsonElement)),
			SkillDmg = GetDouble(r.SkillDamage, "totalSkillDamage"),
			Cooldown = GetDouble(r.CooldownReduction, "totalCooldownReduction"),
			StunHit = GetDouble(r.StunHit, "totalStunHitPercent"),
			Perfect = GetDouble(r.Perfect, "totalPerfectPercent"),
			MultiHit = GetDouble(r.MultiHit, "totalMultiHitPercent"),
			AccMax = ((GetDouble(r.Accuracy, "finalAccuracyMax") > 0.0) ? GetDouble(r.Accuracy, "finalAccuracyMax") : GetDouble(r.Accuracy, "totalIntegerAccuracyMax"))
		};
		static double GetDouble(JsonElement el, string prop)
		{
			if (el.ValueKind != JsonValueKind.Object)
			{
				return 0.0;
			}
			if (!el.TryGetProperty(prop, out var value))
			{
				return 0.0;
			}
			if (value.ValueKind != JsonValueKind.Number)
			{
				return 0.0;
			}
			return value.GetDouble();
		}
		static JsonElement GetSub(JsonElement el, string prop)
		{
			if (el.ValueKind != JsonValueKind.Object)
			{
				return default(JsonElement);
			}
			if (!el.TryGetProperty(prop, out var value))
			{
				return default(JsonElement);
			}
			return value;
		}
	}

	private static double CalcCritChance(JsonElement breakdown)
	{
		if (breakdown.ValueKind != JsonValueKind.Object)
		{
			return 0.0;
		}
		double num = Get("baseCriticalHitInteger") + Get("soulCriticalHitInteger") + Get("stoneCriticalHitInteger") + Get("daevanionCriticalHitInteger") + Get("wingCriticalHitInteger") + Get("titleEquipCriticalHit");
		double num2 = Get("intelligentPetCriticalMax");
		if (num2 == 0.0)
		{
			num2 = 41.0;
		}
		double num3 = num + (num2 + 80.0);
		double num4 = Get("deathCriticalHitPercent");
		double num5 = Get("accuracyCriticalHitPercent");
		double num6 = 1.0 + (num4 + num5) / 100.0;
		return Math.Min(Math.Round(num3 * num6) * 0.4 / 10.0, 80.0);
		double Get(string prop)
		{
			if (breakdown.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.Number)
			{
				return value.GetDouble();
			}
			return 0.0;
		}
	}

	private static double AccToDi(double acc, FormulaConfig cfg)
	{
		if (acc <= (double)cfg.AccuracyCapMin)
		{
			return 0.0;
		}
		if (acc >= (double)cfg.AccuracyCapMax)
		{
			return cfg.AccuracyMaxDi;
		}
		double num = 0.0;
		foreach (double[] accuracyInterval in cfg.AccuracyIntervals)
		{
			double num2 = accuracyInterval[0];
			double num3 = accuracyInterval[1];
			double num4 = accuracyInterval[2];
			if (acc <= num2)
			{
				break;
			}
			if (acc >= num3)
			{
				num += num4;
				continue;
			}
			num += num4 * (acc - num2) / (num3 - num2);
			break;
		}
		return num;
	}

	private static FormulaConfig LoadFormulaConfig()
	{
		return new FormulaConfig();
	}

	private static string NormalizeSkillCategory(string? category)
	{
		return (category ?? "").Trim() switch
		{
			"Active" => "active",
			"Passive" => "passive",
			"Dp" => "stigma",
			_ => "stigma",
		};
	}
}
