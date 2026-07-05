using System;

namespace INGMeter.Core;

public sealed record LocalPlayerStateEvent(DateTime TimestampUtc, int Kind, long Value, long MaxValue, long BonusValue, int EntityId, int ServerId, int CharacterNumber, string Context);
