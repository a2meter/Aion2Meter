using A2Meter.Dps;
using Xunit;

namespace A2Meter.Tests;

public sealed class PartyTrackerTests
{
    [Fact]
    public void ClearPartyForDungeonEnterPreservesSelfIdentity()
    {
        var tracker = new PartyTracker();
        tracker.Upsert(new PartyMember
        {
            CharacterId = 3377,
            Nickname = "SelfPlayer",
            ServerId = 1001,
            JobCode = 37,
            IsSelf = true,
            IsPartyMember = true,
        });

        tracker.ClearPartyForDungeonEnter();

        Assert.Equal(3377, tracker.SelfEntityId);
        var self = Assert.Single(tracker.SnapshotMembers(), m => m.CharacterId == 3377);
        Assert.True(self.IsSelf);
        Assert.False(self.IsPartyMember);
        Assert.False(self.IsPartyRequest);
    }

    [Fact]
    public void TryGetSelfIdentityReturnsLastKnownSelfWhenSelfEntityChanges()
    {
        var tracker = new PartyTracker();
        tracker.Upsert(new PartyMember
        {
            CharacterId = 3377,
            Nickname = "\uB0A8\uD790",
            ServerId = 1002,
            ServerName = "\uB124\uC790\uCE78",
            JobCode = 29,
            CombatPower = 464812,
            IsSelf = true,
        });

        tracker.Upsert(new PartyMember
        {
            CharacterId = 5390,
            Nickname = "\uB098",
            JobCode = 0,
            IsSelf = true,
        });

        Assert.True(tracker.TryGetSelfIdentity(out var self));
        Assert.Equal(5390u, self.CharacterId);
        Assert.Equal("\uB0A8\uD790", self.Nickname);
        Assert.Equal(1002, self.ServerId);
        Assert.Equal("\uB124\uC790\uCE78", self.ServerName);
        Assert.Equal(29, self.JobCode);
        Assert.Equal(464812, self.CombatPower);
        Assert.DoesNotContain(tracker.SnapshotMembers(), m => m.CharacterId == 3377 && m.IsSelf);
    }
}
