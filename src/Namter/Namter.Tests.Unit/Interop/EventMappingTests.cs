using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Namter.Core.Interop;
using Namter.Encounter;

namespace Namter.Tests.Unit.Interop;

public sealed class EventMappingTests
{
    private static NativeEvent Complete(NativeEventKind kind) => new()
    {
        Kind = kind,
        FirstTimestampNs = 100,
        LastTimestampNs = 101,
        Epoch = 7,
        FirstFileOffset = 200,
        LastFileOffset = 201,
        SourceAddress = 1,
        DestinationAddress = 2,
        SourcePort = 13_328,
        DestinationPort = 55_000,
        ActorId = 11,
        TargetId = 12,
        OwnerId = 13,
        SkillId = 14,
        BuffId = 15,
        MobId = 16,
        BossId = 17,
        ContentId = 18,
        DungeonId = 19,
        PartyId = 20,
        ServerId = 21,
        JobId = 22,
        Damage = 23,
        MultiDamage = 24,
        Healing = 25,
        CurrentHp = 26,
        MaxHp = 27,
        SpecialMask = 0x1234,
        DurationMs = 28,
        State = 29,
        Action = 30,
        DamageType = 31,
        IsDot = true,
        IsSelf = true,
        IsBoss = true,
        Name = "Namter",
        Payload = [1, 2, 3],
    };

    [Fact]
    public void MapsDamageAndDotWithoutCollapsingSeparateValues()
    {
        var damage = Assert.IsType<DamageEvent>(CombatEventMapper.Map(Complete(NativeEventKind.Damage)));
        Assert.Equal(11U, damage.ActorId);
        Assert.Equal(12U, damage.TargetId);
        Assert.Equal(14U, damage.SkillId);
        Assert.Equal(23UL, damage.Damage);
        Assert.Equal(24UL, damage.MultiDamage);
        Assert.Equal(25UL, damage.Healing);
        Assert.Equal(0x1234U, damage.SpecialMask);
        Assert.Equal((byte)31, damage.DamageType);
        Assert.True(damage.IsDot);
        Assert.Equal(200UL, damage.Provenance.FirstFileOffset);

        var dot = Assert.IsType<DamageEvent>(CombatEventMapper.Map(Complete(NativeEventKind.Dot)));
        Assert.True(dot.IsDot);
    }

    [Fact]
    public void MapsAllClosedIdentityLifecycleAndContextVariants()
    {
        var buff = Assert.IsType<BuffEvent>(CombatEventMapper.Map(Complete(NativeEventKind.Buff)));
        Assert.Equal((13U, 12U, 15U, 28U, (byte)30),
            (buff.OwnerId, buff.TargetId, buff.BuffId, buff.DurationMs, buff.Action));

        var self = Assert.IsType<ActorObservedEvent>(CombatEventMapper.Map(Complete(NativeEventKind.SelfActor)));
        Assert.Equal("Namter", self.Name); Assert.True(self.IsSelf); Assert.Equal((ushort)21, self.ServerId);
        var other = Assert.IsType<ActorObservedEvent>(CombatEventMapper.Map(Complete(NativeEventKind.OtherActor)));
        Assert.False(other.IsSelf);

        var mob = Assert.IsType<MobSpawnedEvent>(CombatEventMapper.Map(Complete(NativeEventKind.MobSpawn)));
        Assert.Equal((16U, 17U, 26UL, 27UL), (mob.MobId, mob.BossId, mob.CurrentHp, mob.MaxHp));
        Assert.True(mob.IsBoss);
        var boss = Assert.IsType<BossHpEvent>(CombatEventMapper.Map(Complete(NativeEventKind.BossHp)));
        Assert.Equal((11U, 17U, 26UL, 27UL), (boss.ActorId, boss.BossId, boss.CurrentHp, boss.MaxHp));
        Assert.Equal(11U, Assert.IsType<EntityRemovedEvent>(CombatEventMapper.Map(Complete(NativeEventKind.EntityRemoved))).ActorId);

        var party = Assert.IsType<PartyEvent>(CombatEventMapper.Map(Complete(NativeEventKind.Party)));
        Assert.Equal((20U, 11U, 18U, 19U), (party.PartyId, party.ActorId, party.ContentId, party.DungeonId));
        var content = Assert.IsType<ContentEvent>(CombatEventMapper.Map(Complete(NativeEventKind.Content)));
        Assert.Equal((18U, 19U, (byte)29), (content.ContentId, content.DungeonId, content.State));
        var combat = Assert.IsType<CombatStateEvent>(CombatEventMapper.Map(Complete(NativeEventKind.CombatState)));
        Assert.Equal((11U, (byte)29), (combat.ActorId, combat.State));
    }

