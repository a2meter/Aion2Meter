using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Namter.Cli.Comparison;

public sealed record FieldDiscrepancy(string Field, string Expected, string Actual, string Provenance);
public sealed record ComparisonReport(bool IsMatch, ImmutableArray<string> Matches, ImmutableArray<string> Tolerances, ImmutableArray<FieldDiscrepancy> Missing, ImmutableArray<FieldDiscrepancy> Extra, ImmutableArray<FieldDiscrepancy> Discrepancies, string ActualProvenance, string ExpectedProvenance)
{
    public byte[] WriteStableJson()
    {
        using var stream = new MemoryStream(); using var w = new Utf8JsonWriter(stream);
        w.WriteStartObject(); w.WriteBoolean("isMatch",IsMatch); Array(w,"matches",Matches); Array(w,"tolerances",Tolerances); Differences(w,"missing",Missing); Differences(w,"extra",Extra); Differences(w,"discrepancies",Discrepancies); w.WriteString("actualProvenance",ActualProvenance); w.WriteString("expectedProvenance",ExpectedProvenance);w.WriteString("fixtureEvidence",FixtureEvidence); w.WriteEndObject(); w.Flush(); return stream.ToArray();
    }
    private static void Array(Utf8JsonWriter w,string n,ImmutableArray<string> values){w.WriteStartArray(n);foreach(string v in values)w.WriteStringValue(v);w.WriteEndArray();}
    private static void Differences(Utf8JsonWriter w,string n,ImmutableArray<FieldDiscrepancy> values){w.WriteStartArray(n);foreach(var v in values.OrderBy(x=>x.Field,StringComparer.Ordinal).ThenBy(x=>x.Expected,StringComparer.Ordinal).ThenBy(x=>x.Actual,StringComparer.Ordinal)){w.WriteStartObject();w.WriteString("field",v.Field);w.WriteString("expected",v.Expected);w.WriteString("actual",v.Actual);w.WriteString("provenance",v.Provenance);w.WriteEndObject();}w.WriteEndArray();}
    public string FixtureEvidence { get; init; } = "PcapCandidate";
}

