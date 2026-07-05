using System;

namespace INGMeter.Core;

public sealed record EncounterReplayProgress(int PlayedEvents, int TotalEvents, TimeSpan Position, TimeSpan Duration, bool IsComplete);
