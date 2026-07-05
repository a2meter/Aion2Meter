using System;

namespace INGMeter.Core;

public sealed record ExtendedUserInfoEvent(DateTime TimestampUtc, int EntityId, int Slot, int Mode, uint Value1, int ServerId, string Nickname, int JobCode, int Level, int GearScore, int CombatPower, int Source);
