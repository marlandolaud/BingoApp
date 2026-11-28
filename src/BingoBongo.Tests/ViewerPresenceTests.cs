using BingoBongo.Games;

namespace BingoBongo.Tests;

[Trait("Category", "Unit")]
public class ViewerPresenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(20000);

    [Fact]
    public void Watching_window_is_two_heartbeats() =>
        Assert.Equal(Now.AddSeconds(-30), ViewerPresence.WatchingSince(Now, 15));

    [Fact]
    public void Watching_window_scales_with_the_heartbeat() =>
        Assert.Equal(Now.AddSeconds(-2), ViewerPresence.WatchingSince(Now, 1));
}
