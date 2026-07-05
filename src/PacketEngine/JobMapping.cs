using System.Collections.Generic;

namespace PacketEngine;

/// Game job-code to Korean job-name mapping.
/// Matches A2Meter.Dps.JobMapping.GameToName.
internal static class JobMapping
{
    private static readonly Dictionary<int, string> GameToName = new()
    {
        [5]  = "검성",  [6]  = "검성",  [7]  = "검성",  [8]  = "검성",
        [9]  = "수호성", [10] = "수호성", [11] = "수호성", [12] = "수호성",
        [13] = "궁성",  [14] = "궁성",  [15] = "궁성",  [16] = "궁성",
        [17] = "살성",  [18] = "살성",  [19] = "살성",  [20] = "살성",
        [21] = "정령성", [22] = "정령성", [23] = "정령성", [24] = "정령성",
        [25] = "마도성", [26] = "마도성", [27] = "마도성", [28] = "마도성",
        [29] = "치유성", [30] = "치유성", [31] = "치유성", [32] = "치유성",
        [33] = "호법성", [34] = "호법성", [35] = "호법성", [36] = "호법성",
        [37] = "권성",  [38] = "권성",  [39] = "권성",  [40] = "권성",
    };

    public static string GetName(int jobCode)
        => GameToName.TryGetValue(jobCode, out var name) ? name : "직업불명";
}
