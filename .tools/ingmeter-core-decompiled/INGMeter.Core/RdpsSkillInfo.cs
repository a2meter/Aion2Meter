using System.Collections.Generic;

namespace INGMeter.Core;

public sealed record RdpsSkillInfo(int Id, string Name, int JobId, string JobText, string Category, int MaxLevel, string Icon, IReadOnlyList<RdpsSpecializationInfo> Specializations);
