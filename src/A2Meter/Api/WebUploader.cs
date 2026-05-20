using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using A2Meter.Core;
using A2Meter.Dps;

namespace A2Meter.Api;

/// Uploads combat records to the A2Web statistics server.
/// Auto-registers on first launch (no user action needed).
internal sealed class WebUploader : IDisposable
{
    private static readonly string StateDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter");
    private static readonly string ClientIdPath = Path.Combine(StateDir, "client_id");
    private static readonly string TokenPath    = Path.Combine(StateDir, "web_token");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string _clientId = "";
    private string _token = "";
    private bool _initialized;

    public string BaseUrl { get; set; } = "https://api.aion2meter.com";

    public WebUploader()
    {
        _clientId = LoadOrCreateClientId();
        _token = LoadToken();
    }

    /// Upload a combat record in the background. Fire-and-forget safe.
    public async void UploadAsync(CombatRecord record)
    {
        try
        {
            await EnsureTokenAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(_token)) return;

            var json = JsonSerializer.Serialize(record, JsonOpts);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/upload")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Token expired — refresh and retry once.
                _token = "";
                await EnsureTokenAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(_token)) return;

                using var retry = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/upload")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                using var _ = await _http.SendAsync(retry).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best effort — don't crash the meter on upload failure.
        }
    }

    private async Task EnsureTokenAsync()
    {
        if (!string.IsNullOrEmpty(_token)) return;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(_token)) return;

            var body = JsonSerializer.Serialize(new { clientId = _clientId });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/auth/token")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;

            var respJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(respJson);
            if (doc.RootElement.TryGetProperty("token", out var tok))
            {
                _token = tok.GetString() ?? "";
                SaveToken(_token);
            }
        }
        catch { /* best effort */ }
        finally { _lock.Release(); }
    }

    // ── Persistence helpers ──

    private static string LoadOrCreateClientId()
    {
        try
        {
            Directory.CreateDirectory(StateDir);
            if (File.Exists(ClientIdPath))
            {
                var id = File.ReadAllText(ClientIdPath).Trim();
                if (id.Length == 36) return id;
            }
            var newId = Guid.NewGuid().ToString();
            File.WriteAllText(ClientIdPath, newId);
            return newId;
        }
        catch { return Guid.NewGuid().ToString(); }
    }

    private static string LoadToken()
    {
        try { return File.Exists(TokenPath) ? File.ReadAllText(TokenPath).Trim() : ""; }
        catch { return ""; }
    }

    private static void SaveToken(string token)
    {
        try
        {
            Directory.CreateDirectory(StateDir);
            File.WriteAllText(TokenPath, token);
        }
        catch { /* best effort */ }
    }

    public void Dispose()
    {
        _http.Dispose();
        _lock.Dispose();
    }
}
