using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Namter.Cli.Comparison;

public enum FixtureEvidence { PcapCandidate, ReducerOnly }
public sealed record ReadableSummary(string BossName, uint BossActorId, uint BossMobCode, ulong BossMaxHp, uint ContentCode, long StartTimestampMs, long EndTimestampMs, ulong TotalDamage, int ParticipantCount, int DamageEventRows, int BuffWindowRows, int BuffUptimeRows);
public sealed record ReadableParticipant(uint ActorId, string Name, string ServerName, ushort Job, ulong Damage, ulong Healing);
public sealed record ReadableEvent(int Index, long OffsetMs, bool IsDot, uint ActorId, uint TargetId, uint SkillId, ulong Damage, ulong MultiDamage, ulong Healing, uint SpecialMask, string SpecialFlags);
public sealed record ReadableBuffWindow(int Index, long StartOffsetMs, long EndOffsetMs, ulong DurationMs, string Kind, uint TargetId, uint OwnerId, uint BuffId, uint SkillId);
public sealed record ReadableBuffUptime(int Index, uint ActorId, uint BuffId, uint SkillId, ulong UptimeMs, uint WindowCount);
public sealed record ReadableFixture(string DirectoryPath, FixtureEvidence Evidence, ReadableSummary Summary, ImmutableArray<ReadableParticipant> Participants, ImmutableArray<ReadableEvent> Events, ImmutableArray<ReadableBuffWindow> BuffWindows, ImmutableArray<ReadableBuffUptime> BuffUptimes);

public static class ReadableFixtureLoader
{
    private const int MaxRows = 1_000_000, MaxLineBytes = 16_384, MaxSummaryBytes=262_144, MaxSummaryLines=4096;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string[] ParticipantHeader = ["rank","actorId","name","serverName","job","damage","dps","hits","healing","selfHealing","otherHealing","hps","healHits"];
    private static readonly string[] EventHeader = ["index","offsetMs","timestampUtc","isDot","actorId","actorName","targetId","targetName","skillId","damage","multiDamage","heal","totalDamage","specialMask","specialFlags","skillLevel","baseSkillLevel","actorIndex","targetIndex","skillIndex"];
    private static readonly string[] WindowHeader = ["index","startOffsetMs","endOffsetMs","durationMs","startUtc","endUtc","kind","targetId","targetName","ownerId","ownerName","buffId","skillId","skillLevel","baseSkillLevel"];
    private static readonly string[] UptimeHeader = ["index","actorIndex","actorId","actorName","buffIndex","buffId","skillId","buffName","buffType","uptimeMs","windowCount","uptimePct"];

    public static ReadableFixture Load(string directory)
    {
        string root = Path.GetFullPath(directory); if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        ReadableSummary summary = ReadSummary(Path.Combine(root, "summary.txt"));
        var participants = ReadCsv(Path.Combine(root, "participants.csv"), ParticipantHeader, ParseParticipant, x => x.ActorId);
        var events = ReadCsv(Path.Combine(root, "events.csv"), EventHeader, ParseEvent, x => x.Index);
        var windows = ReadCsv(Path.Combine(root, "buff-windows.csv"), WindowHeader, ParseWindow, x => x.Index);
        var uptimes = ReadCsv(Path.Combine(root, "buff-uptimes.csv"), UptimeHeader, ParseUptime, x => x.Index);
        if (participants.Length != summary.ParticipantCount || events.Length != summary.DamageEventRows || windows.Length != summary.BuffWindowRows || uptimes.Length != summary.BuffUptimeRows)
            throw new InvalidDataException("Readable fixture row counts do not match summary.txt.");
        bool basilus = Path.GetFileName(root).Contains("바실루스", StringComparison.Ordinal);
        return new(root, basilus ? FixtureEvidence.ReducerOnly : FixtureEvidence.PcapCandidate, summary, participants, events, windows, uptimes);
    }

