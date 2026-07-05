using System.Collections.Generic;

namespace A2Meter.Dps;

/// Aion 2 job-code mappings. The packet protocol carries fine-grained codes,
/// four per archetype; the UI groups them by Korean job name.
internal static class JobMapping
{
    public static readonly Dictionary<int, int> GameToUi = new()
    {
        [5]  = 0, [6]  = 0, [7]  = 0, [8]  = 0,   // 검성
        [9]  = 4, [10] = 4, [11] = 4, [12] = 4,   // 수호성
        [13] = 1, [14] = 1, [15] = 1, [16] = 1,   // 궁성
        [17] = 3, [18] = 3, [19] = 3, [20] = 3,   // 살성
        [21] = 5, [22] = 5, [23] = 5, [24] = 5,   // 정령성
        [25] = 2, [26] = 2, [27] = 2, [28] = 2,   // 마도성
        [29] = 6, [30] = 6, [31] = 6, [32] = 6,   // 치유성
        [33] = 7, [34] = 7, [35] = 7, [36] = 7,   // 호법성
        [37] = 8, [38] = 8, [39] = 8, [40] = 8,   // 권성
    };

    public static readonly Dictionary<int, string> UiToName = new()
    {
        [0] = "검성",  [1] = "궁성",  [2] = "마도성", [3] = "살성",
        [4] = "수호성", [5] = "정령성", [6] = "치유성", [7] = "호법성",
        [8] = "권성",
    };

    public static readonly Dictionary<int, string> GameToName = new()
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

    public static int GameToUiIndex(int gameCode) =>
        GameToUi.TryGetValue(gameCode, out var ui) ? ui : -1;

    public static string GameToJobName(int gameCode) =>
        GameToName.TryGetValue(gameCode, out var n) ? n : "";

    public static string UiToJobName(int uiIndex) =>
        UiToName.TryGetValue(uiIndex, out var n) ? n : "";
}
