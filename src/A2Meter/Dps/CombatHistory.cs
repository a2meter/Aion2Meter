using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2Meter.Dps;

/// Persisted combat record — one per boss kill / session end.
internal sealed class CombatRecord
{
    public DateTime Timestamp { get; set; }
    /// UUID assigned at combat start — used to dedup saves within the same session.
    public string? SessionId { get; set; }
    public string? BossName { get; set; }
    public double DurationSec { get; set; }
    public long TotalDamage { get; set; }
    public long AverageDps { get; set; }
    public long PeakDps { get; set; }
    public DpsSnapshot Snapshot { get; set; } = new();
    public List<TimelineEntry>? Timeline { get; set; }
    public List<HitLogEntry>? HitLog { get; set; }
    public List<BuffUptimeDto>? TargetBuffs { get; set; }
    public int? DungeonId { get; set; }
}

/// Saves / loads combat records as JSON files under %AppData%\A2Meter\history\.
internal sealed class CombatHistory
{
    private static readonly string HistoryDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "history");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly List<CombatRecord> _records = new();
    public IReadOnlyList<CombatRecord> Records => _records;

    public CombatHistory()
    {
        LoadAll();
    }

    /// Returns true if saved, false if skipped as a near-duplicate of the previous record.
    public bool Save(CombatRecord record)
    {
        // Dedup by SessionId: if a record with the same session already exists,
        // overwrite it in-place instead of creating a duplicate entry.
        if (!string.IsNullOrEmpty(record.SessionId))
        {
            int idx = _records.FindIndex(r => r.SessionId == record.SessionId);
            if (idx >= 0)
            {
                var old = _records[idx];
                _records[idx] = record;
                // Delete the old file (timestamp may differ).
                try
                {
                    string oldFile = Path.Combine(HistoryDir, $"{old.Timestamp:yyyyMMdd-HHmmss}.json");
                    if (File.Exists(oldFile)) File.Delete(oldFile);
                }
                catch { }
                // Write updated record.
                try
                {
                    Directory.CreateDirectory(HistoryDir);
                    string fileName = $"{record.Timestamp:yyyyMMdd-HHmmss}.json";
                    File.WriteAllText(Path.Combine(HistoryDir, fileName),
                        JsonSerializer.Serialize(record, JsonOpts));
                }
                catch { }
                return true;
            }
        }

        // Skip near-duplicate saves: same boss as the most recent record AND any
        // player has matching accumulated damage with the same-named player there.
        // This catches duplicate saves from different SessionIds for the same fight.
        if (_records.Count > 0 && IsDuplicateOfPrevious(_records[0], record))
            return false;

        _records.Insert(0, record);
        TrimOld();
        try
        {
            Directory.CreateDirectory(HistoryDir);
            string fileName = $"{record.Timestamp:yyyyMMdd-HHmmss}.json";
            string path = Path.Combine(HistoryDir, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOpts));
        }
        catch { /* best effort */ }
        return true;
    }

    private static bool IsDuplicateOfPrevious(CombatRecord prev, CombatRecord curr)
    {
        if (prev.BossName != curr.BossName) return false;
        var prevPlayers = prev.Snapshot?.Players;
        var currPlayers = curr.Snapshot?.Players;
        if (prevPlayers is null || prevPlayers.Count == 0) return false;
        if (currPlayers is null || currPlayers.Count == 0) return false;

        foreach (var c in currPlayers)
        {
            if (c.TotalDamage <= 0) continue;
            foreach (var p in prevPlayers)
            {
                if (p.Name == c.Name && p.TotalDamage == c.TotalDamage)
                    return true;
            }
        }
        return false;
    }

    private void LoadAll()
    {
        _records.Clear();
        if (!Directory.Exists(HistoryDir)) return;
        try
        {
            var files = Directory.GetFiles(HistoryDir, "*.json");
            Array.Sort(files);
            Array.Reverse(files); // newest first
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var rec = JsonSerializer.Deserialize<CombatRecord>(json, JsonOpts);
                    if (rec != null) _records.Add(rec);
                }
                catch { /* skip corrupt files */ }
            }
            TrimOld();
        }
        catch { }
    }

    /// Keep at most 100 records.
    private void TrimOld()
    {
        const int MaxRecords = 100;
        while (_records.Count > MaxRecords)
        {
            var oldest = _records[_records.Count - 1];
            _records.RemoveAt(_records.Count - 1);
            try
            {
                string fileName = $"{oldest.Timestamp:yyyyMMdd-HHmmss}.json";
                string path = Path.Combine(HistoryDir, fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }
}
