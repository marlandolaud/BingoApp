namespace BingoBongo.Games;

public static class ViewerPresence
{
    /// <summary>A viewer counts as watching if seen after this: within the last two heartbeats.</summary>
    public static DateTimeOffset WatchingSince(DateTimeOffset now, int heartbeatSeconds) =>
        now - TimeSpan.FromSeconds(2 * heartbeatSeconds);
}
