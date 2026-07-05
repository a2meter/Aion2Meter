using System.Net;
using A2Meter.Dps;
using Xunit;

namespace A2Meter.Tests;

public sealed class DpsPipelineTests
{
    [Fact]
    public void UnknownBossActorIsNotInferredAsSelfWhenIdentityPacketsAreMissing()
    {
        var source = new FakePacketSource();
        var meter = new DpsMeter();
        var party = new PartyTracker();
        using var pipeline = new DpsPipeline(source, meter, party)
        {
            SuppressExternalServices = true,
        };

        source.RaisePartyMemberSeen(new PartyMember
        {
            CharacterId = 101,
            Nickname = "PartyMember",
            ServerId = 1001,
            JobCode = 5,
            IsPartyMember = true,
        });
        source.RaiseMobSpawned(new MobTarget
        {
            EntityId = 9000,
            Name = "Boss",
            MaxHp = 100_000,
            CurrentHp = 100_000,
            IsBoss = true,
        });

        source.RaiseCombatHit(new CombatHitArgs(
            ActorId: 5390,
            TargetId: 9000,
            Name: "#5390",
            JobCode: -1,
            Damage: 1234,
            HitFlags: 0,
            IsHeal: false,
            Skill: "Hit",
            ExtraHits: 0,
            IsDot: false,
            Specs: null));

        Assert.Null(party.SelfEntityId);
        Assert.DoesNotContain(party.SnapshotMembers(), m => m.CharacterId == 5390);

        var actor = Assert.Single(meter.BuildTargetSnapshot(9000).Players, p => p.EntityId == 5390);
        Assert.Equal("#5390", actor.Name);
        Assert.Equal(-1, actor.JobCode);
    }

    [Fact]
    public void UnknownBossActorDoesNotReplaceKnownSelfIdentityWhenEntityChanges()
    {
        var source = new FakePacketSource();
        var meter = new DpsMeter();
        var party = new PartyTracker();
        using var pipeline = new DpsPipeline(source, meter, party)
        {
            SuppressExternalServices = true,
        };

        source.RaisePartyMemberSeen(new PartyMember
        {
            CharacterId = 7777,
            Nickname = "\uB0A8\uD790",
            ServerId = 1002,
            ServerName = "\uB124\uC790\uCE78",
            JobCode = 29,
            IsSelf = true,
        });
        source.RaiseMobSpawned(new MobTarget
        {
            EntityId = 9000,
            Name = "Boss",
            MaxHp = 100_000,
            CurrentHp = 100_000,
            IsBoss = true,
        });

        source.RaiseCombatHit(new CombatHitArgs(5390, 9000, "#5390", -1, 1234, 0, false, "Hit", 0, false, null));

        Assert.Equal(7777, party.SelfEntityId);
        Assert.DoesNotContain(party.SnapshotMembers(), m => m.CharacterId == 5390);
        Assert.Contains(party.SnapshotMembers(), m => m.CharacterId == 7777 && m.IsSelf && m.Nickname == "\uB0A8\uD790");

        var actor = Assert.Single(meter.BuildTargetSnapshot(9000).Players, p => p.EntityId == 5390);
        Assert.Equal("#5390", actor.Name);
        Assert.Equal(-1, actor.JobCode);
    }

    [Fact]
    public void UnknownBossActorRemainsUnknownAfterDpsReset()
    {
        var source = new FakePacketSource();
        var meter = new DpsMeter();
        var party = new PartyTracker();
        using var pipeline = new DpsPipeline(source, meter, party)
        {
            SuppressExternalServices = true,
        };

        source.RaiseMobSpawned(new MobTarget
        {
            EntityId = 9000,
            Name = "Boss",
            MaxHp = 100_000,
            CurrentHp = 100_000,
            IsBoss = true,
        });

        source.RaiseCombatHit(new CombatHitArgs(5390, 9000, "#5390", -1, 1234, 0, false, "Hit", 0, false, null));
        pipeline.ResetDpsTab();
        source.RaiseMobSpawned(new MobTarget
        {
            EntityId = 9000,
            Name = "Boss",
            MaxHp = 100_000,
            CurrentHp = 100_000,
            IsBoss = true,
        });
        source.RaiseCombatHit(new CombatHitArgs(5390, 9000, "#5390", -1, 2345, 0, false, "Hit", 0, false, null));

        Assert.Null(party.SelfEntityId);
        Assert.DoesNotContain(party.SnapshotMembers(), m => m.CharacterId == 5390);

        var actor = Assert.Single(meter.BuildTargetSnapshot(9000).Players, p => p.EntityId == 5390);
        Assert.Equal("#5390", actor.Name);
        Assert.Equal(-1, actor.JobCode);
    }

