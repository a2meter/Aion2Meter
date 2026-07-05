using System;

namespace INGMeter.Core;

public sealed record AbyssArtifactStateEvent(DateTime TimestampUtc, int AreaCode, int ArtifactId, int OwnerSide, int OwnerServerId, int MatchServer1Id, int MatchServer2Id);
