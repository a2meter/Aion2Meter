using System.Collections.Generic;

namespace INGMeter.Core;

public sealed record RdpsSkillCodeParts(int RawSkillId, int BaseSkillId, int TraitDigits, int ChargeStep, IReadOnlyList<int> TraitIndexes);
