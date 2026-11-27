using BingoBongo.Live;

namespace BingoBongo.Tests;

[Trait("Category", "Unit")]
public class GameEventHubTests
{
    [Fact]
    public void Publish_wakes_only_that_games_subscribers()
    {
        var hub = new GameEventHub();
        var gameA = Guid.NewGuid();
        using var a1 = hub.Subscribe(gameA);
        using var a2 = hub.Subscribe(gameA);
        using var b = hub.Subscribe(Guid.NewGuid());

        hub.Publish(gameA);

        Assert.True(a1.Changes.TryRead(out _));
        Assert.True(a2.Changes.TryRead(out _));
        Assert.False(b.Changes.TryRead(out _));
    }

    [Fact]
    public void Many_publishes_collapse_into_one_pending_change()
    {
        var hub = new GameEventHub();
        var game = Guid.NewGuid();
        using var sub = hub.Subscribe(game);

        for (var i = 0; i < 5; i++)
        {
            hub.Publish(game);
        }

        Assert.True(sub.Changes.TryRead(out _));
        Assert.False(sub.Changes.TryRead(out _));
    }

    [Fact]
    public void Disposed_subscription_no_longer_receives()
    {
        var hub = new GameEventHub();
        var game = Guid.NewGuid();
        var sub = hub.Subscribe(game);
        using var other = hub.Subscribe(game);

        sub.Dispose();
        hub.Publish(game);

        Assert.False(sub.Changes.TryRead(out _));
        Assert.True(other.Changes.TryRead(out _));
    }

    [Fact]
    public void PublishAll_wakes_every_subscriber()
    {
        var hub = new GameEventHub();
        using var a = hub.Subscribe(Guid.NewGuid());
        using var b = hub.Subscribe(Guid.NewGuid());

        hub.PublishAll();

        Assert.True(a.Changes.TryRead(out _));
        Assert.True(b.Changes.TryRead(out _));
    }

    [Fact]
    public void Last_unsubscribe_forgets_the_game()
    {
        var hub = new GameEventHub();
        var game = Guid.NewGuid();
        var first = hub.Subscribe(game);
        var second = hub.Subscribe(game);

        first.Dispose();
        Assert.Equal(1, hub.GameCount);
        second.Dispose();
        Assert.Equal(0, hub.GameCount);
    }

    [Fact]
    public void Publish_with_no_subscribers_is_harmless()
    {
        var hub = new GameEventHub();
        var game = Guid.NewGuid();
        var sub = hub.Subscribe(game);
        sub.Dispose();
        sub.Dispose();

        hub.Publish(game);
        hub.Publish(Guid.NewGuid());
        hub.PublishAll();
    }
}
