using INGMeter.Core;

namespace INGMeter.App;

internal readonly record struct RdpsBuffRowKey(RdpsEffectScope EffectScope, int SkillId, int LevelCode, int OwnerId, int TargetId);