    private static ReadableSummary ReadSummary(string path)
    {
        if(!File.Exists(path))throw new FileNotFoundException("Required readable fixture file is missing.",path);if(new FileInfo(path).Length>MaxSummaryBytes)throw new InvalidDataException("summary.txt exceeds its byte bound.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        int lineCount=0;foreach (string line in ReadLines(path))
        {
            if(++lineCount>MaxSummaryLines)throw new InvalidDataException("summary.txt exceeds its line-count bound.");
            string trimmed = line.Trim(); int colon = trimmed.IndexOf(':'); if (colon <= 0) continue;
            string key = trimmed[..colon], value = trimmed[(colon + 1)..].Trim();
            if (RequiredSummaryKeys.Contains(key) && !values.TryAdd(key, value)) throw new InvalidDataException($"Duplicate summary field '{key}'.");
        }
        string S(string k) => values.TryGetValue(k, out string? v) ? v : throw new InvalidDataException($"Missing summary field '{k}'.");
        return new(S("Boss"), U32(S("BossActorId")), U32(S("BossMobCode")), U64(S("BossMaxHp")), U32(S("ContentCode")), Time(S("StartUtc")), Time(S("EndUtc")), U64(S("TotalDamage")), I32(S("ParticipantCount")), I32(S("DamageEventRows")), I32(S("BuffWindowRows")), I32(S("BuffUptimeRows")));
    }
    private static readonly HashSet<string> RequiredSummaryKeys = ["Boss","BossActorId","BossMobCode","BossMaxHp","ContentCode","StartUtc","EndUtc","TotalDamage","ParticipantCount","DamageEventRows","BuffWindowRows","BuffUptimeRows"];

    private static ImmutableArray<T> ReadCsv<T,TKey>(string path, string[] expectedHeader, Func<string[],T> parse, Func<T,TKey> key) where TKey : notnull
    {
        using IEnumerator<string> lines = ReadLines(path).GetEnumerator(); if (!lines.MoveNext()) throw new InvalidDataException($"CSV is empty: {path}");
        string[] header = Csv(lines.Current); if (!header.SequenceEqual(expectedHeader, StringComparer.Ordinal)) throw new InvalidDataException($"CSV header mismatch: {Path.GetFileName(path)}");
        var result = ImmutableArray.CreateBuilder<T>(); var keys = new HashSet<TKey>();
        while (lines.MoveNext())
        {
            if (string.IsNullOrEmpty(lines.Current)) throw new InvalidDataException("Blank CSV rows are not allowed.");
            if (result.Count >= MaxRows) throw new InvalidDataException("CSV row bound exceeded.");
            string[] fields = Csv(lines.Current); if (fields.Length != expectedHeader.Length) throw new InvalidDataException($"CSV field count mismatch in {Path.GetFileName(path)}.");
            T row = parse(fields); if (!keys.Add(key(row))) throw new InvalidDataException($"Duplicate row key in {Path.GetFileName(path)}."); result.Add(row);
        }
        return result.ToImmutable();
    }
    private static IEnumerable<string> ReadLines(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Required readable fixture file is missing.", path);
        using var reader = new StreamReader(path, Utf8, detectEncodingFromByteOrderMarks:true);
        while (reader.ReadLine() is { } line) { if (Utf8.GetByteCount(line) > MaxLineBytes) throw new InvalidDataException("Readable fixture line bound exceeded."); yield return line; }
    }
    private static string[] Csv(string line)
    {
        var fields = new List<string>(); int offset=0;
        while(offset<line.Length)
        {
            if(line[offset++]!='"')throw new InvalidDataException("CSV fields must use canonical quotes.");var value=new StringBuilder();bool closed=false;
            while(offset<line.Length){char c=line[offset++];if(c!='"'){value.Append(c);continue;}if(offset<line.Length&&line[offset]=='"'){value.Append('"');offset++;continue;}closed=true;break;}
            if(!closed)throw new InvalidDataException("Unterminated CSV quote.");fields.Add(value.ToString());if(offset==line.Length)break;if(line[offset++]!=',')throw new InvalidDataException("Unexpected data after CSV quote.");if(offset==line.Length)throw new InvalidDataException("Trailing empty CSV field is not canonical.");
        }
        if(fields.Count==0)throw new InvalidDataException("CSV row is empty.");return fields.ToArray();
    }
    private static ReadableParticipant ParseParticipant(string[] f) => new(U32(f[1]), Text(f[2]), Text(f[3]), U16(f[4]), U64(f[5]), U64(f[8]));
    private static ReadableEvent ParseEvent(string[] f) { bool dot = bool.TryParse(f[3], out bool b) ? b : throw new InvalidDataException("Invalid Boolean."); ulong d=U64(f[9]),m=U64(f[10]); if(U64(f[12]) != checked(d+m)) throw new InvalidDataException("Event totalDamage is inconsistent."); return new(I32(f[0]), I64(f[1]), dot, U32(f[4]), U32(f[6]), U32(f[8]), d,m,U64(f[11]),U32(f[13]),Text(f[14])); }
    private static ReadableBuffWindow ParseWindow(string[] f) { long s=I64(f[1]),e=I64(f[2]); ulong duration=U64(f[3]); if(e<s || checked((ulong)(e-s)) != duration) throw new InvalidDataException("Buff window duration is inconsistent."); return new(I32(f[0]),s,e,duration,Text(f[6]),U32(f[7]),U32(f[9]),U32(f[11]),U32(f[12])); }
    private static ReadableBuffUptime ParseUptime(string[] f) => new(I32(f[0]),U32(f[2]),U32(f[5]),U32(f[6]),U64(f[9]),U32(f[10]));
    private static string Text(string value) { if(Utf8.GetByteCount(value)>4096) throw new InvalidDataException("Readable text field bound exceeded."); return value; }
    private static string Clean(string v)=>v.Replace(",", "", StringComparison.Ordinal);
    private static uint U32(string v)=>uint.TryParse(Clean(v),NumberStyles.None,CultureInfo.InvariantCulture,out uint x)?x:throw new InvalidDataException($"Invalid uint '{v}'.");
    private static ushort U16(string v)=>ushort.TryParse(Clean(v),NumberStyles.None,CultureInfo.InvariantCulture,out ushort x)?x:throw new InvalidDataException($"Invalid ushort '{v}'.");
    private static ulong U64(string v)=>ulong.TryParse(Clean(v),NumberStyles.None,CultureInfo.InvariantCulture,out ulong x)?x:throw new InvalidDataException($"Invalid ulong '{v}'.");
    private static int I32(string v)=>int.TryParse(Clean(v),NumberStyles.None,CultureInfo.InvariantCulture,out int x)&&x>=0?x:throw new InvalidDataException($"Invalid int '{v}'.");
    private static long I64(string v)=>long.TryParse(Clean(v),NumberStyles.Integer,CultureInfo.InvariantCulture,out long x)&&x>=0?x:throw new InvalidDataException($"Invalid long '{v}'.");
    private static long Time(string v)=>DateTimeOffset.TryParse(v,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var x)?x.ToUnixTimeMilliseconds():throw new InvalidDataException($"Invalid timestamp '{v}'.");
}
