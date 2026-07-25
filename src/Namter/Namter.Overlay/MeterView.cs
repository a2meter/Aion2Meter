using System.Collections.Immutable;

namespace Namter.Overlay;

/// Immutable snapshot handed from the capture/consumer thread to the UI thread.
/// Only immutable references cross the thread boundary, so no locking is needed.
internal sealed record MeterView(
    string BossName,
    ulong? BossCurrentHp,
    ulong? BossMaxHp,
    long ElapsedMs,
    bool Live,
    ImmutableArray<MeterRow> Rows)
{
    public static readonly MeterView Empty =
        new("", null, null, 0, false, ImmutableArray<MeterRow>.Empty);

    /// Sum of every row's cumulative damage — the raid total for this encounter.
    public ulong TotalDamage
    {
        get
        {
            ulong sum = 0;
            // Display total only; never throw on the paint path, so wrap rather than check.
            foreach (MeterRow row in Rows) sum = unchecked(sum + row.Damage);
            return sum;
        }
    }
}

/// One participant row. Damage is direct + DoT (multi-hit is tracked separately and
/// never subtracted, per the protocol's damage/multiDamage split).
internal sealed record MeterRow(
    uint ActorId,
    string Name,
    ushort JobId,
    bool IsSelf,
    ulong Damage,
    double DpsPerSec,
    double BossHpShare);
