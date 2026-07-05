using System;
using System.Collections.Generic;

namespace INGMeter.Core;

public sealed record RdpsSupportGroup<TWindow>(double Multiplier, double Percent, string SkillName, string ExclusiveGroup, RdpsEffectKind EffectKind, DateTime LatestStart, IReadOnlyList<RdpsSupportSourceShare<TWindow>> Sources) where TWindow : IRdpsSupportWindow;
