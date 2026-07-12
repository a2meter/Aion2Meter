using System.Text;
using Namter.Cli.Comparison;

namespace Namter.Tests.Integration.Golden;

public sealed class GoldenComparisonTests
{
    [Fact]
    public void Strict_loader_reads_generated_fixture_and_rejects_duplicate_rows()
    {
        string root = CreateFixture();
        try
        {
            ReadableFixture fixture = ReadableFixtureLoader.Load(root);
            Assert.Equal(18804u, fixture.Summary.BossActorId);
            Assert.Single(fixture.Participants);
            Assert.Equal(2, fixture.Events.Length);
            File.AppendAllText(Path.Combine(root, "participants.csv"), "\"1\",\"886\",\"dup\",\"x\",\"13\",\"1\",\"1\",\"1\",\"0\",\"0\",\"0\",\"0\",\"0\"\n", Encoding.UTF8);
            Assert.Throws<InvalidDataException>(() => ReadableFixtureLoader.Load(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Comparator_reports_every_difference_and_does_not_equate_event_count_to_damage_rows()
    {
        string root = CreateFixture();
        string actual = Path.Combine(root, "actual.json");
        try
        {
            File.WriteAllText(actual, """
              {"startTimestampMs":1000,"endTimestampMs":2000,"encounter":{"contentId":600153,"dungeonId":0,"bossActorId":18804,"bossCode":2301721,"name":"Wrong","maxHp":null},"participants":[{"actorId":886,"name":"A","jobId":13,"damage":99,"multiDamage":0,"dotDamage":0,"healing":0}],"events":[{"timestampMs":1000,"sourceActorId":886,"attributedActorId":886,"targetActorId":18804,"isBossTarget":true,"skillId":10,"damage":99,"multiDamage":0,"healing":0,"specialMask":1,"damageType":0,"category":"Damage"}],"buffWindows":[],"buffUptimes":[],"provenance":{"captureId":"tiny"}}
              """);
            ComparisonReport report = GoldenComparator.Compare(actual, ReadableFixtureLoader.Load(root));
            Assert.False(report.IsMatch);
            Assert.Contains(report.Discrepancies, x => x.Field == "totalBossDamage");
            Assert.Contains(report.Discrepancies, x => x.Field == "bossName");
            Assert.Contains(report.Discrepancies, x => x.Field == "bossMaxHp");
            Assert.DoesNotContain(report.Discrepancies, x => x.Field == "eventCount");
            Assert.All(report.Discrepancies, x => Assert.False(string.IsNullOrWhiteSpace(x.Provenance)));
            string reportPath=Path.Combine(root,"report.json");int exit=await Namter.Cli.CliApplication.RunAsync(["compare","--actual",actual,"--expected",root,"--report",reportPath],TextWriter.Null,TextWriter.Null);Assert.Equal((int)Namter.Cli.CliExitCode.ComparisonMismatch,exit);Assert.True(File.Exists(reportPath));Assert.Empty(Directory.GetFiles(root,"*.tmp",SearchOption.AllDirectories));
            ComparisonReport reducerOnly=GoldenComparator.Compare(actual,ReadableFixtureLoader.Load(root) with{Evidence=FixtureEvidence.ReducerOnly});Assert.Equal("ReducerOnly",reducerOnly.FixtureEvidence);Assert.Contains("\"fixtureEvidence\":\"ReducerOnly\"",System.Text.Encoding.UTF8.GetString(reducerOnly.WriteStableJson()),StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Malformed_actual_json_is_invalid_data_exit_not_internal_error()
    {
        string root=CreateFixture();try{string actual=Path.Combine(root,"bad.json"),report=Path.Combine(root,"report.json");await File.WriteAllTextAsync(actual,"{\"encounter\":42}");int exit=await Namter.Cli.CliApplication.RunAsync(["compare","--actual",actual,"--expected",root,"--report",report],TextWriter.Null,TextWriter.Null);Assert.Equal((int)Namter.Cli.CliExitCode.InvalidData,exit);Assert.False(File.Exists(report));}finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void Strict_loader_rejects_invalid_utf8_duplicate_summary_and_summary_bounds()
    {
        string root=CreateFixture();try{string summary=Path.Combine(root,"summary.txt");File.AppendAllText(summary,"\n  BossActorId: 18804\n");Assert.Throws<InvalidDataException>(()=>ReadableFixtureLoader.Load(root));File.WriteAllBytes(summary,[0xff,0xfe,0xfd]);Assert.Throws<InvalidDataException>(()=>ReadableFixtureLoader.Load(root));File.WriteAllBytes(summary,new byte[262_145]);Assert.Throws<InvalidDataException>(()=>ReadableFixtureLoader.Load(root));}finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void Supplied_readable_fixtures_have_certified_headers_and_basilus_is_reducer_only()
    {
        string captures = FindCaptures();
        if (!Directory.Exists(captures)) return;
        string[] dirs = Directory.GetDirectories(captures, "*_readable", SearchOption.TopDirectoryOnly);
        Assert.True(dirs.Length >= 3);
        foreach (string dir in dirs)
        {
            ReadableFixture fixture = ReadableFixtureLoader.Load(dir);
            if (Path.GetFileName(dir).Contains("바실루스", StringComparison.Ordinal))
                Assert.Equal(FixtureEvidence.ReducerOnly, fixture.Evidence);
        }
    }

    private static string CreateFixture()
    {
        string root = Path.Combine(Path.GetTempPath(), "namter-readable-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "summary.txt"), """
            Encounter
              Boss: Tiny
              BossActorId: 18804
              BossMobCode: 2301721
              BossMaxHp: 100
              ContentCode: 600153
              StartUtc: 1970-01-01T00:00:01.0000000Z
              EndUtc: 1970-01-01T00:00:02.0000000Z
              TotalDamage: 30
              ParticipantCount: 1
              DamageEventRows: 2
              BuffWindowRows: 0
              BuffUptimeRows: 0
            """);
        File.WriteAllText(Path.Combine(root, "participants.csv"), "\"rank\",\"actorId\",\"name\",\"serverName\",\"job\",\"damage\",\"dps\",\"hits\",\"healing\",\"selfHealing\",\"otherHealing\",\"hps\",\"healHits\"\n\"1\",\"886\",\"A\",\"S\",\"13\",\"30\",\"1\",\"2\",\"0\",\"0\",\"0\",\"0\",\"0\"\n");
        File.WriteAllText(Path.Combine(root, "events.csv"), "\"index\",\"offsetMs\",\"timestampUtc\",\"isDot\",\"actorId\",\"actorName\",\"targetId\",\"targetName\",\"skillId\",\"damage\",\"multiDamage\",\"heal\",\"totalDamage\",\"specialMask\",\"specialFlags\",\"skillLevel\",\"baseSkillLevel\",\"actorIndex\",\"targetIndex\",\"skillIndex\"\n\"0\",\"0\",\"1970-01-01T00:00:01Z\",\"False\",\"886\",\"A\",\"18804\",\"Tiny\",\"10\",\"10\",\"0\",\"0\",\"10\",\"1\",\"CRITICAL\",\"0\",\"0\",\"0\",\"0\",\"0\"\n\"1\",\"1\",\"1970-01-01T00:00:01.001Z\",\"True\",\"886\",\"A\",\"18804\",\"Tiny\",\"11\",\"20\",\"0\",\"0\",\"20\",\"0\",\"\",\"0\",\"0\",\"0\",\"0\",\"1\"\n");
        File.WriteAllText(Path.Combine(root, "buff-windows.csv"), "\"index\",\"startOffsetMs\",\"endOffsetMs\",\"durationMs\",\"startUtc\",\"endUtc\",\"kind\",\"targetId\",\"targetName\",\"ownerId\",\"ownerName\",\"buffId\",\"skillId\",\"skillLevel\",\"baseSkillLevel\"\n");
        File.WriteAllText(Path.Combine(root, "buff-uptimes.csv"), "\"index\",\"actorIndex\",\"actorId\",\"actorName\",\"buffIndex\",\"buffId\",\"skillId\",\"buffName\",\"buffType\",\"uptimeMs\",\"windowCount\",\"uptimePct\"\n");
        return root;
    }

    private static string FindCaptures()
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (d is not null) { string p = Path.Combine(d.FullName, "captures"); if (Directory.Exists(p) && Directory.GetDirectories(p, "*_readable", SearchOption.TopDirectoryOnly).Length != 0) return p; d = d.Parent; }
        return string.Empty;
    }
}
