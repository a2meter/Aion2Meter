using System.Net;
using System.Reflection;
using A2Meter.Api;
using A2Meter.Dps;
using A2Meter.Dps.Protocol;
using Xunit;

namespace A2Meter.Tests;

public sealed class ProtocolPipelineTests
{
    [Fact]
    public void LowBossHpForUnknownEntityDoesNotRaisePlaceholderBoss()
    {
        var source = new FakePacketSource();
        using var pipeline = new ProtocolPipeline(
            source,
            new SkillDatabase(new GameDataSnapshot()),
            forceManagedParser: true);

        MobTarget? spawned = null;
        source.MobSpawned += mob => spawned = mob;

        InvokeOnBossHp(pipeline, entityId: 23396, currentHp: 37_394);

        Assert.Null(spawned);
    }

    [Fact]
    public void HighBossHpForUnknownEntityRaisesEstimatedBoss()
    {
        var source = new FakePacketSource();
        using var pipeline = new ProtocolPipeline(
            source,
            new SkillDatabase(new GameDataSnapshot()),
            forceManagedParser: true);

        MobTarget? spawned = null;
        source.MobSpawned += mob => spawned = mob;

        InvokeOnBossHp(pipeline, entityId: 23396, currentHp: 113_801_165);

        Assert.NotNull(spawned);
        Assert.Equal(23396, spawned.EntityId);
        Assert.Equal("Boss #23396", spawned.Name);
        Assert.True(spawned.IsBoss);
        Assert.Equal(113_801_165, spawned.CurrentHp);
        Assert.Equal(113_801_165, spawned.MaxHp);
    }

    [Fact]
    public void KnownNonBossMobIsNotPromotedByHighBossHp()
    {
        var source = new FakePacketSource();
        var skills = new SkillDatabase(new GameDataSnapshot
        {
            Mobs =
            {
                new GameMobRow { Id = 2920827, Name = "정예 잡몹", IsBoss = 0 },
            },
        });
        using var pipeline = new ProtocolPipeline(source, skills, forceManagedParser: true);

        MobTarget? spawned = null;
        source.MobSpawned += mob => spawned = mob;

        InvokeOnMobSpawn(pipeline, entityId: 22279, mobCode: 2920827, hp: 37_394, isBoss: 0);
        InvokeOnBossHp(pipeline, entityId: 22279, currentHp: 20_000_000);

        Assert.Null(spawned);
    }

    [Fact]
    public void InvisibleMobIsNotPromotedByHighBossHp()
    {
        var source = new FakePacketSource();
        var skills = new SkillDatabase(new GameDataSnapshot
        {
            Mobs =
            {
                new GameMobRow { Id = 2921251, Name = "N_Invisible_Summon_Idris_01_V01_004", IsBoss = 1 },
            },
        });
        using var pipeline = new ProtocolPipeline(source, skills, forceManagedParser: true);

        MobTarget? spawned = null;
        source.MobSpawned += mob => spawned = mob;

        InvokeOnMobSpawn(pipeline, entityId: 24350, mobCode: 2921251, hp: 354_439_800, isBoss: 1);
        InvokeOnBossHp(pipeline, entityId: 24350, currentHp: 354_439_800);

        Assert.Null(spawned);
    }

    [Fact]
    public void EstimatedBossIsInvalidatedWhenLaterMobSpawnShowsNonBoss()
    {
        var source = new FakePacketSource();
        var skills = new SkillDatabase(new GameDataSnapshot
        {
            Mobs =
            {
                new GameMobRow { Id = 2920827, Name = "정예 잡몹", IsBoss = 0 },
            },
        });
        using var pipeline = new ProtocolPipeline(source, skills, forceManagedParser: true);

        var spawned = new List<MobTarget>();
        source.MobSpawned += mob => spawned.Add(mob);

        InvokeOnBossHp(pipeline, entityId: 22279, currentHp: 20_000_000);
        InvokeOnMobSpawn(pipeline, entityId: 22279, mobCode: 2920827, hp: 37_394, isBoss: 0);

        Assert.Equal(2, spawned.Count);
        Assert.True(spawned[0].IsBoss);
        Assert.False(spawned[1].IsBoss);
        Assert.Equal("정예 잡몹", spawned[1].Name);
    }

    private static void InvokeOnBossHp(ProtocolPipeline pipeline, int entityId, int currentHp)
    {
        var method = typeof(ProtocolPipeline).GetMethod("OnBossHp", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(pipeline, new object[] { entityId, currentHp });
    }

    private static void InvokeOnMobSpawn(ProtocolPipeline pipeline, int entityId, int mobCode, int hp, int isBoss)
    {
        var method = typeof(ProtocolPipeline).GetMethod("OnMobSpawn", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(pipeline, new object[] { entityId, mobCode, hp, isBoss });
    }

    private sealed class FakePacketSource : IPacketSource, IInternalEventRaise
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

        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Dispose() { }

        public void RaiseCombatHit(CombatHitArgs args) => CombatHit?.Invoke(args);
        public void RaiseTargetChanged(MobTarget? target) => TargetChanged?.Invoke(target);
        public void RaiseMobSpawned(MobTarget mob) => MobSpawned?.Invoke(mob);
        public void RaiseEntityRemoved(int entityId) => EntityRemoved?.Invoke(entityId);
        public void RaisePartyMemberSeen(PartyMember member) => PartyMemberSeen?.Invoke(member);
        public void RaisePartyRequestReceived(PartyMember member) => PartyRequestReceived?.Invoke(member);
        public void RaisePartyLeft() => PartyLeft?.Invoke();
        public void RaiseDungeonChanged(int dungeonId) => DungeonChanged?.Invoke(dungeonId);
        public void RaiseBuffEvent(int entityId, int buffId, int type, uint durationMs, long timestamp, int casterId)
            => BuffEvent?.Invoke(entityId, buffId, type, durationMs, timestamp, casterId);
        public void RaiseBuffRefreshEvent(int entityId, int buffId, uint durationMs, long timestamp, int casterId)
            => BuffRefreshEvent?.Invoke(entityId, buffId, durationMs, timestamp, casterId);
        public void RaiseCombatStateChanged(int entityId, int state) => CombatStateChanged?.Invoke(entityId, state);
        public void RaiseRemainHpChanged(int targetId, uint remainHp) => RemainHpChanged?.Invoke(targetId, remainHp);
        public void RaiseNpcGroggyChanged(int targetId, uint maxGroggy, uint currentGroggy, int groggyStatus)
            => NpcGroggyChanged?.Invoke(targetId, maxGroggy, currentGroggy, groggyStatus);
        public void RaiseTargetOn(int targetId, int aggroId, int targetingMode) => TargetOn?.Invoke(targetId, aggroId, targetingMode);
        public void RaiseTargetOff(int targetId, int offMode) => TargetOff?.Invoke(targetId, offMode);
        public void RaiseZoneMoved(uint zoneId) => ZoneMoved?.Invoke(zoneId);

        public void RaiseSegment() => SegmentReceived?.Invoke(new TcpSegment(
            DateTime.UtcNow,
            IPAddress.Loopback,
            1,
            IPAddress.Loopback,
            2,
            0,
            Array.Empty<byte>()));
    }
}
