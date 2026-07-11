using Namter.Core.Interop;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Namter.Tests.Unit.Interop;

public sealed class NativeLoadTests
{
    [Fact]
    public void NativeLibrary_reports_supported_abi() =>
        Assert.Equal(1u, NativeMethods.nm_core_abi_version());

    [Fact]
    public void NativeEventV1_matches_the_frozen_x64_native_layout()
    {
        Assert.Equal(200, Unsafe.SizeOf<NativeEventV1>());
        var offsets = new (string Field, int Offset)[]
        {
            (nameof(NativeEventV1.AbiVersion), 0), (nameof(NativeEventV1.StructSize), 4),
            (nameof(NativeEventV1.Kind), 8), (nameof(NativeEventV1.Reserved), 12),
            (nameof(NativeEventV1.FirstTimestampNs), 16), (nameof(NativeEventV1.LastTimestampNs), 24),
            (nameof(NativeEventV1.Epoch), 32), (nameof(NativeEventV1.FirstFileOffset), 40),
            (nameof(NativeEventV1.LastFileOffset), 48), (nameof(NativeEventV1.SourceAddress), 56),
            (nameof(NativeEventV1.DestinationAddress), 60), (nameof(NativeEventV1.SourcePort), 64),
            (nameof(NativeEventV1.DestinationPort), 66), (nameof(NativeEventV1.ActorId), 68),
            (nameof(NativeEventV1.TargetId), 72), (nameof(NativeEventV1.OwnerId), 76),
            (nameof(NativeEventV1.SkillId), 80), (nameof(NativeEventV1.BuffId), 84),
            (nameof(NativeEventV1.MobId), 88), (nameof(NativeEventV1.BossId), 92),
            (nameof(NativeEventV1.ContentId), 96), (nameof(NativeEventV1.DungeonId), 100),
            (nameof(NativeEventV1.PartyId), 104), (nameof(NativeEventV1.ServerId), 108),
            (nameof(NativeEventV1.JobId), 110), (nameof(NativeEventV1.Damage), 112),
            (nameof(NativeEventV1.MultiDamage), 120), (nameof(NativeEventV1.Healing), 128),
            (nameof(NativeEventV1.CurrentHp), 136), (nameof(NativeEventV1.MaxHp), 144),
            (nameof(NativeEventV1.SpecialMask), 152), (nameof(NativeEventV1.DurationMs), 156),
            (nameof(NativeEventV1.State), 160), (nameof(NativeEventV1.Action), 161),
            (nameof(NativeEventV1.DamageType), 162), (nameof(NativeEventV1.IsDot), 163),
            (nameof(NativeEventV1.IsSelf), 164), (nameof(NativeEventV1.IsBoss), 165),
            (nameof(NativeEventV1.FlagsReserved), 166), (nameof(NativeEventV1.Name), 168),
            (nameof(NativeEventV1.NameSize), 176), (nameof(NativeEventV1.Payload), 184),
            (nameof(NativeEventV1.PayloadSize), 192),
        };
        foreach ((string field, int offset) in offsets)
        {
            Assert.Equal(offset, Marshal.OffsetOf<NativeEventV1>(field).ToInt32());
        }
    }
}