    [Fact]
    public void ZeroHpSampleDoesNotConfirmBossKillUntilIdleOrRemoval()
    {
        var boss = new MobTarget
        {
            EntityId = 9000,
            Name = "Boss",
            MaxHp = 100_000,
            CurrentHp = 0,
            IsBoss = true,
            DeathConfirmed = true,
            LastZeroHpAt = DateTime.UtcNow,
        };
        var now = boss.LastZeroHpAt;

        Assert.False(DpsPipeline.IsBossKillConfirmedForSession(boss, entityRemoved: false, lastHitUtc: now, now: now));
        Assert.False(DpsPipeline.IsBossKillConfirmedForSession(boss, entityRemoved: false, lastHitUtc: now.AddSeconds(2), now: now.AddSeconds(4)));
        Assert.True(DpsPipeline.IsBossKillConfirmedForSession(boss, entityRemoved: false, lastHitUtc: now, now: now.AddSeconds(4)));
        Assert.True(DpsPipeline.IsBossKillConfirmedForSession(boss, entityRemoved: true, lastHitUtc: now.AddSeconds(4), now: now.AddSeconds(4)));
    }

    [Fact]
    public void FinishedBossHpIsReconciledToMeasuredDamageForSavedRecord()
    {
        var snap = new DpsSnapshot
        {
            TotalPartyDamage = 85_113_784,
            Target = new MobTarget
            {
                EntityId = 9000,
                Name = "Boss",
                MaxHp = 229_721_998,
                CurrentHp = 0,
                TotalDamageReceived = 85_135_146,
                IsBoss = true,
                DeathConfirmed = true,
            },
        };

        DpsPipeline.ReconcileFinishedBossHpForRecord(snap);

        Assert.Equal(snap.TotalPartyDamage, snap.Target.MaxHp);
    }

    private sealed class FakePacketSource : IPacketSource
    {
        public bool IsRunning { get; private set; }
        public event Action<TcpSegment>? SegmentReceived;
        public event Action<CombatHitArgs>? CombatHit;
        public event Action<MobTarget?>? TargetChanged;
        public event Action<MobTarget>? MobSpawned;
        public event Action<int>? EntityRemoved;
        public event Action<PartyMember>? PartyMemberSeen;
        public event Action<PartyMember>? PartyRequestReceived;
        public event Action? PartyLeft;
        public event Action<int>? DungeonChanged;
        public event Action<int, int, int, uint, long, int>? BuffEvent;
        public event Action<int, int, uint, long, int>? BuffRefreshEvent;
        public event Action<int, int>? CombatStateChanged;
        public event Action<int, uint>? RemainHpChanged;
        public event Action<int, uint, uint, int>? NpcGroggyChanged;
        public event Action<int, int, int>? TargetOn;
        public event Action<int, int>? TargetOff;
        public event Action<uint>? ZoneMoved;

        public void RaiseCombatHit(CombatHitArgs args) => CombatHit?.Invoke(args);
        public void RaiseMobSpawned(MobTarget target) => MobSpawned?.Invoke(target);
        public void RaisePartyMemberSeen(PartyMember member) => PartyMemberSeen?.Invoke(member);

        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Dispose() { }

        public void RaiseSegment() => SegmentReceived?.Invoke(new TcpSegment(
            DateTime.UtcNow,
            IPAddress.Loopback,
            1,
            IPAddress.Loopback,
            2,
            0,
            Array.Empty<byte>()));

        public void RaiseTargetChanged(MobTarget? target) => TargetChanged?.Invoke(target);
        public void RaiseEntityRemoved(int entityId) => EntityRemoved?.Invoke(entityId);
        public void RaisePartyRequest(PartyMember member) => PartyRequestReceived?.Invoke(member);
        public void RaisePartyLeft() => PartyLeft?.Invoke();
        public void RaiseDungeonChanged(int dungeonId) => DungeonChanged?.Invoke(dungeonId);
        public void RaiseBuff(int entityId, int buffId, int type, uint durationMs, long timestamp, int casterId)
            => BuffEvent?.Invoke(entityId, buffId, type, durationMs, timestamp, casterId);
        public void RaiseBuffRefresh(int entityId, int buffId, uint durationMs, long timestamp, int casterId)
            => BuffRefreshEvent?.Invoke(entityId, buffId, durationMs, timestamp, casterId);
        public void RaiseCombatState(int entityId, int state) => CombatStateChanged?.Invoke(entityId, state);
        public void RaiseRemainHp(int targetId, uint remainHp) => RemainHpChanged?.Invoke(targetId, remainHp);
        public void RaiseNpcGroggy(int targetId, uint maxGroggy, uint currentGroggy, int status)
            => NpcGroggyChanged?.Invoke(targetId, maxGroggy, currentGroggy, status);
        public void RaiseTargetOn(int targetId, int aggroId, int mode) => TargetOn?.Invoke(targetId, aggroId, mode);
        public void RaiseTargetOff(int targetId, int mode) => TargetOff?.Invoke(targetId, mode);
        public void RaiseZoneMoved(uint zoneId) => ZoneMoved?.Invoke(zoneId);
    }
}
