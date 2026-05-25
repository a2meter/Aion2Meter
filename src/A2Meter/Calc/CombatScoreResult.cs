using System.Collections.Generic;

namespace A2Meter.Calc;

public class CombatScoreResult
{
	public string CharacterId { get; set; } = "";

	public int ServerId { get; set; }

	public string ServerName { get; set; } = "";

	public int Score { get; set; }

	public int CombatPower { get; set; }

	public string ClassName { get; set; } = "";

	public Dictionary<string, int> SkillLevels { get; set; } = new Dictionary<string, int>();

	public List<A2Meter.Api.CharacterDpSkill> DpSkills { get; set; } = new List<A2Meter.Api.CharacterDpSkill>();

	public bool HasJonggul { get; set; }

	public bool HasNaked { get; set; }
}