public static class GoldenComparator
{
    public const int MaxActualRecordBytes=64*1024*1024;
    public static ComparisonReport Compare(string actualRecordPath, ReadableFixture expected)
    {
        try{return CompareCore(actualRecordPath,expected) with{FixtureEvidence=expected.Evidence.ToString()};}
        catch(InvalidDataException){throw;}
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or OverflowException or ArgumentException or KeyNotFoundException){throw new InvalidDataException("Actual encounter JSON is malformed or has an incompatible schema.",ex);}
    }
    private static ComparisonReport CompareCore(string actualRecordPath, ReadableFixture expected)
    {
        using JsonDocument doc = JsonDocument.Parse(ReadActualRecord(actualRecordPath), new JsonDocumentOptions{MaxDepth=64}); JsonElement root=doc.RootElement;
        var matches=ImmutableArray.CreateBuilder<string>(); var diffs=ImmutableArray.CreateBuilder<FieldDiscrepancy>(); var missing=ImmutableArray.CreateBuilder<FieldDiscrepancy>(); var extra=ImmutableArray.CreateBuilder<FieldDiscrepancy>();
        JsonElement identity=Req(root,"encounter"); Eq("bossActorId",expected.Summary.BossActorId,U32(identity,"bossActorId")); Eq("bossCode",expected.Summary.BossMobCode,U32(identity,"bossCode"));Eq("bossName",expected.Summary.BossName,Str(identity,"name"));Eq<ulong?>("bossMaxHp",expected.Summary.BossMaxHp,NullableU64(identity,"maxHp")); Eq("contentId",expected.Summary.ContentCode,U32(identity,"contentId")); Eq("startTimestampMs",expected.Summary.StartTimestampMs,I64(root,"startTimestampMs")); Eq("endTimestampMs",expected.Summary.EndTimestampMs,I64(root,"endTimestampMs"));
        _=U32(identity,"dungeonId"); matches.Add("dungeonId:not-present-in-readable");
        var actualParticipants=UniqueBy(Req(root,"participants").EnumerateArray(),x=>U32(x,"actorId"),"actual participant actorId");
        foreach(var p in expected.Participants){if(!actualParticipants.Remove(p.ActorId,out JsonElement a)){missing.Add(new($"participant[{p.ActorId}]",p.Name,"<missing>","readable.participants.csv"));continue;} ulong participantDamage=checked(U64(a,"damage")+U64(a,"dotDamage"));Eq($"participant[{p.ActorId}].damage",p.Damage,participantDamage); Eq($"participant[{p.ActorId}].healing",p.Healing,U64(a,"healing"));Eq($"participant[{p.ActorId}].job",p.Job,checked((ushort)U32(a,"jobId"))); string name=Str(a,"name"); if(!string.Equals(p.Name,name,StringComparison.Ordinal))diffs.Add(new($"participant[{p.ActorId}].name",p.Name,name,"actual.participants vs readable.participants.csv"));}
        foreach(uint id in actualParticipants.Keys.Order())extra.Add(new($"participant[{id}]","<none>",id.ToString(CultureInfo.InvariantCulture),"actual.participants"));
        var participantIds=expected.Participants.Select(x=>x.ActorId).ToHashSet();
        ImmutableArray<ReadableEvent> expectedEvents=[.. expected.Events.Where(x=>x.TargetId==expected.Summary.BossActorId&&x.Damage>0&&participantIds.Contains(x.ActorId))];
        JsonElement[] events=Req(root,"events").EnumerateArray().Where(e=>Bool(e,"isBossTarget")&&U32(e,"targetActorId")==expected.Summary.BossActorId&&U64(e,"damage")>0&&participantIds.Contains(U32(e,"attributedActorId"))).ToArray();
        ulong actualBossDamage=0; foreach(var e in events) actualBossDamage=checked(actualBossDamage+U64(e,"damage")); Eq("totalBossDamage",expected.Summary.TotalDamage,actualBossDamage);
        CompareEventGroups(expectedEvents,events,diffs,missing,extra,matches);
        CompareEventRows(expectedEvents,events,expected.Summary.StartTimestampMs,missing,extra,matches);
        CompareWindows(expected.BuffWindows,Req(root,"buffWindows").EnumerateArray().ToArray(),expected.Summary.StartTimestampMs,diffs,missing,extra,matches);
        CompareUptimes(expected.BuffUptimes,Req(root,"buffUptimes").EnumerateArray().ToArray(),diffs,missing,extra,matches);
        string actualProv=root.TryGetProperty("provenance",out JsonElement pnode)&&pnode.TryGetProperty("captureId",out JsonElement cid)?cid.GetString()??"":"unknown";
        return new(diffs.Count==0&&missing.Count==0&&extra.Count==0,matches.ToImmutable(),["event timestamps: +/- 10 ms source-acquisition tolerance, PCAP capture time remains authoritative","buff boundaries: +/- 25 ms source-acquisition tolerance, PCAP capture time remains authoritative","names: ordinal UTF-8","specialFlags: numeric specialMask is authoritative","damageType and readable buff kind: unavailable on the opposite side"],missing.ToImmutable(),extra.ToImmutable(),diffs.ToImmutable(),actualProv,expected.DirectoryPath);

        void Eq<T>(string field,T exp,T act) {if(EqualityComparer<T>.Default.Equals(exp,act))matches.Add(field);else diffs.Add(new(field,F(exp),F(act),$"actual.{field} vs readable fixture"));}
    }

    private static void CompareEventGroups(ImmutableArray<ReadableEvent> expected,JsonElement[] actual,ImmutableArray<FieldDiscrepancy>.Builder diffs,ImmutableArray<FieldDiscrepancy>.Builder missing,ImmutableArray<FieldDiscrepancy>.Builder extra,ImmutableArray<string>.Builder matches)
    {
        var e=expected.GroupBy(x=>(x.ActorId,x.SkillId,x.IsDot)).ToDictionary(g=>g.Key,g=>new Totals(g.Aggregate(0UL,(s,x)=>checked(s+x.Damage)),g.Aggregate(0UL,(s,x)=>checked(s+x.MultiDamage)),g.Aggregate(0UL,(s,x)=>checked(s+x.Healing)),g.Aggregate(0u,(s,x)=>s|x.SpecialMask),g.Count()));
        var a=actual.GroupBy(x=>(U32(x,"attributedActorId"),U32(x,"skillId"),string.Equals(Str(x,"category"),"Dot",StringComparison.Ordinal))).ToDictionary(g=>g.Key,g=>new Totals(g.Aggregate(0UL,(s,x)=>checked(s+U64(x,"damage"))),g.Aggregate(0UL,(s,x)=>checked(s+U64(x,"multiDamage"))),g.Aggregate(0UL,(s,x)=>checked(s+U64(x,"healing"))),g.Aggregate(0u,(s,x)=>s|U32(x,"specialMask")),g.Count()));
        foreach(var pair in e.OrderBy(x=>x.Key.ActorId).ThenBy(x=>x.Key.SkillId).ThenBy(x=>x.Key.IsDot)){string key=$"event[{pair.Key.ActorId}/{pair.Key.SkillId}/dot={pair.Key.IsDot}]";if(!a.Remove(pair.Key,out Totals? av)){missing.Add(new(key,F(pair.Value),"<missing>","readable.events.csv"));continue;} if(pair.Value==av)matches.Add(key);else diffs.Add(new(key,F(pair.Value),F(av),"aggregated actual.events vs readable.events.csv (damage,multi,healing,masks,rowCount)"));}
        foreach(var pair in a)extra.Add(new($"event[{pair.Key.Item1}/{pair.Key.Item2}/dot={pair.Key.Item3}]","<none>",F(pair.Value),"actual.events"));
    }
    private static void CompareEventRows(ImmutableArray<ReadableEvent> expected,JsonElement[] actual,long startMs,ImmutableArray<FieldDiscrepancy>.Builder missing,ImmutableArray<FieldDiscrepancy>.Builder extra,ImmutableArray<string>.Builder matches)
    {
        var rows=actual.Select(x=>new EventRow(I64(x,"timestampMs")-startMs,new(U32(x,"attributedActorId"),U32(x,"targetActorId"),U32(x,"skillId"),string.Equals(Str(x,"category"),"Dot",StringComparison.Ordinal),U64(x,"damage"),U64(x,"multiDamage"),U32(x,"specialMask")))).GroupBy(x=>x.Key).ToDictionary(g=>g.Key,g=>g.Select(x=>x.Offset).ToList());
        foreach(var value in expected)
        {
            var key=new EventKey(value.ActorId,value.TargetId,value.SkillId,value.IsDot,value.Damage,value.MultiDamage,value.SpecialMask);
            if(!rows.TryGetValue(key,out List<long>? offsets)||!RemoveClosest(offsets,value.OffsetMs,10)) missing.Add(new("eventRow",$"{key} at {value.OffsetMs} ms","<missing>","readable.events.csv vs PCAP capture timestamps"));
            else matches.Add($"eventRow:{key} at {value.OffsetMs} ms (+/-10 ms)");
        }
        foreach(var pair in rows)foreach(long offset in pair.Value)extra.Add(new("eventRow","<none>",$"{pair.Key} at {offset} ms","actual.events PCAP capture timestamps"));
    }
    private static void CompareWindows(ImmutableArray<ReadableBuffWindow> expected,JsonElement[] actual,long encounterStartMs,ImmutableArray<FieldDiscrepancy>.Builder diffs,ImmutableArray<FieldDiscrepancy>.Builder missing,ImmutableArray<FieldDiscrepancy>.Builder extra,ImmutableArray<string>.Builder matches)
    {
        var rows=actual.Select(x=>new WindowRow(I64(x,"startTimestampMs")-encounterStartMs,I64(x,"endTimestampMs")-encounterStartMs,new(U32(x,"ownerId"),U32(x,"targetId"),U32(x,"buffId")))).GroupBy(x=>x.Key).ToDictionary(g=>g.Key,g=>g.Select(x=>(x.Start,x.End)).ToList());
        foreach(var value in expected)
        {
            var key=new WindowKey(value.OwnerId,value.TargetId,value.BuffId);
            if(!rows.TryGetValue(key,out List<(long Start,long End)>? candidates)||!RemoveClosest(candidates,value.StartOffsetMs,value.EndOffsetMs,25)) missing.Add(new("buffWindow",$"{key} [{value.StartOffsetMs},{value.EndOffsetMs}]","<missing>","readable.buff-windows.csv vs PCAP capture timestamps"));
            else matches.Add($"buffWindow:{key} [{value.StartOffsetMs},{value.EndOffsetMs}] (+/-25 ms)");
        }
        foreach(var pair in rows)foreach(var value in pair.Value)extra.Add(new("buffWindow","<none>",$"{pair.Key} [{value.Start},{value.End}]","actual.buffWindows PCAP capture timestamps")); _=diffs;
    }
    private static void CompareUptimes(ImmutableArray<ReadableBuffUptime> expected,JsonElement[] actual,ImmutableArray<FieldDiscrepancy>.Builder diffs,ImmutableArray<FieldDiscrepancy>.Builder missing,ImmutableArray<FieldDiscrepancy>.Builder extra,ImmutableArray<string>.Builder matches)
    {var e=expected.GroupBy(x=>(x.ActorId,x.BuffId)).ToDictionary(g=>g.Key,g=>(g.Aggregate(0UL,(s,x)=>checked(s+x.UptimeMs)),g.Aggregate(0u,(s,x)=>checked(s+x.WindowCount))));var a=actual.GroupBy(x=>(U32(x,"ownerId"),U32(x,"buffId"))).ToDictionary(g=>g.Key,g=>(g.Aggregate(0UL,(s,x)=>checked(s+U64(x,"totalDurationMs"))),g.Aggregate(0u,(s,x)=>checked(s+U32(x,"windowCount")))));foreach(var p in e){string k=$"buffUptime[{p.Key.ActorId}/{p.Key.BuffId}]";if(!a.Remove(p.Key,out var av))missing.Add(new(k,F(p.Value),"<missing>","readable.buff-uptimes.csv"));else if(p.Value==av)matches.Add(k);else diffs.Add(new(k,F(p.Value),F(av),"actual.buffUptimes vs readable.buff-uptimes.csv"));}foreach(var p in a)extra.Add(new($"buffUptime[{p.Key.Item1}/{p.Key.Item2}]","<none>",F(p.Value),"actual.buffUptimes"));}
    private static void CompareMultiset<T>(string field,Dictionary<T,int> e,Dictionary<T,int> a,string provenance,ImmutableArray<FieldDiscrepancy>.Builder missing,ImmutableArray<FieldDiscrepancy>.Builder extra,ImmutableArray<string>.Builder matches) where T:notnull {foreach(var p in e){a.Remove(p.Key,out int n);if(n==p.Value)matches.Add($"{field}:{p.Key}");else{if(n<p.Value)missing.Add(new(field,$"{p.Key} x{p.Value-n}","<missing>",provenance));if(n>p.Value)extra.Add(new(field,"<none>",$"{p.Key} x{n-p.Value}",provenance));}}foreach(var p in a)extra.Add(new(field,"<none>",$"{p.Key} x{p.Value}",provenance));}
    private sealed record Totals(ulong Damage,ulong Multi,ulong Healing,uint Masks,int Rows);
    private sealed record EventKey(uint Actor,uint Target,uint Skill,bool Dot,ulong Damage,ulong Multi,uint Mask);
    private sealed record EventRow(long Offset,EventKey Key);
    private sealed record WindowKey(uint Owner,uint Target,uint Buff);
    private sealed record WindowRow(long Start,long End,WindowKey Key);
    private static bool RemoveClosest(List<long> values,long expected,long tolerance){int best=-1;long distance=long.MaxValue;for(int i=0;i<values.Count;i++){long d=Math.Abs(values[i]-expected);if(d<=tolerance&&d<distance){best=i;distance=d;}}if(best<0)return false;values.RemoveAt(best);return true;}
    private static bool RemoveClosest(List<(long Start,long End)> values,long start,long end,long tolerance){int best=-1;long distance=long.MaxValue;for(int i=0;i<values.Count;i++){long ds=Math.Abs(values[i].Start-start),de=Math.Abs(values[i].End-end);if(ds<=tolerance&&de<=tolerance&&ds+de<distance){best=i;distance=ds+de;}}if(best<0)return false;values.RemoveAt(best);return true;}
    private static Dictionary<TKey,JsonElement> UniqueBy<TKey>(IEnumerable<JsonElement> values,Func<JsonElement,TKey> key,string description) where TKey:notnull{var result=new Dictionary<TKey,JsonElement>();foreach(JsonElement value in values)if(!result.TryAdd(key(value),value))throw new InvalidDataException($"Duplicate {description}.");return result;}
    private static JsonElement Req(JsonElement x,string p)=>x.TryGetProperty(p,out var v)?v:throw new InvalidDataException($"Actual record missing '{p}'.");
    private static uint U32(JsonElement x,string p)=>Req(x,p).TryGetUInt32(out uint v)?v:throw new InvalidDataException($"Actual '{p}' is invalid."); private static ulong U64(JsonElement x,string p)=>Req(x,p).TryGetUInt64(out ulong v)?v:throw new InvalidDataException($"Actual '{p}' is invalid."); private static long I64(JsonElement x,string p)=>Req(x,p).TryGetInt64(out long v)?v:throw new InvalidDataException($"Actual '{p}' is invalid."); private static bool Bool(JsonElement x,string p)=>Req(x,p).ValueKind switch{JsonValueKind.True=>true,JsonValueKind.False=>false,_=>throw new InvalidDataException($"Actual '{p}' is invalid.")}; private static string Str(JsonElement x,string p)=>Req(x,p).GetString()??"";
    private static ulong? NullableU64(JsonElement x,string p){JsonElement value=Req(x,p);return value.ValueKind==JsonValueKind.Null?null:value.TryGetUInt64(out ulong result)?result:throw new InvalidDataException($"Actual '{p}' is invalid.");}
    private static string F<T>(T value)=>Convert.ToString(value,CultureInfo.InvariantCulture)??"";
    private static byte[] ReadActualRecord(string path)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.SequentialScan);
        if(input.Length>MaxActualRecordBytes)throw new InvalidDataException($"Actual encounter JSON exceeds the {MaxActualRecordBytes}-byte bound.");
        using var output=new MemoryStream(checked((int)input.Length));byte[] buffer=new byte[81920];int total=0;
        while(true){int read=input.Read(buffer,0,Math.Min(buffer.Length,MaxActualRecordBytes+1-total));if(read==0)break;total+=read;if(total>MaxActualRecordBytes)throw new InvalidDataException($"Actual encounter JSON exceeds the {MaxActualRecordBytes}-byte bound.");output.Write(buffer,0,read);}
        return output.ToArray();
    }
}
