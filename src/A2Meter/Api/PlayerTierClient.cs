using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;

namespace A2Meter.Api;

/// HTTP client for A2Web's /api/players/tier endpoint.
/// Fetches the cached tier for a (player, server) across all allowed dungeons.
internal static class PlayerTierClient
{
    private const string DefaultBaseUrl = "https://www.aion2meter.com";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(6),
        DefaultRequestHeaders =
        {
            { "User-Agent", "A2Meter-TierClient" }
        },
    };

    public static string BaseUrl { get; set; } = DefaultBaseUrl;

    /// Fetch all dungeon tiers for a (player, server). Returns null on any failure.
    public static async Task<TierResponse?> FetchAsync(string playerName, int serverId)
    {
        if (string.IsNullOrWhiteSpace(playerName) || serverId <= 0) return null;
        try
        {
            string url = BaseUrl
                + "/api/players/tier?name=" + HttpUtility.UrlEncode(playerName)
                + "&serverId=" + serverId;
            using var resp = await Http.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<TierResponse>().ConfigureAwait(false);
        }
        catch { return null; }
    }

    public sealed class TierResponse
    {
        [JsonPropertyName("name")]     public string Name { get; set; } = "";
        [JsonPropertyName("serverId")] public int ServerId { get; set; }
        [JsonPropertyName("dungeons")] public List<DungeonTier> Dungeons { get; set; } = new();
    }

    public sealed class DungeonTier
    {
        [JsonPropertyName("dungeonId")]    public int DungeonId { get; set; }
        [JsonPropertyName("sampleCount")]  public int SampleCount { get; set; }
        [JsonPropertyName("avgDps")]       public long AvgDps { get; set; }
        [JsonPropertyName("latestCp")]     public int LatestCp { get; set; }
        [JsonPropertyName("baselineDps")]  public long BaselineDps { get; set; }
        [JsonPropertyName("tierScore")]    public double TierScore { get; set; }
        [JsonPropertyName("zScore")]       public double ZScore { get; set; }
        [JsonPropertyName("tier")]         public string Tier { get; set; } = "";
    }
}
