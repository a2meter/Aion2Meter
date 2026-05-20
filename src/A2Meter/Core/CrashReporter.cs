using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace A2Meter.Core;

/// Reads accumulated crash.log entries and posts new ones to the crash-report
/// proxy. The proxy holds the GitHub token and creates/updates issues — this
/// client never sees a credential. Off by default; user must opt in through
/// SettingsPanelForm → 진단 → 익명 크래시 리포트.
internal static class CrashReporter
{
    private const string ProxyEndpoint = "https://a2meter-crash-proxy.a2meter.workers.dev/report";
    private const int MaxBodyChars     = 32_000;
    private const int MaxEntriesPerRun = 5;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static string CrashLogPath =>
        Path.Combine(AppContext.BaseDirectory, "crash.log");

    private static string CrashLogPathAlt =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "A2Meter", "crash.log");

    private static string SentHashesPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "A2Meter", "crash.sent.hashes");

    public static async Task ReportPendingAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (!settings.CrashReportingEnabled) return;

        var activatedAt = ParseActivatedAt(settings.CrashReportingActivatedAt);
        if (activatedAt is null) return;

        var entries = ReadAllEntries();
        if (entries.Count == 0) return;

        var sentHashes = LoadSentHashes();
        int sentThisRun = 0;

        foreach (var entry in entries)
        {
            if (ct.IsCancellationRequested) break;
            if (sentThisRun >= MaxEntriesPerRun) break;
            if (entry.Timestamp < activatedAt.Value) continue;

            var masked = MaskSensitive(entry.Raw);
            string hash = Sha256Hex(NormalizeForHash(masked));
            if (sentHashes.Contains(hash)) continue;

            if (await TryPostAsync(hash, entry, masked, ct).ConfigureAwait(false))
            {
                AppendSentHash(hash);
                sentHashes.Add(hash);
                sentThisRun++;
            }
            else
            {
                // Single transient failure aborts the whole run — retry next start.
                break;
            }
        }
    }

    // ── parsing ──────────────────────────────────────────────────────────────

    private sealed record CrashEntry(DateTime Timestamp, string Source, string Raw);

    private static readonly Regex HeaderRegex = new(
        @"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\] (\w+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static List<CrashEntry> ReadAllEntries()
    {
        var result = new List<CrashEntry>();
        var seen = new HashSet<string>();
        foreach (var path in new[] { CrashLogPath, CrashLogPathAlt })
        {
            if (!File.Exists(path)) continue;
            string text;
            try { text = File.ReadAllText(path); } catch { continue; }
            ParseEntries(text, result, seen);
        }
        return result;
    }

    private static void ParseEntries(string text, List<CrashEntry> sink, HashSet<string> seen)
    {
        var matches = HeaderRegex.Matches(text);
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            int start = m.Index;
            int end = (i + 1 < matches.Count) ? matches[i + 1].Index : text.Length;
            string raw = text[start..end].TrimEnd('\r', '\n', ' ');
            if (!seen.Add(raw)) continue; // de-dup between primary and alt log
            if (!DateTime.TryParse(m.Groups[1].Value, out var ts)) continue;
            sink.Add(new CrashEntry(ts, m.Groups[2].Value, raw));
        }
    }

    private static DateTime? ParseActivatedAt(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        if (!DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return null;
        return dt.ToLocalTime();
    }

    // ── masking ──────────────────────────────────────────────────────────────

    private static readonly Regex UserPathRegex = new(
        @"([A-Z]:\\Users\\)[^\\\s""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CharServerRegex = new(
        @"[가-힣A-Za-z0-9_]{2,32}\[(?:시엘|네자칸|바이젤|카이시넬|유스티엘|아리엘|프레기온|메스람타에다|히타니에|나니아|타하바타|루터스|페르노스|다미누|카사카|바카르마|챈가룽|코치룽|이슈타르|티아마트|포에타|이스라펠|지켈|트리니엘|루미엘|마르쿠탄|아스펠|에레슈키갈|브리트라|네몬|하달|루드라|울고른|무닌|오다르|젠카카|크로메데|콰이링|바바룽|파프니르|인드나흐|이스할겐)\]",
        RegexOptions.Compiled);

    private static readonly Regex Ipv4Regex = new(
        @"\b(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\b",
        RegexOptions.Compiled);

    internal static string MaskSensitive(string raw)
    {
        string s = UserPathRegex.Replace(raw, "$1<user>");
        s = CharServerRegex.Replace(s, "<player>[<server>]");
        s = Ipv4Regex.Replace(s, "<ip>");
        if (s.Length > MaxBodyChars) s = s[..MaxBodyChars] + "\n…(truncated)";
        return s;
    }

    /// Strips the "[yyyy-MM-dd HH:mm:ss] Source" header line so the hash
    /// reflects the exception/stack identity, not the moment of occurrence.
    /// Without this, the same crash at two different times produces two issues.
    private static readonly Regex HeaderLineRegex = new(
        @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] \w+\r?\n",
        RegexOptions.Compiled);

    internal static string NormalizeForHash(string masked) =>
        HeaderLineRegex.Replace(masked, "").Trim();

    // ── transport ────────────────────────────────────────────────────────────

    private static async Task<bool> TryPostAsync(string hash, CrashEntry entry, string maskedBody, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                hash,
                source       = entry.Source,
                timestamp    = entry.Timestamp.ToString("o"),
                app_version  = AppVersion(),
                os           = Environment.OSVersion.VersionString,
                dotnet       = Environment.Version.ToString(),
                body         = maskedBody,
            };
            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(ProxyEndpoint, content, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string AppVersion()
    {
        try
        {
            var asm = typeof(CrashReporter).Assembly.GetName();
            return asm.Version?.ToString() ?? "0.0.0";
        }
        catch { return "0.0.0"; }
    }

    // ── sent-hash bookkeeping ────────────────────────────────────────────────

    private static HashSet<string> LoadSentHashes()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(SentHashesPath)) return set;
        try
        {
            foreach (var line in File.ReadAllLines(SentHashesPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 64) set.Add(trimmed);
            }
        }
        catch { }
        return set;
    }

    private static void AppendSentHash(string hash)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SentHashesPath)!);
            File.AppendAllText(SentHashesPath, hash + "\n");
        }
        catch { }
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(64);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
