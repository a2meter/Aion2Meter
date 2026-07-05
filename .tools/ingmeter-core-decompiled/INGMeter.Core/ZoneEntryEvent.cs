using System;

namespace INGMeter.Core;

public sealed record ZoneEntryEvent(DateTime TimestampUtc, int ContentCode, int Kind);