    [Fact]
    public void UnknownProtocolRetainsAnImmutablePayloadCopy()
    {
        byte[] mutable = [4, 5, 6];
        NativeEvent native = Complete(NativeEventKind.UnknownProtocol) with { Payload = ImmutableArray.Create(mutable) };

        var mapped = Assert.IsType<UnknownProtocolEvent>(CombatEventMapper.Map(native));
        mutable[0] = 99;

        Assert.Equal<byte>([4, 5, 6], mapped.Payload);
    }

    [Fact]
    public void SourceLifecycleIsNotACombatEvent()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatEventMapper.Map(Complete(NativeEventKind.SourceStarted)));
    }

    [Fact]
    public async Task ActualUnmanagedCallbackCopiesTheCompleteEventBeforeNativeBuffersAreReused()
    {
        NativeEvent? captured = null;
        await using var core = new NativeCore(value => captured = value);
        byte[] name = Encoding.UTF8.GetBytes("Namter");
        byte[] payload = [1, 2, 3, 4];
        GCHandle namePin = GCHandle.Alloc(name, GCHandleType.Pinned);
        GCHandle payloadPin = GCHandle.Alloc(payload, GCHandleType.Pinned);
        try
        {
            var native = new NativeEventV1
            {
                AbiVersion = 1, StructSize = (uint)Unsafe.SizeOf<NativeEventV1>(), Kind = NativeEventKind.Damage,
                FirstTimestampNs = 100, LastTimestampNs = 101, Epoch = 7,
                FirstFileOffset = 200, LastFileOffset = 201,
                SourceAddress = 1, DestinationAddress = 2, SourcePort = 13_328, DestinationPort = 55_000,
                ActorId = 11, TargetId = 12, OwnerId = 13, SkillId = 14, BuffId = 15,
                MobId = 16, BossId = 17, ContentId = 18, DungeonId = 19, PartyId = 20,
                ServerId = 21, JobId = 22, Damage = 23, MultiDamage = 24, Healing = 25,
                CurrentHp = 26, MaxHp = 27, SpecialMask = 0x1234, DurationMs = 28,
                State = 29, Action = 30, DamageType = 31, IsDot = 1, IsSelf = 1, IsBoss = 1,
                Name = namePin.AddrOfPinnedObject(), NameSize = (nuint)name.Length,
                Payload = payloadPin.AddrOfPinnedObject(), PayloadSize = (nuint)payload.Length,
            };

            core.InvokeEventCallbackForTesting(ref native);
            name.AsSpan().Fill((byte)'X');
            payload.AsSpan().Fill(0xff);
        }
        finally
        {
            payloadPin.Free();
            namePin.Free();
        }

        NativeEvent value = Assert.IsType<NativeEvent>(captured);
        Assert.Equal((100UL, 101UL, 7UL, 200UL, 201UL),
            (value.FirstTimestampNs, value.LastTimestampNs, value.Epoch, value.FirstFileOffset, value.LastFileOffset));
        Assert.Equal((1U, 2U, (ushort)13_328, (ushort)55_000),
            (value.SourceAddress, value.DestinationAddress, value.SourcePort, value.DestinationPort));
        Assert.Equal((11U, 12U, 13U, 14U, 15U, 16U, 17U, 18U, 19U, 20U),
            (value.ActorId, value.TargetId, value.OwnerId, value.SkillId, value.BuffId,
             value.MobId, value.BossId, value.ContentId, value.DungeonId, value.PartyId));
        Assert.Equal(((ushort)21, (ushort)22), (value.ServerId, value.JobId));
        Assert.Equal((23UL, 24UL, 25UL, 26UL, 27UL),
            (value.Damage, value.MultiDamage, value.Healing, value.CurrentHp, value.MaxHp));
        Assert.Equal((0x1234U, 28U, (byte)29, (byte)30, (byte)31),
            (value.SpecialMask, value.DurationMs, value.State, value.Action, value.DamageType));
        Assert.True(value.IsDot); Assert.True(value.IsSelf); Assert.True(value.IsBoss);
        Assert.Equal("Namter", value.Name);
        Assert.Equal<byte>([1, 2, 3, 4], value.Payload);

        var mapped = Assert.IsType<DamageEvent>(CombatEventMapper.Map(value));
        Assert.Equal((11U, 12U, 14U, 23UL, 24UL, 25UL, 0x1234U, (byte)31, true),
            (mapped.ActorId, mapped.TargetId, mapped.SkillId, mapped.Damage, mapped.MultiDamage,
             mapped.Healing, mapped.SpecialMask, mapped.DamageType, mapped.IsDot));
        Assert.Equal((100UL, 101UL, 7UL, 200UL, 201UL),
            (mapped.Provenance.FirstTimestampNs, mapped.Provenance.LastTimestampNs, mapped.Provenance.Epoch,
             mapped.Provenance.FirstFileOffset, mapped.Provenance.LastFileOffset));
    }
}
