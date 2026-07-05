using System;

namespace INGMeter.Core;

public sealed record NativePacketInfo(DateTime TimestampUtc, string Kind, string Summary, int PrimaryId = 0, int SecondaryId = 0, int SkillCode = 0, long Value = 0L, string Detail = "");
