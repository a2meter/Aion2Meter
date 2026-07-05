using System;

namespace INGMeter.Core;

public sealed record MobSpawnObservedEvent(DateTime TimestampUtc, int MobId, int MobCode, int Hp, int RawHp, int Extra1, int Extra2, int StateMarker);
