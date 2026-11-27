using System.Text.Json;
using BingoBongo.Games;

namespace BingoBongo.Live;

public enum SsePresence { Connected, Heartbeat, Disconnected }

/// <summary>
/// Streams game snapshots as Server-Sent Events: one on connect, one after every hub change,
/// and a comment heartbeat in between. Runs until the client disconnects.
/// </summary>
public static class SseStream
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(
        HttpContext ctx,
        Guid gameId,
        Func<CancellationToken, Task<GameSnapshot?>> load,
        Func<SsePresence, CancellationToken, Task>? presence,
        GameEventHub hub,
        TimeSpan heartbeat)
    {
        var ct = ctx.RequestAborted;
        var response = ctx.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            if (presence != null)
            {
                await presence(SsePresence.Connected, ct);
            }

            // Subscribe before the first load so a change in between still triggers a reload.
            using var subscription = hub.Subscribe(gameId);
            await response.StartAsync(ct);
            await WriteSnapshotAsync(response, load, ct);

            using var timer = new PeriodicTimer(heartbeat);
            Task<bool>? tick = null;
            Task<bool>? change = null;
            while (true)
            {
                tick ??= timer.WaitForNextTickAsync(ct).AsTask();
                change ??= subscription.Changes.WaitToReadAsync(ct).AsTask();
                var done = await Task.WhenAny(tick, change);

                if (done == change)
                {
                    change = null;
                    if (!await done)
                    {
                        return;
                    }
                    subscription.Changes.TryRead(out _);
                    await WriteSnapshotAsync(response, load, ct);
                }
                else
                {
                    tick = null;
                    await done;
                    await response.WriteAsync(": ping\n\n", ct);
                    await response.Body.FlushAsync(ct);
                    if (presence != null)
                    {
                        await presence(SsePresence.Heartbeat, ct);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected.
        }
        finally
        {
            if (presence != null)
            {
                await presence(SsePresence.Disconnected, CancellationToken.None);
            }
        }
    }

    private static async Task WriteSnapshotAsync(
        HttpResponse response, Func<CancellationToken, Task<GameSnapshot?>> load, CancellationToken ct)
    {
        var snapshot = await load(ct);
        if (snapshot == null)
        {
            return;
        }
        var json = JsonSerializer.Serialize(snapshot, Json);
        await response.WriteAsync($"event: state\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
