using A2Meter.Dps;
using Xunit;

namespace A2Meter.Tests;

public sealed class CombatHistoryTests
{
    [Fact]
    public void ShouldReplacePreviousRecordForSameBossTargetWhenDamageIncreases()
    {
        var prev = NewRecord("Boss", targetId: 9000, totalDamage: 100_000);
        var curr = NewRecord("Boss", targetId: 9000, totalDamage: 150_000);

        Assert.True(CombatHistory.ShouldReplacePreviousRecord(prev, curr));
    }

    [Fact]
    public void ShouldNotReplacePreviousRecordForDifferentBossTarget()
    {
        var prev = NewRecord("Boss", targetId: 9000, totalDamage: 100_000);
        var curr = NewRecord("Boss", targetId: 9001, totalDamage: 150_000);

        Assert.False(CombatHistory.ShouldReplacePreviousRecord(prev, curr));
    }

    private static CombatRecord NewRecord(string bossName, int targetId, long totalDamage)
        => new()
        {
            Timestamp = DateTime.Now,
            BossName = bossName,
            TotalDamage = totalDamage,
            Snapshot = new DpsSnapshot
            {
                TotalPartyDamage = totalDamage,
                Target = new MobTarget
                {
                    EntityId = targetId,
                    Name = bossName,
                    IsBoss = true,
                    CurrentHp = 0,
                },
            },
        };
}
