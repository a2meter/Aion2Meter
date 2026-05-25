using System.Collections.Generic;
using System.Text.Json;

namespace A2Meter.Api;

public class CharacterData
{
	public string CharacterId { get; set; } = "";

	public int ServerId { get; set; }

	public string ServerName { get; set; } = "";

	public JsonElement Profile { get; set; }

	public JsonElement StatData { get; set; }

	public List<JsonElement> TitleList { get; set; } = new List<JsonElement>();

	public List<JsonElement> SkillList { get; set; } = new List<JsonElement>();

	public string WingName { get; set; } = "";

	public string ClassName { get; set; } = "";

	public Dictionary<int, JsonElement> ItemDetails { get; set; } = new Dictionary<int, JsonElement>();

	public Dictionary<int, JsonElement> DaevanionDetails { get; set; } = new Dictionary<int, JsonElement>();

	public Dictionary<int, int> SlotExceed { get; set; } = new Dictionary<int, int>();
}
