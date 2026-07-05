using System;
using System.Collections.Generic;

namespace INGMeter.Core;

public sealed record CombatSnapshot(DateTime SessionStartUtc, DateTime LastEventUtc, TimeSpan SessionDuration, IReadOnlyList<ActorStats> Actors, int TopTargetId, string TopTargetName, long TopTargetDamage, int TopTargetHits, TimeSpan TopTargetDuration, bool IsBossActive, bool IsBossConfirmed, int TopTargetMaxHp, int TopTargetCurrentHp);
