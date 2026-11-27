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
}
