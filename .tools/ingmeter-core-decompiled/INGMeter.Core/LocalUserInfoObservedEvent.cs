using System;

namespace INGMeter.Core;

public sealed record LocalUserInfoObservedEvent(DateTime TimestampUtc, int EntityId, string Nickname, int ServerId, int JobCode, int Extra, int CharacterNumber);
