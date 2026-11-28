namespace BingoBongo.Games;

/// <summary>Decides when the next automatic draw is due.</summary>
public static class AutoPilotSchedule
{
    public const int MaxDraws = 75;

    /// <summary>After a draw: stay off if off, turn off when the game is over, else reschedule.</summary>
    public static DateTimeOffset? AfterDraw(DateTimeOffset? current, int drawCount, DateTimeOffset now, TimeSpan interval) =>
        current is null || drawCount >= MaxDraws ? null : now + interval;

    /// <summary>When the dealer flips the switch.</summary>
    public static DateTimeOffset? Toggle(bool on, int drawCount, DateTimeOffset now, TimeSpan interval) =>
        !on || drawCount >= MaxDraws ? null : now + interval;

    /// <summary>
    /// Whether a draw aimed at <paramref name="due"/> should happen: a draw without a target
    /// (a manual draw) always does; an auto pilot draw only while its target is still the
    /// scheduled one, so two dealer tabs firing at the same tick draw one ball. Compared at
    /// microseconds, the precision Postgres stores.
    /// </summary>
    public static bool IsDue(DateTimeOffset? scheduled, DateTimeOffset? due) =>
        due is null || (scheduled is not null && Micros(scheduled.Value) == Micros(due.Value));

    private static long Micros(DateTimeOffset at) => at.UtcTicks / TimeSpan.TicksPerMicrosecond;
}
