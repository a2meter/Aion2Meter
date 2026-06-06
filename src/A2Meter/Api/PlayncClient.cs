using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using A2Meter.Dps.Protocol;

namespace A2Meter.Api;

/// Minimal Plaync API client for fetching character skill levels and combat power.
internal static class PlayncClient
{
    private static readonly Regex ServerSuffixRegex =
        new(@"^(?<name>.+?)\[(?<server>[^\]]+)\]$", RegexOptions.Compiled);

    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://aion2.plaync.com"),
        Timeout = TimeSpan.FromSeconds(10),
    };
    private static readonly SemaphoreSlim HttpGate = new(2, 2);

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "api_debug.log");

    static PlayncClient()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        Http.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
    }

    private static void Log(string msg)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("A2METER_API_DEBUG"), "1", StringComparison.Ordinal))
            return;

        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }
        catch { }
    }

    private static async Task<JsonElement> GetJson(string path)
    {
        await HttpGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Log($"GET {path}");
            var resp = await Http.GetAsync(path).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            Log($"  → {(int)resp.StatusCode} | {json[..Math.Min(json.Length, 300)]}");
            resp.EnsureSuccessStatusCode();
            return JsonDocument.Parse(json).RootElement;
        }
        finally
        {
            HttpGate.Release();
        }
    }

    /// Map game packet serverId (1001~2021) to Plaync API serverId.
    /// The API might use the raw game ID or a stripped version.
    private static int[] GetApiServerIds(int gameServerId)
    {
        if (gameServerId <= 0) return new[] { 0 };
        // Try: raw ID, stripped to 1-21, and 0 (meaning omit from query)
        int stripped = gameServerId % 1000;
        if (stripped == gameServerId) return new[] { gameServerId, 0 };
        return new[] { gameServerId, stripped, 0 };
    }

    public readonly record struct CharacterQuery(string Name, int ServerId, string ServerName);

    public static CharacterQuery NormalizeCharacterQuery(string name, int serverId, string serverName = "")
    {
        string cleanName = (name ?? "").Trim();
        string parsedServerName = "";
        var match = ServerSuffixRegex.Match(cleanName);
        if (match.Success)
        {
            cleanName = match.Groups["name"].Value.Trim();
            parsedServerName = match.Groups["server"].Value.Trim();
        }

        if (serverId <= 0 && parsedServerName.Length > 0)
        {
            foreach (var (id, sn) in ServerMap.Servers)
            {
                if (string.Equals(sn, parsedServerName, StringComparison.Ordinal))
                {
                    serverId = id;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(serverName))
            serverName = parsedServerName;
        if (string.IsNullOrWhiteSpace(serverName) && serverId > 0)
            serverName = ServerMap.GetName(serverId);

        return new CharacterQuery(cleanName, serverId, serverName ?? "");
    }

    /// Search for a character by name and server, returns PlayNC identity or null.
    public static async Task<(int Race, string CharId, int ServerId, string ServerName)?> SearchCharacter(string name, int serverId, int race = 0)
    {
        var queryInput = NormalizeCharacterQuery(name, serverId);
        if (string.IsNullOrWhiteSpace(queryInput.Name)) return null;

        foreach (var sid in GetApiServerIds(queryInput.ServerId))
        {
            try
            {
                string query = sid > 0
                    ? $"/ko-kr/api/search/aion2/search/v2/character?keyword={Uri.EscapeDataString(queryInput.Name)}&serverId={sid}&page=1&size=30"
                    : $"/ko-kr/api/search/aion2/search/v2/character?keyword={Uri.EscapeDataString(queryInput.Name)}&page=1&size=30";
                var root = await GetJson(query);

                if (!root.TryGetProperty("list", out var list)) continue;

                foreach (var item in list.EnumerateArray())
                {
                    string raw = "";
                    if (item.TryGetProperty("name", out var nameEl))
                        raw = nameEl.GetString() ?? "";
                    var clean = Regex.Replace(raw, "<[^>]+>", "");
                    if (clean.Equals(queryInput.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!item.TryGetProperty("characterId", out var cid)) continue;
                        string id = cid.ValueKind == JsonValueKind.String
                            ? Uri.UnescapeDataString(cid.GetString() ?? "")
                            : cid.GetRawText();
                        int foundRace = GetInt(item, "race");
                        if (foundRace == 0) foundRace = race;
                        int foundServerId = GetInt(item, "serverId");
                        if (foundServerId == 0) foundServerId = sid > 0 ? sid : queryInput.ServerId;
                        string foundServerName = GetString(item, "serverName")
                            ?? queryInput.ServerName
                            ?? ServerMap.GetName(foundServerId);
                        Log($"  Found character: {queryInput.Name} → charId={id} (sid={foundServerId}, race={foundRace})");
                        return (foundRace, id, foundServerId, foundServerName);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"  Search failed (sid={sid}, race={race}): {ex.Message}");
            }
        }
        return null;
    }

    /// Fetch character info (contains profile with combatPower).
    public static Task<JsonElement> FetchInfo(string charId, int serverId)
        => GetJson($"/api/character/info?lang=ko&characterId={Uri.EscapeDataString(charId)}&serverId={serverId}");

    /// Fetch equipment (contains skill list with levels).
    public static Task<JsonElement> FetchEquipment(string charId, int serverId)
        => GetJson($"/api/character/equipment?lang=ko&characterId={Uri.EscapeDataString(charId)}&serverId={serverId}");

    /// Fetch a single item's detail (stats breakdown).
    public static Task<JsonElement> FetchItem(string itemId, int enchant, string charId, int serverId, int slot, int exceed = 0)
    {
        string path = $"/api/character/equipment/item?id={Uri.EscapeDataString(itemId)}&enchantLevel={enchant}&characterId={Uri.EscapeDataString(charId)}&serverId={serverId}&slotPos={slot}&lang=ko";
        if (exceed > 0)
        {
            path += $"&exceedLevel={exceed}";
        }
        return GetJson(path);
    }

    /// Fetch daevanion board detail.
    public static Task<JsonElement> FetchDaevanion(string charId, int serverId, int boardId)
        => GetJson($"/api/character/daevanion/detail?lang=ko&characterId={Uri.EscapeDataString(charId)}&serverId={serverId}&boardId={boardId}");

    /// Fetch all character data needed for combat score calculation.
    public static async Task<CharacterData?> FetchAll(string name, int serverId, int race = 1)
    {
        var found = await SearchCharacter(name, serverId, race);
        if (!found.HasValue && race == 1)
            found = await SearchCharacter(name, serverId, 2);
        if (!found.HasValue)
        {
            return null;
        }
        string charId = found.Value.CharId;
        int apiServerId = found.Value.ServerId > 0 ? found.Value.ServerId : serverId;
        var infoTask = FetchInfo(charId, apiServerId);
        var equipTask = FetchEquipment(charId, apiServerId);
        await Task.WhenAll(infoTask, equipTask);
        JsonElement info = infoTask.Result;
        JsonElement equip = equipTask.Result;
        JsonElement profile = info.GetProp("profile");
        JsonElement statData = info.GetProp("stat");
        List<JsonElement> titleList = info.GetPropArray("title", "titleList");
        List<JsonElement> skillList = equip.GetPropArray("skill", "skillList");
        string wingName = equip.GetProp("petwing").GetProp("wing").GetString("name") ?? "";
        string className = profile.GetString("className") ?? "";
        List<int> boardIds = new List<int>();
        foreach (JsonElement item2 in info.GetPropArray("daevanion", "boardList"))
        {
            if (item2.TryGetProperty("id", out var val) && val.ValueKind == JsonValueKind.Number)
            {
                boardIds.Add(val.GetInt32());
            }
        }
        List<JsonElement> equipList = equip.GetPropArray("equipment", "equipmentList");
        var itemTasks = new List<Task<(int slot, JsonElement detail)?>>();
        Dictionary<int, int> slotExceed = new Dictionary<int, int>();
        foreach (JsonElement item3 in equipList)
        {
            if (!item3.TryGetProperty("slotPos", out var slotVal) || !item3.TryGetProperty("id", out var idVal))
            {
                continue;
            }
            int slot = (slotVal.ValueKind == JsonValueKind.Number) ? slotVal.GetInt32()
                     : (int.TryParse(slotVal.GetString(), out var sp) ? sp : 0);
            string iid = (idVal.ValueKind == JsonValueKind.String) ? (idVal.GetString() ?? "") : idVal.GetRawText();
            int enc = 0;
            if (item3.TryGetProperty("enchantLevel", out var encVal) && encVal.ValueKind == JsonValueKind.Number)
                enc = encVal.GetInt32();
            int exc = 0;
            if (item3.TryGetProperty("exceedLevel", out var excVal) && excVal.ValueKind == JsonValueKind.Number)
                exc = excVal.GetInt32();
            slotExceed[slot] = exc;
            int capturedSlot = slot;
            string capturedIid = iid;
            int capturedEnc = enc;
            int capturedExc = exc;
            itemTasks.Add(Task.Run(async () =>
            {
                try
                {
                    JsonElement detail = await FetchItem(capturedIid, capturedEnc, charId, apiServerId, capturedSlot, capturedExc);
                    return ((int slot, JsonElement detail)?)(capturedSlot, detail);
                }
                catch
                {
                    return null;
                }
            }));
        }
        var daevTasks = boardIds.Select(bid => Task.Run(async () =>
        {
            try
            {
                JsonElement data = await FetchDaevanion(charId, apiServerId, bid);
                return ((int bid, JsonElement data)?)(bid, data);
            }
            catch
            {
                return null;
            }
        })).ToList();
        await Task.WhenAll(
            Task.WhenAll(itemTasks),
            Task.WhenAll(daevTasks)
        );
        Dictionary<int, JsonElement> itemDetails = new Dictionary<int, JsonElement>();
        foreach (var t in itemTasks)
        {
            var result = t.Result;
            if (result.HasValue)
            {
                itemDetails[result.Value.slot] = result.Value.detail;
            }
        }
        Dictionary<int, JsonElement> daevanionDetails = new Dictionary<int, JsonElement>();
        foreach (var t in daevTasks)
        {
            var result = t.Result;
            if (result.HasValue)
            {
                daevanionDetails[result.Value.bid] = result.Value.data;
            }
        }
        return new CharacterData
        {
            CharacterId = charId,
            ServerId = apiServerId,
            ServerName = found.Value.ServerName,
            Profile = profile,
            StatData = statData,
            TitleList = titleList,
            SkillList = skillList,
            WingName = wingName,
            ClassName = className,
            ItemDetails = itemDetails,
            DaevanionDetails = daevanionDetails,
            SlotExceed = slotExceed
        };
    }

    /// Fetch character's skill levels and combat score data.
    /// Returns (combatPower, skillLevels dictionary: skillName → level).
    public static async Task<CharacterSkillData?> FetchCharacterData(string name, int serverId)
    {
        Log($"FetchCharacterData: name={name}, serverId={serverId}");
        try
        {
            var found = await SearchCharacter(name, serverId, 0);
            if (found is null) { Log($"  Character not found: {name}"); return null; }

            var charId = found.Value.CharId;
            var apiServerId = found.Value.ServerId > 0 ? found.Value.ServerId : serverId;

            // Fetch info + equipment in parallel.
            var infoTask = FetchInfo(charId, apiServerId);
            var equipTask = FetchEquipment(charId, apiServerId);
            await Task.WhenAll(infoTask, equipTask);

            var info = infoTask.Result;
            var equip = equipTask.Result;

            // Extract combatPower and combatScore from profile.
            int combatPower = 0;
            int combatScore = 0;
            if (info.TryGetProperty("profile", out var profile))
            {
                combatPower = GetInt(profile, "combatPower");
                combatScore = GetInt(profile, "combatScore");
                if (combatScore == 0) combatScore = GetInt(profile, "artifactScore");
                if (combatScore == 0) combatScore = GetInt(profile, "characterScore");
                Log($"  Profile: CP={combatPower}, Score={combatScore}");
                // Dump profile keys for debugging field names.
                if (combatPower == 0 && profile.ValueKind == JsonValueKind.Object)
                {
                    var keys = new List<string>();
                    foreach (var prop in profile.EnumerateObject())
                        keys.Add($"{prop.Name}={prop.Value.ToString()[..Math.Min(prop.Value.ToString().Length, 30)]}");
                    Log($"  Profile keys: {string.Join(", ", keys)}");
                }
            }
            else
            {
                // Dump top-level keys of info response.
                if (info.ValueKind == JsonValueKind.Object)
                {
                    var keys = new List<string>();
                    foreach (var prop in info.EnumerateObject()) keys.Add(prop.Name);
                    Log($"  Info has no 'profile'. Keys: {string.Join(", ", keys)}");
                }
            }

            // Extract skill list from equipment response.
            var skills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var skillArray = GetNestedArray(equip, "skill", "skillList");
            var dpSkills = ExtractEquippedDpSkills(skillArray);
            foreach (var s in skillArray)
            {
                string skillName = GetString(s, "skillName") ?? GetString(s, "name") ?? "";
                int level = GetInt(s, "skillLevel");
                if (level == 0) level = GetInt(s, "level_int");
                if (!string.IsNullOrWhiteSpace(skillName) && level > 0)
                {
                    // Keep highest level if duplicate names exist.
                    if (!skills.TryGetValue(skillName, out var existing) || level > existing)
                        skills[skillName] = level;
                }
            }

            return new CharacterSkillData
            {
                CharacterId = charId,
                ServerId = apiServerId,
                ServerName = found.Value.ServerName,
                CombatPower = combatPower,
                CombatScore = combatScore,
                SkillLevels = skills,
                DpSkills = dpSkills,
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PlayncClient] fetch failed for {name}: {ex.Message}");
            return null;
        }
    }

    // ── JSON helpers ──

    private static string? GetString(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v))
        {
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
        }
        return null;
    }

    private static int GetInt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetInt32();
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var n)) return n;
        return 0;
    }

    private static List<JsonElement> GetNestedArray(JsonElement root, string obj, string arr)
    {
        var result = new List<JsonElement>();
        JsonElement target = root;
        if (root.TryGetProperty(obj, out var sub)) target = sub;
        if (target.TryGetProperty(arr, out var array) && array.ValueKind == JsonValueKind.Array)
            foreach (var item in array.EnumerateArray())
                result.Add(item);
        return result;
    }

    public static List<CharacterDpSkill> ExtractEquippedDpSkills(IEnumerable<JsonElement> skillArray)
    {
        var result = new List<CharacterDpSkill>();
        foreach (var s in skillArray)
        {
            string category = GetString(s, "category") ?? "";
            if (!string.Equals(category, "Dp", StringComparison.OrdinalIgnoreCase)) continue;
            if (GetInt(s, "acquired") != 1 || GetInt(s, "equip") != 1) continue;

            string skillName = GetString(s, "skillName") ?? GetString(s, "name") ?? "";
            if (string.IsNullOrWhiteSpace(skillName)) continue;
            int level = GetInt(s, "skillLevel");
            if (level == 0) level = GetInt(s, "level_int");

            result.Add(new CharacterDpSkill
            {
                Id = GetInt(s, "id"),
                Name = skillName,
                SkillLevel = level,
                Icon = GetString(s, "icon") ?? "",
            });
        }

        return result;
    }
}

internal sealed class CharacterSkillData
{
    public string CharacterId { get; set; } = "";
    public int ServerId { get; set; }
    public string ServerName { get; set; } = "";
    public int CombatPower { get; set; }
    public int CombatScore { get; set; }
    public Dictionary<string, int> SkillLevels { get; set; } = new();
    public List<CharacterDpSkill> DpSkills { get; set; } = new();
}

public sealed class CharacterDpSkill
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SkillLevel { get; set; }
    public string Icon { get; set; } = "";
}
