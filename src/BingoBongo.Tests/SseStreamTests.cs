using System.Text;
using BingoBongo.Games;
using BingoBongo.Live;
using Microsoft.AspNetCore.Http;

namespace BingoBongo.Tests;

[Trait("Category", "Unit")]
public class SseStreamTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static GameSnapshot Snapshot(int drew) => new(
        1, drew, null, new Dictionary<int, string>(), new AutoPilotState(false, null),
        DateTimeOffset.UnixEpoch, 0, null);

    private static (DefaultHttpContext ctx, MemoryStream body, CancellationTokenSource abort) NewContext()
    {
        var body = new MemoryStream();
        var abort = new CancellationTokenSource();
        var ctx = new DefaultHttpContext { RequestAborted = abort.Token };
        ctx.Response.Body = body;
        return (ctx, body, abort);
    }

    [Fact]
    public async Task Reports_presence_in_order_and_streams_until_disconnect()
    {
        var (ctx, body, abort) = NewContext();
        var hub = new GameEventHub();
        var game = Guid.NewGuid();
        var loads = 0;
        var heartbeat = new TaskCompletionSource();
        // The loop is sequential, so a heartbeat after the second load means its frame was written.
        var heartbeatAfterReload = new TaskCompletionSource();
        var events = new List<SsePresence>();

        var run = SseStream.RunAsync(ctx, game,
            _ => Task.FromResult<GameSnapshot?>(Snapshot(Interlocked.Increment(ref loads) - 1)),
            (p, _) =>
            {
                lock (events) events.Add(p);
                if (p == SsePresence.Heartbeat) heartbeat.TrySetResult();
                if (p == SsePresence.Heartbeat && Volatile.Read(ref loads) >= 2) heartbeatAfterReload.TrySetResult();
                return Task.CompletedTask;
            },
            hub, TimeSpan.FromMilliseconds(50));

        await heartbeat.Task.WaitAsync(Timeout);
        hub.Publish(game);
        await heartbeatAfterReload.Task.WaitAsync(Timeout);
        abort.Cancel();
        await run.WaitAsync(Timeout);

        // One load on connect and exactly one per change: the pending change is consumed.
        Assert.Equal(2, loads);
        Assert.Equal(SsePresence.Connected, events[0]);
        Assert.Equal(SsePresence.Disconnected, events[^1]);
        Assert.All(events[1..^1], p => Assert.Equal(SsePresence.Heartbeat, p));
        Assert.Equal("text/event-stream", ctx.Response.Headers.ContentType);
        Assert.Equal("no-cache", ctx.Response.Headers.CacheControl);
        var text = Encoding.UTF8.GetString(body.ToArray());
        Assert.StartsWith("event: state\ndata: {\"round\":1,\"numberOfDrew\":0,", text);
        Assert.Contains("event: state\ndata: {\"round\":1,\"numberOfDrew\":1,", text);
        Assert.Contains(": ping\n\n", text);
    }

    [Fact]
    public async Task Missing_snapshot_writes_no_frame()
    {
        var (ctx, body, abort) = NewContext();
        var loaded = new TaskCompletionSource();

        var run = SseStream.RunAsync(ctx, Guid.NewGuid(),
            _ => { loaded.TrySetResult(); return Task.FromResult<GameSnapshot?>(null); },
            null, new GameEventHub(), TimeSpan.FromHours(1));

        await loaded.Task.WaitAsync(Timeout);
        abort.Cancel();
        await run.WaitAsync(Timeout);

        Assert.Empty(body.ToArray());
    }

    [Fact]
    public async Task Load_failure_propagates_and_still_reports_disconnect()
    {
        var (ctx, _, _) = NewContext();
        var events = new List<SsePresence>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => SseStream.RunAsync(ctx, Guid.NewGuid(),
            _ => throw new InvalidOperationException("db down"),
            (p, _) => { events.Add(p); return Task.CompletedTask; },
            new GameEventHub(), TimeSpan.FromHours(1)));

        Assert.Equal([SsePresence.Connected, SsePresence.Disconnected], events);
    }
}
