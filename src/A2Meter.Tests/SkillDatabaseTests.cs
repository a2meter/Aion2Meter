using A2Meter.Api;
using A2Meter.Dps.Protocol;
using Xunit;

namespace A2Meter.Tests;

public sealed class SkillDatabaseTests
{
    [Fact]
    public void DungeonBossRowsPromoteMobsToBosses()
    {
        var db = new SkillDatabase(CreateProblemDungeonSnapshot());

        Assert.True(db.IsMobBoss(2301209));
        Assert.True(db.IsMobBoss(2301207));
        Assert.True(db.IsMobBoss(2301208));
        Assert.True(db.IsMobBoss(2301055));
        Assert.True(db.IsMobBoss(2301067));
        Assert.True(db.IsMobBoss(2301089));
        Assert.Equal("포식하는 뉴트라", db.GetMobName(2301209));
        Assert.Equal("염화의 수호검", db.GetMobName(2301055));
        Assert.Equal("이스카리엘", db.GetMobName(2301089));
    }

    [Fact]
    public void DungeonNamesUseExplicitDisplayNames()
    {
        var db = new SkillDatabase(CreateProblemDungeonSnapshot());

        Assert.Equal("침식의 정화소", db.GetDungeonName(620011));
        Assert.Equal("무스펠의 성배(어려움)", db.GetDungeonName(620021));
        Assert.Equal("무스펠의 성배(보통)", db.GetDungeonName(620022));
        Assert.Equal("테스트 던전(어려움)", db.GetDungeonName(620099));
    }

    private static GameDataSnapshot CreateProblemDungeonSnapshot() => new()
    {
        Dungeons =
        {
            new GameDungeonRow { Id = 620011, Name = "ErosionPurifier_01", BaseName = "ErosionPurifier", Tier = "기본" },
            new GameDungeonRow { Id = 620021, Name = "무스펠의 성배(어려움)", BaseName = "무스펠의 성배", Tier = "어려움" },
            new GameDungeonRow { Id = 620022, Name = "무스펠의 성배(보통)", BaseName = "무스펠의 성배", Tier = "보통" },
            new GameDungeonRow { Id = 620099, Name = "테스트 던전", BaseName = "테스트 던전", Tier = "어려움" },
        },
        Mobs =
        {
            new GameMobRow { Id = 2301209, Name = "포식하는 뉴트라", IsBoss = 0 },
            new GameMobRow { Id = 2301207, Name = "검은 피 블라트", IsBoss = 0 },
            new GameMobRow { Id = 2090720, Name = "중합체 바고트", IsBoss = 1 },
            new GameMobRow { Id = 2301059, Name = "이스카리엘", IsBoss = 1 },
            new GameMobRow { Id = 2301060, Name = "칼드릭스", IsBoss = 1 },
        },
        DungeonBosses =
        {
            new GameDungeonBossRow { DungeonId = 620011, Ord = 1, BossName = "포식하는 뉴트라", MobId = 2301209 },
            new GameDungeonBossRow { DungeonId = 620011, Ord = 2, BossName = "검은 피 블라트", MobId = 2301207 },
            new GameDungeonBossRow { DungeonId = 620011, Ord = 3, BossName = "중합체 바고트", MobId = 2301208 },
            new GameDungeonBossRow { DungeonId = 620011, Ord = 4, BossName = "중합체 바고트", MobId = 2090720 },
            new GameDungeonBossRow { DungeonId = 620021, Ord = 1, BossName = "염화의 수호검", MobId = 2301055 },
            new GameDungeonBossRow { DungeonId = 620021, Ord = 2, BossName = "이스카리엘", MobId = 2301059 },
            new GameDungeonBossRow { DungeonId = 620021, Ord = 3, BossName = "칼드릭스", MobId = 2301060 },
            new GameDungeonBossRow { DungeonId = 620021, Ord = 4, BossName = "염화의 수호검", MobId = 2301067 },
            new GameDungeonBossRow { DungeonId = 620022, Ord = 1, BossName = "염화의 수호검", MobId = 2301055 },
            new GameDungeonBossRow { DungeonId = 620022, Ord = 2, BossName = "이스카리엘", MobId = 2301089 },
            new GameDungeonBossRow { DungeonId = 620022, Ord = 3, BossName = "칼드릭스", MobId = 2301090 },
            new GameDungeonBossRow { DungeonId = 620022, Ord = 4, BossName = "염화의 수호검", MobId = 2301067 },
        },
    };
}
