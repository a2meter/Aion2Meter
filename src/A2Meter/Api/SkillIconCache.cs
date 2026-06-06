using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace A2Meter.Api;

/// Downloads and caches skill icon PNGs.
/// Icons are stored on disk under %APPDATA%/A2Meter/skill_icons/.
/// Thread-safe singleton; lookups are non-blocking (returns cached Image or null).
internal sealed class SkillIconCache
{
    private const string OldBase = "https://assets.playnccdn.com/static-aion2-gamedata/resources/";
    private const string NewBase = "https://cdn.jsdelivr.net/gh/a2meter/a2meter.github.io@v1.0.1/Assets/Icon/Skill/";
    private static readonly Lazy<SkillIconCache> _instance = new(() => new());
    public static SkillIconCache Instance => _instance.Value;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "skill_icons");

    /// skill name → icon URL (from known_skills_catalog.json)
    private readonly Dictionary<string, string> _urlMap = new(StringComparer.Ordinal);
    /// skill name → loaded Image (in-memory cache)
    private readonly ConcurrentDictionary<string, Image> _images = new();
    /// prevents duplicate downloads
    private readonly ConcurrentDictionary<string, byte> _pending = new();

    /// Fired when a new icon finishes downloading. Subscribers can invalidate their UI.
    public event Action? IconReady;

    private SkillIconCache()
    {
        Directory.CreateDirectory(CacheDir);
        LoadCatalog();
    }

    /// Returns the cached icon or null. Kicks off a background download if not cached.
    public Image? Get(string skillName)
    {
        if (_images.TryGetValue(skillName, out var img))
            return img;

        // Try load from disk
        string? path = DiskPath(skillName);
        if (path != null && File.Exists(path))
        {
            try
            {
                // Load into a memory-backed Image so the file isn't locked.
                using var fs = File.OpenRead(path);
                var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Position = 0;
                img = Image.FromStream(ms);
                _images[skillName] = img;
                return img;
            }
            catch { /* fall through to download */ }
        }

        // Start async download
        if (_urlMap.TryGetValue(skillName, out var url) && _pending.TryAdd(skillName, 0))
            _ = DownloadAsync(skillName, url);

        return null;
    }

    private async Task DownloadAsync(string skillName, string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            string? path = DiskPath(skillName);
            if (path != null)
            {
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            }
            var ms = new MemoryStream(bytes);
            var img = Image.FromStream(ms);
            _images[skillName] = img;
            IconReady?.Invoke();
        }
        catch { /* best-effort */ }
        finally { _pending.TryRemove(skillName, out _); }
    }

    private string? DiskPath(string skillName)
    {
        // Use the URL filename as the cache key if available; otherwise hash the name.
        if (_urlMap.TryGetValue(skillName, out var url))
        {
            var seg = url.AsSpan();
            int lastSlash = seg.LastIndexOf('/');
            if (lastSlash >= 0)
                return Path.Combine(CacheDir, seg[(lastSlash + 1)..].ToString());
        }
        return null;
    }

    private void LoadCatalog()
    {
        try
        {
            foreach (var skill in GameDataClient.Snapshot.Skills)
            {
                if (!string.IsNullOrWhiteSpace(skill.Name) && !string.IsNullOrWhiteSpace(skill.IconUrl))
                    _urlMap.TryAdd(skill.Name, skill.IconUrl.Replace(OldBase, NewBase));
            }
        }
        catch { }
    }
}
