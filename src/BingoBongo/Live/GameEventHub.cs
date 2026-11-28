using System.Threading.Channels;

namespace BingoBongo.Live;

/// <summary>
/// In-process fan-out of "game changed" signals. Each subscription holds at most one pending
/// change, so a burst of publishes collapses into a single reload by the subscriber.
/// </summary>
public class GameEventHub
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, HashSet<GameSubscription>> _subscribers = new();

    public GameSubscription Subscribe(Guid gameId)
    {
        var subscription = new GameSubscription(this, gameId);
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(gameId, out var subs))
            {
                _subscribers[gameId] = subs = new HashSet<GameSubscription>();
            }
            subs.Add(subscription);
        }
        return subscription;
    }

    public void Publish(Guid gameId)
    {
        lock (_gate)
        {
            if (_subscribers.TryGetValue(gameId, out var subs))
            {
                foreach (var sub in subs)
                {
                    sub.Signal();
                }
            }
        }
    }

    /// <summary>Wakes every subscriber; used when notifications may have been missed.</summary>
    public void PublishAll()
    {
        lock (_gate)
        {
            foreach (var sub in _subscribers.Values.SelectMany(subs => subs))
            {
                sub.Signal();
            }
        }
    }

    /// <summary>Number of games with at least one subscriber.</summary>
    internal int GameCount
    {
        get { lock (_gate) return _subscribers.Count; }
    }

    internal void Unsubscribe(GameSubscription subscription)
    {
        lock (_gate)
        {
            if (_subscribers.TryGetValue(subscription.GameId, out var subs)
                && subs.Remove(subscription) && subs.Count == 0)
            {
                _subscribers.Remove(subscription.GameId);
            }
        }
    }
}

public sealed class GameSubscription : IDisposable
{
    private readonly GameEventHub _hub;
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });

    internal GameSubscription(GameEventHub hub, Guid gameId)
    {
        _hub = hub;
        GameId = gameId;
    }

    public Guid GameId { get; }

    public ChannelReader<bool> Changes => _channel.Reader;

    internal void Signal() => _channel.Writer.TryWrite(true);

    public void Dispose() => _hub.Unsubscribe(this);
}
